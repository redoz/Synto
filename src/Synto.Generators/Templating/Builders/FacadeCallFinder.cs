using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.Generators.Symbols;
using Synto.Generators.Templating.Emit;
using Synto.Generators.Templating.Diagnostics;
using Synto.Generators.Templating.Lifting;
using Synto.Templating;

namespace Synto.Generators.Templating.Builders;

/// <summary>
/// Discovers <c>[SyntaxBuilder]</c> facade calls in a <c>[Template]</c> body and resolves each to its
/// factory-time builder invocation with per-argument binding (plan Task 3). ALL facades — the <c>Member</c>
/// and <c>Switch</c>/<c>Case</c> builders on <c>SyntoBuilders</c>, the operator builders (<c>TypeOf</c>) on
/// <c>OperatorBuilders</c>, and every user builder — ride the same structural path: recognition is by the
/// invoked simple name matching a registered builder, since the synthesized facade is not present in the
/// compilation the generator analyzes so a genuine facade call is UNBOUND (there is no hardwired,
/// bind-recognized arm). All discovery runs inside the <c>ForAttributeWithMetadataName</c> transform; nothing
/// is captured into pipeline state.
/// <para>
/// The finder is a <see cref="TemplateScopedWalker"/> (Capability 1): a foreign nested child <c>[Template]</c>
/// subtree is SKIPPED wholesale, so the parent carrier's builder pass never re-reports the child's facade errors
/// nor pulls the child's staged spine into the parent's <c>BuilderStagedConsumedNodes</c>.
/// </para>
/// </summary>
internal sealed class FacadeCallFinder : TemplateScopedWalker
{
    private readonly SemanticModel _semanticModel;
    private readonly Dictionary<string, List<IMethodSymbol>> _buildersByName;
    private readonly ValueLift _valueLift;
    private readonly List<BuilderCall> _calls = new();
    private readonly List<DiagnosticInfo> _diagnostics = new();

    private FacadeCallFinder(SemanticModel semanticModel, TemplateScope scope)
        : base(scope)
    {
        _semanticModel = semanticModel;

        var compilation = semanticModel.Compilation;
        var syntaxBuilderAttribute = compilation.GetTypeByMetadataName(typeof(global::Synto.Templating.SyntaxBuilderAttribute).FullName!);

        // The shared value-lift for [Constant] builder arguments, built from the compilation exactly as
        // TemplateFactoryBuilder.InitValueLift does (gathering the [Runtime] converter attribute + ExpressionSyntax
        // symbols). Batteries-included, so an enum lifts with no converter and never hits SY1008.
        var runtimeAttribute = compilation.GetTypeByMetadataName(typeof(RuntimeAttribute).FullName!);
        var expressionSyntaxSymbol = compilation.GetTypeByMetadataName(typeof(ExpressionSyntax).FullName!);
        _valueLift = new ValueLift(semanticModel, runtimeAttribute, expressionSyntaxSymbol);

        var builders = SyntaxBuilderRegistry.FindBuilders(compilation, syntaxBuilderAttribute);

        // Facade identity for a user builder = its method name. Index by name; >1 with the same name is
        // ambiguous (SY1018) once a matching call is found.
        _buildersByName = new Dictionary<string, List<IMethodSymbol>>(System.StringComparer.Ordinal);
        foreach (var b in builders)
        {
            if (!_buildersByName.TryGetValue(b.Name, out var list))
                _buildersByName[b.Name] = list = new List<IMethodSymbol>();
            list.Add(b);
        }
    }

    public static BuilderCallResult FindBuilderCalls(SemanticModel semanticModel, SyntaxNode body, TemplateScope scope)
    {
        var finder = new FacadeCallFinder(semanticModel, scope);
        finder.Visit(body);
        return new BuilderCallResult(finder._calls, finder._diagnostics);
    }

    public override void VisitInvocationExpression(InvocationExpressionSyntax invocation)
    {
        var boundSymbol = _semanticModel.GetSymbolInfo(invocation).Symbol;

        // Facade calls are recognized structurally by the simple name. The synthesized facade is not in
        // the analyzed compilation, so a genuine facade call is UNBOUND. The only legal bound case is the
        // self-facade — a call that binds directly to one of the registered builder methods (e.g. a
        // fully-qualified `MyBuilders.Wrap(...)`). A call that binds to any OTHER method (`settings.Wrap()`
        // on the user's own type) is a real invocation and must be quoted verbatim, never hijacked.
        string? simpleName = GetInvokedSimpleName(invocation);
        if (simpleName is not null && _buildersByName.TryGetValue(simpleName, out var candidates))
        {
            // Recognition rule (#1): reject a successfully-bound, unrelated method — only an unbound call or
            // one binding to the registered builder itself (self-facade) is treated as a facade.
            if (boundSymbol is not null && !IsRegisteredBuilder(boundSymbol, candidates))
            {
                base.VisitInvocationExpression(invocation);
                return;
            }

            // Named arguments are unsupported on facade calls (v1 policy: diagnose, do not reorder).
            if (TryGetNamedArgument(invocation, out var userNamedArg))
            {
                _diagnostics.Add(TemplateDiagnostics.NamedArgumentOnFacade(userNamedArg.GetLocation(), simpleName));
                base.VisitInvocationExpression(invocation);
                return;
            }

            // Explicit type arguments select among heterogeneous same-name overloads. A fresh-TResult facade
            // (an ExpressionSyntax-derived builder such as the expression `Switch<T>`) is callable ONLY with a
            // type argument — TResult is return-only, never inferable — while a non-generic overload (the
            // statement `Switch`) cannot receive one. Narrowing by the call's type-argument arity lets the two
            // coexist, mirroring how the C# compiler picks the real overload at factory-compile time.
            List<IMethodSymbol> resolved = candidates.Count > 1
                ? NarrowByTypeArgArity(candidates, GetTypeArgumentCount(invocation))
                : candidates;

            // Same-name overloads that survive narrowing are allowed as long as they share a binding shape (e.g.
            // the two `Switch` overloads are both `[Quoted governing, Staged list]`): bind with the shared shape
            // and let the C# compiler resolve the real overload from the typed argument. A group whose shapes
            // DIFFER (e.g. staged `Arm(ExpressionSyntax, …)` vs ordinal `Arm([Constant] SyntaxNode, …)`) is
            // resolved by argument type via the bounded resolver (spec 2026-07-11-builder-overload-resolution-
            // design.md §2); SY1018 fires only when it cannot pick a single overload.
            IMethodSymbol chosen;
            if (resolved.Count > 1 && !ShareBindingShape(resolved))
            {
                var picked = ResolveOverloadByArguments(resolved, invocation);
                if (picked is null)
                {
                    _diagnostics.Add(TemplateDiagnostics.AmbiguousBuilder(invocation.GetLocation(), simpleName));
                    base.VisitInvocationExpression(invocation);
                    return;
                }
                chosen = picked;
            }
            else
            {
                chosen = resolved[0];
            }

            var match = FacadeArgumentBinder.Bind(_semanticModel, invocation, chosen, _valueLift, _diagnostics);
            if (match is not null)
                _calls.Add(match);
        }

        base.VisitInvocationExpression(invocation);
    }

    /// <summary>
    /// Resolves same-name builder overloads whose binding shapes DIFFER, by the bounded, argument-directed rule
    /// (spec 2026-07-11-builder-overload-resolution-design.md §2). ONLY staged params discriminate: a staged
    /// param faces a real builder-typed facade parameter, so the argument's resolved type must exist and
    /// implicitly convert to it. A <c>[Quoted]</c>/<c>[Constant]</c> param is object-facing — an island that
    /// accepts any expression — and is NEVER type-checked (its argument is frequently a nested, still-unbound
    /// facade call whose type cannot resolve; type-checking would spuriously reject the candidate — spec §4). A
    /// <c>[TypeArg]</c> param is not a value argument (already narrowed upstream by type-argument arity). Among
    /// applicable candidates the most concretely-typed one wins (fewest object-facing params — staged
    /// <c>Arm(ExpressionSyntax, object)</c> beats ordinal <c>Arm(object, object)</c> when an ExpressionSyntax
    /// argument fits both). Returns null on zero applicable or a tie, so the caller reports SY1018. Runs entirely
    /// inside the transform; captures nothing into pipeline state.
    /// </summary>
    private IMethodSymbol? ResolveOverloadByArguments(List<IMethodSymbol> candidates, InvocationExpressionSyntax invocation)
    {
        var args = invocation.ArgumentList.Arguments;
        var compilation = (CSharpCompilation)_semanticModel.Compilation;

        var applicable = new List<(IMethodSymbol Method, int ObjectFacing)>();
        foreach (var candidate in candidates)
        {
            var shape = FacadeShape.Derive(candidate, out var annotationError, out var returnShapeError);
            if (shape is null || annotationError is not null || returnShapeError is not null)
                continue;

            // Facade value params (skip [TypeArg]) in call order, paired with the builder param that supplies a
            // staged slot's real conversion target. shape.Parameters is 1:1 with builder.Parameters in order.
            var valueParams = new List<(FacadeParam Facade, IParameterSymbol Builder)>();
            for (int i = 0; i < shape.Parameters.Count; i++)
            {
                if (shape.Parameters[i].Kind == BuilderArgKind.QuotedTypeArg)
                    continue;
                valueParams.Add((shape.Parameters[i], candidate.Parameters[i]));
            }

            if (valueParams.Count != args.Count)
                continue;

            bool ok = true;
            int objectFacing = 0;
            for (int i = 0; i < valueParams.Count; i++)
            {
                var (facade, builderParam) = valueParams[i];
                if (facade.Kind == BuilderArgKind.Quoted || facade.Kind == BuilderArgKind.Constant)
                {
                    // Object-facing island: unconditionally applicable, never type-checked (spec §4).
                    objectFacing++;
                    continue;
                }

                // Staged: the argument must have a resolved type implicitly convertible to the builder param type.
                var argType = _semanticModel.GetTypeInfo(args[i].Expression).Type;
                if (argType is null || argType.TypeKind == TypeKind.Error)
                {
                    ok = false;
                    break;
                }

                var conversion = compilation.ClassifyConversion(argType, builderParam.Type);
                if (!conversion.IsImplicit)
                {
                    ok = false;
                    break;
                }
            }

            if (ok)
                applicable.Add((candidate, objectFacing));
        }

        if (applicable.Count == 0)
            return null;

        int min = applicable.Min(a => a.ObjectFacing);
        var best = applicable.Where(a => a.ObjectFacing == min).ToList();
        return best.Count == 1 ? best[0].Method : null;
    }

    /// <summary>
    /// True when every same-name builder candidate derives the SAME binding shape — the sequence of quoted /
    /// staged / type-arg parameter kinds (plus fresh-return + generic-parameter count). Such overloads bind
    /// identically (candidates[0] is representative); the C# compiler resolves the real one from the emitted
    /// fully-qualified call's argument types. Any candidate that fails to derive a facade is treated as a conflict.
    /// </summary>
    /// <summary>
    /// Narrows same-name builder overloads to those callable with the given explicit type-argument count. A
    /// fresh-TResult facade needs exactly one explicit type argument (TResult is return-only, uninferable), so a
    /// call supplying none can never target it; conversely a call supplying some can never target a non-generic
    /// overload. Returns the input unchanged when nothing matches (the arity is ambiguous — let SY1018 decide).
    /// </summary>
    private static List<IMethodSymbol> NarrowByTypeArgArity(List<IMethodSymbol> candidates, int callTypeArgCount)
    {
        var narrowed = new List<IMethodSymbol>();
        foreach (var candidate in candidates)
        {
            var shape = FacadeShape.Derive(candidate, out _, out _);
            if (shape is null)
                continue;

            bool compatible = callTypeArgCount > 0
                ? shape.GenericParameters.Count == callTypeArgCount
                : !shape.FreshReturnTypeParam;

            if (compatible)
                narrowed.Add(candidate);
        }

        return narrowed.Count > 0 ? narrowed : candidates;
    }

    private static int GetTypeArgumentCount(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            GenericNameSyntax g => g.TypeArgumentList.Arguments.Count,
            MemberAccessExpressionSyntax { Name: GenericNameSyntax g } => g.TypeArgumentList.Arguments.Count,
            _ => 0,
        };
    }

    private static bool ShareBindingShape(List<IMethodSymbol> candidates)
    {
        string? key = null;
        foreach (var candidate in candidates)
        {
            var shape = FacadeShape.Derive(candidate, out var annotationError, out var returnShapeError);
            if (shape is null || annotationError is not null || returnShapeError is not null)
                return false;

            var sb = new StringBuilder();
            sb.Append(shape.FreshReturnTypeParam ? '1' : '0').Append('|').Append(shape.GenericParameters.Count);
            foreach (var p in shape.Parameters)
                sb.Append('|').Append((int)p.Kind);

            string candidateKey = sb.ToString();
            if (key is null)
                key = candidateKey;
            else if (!string.Equals(key, candidateKey, System.StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static bool IsRegisteredBuilder(ISymbol boundSymbol, List<IMethodSymbol> candidates)
    {
        var def = (boundSymbol as IMethodSymbol)?.OriginalDefinition ?? boundSymbol;
        foreach (var candidate in candidates)
        {
            if (SymbolEqualityComparer.Default.Equals(candidate, def))
                return true;
        }
        return false;
    }

    private static bool TryGetNamedArgument(InvocationExpressionSyntax invocation, out ArgumentSyntax namedArgument)
    {
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (argument.NameColon is not null)
            {
                namedArgument = argument;
                return true;
            }
        }

        namedArgument = null!;
        return false;
    }

    private static string? GetInvokedSimpleName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            IdentifierNameSyntax id => id.Identifier.ValueText,
            GenericNameSyntax g => g.Identifier.ValueText,
            MemberAccessExpressionSyntax { Name: IdentifierNameSyntax id } => id.Identifier.ValueText,
            MemberAccessExpressionSyntax { Name: GenericNameSyntax g } => g.Identifier.ValueText,
            _ => null,
        };
    }
}

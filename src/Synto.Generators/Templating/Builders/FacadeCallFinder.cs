using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.Generators.Templating.Emit;
using Synto.Generators.Templating.Diagnostics;
using Synto.Generators.Templating.Lifting;
using Synto.Templating;

namespace Synto.Generators.Templating.Builders;

/// <summary>
/// Discovers the <c>Template.TypeOf</c> facade call (recognized by binding — it must stay bind-recognized:
/// its value-position <c>System.Type</c> return can't be paired with a same-signature <c>[SyntaxBuilder]</c>
/// on <c>SyntoBuilders</c> without CS0111, and its type-name argument is a live/staged value, so <c>[TypeArg]</c>
/// can't express it either) and user <c>[SyntaxBuilder]</c> facade calls — including the <c>Member</c> builder,
/// which rides the same structural path as any user builder, shielded by its <c>[Quoted] ExpressionSyntax</c>
/// first parameter so a real author argument never binds to the builder — (recognized structurally, since the
/// synthesized facade is not present in the compilation the generator analyzes) in a <c>[Template]</c> body,
/// and resolves each to its factory-time builder invocation with per-argument binding (plan Task 3). All
/// discovery runs inside the <c>ForAttributeWithMetadataName</c> transform; nothing is captured into pipeline
/// state.
/// <para>
/// The finder is a <see cref="TemplateScopedWalker"/> (Capability 1): a foreign nested child <c>[Template]</c>
/// subtree is SKIPPED wholesale, so the parent carrier's builder pass never re-reports the child's facade errors
/// nor pulls the child's staged spine into the parent's <c>BuilderStagedConsumedNodes</c>.
/// </para>
/// </summary>
internal sealed class FacadeCallFinder : TemplateScopedWalker
{
    private const string BuiltInBuilderType = "global::Synto.Templating.SyntoBuilders";

    private readonly SemanticModel _semanticModel;
    private readonly INamedTypeSymbol? _templateSymbol;
    private readonly Dictionary<string, List<IMethodSymbol>> _buildersByName;
    private readonly ValueLift _valueLift;
    private readonly List<BuilderCall> _calls = new();
    private readonly List<DiagnosticInfo> _diagnostics = new();

    private FacadeCallFinder(SemanticModel semanticModel, TemplateScope scope)
        : base(scope)
    {
        _semanticModel = semanticModel;

        var compilation = semanticModel.Compilation;
        _templateSymbol = compilation.GetTypeByMetadataName(typeof(global::Synto.Templating.Template).FullName!);
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

        // TypeOf is the only facade left bind-recognized (see class summary for why it can't move to a
        // structural [SyntaxBuilder] like Member did): a Template.TypeOf call binds here directly.
        if (_templateSymbol is not null
            && boundSymbol is IMethodSymbol bound
            && SymbolEqualityComparer.Default.Equals(bound.ContainingType, _templateSymbol)
            && IsBuiltInFacadeName(bound.Name))
        {
            // Named arguments are unsupported on facade calls (v1 policy: diagnose, do not reorder).
            if (TryGetNamedArgument(invocation, out var builtInNamedArg))
            {
                _diagnostics.Add(TemplateDiagnostics.NamedArgumentOnFacade(builtInNamedArg.GetLocation(), bound.Name));
                base.VisitInvocationExpression(invocation);
                return;
            }
            if (TryBuiltIn(invocation, bound, _calls))
            {
                base.VisitInvocationExpression(invocation);
                return;
            }
        }

        // User facade calls are recognized structurally by the simple name. The synthesized facade is not in
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

            // Same-name overloads are allowed as long as they share a binding shape (e.g. the two `Switch`
            // overloads are both `[Quoted governing, Staged list]`): bind with the shared shape and let the
            // C# compiler resolve the real overload from the typed argument at factory-compile time. SY1018
            // fires only on a genuine shape conflict (differing quoted/staged/type-arg parameter kinds).
            if (candidates.Count > 1 && !ShareBindingShape(candidates))
            {
                _diagnostics.Add(TemplateDiagnostics.AmbiguousBuilder(invocation.GetLocation(), simpleName));
                base.VisitInvocationExpression(invocation);
                return;
            }

            var match = FacadeArgumentBinder.Bind(_semanticModel, invocation, candidates[0], _valueLift, _diagnostics);
            if (match is not null)
                _calls.Add(match);
        }

        base.VisitInvocationExpression(invocation);
    }

    /// <summary>
    /// Resolves a bind-recognized <c>Template.TypeOf</c> call to its <c>SyntoBuilders.TypeOf</c> builder
    /// invocation. <c>TypeOf</c> is the only facade still matched here rather than structurally — its
    /// value-position <c>System.Type</c> return can't share a signature with the <c>ExpressionSyntax</c>-returning
    /// builder (CS0111), and its type-name argument is a live/staged value that <c>[TypeArg]</c> can't express
    /// (see the class summary and <see cref="global::Synto.Templating.SyntoBuilders.TypeOf(string)"/> for the
    /// full rationale).
    /// </summary>
    private static bool TryBuiltIn(InvocationExpressionSyntax invocation, IMethodSymbol bound, List<BuilderCall> calls)
    {
        var args = invocation.ArgumentList.Arguments;
        switch (bound.Name)
        {
            case nameof(global::Synto.Templating.Template.TypeOf) when args.Count == 1:
                calls.Add(new BuilderCall(invocation, BuiltInBuilderType, "TypeOf", new[]
                {
                    new BuilderArgBinding(BuilderArgKind.Staged, "name", args[0].Expression, null),
                }));
                return true;
        }

        return false;
    }

    /// <summary>
    /// True when every same-name builder candidate derives the SAME binding shape — the sequence of quoted /
    /// staged / type-arg parameter kinds (plus fresh-return + generic-parameter count). Such overloads bind
    /// identically (candidates[0] is representative); the C# compiler resolves the real one from the emitted
    /// fully-qualified call's argument types. Any candidate that fails to derive a facade is treated as a conflict.
    /// </summary>
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

    private static bool IsBuiltInFacadeName(string name) =>
        name == nameof(global::Synto.Templating.Template.TypeOf);

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

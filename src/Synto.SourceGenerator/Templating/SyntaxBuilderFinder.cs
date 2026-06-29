using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto;

/// <summary>
/// Discovers the built-in <c>Template.Member</c>/<c>Template.TypeOf</c> facade calls (recognized by binding)
/// and user <c>[SyntaxBuilder]</c> facade calls (recognized structurally, since the synthesized facade is not
/// present in the compilation the generator analyzes) in a <c>[Template]</c> body, and resolves each to its
/// factory-time builder invocation with per-argument binding (plan Task 3). All discovery runs inside the
/// <c>ForAttributeWithMetadataName</c> transform; nothing is captured into pipeline state.
/// </summary>
internal static class SyntaxBuilderFinder
{
    private const string BuiltInBuilderType = "global::Synto.Templating.SyntoBuilders";

    public static BuilderCallResult FindBuilderCalls(SemanticModel semanticModel, SyntaxNode body)
    {
        var compilation = semanticModel.Compilation;
        var templateSymbol = compilation.GetTypeByMetadataName(typeof(global::Synto.Templating.Template).FullName!);
        var syntaxBuilderAttribute = compilation.GetTypeByMetadataName(typeof(global::Synto.Templating.SyntaxBuilderAttribute).FullName!);

        var builders = SyntaxBuilderRegistry.FindBuilders(compilation, syntaxBuilderAttribute);

        // Facade identity for a user builder = its method name. Index by name; >1 with the same name is
        // ambiguous (SY1018) once a matching call is found.
        var buildersByName = new Dictionary<string, List<IMethodSymbol>>(System.StringComparer.Ordinal);
        foreach (var b in builders)
        {
            if (!buildersByName.TryGetValue(b.Name, out var list))
                buildersByName[b.Name] = list = new List<IMethodSymbol>();
            list.Add(b);
        }

        var calls = new List<BuilderCall>();
        var diagnostics = new List<DiagnosticInfo>();

        foreach (var invocation in body.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            // Built-in facade calls bind to Template.Member / Template.TypeOf.
            if (templateSymbol is not null
                && semanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol bound
                && SymbolEqualityComparer.Default.Equals(bound.ContainingType, templateSymbol))
            {
                if (TryBuiltIn(invocation, bound, calls))
                    continue;
            }

            // User facade calls are recognized structurally by the simple name (the synthesized facade is not
            // in the analyzed compilation, so they do not bind).
            string? simpleName = GetInvokedSimpleName(invocation);
            if (simpleName is not null && buildersByName.TryGetValue(simpleName, out var candidates))
            {
                if (candidates.Count > 1)
                {
                    diagnostics.Add(TemplateDiagnostics.AmbiguousBuilder(invocation.GetLocation(), simpleName));
                    continue;
                }

                var match = TryUser(semanticModel, invocation, candidates[0], diagnostics);
                if (match is not null)
                    calls.Add(match);
            }
        }

        return new BuilderCallResult(calls, diagnostics);
    }

    private static bool TryBuiltIn(InvocationExpressionSyntax invocation, IMethodSymbol bound, List<BuilderCall> calls)
    {
        var args = invocation.ArgumentList.Arguments;
        switch (bound.Name)
        {
            case nameof(global::Synto.Templating.Template.Member) when args.Count == 2:
                calls.Add(new BuilderCall(invocation, BuiltInBuilderType, "Member", new[]
                {
                    new BuilderArgBinding(BuilderArgKind.Quoted, "instance", args[0].Expression, null),
                    new BuilderArgBinding(BuilderArgKind.Staged, "name", args[1].Expression, null),
                }));
                return true;

            case nameof(global::Synto.Templating.Template.TypeOf) when args.Count == 1:
                calls.Add(new BuilderCall(invocation, BuiltInBuilderType, "TypeOf", new[]
                {
                    new BuilderArgBinding(BuilderArgKind.Staged, "name", args[0].Expression, null),
                }));
                return true;
        }

        return false;
    }

    private static BuilderCall? TryUser(SemanticModel semanticModel, InvocationExpressionSyntax invocation, IMethodSymbol builder, List<DiagnosticInfo> diagnostics)
    {
        var shape = FacadeShape.Derive(builder, out var annotationError, out var returnShapeError);
        if (returnShapeError is not null)
        {
            diagnostics.Add(TemplateDiagnostics.BuilderBadReturnShape(invocation.GetLocation(), builder.Name, returnShapeError));
            return null;
        }
        if (annotationError is not null)
        {
            diagnostics.Add(TemplateDiagnostics.FacadeSynthesisError(invocation.GetLocation(), builder.Name, annotationError));
            return null;
        }

        // Facade type arguments (e.g. `int` in `Cast<int>(x)`) and value arguments (e.g. `x`).
        var typeArgs = GetTypeArguments(invocation);
        var valueArgs = invocation.ArgumentList.Arguments;

        var bindings = new List<BuilderArgBinding>();
        int typeArgCursor = shape!.FreshReturnTypeParam ? 1 : 0; // skip the leading TResult facade type-arg
        int valueCursor = 0;

        foreach (var p in shape.Parameters)
        {
            switch (p.Kind)
            {
                case BuilderArgKind.QuotedTypeArg:
                    if (typeArgCursor >= typeArgs.Count)
                    {
                        diagnostics.Add(TemplateDiagnostics.BuilderArgBindingMismatch(invocation.GetLocation(), p.ParameterName, "missing type argument"));
                        return null;
                    }
                    bindings.Add(new BuilderArgBinding(BuilderArgKind.QuotedTypeArg, p.ParameterName, null, typeArgs[typeArgCursor]));
                    typeArgCursor++;
                    break;

                default:
                    if (valueCursor >= valueArgs.Count)
                    {
                        diagnostics.Add(TemplateDiagnostics.BuilderArgBindingMismatch(invocation.GetLocation(), p.ParameterName, "missing argument"));
                        return null;
                    }
                    bindings.Add(new BuilderArgBinding(p.Kind, p.ParameterName, valueArgs[valueCursor].Expression, null));
                    valueCursor++;
                    break;
            }
        }

        string builderType = builder.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return new BuilderCall(invocation, builderType, builder.Name, bindings);
    }

    private static SeparatedSyntaxList<TypeSyntax> GetTypeArguments(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            GenericNameSyntax g => g.TypeArgumentList.Arguments,
            MemberAccessExpressionSyntax { Name: GenericNameSyntax g } => g.TypeArgumentList.Arguments,
            _ => default,
        };
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

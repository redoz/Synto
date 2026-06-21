using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.Matching;

namespace Synto;

/// <summary>
/// Resolves the <c>Synto.Matching</c> marker symbols ONCE per pattern (via
/// <see cref="Compilation.GetTypeByMetadataName(string)"/>) and classifies a pattern's parameters by
/// <see cref="SymbolEqualityComparer.Default"/> — never by <c>ToDisplayString()</c> string-matching. A node
/// walk reuses the built instance (<c>ctx.Markers</c>) rather than rebuilding it per node.
/// </summary>
/// <remarks>
/// Task 5 only needs the capture-parameter set (to confirm a zero-capture pattern) and the expression-body
/// shape check. Later tasks extend this with the hole predicates (<c>TryGetCapture</c>,
/// <c>IsExpressionWildcard</c>, <c>TryGetStatementHole</c>, <c>TryGetAnchor</c>).
/// </remarks>
internal sealed class MatchMarkers
{
    private MatchMarkers(IReadOnlyList<IParameterSymbol> captureParameters)
    {
        CaptureParameters = captureParameters;
    }

    /// <summary>The pattern parameters carrying <c>[Capture]</c> or <c>[Capture&lt;TNode&gt;]</c>, in signature order.</summary>
    public IReadOnlyList<IParameterSymbol> CaptureParameters { get; }

    public static MatchMarkers Create(MatchInfo info)
    {
        var compilation = info.SemanticModel.Compilation;

        var captureAttribute = compilation.GetTypeByMetadataName(typeof(CaptureAttribute).FullName!);
        var captureAttributeUnbound = compilation
            .GetTypeByMetadataName(typeof(CaptureAttribute<>).FullName!)?
            .ConstructUnboundGenericType();

        var captures = new List<IParameterSymbol>();
        foreach (var parameter in info.PatternSymbol.Parameters)
        {
            if (IsCapture(parameter, captureAttribute, captureAttributeUnbound))
                captures.Add(parameter);
        }

        return new MatchMarkers(captures);
    }

    /// <summary>
    /// A pattern with an arrow (expression) body — a method or local function lowered as expression-Single.
    /// </summary>
    public static bool IsExpressionBodied(SyntaxNode pattern, out ExpressionSyntax expression)
    {
        var arrow = pattern switch
        {
            MethodDeclarationSyntax method => method.ExpressionBody,
            LocalFunctionStatementSyntax localFunction => localFunction.ExpressionBody,
            _ => null,
        };

        expression = arrow?.Expression!;
        return arrow is not null;
    }

    private static bool IsCapture(
        IParameterSymbol parameter,
        INamedTypeSymbol? captureAttribute,
        INamedTypeSymbol? captureAttributeUnbound)
    {
        foreach (var attribute in parameter.GetAttributes())
        {
            if (attribute.AttributeClass is not { } attributeClass)
                continue;

            if (captureAttribute is not null
                && SymbolEqualityComparer.Default.Equals(attributeClass, captureAttribute))
                return true;

            if (captureAttributeUnbound is not null
                && attributeClass is { IsGenericType: true }
                && SymbolEqualityComparer.Default.Equals(attributeClass.ConstructUnboundGenericType(), captureAttributeUnbound))
                return true;
        }

        return false;
    }
}

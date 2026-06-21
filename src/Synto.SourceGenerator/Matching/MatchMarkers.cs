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
/// Task 5 needed only the capture-parameter set and the expression-body shape check. Task 6 adds the
/// expression-capture hole predicate (<see cref="TryGetCapture"/>) — a bare identifier binding to a
/// non-<c>Stmt</c> <c>[Capture]</c> param, typed <c>ExpressionSyntax</c> or the narrowed <c>[Capture&lt;TNode&gt;]</c>
/// type. Later tasks extend this with the remaining hole predicates (<c>IsExpressionWildcard</c>,
/// <c>TryGetStatementHole</c>, <c>TryGetAnchor</c>).
/// </remarks>
internal sealed class MatchMarkers
{
    private const string StmtMetadataName = "Synto.Matching.Stmt";
    private const string ExprMetadataName = "Synto.Matching.Expr";

    private readonly SemanticModel _semanticModel;
    private readonly Dictionary<ISymbol, CaptureParameter> _expressionCaptures;
    private readonly INamedTypeSymbol? _exprType;

    private MatchMarkers(
        SemanticModel semanticModel,
        IReadOnlyList<IParameterSymbol> captureParameters,
        Dictionary<ISymbol, CaptureParameter> expressionCaptures,
        INamedTypeSymbol? exprType)
    {
        _semanticModel = semanticModel;
        _expressionCaptures = expressionCaptures;
        _exprType = exprType;
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
        var stmtType = compilation.GetTypeByMetadataName(StmtMetadataName);
        var exprType = compilation.GetTypeByMetadataName(ExprMetadataName);

        var captures = new List<IParameterSymbol>();
        var expressionCaptures = new Dictionary<ISymbol, CaptureParameter>(SymbolEqualityComparer.Default);

        foreach (var parameter in info.PatternSymbol.Parameters)
        {
            if (!TryClassifyCapture(parameter, captureAttribute, captureAttributeUnbound, out var narrowType))
                continue;

            captures.Add(parameter);

            // A `[Capture] Stmt` param is a statement-quantifier holder (its member type comes from the verb
            // at the hole site, not the parameter) — it is NOT an expression capture. Everything else is an
            // expression hole, typed ExpressionSyntax or the narrowed [Capture<TNode>] type.
            if (stmtType is not null && SymbolEqualityComparer.Default.Equals(parameter.Type, stmtType))
                continue;

            string memberType = narrowType is null
                ? "ExpressionSyntax"
                : narrowType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            expressionCaptures.Add(
                parameter,
                new CaptureParameter(parameter.Ordinal, parameter.Name, ToMemberName(parameter.Name), memberType));
        }

        return new MatchMarkers(info.SemanticModel, captures, expressionCaptures, exprType);
    }

    /// <summary>
    /// Recognizes the expression wildcard <c>Expr.Any&lt;T&gt;()</c> — an invocation binding (by symbol) to a
    /// method on the <c>Synto.Matching.Expr</c> holder. It matches any expression and captures nothing.
    /// </summary>
    public bool IsExpressionWildcard(SyntaxNode node)
    {
        if (_exprType is null)
            return false;

        return _semanticModel.GetSymbolInfo(node).Symbol is IMethodSymbol method
            && SymbolEqualityComparer.Default.Equals(method.ContainingType, _exprType);
    }

    /// <summary>
    /// Recognizes an expression-capture hole: a node that binds (by symbol) to a non-<c>Stmt</c>
    /// <c>[Capture]</c> parameter. Bound by <see cref="SemanticModel.GetSymbolInfo(SyntaxNode, System.Threading.CancellationToken)"/>
    /// + <see cref="SymbolEqualityComparer.Default"/> — never by node type/shape (the §3.1 leak-free invariant).
    /// </summary>
    public bool TryGetCapture(SyntaxNode node, out CaptureParameter capture)
    {
        capture = null!;
        if (_semanticModel.GetSymbolInfo(node).Symbol is not { } symbol)
            return false;

        return _expressionCaptures.TryGetValue(symbol, out capture!);
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

    private static bool TryClassifyCapture(
        IParameterSymbol parameter,
        INamedTypeSymbol? captureAttribute,
        INamedTypeSymbol? captureAttributeUnbound,
        out ITypeSymbol? narrowType)
    {
        narrowType = null;

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
            {
                // [Capture<TNode>] — the narrowed member/guard type is the single type argument.
                narrowType = attributeClass.TypeArguments.Length == 1 ? attributeClass.TypeArguments[0] : null;
                return true;
            }
        }

        return false;
    }

    private static string ToMemberName(string parameterName) =>
        parameterName.Length == 0
            ? parameterName
            : char.ToUpperInvariant(parameterName[0]) + parameterName.Substring(1);
}

/// <summary>
/// A resolved expression-capture parameter: carries its signature <see cref="Ordinal"/> (record-member order),
/// the parameter name (for the <c>cap_{name}</c> local), the PascalCased record-member name, and the member /
/// guard type (<c>ExpressionSyntax</c>, or the fully-qualified <c>[Capture&lt;TNode&gt;]</c> narrow type).
/// </summary>
internal sealed class CaptureParameter
{
    public CaptureParameter(int ordinal, string parameterName, string memberName, string memberType)
    {
        Ordinal = ordinal;
        ParameterName = parameterName;
        MemberName = memberName;
        MemberType = memberType;
    }

    public int Ordinal { get; }
    public string ParameterName { get; }
    public string MemberName { get; }
    public string MemberType { get; }
}

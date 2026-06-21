using System.Collections.Generic;
using System.Linq;
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
    private const string StatementMetadataName = "Synto.Matching.Statement";
    private const string ExprMetadataName = "Synto.Matching.Expr";
    private const string BlockMetadataName = "Synto.Matching.Block";

    private readonly SemanticModel _semanticModel;
    private readonly Dictionary<ISymbol, CaptureParameter> _expressionCaptures;
    private readonly HashSet<ISymbol> _captureSymbols;
    private readonly INamedTypeSymbol? _exprType;
    private readonly INamedTypeSymbol? _stmtType;
    private readonly INamedTypeSymbol? _statementType;
    private readonly INamedTypeSymbol? _blockType;

    private MatchMarkers(
        SemanticModel semanticModel,
        IReadOnlyList<IParameterSymbol> captureParameters,
        Dictionary<ISymbol, CaptureParameter> expressionCaptures,
        INamedTypeSymbol? exprType,
        INamedTypeSymbol? stmtType,
        INamedTypeSymbol? statementType,
        INamedTypeSymbol? blockType)
    {
        _semanticModel = semanticModel;
        _expressionCaptures = expressionCaptures;
        _captureSymbols = new HashSet<ISymbol>(captureParameters, SymbolEqualityComparer.Default);
        _exprType = exprType;
        _stmtType = stmtType;
        _statementType = statementType;
        _blockType = blockType;
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
        var statementType = compilation.GetTypeByMetadataName(StatementMetadataName);
        var exprType = compilation.GetTypeByMetadataName(ExprMetadataName);
        var blockType = compilation.GetTypeByMetadataName(BlockMetadataName);

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

        return new MatchMarkers(info.SemanticModel, captures, expressionCaptures, exprType, stmtType, statementType, blockType);
    }

    /// <summary>
    /// Recognizes a <c>Block.Start()</c> / <c>Block.End()</c> anchor: an <see cref="ExpressionStatementSyntax"/>
    /// wrapping an invocation on the <c>Synto.Matching.Block</c> holder (by resolved symbol). <paramref name="isStart"/>
    /// distinguishes <c>Start</c> from <c>End</c>.
    /// </summary>
    public bool TryGetAnchor(StatementSyntax statement, out bool isStart)
    {
        isStart = false;

        if (statement is not ExpressionStatementSyntax { Expression: InvocationExpressionSyntax invocation })
            return false;

        if (_semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
            return false;

        if (_blockType is null || !SymbolEqualityComparer.Default.Equals(method.ContainingType, _blockType))
            return false;

        isStart = method.Name == "Start";
        return true;
    }

    /// <summary>
    /// Classifies a statement hole: an <see cref="ExpressionStatementSyntax"/> wrapping a quantifier invocation
    /// on a <c>[Capture] Stmt</c> param (a capture) or the static <c>Statement</c> holder (a wildcard). The verb
    /// (<c>One</c>/<c>Opt</c>/<c>Some</c>/<c>All</c>/<c>Exactly</c>) selects the cardinality and member type; the
    /// receiver (for a capture) must itself bind to a <c>[Capture]</c> param (the §3.1 leak-free invariant).
    /// </summary>
    public bool TryGetStatementHole(StatementSyntax statement, out StatementHole hole)
    {
        hole = null!;

        if (statement is not ExpressionStatementSyntax { Expression: InvocationExpressionSyntax invocation })
            return false;

        if (_semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
            return false;

        if (!TryParseQuantifier(method.Name, out var quantifier))
            return false;

        int count = 0;
        if (quantifier == StatementQuantifier.Exactly)
        {
            var argument = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
            if (argument is null || _semanticModel.GetConstantValue(argument).Value is not int parsed)
                return false;
            count = parsed;
        }

        if (_statementType is not null && SymbolEqualityComparer.Default.Equals(method.ContainingType, _statementType))
        {
            hole = new StatementHole(StatementHoleKind.Wildcard, quantifier, count, ordinal: -1, memberName: string.Empty, parameterName: string.Empty);
            return true;
        }

        if (_stmtType is not null && SymbolEqualityComparer.Default.Equals(method.ContainingType, _stmtType))
        {
            // A capture hole — the receiver of the quantifier verb must bind to a [Capture] Stmt param.
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                return false;
            if (_semanticModel.GetSymbolInfo(memberAccess.Expression).Symbol is not IParameterSymbol parameter)
                return false;
            if (!_captureSymbols.Contains(parameter))
                return false;

            hole = new StatementHole(StatementHoleKind.Capture, quantifier, count, parameter.Ordinal, ToMemberName(parameter.Name), parameter.Name);
            return true;
        }

        return false;
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

    /// <summary>
    /// A pattern with a block (statement) body — a method or local function whose <c>{ }</c> holds the
    /// statement core the matcher aligns (statement-Single / Bare / None).
    /// </summary>
    public static bool TryGetBlockBody(SyntaxNode pattern, out BlockSyntax block)
    {
        block = pattern switch
        {
            MethodDeclarationSyntax method => method.Body!,
            LocalFunctionStatementSyntax localFunction => localFunction.Body!,
            _ => null!,
        };

        return block is not null;
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

    private static bool TryParseQuantifier(string name, out StatementQuantifier quantifier)
    {
        switch (name)
        {
            case "One": quantifier = StatementQuantifier.One; return true;
            case "Opt": quantifier = StatementQuantifier.Opt; return true;
            case "Some": quantifier = StatementQuantifier.Some; return true;
            case "All": quantifier = StatementQuantifier.All; return true;
            case "Exactly": quantifier = StatementQuantifier.Exactly; return true;
            default: quantifier = default; return false;
        }
    }
}

/// <summary>Whether a statement hole captures (an instance verb on a <c>[Capture] Stmt</c>) or merely matches (a static <c>Statement</c> verb).</summary>
internal enum StatementHoleKind
{
    Capture,
    Wildcard,
}

/// <summary>The statement-hole cardinality verb. <c>Some</c>/<c>All</c>/<c>Opt</c> are variable-length; <c>One</c>/<c>Exactly</c> are fixed-arity.</summary>
internal enum StatementQuantifier
{
    One,
    Opt,
    Some,
    All,
    Exactly,
}

/// <summary>
/// A classified statement hole: its kind (capture/wildcard), quantifier, the <c>Exactly</c> count, and — for a
/// capture — the parameter <see cref="Ordinal"/> (record-member order), PascalCased member name, and parameter
/// name (the <c>cap_{name}</c> local).
/// </summary>
internal sealed class StatementHole
{
    public StatementHole(StatementHoleKind kind, StatementQuantifier quantifier, int count, int ordinal, string memberName, string parameterName)
    {
        Kind = kind;
        Quantifier = quantifier;
        Count = count;
        Ordinal = ordinal;
        MemberName = memberName;
        ParameterName = parameterName;
    }

    public StatementHoleKind Kind { get; }
    public StatementQuantifier Quantifier { get; }
    public int Count { get; }
    public int Ordinal { get; }
    public string MemberName { get; }
    public string ParameterName { get; }

    /// <summary><c>Some</c> (1+), <c>All</c> (0+), <c>Opt</c> (0–1) consume a variable number of statements; <c>One</c>/<c>Exactly</c> are fixed.</summary>
    public bool IsVariableLength => Quantifier is StatementQuantifier.Some or StatementQuantifier.All or StatementQuantifier.Opt;
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

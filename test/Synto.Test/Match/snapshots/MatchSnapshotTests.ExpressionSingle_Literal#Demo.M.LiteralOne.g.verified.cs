//HintName: Demo.M.LiteralOne.g.cs
#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Demo;
partial class M
{
    public sealed record LiteralOneMatch();
    public static LiteralOneMatch? LiteralOne(SyntaxNode node)
    {
        if (node is not LiteralExpressionSyntax _t0)
            return null;
        if (!_t0.IsKind(SyntaxKind.NumericLiteralExpression))
            return null;
        if (_t0.ChildNodesAndTokens().Count != 1)
            return null;
        var _t1 = _t0.ChildNodesAndTokens()[0];
        if (!_t1.IsKind(SyntaxKind.NumericLiteralToken))
            return null;
        if (_t1.AsToken().Text != "1")
            return null;
        return new LiteralOneMatch();
    }

    public static bool LiteralOneCouldMatch(SyntaxNode node)
    {
        return node is LiteralExpressionSyntax && node.IsKind(SyntaxKind.NumericLiteralExpression);
    }

    public static global::Synto.Matching.MatchPattern<LiteralOneMatch> LiteralOnePattern { get; } = new(LiteralOneCouldMatch, LiteralOne);
}
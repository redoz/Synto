//HintName: Demo.M.EqualsAnything.g.cs
#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Demo;
partial class M
{
    public sealed record EqualsAnythingMatch(ExpressionSyntax Lhs);
    public static EqualsAnythingMatch? EqualsAnything(SyntaxNode node)
    {
        if (node is not BinaryExpressionSyntax _t0)
            return null;
        if (!_t0.IsKind(SyntaxKind.EqualsExpression))
            return null;
        if (_t0.ChildNodesAndTokens().Count != 3)
            return null;
        if (_t0.ChildNodesAndTokens()[0].AsNode()is not ExpressionSyntax cap_lhs)
            return null;
        var _t1 = _t0.ChildNodesAndTokens()[1];
        if (!_t1.IsKind(SyntaxKind.EqualsEqualsToken))
            return null;
        if (_t1.AsToken().Text != "==")
            return null;
        if (_t0.ChildNodesAndTokens()[2].AsNode()is not ExpressionSyntax)
            return null;
        return new EqualsAnythingMatch(cap_lhs);
    }

    public static bool EqualsAnythingCouldMatch(SyntaxNode node)
    {
        return node is BinaryExpressionSyntax && node.IsKind(SyntaxKind.EqualsExpression);
    }

    public static global::Synto.Matching.MatchPattern<EqualsAnythingMatch> EqualsAnythingPattern { get; } = new(EqualsAnythingCouldMatch, EqualsAnything);
}
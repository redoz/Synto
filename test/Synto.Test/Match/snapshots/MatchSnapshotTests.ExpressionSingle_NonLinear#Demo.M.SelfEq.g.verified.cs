//HintName: Demo.M.SelfEq.g.cs
#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Demo;
partial class M
{
    public sealed record SelfEqMatch(ExpressionSyntax X);
    public static SelfEqMatch? SelfEq(SyntaxNode node)
    {
        if (node is not BinaryExpressionSyntax _t0)
            return null;
        if (!_t0.IsKind(SyntaxKind.EqualsExpression))
            return null;
        if (_t0.ChildNodesAndTokens().Count != 3)
            return null;
        if (_t0.ChildNodesAndTokens()[0].AsNode()is not ExpressionSyntax cap_x)
            return null;
        var _t1 = _t0.ChildNodesAndTokens()[1];
        if (!_t1.IsKind(SyntaxKind.EqualsEqualsToken))
            return null;
        if (_t1.AsToken().Text != "==")
            return null;
        if (_t0.ChildNodesAndTokens()[2].AsNode()is not ExpressionSyntax _t2)
            return null;
        if (!_t2.IsEquivalentTo(
                   cap_x, 
                   topLevel: false))
            return null;
        return new SelfEqMatch(cap_x);
    }

    public static bool SelfEqCouldMatch(SyntaxNode node)
    {
        return node is BinaryExpressionSyntax && node.IsKind(SyntaxKind.EqualsExpression);
    }

    public static global::Synto.Matching.MatchPattern<SelfEqMatch> SelfEqPattern { get; } = new(SelfEqCouldMatch, SelfEq);
}
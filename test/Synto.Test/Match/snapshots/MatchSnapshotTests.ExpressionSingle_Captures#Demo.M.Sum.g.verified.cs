//HintName: Demo.M.Sum.g.cs
#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Demo;
partial class M
{
    public sealed record SumMatch(ExpressionSyntax A, ExpressionSyntax B);
    public static SumMatch? Sum(SyntaxNode node)
    {
        if (node is not BinaryExpressionSyntax _t0)
            return null;
        if (!_t0.IsKind(SyntaxKind.AddExpression))
            return null;
        if (_t0.ChildNodesAndTokens().Count != 3)
            return null;
        if (_t0.ChildNodesAndTokens()[0].AsNode()is not ExpressionSyntax cap_a)
            return null;
        var _t1 = _t0.ChildNodesAndTokens()[1];
        if (!_t1.IsKind(SyntaxKind.PlusToken))
            return null;
        if (_t1.AsToken().Text != "+")
            return null;
        if (_t0.ChildNodesAndTokens()[2].AsNode()is not ExpressionSyntax cap_b)
            return null;
        return new SumMatch(cap_a, cap_b);
    }

    public static bool SumCouldMatch(SyntaxNode node)
    {
        return node is BinaryExpressionSyntax && node.IsKind(SyntaxKind.AddExpression);
    }
}
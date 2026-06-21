//HintName: Demo.M.OneGuard.g.cs
#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Demo;
partial class M
{
    public sealed record OneGuardMatch(ExpressionSyntax Cond, StatementSyntax Only);
    public static OneGuardMatch? OneGuard(SyntaxNode node)
    {
        if (node is not BlockSyntax _blk)
            return null;
        OneGuardMatch? _TryAt(int _o)
        {
            if (_blk.Statements[_o] is not IfStatementSyntax _t0)
                return null;
            if (!_t0.IsKind(SyntaxKind.IfStatement))
                return null;
            if (_t0.ChildNodesAndTokens().Count != 5)
                return null;
            var _t1 = _t0.ChildNodesAndTokens()[0];
            if (!_t1.IsKind(SyntaxKind.IfKeyword))
                return null;
            if (_t1.AsToken().Text != "if")
                return null;
            var _t2 = _t0.ChildNodesAndTokens()[1];
            if (!_t2.IsKind(SyntaxKind.OpenParenToken))
                return null;
            if (_t2.AsToken().Text != "(")
                return null;
            if (_t0.ChildNodesAndTokens()[2].AsNode()is not ExpressionSyntax cap_cond)
                return null;
            var _t3 = _t0.ChildNodesAndTokens()[3];
            if (!_t3.IsKind(SyntaxKind.CloseParenToken))
                return null;
            if (_t3.AsToken().Text != ")")
                return null;
            if (_t0.ChildNodesAndTokens()[4].AsNode()is not StatementSyntax cap_only)
                return null;
            return new OneGuardMatch(cap_cond, cap_only);
        }

        for (int _o = 0; _o <= _blk.Statements.Count - 1; _o++)
        {
            if (_TryAt(_o)is { } _m)
                return _m;
        }

        return null;
    }

    public static bool OneGuardCouldMatch(SyntaxNode node)
    {
        return node is BlockSyntax;
    }

    public static global::Synto.Matching.MatchPattern<OneGuardMatch> OneGuardPattern { get; } = new(OneGuardCouldMatch, OneGuard);
}
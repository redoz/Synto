//HintName: Demo.M.Reversed.g.cs
#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace Demo;
partial class M
{
    public sealed record ReversedMatch(SyntaxList<StatementSyntax> Rest, ExpressionSyntax Cond);
    public static ReversedMatch? Reversed(SyntaxNode node)
    {
        if (node is not BlockSyntax _blk)
            return null;
        ReversedMatch? _TryAt(int _o)
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
            if (_t0.ChildNodesAndTokens()[4].AsNode()is not BlockSyntax _t4)
                return null;
            if (!_t4.IsKind(SyntaxKind.Block))
                return null;
            if (_t4.ChildNodesAndTokens().Count != 2)
                return null;
            var _t5 = _t4.ChildNodesAndTokens()[0];
            if (!_t5.IsKind(SyntaxKind.OpenBraceToken))
                return null;
            if (_t5.AsToken().Text != "{")
                return null;
            var _t6 = _t4.ChildNodesAndTokens()[1];
            if (!_t6.IsKind(SyntaxKind.CloseBraceToken))
                return null;
            if (_t6.AsToken().Text != "}")
                return null;
            int _var = _blk.Statements.Count - _o - 1 - 0;
            SyntaxList<StatementSyntax> cap_rest = new SyntaxList<StatementSyntax>(_blk.Statements.Skip(_o + 1).Take(_var));
            return new ReversedMatch(cap_rest, cap_cond);
        }

        for (int _o = 0; _o <= _blk.Statements.Count - 1; _o++)
        {
            if (_TryAt(_o)is { } _m)
                return _m;
        }

        return null;
    }

    public static bool ReversedCouldMatch(SyntaxNode node)
    {
        return node is BlockSyntax;
    }

    public static global::Synto.Matching.MatchPattern<ReversedMatch> ReversedPattern { get; } = new(ReversedCouldMatch, Reversed);
}
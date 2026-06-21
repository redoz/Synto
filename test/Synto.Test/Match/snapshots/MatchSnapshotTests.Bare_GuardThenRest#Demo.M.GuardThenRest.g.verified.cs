//HintName: Demo.M.GuardThenRest.g.cs
#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace Demo;
partial class M
{
    public sealed record GuardThenRestMatch(ExpressionSyntax Cond, StatementSyntax Guard, SyntaxList<StatementSyntax> Rest);
    public static GuardThenRestMatch? GuardThenRest(SyntaxNode node)
    {
        if (node is not BlockSyntax _blk)
            return null;
        GuardThenRestMatch? _TryAt(int _o)
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
            if (_t0.ChildNodesAndTokens()[4].AsNode()is not StatementSyntax cap_guard)
                return null;
            int _var = _blk.Statements.Count - _o - 1 - 0;
            SyntaxList<StatementSyntax> cap_rest = new SyntaxList<StatementSyntax>(_blk.Statements.Skip(_o + 1).Take(_var));
            return new GuardThenRestMatch(cap_cond, cap_guard, cap_rest);
        }

        for (int _o = 0; _o <= _blk.Statements.Count - 1; _o++)
        {
            if (_TryAt(_o)is { } _m)
                return _m;
        }

        return null;
    }

    public static bool GuardThenRestCouldMatch(SyntaxNode node)
    {
        return node is BlockSyntax;
    }
}
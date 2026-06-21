//HintName: Demo.M.RunThenReturn.g.cs
#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace Demo;
partial class M
{
    public sealed record RunThenReturnMatch(SyntaxList<StatementSyntax> Body);
    public static RunThenReturnMatch? RunThenReturn(SyntaxNode node)
    {
        if (node is not BlockSyntax _blk)
            return null;
        RunThenReturnMatch? _TryAt(int _o)
        {
            int _var = _blk.Statements.Count - _o - 0 - 1;
            SyntaxList<StatementSyntax> cap_body = new SyntaxList<StatementSyntax>(_blk.Statements.Skip(_o).Take(_var));
            if (_blk.Statements[_o + _var] is not ReturnStatementSyntax _t0)
                return null;
            if (!_t0.IsKind(SyntaxKind.ReturnStatement))
                return null;
            if (_t0.ChildNodesAndTokens().Count != 2)
                return null;
            var _t1 = _t0.ChildNodesAndTokens()[0];
            if (!_t1.IsKind(SyntaxKind.ReturnKeyword))
                return null;
            if (_t1.AsToken().Text != "return")
                return null;
            var _t2 = _t0.ChildNodesAndTokens()[1];
            if (!_t2.IsKind(SyntaxKind.SemicolonToken))
                return null;
            if (_t2.AsToken().Text != ";")
                return null;
            return new RunThenReturnMatch(cap_body);
        }

        for (int _o = 0; _o <= _blk.Statements.Count - 1; _o++)
        {
            if (_TryAt(_o)is { } _m)
                return _m;
        }

        return null;
    }

    public static bool RunThenReturnCouldMatch(SyntaxNode node)
    {
        return node is BlockSyntax;
    }
}
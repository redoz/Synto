//HintName: Demo.M.ReturnCapture.g.cs
#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Demo;
partial class M
{
    public sealed record ReturnCaptureMatch(ExpressionSyntax Result);
    public static ReturnCaptureMatch? ReturnCapture(SyntaxNode node)
    {
        if (node is not BlockSyntax _blk)
            return null;
        ReturnCaptureMatch? _TryAt(int _o)
        {
            if (_blk.Statements[_o] is not ReturnStatementSyntax _t0)
                return null;
            if (!_t0.IsKind(SyntaxKind.ReturnStatement))
                return null;
            if (_t0.ChildNodesAndTokens().Count != 3)
                return null;
            var _t1 = _t0.ChildNodesAndTokens()[0];
            if (!_t1.IsKind(SyntaxKind.ReturnKeyword))
                return null;
            if (_t1.AsToken().Text != "return")
                return null;
            if (_t0.ChildNodesAndTokens()[1].AsNode()is not ExpressionSyntax cap_result)
                return null;
            var _t2 = _t0.ChildNodesAndTokens()[2];
            if (!_t2.IsKind(SyntaxKind.SemicolonToken))
                return null;
            if (_t2.AsToken().Text != ";")
                return null;
            return new ReturnCaptureMatch(cap_result);
        }

        for (int _o = 0; _o <= _blk.Statements.Count - 1; _o++)
        {
            if (_TryAt(_o)is { } _m)
                return _m;
        }

        return null;
    }
}
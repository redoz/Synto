//HintName: Demo.M.SingleDiscard.g.cs
#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Demo;
partial class M
{
    public sealed record SingleDiscardMatch(ExpressionSyntax X);
    public static SingleDiscardMatch? SingleDiscard(SyntaxNode node)
    {
        BlockSyntax? _body = node switch
        {
            MethodDeclarationSyntax _md => _md.Body,
            LocalFunctionStatementSyntax _lf => _lf.Body,
            _ => null
        };
        if (_body is not { } _blk)
            return null;
        SingleDiscardMatch? _TryAt(int _o)
        {
            if (_blk.Statements[_o] is not ExpressionStatementSyntax _t0)
                return null;
            if (!_t0.IsKind(SyntaxKind.ExpressionStatement))
                return null;
            if (_t0.ChildNodesAndTokens().Count != 2)
                return null;
            if (_t0.ChildNodesAndTokens()[0].AsNode()is not AssignmentExpressionSyntax _t1)
                return null;
            if (!_t1.IsKind(SyntaxKind.SimpleAssignmentExpression))
                return null;
            if (_t1.ChildNodesAndTokens().Count != 3)
                return null;
            if (_t1.ChildNodesAndTokens()[0].AsNode()is not IdentifierNameSyntax _t2)
                return null;
            if (!_t2.IsKind(SyntaxKind.IdentifierName))
                return null;
            if (_t2.ChildNodesAndTokens().Count != 1)
                return null;
            var _t3 = _t2.ChildNodesAndTokens()[0];
            if (!_t3.IsKind(SyntaxKind.IdentifierToken))
                return null;
            if (_t3.AsToken().Text != "_")
                return null;
            var _t4 = _t1.ChildNodesAndTokens()[1];
            if (!_t4.IsKind(SyntaxKind.EqualsToken))
                return null;
            if (_t4.AsToken().Text != "=")
                return null;
            if (_t1.ChildNodesAndTokens()[2].AsNode()is not ExpressionSyntax cap_x)
                return null;
            var _t5 = _t0.ChildNodesAndTokens()[1];
            if (!_t5.IsKind(SyntaxKind.SemicolonToken))
                return null;
            if (_t5.AsToken().Text != ";")
                return null;
            return new SingleDiscardMatch(cap_x);
        }

        int _o = 0;
        if (_blk.Statements.Count != 1)
            return null;
        if (_TryAt(_o)is { } _m)
            return _m;
        return null;
    }

    public static bool SingleDiscardCouldMatch(SyntaxNode node)
    {
        return node is MethodDeclarationSyntax or LocalFunctionStatementSyntax;
    }
}
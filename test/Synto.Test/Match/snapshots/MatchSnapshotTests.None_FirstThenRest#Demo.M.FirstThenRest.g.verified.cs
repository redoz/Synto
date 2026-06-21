//HintName: Demo.M.FirstThenRest.g.cs
#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace Demo;
partial class M
{
    public sealed record FirstThenRestMatch(StatementSyntax First, SyntaxList<StatementSyntax> Rest);
    public static FirstThenRestMatch? FirstThenRest(SyntaxNode node)
    {
        BlockSyntax? _body = node switch
        {
            MethodDeclarationSyntax _md => _md.Body,
            LocalFunctionStatementSyntax _lf => _lf.Body,
            _ => null
        };
        if (_body is not { } _blk)
            return null;
        FirstThenRestMatch? _TryAt(int _o)
        {
            if (_blk.Statements[_o] is not StatementSyntax cap_first)
                return null;
            int _var = _blk.Statements.Count - _o - 1 - 0;
            SyntaxList<StatementSyntax> cap_rest = new SyntaxList<StatementSyntax>(_blk.Statements.Skip(_o + 1).Take(_var));
            return new FirstThenRestMatch(cap_first, cap_rest);
        }

        int _o = 0;
        if (_blk.Statements.Count < 1)
            return null;
        if (_TryAt(_o)is { } _m)
            return _m;
        return null;
    }

    public static bool FirstThenRestCouldMatch(SyntaxNode node)
    {
        return node is MethodDeclarationSyntax or LocalFunctionStatementSyntax;
    }
}
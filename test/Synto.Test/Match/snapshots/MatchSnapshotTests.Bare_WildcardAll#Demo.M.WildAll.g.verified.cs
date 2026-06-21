//HintName: Demo.M.WildAll.g.cs
#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Demo;
partial class M
{
    public sealed record WildAllMatch();
    public static WildAllMatch? WildAll(SyntaxNode node)
    {
        if (node is not BlockSyntax _blk)
            return null;
        WildAllMatch? _TryAt(int _o)
        {
            return new WildAllMatch();
        }

        for (int _o = 0; _o <= _blk.Statements.Count - 0; _o++)
        {
            if (_TryAt(_o)is { } _m)
                return _m;
        }

        return null;
    }

    public static bool WildAllCouldMatch(SyntaxNode node)
    {
        return node is BlockSyntax;
    }

    public static global::Synto.Matching.MatchPattern<WildAllMatch> WildAllPattern { get; } = new(WildAllCouldMatch, WildAll);
}
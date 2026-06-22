//HintName: MatchReplaceExtensions.g.cs
#nullable enable
using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Synto.Matching;

/// <summary>
/// <c>Replace</c> — the consumer-facing, Regex-parallel mirror of <c>Regex.Replace</c>: rewrites the nodes a
/// Synto <c>[Match]</c> pattern matches in a syntax tree. Imperative (returns a <see cref="SyntaxNode"/>);
/// never part of cached incremental-pipeline state — call it from a <c>RegisterSourceOutput</c>/transform
/// stage, the consumer's own rewriter being the explicit alternative.
/// </summary>
/// <remarks>
/// Injected as <c>internal</c> source into the consumer's generator project (no Synto runtime dependency).
/// Self-contained on <c>netstandard2.0</c>: it references only <c>Microsoft.CodeAnalysis(.CSharp)</c> and the
/// injected <c>MatchPattern&lt;T&gt;</c> / <c>Matched&lt;T&gt;</c> data surface.
/// </remarks>
internal static class SyntoMatchReplaceExtensions
{
    /// <summary>
    /// Walks <paramref name="root"/> descendants-and-self with a single-pass <see cref="CSharpSyntaxRewriter"/>
    /// and replaces each node <paramref name="pattern"/> matches with <paramref name="replacement"/>, returning
    /// the rewritten root as <typeparamref name="TRoot"/>. Outermost-wins: a matched subtree is replaced
    /// wholesale and not descended into. When <paramref name="root"/> itself matches, its replacement is
    /// returned (cast to <typeparamref name="TRoot"/>); when nothing matches, <paramref name="root"/> is
    /// returned unchanged (the same instance).
    /// </summary>
    public static TRoot Replace<TRoot, TMatch>(
        this MatchPattern<TMatch> pattern,
        TRoot root,
        Func<Matched<TMatch>, SyntaxNode> replacement,
        ReplaceOption option = ReplaceOption.All)
        where TRoot : SyntaxNode
        where TMatch : class
    {
        var rewriter = new ReplaceRewriter<TMatch>(pattern, replacement);
        return (TRoot)rewriter.Visit(root)!;
    }

    /// <summary>
    /// Outermost-wins, single-pass match-driven rewriter: at each node it runs the matcher and, on a match,
    /// returns the consumer's replacement WITHOUT descending into the matched subtree; non-matching nodes are
    /// descended into normally.
    /// </summary>
    private sealed class ReplaceRewriter<TMatch> : CSharpSyntaxRewriter
        where TMatch : class
    {
        private readonly MatchPattern<TMatch> _pattern;
        private readonly Func<Matched<TMatch>, SyntaxNode> _replacement;

        public ReplaceRewriter(MatchPattern<TMatch> pattern, Func<Matched<TMatch>, SyntaxNode> replacement)
        {
            _pattern = pattern;
            _replacement = replacement;
        }

        public override SyntaxNode? Visit(SyntaxNode? node)
        {
            if (node is null)
                return null;

            var captures = _pattern.Match(node);
            if (captures is not null)
                return _replacement(new Matched<TMatch>(node, captures));

            return base.Visit(node);
        }
    }
}

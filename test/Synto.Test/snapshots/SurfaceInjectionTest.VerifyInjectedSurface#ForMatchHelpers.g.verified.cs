//HintName: ForMatchHelpers.g.cs
#nullable enable
using System;
using Microsoft.CodeAnalysis;

namespace Synto.Matching;

/// <summary>
/// The matched node together with the captures the matcher bound for it. Carries the matched
/// <see cref="SyntaxNode"/> (diagnostic location, replacement target) alongside the strongly-typed
/// <typeparamref name="TMatch"/> capture record. Holding a syntax node, this is intentionally NOT an
/// equatable pipeline value — it is the value the THIN (convenient, non-cacheable)
/// <c>ForMatch</c> overload yields; the projecting overload keeps it transform-local.
/// </summary>
/// <typeparam name="TMatch">The generated capture record for the pattern.</typeparam>
internal readonly record struct Matched<TMatch>(SyntaxNode Node, TMatch Captures)
    where TMatch : class;

/// <summary>
/// Bundles a pattern's cheap syntactic predicate (<see cref="CouldMatch"/>) with its full matcher
/// (<see cref="MatchFn"/>) so a consumer references a single descriptor (e.g. <c>M.SumPattern</c>). The
/// generated per-pattern <c>{Pattern}Pattern</c> property is an instance of this type; it is also usable
/// imperatively via <see cref="IsMatch"/> / <see cref="Match"/>.
/// </summary>
/// <typeparam name="TMatch">The generated capture record for the pattern.</typeparam>
internal readonly struct MatchPattern<TMatch> where TMatch : class
{
    /// <summary>Creates a descriptor pairing the cheap predicate <paramref name="couldMatch"/> with the full <paramref name="match"/>.</summary>
    public MatchPattern(Func<SyntaxNode, bool> couldMatch, Func<SyntaxNode, TMatch?> match)
    {
        CouldMatch = couldMatch;
        MatchFn = match;
    }

    /// <summary>The cheap root kind/shape gate — a superset of <see cref="MatchFn"/> (every node the matcher accepts passes it).</summary>
    internal Func<SyntaxNode, bool> CouldMatch { get; }

    /// <summary>The full matcher: returns the captures for a matching node, or <see langword="null"/>.</summary>
    internal Func<SyntaxNode, TMatch?> MatchFn { get; }

    /// <summary>Whether <paramref name="node"/> is a full match (cheap gate passes AND the matcher succeeds).</summary>
    public bool IsMatch(SyntaxNode node) => CouldMatch(node) && MatchFn(node) is not null;

    /// <summary>Runs the full matcher against <paramref name="node"/>, returning its captures or <see langword="null"/>.</summary>
    public TMatch? Match(SyntaxNode node) => MatchFn(node);
}

/// <summary>
/// <c>ForMatch</c> — the consumer-facing mirror of <c>ForAttributeWithMetadataName</c>: hooks an
/// incremental generator pipeline onto a Synto <c>[Match]</c> pattern. The overloads land in later tasks.
/// </summary>
internal static class SyntoMatchProviderExtensions
{
}

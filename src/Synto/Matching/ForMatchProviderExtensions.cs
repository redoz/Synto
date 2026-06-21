using Microsoft.CodeAnalysis;

namespace Synto.Matching;

/// <summary>
/// <c>ForMatch</c> — the consumer-facing mirror of <c>ForAttributeWithMetadataName</c>: hooks an
/// incremental generator pipeline onto a Synto <c>[Match]</c> pattern. Both overloads wrap
/// <c>SyntaxValueProvider.CreateSyntaxProvider</c> with the pattern's cheap predicate (the incremental
/// predicate) and its full matcher (run in the transform).
/// </summary>
/// <remarks>
/// Injected as <c>internal</c> source into the consumer's generator project (no Synto runtime dependency).
/// These wrappers are thin calls over the Roslyn incremental-generator API, which every generator project
/// references; the netstandard2.0 self-containment claim (C-FM3 — records via the injected polyfill, no
/// Synto/BCL dependency) lives with the <c>Matched&lt;T&gt;</c> / <c>MatchPattern&lt;T&gt;</c> data surface.
/// </remarks>
public static class SyntoMatchProviderExtensions
{
    /// <summary>
    /// The THIN overload: yields one <see cref="Matched{TMatch}"/> per node where <paramref name="pattern"/>
    /// matches, wrapping <c>CreateSyntaxProvider</c> with the cheap predicate + the full matcher. Convenient
    /// but NOT cacheable — <see cref="Matched{TMatch}"/> holds a <see cref="SyntaxNode"/> and so roots syntax
    /// in pipeline state. Use the projecting overload for a cacheable pipeline. (C-FM4: one per matched node.)
    /// </summary>
    public static IncrementalValuesProvider<Matched<TMatch>> ForMatch<TMatch>(
        this SyntaxValueProvider syntax, MatchPattern<TMatch> pattern)
        where TMatch : class
    {
        return syntax.CreateSyntaxProvider(
                predicate: (node, _) => pattern.CouldMatch(node),
                transform: (context, _) =>
                {
                    var captures = pattern.MatchFn(context.Node);
                    return captures is not null ? new Matched<TMatch>(context.Node, captures) : (Matched<TMatch>?)null;
                })
            .Where(static matched => matched.HasValue)
            .Select(static (matched, _) => matched!.Value);
    }
}

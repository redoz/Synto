using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto;

/// <summary>
/// Composition root for Synto's templating quoter: assembles the ordered middleware chain that reproduces the
/// dissolved <c>TemplateSyntaxQuoter</c>'s five hard-coded features over the feature-agnostic
/// <see cref="MiddlewareSyntaxQuoter{TContext}"/> engine. Only non-empty channels contribute a middleware
/// (an empty channel adds nothing, leaving the engine's zero-overhead fast path intact). Registration order
/// encodes seam precedence: within the Node seam Replacement → Trim → Decoration; within the List seam
/// InterpolationFold → MemberSegment (the engine partitions by seam, so the interleaved Decoration / MemberSegment
/// entries land in their respective partitions in this relative order).
/// </summary>
internal static class TemplateQuoterFactory
{
    public static MiddlewareSyntaxQuoter<QuoteContext> Create(
        IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> replacements,
        HashSet<SyntaxNode> trimNodes,
        bool includeTrivia,
        IReadOnlyDictionary<SyntaxNode, ExpressionSyntax>? memberSegments = null,
        IReadOnlyDictionary<SyntaxNode, ExpressionSyntax>? stringStagedRoots = null,
        IReadOnlyDictionary<SyntaxNode, ImmutableArray<AppliedDecoration>>? decorationHooks = null)
    {
        var middleware = new List<IQuoteMiddleware>();

        // Node seam (in precedence order): Replacement (pre, short-circuit) → Trim (pre, drop) → Decoration (post).
        if (replacements.Count > 0)
            middleware.Add(new ReplacementMiddleware(replacements));
        if (trimNodes.Count > 0)
            middleware.Add(new TrimMiddleware(trimNodes));

        // List seam (in precedence order): InterpolationFold → MemberSegment.
        if (stringStagedRoots is { Count: > 0 })
            middleware.Add(new InterpolationFoldMiddleware(stringStagedRoots));
        if (memberSegments is { Count: > 0 })
            middleware.Add(new MemberSegmentMiddleware(memberSegments));

        if (decorationHooks is { Count: > 0 })
            middleware.Add(new DecorationMiddleware(decorationHooks));

        return new MiddlewareSyntaxQuoter<QuoteContext>(middleware, includeTrivia);
    }
}

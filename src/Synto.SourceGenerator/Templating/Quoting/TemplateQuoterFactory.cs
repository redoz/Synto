using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto.Generators.Templating;

/// <summary>
/// Composition root for Synto's templating quoter. The Node seam is realized as the "quote action pipeline":
/// each affected node carries an ordered step list — Replace (terminal) → Builder (terminal, re-entrant
/// assembly) → Trim (terminal drop) → Decorate (post) — dispatched per node by a single
/// <see cref="NodePipelineMiddleware"/>. The List seam mirrors this: each affected owner node carries an
/// ordered step list — InterpolationFold → MemberSegment — dispatched per owner by a single
/// <see cref="ListPipelineMiddleware"/>. Only non-empty channels contribute a middleware, leaving the engine's
/// zero-overhead fast path intact. Precedence within each seam is hand-written in the folds below.
/// </summary>
internal static class TemplateQuoterFactory
{
    public static MiddlewareSyntaxQuoter<QuoteContext> Create(
        IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> replacements,
        HashSet<SyntaxNode> trimNodes,
        bool includeTrivia,
        IReadOnlyDictionary<SyntaxNode, ExpressionSyntax>? memberSegments = null,
        IReadOnlyDictionary<SyntaxNode, ExpressionSyntax>? stringStagedRoots = null,
        IReadOnlyDictionary<SyntaxNode, ImmutableArray<AppliedDecoration>>? decorationHooks = null,
        IReadOnlyDictionary<SyntaxNode, BuilderCall>? builderCalls = null)
    {
        var middleware = new List<IQuoteMiddleware>();

        // Node seam: one ordered step list per affected node, in precedence order Replace → Builder → Trim →
        // Decorate, dispatched per node.
        var nodeSteps = BuildNodeSteps(replacements, builderCalls, trimNodes, decorationHooks);
        if (nodeSteps.Count > 0)
            middleware.Add(new NodePipelineMiddleware(nodeSteps));

        // List seam: one ordered step list per affected owner node, in precedence order InterpolationFold →
        // MemberSegment, dispatched per owner.
        var listSteps = BuildListSteps(stringStagedRoots, memberSegments);
        if (listSteps.Count > 0)
            middleware.Add(new ListPipelineMiddleware(listSteps));

        return new MiddlewareSyntaxQuoter<QuoteContext>(middleware, includeTrivia);
    }

    // Fold the two list channels — re-keyed from their per-node channel keys to the OWNER node the list
    // pipeline dispatches on — into one ordered step list per owner. Precedence within an owner is fixed
    // (InterpolationFold → MemberSegment); no owner has both today (different owner node types), but the order
    // is encoded anyway for uniformity. Each owner gets ONE step carrying the FULL channel map (the steps do
    // per-hole / per-child lookup and self-defer), so a keyed owner where nothing folds reproduces exactly
    // what the global middleware did. Across different owners order is irrelevant (keyed separately).
    private static Dictionary<SyntaxNode, ImmutableArray<IListQuoteMiddleware<QuoteContext>>> BuildListSteps(
        IReadOnlyDictionary<SyntaxNode, ExpressionSyntax>? stringStagedRoots,
        IReadOnlyDictionary<SyntaxNode, ExpressionSyntax>? memberSegments)
    {
        var builders = new Dictionary<SyntaxNode, ImmutableArray<IListQuoteMiddleware<QuoteContext>>.Builder>();

        ImmutableArray<IListQuoteMiddleware<QuoteContext>>.Builder StepsFor(SyntaxNode owner)
        {
            if (!builders.TryGetValue(owner, out var builder))
            {
                builder = ImmutableArray.CreateBuilder<IListQuoteMiddleware<QuoteContext>>();
                builders.Add(owner, builder);
            }
            return builder;
        }

        // StringRoots keys are staged-string reference nodes; the owner is the enclosing interpolated string
        // (the list the fold rewrites). Distinct owners each get ONE InterpolationFoldStep over the full map.
        if (stringStagedRoots is { Count: > 0 })
        {
            InterpolationFoldStep? step = null;
            var seen = new HashSet<SyntaxNode>();
            foreach (var root in stringStagedRoots)
            {
                var owner = root.Key.FirstAncestorOrSelf<InterpolatedStringExpressionSyntax>();
                if (owner is null || !seen.Add(owner))
                    continue;
                StepsFor(owner).Add(step ??= new InterpolationFoldStep(stringStagedRoots));
            }
        }

        // MemberSegments keys are [Splice] generator method declarations; the owner is the enclosing type
        // declaration (key.Parent). Distinct owners each get ONE MemberSegmentStep over the full map.
        if (memberSegments is { Count: > 0 })
        {
            MemberSegmentStep? step = null;
            var seen = new HashSet<SyntaxNode>();
            foreach (var segment in memberSegments)
            {
                var owner = segment.Key.Parent;
                if (owner is null || !seen.Add(owner))
                    continue;
                StepsFor(owner).Add(step ??= new MemberSegmentStep(memberSegments));
            }
        }

        var steps = new Dictionary<SyntaxNode, ImmutableArray<IListQuoteMiddleware<QuoteContext>>>(builders.Count);
        foreach (var entry in builders)
            steps.Add(entry.Key, entry.Value.ToImmutable());
        return steps;
    }

    // Fold the four node-keyed channels into one ordered step list per node. Precedence within a node is
    // fixed (Replace outermost → Builder → Trim → Decorate), reproducing the dissolved global Node chain: a
    // ReplaceStep short-circuits before Builder/Trim/Decorate; a BuilderStep assembles (re-entrant) and
    // short-circuits before Trim/Decorate; a TrimStep drops before Decorate; a DecorateStep wraps the base
    // quote. Across different nodes order is irrelevant (keyed separately).
    private static Dictionary<SyntaxNode, ImmutableArray<INodeQuoteMiddleware<QuoteContext>>> BuildNodeSteps(
        IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> replacements,
        IReadOnlyDictionary<SyntaxNode, BuilderCall>? builderCalls,
        HashSet<SyntaxNode> trimNodes,
        IReadOnlyDictionary<SyntaxNode, ImmutableArray<AppliedDecoration>>? decorationHooks)
    {
        var builders = new Dictionary<SyntaxNode, ImmutableArray<INodeQuoteMiddleware<QuoteContext>>.Builder>();

        ImmutableArray<INodeQuoteMiddleware<QuoteContext>>.Builder StepsFor(SyntaxNode node)
        {
            if (!builders.TryGetValue(node, out var builder))
            {
                builder = ImmutableArray.CreateBuilder<INodeQuoteMiddleware<QuoteContext>>();
                builders.Add(node, builder);
            }
            return builder;
        }

        foreach (var replacement in replacements)
            StepsFor(replacement.Key).Add(new ReplaceStep(replacement.Value));

        if (builderCalls is { Count: > 0 })
            foreach (var call in builderCalls)
                StepsFor(call.Key).Add(new BuilderStep(call.Value));

        foreach (var node in trimNodes)
            StepsFor(node).Add(TrimStep.Instance);

        if (decorationHooks is { Count: > 0 })
            foreach (var hook in decorationHooks)
                StepsFor(hook.Key).Add(new DecorateStep(hook.Value));

        var steps = new Dictionary<SyntaxNode, ImmutableArray<INodeQuoteMiddleware<QuoteContext>>>(builders.Count);
        foreach (var entry in builders)
            steps.Add(entry.Key, entry.Value.ToImmutable());
        return steps;
    }
}

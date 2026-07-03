using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto.Generators.Templating;

/// <summary>
/// Plan-stage assembler: folds <c>QuotePlan</c> channels into the per-node / per-owner step maps the quoter
/// runs. Precedence is hand-written here (Node: Replace → Builder → Trim → Decorate; List: InterpolationFold →
/// MemberSegment) — the ONLY place feature order is known. Callable from all three quoter construction sites,
/// each with its own (partial) channels.
/// </summary>
internal static class PlanAssembler
{
    // Fold the four node-keyed channels into one ordered step list per node. Precedence within a node is
    // fixed (Replace outermost → Builder → Trim → Decorate), reproducing the dissolved global Node chain: a
    // ReplaceStep short-circuits before Builder/Trim/Decorate; a BuilderStep assembles (re-entrant) and
    // short-circuits before Trim/Decorate; a TrimStep drops before Decorate; a DecorateStep wraps the base
    // quote. Across different nodes order is irrelevant (keyed separately).
    public static Dictionary<SyntaxNode, ImmutableArray<INodeQuoteMiddleware>> BuildNodeSteps(
        IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> replacements,
        IReadOnlyDictionary<SyntaxNode, BuilderCall>? builderCalls,
        HashSet<SyntaxNode> trimNodes,
        IReadOnlyDictionary<SyntaxNode, ImmutableArray<AppliedDecoration>>? decorationHooks)
    {
        var builders = new Dictionary<SyntaxNode, ImmutableArray<INodeQuoteMiddleware>.Builder>();

        ImmutableArray<INodeQuoteMiddleware>.Builder StepsFor(SyntaxNode node)
        {
            if (!builders.TryGetValue(node, out var builder))
            {
                builder = ImmutableArray.CreateBuilder<INodeQuoteMiddleware>();
                builders.Add(node, builder);
            }
            return builder;
        }

        foreach (var replacement in replacements)
            StepsFor(replacement.Key).Add(new ReplaceStep(replacement.Value));

        if (builderCalls is { Count: > 0 })
            foreach (var call in builderCalls)
                StepsFor(call.Key).Add(new BuilderStep(call.Value, builderCalls));

        foreach (var node in trimNodes)
            StepsFor(node).Add(TrimStep.Instance);

        if (decorationHooks is { Count: > 0 })
            foreach (var hook in decorationHooks)
                StepsFor(hook.Key).Add(new DecorateStep(hook.Value));

        var steps = new Dictionary<SyntaxNode, ImmutableArray<INodeQuoteMiddleware>>(builders.Count);
        foreach (var entry in builders)
            steps.Add(entry.Key, entry.Value.ToImmutable());
        return steps;
    }

    // Fold the two list channels — re-keyed from their per-node channel keys to the OWNER node the list
    // pipeline dispatches on — into one ordered step list per owner. Precedence within an owner is fixed
    // (InterpolationFold → MemberSegment); no owner has both today (different owner node types), but the order
    // is encoded anyway for uniformity. Each owner gets ONE step carrying the FULL channel map (the steps do
    // per-hole / per-child lookup and self-defer), so a keyed owner where nothing folds reproduces exactly
    // what the global middleware did. Across different owners order is irrelevant (keyed separately).
    public static Dictionary<SyntaxNode, ImmutableArray<IListQuoteMiddleware>> BuildListSteps(
        IReadOnlyDictionary<SyntaxNode, ExpressionSyntax>? stringRoots,
        IReadOnlyDictionary<SyntaxNode, ExpressionSyntax>? memberSegments)
    {
        var builders = new Dictionary<SyntaxNode, ImmutableArray<IListQuoteMiddleware>.Builder>();

        ImmutableArray<IListQuoteMiddleware>.Builder StepsFor(SyntaxNode owner)
        {
            if (!builders.TryGetValue(owner, out var builder))
            {
                builder = ImmutableArray.CreateBuilder<IListQuoteMiddleware>();
                builders.Add(owner, builder);
            }
            return builder;
        }

        // StringRoots keys are staged-string reference nodes; the owner is the enclosing interpolated string
        // (the list the fold rewrites). Distinct owners each get ONE InterpolationFoldStep over the full map.
        if (stringRoots is { Count: > 0 })
        {
            InterpolationFoldStep? step = null;
            var seen = new HashSet<SyntaxNode>();
            foreach (var root in stringRoots)
            {
                var owner = root.Key.FirstAncestorOrSelf<InterpolatedStringExpressionSyntax>();
                if (owner is null || !seen.Add(owner))
                    continue;
                StepsFor(owner).Add(step ??= new InterpolationFoldStep(stringRoots));
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

        var steps = new Dictionary<SyntaxNode, ImmutableArray<IListQuoteMiddleware>>(builders.Count);
        foreach (var entry in builders)
            steps.Add(entry.Key, entry.Value.ToImmutable());
        return steps;
    }
}

using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto.Generators.Templating;

/// <summary>
/// The Node-seam realization of the "quote action pipeline": the plan attaches an ordered list of steps to
/// each affected node; this middleware looks the visited node up and runs its per-node step chain (terminal =
/// the engine's base quote via <c>next</c>). Untouched nodes defer straight to <c>next</c>. Composes the steps
/// exactly like the engine composes its global chain, so it reproduces the dissolved
/// Replacement→Trim→Decoration Node chain byte-for-byte, but dispatched per node instead of scanned globally.
/// </summary>
internal sealed class NodePipelineMiddleware : INodeQuoteMiddleware<QuoteContext>
{
    private readonly IReadOnlyDictionary<SyntaxNode, ImmutableArray<INodeQuoteMiddleware<QuoteContext>>> _steps;

    public NodePipelineMiddleware(IReadOnlyDictionary<SyntaxNode, ImmutableArray<INodeQuoteMiddleware<QuoteContext>>> steps)
        => _steps = steps;

    public ExpressionSyntax? Quote(SyntaxNode? node, QuoteContext ctx, NodeQuoteNext next)
    {
        if (node is null || !_steps.TryGetValue(node, out var steps))
            return next(node);

        // Compose last-to-first with the engine's continuation as the terminal — identical to the engine's own
        // chain construction, so the first step in the list is outermost (runs first).
        NodeQuoteNext chain = next;
        for (int i = steps.Length - 1; i >= 0; i--)
        {
            var step = steps[i];
            var localNext = chain;
            chain = n => step.Quote(n, ctx, localNext);
        }

        return chain(node);
    }
}

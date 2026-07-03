using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto.Generators.Templating;

/// <summary>
/// The List-seam realization of the "quote action pipeline": the mirror of <see cref="NodePipelineMiddleware"/>,
/// keyed by the visited list's <em>owner node</em> (<c>list[0].Parent</c>). The plan attaches an ordered list
/// of steps to each affected owner; this middleware looks the owner up and runs its per-owner step chain
/// (terminal = the engine's base list quote via <c>next</c>). Lists with no owner, or an unkeyed owner, defer
/// straight to <c>next</c>. Reproduces the dissolved InterpolationFold→MemberSegment List chain byte-for-byte,
/// but dispatched per owner instead of scanned globally.
/// </summary>
/// <remarks>
/// Because the engine's continuation is the generic <see cref="ListQuoteNext{TNode}"/>, the step chain cannot
/// be pre-composed in the ctor (as the node pipeline does); it is composed per call inside the generic
/// <see cref="Quote{TNode}"/>, last-to-first, so the first step in the owner's list is outermost.
///
/// Owner-key collision residual: one owner (e.g. a <c>TypeDeclarationSyntax</c>) owns several lists —
/// attributes, base types, members — that all share it as <c>.Parent</c>. Steps found for a non-target list
/// must self-check and defer; the steps already do (see <c>InterpolationFoldStep</c> /
/// <c>MemberSegmentStep</c>), so this pipeline stays generic and feature-blind.
/// </remarks>
internal sealed class ListPipelineMiddleware : IListQuoteMiddleware<QuoteContext>
{
    private readonly IReadOnlyDictionary<SyntaxNode, ImmutableArray<IListQuoteMiddleware<QuoteContext>>> _steps;

    public ListPipelineMiddleware(IReadOnlyDictionary<SyntaxNode, ImmutableArray<IListQuoteMiddleware<QuoteContext>>> steps)
        => _steps = steps;

    public ExpressionSyntax Quote<TNode>(SyntaxList<TNode> list, QuoteContext ctx, ListQuoteNext<TNode> next)
        where TNode : SyntaxNode
    {
        var owner = list.Count > 0 ? list[0].Parent : null;
        if (owner is null || !_steps.TryGetValue(owner, out var steps))
            return next(list);

        // Compose last-to-first with the engine's continuation as the terminal — identical to the engine's own
        // chain construction, so the first step in the list is outermost (runs first). Composed per call
        // because the continuation is generic over TNode.
        ListQuoteNext<TNode> chain = next;
        for (int i = steps.Length - 1; i >= 0; i--)
        {
            var step = steps[i];
            var localNext = chain;
            chain = l => step.Quote(l, ctx, localNext);
        }

        return chain(list);
    }
}

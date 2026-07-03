using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto;

// The two quote-step seams the templating feature model uses. Each (Node, List) has: a `…Next` continuation
// delegate (call it to defer to the next step / the base quoter) and an `I…QuoteMiddleware<TContext>`
// interface with a single Quote method. Steps are dispatched per node/owner by the
// PerNodePipelineQuoter. All public-SHAPED but `internal` for now; a GA flip to `public` is a pure
// accessibility change.

/// <summary>Empty marker for any quote middleware. A concrete middleware implements one or more of the seam interfaces.</summary>
internal interface IQuoteMiddleware
{
}

// ── Node seam ─────────────────────────────────────────────────────────────────────────────────────────
internal delegate ExpressionSyntax? NodeQuoteNext(SyntaxNode? node);

internal interface INodeQuoteMiddleware<TContext> : IQuoteMiddleware where TContext : QuoteContext
{
    ExpressionSyntax? Quote(SyntaxNode? node, TContext ctx, NodeQuoteNext next);
}

// ── List seam ─────────────────────────────────────────────────────────────────────────────────────────
internal delegate ExpressionSyntax ListQuoteNext<TNode>(SyntaxList<TNode> list) where TNode : SyntaxNode;

internal interface IListQuoteMiddleware<TContext> : IQuoteMiddleware where TContext : QuoteContext
{
    ExpressionSyntax Quote<TNode>(SyntaxList<TNode> list, TContext ctx, ListQuoteNext<TNode> next) where TNode : SyntaxNode;
}

using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto.Generators.Templating;

// The two quote-step seams the templating feature model uses. Each (Node, List) has: a `…Next` continuation
// delegate (call it to defer to the next step / the base quoter) and an `I…QuoteMiddleware` interface with a
// single Quote method. Steps are dispatched per node/owner by the PerNodePipelineQuoter and re-enter it via
// QuoteContext.

// ── Node seam ─────────────────────────────────────────────────────────────────────────────────────────
internal delegate ExpressionSyntax? NodeQuoteNext(SyntaxNode? node);

internal interface INodeQuoteMiddleware
{
    ExpressionSyntax? Quote(SyntaxNode? node, QuoteContext ctx, NodeQuoteNext next);
}

// ── List seam ─────────────────────────────────────────────────────────────────────────────────────────
internal delegate ExpressionSyntax ListQuoteNext<TNode>(SyntaxList<TNode> list) where TNode : SyntaxNode;

internal interface IListQuoteMiddleware
{
    // Element type this step targets (the SyntaxList element type, e.g. InterpolatedStringContentSyntax /
    // MemberDeclarationSyntax). Owner-keying alone is ambiguous — one owner node exposes several SyntaxList<T>
    // (a type declaration's AttributeLists AND Members both key on the type node). The quoter dispatches a step
    // ONLY when the visited list's TNode matches this, so each step no longer re-implements its own element-type
    // self-guard; the invariant lives in one place (PerNodePipelineQuoter.Visit{TNode}).
    Type ElementType { get; }

    ExpressionSyntax Quote<TNode>(SyntaxList<TNode> list, QuoteContext ctx, ListQuoteNext<TNode> next) where TNode : SyntaxNode;
}

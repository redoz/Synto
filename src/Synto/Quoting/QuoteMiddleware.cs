using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto;

// The middleware quoting seams. Each of the 7 base quoter seams (Node, List, SeparatedList, Token,
// TriviaList, TokenList, Kind) has: a `…Next` continuation delegate (call it to defer to the next
// middleware / the base quoter) and an `I…QuoteMiddleware<TContext>` interface with a single Quote method.
// A middleware implements only the seam interfaces it handles (netstandard2.0 has no default interface
// methods). All public-SHAPED but `internal` for now; a GA flip to `public` is a pure accessibility change.

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

// ── SeparatedList seam ────────────────────────────────────────────────────────────────────────────────
internal delegate ExpressionSyntax SeparatedListQuoteNext<TNode>(SeparatedSyntaxList<TNode> list) where TNode : SyntaxNode;

internal interface ISeparatedListQuoteMiddleware<TContext> : IQuoteMiddleware where TContext : QuoteContext
{
    ExpressionSyntax Quote<TNode>(SeparatedSyntaxList<TNode> list, TContext ctx, SeparatedListQuoteNext<TNode> next) where TNode : SyntaxNode;
}

// ── Token seam ────────────────────────────────────────────────────────────────────────────────────────
internal delegate ExpressionSyntax TokenQuoteNext(SyntaxToken token);

internal interface ITokenQuoteMiddleware<TContext> : IQuoteMiddleware where TContext : QuoteContext
{
    ExpressionSyntax Quote(SyntaxToken token, TContext ctx, TokenQuoteNext next);
}

// ── TriviaList seam ───────────────────────────────────────────────────────────────────────────────────
internal delegate ExpressionSyntax TriviaListQuoteNext(SyntaxTriviaList triviaList);

internal interface ITriviaListQuoteMiddleware<TContext> : IQuoteMiddleware where TContext : QuoteContext
{
    ExpressionSyntax Quote(SyntaxTriviaList triviaList, TContext ctx, TriviaListQuoteNext next);
}

// ── TokenList seam ────────────────────────────────────────────────────────────────────────────────────
internal delegate ExpressionSyntax TokenListQuoteNext(SyntaxTokenList tokenList);

internal interface ITokenListQuoteMiddleware<TContext> : IQuoteMiddleware where TContext : QuoteContext
{
    ExpressionSyntax Quote(SyntaxTokenList tokenList, TContext ctx, TokenListQuoteNext next);
}

// ── Kind seam ─────────────────────────────────────────────────────────────────────────────────────────
internal delegate ExpressionSyntax KindQuoteNext(SyntaxKind kind);

internal interface IKindQuoteMiddleware<TContext> : IQuoteMiddleware where TContext : QuoteContext
{
    ExpressionSyntax Quote(SyntaxKind kind, TContext ctx, KindQuoteNext next);
}

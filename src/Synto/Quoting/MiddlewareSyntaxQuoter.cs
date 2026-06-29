using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto;

/// <summary>
/// A feature-agnostic quoting engine: it owns only seam dispatch + middleware composition and "knows nothing
/// about the step it performs." Each of the base quoter's 7 seams (Node, List, SeparatedList, Token,
/// TriviaList, TokenList, Kind) is overridden to run an ordered chain of <see cref="IQuoteMiddleware"/> whose
/// terminal continuation is the base quoter. A seam whose middleware partition is empty calls
/// <c>base</c> directly (zero-overhead fast path), so an engine built with no middleware quotes exactly like a
/// plain <see cref="CSharpSyntaxQuoter"/>. Middleware are partitioned once at construction; registration order
/// is preserved within each seam, encoding precedence (e.g. Node order Replacement → Trim → Decoration).
/// Public-SHAPED but <c>internal</c> for now; a GA flip to <c>public</c> is a pure accessibility change.
/// </summary>
internal class MiddlewareSyntaxQuoter<TContext> : CSharpSyntaxQuoter where TContext : QuoteContext
{
    private readonly INodeQuoteMiddleware<TContext>[] _nodeMiddleware;
    private readonly IListQuoteMiddleware<TContext>[] _listMiddleware;
    private readonly ISeparatedListQuoteMiddleware<TContext>[] _separatedListMiddleware;
    private readonly ITokenQuoteMiddleware<TContext>[] _tokenMiddleware;
    private readonly ITriviaListQuoteMiddleware<TContext>[] _triviaListMiddleware;
    private readonly ITokenListQuoteMiddleware<TContext>[] _tokenListMiddleware;
    private readonly IKindQuoteMiddleware<TContext>[] _kindMiddleware;

    private readonly TContext _context;
    private readonly NodeQuoteNext _nodeChain;

    public MiddlewareSyntaxQuoter(IReadOnlyList<IQuoteMiddleware> middleware, bool includeTrivia)
        : base(includeTrivia)
    {
        _nodeMiddleware = middleware.OfType<INodeQuoteMiddleware<TContext>>().ToArray();
        _listMiddleware = middleware.OfType<IListQuoteMiddleware<TContext>>().ToArray();
        _separatedListMiddleware = middleware.OfType<ISeparatedListQuoteMiddleware<TContext>>().ToArray();
        _tokenMiddleware = middleware.OfType<ITokenQuoteMiddleware<TContext>>().ToArray();
        _triviaListMiddleware = middleware.OfType<ITriviaListQuoteMiddleware<TContext>>().ToArray();
        _tokenListMiddleware = middleware.OfType<ITokenListQuoteMiddleware<TContext>>().ToArray();
        _kindMiddleware = middleware.OfType<IKindQuoteMiddleware<TContext>>().ToArray();

        _context = CreateContext();

        // The node chain is non-generic, so pre-compose it once: build last-to-first so the
        // first-registered middleware ends up outermost (runs first). Terminal is base.Visit.
        NodeQuoteNext nodeNext = BaseVisitNode;
        for (int i = _nodeMiddleware.Length - 1; i >= 0; i--)
        {
            var mw = _nodeMiddleware[i];
            var localNext = nodeNext;
            nodeNext = node => mw.Quote(node, _context, localNext);
        }

        _nodeChain = nodeNext;
    }

    /// <summary>
    /// Creates the single <typeparamref name="TContext"/> bound to this engine for the lifetime of the
    /// transform (one quoter == one transform). The default supplies a trivial <see cref="DefaultQuoteContext"/>
    /// (valid when <typeparamref name="TContext"/> is <see cref="QuoteContext"/>); a future engine subclass
    /// with a richer context overrides this.
    /// </summary>
    protected virtual TContext CreateContext() => (TContext)(object)new DefaultQuoteContext(this);

    /// <summary>Bridge so the nested context can reach the base's <c>protected static</c> array-literal builder.</summary>
    private ExpressionSyntax InvokeToArrayLiteral(IEnumerable<ExpressionSyntax> nodes, TypeSyntax elementType)
        => ToArrayLiteral(nodes, elementType);

    // base.* cannot be captured inside a lambda, so each seam's terminal continuation is a tiny helper.
    private ExpressionSyntax? BaseVisitNode(SyntaxNode? node) => base.Visit(node);
    private ExpressionSyntax BaseVisitList<TNode>(SyntaxList<TNode> list) where TNode : SyntaxNode => base.Visit(list);
    private ExpressionSyntax BaseVisitSeparatedList<TNode>(SeparatedSyntaxList<TNode> list) where TNode : SyntaxNode => base.Visit(list);
    private ExpressionSyntax BaseVisitToken(SyntaxToken token) => base.Visit(token);
    private ExpressionSyntax BaseVisitTriviaList(SyntaxTriviaList triviaList) => base.Visit(triviaList);
    private ExpressionSyntax BaseVisitTokenList(SyntaxTokenList tokenList) => base.Visit(tokenList);
    private ExpressionSyntax BaseVisitKind(SyntaxKind kind) => base.Visit(kind);

    public override ExpressionSyntax? Visit(SyntaxNode? node)
    {
        if (_nodeMiddleware.Length == 0)
            return base.Visit(node);

        return _nodeChain(node);
    }

    public override ExpressionSyntax Visit<TNode>(SyntaxList<TNode> nodeList)
    {
        if (_listMiddleware.Length == 0)
            return base.Visit(nodeList);

        ListQuoteNext<TNode> next = BaseVisitList;
        for (int i = _listMiddleware.Length - 1; i >= 0; i--)
        {
            var mw = _listMiddleware[i];
            var localNext = next;
            next = list => mw.Quote(list, _context, localNext);
        }

        return next(nodeList);
    }

    public override ExpressionSyntax Visit<TNode>(SeparatedSyntaxList<TNode> nodeList)
    {
        if (_separatedListMiddleware.Length == 0)
            return base.Visit(nodeList);

        SeparatedListQuoteNext<TNode> next = BaseVisitSeparatedList;
        for (int i = _separatedListMiddleware.Length - 1; i >= 0; i--)
        {
            var mw = _separatedListMiddleware[i];
            var localNext = next;
            next = list => mw.Quote(list, _context, localNext);
        }

        return next(nodeList);
    }

    public override ExpressionSyntax Visit(SyntaxToken token)
    {
        if (_tokenMiddleware.Length == 0)
            return base.Visit(token);

        TokenQuoteNext next = BaseVisitToken;
        for (int i = _tokenMiddleware.Length - 1; i >= 0; i--)
        {
            var mw = _tokenMiddleware[i];
            var localNext = next;
            next = t => mw.Quote(t, _context, localNext);
        }

        return next(token);
    }

    public override ExpressionSyntax Visit(SyntaxTriviaList triviaList)
    {
        if (_triviaListMiddleware.Length == 0)
            return base.Visit(triviaList);

        TriviaListQuoteNext next = BaseVisitTriviaList;
        for (int i = _triviaListMiddleware.Length - 1; i >= 0; i--)
        {
            var mw = _triviaListMiddleware[i];
            var localNext = next;
            next = tl => mw.Quote(tl, _context, localNext);
        }

        return next(triviaList);
    }

    public override ExpressionSyntax Visit(SyntaxTokenList tokenList)
    {
        if (_tokenListMiddleware.Length == 0)
            return base.Visit(tokenList);

        TokenListQuoteNext next = BaseVisitTokenList;
        for (int i = _tokenListMiddleware.Length - 1; i >= 0; i--)
        {
            var mw = _tokenListMiddleware[i];
            var localNext = next;
            next = tl => mw.Quote(tl, _context, localNext);
        }

        return next(tokenList);
    }

    public override ExpressionSyntax Visit(SyntaxKind kind)
    {
        if (_kindMiddleware.Length == 0)
            return base.Visit(kind);

        KindQuoteNext next = BaseVisitKind;
        for (int i = _kindMiddleware.Length - 1; i >= 0; i--)
        {
            var mw = _kindMiddleware[i];
            var localNext = next;
            next = k => mw.Quote(k, _context, localNext);
        }

        return next(kind);
    }

    /// <summary>
    /// The trivial <see cref="QuoteContext"/> bound to this engine: re-entry (<see cref="Quote"/> /
    /// <see cref="QuoteList{TNode}"/>) routes back through the engine's seam pipelines; array-literal building
    /// defers to the base quoter. Holds only the engine reference — no live-symbol / semantic-model state.
    /// </summary>
    private sealed class DefaultQuoteContext : QuoteContext
    {
        private readonly MiddlewareSyntaxQuoter<TContext> _quoter;

        public DefaultQuoteContext(MiddlewareSyntaxQuoter<TContext> quoter) => _quoter = quoter;

        public override ExpressionSyntax? Quote(SyntaxNode? node) => _quoter.Visit(node);

        public override ExpressionSyntax QuoteList<TNode>(SyntaxList<TNode> list) => _quoter.Visit(list);

        public override ExpressionSyntax ToArrayLiteral(IEnumerable<ExpressionSyntax> nodes, TypeSyntax elementType)
            => _quoter.InvokeToArrayLiteral(nodes, elementType);
    }
}

using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto.Generators.Templating;

/// <summary>
/// The templating quoter: a <see cref="CSharpSyntaxQuoter"/> subclass that dispatches per-node and per-owner
/// step pipelines natively at the two seams the feature model uses (Node and List). It replaces the dissolved
/// feature-agnostic <c>MiddlewareSyntaxQuoter</c> engine plus its two pipeline adapters
/// (<c>NodePipelineMiddleware</c> / <c>ListPipelineMiddleware</c>): the adapters' bodies are inlined directly
/// into the <see cref="Visit(SyntaxNode?)"/> / <see cref="Visit{TNode}(SyntaxList{TNode})"/> overrides, one
/// dispatch layer instead of two. Feature precedence lives entirely in <see cref="PlanAssembler"/>, which
/// hands over the two pre-assembled step maps; steps re-enter the quoter via <see cref="QuoteContext"/>. The
/// other five base-quoter seams (SeparatedList, Token, TriviaList, TokenList, Kind) are left at their pinned
/// base behavior. Public-SHAPED but <c>internal</c> for now; a GA flip to <c>public</c> is a pure
/// accessibility change.
/// </summary>
internal sealed class PerNodePipelineQuoter : CSharpSyntaxQuoter
{
    private readonly IReadOnlyDictionary<SyntaxNode, ImmutableArray<INodeQuoteMiddleware<QuoteContext>>> _nodeSteps;
    private readonly IReadOnlyDictionary<SyntaxNode, ImmutableArray<IListQuoteMiddleware<QuoteContext>>> _listSteps;
    private readonly QuoteContext _context;

    public PerNodePipelineQuoter(
        IReadOnlyDictionary<SyntaxNode, ImmutableArray<INodeQuoteMiddleware<QuoteContext>>> nodeSteps,
        IReadOnlyDictionary<SyntaxNode, ImmutableArray<IListQuoteMiddleware<QuoteContext>>> listSteps,
        bool includeTrivia)
        : base(includeTrivia)
    {
        _nodeSteps = nodeSteps;
        _listSteps = listSteps;
        _context = new PipelineQuoteContext(this);
    }

    /// <summary>Bridge so the nested context can reach the base's <c>protected static</c> array-literal builder.</summary>
    private ExpressionSyntax InvokeToArrayLiteral(IEnumerable<ExpressionSyntax> nodes, TypeSyntax elementType)
        => ToArrayLiteral(nodes, elementType);

    // base.* cannot be captured inside a lambda, so each seam's terminal continuation is a tiny helper.
    private ExpressionSyntax? BaseVisitNode(SyntaxNode? node) => base.Visit(node);
    private ExpressionSyntax BaseVisitList<TNode>(SyntaxList<TNode> list) where TNode : SyntaxNode => base.Visit(list);

    public override ExpressionSyntax? Visit(SyntaxNode? node)
    {
        if (node is null || !_nodeSteps.TryGetValue(node, out var steps))
            return base.Visit(node);

        // Compose last-to-first with base.Visit as the terminal, so the first step in the list is outermost
        // (runs first) — identical ordering to the dissolved engine + adapter chain.
        NodeQuoteNext chain = BaseVisitNode;
        for (int i = steps.Length - 1; i >= 0; i--)
        {
            var step = steps[i];
            var localNext = chain;
            chain = n => step.Quote(n, _context, localNext);
        }

        return chain(node);
    }

    public override ExpressionSyntax Visit<TNode>(SyntaxList<TNode> nodeList)
    {
        // Keyed by the visited list's owner node (list[0].Parent). Lists with no owner, or an unkeyed owner,
        // defer straight to base. Composed per call because the continuation is generic over TNode.
        var owner = nodeList.Count > 0 ? nodeList[0].Parent : null;
        if (owner is null || !_listSteps.TryGetValue(owner, out var steps))
            return base.Visit(nodeList);

        ListQuoteNext<TNode> chain = BaseVisitList;
        for (int i = steps.Length - 1; i >= 0; i--)
        {
            var step = steps[i];
            var localNext = chain;
            chain = l => step.Quote(l, _context, localNext);
        }

        return chain(nodeList);
    }

    /// <summary>
    /// The <see cref="QuoteContext"/> bound to this quoter: re-entry (<see cref="QuoteContext.Quote"/> /
    /// <see cref="QuoteContext.QuoteList{TNode}"/>) routes back through this quoter's seam dispatch;
    /// array-literal building defers to the base quoter. Holds only the quoter reference — no live-symbol /
    /// semantic-model state.
    /// </summary>
    private sealed class PipelineQuoteContext : QuoteContext
    {
        private readonly PerNodePipelineQuoter _quoter;

        public PipelineQuoteContext(PerNodePipelineQuoter quoter) => _quoter = quoter;

        public override ExpressionSyntax? Quote(SyntaxNode? node) => _quoter.Visit(node);

        public override ExpressionSyntax QuoteList<TNode>(SyntaxList<TNode> list) => _quoter.Visit(list);

        public override ExpressionSyntax ToArrayLiteral(IEnumerable<ExpressionSyntax> nodes, TypeSyntax elementType)
            => _quoter.InvokeToArrayLiteral(nodes, elementType);
    }
}

using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto.Generators.Templating;

/// <summary>
/// Composition root for Synto's templating quoter. Names zero features: it wires the two pre-assembled step
/// maps (produced by <see cref="PlanAssembler"/>) into the feature-agnostic
/// <see cref="MiddlewareSyntaxQuoter{TContext}"/> engine — one <see cref="NodePipelineMiddleware"/> for the
/// per-node step map, one <see cref="ListPipelineMiddleware"/> for the per-owner step map. An empty map
/// contributes no middleware, leaving the engine's zero-overhead fast path intact. Feature precedence lives
/// entirely in <see cref="PlanAssembler"/>; this factory knows nothing of it.
/// </summary>
internal static class TemplateQuoterFactory
{
    public static MiddlewareSyntaxQuoter<QuoteContext> Create(
        IReadOnlyDictionary<SyntaxNode, ImmutableArray<INodeQuoteMiddleware<QuoteContext>>> nodeSteps,
        IReadOnlyDictionary<SyntaxNode, ImmutableArray<IListQuoteMiddleware<QuoteContext>>> listSteps,
        bool includeTrivia)
    {
        var middleware = new List<IQuoteMiddleware>();

        if (nodeSteps.Count > 0)
            middleware.Add(new NodePipelineMiddleware(nodeSteps));

        if (listSteps.Count > 0)
            middleware.Add(new ListPipelineMiddleware(listSteps));

        return new MiddlewareSyntaxQuoter<QuoteContext>(middleware, includeTrivia);
    }
}

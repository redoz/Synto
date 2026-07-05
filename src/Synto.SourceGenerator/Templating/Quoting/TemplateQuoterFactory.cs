using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.Generators.Templating.Quoting.Passes;

namespace Synto.Generators.Templating.Quoting;

/// <summary>
/// Composition root for Synto's templating quoter. Names zero features: it wires the two pre-assembled step
/// maps (produced by <see cref="PlanAssembler"/>) directly into a <see cref="PerNodePipelineQuoter"/>, which
/// dispatches the per-node step map at the Node seam and the per-owner step map at the List seam. An empty
/// map leaves that seam at its zero-overhead base behavior. Feature precedence lives entirely in
/// <see cref="PlanAssembler"/>; this factory knows nothing of it.
/// </summary>
internal static class TemplateQuoterFactory
{
    public static PerNodePipelineQuoter Create(
        IReadOnlyDictionary<SyntaxNode, ImmutableArray<INodeQuoteMiddleware>> nodeSteps,
        IReadOnlyDictionary<SyntaxNode, ImmutableArray<IListQuoteMiddleware>> listSteps,
        bool includeTrivia)
        => new PerNodePipelineQuoter(nodeSteps, listSteps, includeTrivia);
}

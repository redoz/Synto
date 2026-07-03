using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto.Generators.Templating;

/// <summary>
/// Composition root for Synto's templating quoter. The Node seam is realized as the "quote action pipeline":
/// each affected node carries an ordered step list — Replace (terminal) → Builder (terminal, re-entrant
/// assembly) → Trim (terminal drop) → Decorate (post) — dispatched per node by a single
/// <see cref="NodePipelineMiddleware"/>. The List seam mirrors this: each affected owner node carries an
/// ordered step list — InterpolationFold → MemberSegment — dispatched per owner by a single
/// <see cref="ListPipelineMiddleware"/>. Only non-empty channels contribute a middleware, leaving the engine's
/// zero-overhead fast path intact. Precedence within each seam is hand-written in the folds below.
/// </summary>
internal static class TemplateQuoterFactory
{
    public static MiddlewareSyntaxQuoter<QuoteContext> Create(
        IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> replacements,
        HashSet<SyntaxNode> trimNodes,
        bool includeTrivia,
        IReadOnlyDictionary<SyntaxNode, ExpressionSyntax>? memberSegments = null,
        IReadOnlyDictionary<SyntaxNode, ExpressionSyntax>? stringStagedRoots = null,
        IReadOnlyDictionary<SyntaxNode, ImmutableArray<AppliedDecoration>>? decorationHooks = null,
        IReadOnlyDictionary<SyntaxNode, BuilderCall>? builderCalls = null)
    {
        var middleware = new List<IQuoteMiddleware>();

        // Node seam: one ordered step list per affected node, in precedence order Replace → Builder → Trim →
        // Decorate, dispatched per node.
        var nodeSteps = PlanAssembler.BuildNodeSteps(replacements, builderCalls, trimNodes, decorationHooks);
        if (nodeSteps.Count > 0)
            middleware.Add(new NodePipelineMiddleware(nodeSteps));

        // List seam: one ordered step list per affected owner node, in precedence order InterpolationFold →
        // MemberSegment, dispatched per owner.
        var listSteps = PlanAssembler.BuildListSteps(stringStagedRoots, memberSegments);
        if (listSteps.Count > 0)
            middleware.Add(new ListPipelineMiddleware(listSteps));

        return new MiddlewareSyntaxQuoter<QuoteContext>(middleware, includeTrivia);
    }
}

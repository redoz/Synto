using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto;

/// <summary>
/// Discovers the live control regions in a template body and the set of nodes those regions consume. Pure
/// transform-internal helper; nothing captured into pipeline state.
/// </summary>
internal static class StagedRegionFinder
{
    /// <summary>Discovers the live control regions in <paramref name="body"/> using the classifier partition.</summary>
    public static IReadOnlyList<StagedRegion> FindRegions(SemanticModel semanticModel, SyntaxNode body, BindingTimePartition partition)
    {
        var regions = new List<StagedRegion>();
        foreach (var node in body.DescendantNodes())
        {
            if (node is not StatementSyntax statement)
                continue;
            if (!partition.IsStagedControl(statement))
                continue;
            if (statement.Parent is not BlockSyntax container)
                continue; // only block-owned regions are unrolled in v1 (else-if chains handled within the if)

            var loopVariables = new List<ISymbol>();
            switch (statement)
            {
                case ForEachStatementSyntax forEach:
                    if (semanticModel.GetDeclaredSymbol(forEach) is { } v)
                        loopVariables.Add(v);
                    break;
                case ForStatementSyntax forStatement when forStatement.Declaration is { } declaration:
                    foreach (var declarator in declaration.Variables)
                        if (semanticModel.GetDeclaredSymbol(declarator) is { } fv)
                            loopVariables.Add(fv);
                    break;
            }

            regions.Add(new StagedRegion(statement, container, loopVariables));
        }

        return regions;
    }

    /// <summary>
    /// The set of nodes consumed by a live region (every node inside the region's control statement). A live-root
    /// reference in this set is handled by the verbatim scaffold (which uses the factory parameter directly),
    /// so the caller must NOT also emit a depth-0 <c>.ToSyntax()</c> lift for it.
    /// </summary>
    public static HashSet<SyntaxNode> ComputeConsumedNodes(IReadOnlyList<StagedRegion> regions)
    {
        var consumed = new HashSet<SyntaxNode>();
        foreach (var region in regions)
        {
            foreach (var node in region.Control.DescendantNodesAndSelf())
                consumed.Add(node);
        }

        return consumed;
    }

}

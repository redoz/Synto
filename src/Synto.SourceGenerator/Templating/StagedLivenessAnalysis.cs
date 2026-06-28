using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto;

/// <summary>
/// Computes the region-local live (staged) symbol set for the staging emitter. Pure transform-internal helper;
/// nothing captured into pipeline state.
/// </summary>
internal static class StagedLivenessAnalysis
{
    /// <summary>
    /// The region-local live set: the classifier's live symbols, plus every region loop variable, plus
    /// accumulators (locals whose initializer or an assignment within the region references a live value). The
    /// classifier tracks neither loop variables nor mutation-defined accumulators, so the emitter recovers them
    /// here by a fixpoint over the region bodies.
    /// </summary>
    public static HashSet<ISymbol> ComputeStagedSet(SemanticModel semanticModel, IReadOnlyList<StagedRegion> regions, IReadOnlyCollection<ISymbol> baseStaged)
    {
        var live = new HashSet<ISymbol>(baseStaged, SymbolEqualityComparer.Default);
        foreach (var region in regions)
            foreach (var loopVariable in region.LoopVariables)
                live.Add(loopVariable);

        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var region in regions)
            {
                foreach (var node in region.Control.DescendantNodes())
                {
                    switch (node)
                    {
                        case VariableDeclaratorSyntax declarator when declarator.Initializer is { } initializer:
                            if (semanticModel.GetDeclaredSymbol(declarator) is { } local
                                && !live.Contains(local)
                                && ReferencesStaged(semanticModel, initializer.Value, live))
                            {
                                live.Add(local);
                                changed = true;
                            }
                            break;

                        case AssignmentExpressionSyntax assignment:
                            if (semanticModel.GetSymbolInfo(assignment.Left).Symbol is ILocalSymbol target
                                && !live.Contains(target)
                                && ReferencesStaged(semanticModel, assignment.Right, live))
                            {
                                live.Add(target);
                                changed = true;
                            }
                            break;
                    }
                }
            }
        }

        return live;
    }

    public static bool ReferencesStaged(SemanticModel semanticModel, SyntaxNode node, HashSet<ISymbol> stagedSet)
    {
        foreach (var identifier in node.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            if (identifier.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == identifier)
                continue;

            var symbol = semanticModel.GetSymbolInfo(identifier).Symbol;
            if (symbol is not null && stagedSet.Contains(symbol))
                return true;
        }

        return false;
    }
}

using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.Generators.Templating.BindingTime;
using Synto.Generators.Templating.Builders;
using Synto.Generators.Templating.Diagnostics;
using Synto.Generators.Templating.Emit;
using Synto.Generators.Templating.Quoting;

namespace Synto.Generators.Templating.Staging;

/// <summary>
/// Precomputes the run+collect replacement for each live control region (plan Task 6–7). The verbatim control
/// scaffold is hoisted into the factory <c>preamble</c> (collecting quoted islands into a
/// <c>List&lt;StatementSyntax&gt;</c> run, and keeping live locals/accumulators as runtime state), and the
/// region's owning container block is keyed in <c>unquotedReplacements</c> to a
/// <c>Block(BuildList(Run(run), &lt;fixed siblings&gt;...))</c> — so the quoter never descends into the region
/// (the map hit returns the precomputed expression). Each quoted island is produced by a fresh
/// <see cref="PerNodePipelineQuoter"/> (built by <see cref="TemplateQuoterFactory"/>) whose map lifts every maximal purely-live subexpression via
/// <c>.ToSyntax()</c>. Runs entirely inside the generator transform; nothing captured into pipeline state.
/// </summary>
internal static class StagedRegionEmitter
{
    public static StagedRegionEmission Emit(
        SemanticModel semanticModel,
        BindingTimePartition partition,
        IReadOnlyList<StagedRegion> regions,
        IReadOnlyCollection<ISymbol> baseStaged,
        IReadOnlyDictionary<ISymbol, string> rootNames,
        IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> baseReplacements,
        HashSet<SyntaxNode> trimNodes,
        IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> stringStagedRoots,
        IReadOnlyDictionary<SyntaxNode, BuilderCall> builderCalls,
        ref int counter)
    {
        var emission = new StagedRegionEmission();
        var stagedSet = StagedLivenessAnalysis.ComputeStagedSet(semanticModel, regions, baseStaged);
        var renamer = new RootRenameRewriter(semanticModel, rootNames);
        var context = new StagedEmitContext(semanticModel, partition, stagedSet, renamer, baseReplacements, stringStagedRoots, builderCalls, emission);

        foreach (var group in regions.GroupBy(r => r.Container))
        {
            var container = group.Key;
            var regionByControl = new Dictionary<SyntaxNode, StagedRegion>();
            foreach (var region in group)
                regionByControl[region.Control] = region;

            var segments = new List<ExpressionSyntax>();

            foreach (var statement in container.Statements)
            {
                if (trimNodes.Contains(statement))
                    continue;

                if (regionByControl.TryGetValue(statement, out var region))
                {
                    string runName = "__run_" + counter++;
                    if (!StagedScaffoldBuilder.TryBuildScaffold(context, region, runName))
                        return emission; // a diagnostic was recorded
                    // Tap 3 (SY1035): the live control driver (foreach source / for|while|if condition) that
                    // steers this factory-time unroll is itself live template code — highlight the pre-rename node.
                    emission.InfoDiagnostics.Add(TemplateDiagnostics.LiveFragment(DriverLocation(region.Control)));
                    segments.Add(StagedHelperCallFactory.RunSegment(runName));
                }
                else if (StagedScaffoldBuilder.IsStagedStatement(semanticModel, statement, stagedSet))
                {
                    // A live sibling of the region (e.g. an accumulator declaration `int sum = 0;`): hoist it
                    // verbatim (root-renamed) into the factory body as real runtime state, not a quoted island.
                    emission.Preamble.Add((StatementSyntax)renamer.Visit(statement)!.WithoutTrivia());
                }
                else
                {
                    // A fixed quoted sibling of the region (e.g. a trailing throw): quote it verbatim and add it
                    // as a single-node segment (implicit TNode -> ListSegment conversion).
                    var fixedNodeSteps = PlanAssembler.BuildNodeSteps(baseReplacements, builderCalls, new HashSet<SyntaxNode>(), null);
                    var fixedListSteps = PlanAssembler.BuildListSteps(stringStagedRoots, null);
                    var fixedQuoter = TemplateQuoterFactory.Create(fixedNodeSteps, fixedListSteps, includeTrivia: false);
                    if (fixedQuoter.Visit(statement) is { } quote)
                        segments.Add(quote);
                }
            }

            emission.ContainerReplacements[container] = StagedHelperCallFactory.BlockReplacement(segments);
        }

        return emission;
    }

    // The live driving expression of a control region — the fragment steering the factory-time unroll. Mirrors the
    // driver each StagedScaffoldBuilder control arm renames: foreach -> iteration source, for|while|if -> condition.
    private static Location DriverLocation(StatementSyntax control) => control switch
    {
        ForEachStatementSyntax forEach => forEach.Expression.GetLocation(),
        ForStatementSyntax { Condition: { } condition } => condition.GetLocation(),
        WhileStatementSyntax whileStatement => whileStatement.Condition.GetLocation(),
        IfStatementSyntax ifStatement => ifStatement.Condition.GetLocation(),
        _ => control.GetLocation(),
    };
}

using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto;

/// <summary>
/// A live control region to unroll at factory-build time (plan Task 6 / spec §5.2–5.3): a <c>foreach</c>
/// whose iteration source is a live root. The region runs verbatim in the factory body and each iteration
/// collects the quote of its body islands; the region's owning <see cref="Container"/> block is replaced by a
/// <c>BuildList</c> of that run plus the quoted fixed siblings.
/// </summary>
internal sealed class LiveRegion
{
    public LiveRegion(ForEachStatementSyntax forEach, BlockSyntax container, ISymbol? loopVariable)
    {
        ForEach = forEach;
        Container = container;
        LoopVariable = loopVariable;
    }

    /// <summary>The live <c>foreach</c> statement (its driver is a live root).</summary>
    public ForEachStatementSyntax ForEach { get; }

    /// <summary>The block that directly owns <see cref="ForEach"/> — the replacement key (spec §5.3).</summary>
    public BlockSyntax Container { get; }

    /// <summary>The loop variable symbol (live within the scaffold; ranges over live values at factory time).</summary>
    public ISymbol? LoopVariable { get; }
}

/// <summary>The product of unrolling the live regions: scaffold preamble + per-container replacements.</summary>
internal sealed class LiveRegionEmission
{
    public List<StatementSyntax> Preamble { get; } = new();
    public Dictionary<SyntaxNode, ExpressionSyntax> ContainerReplacements { get; } = new();
    public List<DiagnosticInfo> Diagnostics { get; } = new();
}

/// <summary>
/// Precomputes the run+collect replacement for each live control region (plan Task 6). The verbatim loop
/// scaffold is hoisted into the factory <c>preamble</c> (collecting quoted islands into a <c>List&lt;StatementSyntax&gt;</c>
/// run), and the region's owning container block is keyed in <c>unquotedReplacements</c> to a
/// <c>Block(BuildList(Run(run), &lt;fixed siblings&gt;...))</c> — so the quoter never descends into the region
/// (the map hit returns the precomputed expression). Each quoted island is produced by a fresh
/// <see cref="TemplateSyntaxQuoter"/> whose map lifts every maximal purely-live subexpression via
/// <c>.ToSyntax()</c>. Runs entirely inside the generator transform; nothing captured into pipeline state.
/// </summary>
internal static class LiveRegionEmitter
{
    /// <summary>The file-local collection helper class name (emitted by the scan-based injection, Task 5).</summary>
    private const string CollectionHelper = "CollectionSyntaxExtensions";

    /// <summary>Discovers the live <c>foreach</c> regions in <paramref name="body"/> using the classifier partition.</summary>
    public static IReadOnlyList<LiveRegion> FindForeachRegions(SemanticModel semanticModel, SyntaxNode body, BindingTimePartition partition)
    {
        var regions = new List<LiveRegion>();
        foreach (var node in body.DescendantNodes())
        {
            if (node is ForEachStatementSyntax forEach
                && partition.IsLiveControl(forEach)
                && forEach.Parent is BlockSyntax container)
            {
                regions.Add(new LiveRegion(forEach, container, semanticModel.GetDeclaredSymbol(forEach)));
            }
        }

        return regions;
    }

    /// <summary>
    /// The set of nodes consumed by a live region (every node inside the region's <c>foreach</c>). A live-root
    /// reference in this set is handled by the verbatim scaffold (which uses the factory parameter directly),
    /// so the caller must NOT also emit a depth-0 <c>.ToSyntax()</c> lift for it.
    /// </summary>
    public static HashSet<SyntaxNode> ComputeConsumedNodes(IReadOnlyList<LiveRegion> regions)
    {
        var consumed = new HashSet<SyntaxNode>();
        foreach (var region in regions)
        {
            foreach (var node in region.ForEach.DescendantNodesAndSelf())
                consumed.Add(node);
        }

        return consumed;
    }

    public static LiveRegionEmission Emit(
        SemanticModel semanticModel,
        IReadOnlyList<LiveRegion> regions,
        IReadOnlyCollection<ISymbol> liveRoots,
        IReadOnlyDictionary<ISymbol, string> rootNames,
        IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> baseReplacements,
        HashSet<SyntaxNode> trimNodes,
        ref int counter)
    {
        var emission = new LiveRegionEmission();

        foreach (var group in regions.GroupBy(r => r.Container))
        {
            var container = group.Key;
            var regionByForEach = new Dictionary<SyntaxNode, LiveRegion>();
            foreach (var region in group)
                regionByForEach[region.ForEach] = region;

            var segments = new List<ExpressionSyntax>();

            foreach (var statement in container.Statements)
            {
                if (trimNodes.Contains(statement))
                    continue;

                if (regionByForEach.TryGetValue(statement, out var region))
                {
                    string runName = "__run_" + counter++;
                    if (!TryBuildScaffold(semanticModel, region, liveRoots, rootNames, baseReplacements, runName, emission))
                        return emission; // a diagnostic was recorded
                    segments.Add(RunSegment(runName));
                }
                else
                {
                    // A fixed quoted sibling of the region (e.g. a trailing throw): quote it verbatim and add it
                    // as a single-node segment (implicit TNode -> ListSegment conversion).
                    var fixedQuoter = new TemplateSyntaxQuoter(semanticModel, baseReplacements, new HashSet<SyntaxNode>(), includeTrivia: false);
                    if (fixedQuoter.Visit(statement) is { } quote)
                        segments.Add(quote);
                }
            }

            emission.ContainerReplacements[container] = BlockReplacement(segments);
        }

        return emission;
    }

    private static bool TryBuildScaffold(
        SemanticModel semanticModel,
        LiveRegion region,
        IReadOnlyCollection<ISymbol> liveRoots,
        IReadOnlyDictionary<ISymbol, string> rootNames,
        IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> baseReplacements,
        string runName,
        LiveRegionEmission emission)
    {
        var runIdentifier = Identifier(runName);

        // var __run_N = new global::System.Collections.Generic.List<...StatementSyntax>();
        emission.Preamble.Add(
            LocalDeclarationStatement(
                VariableDeclaration(
                    IdentifierName("var"),
                    SingletonSeparatedList(
                        VariableDeclarator(runIdentifier)
                            .WithInitializer(EqualsValueClause(
                                ObjectCreationExpression(
                                    ParseTypeName("global::System.Collections.Generic.List<global::Microsoft.CodeAnalysis.CSharp.Syntax.StatementSyntax>"))
                                    .WithArgumentList(ArgumentList())))))));

        // The loop variable is live within the scaffold (it ranges over live values at factory time).
        var liveSet = new HashSet<ISymbol>(liveRoots, SymbolEqualityComparer.Default);
        if (region.LoopVariable is not null)
            liveSet.Add(region.LoopVariable);

        var bodyStatements = region.ForEach.Statement is BlockSyntax block
            ? (IReadOnlyList<StatementSyntax>)block.Statements
            : new[] { region.ForEach.Statement };

        var addStatements = new List<StatementSyntax>();
        foreach (var statement in bodyStatements)
        {
            var liftMap = new Dictionary<SyntaxNode, ExpressionSyntax>();
            foreach (var pair in baseReplacements)
                liftMap[pair.Key] = pair.Value;
            CollectLiftPoints(semanticModel, statement, liveSet, liftMap);

            var islandQuoter = new TemplateSyntaxQuoter(semanticModel, liftMap, new HashSet<SyntaxNode>(), includeTrivia: false);
            if (islandQuoter.Visit(statement) is not { } island)
            {
                emission.Diagnostics.Add(Diagnostics.UnsupportedLiveShape(region.ForEach.GetLocation(), "live loop body could not be quoted"));
                return false;
            }

            // __run_N.Add(<island quote>);
            addStatements.Add(
                ExpressionStatement(
                    InvocationExpression(
                        MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            IdentifierName(runIdentifier),
                            IdentifierName("Add")))
                        .AddArgumentListArguments(Argument(island))));
        }

        // The verbatim foreach, with live-root references rewritten to factory parameter names and the body
        // replaced by the island-collecting adds.
        var source = (ExpressionSyntax)new RootRenameRewriter(semanticModel, rootNames).Visit(region.ForEach.Expression)!;
        emission.Preamble.Add(
            ForEachStatement(
                region.ForEach.Type.WithoutTrivia(),
                region.ForEach.Identifier.WithoutTrivia(),
                source.WithoutTrivia(),
                Block(addStatements)));

        return true;
    }

    /// <summary><c>CollectionSyntaxExtensions.ListSegment&lt;StatementSyntax&gt;.Run(runName)</c>.</summary>
    private static ExpressionSyntax RunSegment(string runName) =>
        InvocationExpression(
            MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName(CollectionHelper),
                    GenericName(Identifier("ListSegment"))
                        .WithTypeArgumentList(TypeArgumentList(SingletonSeparatedList<TypeSyntax>(IdentifierName("StatementSyntax"))))),
                IdentifierName("Run")))
            .AddArgumentListArguments(Argument(IdentifierName(runName)));

    /// <summary><c>Block(CollectionSyntaxExtensions.BuildList&lt;StatementSyntax&gt;(seg0, seg1, ...))</c>.</summary>
    private static ExpressionSyntax BlockReplacement(IReadOnlyList<ExpressionSyntax> segments)
    {
        var buildList = InvocationExpression(
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName(CollectionHelper),
                    GenericName(Identifier("BuildList"))
                        .WithTypeArgumentList(TypeArgumentList(SingletonSeparatedList<TypeSyntax>(IdentifierName("StatementSyntax"))))))
            .WithArgumentList(ArgumentList(SeparatedList(segments.Select(Argument))));

        return InvocationExpression(IdentifierName("Block"))
            .AddArgumentListArguments(Argument(buildList));
    }

    /// <summary>
    /// Records every maximal purely-live subexpression of <paramref name="node"/> as a <c>.ToSyntax()</c> lift
    /// in <paramref name="map"/>. An expression is purely live when it references at least one live symbol and
    /// no output-world local/parameter — so <c>c.Ordinal</c> lifts but <c>i == c.Ordinal</c> (mixing the
    /// output-world <c>i</c>) does not; only its live operand does.
    /// </summary>
    private static void CollectLiftPoints(SemanticModel semanticModel, SyntaxNode node, HashSet<ISymbol> liveSet, Dictionary<SyntaxNode, ExpressionSyntax> map)
    {
        if (node is ExpressionSyntax expression && IsPurelyLive(semanticModel, expression, liveSet))
        {
            map[expression] = ToSyntaxCall(expression);
            return; // maximal — do not descend
        }

        foreach (var child in node.ChildNodes())
            CollectLiftPoints(semanticModel, child, liveSet, map);
    }

    private static bool IsPurelyLive(SemanticModel semanticModel, ExpressionSyntax expression, HashSet<ISymbol> liveSet)
    {
        bool hasLive = false;
        foreach (var identifier in expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            // The member-name part of a member access (e.g. `Ordinal` in `c.Ordinal`) names a member, not a value.
            if (identifier.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == identifier)
                continue;

            var symbol = semanticModel.GetSymbolInfo(identifier).Symbol;
            if (symbol is ILocalSymbol or IParameterSymbol or IRangeVariableSymbol)
            {
                if (liveSet.Contains(symbol))
                    hasLive = true;
                else
                    return false; // an output-world value disqualifies the whole expression
            }
        }

        return hasLive;
    }

    private static ExpressionSyntax ToSyntaxCall(ExpressionSyntax expression)
    {
        ExpressionSyntax target = NeedsParentheses(expression)
            ? ParenthesizedExpression(expression.WithoutTrivia())
            : expression.WithoutTrivia();

        return InvocationExpression(
            MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                target,
                IdentifierName("ToSyntax")));
    }

    private static bool NeedsParentheses(ExpressionSyntax expression) =>
        expression is not (IdentifierNameSyntax or MemberAccessExpressionSyntax or InvocationExpressionSyntax
            or ElementAccessExpressionSyntax or ParenthesizedExpressionSyntax or LiteralExpressionSyntax);

    /// <summary>Rewrites live-root identifier references to their factory parameter names (identity when unchanged).</summary>
    private sealed class RootRenameRewriter : CSharpSyntaxRewriter
    {
        private readonly SemanticModel _semanticModel;
        private readonly IReadOnlyDictionary<ISymbol, string> _rootNames;

        public RootRenameRewriter(SemanticModel semanticModel, IReadOnlyDictionary<ISymbol, string> rootNames)
        {
            _semanticModel = semanticModel;
            _rootNames = rootNames;
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            if (node.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == node)
                return base.VisitIdentifierName(node);

            var symbol = _semanticModel.GetSymbolInfo(node).Symbol;
            if (symbol is not null && _rootNames.TryGetValue(symbol, out var name))
                return IdentifierName(name);

            return base.VisitIdentifierName(node);
        }
    }
}

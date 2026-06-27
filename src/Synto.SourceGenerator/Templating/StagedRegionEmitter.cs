using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto;

/// <summary>
/// A live control region to unroll at factory-build time (plan Task 6–7 / spec §5.2–5.3): a control statement
/// (<c>foreach</c>/<c>for</c>/<c>while</c>/<c>if</c>) whose driving expression is live. The region runs verbatim
/// in the factory body — keeping live locals/accumulators as real runtime state — while each body island
/// collects the quote of its statements; the region's owning <see cref="Container"/> block is replaced by a
/// <c>BuildList</c> of that run plus the quoted fixed siblings.
/// </summary>
internal sealed class StagedRegion
{
    public StagedRegion(StatementSyntax control, BlockSyntax container, IReadOnlyList<ISymbol> loopVariables)
    {
        Control = control;
        Container = container;
        LoopVariables = loopVariables;
    }

    /// <summary>The live control statement (its driver — iteration source / condition — is a live expression).</summary>
    public StatementSyntax Control { get; }

    /// <summary>The block that directly owns <see cref="Control"/> — the replacement key (spec §5.3).</summary>
    public BlockSyntax Container { get; }

    /// <summary>
    /// The loop variables introduced by the control statement (the <c>foreach</c> variable, the <c>for</c>
    /// declarators) — live within the scaffold (they range over live values at factory time). Empty for
    /// <c>while</c>/<c>if</c>.
    /// </summary>
    public IReadOnlyList<ISymbol> LoopVariables { get; }
}

/// <summary>The product of unrolling the live regions: scaffold preamble + per-container replacements.</summary>
internal sealed class StagedRegionEmission
{
    public List<StatementSyntax> Preamble { get; } = new();
    public Dictionary<SyntaxNode, ExpressionSyntax> ContainerReplacements { get; } = new();
    public List<DiagnosticInfo> Diagnostics { get; } = new();
}

/// <summary>
/// Precomputes the run+collect replacement for each live control region (plan Task 6–7). The verbatim control
/// scaffold is hoisted into the factory <c>preamble</c> (collecting quoted islands into a
/// <c>List&lt;StatementSyntax&gt;</c> run, and keeping live locals/accumulators as runtime state), and the
/// region's owning container block is keyed in <c>unquotedReplacements</c> to a
/// <c>Block(BuildList(Run(run), &lt;fixed siblings&gt;...))</c> — so the quoter never descends into the region
/// (the map hit returns the precomputed expression). Each quoted island is produced by a fresh
/// <see cref="TemplateSyntaxQuoter"/> whose map lifts every maximal purely-live subexpression via
/// <c>.ToSyntax()</c>. Runs entirely inside the generator transform; nothing captured into pipeline state.
/// </summary>
internal static class StagedRegionEmitter
{
    /// <summary>
    /// Emitted helper names, taken from the Synto.Core symbols via <c>nameof</c> so a rename of a helper fails at
    /// generator compile time instead of silently producing a non-compiling factory (the runtime helpers are
    /// authored in Synto.Core and injected <c>file static</c> by the scan-based injection, Task 5).
    /// </summary>
    private const string CollectionHelper = nameof(CollectionSyntaxExtensions);
    private const string BuildListMethod = nameof(CollectionSyntaxExtensions.BuildList);
    private const string ListSegmentType = nameof(CollectionSyntaxExtensions.ListSegment<StatementSyntax>);
    private const string RunMethod = nameof(CollectionSyntaxExtensions.ListSegment<StatementSyntax>.Run);
    private const string ToSyntaxMethod = nameof(LiteralSyntaxExtensions.ToSyntax);

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

    public static StagedRegionEmission Emit(
        SemanticModel semanticModel,
        BindingTimePartition partition,
        IReadOnlyList<StagedRegion> regions,
        IReadOnlyCollection<ISymbol> baseStaged,
        IReadOnlyDictionary<ISymbol, string> rootNames,
        IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> baseReplacements,
        HashSet<SyntaxNode> trimNodes,
        ref int counter)
    {
        var emission = new StagedRegionEmission();
        var stagedSet = ComputeStagedSet(semanticModel, regions, baseStaged);
        var renamer = new RootRenameRewriter(semanticModel, rootNames);

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
                    if (!TryBuildScaffold(semanticModel, partition, region, stagedSet, renamer, baseReplacements, runName, emission))
                        return emission; // a diagnostic was recorded
                    segments.Add(RunSegment(runName));
                }
                else if (IsStagedStatement(semanticModel, statement, stagedSet))
                {
                    // A live sibling of the region (e.g. an accumulator declaration `int sum = 0;`): hoist it
                    // verbatim (root-renamed) into the factory body as real runtime state, not a quoted island.
                    emission.Preamble.Add((StatementSyntax)renamer.Visit(statement)!.WithoutTrivia());
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
        BindingTimePartition partition,
        StagedRegion region,
        HashSet<ISymbol> stagedSet,
        RootRenameRewriter renamer,
        IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> baseReplacements,
        string runName,
        StagedRegionEmission emission)
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

        if (!TryBuildControl(semanticModel, partition, region.Control, stagedSet, renamer, baseReplacements, runIdentifier, emission, out var scaffold))
            return false;

        emission.Preamble.Add(scaffold!);
        return true;
    }

    /// <summary>
    /// Builds the verbatim control scaffold: the control statement with its live-root references renamed to
    /// factory parameters and its body replaced by the island-collecting / live-verbatim block. An <c>if</c> is
    /// branch-specialized (both branches append to the SAME run; exactly one runs at factory time).
    /// </summary>
    private static bool TryBuildControl(
        SemanticModel semanticModel,
        BindingTimePartition partition,
        StatementSyntax control,
        HashSet<ISymbol> stagedSet,
        RootRenameRewriter renamer,
        IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> baseReplacements,
        SyntaxToken runIdentifier,
        StagedRegionEmission emission,
        out StatementSyntax? scaffold)
    {
        scaffold = null;

        switch (control)
        {
            case ForEachStatementSyntax forEach:
                {
                    if (!TryBuildRegionBody(semanticModel, partition, forEach.Statement, stagedSet, renamer, baseReplacements, runIdentifier, emission, out var body))
                        return false;
                    var source = (ExpressionSyntax)renamer.Visit(forEach.Expression)!;
                    scaffold = ForEachStatement(forEach.Type.WithoutTrivia(), forEach.Identifier.WithoutTrivia(), source.WithoutTrivia(), body!);
                    return true;
                }

            case ForStatementSyntax forStatement:
                {
                    if (!TryBuildRegionBody(semanticModel, partition, forStatement.Statement, stagedSet, renamer, baseReplacements, runIdentifier, emission, out var body))
                        return false;

                    var result = ForStatement(body!)
                        .WithInitializers(SeparatedList(forStatement.Initializers.Select(i => (ExpressionSyntax)renamer.Visit(i)!.WithoutTrivia())))
                        .WithCondition(forStatement.Condition is { } condition ? (ExpressionSyntax)renamer.Visit(condition)!.WithoutTrivia() : null)
                        .WithIncrementors(SeparatedList(forStatement.Incrementors.Select(i => (ExpressionSyntax)renamer.Visit(i)!.WithoutTrivia())));
                    if (forStatement.Declaration is { } declaration)
                        result = result.WithDeclaration((VariableDeclarationSyntax)renamer.Visit(declaration)!.WithoutTrivia());
                    scaffold = result;
                    return true;
                }

            case WhileStatementSyntax whileStatement:
                {
                    if (!TryBuildRegionBody(semanticModel, partition, whileStatement.Statement, stagedSet, renamer, baseReplacements, runIdentifier, emission, out var body))
                        return false;
                    scaffold = WhileStatement((ExpressionSyntax)renamer.Visit(whileStatement.Condition)!.WithoutTrivia(), body!);
                    return true;
                }

            case IfStatementSyntax ifStatement:
                return TryBuildIf(semanticModel, partition, ifStatement, stagedSet, renamer, baseReplacements, runIdentifier, emission, out scaffold);
        }

        emission.Diagnostics.Add(Diagnostics.UnsupportedStagedShape(control.GetLocation(), "unsupported staged control shape"));
        return false;
    }

    private static bool TryBuildIf(
        SemanticModel semanticModel,
        BindingTimePartition partition,
        IfStatementSyntax ifStatement,
        HashSet<ISymbol> stagedSet,
        RootRenameRewriter renamer,
        IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> baseReplacements,
        SyntaxToken runIdentifier,
        StagedRegionEmission emission,
        out StatementSyntax? scaffold)
    {
        scaffold = null;

        if (!TryBuildRegionBody(semanticModel, partition, ifStatement.Statement, stagedSet, renamer, baseReplacements, runIdentifier, emission, out var thenBody))
            return false;

        var condition = (ExpressionSyntax)renamer.Visit(ifStatement.Condition)!.WithoutTrivia();
        var result = IfStatement(condition, thenBody!);

        if (ifStatement.Else is { } elseClause)
        {
            // `else if` chains recurse so the whole chain specializes at factory time onto the same run.
            if (elseClause.Statement is IfStatementSyntax elseIf)
            {
                if (!TryBuildIf(semanticModel, partition, elseIf, stagedSet, renamer, baseReplacements, runIdentifier, emission, out var elseIfScaffold))
                    return false;
                result = result.WithElse(ElseClause(elseIfScaffold!));
            }
            else
            {
                if (!TryBuildRegionBody(semanticModel, partition, elseClause.Statement, stagedSet, renamer, baseReplacements, runIdentifier, emission, out var elseBody))
                    return false;
                result = result.WithElse(ElseClause(elseBody!));
            }
        }

        scaffold = result;
        return true;
    }

    /// <summary>
    /// Partitions a region body into live code (verbatim, root-renamed runtime statements — declarations of live
    /// locals and mutations of live accumulators) and quoted islands (each becomes <c>__run.Add(&lt;quote&gt;)</c>).
    /// A nested live-control statement is not supported in v1 → <c>SY1014</c> (rather than a silent mis-expansion).
    /// </summary>
    private static bool TryBuildRegionBody(
        SemanticModel semanticModel,
        BindingTimePartition partition,
        StatementSyntax body,
        HashSet<ISymbol> stagedSet,
        RootRenameRewriter renamer,
        IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> baseReplacements,
        SyntaxToken runIdentifier,
        StagedRegionEmission emission,
        out BlockSyntax? block)
    {
        block = null;
        var statements = body is BlockSyntax blockBody
            ? (IReadOnlyList<StatementSyntax>)blockBody.Statements
            : new[] { body };

        var result = new List<StatementSyntax>();
        foreach (var statement in statements)
        {
            // A nested live-control region inside this body would be mis-expanded (quoted verbatim instead of
            // unrolled) — degrade to a diagnostic instead.
            if (partition.IsStagedControl(statement) || statement.DescendantNodes().Any(partition.IsStagedControl))
            {
                emission.Diagnostics.Add(Diagnostics.UnsupportedStagedShape(statement.GetLocation(), "nested staged control region is not supported in v1"));
                return false;
            }

            if (IsStagedStatement(semanticModel, statement, stagedSet))
            {
                result.Add((StatementSyntax)renamer.Visit(statement)!.WithoutTrivia());
                continue;
            }

            var liftMap = new Dictionary<SyntaxNode, ExpressionSyntax>();
            foreach (var pair in baseReplacements)
                liftMap[pair.Key] = pair.Value;
            CollectLiftPoints(semanticModel, statement, stagedSet, renamer, liftMap);

            var islandQuoter = new TemplateSyntaxQuoter(semanticModel, liftMap, new HashSet<SyntaxNode>(), includeTrivia: false);
            if (islandQuoter.Visit(statement) is not { } island)
            {
                emission.Diagnostics.Add(Diagnostics.UnsupportedStagedShape(body.GetLocation(), "staged region body could not be quoted"));
                return false;
            }

            // __run_N.Add(<island quote>);
            result.Add(
                ExpressionStatement(
                    InvocationExpression(
                        MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            IdentifierName(runIdentifier),
                            IdentifierName("Add")))
                        .AddArgumentListArguments(Argument(island))));
        }

        block = Block(result);
        return true;
    }

    /// <summary>
    /// True if <paramref name="statement"/> is live runtime code that must stay verbatim in the scaffold — a
    /// declaration of a live local, or a mutation (assignment / increment / decrement) of a live local — rather
    /// than a quoted output-world island.
    /// </summary>
    private static bool IsStagedStatement(SemanticModel semanticModel, StatementSyntax statement, HashSet<ISymbol> stagedSet)
    {
        switch (statement)
        {
            case LocalDeclarationStatementSyntax local:
                foreach (var declarator in local.Declaration.Variables)
                    if (semanticModel.GetDeclaredSymbol(declarator) is { } symbol && stagedSet.Contains(symbol))
                        return true;
                return false;

            case ExpressionStatementSyntax expressionStatement:
                return IsStagedMutation(semanticModel, expressionStatement.Expression, stagedSet);
        }

        return false;
    }

    private static bool IsStagedMutation(SemanticModel semanticModel, ExpressionSyntax expression, HashSet<ISymbol> stagedSet)
    {
        ExpressionSyntax? target = expression switch
        {
            AssignmentExpressionSyntax assignment => assignment.Left,
            PostfixUnaryExpressionSyntax postfix => postfix.Operand,
            PrefixUnaryExpressionSyntax prefix when prefix.IsKind(SyntaxKind.PreIncrementExpression) || prefix.IsKind(SyntaxKind.PreDecrementExpression) => prefix.Operand,
            _ => null,
        };

        if (target is null)
            return false;

        var symbol = semanticModel.GetSymbolInfo(target).Symbol;
        return symbol is not null && stagedSet.Contains(symbol);
    }

    private static bool ReferencesStaged(SemanticModel semanticModel, SyntaxNode node, HashSet<ISymbol> stagedSet)
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

    /// <summary><c>CollectionSyntaxExtensions.ListSegment&lt;StatementSyntax&gt;.Run(runName)</c>.</summary>
    private static ExpressionSyntax RunSegment(string runName) =>
        InvocationExpression(
            MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName(CollectionHelper),
                    GenericName(Identifier(ListSegmentType))
                        .WithTypeArgumentList(TypeArgumentList(SingletonSeparatedList<TypeSyntax>(IdentifierName("StatementSyntax"))))),
                IdentifierName(RunMethod)))
            .AddArgumentListArguments(Argument(IdentifierName(runName)));

    /// <summary><c>Block(CollectionSyntaxExtensions.BuildList&lt;StatementSyntax&gt;(seg0, seg1, ...))</c>.</summary>
    private static ExpressionSyntax BlockReplacement(IReadOnlyList<ExpressionSyntax> segments)
    {
        var buildList = InvocationExpression(
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName(CollectionHelper),
                    GenericName(Identifier(BuildListMethod))
                        .WithTypeArgumentList(TypeArgumentList(SingletonSeparatedList<TypeSyntax>(IdentifierName("StatementSyntax"))))))
            .WithArgumentList(ArgumentList(SeparatedList(segments.Select(Argument))));

        return InvocationExpression(IdentifierName("Block"))
            .AddArgumentListArguments(Argument(buildList));
    }

    /// <summary>
    /// Records every maximal purely-live subexpression of <paramref name="node"/> as a <c>.ToSyntax()</c> lift
    /// in <paramref name="map"/>. An expression is purely live when it references at least one live symbol and
    /// no output-world local/parameter — so <c>c.Ordinal</c> lifts but <c>i == c.Ordinal</c> (mixing the
    /// output-world <c>i</c>) does not; only its live operand does. The lift VALUE is root-renamed (the map key
    /// stays the original node so the quoter still matches it) so a live root referenced by value inside the
    /// island resolves to its factory parameter, not the trimmed source local.
    /// </summary>
    private static void CollectLiftPoints(SemanticModel semanticModel, SyntaxNode node, HashSet<ISymbol> stagedSet, RootRenameRewriter renamer, Dictionary<SyntaxNode, ExpressionSyntax> map)
    {
        if (node is ExpressionSyntax expression && IsPurelyStaged(semanticModel, expression, stagedSet))
        {
            map[expression] = ToSyntaxCall((ExpressionSyntax)renamer.Visit(expression)!);
            return; // maximal — do not descend
        }

        foreach (var child in node.ChildNodes())
            CollectLiftPoints(semanticModel, child, stagedSet, renamer, map);
    }

    private static bool IsPurelyStaged(SemanticModel semanticModel, ExpressionSyntax expression, HashSet<ISymbol> stagedSet)
    {
        // An invocation / object creation is output-world CODE that must be emitted as syntax (e.g.
        // `System.Console.WriteLine(k)` is quoted, with only its live operand `k` lifted) — never collapsed
        // to a literal — even when all its operands are live. Force descent so the live operands lift instead.
        if (expression.DescendantNodesAndSelf().Any(n => n is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax))
            return false;

        bool hasStaged = false;
        foreach (var identifier in expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            // The member-name part of a member access (e.g. `Ordinal` in `c.Ordinal`) names a member, not a value.
            if (identifier.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == identifier)
                continue;

            var symbol = semanticModel.GetSymbolInfo(identifier).Symbol;
            if (symbol is ILocalSymbol or IParameterSymbol or IRangeVariableSymbol)
            {
                if (stagedSet.Contains(symbol))
                    hasStaged = true;
                else
                    return false; // an output-world value disqualifies the whole expression
            }
        }

        return hasStaged;
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
                IdentifierName(ToSyntaxMethod)));
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

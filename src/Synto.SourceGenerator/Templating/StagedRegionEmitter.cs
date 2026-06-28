using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto;

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
    public static StagedRegionEmission Emit(
        SemanticModel semanticModel,
        BindingTimePartition partition,
        IReadOnlyList<StagedRegion> regions,
        IReadOnlyCollection<ISymbol> baseStaged,
        IReadOnlyDictionary<ISymbol, string> rootNames,
        IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> baseReplacements,
        HashSet<SyntaxNode> trimNodes,
        IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> stringStagedRoots,
        ref int counter)
    {
        var emission = new StagedRegionEmission();
        var stagedSet = StagedLivenessAnalysis.ComputeStagedSet(semanticModel, regions, baseStaged);
        var renamer = new RootRenameRewriter(semanticModel, rootNames);
        var context = new StagedEmitContext(semanticModel, partition, stagedSet, renamer, baseReplacements, stringStagedRoots, emission);

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
                    if (!TryBuildScaffold(context, region, runName))
                        return emission; // a diagnostic was recorded
                    segments.Add(StagedHelperCallFactory.RunSegment(runName));
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
                    var fixedQuoter = new TemplateSyntaxQuoter(semanticModel, baseReplacements, new HashSet<SyntaxNode>(), includeTrivia: false, stringStagedRoots: stringStagedRoots);
                    if (fixedQuoter.Visit(statement) is { } quote)
                        segments.Add(quote);
                }
            }

            emission.ContainerReplacements[container] = StagedHelperCallFactory.BlockReplacement(segments);
        }

        return emission;
    }

    private static bool TryBuildScaffold(StagedEmitContext context, StagedRegion region, string runName)
    {
        var runIdentifier = Identifier(runName);
        context.RunIdentifier = runIdentifier;

        // var __run_N = new global::System.Collections.Generic.List<...StatementSyntax>();
        context.Emission.Preamble.Add(
            LocalDeclarationStatement(
                VariableDeclaration(
                    IdentifierName("var"),
                    SingletonSeparatedList(
                        VariableDeclarator(runIdentifier)
                            .WithInitializer(EqualsValueClause(
                                ObjectCreationExpression(
                                    ParseTypeName("global::System.Collections.Generic.List<global::Microsoft.CodeAnalysis.CSharp.Syntax.StatementSyntax>"))
                                    .WithArgumentList(ArgumentList())))))));

        if (!TryBuildControl(context, region.Control, out var scaffold))
            return false;

        context.Emission.Preamble.Add(scaffold!);
        return true;
    }

    /// <summary>
    /// Builds the verbatim control scaffold: the control statement with its live-root references renamed to
    /// factory parameters and its body replaced by the island-collecting / live-verbatim block. An <c>if</c> is
    /// branch-specialized (both branches append to the SAME run; exactly one runs at factory time).
    /// </summary>
    private static bool TryBuildControl(StagedEmitContext context, StatementSyntax control, out StatementSyntax? scaffold)
    {
        scaffold = null;
        var renamer = context.Renamer;

        switch (control)
        {
            case ForEachStatementSyntax forEach:
                {
                    if (!TryBuildRegionBody(context, forEach.Statement, out var body))
                        return false;
                    var source = (ExpressionSyntax)renamer.Visit(forEach.Expression)!;
                    scaffold = ForEachStatement(forEach.Type.WithoutTrivia(), forEach.Identifier.WithoutTrivia(), source.WithoutTrivia(), body!);
                    return true;
                }

            case ForStatementSyntax forStatement:
                {
                    if (!TryBuildRegionBody(context, forStatement.Statement, out var body))
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
                    if (!TryBuildRegionBody(context, whileStatement.Statement, out var body))
                        return false;
                    scaffold = WhileStatement((ExpressionSyntax)renamer.Visit(whileStatement.Condition)!.WithoutTrivia(), body!);
                    return true;
                }

            case IfStatementSyntax ifStatement:
                return TryBuildIf(context, ifStatement, out scaffold);
        }

        context.Emission.Diagnostics.Add(TemplateDiagnostics.UnsupportedStagedShape(control.GetLocation(), "unsupported staged control shape"));
        return false;
    }

    private static bool TryBuildIf(StagedEmitContext context, IfStatementSyntax ifStatement, out StatementSyntax? scaffold)
    {
        scaffold = null;

        if (!TryBuildRegionBody(context, ifStatement.Statement, out var thenBody))
            return false;

        var condition = (ExpressionSyntax)context.Renamer.Visit(ifStatement.Condition)!.WithoutTrivia();
        var result = IfStatement(condition, thenBody!);

        if (ifStatement.Else is { } elseClause)
        {
            // `else if` chains recurse so the whole chain specializes at factory time onto the same run.
            if (elseClause.Statement is IfStatementSyntax elseIf)
            {
                if (!TryBuildIf(context, elseIf, out var elseIfScaffold))
                    return false;
                result = result.WithElse(ElseClause(elseIfScaffold!));
            }
            else
            {
                if (!TryBuildRegionBody(context, elseClause.Statement, out var elseBody))
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
    private static bool TryBuildRegionBody(StagedEmitContext context, StatementSyntax body, out BlockSyntax? block)
    {
        block = null;
        var semanticModel = context.SemanticModel;
        var partition = context.Partition;
        var stagedSet = context.StagedSet;
        var renamer = context.Renamer;
        var emission = context.Emission;
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
                emission.Diagnostics.Add(TemplateDiagnostics.UnsupportedStagedShape(statement.GetLocation(), "nested staged control region is not supported in v1"));
                return false;
            }

            if (IsStagedStatement(semanticModel, statement, stagedSet))
            {
                result.Add((StatementSyntax)renamer.Visit(statement)!.WithoutTrivia());
                continue;
            }

            var liftMap = new Dictionary<SyntaxNode, ExpressionSyntax>();
            foreach (var pair in context.BaseReplacements)
                liftMap[pair.Key] = pair.Value;
            CollectLiftPoints(semanticModel, statement, stagedSet, renamer, liftMap);

            var islandQuoter = new TemplateSyntaxQuoter(semanticModel, liftMap, new HashSet<SyntaxNode>(), includeTrivia: false, stringStagedRoots: context.StringStagedRoots);
            if (islandQuoter.Visit(statement) is not { } island)
            {
                emission.Diagnostics.Add(TemplateDiagnostics.UnsupportedStagedShape(body.GetLocation(), "staged region body could not be quoted"));
                return false;
            }

            // __run_N.Add(<island quote>);
            result.Add(
                ExpressionStatement(
                    InvocationExpression(
                        MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            IdentifierName(context.RunIdentifier),
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
            map[expression] = StagedHelperCallFactory.ToSyntaxCall((ExpressionSyntax)renamer.Visit(expression)!);
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
}

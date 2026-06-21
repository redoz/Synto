using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.Formatting;
using Synto.Matching;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto;

/// <summary>
/// Lowers a validated <see cref="MatchInfo"/> into the generated matcher source via a generic structural
/// walk over <c>ChildNodesAndTokens()</c> (no per-<see cref="SyntaxKind"/> switch). Two trees are structurally
/// equal (trivia-insensitive) iff they share the same <c>RawKind</c>, the same child count, equal token
/// texts, and structurally-equal child nodes; the walk unrolls this over the known pattern tree, capturing at
/// holes. Returning <see langword="null"/> means "nothing emitted" — the pipeline then flows only the
/// (possibly empty) diagnostics, exactly like Templating's diagnostics-only bail.
/// </summary>
internal static class MatchEmitter
{
    public static (string FileName, string Source)? Emit(List<DiagnosticInfo> diagnostics, MatchInfo info)
    {
        var markers = MatchMarkers.Create(info);
        var ctx = new MatchContext(info, markers);
        var body = new List<StatementSyntax>();

        // Pre-scan for the deferred repetition path: a phantom foreach iterating a [Capture] param needs the
        // v2 backtracking lowering -> SY1203, no tree. Reuses the already-built ctx.Markers (no per-foreach rebuild).
        ScanForDeferredForeach(info, ctx);

        if (ctx.Aborted)
        {
            diagnostics.AddRange(ctx.Diagnostics);
            return null;
        }

        if (MatchMarkers.IsExpressionBodied(info.PatternSyntax, out var expression))
        {
            // Expression-Single (arrow body). Root the walk on the handed `node`; the walk captures expression
            // holes into ctx.Captures, then we return the record built from the captured locals in SIGNATURE
            // order. A None/Bare option on an expression body is an option×body-shape misuse -> SY1205.
            if (info.Option == MatchOption.Single)
            {
                EmitNodeMatch(body, "node", expression, ctx);
                body.Add(ParseStatement($"return new {info.Name}Match({RenderCaptureArguments(ctx)});"));
            }
            else
            {
                ctx.Diagnostics.Add(MatchDiagnostics.MalformedPatternBody(
                    info.AttributeSyntax.GetLocation(),
                    $"A '{info.Option}' pattern requires a block body, not an expression body"));
                ctx.Aborted = true;
            }
        }
        else if (MatchMarkers.TryGetBlockBody(info.PatternSyntax, out var block))
        {
            // Block body. Extract anchors FIRST (ExtractAnchors ordering), so the count-based shape check below
            // counts only the CORE (post-anchor) statements — never a silent default arm. ExtractAnchors may also
            // raise SY1202 (provable anchor contradiction) and abort, in which case we emit nothing.
            var (core, anchorStart, anchorEnd, anchorLocations) = ExtractAnchors(block.Statements, ctx);

            if (ctx.Aborted)
            {
                // SY1202 raised during extraction — diagnostics-only, no tree.
            }
            else if (info.Option == MatchOption.None)
            {
                // None is fully bounded by its own braces, so anchors are a usage error -> SY1201 (each).
                if (anchorStart || anchorEnd)
                {
                    foreach (var location in anchorLocations)
                        ctx.Diagnostics.Add(MatchDiagnostics.AnchorNotAllowed(location));
                    ctx.Aborted = true;
                }
                else
                {
                    var run = BuildRun(core, ctx);
                    EmitDeclarationFullBody(body, core, run, ctx);
                }
            }
            else if (info.Option == MatchOption.Single && core.Count == 1)
            {
                // Statement-Single: root on the candidate block, align the one-element core with the anchor flags.
                var run = BuildRun(core, ctx);
                body.Add(ParseStatement("if (node is not BlockSyntax _blk) return null;"));
                EmitAnchoredRun(body, core, run, ctx, anchorStart, anchorEnd);
            }
            else if (info.Option == MatchOption.Bare)
            {
                // Bare: root on the candidate block, align the run contained in it with the anchor flags.
                var run = BuildRun(core, ctx);
                body.Add(ParseStatement("if (node is not BlockSyntax _blk) return null;"));
                EmitAnchoredRun(body, core, run, ctx, anchorStart, anchorEnd);
            }
            else
            {
                // Single on a multi-statement core -> SY1205 (option×body-shape misuse).
                ctx.Diagnostics.Add(MatchDiagnostics.MalformedPatternBody(
                    info.AttributeSyntax.GetLocation(),
                    $"A 'Single' pattern requires exactly one core statement, but the body has {core.Count}"));
                ctx.Aborted = true;
            }
        }

        // The abort/merge, wired here at MatchContext's introduction: emitter-raised diagnostics always flow
        // out, and an aborted/empty body yields a diagnostics-only emit (no tree). Later tasks only SET
        // ctx.Diagnostics / ctx.Aborted.
        diagnostics.AddRange(ctx.Diagnostics);
        if (ctx.Aborted || body.Count == 0)
            return null;

        return Compose(info, ctx, body);
    }

    /// <summary>
    /// Emits the structural guards for one node position against the runtime candidate <paramref name="accessor"/>
    /// and recurses child-by-child. Guards both the .NET type (binding a typed local for child navigation) and
    /// the <c>RawKind</c>, then the child count, then each child: a node child recurses through the
    /// <c>.AsNode()</c> projection, a token child compares kind + text.
    /// </summary>
    private static void EmitNodeMatch(List<StatementSyntax> body, string accessor, SyntaxNode pattern, MatchContext ctx)
    {
        // Hole dispatch: a node that binds to a [Capture] parameter is a capture position, not literal syntax
        // to match. Narrowing + binding happens AT the hole (the accessor-type contract), so a captured node
        // binds straight into its typed record member even when {accessor} is statically SyntaxNode.
        if (ctx.Markers.TryGetCapture(pattern, out var capture))
        {
            EmitCapture(body, accessor, capture, ctx);
            return;
        }

        // Expression wildcard Expr.Any<T>(): assert the candidate is an expression, capture nothing, and do
        // NOT recurse into the marker invocation's children.
        if (ctx.Markers.IsExpressionWildcard(pattern))
        {
            body.Add(ParseStatement($"if ({accessor} is not ExpressionSyntax) return null;"));
            return;
        }

        // Embedded statement hole: a [Capture] Stmt .One() / Statement.One() sitting as a single embedded
        // statement (e.g. the body of `if (cond) only.One();`) captures / matches that one statement. Only the
        // fixed single (One) is meaningful in a single embedded slot; a variable-length quantifier there can't
        // expand to one slot -> SY1204 (quantifier-placement, arm ii).
        if (pattern is StatementSyntax statement && ctx.Markers.TryGetStatementHole(statement, out var statementHole))
        {
            if (statementHole.Quantifier == StatementQuantifier.One)
            {
                EmitStatementCapture(body, accessor, statementHole, ctx);
                return;
            }

            if (statementHole.IsVariableLength)
            {
                ctx.Diagnostics.Add(MatchDiagnostics.QuantifierPlacementUnsupported(
                    statement.GetLocation(),
                    "a variable-length quantifier (Some/All/Opt) is not allowed in a single-statement slot"));
                ctx.Aborted = true;
                return;
            }
        }

        string local = ctx.NextTmp();
        string typeName = pattern.GetType().Name;

        // Type guard (binds a temp for child navigation) + RawKind guard. The .NET type alone over-accepts
        // kinds sharing a type (e.g. the several BinaryExpressionSyntax kinds), so the kind guard is required.
        body.Add(ParseStatement($"if ({accessor} is not {typeName} {local}) return null;"));
        body.Add(ParseStatement($"if (!{local}.IsKind(SyntaxKind.{pattern.Kind()})) return null;"));

        var children = pattern.ChildNodesAndTokens();

        // Child-count guard: the "same child count" clause of structural equality, which also bounds the child
        // indices and rejects a superset candidate (an optional child present in the candidate but omitted in
        // the pattern).
        body.Add(ParseStatement($"if ({local}.ChildNodesAndTokens().Count != {children.Count}) return null;"));

        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i];
            if (child.IsNode)
            {
                // Node-child boundary: pass the `.AsNode()` accessor (static type SyntaxNode) unchanged.
                EmitNodeMatch(body, $"{local}.ChildNodesAndTokens()[{i}].AsNode()", child.AsNode()!, ctx);
            }
            else
            {
                var token = child.AsToken();
                string tok = ctx.NextTmp();
                body.Add(ParseStatement($"var {tok} = {local}.ChildNodesAndTokens()[{i}];"));
                body.Add(ParseStatement($"if (!{tok}.IsKind(SyntaxKind.{token.Kind()})) return null;"));
                body.Add(ParseStatement($"if ({tok}.AsToken().Text != {SymbolDisplay.FormatLiteral(token.Text, quote: true)}) return null;"));
            }
        }
    }

    /// <summary>
    /// Emits an expression-capture hole: narrow + bind the candidate into a <c>cap_{name}</c> local at the
    /// hole (so it binds straight into the typed record member regardless of the accessor's static type), and
    /// record the capture carrying <c>Ordinal = param.Ordinal</c> for signature-order record members.
    /// </summary>
    /// <remarks>
    /// Non-linear equality (Task 8): a REUSED capture adds no new member — at a site whose local is already
    /// bound, narrow a UNIQUE temp (so 3+ reuse sites don't re-declare a fixed name → CS0128) and require it
    /// <c>IsEquivalentTo</c> the first-site local. The first site binds the typed <c>cap_{name}</c> local and
    /// records the member.
    /// </remarks>
    private static void EmitCapture(List<StatementSyntax> body, string accessor, CaptureParameter capture, MatchContext ctx)
    {
        string local = "cap_" + capture.ParameterName;

        if (ctx.BoundCaptureLocals.Contains(local))
        {
            // Reuse site: narrow into a unique temp (enforces the same kind as the first site) and require
            // structural equality with the first-site capture. No new member.
            string temp = ctx.NextTmp();
            body.Add(ParseStatement($"if ({accessor} is not {capture.MemberType} {temp}) return null;"));
            // topLevel:false is load-bearing — the single-arg IsEquivalentTo defaults to topLevel:true
            // (signature-only / trivia-sensitive on a sub-expression), which rejects structurally-equal sides.
            body.Add(ParseStatement($"if (!{temp}.IsEquivalentTo({local}, topLevel: false)) return null;"));
            return;
        }

        body.Add(ParseStatement($"if ({accessor} is not {capture.MemberType} {local}) return null;"));

        ctx.BoundCaptureLocals.Add(local);
        ctx.Captures.Add(new Capture(capture.Ordinal, capture.MemberName, capture.MemberType, local));
    }

    /// <summary>
    /// Emits a statement-hole guard at <paramref name="accessor"/>, narrowing AT the hole (the accessor-type
    /// contract: never <c>var {local} = {accessor};</c> into a typed member). Task 10 handles the fixed single
    /// (<c>One</c>): a wildcard asserts <c>is StatementSyntax</c> (no capture); a capture binds a
    /// <c>StatementSyntax cap_{name}</c> local and records the member. Tasks 11+ add the variable quantifiers.
    /// </summary>
    private static void EmitStatementCapture(List<StatementSyntax> body, string accessor, StatementHole hole, MatchContext ctx)
    {
        if (hole.Kind == StatementHoleKind.Wildcard)
        {
            body.Add(ParseStatement($"if ({accessor} is not StatementSyntax) return null;"));
            return;
        }

        string local = "cap_" + hole.ParameterName;
        body.Add(ParseStatement($"if ({accessor} is not StatementSyntax {local}) return null;"));
        ctx.BoundCaptureLocals.Add(local);
        ctx.Captures.Add(new Capture(hole.Ordinal, hole.MemberName, "StatementSyntax", local));
    }

    /// <summary>
    /// The captured-local argument list for the result-record constructor, in SIGNATURE order (by
    /// <see cref="Capture.Ordinal"/>) — the same order <see cref="Compose"/> sorts the record members by, so
    /// positional construction lines up with the positional record regardless of walk order.
    /// </summary>
    private static string RenderCaptureArguments(MatchContext ctx) =>
        string.Join(", ", ctx.Captures.OrderBy(capture => capture.Ordinal).Select(capture => capture.LocalName));

    /// <summary>
    /// Classifies each core statement into a <see cref="RunElement"/>: a direct statement hole becomes a
    /// <see cref="HoleElement"/> (carrying its <c>Location</c> for SY1204), everything else a
    /// <see cref="LiteralElement"/> matched structurally (its own embedded holes are reached by the walk).
    /// </summary>
    private static List<RunElement> BuildRun(IReadOnlyList<StatementSyntax> coreStatements, MatchContext ctx)
    {
        var run = new List<RunElement>(coreStatements.Count);
        foreach (var statement in coreStatements)
        {
            if (ctx.Markers.TryGetStatementHole(statement, out var hole))
                run.Add(new HoleElement(hole, statement.GetLocation()));
            else
                run.Add(new LiteralElement(statement));
        }

        return run;
    }

    /// <summary>
    /// Splits a block body into its CORE (non-anchor) statements and the <c>Block.Start()</c> / <c>Block.End()</c>
    /// anchor flags, carrying each anchor's <c>Location</c> (for SY1201). Runs BEFORE any count-based shape check
    /// so the core count never miscounts an anchor as a statement (e.g. <c>{ return x; Block.End(); }</c> extracts
    /// to one core statement + anchorEnd, dispatching as an anchored statement-Single, never a default arm).
    /// While walking in order it also detects the PROVABLE anchor contradiction (SY1202): a core statement
    /// before <c>Block.Start()</c> or after <c>Block.End()</c> can never be satisfied. Conservative — only the
    /// provably-dead is flagged.
    /// </summary>
    private static (List<StatementSyntax> Core, bool AnchorStart, bool AnchorEnd, List<Location> AnchorLocations) ExtractAnchors(
        IReadOnlyList<StatementSyntax> statements, MatchContext ctx)
    {
        var core = new List<StatementSyntax>();
        var anchorLocations = new List<Location>();
        bool anchorStart = false;
        bool anchorEnd = false;

        foreach (var statement in statements)
        {
            if (ctx.Markers.TryGetAnchor(statement, out bool isStart))
            {
                anchorLocations.Add(statement.GetLocation());
                if (isStart)
                {
                    anchorStart = true;

                    // Content before Block.Start() requires statements before the block's first — unsatisfiable.
                    if (core.Count > 0 && !ctx.Aborted)
                    {
                        ctx.Diagnostics.Add(MatchDiagnostics.PatternUnsatisfiable(
                            statement.GetLocation(),
                            "a core statement appears before Block.Start(), so the pattern can never match"));
                        ctx.Aborted = true;
                    }
                }
                else
                {
                    anchorEnd = true;
                }
            }
            else
            {
                // Content after Block.End() requires statements past the block's last — unsatisfiable.
                if (anchorEnd && !ctx.Aborted)
                {
                    ctx.Diagnostics.Add(MatchDiagnostics.PatternUnsatisfiable(
                        statement.GetLocation(),
                        "a core statement appears after Block.End(), so the pattern can never match"));
                    ctx.Aborted = true;
                }

                core.Add(statement);
            }
        }

        return (core, anchorStart, anchorEnd, anchorLocations);
    }

    /// <summary>
    /// Pre-scans the pattern body for a phantom <c>foreach</c> whose iterated expression binds to a
    /// <c>[Capture]</c> param — the §3.7 repetition notation whose backtracking lowering is deferred to v2.
    /// Each such <c>foreach</c> raises SY1203 and aborts (diagnostics-only, no tree). A literal <c>foreach</c>
    /// over a normal collection (not a capture) is left alone — matched literally by the walk.
    /// </summary>
    private static void ScanForDeferredForeach(MatchInfo info, MatchContext ctx)
    {
        foreach (var foreachStatement in info.PatternSyntax.DescendantNodes().OfType<ForEachStatementSyntax>())
        {
            if (ctx.Markers.IsCapture(foreachStatement.Expression))
            {
                ctx.Diagnostics.Add(MatchDiagnostics.ForeachRepetitionNotSupported(foreachStatement.GetLocation()));
                ctx.Aborted = true;
            }
        }
    }

    /// <summary>
    /// The single statement-run alignment core that <c>Bare</c>, statement-<c>Single</c> and <c>None</c> all
    /// flow through. Its 6-arg signature is FINAL at introduction (Task 9): later tasks add the variable-length
    /// split, the anchored drivers and the SY1204 check using the parameters already present — no new field or
    /// parameter. <paramref name="coreStatements"/> is the raw post-anchor list kept for diagnostic
    /// <c>Location</c>s. The CALLER establishes the candidate block local <c>_blk</c> + its null guard, so
    /// <c>None</c> can root <c>_blk</c> on a declaration body while <c>Bare</c>/<c>Single</c> root it on <c>node</c>.
    /// </summary>
    /// <remarks>
    /// Structure: a per-offset attempt local function <c>{Name}Match? _TryAt(int _o)</c> (matches the run's
    /// elements at that offset, captures, returns the record or null), then a driver selected by the anchor
    /// flags. Task 9 fills the no-anchor leftmost-scan driver only; the anchored drivers and the variable split
    /// land in Tasks 11–13. The fixed-only run is the special case where the bound is the exact width.
    /// </remarks>
    private static void EmitAnchoredRun(
        List<StatementSyntax> body,
        IReadOnlyList<StatementSyntax> coreStatements,
        List<RunElement> run,
        MatchContext ctx,
        bool anchorStart,
        bool anchorEnd)
    {
        _ = coreStatements;

        // SY1204 (quantifier-placement, arm i): a run may contain AT MOST one variable-length element (the
        // straight-line single greedy split). >1 needs the deferred backtracking lowering -> located on the
        // SECOND variable-length hole (carried on the HoleElement, no marker re-query), diagnostics-only.
        var variableElements = run.OfType<HoleElement>().Where(element => element.Hole.IsVariableLength).ToList();
        if (variableElements.Count > 1)
        {
            ctx.Diagnostics.Add(MatchDiagnostics.QuantifierPlacementUnsupported(
                variableElements[1].Location,
                "a statement run may contain at most one variable-length quantifier (Some/All/Opt)"));
            ctx.Aborted = true;
            return;
        }

        // Bound on the FIXED-flank width (sum of the fixed-arity element widths = before + after); the single
        // variable-length element absorbs the slack. A fixed-only run is the special case where there is no
        // variable element and the fixed width is the exact run width.
        int fixedWidth = run
            .Where(element => element is not HoleElement { Hole.IsVariableLength: true })
            .Sum(FixedWidth);

        bool hasVariable = run.Any(element => element is HoleElement { Hole.IsVariableLength: true });

        string matchType = ctx.Info.Name + "Match";

        var attemptBody = new List<StatementSyntax>();
        EmitRunElementsAt(attemptBody, run, ctx);
        attemptBody.Add(ParseStatement($"return new {matchType}({RenderCaptureArguments(ctx)});"));

        var attempt = ((LocalFunctionStatementSyntax)ParseStatement($"{matchType}? _TryAt(int _o) {{ }}"))
            .WithBody(Block(attemptBody.ToArray()));
        body.Add(attempt);

        if (!anchorStart && !anchorEnd)
        {
            // No-anchor (Bare contains / statement-Single): leftmost scan over offsets 0 .. Count - fixedWidth.
            body.Add(ParseStatement(
                $"for (int _o = 0; _o <= _blk.Statements.Count - {fixedWidth}; _o++) {{ if (_TryAt(_o) is {{ }} _m) return _m; }}"));
            body.Add(ParseStatement("return null;"));
        }
        else if (anchorStart && anchorEnd)
        {
            // Fully bounded (None): no scan, pin _o = 0. With a variable element the body need only be at least
            // the fixed width (the variable absorbs the rest); a fixed-only run requires the body to be EXACTLY
            // the run width (else trailing statements are uncovered — not "fully bounded"). NEVER `Count == width`
            // miscounting a variable element as 1.
            string countGuard = hasVariable
                ? $"if (_blk.Statements.Count < {fixedWidth}) return null;"
                : $"if (_blk.Statements.Count != {fixedWidth}) return null;";
            body.Add(ParseStatement("int _o = 0;"));
            body.Add(ParseStatement(countGuard));
            body.Add(ParseStatement("if (_TryAt(_o) is { } _m) return _m;"));
            body.Add(ParseStatement("return null;"));
        }
        else
        {
            // Exactly one anchor. anchorStart pins the run to the block start (_o = 0); anchorEnd pins it to the
            // block end — for a fixed-only run that is _o = Count - fixedWidth, while a variable element already
            // ends the run at the last statement (via _var), so _o = 0 and the variable absorbs the lead.
            body.Add(ParseStatement($"if (_blk.Statements.Count < {fixedWidth}) return null;"));

            string offset = anchorStart
                ? "0"
                : hasVariable ? "0" : $"_blk.Statements.Count - {fixedWidth}";

            body.Add(ParseStatement($"int _o = {offset};"));
            body.Add(ParseStatement("if (_TryAt(_o) is { } _m) return _m;"));
            body.Add(ParseStatement("return null;"));
        }
    }

    /// <summary>
    /// Roots a <c>None</c> matcher on the candidate DECLARATION (a method / local function), derives its body
    /// block into the non-null <c>_blk</c> local (the core's precondition), then aligns the run fully bounded
    /// (both anchors). A declaration with no block body (expression-bodied / abstract) yields no match.
    /// </summary>
    private static void EmitDeclarationFullBody(List<StatementSyntax> body, IReadOnlyList<StatementSyntax> coreStatements, List<RunElement> run, MatchContext ctx)
    {
        body.Add(ParseStatement(
            "BlockSyntax? _body = node switch { MethodDeclarationSyntax _md => _md.Body, LocalFunctionStatementSyntax _lf => _lf.Body, _ => null };"));
        body.Add(ParseStatement("if (_body is not { } _blk) return null;"));
        EmitAnchoredRun(body, coreStatements, run, ctx, anchorStart: true, anchorEnd: true);
    }

    /// <summary>
    /// Emits the per-offset guards for a run at offset <c>_o</c>. A run has AT MOST one variable-length element
    /// (else SY1204, Task 15): the fixed elements before it index at <c>_o + slot</c>, the variable element
    /// absorbs <c>_var = Count - _o - before - after</c>, and the fixed elements after it index at the
    /// <c>_var</c>-relative tail <c>_o + before + _var + afterSlot</c>. A fixed-only run is the special case
    /// where there is no variable element (identical output to the pre-Task-11 fixed path).
    /// </summary>
    private static void EmitRunElementsAt(List<StatementSyntax> attemptBody, List<RunElement> run, MatchContext ctx)
    {
        int variableIndex = -1;
        for (int i = 0; i < run.Count; i++)
        {
            if (run[i] is HoleElement { Hole.IsVariableLength: true })
            {
                variableIndex = i;
                break;
            }
        }

        if (variableIndex < 0)
        {
            int slot = 0;
            foreach (var element in run)
                slot += EmitFixedElement(attemptBody, element, Offset(slot), ctx);
            return;
        }

        int before = 0;
        for (int i = 0; i < variableIndex; i++)
            before += FixedWidth(run[i]);

        int after = 0;
        for (int i = variableIndex + 1; i < run.Count; i++)
            after += FixedWidth(run[i]);

        var variable = ((HoleElement)run[variableIndex]).Hole;
        bool hasAfter = variableIndex < run.Count - 1;
        bool needsVar = variable.Kind == StatementHoleKind.Capture
            || variable.Quantifier is StatementQuantifier.Some or StatementQuantifier.Opt
            || hasAfter;

        // Fixed elements BEFORE the variable element, at _o + slot.
        int beforeSlot = 0;
        for (int i = 0; i < variableIndex; i++)
            beforeSlot += EmitFixedElement(attemptBody, run[i], Offset(beforeSlot), ctx);

        if (needsVar)
            attemptBody.Add(ParseStatement($"int _var = _blk.Statements.Count - _o - {before} - {after};"));

        EmitVariableElement(attemptBody, variable, before, ctx);

        // Fixed elements AFTER the variable element, at the _var-relative tail _o + before + _var + afterSlot.
        int afterSlot = 0;
        for (int i = variableIndex + 1; i < run.Count; i++)
            afterSlot += EmitFixedElement(attemptBody, run[i], TailOffset(before, afterSlot), ctx);
    }

    /// <summary>
    /// Emits a single FIXED-arity run element at the index expression <paramref name="indexExpr"/> and returns
    /// its width: a literal statement / <c>One</c> hole (width 1) or an <c>Exactly(n)</c> hole (width n, a
    /// <see cref="SyntaxList{TNode}"/> slice).
    /// </summary>
    private static int EmitFixedElement(List<StatementSyntax> attemptBody, RunElement element, string indexExpr, MatchContext ctx)
    {
        switch (element)
        {
            case LiteralElement literal:
                EmitNodeMatch(attemptBody, $"_blk.Statements[{indexExpr}]", literal.Statement, ctx);
                return 1;

            case HoleElement { Hole: { Quantifier: StatementQuantifier.One } hole }:
                EmitStatementCapture(attemptBody, $"_blk.Statements[{indexExpr}]", hole, ctx);
                return 1;

            case HoleElement { Hole: { Quantifier: StatementQuantifier.Exactly } hole }:
                EmitFixedSliceCapture(attemptBody, indexExpr, hole.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), hole, ctx);
                return hole.Count;

            default:
                return 0;
        }
    }

    /// <summary>
    /// Emits the single variable-length element: a min-length guard (<c>Some</c> needs <c>_var &gt;= 1</c>,
    /// <c>Opt</c> needs <c>_var &lt;= 1</c>; <c>All</c> is already bounded by the driver) plus, for a capture,
    /// the slice into a <c>SyntaxList</c> member (<c>Some</c>/<c>All</c>) or a nullable statement (<c>Opt</c>).
    /// </summary>
    private static void EmitVariableElement(List<StatementSyntax> attemptBody, StatementHole hole, int before, MatchContext ctx)
    {
        switch (hole.Quantifier)
        {
            case StatementQuantifier.Some:
                attemptBody.Add(ParseStatement("if (_var < 1) return null;"));
                break;
            case StatementQuantifier.Opt:
                attemptBody.Add(ParseStatement("if (_var > 1) return null;"));
                break;
        }

        if (hole.Kind == StatementHoleKind.Wildcard)
            return;

        string startExpr = before == 0 ? "_o" : $"_o + {before}";
        string local = "cap_" + hole.ParameterName;

        if (hole.Quantifier == StatementQuantifier.Opt)
        {
            attemptBody.Add(ParseStatement($"StatementSyntax? {local} = _var == 1 ? _blk.Statements[{startExpr}] : null;"));
            ctx.Captures.Add(new Capture(hole.Ordinal, hole.MemberName, "StatementSyntax?", local));
        }
        else
        {
            ctx.NeedsLinq = true;
            attemptBody.Add(ParseStatement($"SyntaxList<StatementSyntax> {local} = new SyntaxList<StatementSyntax>(_blk.Statements.Skip({startExpr}).Take(_var));"));
            ctx.Captures.Add(new Capture(hole.Ordinal, hole.MemberName, "SyntaxList<StatementSyntax>", local));
        }

        ctx.BoundCaptureLocals.Add(local);
    }

    /// <summary>Emits a fixed-count <c>Exactly(n)</c> slice: a wildcard consumes the n slots silently; a capture binds a <see cref="SyntaxList{TNode}"/>.</summary>
    private static void EmitFixedSliceCapture(List<StatementSyntax> attemptBody, string startExpr, string countExpr, StatementHole hole, MatchContext ctx)
    {
        if (hole.Kind == StatementHoleKind.Wildcard)
            return;

        ctx.NeedsLinq = true;
        string local = "cap_" + hole.ParameterName;
        attemptBody.Add(ParseStatement($"SyntaxList<StatementSyntax> {local} = new SyntaxList<StatementSyntax>(_blk.Statements.Skip({startExpr}).Take({countExpr}));"));
        ctx.Captures.Add(new Capture(hole.Ordinal, hole.MemberName, "SyntaxList<StatementSyntax>", local));
        ctx.BoundCaptureLocals.Add(local);
    }

    /// <summary>The fixed-arity width of a run element (literal / One = 1, Exactly(n) = n). Variable-length elements are not fixed.</summary>
    private static int FixedWidth(RunElement element) =>
        element is HoleElement { Hole: { Quantifier: StatementQuantifier.Exactly } hole } ? hole.Count : 1;

    private static string Offset(int slot) => slot == 0 ? "_o" : $"_o + {slot}";

    private static string TailOffset(int before, int afterSlot)
    {
        string expression = "_o";
        if (before > 0)
            expression += $" + {before}";
        expression += " + _var";
        if (afterSlot > 0)
            expression += $" + {afterSlot}";
        return expression;
    }

    /// <summary>
    /// Composes the generated file: the nested result record + the matcher method, wrapped in the partial
    /// target's full ancestry (file-scoped namespace via <see cref="ClassDeclarationSyntaxExtensions.WithAncestryFrom"/>,
    /// no block conversion), under <c>#nullable enable</c> with the three Roslyn usings, normalized + formatted.
    /// The <c>IsExternalInit</c> polyfill is NOT part of this file — it is a single per-assembly post-init
    /// output (C3) registered by <see cref="MatchFactorySourceGenerator"/>.
    /// </summary>
    private static (string FileName, string Source) Compose(MatchInfo info, MatchContext ctx, List<StatementSyntax> body)
    {
        string matchName = info.Name + "Match";

        // Record members are the captures in SIGNATURE order (by Ordinal), regardless of walk order. A
        // positional record gives a free Deconstruct. Zero captures -> an empty-member record.
        string memberList = string.Join(", ",
            ctx.Captures.OrderBy(capture => capture.Ordinal).Select(capture => $"{capture.MemberType} {capture.MemberName}"));

        var record = (RecordDeclarationSyntax)ParseMemberDeclaration($"public sealed record {matchName}({memberList});")!;

        var method = ((MethodDeclarationSyntax)ParseMemberDeclaration($"public static {matchName}? {info.Name}(SyntaxNode node) {{ }}")!)
            .WithBody(Block(body.ToArray()));

        var targetClassDecl = (ClassDeclarationSyntax)info.Target.DeclaringSyntaxReferences[0].GetSyntax();

        MemberDeclarationSyntax targetSyntax = ClassDeclaration(targetClassDecl.Identifier)
            .WithModifiers(targetClassDecl.Modifiers)
            .AddMembers(record, method);

        targetSyntax = targetSyntax.WithAncestryFrom(info.Target);

        var usings = new List<UsingDirectiveSyntax>
        {
            UsingDirective(ParseName("Microsoft.CodeAnalysis")),
            UsingDirective(ParseName("Microsoft.CodeAnalysis.CSharp")),
            UsingDirective(ParseName("Microsoft.CodeAnalysis.CSharp.Syntax")),
        };

        // System.Linq only when the body slices with Skip/Take (a variable/Exactly statement capture) — adding
        // it unconditionally would churn the capture-less goldens.
        if (ctx.NeedsLinq)
            usings.Add(UsingDirective(ParseName("System.Linq")));

        var compilationUnit = CompilationUnit()
            .AddMembers(targetSyntax)
            .AddUsings(usings.ToArray())
            .WithLeadingTrivia(
                Trivia(
                    NullableDirectiveTrivia(
                        Token(SyntaxKind.EnableKeyword),
                        isActive: true)));

        var sourceText = SyntaxFormatter.Format(compilationUnit.NormalizeWhitespace()).GetText(Encoding.UTF8).ToString();

        return ($"{info.TargetFullName}.{info.Name}.g.cs", sourceText);
    }
}

/// <summary>
/// A captured pattern parameter: a result-record member. Carries <see cref="Ordinal"/> = the parameter's
/// signature position so record-member order is signature order regardless of walk order.
/// </summary>
internal sealed class Capture
{
    public Capture(int ordinal, string memberName, string memberType, string localName)
    {
        Ordinal = ordinal;
        MemberName = memberName;
        MemberType = memberType;
        LocalName = localName;
    }

    public int Ordinal { get; }
    public string MemberName { get; }
    public string MemberType { get; }

    /// <summary>The runtime local the captured node binds to (<c>cap_{paramName}</c>); the record ctor argument.</summary>
    public string LocalName { get; }
}

/// <summary>
/// An ordered element of a statement run aligned by <see cref="MatchEmitter"/>'s shared core: a
/// <see cref="LiteralElement"/> (a literal statement matched structurally) or, from Task 10, a
/// <c>HoleElement</c> (a classified statement hole). Carries its data via ctors (no <c>required</c> — absent
/// on netstandard2.0).
/// </summary>
internal abstract class RunElement
{
}

/// <summary>A literal statement in a run, matched structurally via the generic walk.</summary>
internal sealed class LiteralElement : RunElement
{
    public LiteralElement(StatementSyntax statement)
    {
        Statement = statement;
    }

    public StatementSyntax Statement { get; }
}

/// <summary>
/// A classified statement hole in a run. Carries its source statement <see cref="Location"/> so the SY1204
/// quantifier-placement check (Task 15) reports on the offending hole without re-querying the markers.
/// </summary>
internal sealed class HoleElement : RunElement
{
    public HoleElement(StatementHole hole, Location location)
    {
        Hole = hole;
        Location = location;
    }

    public StatementHole Hole { get; }
    public Location Location { get; }
}

/// <summary>
/// Mutable per-pattern emit state. Introduced here in its final shape: later tasks only POPULATE
/// <see cref="Captures"/>/<see cref="BoundCaptureLocals"/>/<see cref="Diagnostics"/> or SET <see cref="Aborted"/>;
/// the abort/merge is already wired in <see cref="MatchEmitter.Emit"/>.
/// </summary>
internal sealed class MatchContext
{
    private int _tmpCounter;

    public MatchContext(MatchInfo info, MatchMarkers markers)
    {
        Info = info;
        Markers = markers;
    }

    public MatchInfo Info { get; }
    public MatchMarkers Markers { get; }

    /// <summary>The captured members, each carrying <c>Ordinal = param.Ordinal</c> (record order = signature order).</summary>
    public List<Capture> Captures { get; } = new();

    /// <summary>First-vs-reuse-site distinction for a reused capture (the non-linear equality path).</summary>
    public HashSet<string> BoundCaptureLocals { get; } = new(System.StringComparer.Ordinal);

    /// <summary>Emitter-raised diagnostics (SY1202/SY1204/SY1205), merged into the pipeline output by <c>Emit</c>.</summary>
    public List<DiagnosticInfo> Diagnostics { get; } = new();

    /// <summary>A branch set this and returned -> <c>Emit</c> emits diagnostics-only (no tree).</summary>
    public bool Aborted { get; set; }

    /// <summary>Set by the slice-emitting helpers when the body uses <c>Skip</c>/<c>Take</c>; gates the <c>using System.Linq;</c> in <see cref="MatchEmitter"/>'s output so capture-less goldens don't churn.</summary>
    public bool NeedsLinq { get; set; }

    /// <summary>A unique temp-local name per call.</summary>
    public string NextTmp() => "_t" + _tmpCounter++;
}

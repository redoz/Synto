using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.Matching;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto;

/// <summary>
/// The statement-run alignment engine: classifies a core statement list into a fixed/variable-length run and
/// emits the per-offset attempt local function plus the anchor-selected driver that aligns it against the
/// candidate block. Pure transform-internal helper invoked synchronously by <see cref="MatchEmitter"/>.
/// </summary>
internal static class MatchRunAligner
{
    /// <summary>
    /// Classifies each core statement into a <see cref="RunElement"/>: a direct statement hole becomes a
    /// <see cref="HoleElement"/> (carrying its <c>Location</c> for SY1204), everything else a
    /// <see cref="LiteralElement"/> matched structurally (its own embedded holes are reached by the walk).
    /// </summary>
    public static List<RunElement> BuildRun(IReadOnlyList<StatementSyntax> coreStatements, MatchContext ctx)
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
    public static void EmitAnchoredRun(
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
        attemptBody.Add(ParseStatement($"return new {matchType}({MatchNodeWalker.RenderCaptureArguments(ctx)});"));

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
    public static void EmitDeclarationFullBody(List<StatementSyntax> body, IReadOnlyList<StatementSyntax> coreStatements, List<RunElement> run, MatchContext ctx)
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
                MatchNodeWalker.EmitNodeMatch(attemptBody, $"_blk.Statements[{indexExpr}]", literal.Statement, ctx);
                return 1;

            case HoleElement { Hole: { Quantifier: StatementQuantifier.One } hole }:
                MatchNodeWalker.EmitStatementCapture(attemptBody, $"_blk.Statements[{indexExpr}]", hole, ctx);
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
}

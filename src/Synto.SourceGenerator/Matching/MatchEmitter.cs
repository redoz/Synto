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

        if (info.Option == MatchOption.Single)
        {
            // Expression-Single (arrow body). Root the walk on the handed `node`; the walk captures expression
            // holes into ctx.Captures, then we return the record built from the captured locals in SIGNATURE
            // order.
            if (MatchMarkers.IsExpressionBodied(info.PatternSyntax, out var expression))
            {
                EmitNodeMatch(body, "node", expression, ctx);
                body.Add(ParseStatement($"return new {info.Name}Match({RenderCaptureArguments(ctx)});"));
            }
            // Statement-Single (block body with one core statement). Root on the candidate block and align the
            // one-element run via the shared core (leftmost, no anchors). The caller establishes `_blk`.
            else if (MatchMarkers.TryGetBlockBody(info.PatternSyntax, out var block) && block.Statements.Count == 1)
            {
                var run = BuildRun(block.Statements, ctx);
                body.Add(ParseStatement("if (node is not BlockSyntax _blk) return null;"));
                EmitAnchoredRun(body, block.Statements, run, ctx, anchorStart: false, anchorEnd: false);
            }
        }
        // Bare (block body, a run contained in the candidate block). Root on the candidate block and align the
        // run at the leftmost contained offset (no anchors). An embedded One hole inside a literal statement is
        // captured by the generic walk (EmitNodeMatch); direct variable-length run elements land in Task 11.
        else if (info.Option == MatchOption.Bare && MatchMarkers.TryGetBlockBody(info.PatternSyntax, out var bareBlock))
        {
            var run = BuildRun(bareBlock.Statements, ctx);
            body.Add(ParseStatement("if (node is not BlockSyntax _blk) return null;"));
            EmitAnchoredRun(body, bareBlock.Statements, run, ctx, anchorStart: false, anchorEnd: false);
        }

        // Every other option/body-shape stays unemitted here (body.Count == 0 -> null), exactly like the
        // former stub; later tasks fill those arms.

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
        // fixed single (One) is meaningful in a single embedded slot; a variable-length quantifier there is left
        // for Task 15's SY1204 (until then it falls through to a dead literal guard, never a crash).
        if (pattern is StatementSyntax statement
            && ctx.Markers.TryGetStatementHole(statement, out var statementHole)
            && statementHole.Quantifier == StatementQuantifier.One)
        {
            EmitStatementCapture(body, accessor, statementHole, ctx);
            return;
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
    /// Classifies each core statement into a <see cref="RunElement"/>. Task 9 only produces
    /// <see cref="LiteralElement"/> (a literal statement matched structurally); Task 10+ adds
    /// <see cref="HoleElement"/> for classified statement holes.
    /// </summary>
    private static List<RunElement> BuildRun(IReadOnlyList<StatementSyntax> coreStatements, MatchContext ctx)
    {
        _ = ctx;
        var run = new List<RunElement>(coreStatements.Count);
        foreach (var statement in coreStatements)
            run.Add(new LiteralElement(statement));

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
    private static void EmitAnchoredRun(
        List<StatementSyntax> body,
        IReadOnlyList<StatementSyntax> coreStatements,
        List<RunElement> run,
        MatchContext ctx,
        bool anchorStart,
        bool anchorEnd)
    {
        _ = coreStatements;

        // Task 9: every element is fixed-arity, so the run width is the element count. Task 11 splits this into
        // before/after widths flanking the single variable-length element (the driver then bounds on before+after).
        int width = run.Count;

        string matchType = ctx.Info.Name + "Match";

        var attemptBody = new List<StatementSyntax>();
        EmitRunElementsAt(attemptBody, run, ctx);
        attemptBody.Add(ParseStatement($"return new {matchType}({RenderCaptureArguments(ctx)});"));

        var attempt = ((LocalFunctionStatementSyntax)ParseStatement($"{matchType}? _TryAt(int _o) {{ }}"))
            .WithBody(Block(attemptBody.ToArray()));
        body.Add(attempt);

        // No-anchor (Bare contains / statement-Single): leftmost scan over offsets 0 .. Count - width.
        if (!anchorStart && !anchorEnd)
        {
            body.Add(ParseStatement(
                $"for (int _o = 0; _o <= _blk.Statements.Count - {width}; _o++) {{ if (_TryAt(_o) is {{ }} _m) return _m; }}"));
            body.Add(ParseStatement("return null;"));
        }
    }

    /// <summary>
    /// Emits the per-offset guards for each run element at <c>_blk.Statements[_o + slot]</c>. A
    /// <see cref="LiteralElement"/> is matched structurally via <see cref="EmitNodeMatch"/> (the indexer is
    /// statically <c>StatementSyntax</c>, narrowed at the node). Task 9 handles fixed-arity literals only.
    /// </summary>
    private static void EmitRunElementsAt(List<StatementSyntax> attemptBody, List<RunElement> run, MatchContext ctx)
    {
        int slot = 0;
        foreach (var element in run)
        {
            switch (element)
            {
                case LiteralElement literal:
                    EmitNodeMatch(attemptBody, StatementAccessor(slot), literal.Statement, ctx);
                    slot++;
                    break;
            }
        }
    }

    private static string StatementAccessor(int slot) =>
        slot == 0 ? "_blk.Statements[_o]" : $"_blk.Statements[_o + {slot}]";

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

        var compilationUnit = CompilationUnit()
            .AddMembers(targetSyntax)
            .AddUsings(
                UsingDirective(ParseName("Microsoft.CodeAnalysis")),
                UsingDirective(ParseName("Microsoft.CodeAnalysis.CSharp")),
                UsingDirective(ParseName("Microsoft.CodeAnalysis.CSharp.Syntax")))
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

    /// <summary>A unique temp-local name per call.</summary>
    public string NextTmp() => "_t" + _tmpCounter++;
}

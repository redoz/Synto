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
                ctx.CouldMatchGuard = MatchNodeWalker.ComputeExpressionRootGate(expression, ctx);
                MatchNodeWalker.EmitNodeMatch(body, "node", expression, ctx);
                body.Add(ParseStatement($"return new {info.Name}Match({MatchNodeWalker.RenderCaptureArguments(ctx)});"));
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
                    // None roots on the candidate DECLARATION (method / local function) — the cheap gate is the
                    // declaration-kind check; the body-shape match runs in the matcher.
                    ctx.CouldMatchGuard = "node is MethodDeclarationSyntax or LocalFunctionStatementSyntax";
                    var run = MatchRunAligner.BuildRun(core, ctx);
                    MatchRunAligner.EmitDeclarationFullBody(body, core, run, ctx);
                }
            }
            else if (info.Option == MatchOption.Single && core.Count == 1)
            {
                // Statement-Single: root on the candidate block, align the one-element core with the anchor flags.
                ctx.CouldMatchGuard = "node is BlockSyntax";
                var run = MatchRunAligner.BuildRun(core, ctx);
                body.Add(ParseStatement("if (node is not BlockSyntax _blk) return null;"));
                MatchRunAligner.EmitAnchoredRun(body, core, run, ctx, anchorStart, anchorEnd);
            }
            else if (info.Option == MatchOption.Bare)
            {
                // Bare: root on the candidate block, align the run contained in it with the anchor flags.
                ctx.CouldMatchGuard = "node is BlockSyntax";
                var run = MatchRunAligner.BuildRun(core, ctx);
                body.Add(ParseStatement("if (node is not BlockSyntax _blk) return null;"));
                MatchRunAligner.EmitAnchoredRun(body, core, run, ctx, anchorStart, anchorEnd);
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

        // The companion cheap predicate {Name}CouldMatch: the matcher's top-level type/kind/shape gate as a
        // standalone bool (C-FM1 superset). Each dispatch branch recorded the SAME guard it roots on.
        var couldMatch = (MethodDeclarationSyntax)ParseMemberDeclaration(
            $"public static bool {info.Name}CouldMatch(SyntaxNode node) {{ return {ctx.CouldMatchGuard}; }}")!;

        // The {Name}Pattern descriptor bundles the cheap predicate + the full matcher into the injected
        // MatchPattern<TMatch> (the single symbol a consumer hands to ForMatch). Fully-qualified so it binds
        // to the injected-internal copy regardless of the consumer's usings.
        var pattern = (PropertyDeclarationSyntax)ParseMemberDeclaration(
            $"public static global::Synto.Matching.MatchPattern<{matchName}> {info.Name}Pattern {{ get; }} = new({info.Name}CouldMatch, {info.Name});")!;

        var targetClassDecl = (ClassDeclarationSyntax)info.Target.DeclaringSyntaxReferences[0].GetSyntax();

        MemberDeclarationSyntax targetSyntax = ClassDeclaration(targetClassDecl.Identifier)
            .WithModifiers(targetClassDecl.Modifiers)
            .AddMembers(record, method, couldMatch, pattern);

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

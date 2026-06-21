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

        // Expression-Single (arrow body). Root the walk on the handed `node`; the walk captures expression
        // holes (Task 6) into ctx.Captures, then we return the record built from the captured locals in
        // SIGNATURE order. Every other option/body-shape stays unemitted here (body.Count == 0 -> null),
        // exactly like the former stub; later tasks fill those arms.
        if (info.Option == MatchOption.Single
            && MatchMarkers.IsExpressionBodied(info.PatternSyntax, out var expression))
        {
            EmitNodeMatch(body, "node", expression, ctx);
            body.Add(ParseStatement($"return new {info.Name}Match({RenderCaptureArguments(ctx)});"));
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
    /// Task 6 deliberately has NO reuse branch: a second occurrence of the same capture re-declares
    /// <c>cap_{name}</c> (CS0128) and re-adds the member (CS0102) — the RED state Task 8's non-linear
    /// equality fixes. Adding a working reuse branch now would make Task 8 green-on-arrival.
    /// </remarks>
    private static void EmitCapture(List<StatementSyntax> body, string accessor, CaptureParameter capture, MatchContext ctx)
    {
        string local = "cap_" + capture.ParameterName;

        body.Add(ParseStatement($"if ({accessor} is not {capture.MemberType} {local}) return null;"));

        ctx.BoundCaptureLocals.Add(local);
        ctx.Captures.Add(new Capture(capture.Ordinal, capture.MemberName, capture.MemberType, local));
    }

    /// <summary>
    /// The captured-local argument list for the result-record constructor, in SIGNATURE order (by
    /// <see cref="Capture.Ordinal"/>) — the same order <see cref="Compose"/> sorts the record members by, so
    /// positional construction lines up with the positional record regardless of walk order.
    /// </summary>
    private static string RenderCaptureArguments(MatchContext ctx) =>
        string.Join(", ", ctx.Captures.OrderBy(capture => capture.Ordinal).Select(capture => capture.LocalName));

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

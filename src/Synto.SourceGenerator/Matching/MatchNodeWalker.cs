using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.Matching;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto;

/// <summary>
/// The generic structural node-walk: emits the type/kind/child guards for a pattern node against a runtime
/// candidate accessor (recursing child-by-child) and the capture/hole binders the walk dispatches to. Pure
/// transform-internal helper invoked synchronously by <see cref="MatchEmitter"/>.
/// </summary>
internal static class MatchNodeWalker
{
    /// <summary>
    /// Emits the structural guards for one node position against the runtime candidate <paramref name="accessor"/>
    /// and recurses child-by-child. Guards both the .NET type (binding a typed local for child navigation) and
    /// the <c>RawKind</c>, then the child count, then each child: a node child recurses through the
    /// <c>.AsNode()</c> projection, a token child compares kind + text.
    /// </summary>
    public static void EmitNodeMatch(List<StatementSyntax> body, string accessor, SyntaxNode pattern, MatchContext ctx)
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
    /// Computes the cheap root gate for an expression-Single matcher rooted on <paramref name="pattern"/>:
    /// the SAME first guard <see cref="EmitNodeMatch"/> emits at the root, lifted to a boolean over <c>node</c>.
    /// A capture root narrows to the capture's member type; an expression wildcard narrows to
    /// <c>ExpressionSyntax</c>; any other node roots on its .NET type + <c>RawKind</c>. C-FM1 superset.
    /// </summary>
    public static string ComputeExpressionRootGate(SyntaxNode pattern, MatchContext ctx)
    {
        if (ctx.Markers.TryGetCapture(pattern, out var capture))
            return $"node is {capture.MemberType}";

        if (ctx.Markers.IsExpressionWildcard(pattern))
            return "node is ExpressionSyntax";

        return $"node is {pattern.GetType().Name} && node.IsKind(SyntaxKind.{pattern.Kind()})";
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
    public static void EmitStatementCapture(List<StatementSyntax> body, string accessor, StatementHole hole, MatchContext ctx)
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
    public static string RenderCaptureArguments(MatchContext ctx) =>
        string.Join(", ", ctx.Captures.OrderBy(capture => capture.Ordinal).Select(capture => capture.LocalName));

}

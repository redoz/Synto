using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;

namespace Synto.Templating;

/// <summary>
/// The factory-time <em>syntax builders</em> injected internal into the consumer's generator compilation by
/// <c>SurfaceInjectionGenerator</c>. <c>Member</c>, the <c>Switch</c>/<c>Case</c>/<c>Arm</c>/<c>Return</c>
/// family, and every user builder are all discovered the same way — structural recognition (an unbound
/// simple-name call) plus facade synthesis; nothing here is hardwired by name. Authored once in
/// <c>src\Synto.Core</c>, <c>&lt;Compile Remove&gt;</c>d from Synto.Core, and injected <c>internal</c> into the
/// consumer's generator compilation by <c>SurfaceInjectionGenerator</c> — so it is called fully-qualified at
/// factory time with no Synto.Core runtime dependency.
/// </summary>
public static partial class SyntoBuilders
{
    /// <summary>Builds <c>instance.name</c> (a member access) from a quoted instance island and a member name.</summary>
    [SyntaxBuilder]
    public static ExpressionSyntax Member([Quoted] ExpressionSyntax instance, string name) =>
        MemberAccessExpression(SimpleMemberAccessExpression, instance, Token(DotToken), IdentifierName(name));

    // ---- data-driven switch builders (list-shaped [SyntaxBuilder] recipe) --------------------------------------
    // A host builder (Switch, overloaded by element type) takes a [Quoted] governing expression plus a staged
    // `params` list of child nodes; element builders (Case / Arm) produce the list items over a live projection.
    // The governing expression is QUOTED from the author's identifier; the section/arm lists are STAGED (produced
    // by the LINQ projection at factory-build time). Case's label is [Constant] — the author passes a factory-time
    // value (e.g. c.Ordinal, or an enum) that is lifted to a constant ExpressionSyntax at the binding seam; a live
    // projection var referenced inside stays liveness-shielded (marking it [Quoted] would instead quote the
    // projection variable out of output scope). Arm's pattern stays STAGED (an already-built constant
    // ExpressionSyntax; a [Constant]/[Quoted] label form there is out of scope). Return's / Arm's value are
    // [Quoted] islands (so a nested Member(...) builder expands via re-entry).

    /// <summary>Builds <c>switch (governing) { sections... }</c> — the statement form (element type selects this overload).</summary>
    [SyntaxBuilder]
    public static SwitchStatementSyntax Switch([Quoted] ExpressionSyntax governing, params IEnumerable<SwitchSectionSyntax> sections) =>
        SwitchStatement(governing).WithSections(List(sections));

    /// <summary>Builds <c>governing switch { arms... }</c> — the expression form (element type selects this overload).</summary>
    [SyntaxBuilder]
    public static SwitchExpressionSyntax Switch([Quoted] ExpressionSyntax governing, params IEnumerable<SwitchExpressionArmSyntax> arms) =>
        SwitchExpression(governing).WithArms(SeparatedList(arms));

    /// <summary>Builds <c>case label: body...</c> — one switch section ([Constant] value-lifted label + staged statement body).</summary>
    [SyntaxBuilder]
    public static SwitchSectionSyntax Case([Constant] ExpressionSyntax label, params IEnumerable<StatementSyntax> body) =>
        SwitchSection().AddLabels(CaseSwitchLabel(label)).WithStatements(List(body));

    /// <summary>Builds <c>default: body...</c> — the trailing default switch section (staged statement body).</summary>
    [SyntaxBuilder]
    public static SwitchSectionSyntax Default(params IEnumerable<StatementSyntax> body) =>
        SwitchSection().AddLabels(DefaultSwitchLabel()).WithStatements(List(body));

    /// <summary>Builds <c>pattern =&gt; value</c> — one switch-expression arm (staged constant pattern + quoted value island).</summary>
    [SyntaxBuilder]
    public static SwitchExpressionArmSyntax Arm(ExpressionSyntax pattern, [Quoted] ExpressionSyntax value) =>
        SwitchExpressionArm(ConstantPattern(pattern), value);

    /// <summary>Builds <c>pattern when guard =&gt; value</c> — a guarded switch-expression arm (staged pattern + quoted guard/value islands).</summary>
    [SyntaxBuilder]
    public static SwitchExpressionArmSyntax WhenArm(ExpressionSyntax pattern, [Quoted] ExpressionSyntax guard, [Quoted] ExpressionSyntax value) =>
        SwitchExpressionArm(ConstantPattern(pattern), WhenClause(guard), value);

    /// <summary>Builds <c>_ =&gt; value</c> — the discard (default) switch-expression arm (quoted value island).</summary>
    [SyntaxBuilder]
    public static SwitchExpressionArmSyntax DefaultArm([Quoted] ExpressionSyntax value) =>
        SwitchExpressionArm(DiscardPattern(), value);

    /// <summary>Builds <c>return value;</c> — a trivial statement body builder (quoted value island).</summary>
    [SyntaxBuilder]
    public static StatementSyntax Return([Quoted] ExpressionSyntax value) =>
        ReturnStatement(value);
}

using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;

namespace Synto.Templating;

/// <summary>
/// The factory-time <em>syntax builders</em> injected internal into the consumer's generator compilation by
/// <c>SurfaceInjectionGenerator</c>. <c>Member</c> is discovered like any user <c>[SyntaxBuilder]</c> method
/// (structural recognition — an unbound simple-name call — plus facade synthesis); only <c>Template.TypeOf</c>
/// remains hardwired: the generator recognizes a <c>Template.TypeOf</c> facade call by binding and rewrites it
/// to a fully-qualified static call of the matching method here. Authored once in <c>src\Synto</c>,
/// <c>&lt;Compile Remove&gt;</c>d from Synto.Core, and injected <c>internal</c> into the consumer's generator
/// compilation by <c>SurfaceInjectionGenerator</c> — so it is called fully-qualified at factory time with no
/// Synto.Core runtime dependency.
/// </summary>
public static partial class SyntoBuilders
{
    /// <summary>Builds <c>instance.name</c> (a member access) from a quoted instance island and a member name.</summary>
    [SyntaxBuilder]
    public static ExpressionSyntax Member([Quoted] ExpressionSyntax instance, string name) =>
        MemberAccessExpression(SimpleMemberAccessExpression, instance, Token(DotToken), IdentifierName(name));

    /// <summary>
    /// Builds the <c>typeof(name)</c> expression from a type name. Deliberately NOT paired with a
    /// <c>[SyntaxBuilder]</c>-facade counterpart the way <see cref="Member"/> is: <c>Template.TypeOf</c> must
    /// return <c>System.Type</c> in <em>value position</em> (e.g.
    /// <c>System.Type GetFieldType() =&gt; TypeOf(c.ColumnTypeName);</c>), whereas this builder returns
    /// <c>ExpressionSyntax</c>. A same-signature <c>System.Type TypeOf(string)</c> facade on
    /// <see cref="SyntoBuilders"/> would be CS0111 against this builder (return-type-only difference); a
    /// synthesized generic escape hatch (<c>TResult TypeOf&lt;TResult&gt;(string)</c>) loses overload resolution
    /// to this exact non-generic match. Unlike <c>Member</c> — whose <c>[Quoted] ExpressionSyntax</c> first
    /// parameter shields it, so a real author argument never binds here and the call falls through to
    /// structural recognition — <c>TypeOf</c>'s only parameter is a factory-time type NAME that is itself often
    /// a live/staged value (e.g. <c>c.ColumnTypeName</c>), so <c>[TypeArg]</c> (a compile-time type token)
    /// cannot express it either. That is why the value-position facade instead lives, hand-pinned to
    /// <c>System.Type</c>, on <c>Template</c> — a different type than this one — and stays bind-recognized
    /// rather than migrating here.
    /// </summary>
    public static ExpressionSyntax TypeOf(string name) => TypeOfExpression(ParseTypeName(name));

    // ---- data-driven switch builders (list-shaped [SyntaxBuilder] recipe) --------------------------------------
    // A host builder (Switch, overloaded by element type) takes a [Quoted] governing expression plus a staged
    // `params` list of child nodes; element builders (Case / Arm) produce the list items over a live projection.
    // The governing expression is QUOTED from the author's identifier; the section/arm lists are STAGED (produced
    // by the LINQ projection at factory-build time). Case's label / Arm's pattern are STAGED (an already-built
    // constant ExpressionSyntax — marking them [Quoted] would quote the projection variable out of output scope);
    // Return's / Arm's value are [Quoted] islands (so a nested Member(...) builder expands via re-entry).

    /// <summary>Builds <c>switch (governing) { sections... }</c> — the statement form (element type selects this overload).</summary>
    [SyntaxBuilder]
    public static SwitchStatementSyntax Switch([Quoted] ExpressionSyntax governing, params IEnumerable<SwitchSectionSyntax> sections) =>
        SwitchStatement(governing).WithSections(List(sections));

    /// <summary>Builds <c>governing switch { arms... }</c> — the expression form (element type selects this overload).</summary>
    [SyntaxBuilder]
    public static SwitchExpressionSyntax Switch([Quoted] ExpressionSyntax governing, params IEnumerable<SwitchExpressionArmSyntax> arms) =>
        SwitchExpression(governing).WithArms(SeparatedList(arms));

    /// <summary>Builds <c>case label: body...</c> — one switch section (staged label + staged statement body).</summary>
    [SyntaxBuilder]
    public static SwitchSectionSyntax Case(ExpressionSyntax label, params IEnumerable<StatementSyntax> body) =>
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

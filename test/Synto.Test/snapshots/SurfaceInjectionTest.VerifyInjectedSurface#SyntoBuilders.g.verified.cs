//HintName: SyntoBuilders.g.cs
#nullable enable
using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;

namespace Synto.Templating;

/// <summary>
/// The built-in factory-time <em>syntax builders</em> behind the hand-authored <c>Template.Member</c> /
/// <c>Template.TypeOf</c> facades. Unlike user builders (discovered via <c>[SyntaxBuilder]</c> and
/// facade-synthesized), these are hardwired: the generator recognizes a <c>Template.Member</c> /
/// <c>Template.TypeOf</c> facade call by binding and rewrites it to a fully-qualified static call of the
/// matching method here. Authored once in <c>src\Synto</c>, <c>&lt;Compile Remove&gt;</c>d from Synto.Core,
/// and injected <c>internal</c> into the consumer's generator compilation by <c>SurfaceInjectionGenerator</c>
/// — so it is called fully-qualified at factory time with no Synto.Core runtime dependency.
/// </summary>
internal static partial class SyntoBuilders
{
    /// <summary>Builds <c>instance.name</c> (a member access) from a quoted instance island and a member name.</summary>
    public static ExpressionSyntax Member([Quoted] ExpressionSyntax instance, string name) =>
        MemberAccessExpression(SimpleMemberAccessExpression, instance, Token(DotToken), IdentifierName(name));

    /// <summary>Builds the <c>typeof(name)</c> expression from a type name (the facade returns <c>System.Type</c>).</summary>
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

    /// <summary>Builds <c>pattern =&gt; value</c> — one switch-expression arm (staged constant pattern + quoted value island).</summary>
    [SyntaxBuilder]
    public static SwitchExpressionArmSyntax Arm(ExpressionSyntax pattern, [Quoted] ExpressionSyntax value) =>
        SwitchExpressionArm(ConstantPattern(pattern), value);

    /// <summary>Builds <c>return value;</c> — a trivial statement body builder (quoted value island).</summary>
    [SyntaxBuilder]
    public static StatementSyntax Return([Quoted] ExpressionSyntax value) =>
        ReturnStatement(value);
}

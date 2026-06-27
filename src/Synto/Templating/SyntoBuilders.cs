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
public static class SyntoBuilders
{
    /// <summary>Builds <c>instance.name</c> (a member access) from a quoted instance island and a member name.</summary>
    public static ExpressionSyntax Member([Quoted] ExpressionSyntax instance, string name) =>
        MemberAccessExpression(SimpleMemberAccessExpression, instance, Token(DotToken), IdentifierName(name));

    /// <summary>Builds the type reference <c>name</c> (a <c>TypeSyntax</c>) from a type name.</summary>
    public static TypeSyntax TypeOf(string name) => IdentifierName(name);
}

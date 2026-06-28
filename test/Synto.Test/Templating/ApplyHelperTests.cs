extern alias SyntoCore;

using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static SyntoCore::Synto.IdentifierAttributeExtensions;
using static SyntoCore::Synto.VisibilityAttributeExtensions;
using static SyntoCore::Synto.SealedAttributeExtensions;
using static SyntoCore::Synto.ImplementsAttributeExtensions;
using static SyntoCore::Synto.InheritsAttributeExtensions;
using Access = SyntoCore::Synto.Templating.Access;

namespace Synto.Test.Templating;

public class ApplyHelperTests
{
    static ClassDeclarationSyntax Parse(string src) =>
        CSharpSyntaxTree.ParseText(src).GetRoot().DescendantNodes()
            .OfType<ClassDeclarationSyntax>().First();

    [Fact]
    public void ApplyIdentifier_RenamesTypeAndConstructors()
    {
        var c = Parse("class Foo { public Foo() {} }");
        var r = c.ApplyIdentifierAttribute("Bar");
        Assert.Equal("Bar", r.Identifier.Text);
        Assert.Equal("Bar", r.Members.OfType<ConstructorDeclarationSyntax>().Single().Identifier.Text);
    }

    [Fact]
    public void ApplyVisibility_File_ReplacesAccess()
    {
        var c = Parse("internal sealed class Foo {}");
        var r = c.ApplyVisibilityAttribute(Access.File);
        Assert.Contains("file", r.Modifiers.ToString());
        Assert.DoesNotContain("internal", r.Modifiers.ToString());
        Assert.Contains("sealed", r.Modifiers.ToString()); // non-access modifier preserved
    }

    [Fact]
    public void ApplySealed_IsIdempotent()
    {
        var once = Parse("class Foo {}").ApplySealedAttribute();
        var twice = once.ApplySealedAttribute();
        Assert.Single(twice.Modifiers, m => m.Kind() == SyntaxKind.SealedKeyword);
    }

    [Fact]
    public void ApplyInheritsThenImplements_OrdersBaseList()
    {
        var c = Parse("class Foo {}")
            .ApplyInheritsAttribute("global::B")
            .ApplyImplementsAttribute("global::I1")
            .ApplyImplementsAttribute("global::I2");
        var bases = c.BaseList!.Types.Select(t => t.ToString().Trim()).ToArray();
        Assert.Equal(new[] { "global::B", "global::I1", "global::I2" }, bases);
    }
}

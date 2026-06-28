extern alias SyntoCore;

using System.Linq;
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
    public void ApplyVisibility_TwoTokenModifiers_ReplaceAccessAndPreserveNonAccess()
    {
        // protected internal — replaces the single "internal" token, adds both "protected" + "internal", preserves "sealed"
        var c1 = Parse("internal sealed class Foo {}");
        var r1 = c1.ApplyVisibilityAttribute(Access.ProtectedInternal);
        var kinds1 = r1.Modifiers.Select(m => m.Kind()).ToArray();
        Assert.Contains(SyntaxKind.ProtectedKeyword, kinds1);
        Assert.Contains(SyntaxKind.InternalKeyword, kinds1);
        Assert.Contains(SyntaxKind.SealedKeyword, kinds1); // non-access modifier preserved
        // "internal" now appears as access modifier — former lone "internal" is gone (replaced by the pair)
        Assert.Equal(3, kinds1.Length); // protected + internal + sealed

        // private protected — replaces the single "public" token, adds "private" + "protected", preserves "sealed"
        var c2 = Parse("public sealed class Bar {}");
        var r2 = c2.ApplyVisibilityAttribute(Access.PrivateProtected);
        var kinds2 = r2.Modifiers.Select(m => m.Kind()).ToArray();
        Assert.Contains(SyntaxKind.PrivateKeyword, kinds2);
        Assert.Contains(SyntaxKind.ProtectedKeyword, kinds2);
        Assert.DoesNotContain(SyntaxKind.PublicKeyword, kinds2);
        Assert.Contains(SyntaxKind.SealedKeyword, kinds2); // non-access modifier preserved
        Assert.Equal(3, kinds2.Length); // private + protected + sealed
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

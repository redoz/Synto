using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Synto.Test.Templating;

public class QualifiedNameFactoryTest
{
    private static INamedTypeSymbol Lookup(string metadataName)
    {
        var compilation = CSharpCompilation.Create(
            "Q",
            references: new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });
        return compilation.GetTypeByMetadataName(metadataName)!;
    }

    [Fact]
    public void PlainType_IsGlobalRooted()
    {
        var name = QualifiedNameFactory.ForSymbol(Lookup("System.DBNull"));
        Assert.Equal("global::System.DBNull", name.ToString());
    }

    [Fact]
    public void OpenGenericType_KeepsArityAndTypeParameters()
    {
        // System.Collections.Generic.IEnumerable`1 -> global::System.Collections.Generic.IEnumerable<T>
        var name = QualifiedNameFactory.ForSymbol(Lookup("System.Collections.Generic.IEnumerable`1"));
        Assert.Equal("global::System.Collections.Generic.IEnumerable<T>", name.ToString());
    }

    [Fact]
    public void NestedType_KeepsContainingType()
    {
        // System.Collections.Generic.List`1+Enumerator -> global::System.Collections.Generic.List<T>.Enumerator
        var name = QualifiedNameFactory.ForSymbol(Lookup("System.Collections.Generic.List`1+Enumerator"));
        Assert.Equal("global::System.Collections.Generic.List<T>.Enumerator", name.ToString());
    }

    [Fact]
    public void SpecialType_RendersAsNameNotKeyword()
    {
        // A special type spelled out by name (System.Int32) must render as a global::-rooted NameSyntax, NOT the
        // `int` keyword — else the NameSyntax cast throws and generation crashes (SY0000). Regression guard.
        var name = QualifiedNameFactory.ForSymbol(Lookup("System.Int32"));
        Assert.Equal("global::System.Int32", name.ToString());
    }

    [Fact]
    public void GenericWithSpecialTypeArgument_RendersArgAsName()
    {
        // The special type also appears as a type ARGUMENT (the crashing path was a generic name descended into a
        // written-out special type arg). global::System.Collections.Generic.List`1<System.Int32> must round-trip.
        var list = Lookup("System.Collections.Generic.List`1");
        var int32 = Lookup("System.Int32");
        var constructed = list.Construct(int32);
        var name = QualifiedNameFactory.ForSymbol(constructed);
        Assert.Equal("global::System.Collections.Generic.List<global::System.Int32>", name.ToString());
    }
}

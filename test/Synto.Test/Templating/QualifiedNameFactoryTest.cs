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
}

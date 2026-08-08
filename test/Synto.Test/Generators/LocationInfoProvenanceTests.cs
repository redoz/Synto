extern alias SyntoIncremental;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using LocationInfo = SyntoIncremental::Synto.Generators.LocationInfo;

namespace Synto.Test.Generators;

public class LocationInfoProvenanceTests
{
    private static (ISymbol symbol, SyntaxNode node) CompileFirstType(string src)
    {
        var tree = CSharpSyntaxTree.ParseText(src, path: "Provenance.cs");
        var comp = CSharpCompilation.Create("Prov", [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
        var typeDecl = tree.GetRoot().DescendantNodes()
            .First(n => n is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax);
        var symbol = comp.GetSemanticModel(tree).GetDeclaredSymbol(typeDecl)!;
        return (symbol, typeDecl);
    }

    [Fact]
    public void From_SyntaxNode_CarriesRealSpanAndPath()
    {
        var (_, node) = CompileFirstType("class Foo { }");
        var info = LocationInfo.From(node);
        Assert.NotNull(info);
        var loc = info!.Value.ToLocation();
        Assert.False(loc.SourceSpan.IsEmpty);
        Assert.Equal("Provenance.cs", loc.GetLineSpan().Path);
    }

    [Fact]
    public void From_Symbol_UsesFirstDeclaringSyntaxLocation()
    {
        var (symbol, node) = CompileFirstType("class Foo { }");
        var fromSymbol = LocationInfo.From(symbol);
        var fromNode = LocationInfo.From(node);
        Assert.Equal(fromNode, fromSymbol); // first declaring syntax == the class node
    }
}

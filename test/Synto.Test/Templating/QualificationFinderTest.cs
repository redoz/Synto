using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto.Test.Templating;

public class QualificationFinderTest
{
    private static (SemanticModel Model, MethodDeclarationSyntax Method) Compile(string body)
    {
        var source = $$"""
            using System;
            using System.Collections.Generic;
            class C<T>
            {
                void M()
                {
                    {{body}}
                }
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create("Q",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });
        var model = compilation.GetSemanticModel(tree);
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        return (model, method);
    }

    private static IReadOnlyDictionary<SyntaxNode, NameSyntax> Find(SemanticModel model, MethodDeclarationSyntax method)
        => QualificationFinder.FindQualifications(model, method, TemplateScope.ForCarrier(model, method));

    [Fact]
    public void QualifiesFrameworkType_TypePosition()
    {
        var (model, method) = Compile("Type t = null; GC.KeepAlive(t);");
        var map = Find(model, method);
        // 'Type' -> global::System.Type
        Assert.Contains(map, kv => kv.Key is IdentifierNameSyntax { Identifier.ValueText: "Type" }
                                   && kv.Value.ToString() == "global::System.Type");
    }

    [Fact]
    public void QualifiesFrameworkType_StaticMemberAccessRoot()
    {
        var (model, method) = Compile("object o = DBNull.Value;");
        var map = Find(model, method);
        Assert.Contains(map, kv => kv.Key is IdentifierNameSyntax { Identifier.ValueText: "DBNull" }
                                   && kv.Value.ToString() == "global::System.DBNull");
    }

    [Fact]
    public void SkipsTypeParameter()
    {
        var (model, method) = Compile("T x = default;");
        var map = Find(model, method);
        Assert.DoesNotContain(map, kv => kv.Key is IdentifierNameSyntax { Identifier.ValueText: "T" });
    }

    [Fact]
    public void SkipsVar()
    {
        var (model, method) = Compile("var y = 1;");
        var map = Find(model, method);
        Assert.DoesNotContain(map, kv => kv.Key is IdentifierNameSyntax { Identifier.ValueText: "var" });
    }

    [Fact]
    public void SkipsLocalAndMemberNames()
    {
        var (model, method) = Compile("int z = 0; z.ToString();");
        var map = Find(model, method);
        Assert.DoesNotContain(map, kv => kv.Key is IdentifierNameSyntax { Identifier.ValueText: "z" });
        Assert.DoesNotContain(map, kv => kv.Key is IdentifierNameSyntax { Identifier.ValueText: "ToString" });
    }
}

using System.Data;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;
using System.Reflection;

namespace Synto.Example.ObjectReader.Test;

public class ObjectReaderGeneratorTest
{
    private static readonly MetadataReference CorlibReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
    private static readonly MetadataReference NetStandardReference = MetadataReference.CreateFromFile(Assembly.Load("netstandard, Version=2.0.0.0").Location);
    private static readonly MetadataReference SystemRuntimeReference = MetadataReference.CreateFromFile(Assembly.Load("System.Runtime, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location);


    static Compilation CreateCompilation()
    {


        return CSharpCompilation.Create("Test",
            Array.Empty<SyntaxTree>(),
            references:
            [
                CorlibReference,
                NetStandardReference,
                SystemRuntimeReference,
                MetadataReference.CreateFromFile(typeof(IDataReader).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(ObjectReader).Assembly.Location)
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
    }

    static GeneratorDriver CreateDriver()
    {
        var generator = new ObjectReaderSourceGenerator();
        return CSharpGeneratorDriver.Create(generator);
    }

    private readonly Compilation _baseCompilation;
    private readonly GeneratorDriver _driver;

    public ObjectReaderGeneratorTest()
    {
        // probably shift this into a class fixture for perf
        _baseCompilation = CreateCompilation();
        _driver = CreateDriver();
    }

    private Compilation CompilationWithSource(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        Assert.Empty(syntaxTree.GetDiagnostics());

        var compilation = _baseCompilation.AddSyntaxTrees(syntaxTree);
        //var compilation = _baseCompilation.Add
        return compilation;
    }

    private async Task VerifyTemplate(string source)
    {
        var compilation = CompilationWithSource(source);
        var compilationDiagnostics = compilation.GetDiagnostics();
        Assert.Empty(compilationDiagnostics.Where(diag => diag.Severity == DiagnosticSeverity.Error));
        var result = _driver.RunGenerators(compilation);
        var diagnostics = result.GetRunResult();
        Assert.Empty(diagnostics.Diagnostics);
        var ret = await Verify(result).UseDirectory("snapshots");
        Assert.NotEmpty(ret.Files);
    }

    [Fact]
    public async Task SimpleTest()
    {
        await VerifyTemplate(
            """
            using System;
            using Synto.Example.ObjectReader;
            
            public class TestClass {
                public int Index { get; }
                public string Name { get; }
                public string Category { get; }
            }

            public class Test {
                public void Method() {
                    var data = new [] { new TestClass() };
                    var reader = ObjectReader.Create(data, "Index", "Name", "Category");
                }
            }
            """
        );
    }
}
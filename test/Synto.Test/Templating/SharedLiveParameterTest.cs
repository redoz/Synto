using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Synto.Test.Templating;

/// <summary>
/// Exercises a single live <c>Parameter&lt;T&gt;()</c> identity shared across MULTIPLE member bodies of a class
/// template (the ObjectReader dog-food shape, plan Task 9): each member re-declares
/// <c>var columns = Parameter&lt;…&gt;();</c> with the SAME inferred (name, T), so they dedup to ONE factory
/// parameter — and EVERY member's live <c>foreach</c> must unroll, not just the first declaration site.
/// </summary>
public class SharedLiveParameterTest
{
    private static readonly MetadataReference CorlibReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
    private static readonly MetadataReference NetStandardReference = MetadataReference.CreateFromFile(Assembly.Load("netstandard, Version=2.0.0.0").Location);
    private static readonly MetadataReference SystemRuntimeReference = MetadataReference.CreateFromFile(Assembly.Load("System.Runtime, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location);

    private static CSharpCompilation CreateCompilation() =>
        CSharpCompilation.Create("Test",
            [],
            references:
            [
                CorlibReference,
                NetStandardReference,
                SystemRuntimeReference,
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                MetadataReference.CreateFromFile(SyntoCoreAssembly.Location),
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private readonly Compilation _baseCompilation = CreateCompilation();
    private readonly GeneratorDriver _driver = CSharpGeneratorDriver.Create(new TemplateFactorySourceGenerator());

    private Compilation CompilationWithSource(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        Assert.Empty(syntaxTree.GetDiagnostics());
        return _baseCompilation.AddSyntaxTrees(syntaxTree);
    }

    private async Task VerifyTemplate(string source)
    {
        var compilation = CompilationWithSource(source);
        Assert.Empty(compilation.GetDiagnostics().Where(diag => diag.Severity == DiagnosticSeverity.Error));
        var result = _driver.RunGenerators(compilation);
        var ret = await Verify(result).UseDirectory("snapshots");
        Assert.NotEmpty(ret.Files);
    }

    // Two methods, each re-declaring `var columns = Parameter<IReadOnlyList<Col>>();` (same inferred name+T):
    // both foreach regions must unroll onto their own run (the second method is the regression: with only the
    // first declaration's symbol seeded as a root, its foreach would NOT be recognized as live control).
    [Fact]
    public async Task SharedParameter_AcrossMembers_BothUnroll()
    {
        await VerifyTemplate(
            """
            using Synto.Templating;
            using System.Collections.Generic;
            using static Synto.Templating.Template;
            partial class Factory {}
            public readonly record struct Col(int Ordinal, string Name);
            [Template(typeof(Factory))]
            public class Reader {
                public string GetName(int i) {
                    var columns = Parameter<IReadOnlyList<Col>>();
                    foreach (var c in columns)
                        if (i == c.Ordinal)
                            return c.Name;
                    throw new System.IndexOutOfRangeException();
                }
                public int GetOrdinal(string name) {
                    var columns = Parameter<IReadOnlyList<Col>>();
                    foreach (var c in columns)
                        if (name == c.Name)
                            return c.Ordinal;
                    throw new System.IndexOutOfRangeException();
                }
            }
            """);
    }
}

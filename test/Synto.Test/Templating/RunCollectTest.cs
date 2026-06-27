using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Synto.Test.Templating;

/// <summary>
/// Exercises the core staging path (plan Task 6 / spec §5.2–5.3): a <c>foreach</c> over a live root unrolls
/// at factory-build time. The loop scaffold runs verbatim in the factory body, each iteration collecting the
/// quote of the (purely-live-lifted) island statements into a run, and the live region's owning container
/// block is replaced by a <c>BuildList</c> of that run plus the quoted fixed siblings.
/// </summary>
public class RunCollectTest
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

    // Snapshot: foreach over a live Parameter<T>() root unrolls to a per-column if-chain (the canonical case).
    [Fact]
    public async Task ForeachOverStagedParameter_UnrollsToIfChain()
    {
        await VerifyTemplate(
            """
            using Synto.Templating;
            using System.Collections.Generic;
            using static Synto.Templating.Template;
            partial class Factory {}
            public readonly record struct Col(int Ordinal, string Name);
            public class TestClass {
                [Template(typeof(Factory))]
                string GetName(int i) {
                    var columns = Parameter<IReadOnlyList<Col>>();
                    foreach (var c in columns)            // live control -> unrolls in the factory
                        if (i == c.Ordinal)               // c.Ordinal -> int literal
                            return c.Name;                // c.Name    -> string literal
                    throw new System.IndexOutOfRangeException(); // quoted island, verbatim
                }
            }
            """);
    }
}

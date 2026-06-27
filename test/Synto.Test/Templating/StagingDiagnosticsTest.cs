using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Synto.Test.Templating;

/// <summary>
/// Exercises the staging error paths (plan Task 8): an <em>impossible cut</em> (a live fragment whose binding
/// transitively depends on an output-world/quoted value — <c>SY1013</c>) and an <em>unsupported live shape</em>
/// (a live construct the v1 emitter does not handle, e.g. a nested live region — <c>SY1014</c>). Both are
/// catch-convert-report diagnostics carrying a real source span; neither is thrown nor mis-expanded.
/// </summary>
public class StagingDiagnosticsTest
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

    private ImmutableArray<Diagnostic> RunAndGetDiagnostics(string source)
    {
        var compilation = CompilationWithSource(source);
        var result = _driver.RunGenerators(compilation);
        return result.GetRunResult().Diagnostics;
    }

    private static void AssertHasRealSpan(Diagnostic diag)
    {
        Assert.NotEqual(Location.None, diag.Location);
        Assert.False(diag.Location.SourceSpan.IsEmpty);
    }

    // A Live<T>() binding that depends on an output-world (quoted) parameter cannot run at factory time.
    [Fact]
    public void ImpossibleCut_ReportsSY1013()
    {
        var diagnostics = RunAndGetDiagnostics(
            """
            using Synto.Templating;
            using static Synto.Templating.Template;
            partial class Factory {}
            public class TestClass {
                [Template(typeof(Factory))]
                void Build(int generatedWorld) {
                    var bad = Live(generatedWorld + 1);  // live fragment needs a value that exists only in OUTPUT
                    System.Console.WriteLine(bad);
                }
            }
            """);
        var diag = Assert.Single(diagnostics, d => d.Id == "SY1013");
        AssertHasRealSpan(diag);
    }

    // A live region nested inside another live region's body is not unrolled in v1.
    [Fact]
    public void UnsupportedLiveShape_ReportsSY1014()
    {
        var diagnostics = RunAndGetDiagnostics(
            """
            using Synto.Templating;
            using System.Collections.Generic;
            using static Synto.Templating.Template;
            partial class Factory {}
            public class TestClass {
                [Template(typeof(Factory))]
                void Build() {
                    var rows = Parameter<IReadOnlyList<int>>();
                    var cols = Parameter<IReadOnlyList<int>>();
                    foreach (var r in rows)
                        foreach (var c in cols)        // nested live region -> unsupported in v1
                            System.Console.WriteLine(r);
                }
            }
            """);
        var diag = Assert.Single(diagnostics, d => d.Id == "SY1014");
        AssertHasRealSpan(diag);
    }
}

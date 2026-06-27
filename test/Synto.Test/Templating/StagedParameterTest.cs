using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Synto.Test.Templating;

/// <summary>
/// Exercises the <c>Synto.Templating.Template.Parameter&lt;T&gt;()</c> live-parameter marker (plan Task 1):
/// a <c>Parameter&lt;T&gt;()</c> call in a <c>[Template]</c> body lifts a depth-0 value to a factory
/// parameter (caller-supplied) rather than quoting it, mirroring an <c>[Inline]</c> value but with the
/// value originating as a factory parameter. Identity is <c>(name, T)</c>; naming errors are diagnostics.
/// </summary>
public class StagedParameterTest
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

    // Snapshot: declaration-initializer position — the binding names the parameter.
    [Fact]
    public async Task DeclarationParameter_LiftsToFactoryParameter()
    {
        await VerifyTemplate(
            """
            using Synto.Templating;
            using static Synto.Templating.Template;
            partial class Factory {}
            public class TestClass {
                [Template(typeof(Factory))]
                void Build() {
                    var count = Parameter<int>();   // -> factory param `int count`; used as a value below
                    System.Console.WriteLine(count);
                }
            }
            """);
    }

    // Snapshot: inline position — parameterName REQUIRED.
    [Fact]
    public async Task InlineParameter_WithExplicitName_Lifts()
    {
        await VerifyTemplate(
            """
            using Synto.Templating;
            using static Synto.Templating.Template;
            partial class Factory {}
            public class TestClass {
                [Template(typeof(Factory))]
                void Build() => System.Console.WriteLine(Parameter<int>("fieldCount"));
            }
            """);
    }

    // Diagnostic: inline with no binding and no name.
    [Fact]
    public void InlineParameter_MissingName_ReportsDiagnostic()
    {
        var diagnostics = RunAndGetDiagnostics(
            """
            using Synto.Templating;
            using static Synto.Templating.Template;
            partial class Factory {}
            public class TestClass {
                [Template(typeof(Factory))]
                void Build() => System.Console.WriteLine(Parameter<int>());
            }
            """);
        var diag = Assert.Single(diagnostics, d => d.Id == "SY1010"); // missing-parameter-name (Task 0 §8)
        AssertHasRealSpan(diag);
    }

    // Diagnostic: two explicit-named sites collide on the same (name, T).
    [Fact]
    public void ExplicitNameCollision_ReportsDiagnostic()
    {
        var diagnostics = RunAndGetDiagnostics(
            """
            using Synto.Templating;
            using static Synto.Templating.Template;
            partial class Factory {}
            public class TestClass {
                [Template(typeof(Factory))]
                void Build() {
                    System.Console.WriteLine(Parameter<int>("dup"));
                    System.Console.WriteLine(Parameter<int>("dup"));
                }
            }
            """);
        var diag = Assert.Single(diagnostics, d => d.Id == "SY1011"); // explicit-name collision (Task 0 §8)
        AssertHasRealSpan(diag);
    }

    // Diagnostic: same name, different T.
    [Fact]
    public void ConflictingParameterType_ReportsDiagnostic()
    {
        var diagnostics = RunAndGetDiagnostics(
            """
            using Synto.Templating;
            using static Synto.Templating.Template;
            partial class Factory {}
            public class TestClass {
                [Template(typeof(Factory))]
                void Build() {
                    var a = Parameter<int>("x");
                    var b = Parameter<string>("x");
                    System.Console.WriteLine($"{a}{b}");
                }
            }
            """);
        var diag = Assert.Single(diagnostics, d => d.Id == "SY1012"); // conflicting-(name,T) (Task 0 §8)
        AssertHasRealSpan(diag);
    }
}

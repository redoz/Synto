using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Synto.Generators.Templating;
namespace Synto.Test.Templating;

/// <summary>
/// Plan Task 1 (#3): <see cref="TemplateInfo.Create"/> runs inside the generator's catch-convert net and
/// guards a malformed <c>Options</c> named argument (an error-kind constant, or a boxed value that is not an
/// <c>int</c>/<c>TemplateOption</c>). A mid-keystroke unresolvable <c>Options</c> expression must not crash the
/// generator (no thrown exception, no CS8785); a well-formed <c>Options</c> still takes effect.
/// </summary>
public class TemplateInfoGuardTest
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
                MetadataReference.CreateFromFile(typeof(System.Console).Assembly.Location),
                MetadataReference.CreateFromFile(SyntoCoreAssembly.Location),
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private readonly Compilation _baseCompilation = CreateCompilation();
    private readonly GeneratorDriver _driver = CSharpGeneratorDriver.Create(new TemplateFactorySourceGenerator());

    private GeneratorRunResult RunSingle(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var compilation = _baseCompilation.AddSyntaxTrees(syntaxTree);
        var result = _driver.RunGenerators(compilation).GetRunResult();
        return Assert.Single(result.Results);
    }

    // A malformed Options named argument (an unresolvable expression, Kind == Error) must not NRE the transform.
    [Fact]
    public void MalformedOptions_DoesNotCrashGenerator()
    {
        var runResult = RunSingle(
            """
            using Synto.Templating;
            partial class Factory {}
            public class TestClass {
                [Template<Factory>(Options = (Synto.Templating.TemplateOption)nonexistent)]
                void Build() {}
            }
            """);

        Assert.Null(runResult.Exception);
        Assert.DoesNotContain(runResult.Diagnostics, d => d.Id == "CS8785");
    }

    // Guard against over-skipping: a well-formed Options = Bare still produces the Bare-shaped factory.
    [Fact]
    public void WellFormedBareOptions_StillTakesEffect()
    {
        var runResult = RunSingle(
            """
            using Synto.Templating;
            partial class Factory {}
            public class TestClass {
                [Template<Factory>(Options = TemplateOption.Bare)]
                int Build() => 1 + 2;
            }
            """);

        Assert.Null(runResult.Exception);
        Assert.DoesNotContain(runResult.Diagnostics, d => d.Id == "CS8785");
        // Bare emits the factory body directly (no wrapping method-declaration quote); at minimum it produces output.
        Assert.NotEmpty(runResult.GeneratedSources);
    }
}

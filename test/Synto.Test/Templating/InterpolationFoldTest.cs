using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Synto.Test.Templating;

/// <summary>
/// Durable proof of the interpolation staged-fold (spec 2026-06-28): a BARE staged-string interpolation hole
/// (no alignment/format clause, in a regular <c>$"…"</c> string) is baked into its surrounding literal text at
/// factory-build time via <c>value.ToInterpolatedText()</c>, instead of being re-emitted as a runtime hole.
/// Runtime output is unchanged — only the generated factory source changes. Harness modeled on
/// <see cref="SimpleTemplateTest"/>.
/// </summary>
public class InterpolationFoldTest
{
    private static readonly MetadataReference CorlibReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
    private static readonly MetadataReference NetStandardReference = MetadataReference.CreateFromFile(Assembly.Load("netstandard, Version=2.0.0.0").Location);
    private static readonly MetadataReference SystemRuntimeReference = MetadataReference.CreateFromFile(Assembly.Load("System.Runtime, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location);

    private static CSharpCompilation CreateCompilation()
    {
        return CSharpCompilation.Create("Test",
            Array.Empty<SyntaxTree>(),
            references:
            [
                CorlibReference,
                NetStandardReference,
                SystemRuntimeReference,
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                MetadataReference.CreateFromFile(SyntoCoreAssembly.Location)
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
    }

    private readonly Compilation _baseCompilation;
    private readonly GeneratorDriver _driver;

    public InterpolationFoldTest()
    {
        _baseCompilation = CreateCompilation();
        _driver = CSharpGeneratorDriver.Create(new TemplateFactorySourceGenerator());
    }

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

    [Fact]
    public async Task BareStagedString_ParameterRoot_FoldsIntoLiteralText()
    {
        // `label` is a string-typed Parameter<string>() staged root; `i` is a genuine runtime parameter.
        // Expected: {label} is baked into the surrounding literal text (fused via label.ToInterpolatedText());
        // {i} stays a runtime hole.
        await VerifyTemplate(
            """
            using System;
            using Synto.Templating;
            using static Synto.Templating.Template;

            partial class Factory {}

            public class TestClass {
                [Template(typeof(Factory))]
                void LocalFunction(int i) {
                    var label = Parameter<string>();
                    Console.WriteLine($"Field {i} is not {label} column.");
                }
            }
            """
        );
    }

    [Fact]
    public async Task BareStagedString_UnquoteLocal_FoldsIntoLiteralText()
    {
        // `label` is a string-typed Unquote<string>(...) staged local; `i` is a genuine runtime parameter.
        // Same fold as the Parameter<string> path (the second, independently-fallible channel-population path).
        await VerifyTemplate(
            """
            using System;
            using Synto.Templating;
            using static Synto.Templating.Template;

            partial class Factory {}

            public class TestClass {
                [Template(typeof(Factory))]
                void LocalFunction(int i) {
                    var label = Unquote<string>("Boolean");
                    Console.WriteLine($"Field {i} is not {label} column.");
                }
            }
            """
        );
    }
}

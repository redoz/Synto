using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Synto.Test.Templating;

/// <summary>
/// Exercises the live bound roots of the live-staged surface (plan Task 2): a <c>Template.Live&lt;T&gt;()</c>
/// local runs its bound expression at factory-build time (a real runtime local hoisted into the factory) and
/// lifts the resulting value into the produced syntax; a <c>[Live]</c> method parameter is supplied to the
/// factory at invocation time and lifted the same way. Both are depth-0 here; staging over control flow
/// arrives in later tasks.
/// </summary>
public class LiveRootTest
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

    // Snapshot: a live local runs in the factory and its value lifts into the output.
    [Fact]
    public async Task LiveLocal_RunsInFactory_AndLiftsValue()
    {
        await VerifyTemplate(
            """
            using Synto.Templating;
            using static Synto.Templating.Template;
            partial class Factory {}
            public class TestClass {
                [Template(typeof(Factory))]
                void Build() {
                    var n = Live(2 + 3);           // runs at factory time -> 5
                    System.Console.WriteLine(n);   // lifts to literal 5 in the OUTPUT
                }
            }
            """);
    }

    // Snapshot: a [Live] parameter lifts to a caller-supplied factory parameter (depth-0 == an [Inline] value).
    [Fact]
    public async Task LiveParameter_LiftsToFactoryParameter()
    {
        await VerifyTemplate(
            """
            using Synto.Templating;
            partial class Factory {}
            public class TestClass {
                [Template(typeof(Factory))]
                void Build([Live] int count) {
                    System.Console.WriteLine(count);
                }
            }
            """);
    }
}

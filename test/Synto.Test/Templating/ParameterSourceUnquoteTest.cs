using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Synto.Test.Templating;

/// <summary>
/// Exercises the parameter-source <c>Template.Unquote&lt;T&gt;(string? parameterName = null)</c> overload
/// (authoring-surface taxonomy §3.1 rename of <c>Parameter&lt;T&gt;()</c>): an in-body <c>Unquote&lt;T&gt;()</c>
/// call with no value / a string name binds the parameter-source overload and lifts to a caller-supplied factory
/// parameter — recognized by the BOUND OVERLOAD SYMBOL, never by method name (both overloads are named
/// <c>Unquote</c>). The value-source <c>Unquote&lt;T&gt;(T value)</c> form must still register a factory-build-time
/// local (regression guard for the disambiguation).
/// </summary>
public class ParameterSourceUnquoteTest
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

    private string RunAndGetGeneratedText(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        Assert.Empty(syntaxTree.GetDiagnostics());
        var compilation = _baseCompilation.AddSyntaxTrees(syntaxTree);
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        var result = _driver.RunGenerators(compilation);
        var runResult = result.GetRunResult();
        return string.Join("\n\n", runResult.GeneratedTrees.Select(t => t.ToString()));
    }

    // Declaration position: `var count = Unquote<int>();` — the bound variable name supplies the factory
    // parameter name. Must lift to `Build(int count)` (parameter-source), NOT a `syntaxForStaged_` local.
    [Fact]
    public void ParameterSourceUnquote_Declaration_LiftsToFactoryParameter()
    {
        var generated = RunAndGetGeneratedText(
            """
            using Synto.Templating;
            using static Synto.Templating.Template;
            partial class Factory {}
            public class TestClass {
                [Template<Factory>]
                void Build() {
                    var count = Unquote<int>();
                    System.Console.WriteLine(count);
                }
            }
            """);

        Assert.Contains("Build(int count)", generated, System.StringComparison.Ordinal);
        Assert.Contains("syntaxForParam_count", generated, System.StringComparison.Ordinal);
        Assert.DoesNotContain("syntaxForStaged_count", generated, System.StringComparison.Ordinal);
    }

    // Inline position with an explicit name: `Unquote<int>("fieldCount")` -> `Build(int fieldCount)`.
    [Fact]
    public void ParameterSourceUnquote_InlineExplicitName_Lifts()
    {
        var generated = RunAndGetGeneratedText(
            """
            using Synto.Templating;
            using static Synto.Templating.Template;
            partial class Factory {}
            public class TestClass {
                [Template<Factory>]
                void Build() => System.Console.WriteLine(Unquote<int>("fieldCount"));
            }
            """);

        Assert.Contains("Build(int fieldCount)", generated, System.StringComparison.Ordinal);
    }

    // T == string boundary: a positional string is ambiguous by design, so the parameter overload is bound
    // explicitly via `parameterName:`. Must lift to `Build(string clrType)`.
    [Fact]
    public void ParameterSourceUnquote_StringType_ViaNamedArg_Lifts()
    {
        var generated = RunAndGetGeneratedText(
            """
            using Synto.Templating;
            using static Synto.Templating.Template;
            partial class Factory {}
            public class TestClass {
                [Template<Factory>]
                void Build() => System.Console.WriteLine(Unquote<string>(parameterName: "clrType"));
            }
            """);

        Assert.Contains("Build(string clrType)", generated, System.StringComparison.Ordinal);
    }

    // Regression guard: the value-source `Unquote(expr)` overload still registers a factory-build-time LOCAL
    // (a `syntaxForStaged_` lift on a parameterless factory), NOT a factory parameter.
    [Fact]
    public void ValueSourceUnquote_StillRegistersLocal()
    {
        var generated = RunAndGetGeneratedText(
            """
            using Synto.Templating;
            using static Synto.Templating.Template;
            partial class Factory {}
            public class TestClass {
                [Template<Factory>]
                void Build() {
                    var n = Unquote(2 + 3);
                    System.Console.WriteLine(n);
                }
            }
            """);

        Assert.Contains("syntaxForStaged_n", generated, System.StringComparison.Ordinal);
        Assert.Contains("public static MethodDeclarationSyntax Build()", generated, System.StringComparison.Ordinal);
    }

    // Regression guard: the explicitly-named value-source overload `Unquote<T>(value: expr)` is also a LOCAL.
    [Fact]
    public void ValueSourceUnquote_NamedValueArg_StillRegistersLocal()
    {
        var generated = RunAndGetGeneratedText(
            """
            using Synto.Templating;
            using static Synto.Templating.Template;
            partial class Factory {}
            public class TestClass {
                [Template<Factory>]
                void Build() {
                    var n = Unquote<int>(value: 2 + 3);
                    System.Console.WriteLine(n);
                }
            }
            """);

        Assert.Contains("syntaxForStaged_n", generated, System.StringComparison.Ordinal);
        Assert.Contains("public static MethodDeclarationSyntax Build()", generated, System.StringComparison.Ordinal);
    }
}

using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Synto.Test.Templating;

/// <summary>
/// Exercises the <c>[Runtime]</c> value-to-syntax converter pluggability: an <c>[Unquote]</c> parameter of a
/// user's own (non-built-in) type is converted to syntax by a user-authored static class marked
/// <c>[Runtime]</c> exposing <c>ExpressionSyntax ToSyntax(this T)</c>. The generator discovers that converter
/// from the inlined parameter's TYPE and injects it as a <c>file static class</c> into the generated factory
/// The user's converter is called DIRECTLY by its fully-qualified name (not copied file-local — that would
/// collide with the user's own copy under CS0121), so the generated output keeps no Synto runtime dependency.
/// </summary>
public class RuntimeConverterTest
{
    private static readonly MetadataReference CorlibReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
    private static readonly MetadataReference NetStandardReference = MetadataReference.CreateFromFile(Assembly.Load("netstandard, Version=2.0.0.0").Location);
    private static readonly MetadataReference SystemRuntimeReference = MetadataReference.CreateFromFile(Assembly.Load("System.Runtime, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location);

    private static readonly MetadataReference[] References =
    [
        CorlibReference,
        NetStandardReference,
        SystemRuntimeReference,
        MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
        // The Roslyn assemblies a user converter references (ExpressionSyntax, SyntaxFactory, SyntaxKind, ...).
        MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.SyntaxNode).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.CSharp.SyntaxKind).Assembly.Location),
        MetadataReference.CreateFromFile(Assembly.Load("System.Linq, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
        MetadataReference.CreateFromFile(Assembly.Load("System.Collections, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
        MetadataReference.CreateFromFile(Assembly.Load("System.Runtime.Extensions, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
        // The PUBLIC Synto.Core surface (markers) the consumer binds [Template]/[Unquote]/[Runtime] against.
        MetadataReference.CreateFromFile(SyntoCoreAssembly.Location),
    ];

    private static Compilation CompilationWithSource(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        Assert.Empty(syntaxTree.GetDiagnostics());

        var compilation = CSharpCompilation.Create("Test",
            [syntaxTree],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        Assert.Empty(compilation.GetDiagnostics().Where(diag => diag.Severity == DiagnosticSeverity.Error));
        return compilation;
    }

    // A consumer that inlines a custom type (Color) AND a built-in (int), so the generated factory needs BOTH
    // the user converter (for Color) and the file-local LiteralSyntaxExtensions (for int).
    private const string ConverterTemplate =
        """
        using System;
        using Microsoft.CodeAnalysis;
        using Microsoft.CodeAnalysis.CSharp;
        using Microsoft.CodeAnalysis.CSharp.Syntax;
        using Synto.Templating;
        using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

        partial class Factory {}

        public readonly struct Color
        {
            public Color(int argb) { Argb = argb; }
            public int Argb { get; }
        }

        [Runtime]
        public static class ColorConverter
        {
            public static ExpressionSyntax ToSyntax(this Color color) =>
                ObjectCreationExpression(IdentifierName("Color"))
                    .AddArgumentListArguments(
                        Argument(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(color.Argb))));
        }

        public class TestClass {
            [Template(typeof(Factory))]
            void Build([Unquote] Color color, [Unquote] int count) {
                Console.WriteLine(color);
                Console.WriteLine(count);
            }
        }
        """;

    [Fact]
    public async Task InlinesCustomTypeWithRuntimeConverter()
    {
        var compilation = CompilationWithSource(ConverterTemplate);

        var driver = CSharpGeneratorDriver.Create(new TemplateFactorySourceGenerator())
            .RunGenerators(compilation, TestContext.Current.CancellationToken);

        // The discovered converter is called directly by its fully-qualified name (no file-local copy).
        var generated = driver.GetRunResult().GeneratedTrees
            .Single(t => t.FilePath.Contains("Factory.Build"))
            .ToString();
        Assert.Contains("global::ColorConverter.ToSyntax(color)", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("class ColorConverter", generated, StringComparison.Ordinal);

        var ret = await Verify(driver).UseDirectory("snapshots");
        Assert.NotEmpty(ret.Files);
    }

    // A consumer that QUOTES a custom type (Color) — the value-lift goes through the SAME [Runtime] converter
    // discovery as [Unquote], so a [Quote] of a custom type emits the fully-qualified converter call, NOT a
    // literal. This guards the "[Literal] would be a misnomer" rationale (spec §7): [Quote] lifts ANY value
    // through its converter, it is not literal-only.
    private const string QuoteConverterTemplate =
        """
        using System;
        using Microsoft.CodeAnalysis;
        using Microsoft.CodeAnalysis.CSharp;
        using Microsoft.CodeAnalysis.CSharp.Syntax;
        using Synto.Templating;
        using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

        partial class Factory {}

        public readonly struct Color
        {
            public Color(int argb) { Argb = argb; }
            public int Argb { get; }
        }

        [Runtime]
        public static class ColorConverter
        {
            public static ExpressionSyntax ToSyntax(this Color color) =>
                ObjectCreationExpression(IdentifierName("Color"))
                    .AddArgumentListArguments(
                        Argument(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(color.Argb))));
        }

        public class TestClass {
            [Template(typeof(Factory))]
            void Build([Quote] Color color) {
                Console.WriteLine(color);
            }
        }
        """;

    [Fact]
    public async Task QuoteCustomType_EmitsRuntimeConverterCall()
    {
        var compilation = CompilationWithSource(QuoteConverterTemplate);

        var driver = CSharpGeneratorDriver.Create(new TemplateFactorySourceGenerator())
            .RunGenerators(compilation, TestContext.Current.CancellationToken);

        var generated = driver.GetRunResult().GeneratedTrees
            .Single(t => t.FilePath.Contains("Factory.Build"))
            .ToString();

        // The quoted custom-type value is lifted via the discovered converter (called fully-qualified), NOT as a
        // literal and NOT with a file-local copy of the converter.
        Assert.Contains("global::ColorConverter.ToSyntax(color)", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("class ColorConverter", generated, StringComparison.Ordinal);

        var ret = await Verify(driver).UseDirectory("snapshots");
        Assert.NotEmpty(ret.Files);
    }

    [Fact]
    public void GeneratedConverterFactoryCompiles()
    {
        // The real round-trip proof: compile the post-generation output and assert there are no errors. In
        // particular the emitted `color.ToSyntax()` must bind to the file-local converter (a non-generic
        // ToSyntax(this Color)) and never the generic fallback or a CS0121 ambiguity.
        var compilation = CompilationWithSource(ConverterTemplate);

        var driver = CSharpGeneratorDriver.Create(new TemplateFactorySourceGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics, TestContext.Current.CancellationToken);

        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains(output.SyntaxTrees, t => t.FilePath.Contains("Factory.Build"));

        var errors = output.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.DoesNotContain(errors, d => d.Id == "CS0121");
        Assert.True(errors.Count == 0,
            "Post-generation compilation reported errors: " + string.Join("; ", errors.Select(d => d.Id + " " + d.GetMessage())));
    }

    [Fact]
    public void NoRuntimeConverterReportsSY1008()
    {
        // Inlining a custom type with NO [Runtime] converter must surface a clean SY1008 diagnostic at
        // generation time (located at the offending parameter type), instead of letting the generic fallback
        // throw NotImplementedException at the author's runtime.
        var diagnostics = RunAndGetDiagnostics(
            """
            using System;
            using Synto.Templating;

            partial class Factory {}

            public readonly struct Color { public int Argb { get; init; } }

            public class TestClass {
                [Template(typeof(Factory))]
                void Build([Unquote] Color color) {
                    Console.WriteLine(color);
                }
            }
            """);

        var diag = Assert.Single(diagnostics, d => d.Id == "SY1008");
        AssertHasRealSpan(diag);
    }

    [Fact]
    public void AmbiguousRuntimeConvertersReportSY1009()
    {
        // Two [Runtime] converters both defining ToSyntax(this Color) is ambiguous; the generator must
        // diagnose (SY1009) rather than silently pick one.
        var diagnostics = RunAndGetDiagnostics(
            """
            using System;
            using Microsoft.CodeAnalysis;
            using Microsoft.CodeAnalysis.CSharp;
            using Microsoft.CodeAnalysis.CSharp.Syntax;
            using Synto.Templating;
            using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

            partial class Factory {}

            public readonly struct Color { public int Argb { get; init; } }

            [Runtime]
            public static class ConvA {
                public static ExpressionSyntax ToSyntax(this Color color) => IdentifierName("a");
            }

            [Runtime]
            public static class ConvB {
                public static ExpressionSyntax ToSyntax(this Color color) => IdentifierName("b");
            }

            public class TestClass {
                [Template(typeof(Factory))]
                void Build([Unquote] Color color) {
                    Console.WriteLine(color);
                }
            }
            """);

        var diag = Assert.Single(diagnostics, d => d.Id == "SY1009");
        AssertHasRealSpan(diag);
    }

    [Fact]
    public void GeneratorIsIncrementalWithRuntimeConverter()
    {
        // Cacheability guard for the converter path: discovery + injection happen entirely inside the
        // ForAttributeWithMetadataName transform and only equatable values (generated text + diagnostics)
        // flow through the pipeline, so an edit in an unrelated tree must leave every tracked step
        // Cached/Unchanged.
        var compilation = CompilationWithSource(ConverterTemplate);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { new TemplateFactorySourceGenerator().AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);

        var modified = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("namespace Other { internal sealed class Unrelated { } }"));
        driver = driver.RunGenerators(modified, TestContext.Current.CancellationToken);

        var result = driver.GetRunResult().Results.Single();

        CacheabilityAssert.AllStepsCachedOrUnchanged(
            result,
            new[] { TemplateTrackingNames.Transform, TemplateTrackingNames.Result });
    }

    private static void AssertHasRealSpan(Diagnostic diag)
    {
        Assert.NotEqual(Location.None, diag.Location);
        Assert.False(diag.Location.SourceSpan.IsEmpty);
    }

    private static ImmutableArray<Diagnostic> RunAndGetDiagnostics(string source)
    {
        var compilation = CompilationWithSource(source);
        var driver = CSharpGeneratorDriver.Create(new TemplateFactorySourceGenerator());
        var result = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        return result.GetRunResult().Diagnostics;
    }
}

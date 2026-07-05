using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Synto.Test.Templating;

/// <summary>
/// Plan "unquote-param-lift-on-splice" — a <c>[Unquote] T value</c>-parameter now means the same thing as the
/// in-body <c>Unquote&lt;T&gt;()</c> spelling: lift-on-splice, control-without-lift. A converterless, control-only
/// <c>[Unquote]</c> param (every reference consumed by a live region) compiles clean and drives unrolling — it no
/// longer dies with SY1008, because the shared <see cref="Synto.Generators.Templating.Lifting.StagedRootLifter"/>
/// core only demands a converter when the depth-zero partition is non-empty.
/// </summary>
public class UnquoteParamLiftOnSpliceTest
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
        // The Roslyn assemblies a user [Runtime] converter references (ExpressionSyntax, SyntaxFactory, ...).
        MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.SyntaxNode).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.CSharp.SyntaxKind).Assembly.Location),
        MetadataReference.CreateFromFile(Assembly.Load("System.Linq, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
        MetadataReference.CreateFromFile(Assembly.Load("System.Collections, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
        MetadataReference.CreateFromFile(Assembly.Load("System.Runtime.Extensions, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
        // The PUBLIC Synto.Core surface (markers) the consumer binds [Template]/[Unquote]/[Runtime] against.
        MetadataReference.CreateFromFile(SyntoCoreAssembly.Location),
    ];

    private static CSharpCompilation CreateCompilation() =>
        CSharpCompilation.Create("Test",
            [],
            references: References,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private readonly Compilation _baseCompilation = CreateCompilation();
    private readonly GeneratorDriver _driver = CSharpGeneratorDriver.Create(new TemplateFactorySourceGenerator());

    private Compilation CompilationWithSource(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        Assert.Empty(syntaxTree.GetDiagnostics());
        return _baseCompilation.AddSyntaxTrees(syntaxTree);
    }

    // Spec §2.1 "Control-only": a converterless [Unquote] param whose every reference is
    // control-consumed compiles clean, drives unrolling, and never asks for a converter. (Was SY1008.)
    private const string ControlOnlySource =
        """
        using Synto.Templating;
        using System.Collections.Generic;
        partial class Factory {}
        public class TestClass {
            [Template<Factory>]
            void Build([Unquote] IReadOnlyList<int> ordinals) {
                foreach (var o in ordinals)
                    System.Console.WriteLine(o);
            }
        }
        """;

    [Fact]
    public async Task ControlOnlyUnquoteParam_NoConverter_CompilesAndUnrolls()
    {
        var compilation = CompilationWithSource(ControlOnlySource);
        var result = _driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        var runResult = result.GetRunResult();

        // No SY1008/no errors: the value is never spliced, so no converter is required.
        Assert.Empty(runResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var doc = runResult.Results.SelectMany(r => r.GeneratedSources)
            .Single(s => s.HintName.Contains("Factory.Build", StringComparison.Ordinal))
            .SourceText.ToString();

        // The caller-supplied factory parameter exists (fully-qualified declared type)...
        Assert.Contains("IReadOnlyList<int> ordinals)", doc, StringComparison.Ordinal);
        // ...drives a factory-time (scaffold) foreach, not a quoted output loop...
        Assert.Contains("foreach (var o in ordinals)", doc, StringComparison.Ordinal);
        Assert.DoesNotContain("ForEachStatement", doc, StringComparison.Ordinal);
        // ...and no value lift is emitted at all (no preamble local, no ToSyntax on the param).
        Assert.DoesNotContain("syntaxForParam_ordinals", doc, StringComparison.Ordinal);

        var ret = await Verify(result).UseDirectory("snapshots");
        Assert.NotEmpty(ret.Files);
    }

    // Spec §2.1 "Mixed": one [Unquote] param both drives a live foreach (control) and is spliced
    // (depth-zero). Depth-zero lifts via the [Runtime] converter; control refs rewrite to the raw
    // factory parameter and the loop unrolls.
    private const string MixedWithConverterSource =
        """
        using System;
        using System.Collections.Generic;
        using Microsoft.CodeAnalysis;
        using Microsoft.CodeAnalysis.CSharp.Syntax;
        using Synto.Templating;
        using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
        partial class Factory {}
        [Runtime]
        public static class OrdinalsConverter {
            public static ExpressionSyntax ToSyntax(this IReadOnlyList<int> value) => IdentifierName("ords");
        }
        public class TestClass {
            [Template<Factory>]
            void Build([Unquote] IReadOnlyList<int> ordinals) {
                foreach (var o in ordinals)
                    Console.WriteLine(o);
                Console.WriteLine(ordinals);   // depth-zero -> lifts via the converter
            }
        }
        """;

    [Fact]
    public async Task MixedUnquoteParam_ControlAndSpliced_LiftsDepthZeroAndUnrolls()
    {
        var compilation = CompilationWithSource(MixedWithConverterSource);
        var result = _driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        var runResult = result.GetRunResult();

        // The spliced (depth-zero) reference has a converter, so no error is reported.
        Assert.Empty(runResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var doc = runResult.Results.SelectMany(r => r.GeneratedSources)
            .Single(s => s.HintName.Contains("Factory.Build", StringComparison.Ordinal))
            .SourceText.ToString();

        // Depth-zero reference lifts via the discovered converter into the preamble local...
        Assert.Contains("global::OrdinalsConverter.ToSyntax(ordinals)", doc, StringComparison.Ordinal);
        Assert.Contains("syntaxForParam_ordinals", doc, StringComparison.Ordinal);
        // ...while the control reference drives a factory-time (scaffold) foreach, not a quoted output loop.
        Assert.Contains("foreach (var o in ordinals)", doc, StringComparison.Ordinal);
        Assert.DoesNotContain("ForEachStatement", doc, StringComparison.Ordinal);

        var ret = await Verify(result).UseDirectory("snapshots");
        Assert.NotEmpty(ret.Files);
    }

    // Spec §2.1 "Spliced-anywhere, no converter": the SAME template minus the [Runtime] converter. The
    // depth-zero splice (`Console.WriteLine(ordinals)`) genuinely requires a converter, so control usage
    // elsewhere must NOT mask it — exactly one SY1008, with a real span.
    private const string MixedNoConverterSource =
        """
        using System;
        using System.Collections.Generic;
        using Synto.Templating;
        partial class Factory {}
        public class TestClass {
            [Template<Factory>]
            void Build([Unquote] IReadOnlyList<int> ordinals) {
                foreach (var o in ordinals)
                    Console.WriteLine(o);
                Console.WriteLine(ordinals);   // depth-zero -> converter genuinely required
            }
        }
        """;

    [Fact]
    public void MixedUnquoteParam_SplicedWithoutConverter_ReportsSY1008()
    {
        var diagnostics = RunAndGetDiagnostics(MixedNoConverterSource);

        var diag = Assert.Single(diagnostics, d => d.Id == "SY1008");
        AssertHasRealSpan(diag);
    }

    [Fact]
    public void GeneratorIsIncrementalForControlOnlyUnquoteParam()
    {
        // Cacheability guard for the control-only shape: the shared StagedRootLifter partition + RootNames
        // wiring all run inside the transform and only equatable values flow through the pipeline, so an edit
        // in an unrelated tree must leave every tracked step Cached/Unchanged.
        var compilation = CompilationWithSource(ControlOnlySource);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { new TemplateFactorySourceGenerator().AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);

        var modified = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("namespace Other { internal sealed class Unrelated { } }", cancellationToken: TestContext.Current.CancellationToken));
        driver = driver.RunGenerators(modified, TestContext.Current.CancellationToken);

        var result = driver.GetRunResult().Results.Single();

        CacheabilityAssert.AllStepsCachedOrUnchanged(
            result,
            new[] { TemplateTrackingNames.GenericTransform, TemplateTrackingNames.GenericResult });
    }

    private static void AssertHasRealSpan(Diagnostic diag)
    {
        Assert.NotEqual(Location.None, diag.Location);
        Assert.False(diag.Location.SourceSpan.IsEmpty);
    }

    private ImmutableArray<Diagnostic> RunAndGetDiagnostics(string source)
    {
        var compilation = CompilationWithSource(source);
        var result = _driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        return result.GetRunResult().Diagnostics;
    }
}

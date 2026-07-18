using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Synto.Test.Model;

/// <summary>
/// The incremental-cacheability guard for the <c>[SyntoModel]</c> generator (spec §Verification 2): the
/// pipeline carries only equatable value types (the shared <c>GenerationResult</c> / <c>DiagnosticInfo</c> /
/// <c>EquatableArray</c>) and captures no <c>Compilation</c>/<c>SemanticModel</c>/<c>ISymbol</c>/<c>SyntaxNode</c>,
/// so an edit in an unrelated tree leaves every tracked step <c>Cached</c>/<c>Unchanged</c>. At this task the
/// emitter is a STUB (a null-Source result), so we assert only on diagnostics + cacheability, not on
/// generated trees.
/// </summary>
public class ModelCacheabilityTests
{
    private const string WellFormedSpec =
        """
        using Synto.Model;

        [SyntoModel]
        class PropModelShape : SyntoModel<Microsoft.CodeAnalysis.IPropertySymbol>
        {
            string Name => Symbol.Name;
        }
        """;

    [Fact]
    public void WellFormedSpec_ReportsNoDiagnostics()
    {
        // A well-formed [SyntoModel] spec passes the stub transform with no diagnostics. The emitter is a stub
        // here — do NOT assert on GeneratedTrees (the emission proof lands in Task 5).
        var result = ModelTestHarness.Run(WellFormedSpec);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Generator_IsIncremental_OnUnrelatedEdit()
    {
        // Cacheability guard: the [SyntoModel] tree is byte-identical after an unrelated edit, so its pipeline
        // results must come from cache. The Result step caches a null-Source GenerationResult (the stub emits
        // nothing).
        var compilation = ModelTestHarness.CreateCompilation(WellFormedSpec);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { new SyntoModelGenerator().AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation, Xunit.TestContext.Current.CancellationToken);

        var modified = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("namespace Other { internal sealed class Unrelated { } }", cancellationToken: Xunit.TestContext.Current.CancellationToken));
        driver = driver.RunGenerators(modified, Xunit.TestContext.Current.CancellationToken);

        var result = driver.GetRunResult().Results.Single();

        CacheabilityAssert.AllStepsCachedOrUnchanged(
            result,
            new[] { ModelTrackingNames.Transform, ModelTrackingNames.Result });
    }

    [Fact]
    public void Generator_IsIncremental_OnUnrelatedEdit_ForDiagnosticReportingSpec()
    {
        // Cacheability under diagnostics: a spec that REPORTS a cache-safety violation (SY1036) must still ride the
        // cache. The violation is carried as an equatable DiagnosticInfo in the GenerationResult, so an unrelated
        // edit leaves every tracked step Cached/Unchanged (no Compilation/ISymbol/Location captured into state).
        const string diagnosticSpec =
            """
            using Synto.Model;
            using Microsoft.CodeAnalysis;

            [SyntoModel]
            class BadShape : SyntoModel<INamedTypeSymbol>
            {
                INamedTypeSymbol Bad => Symbol;
            }
            """;

        var compilation = ModelTestHarness.CreateCompilation(diagnosticSpec);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { new SyntoModelGenerator().AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation, Xunit.TestContext.Current.CancellationToken);

        var modified = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("namespace Other { internal sealed class Unrelated { } }", cancellationToken: Xunit.TestContext.Current.CancellationToken));
        driver = driver.RunGenerators(modified, Xunit.TestContext.Current.CancellationToken);

        var result = driver.GetRunResult().Results.Single();

        CacheabilityAssert.AllStepsCachedOrUnchanged(
            result,
            new[] { ModelTrackingNames.Transform, ModelTrackingNames.Result });
    }
}

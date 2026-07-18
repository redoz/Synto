using Microsoft.CodeAnalysis;
using Synto.Test.Match;

namespace Synto.Test.Generators;

/// <summary>
/// Proves the injected generator-author utilities (the cacheability toolkit
/// <c>EquatableArray&lt;T&gt;</c> / <c>LocationInfo</c> / <c>DiagnosticInfo</c>, sourced from Synto's
/// existing internal files via a namespace-shifting <see cref="SurfaceInjectionGenerator"/> extension)
/// land in the locked <c>Synto.Generators</c> namespace and compile self-contained on netstandard2.0.
/// </summary>
public class InjectedGeneratorUtilitiesTests
{
    [Fact]
    public void InjectedCacheabilityTypes_LandInSyntoGeneratorsNamespace() // C-1 shift fired, scoped
    {
        var equatableArray = MatchTestHarness.InjectedSurfaceSource("struct EquatableArray");
        Assert.Contains("namespace Synto.Generators", equatableArray, System.StringComparison.Ordinal);

        var diagnostics = MatchTestHarness.InjectedSurfaceSource("record struct DiagnosticInfo");
        Assert.Contains("namespace Synto.Generators", diagnostics, System.StringComparison.Ordinal);
    }

    [Fact]
    public void InjectedCacheabilityToolkit_CompilesOn_NetStandard20() // C-2
    {
        var diagnostics = MatchTestHarness.CompileInjectedGeneratorUtilitiesOnNetStandard20();
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void InjectedDiagnosticBag_CompilesOn_NetStandard20() // C-2 extension: DiagnosticBag + Drain
    {
        var diagnostics = MatchTestHarness.CompileInjectedDiagnosticBagOnNetStandard20();
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void InjectedInterceptorsHelper_DefinesTheCanonicalAttribute() // C-4
    {
        var source = MatchTestHarness.InjectedSurfaceSource("class Interceptors");
        Assert.Contains("namespace System.Runtime.CompilerServices", source, System.StringComparison.Ordinal);
        Assert.Contains("class InterceptsLocationAttribute", source, System.StringComparison.Ordinal);
        Assert.Contains("InterceptsLocationAttribute(int version, string data)", source, System.StringComparison.Ordinal);
    }
}

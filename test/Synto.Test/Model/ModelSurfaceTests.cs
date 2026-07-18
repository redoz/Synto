using System;
using Microsoft.CodeAnalysis;
using Synto.Test.Match;

namespace Synto.Test.Model;

/// <summary>
/// Validates the <c>[SyntoModel]</c> consumer surface is injected as <c>internal</c> by
/// <see cref="ModelSurfaceInjector"/> (the <c>public</c>-&gt;<c>internal</c> rewrite that
/// <see cref="Synto.GenKit.SurfaceInjectorBase"/> performs).
/// </summary>
public class ModelSurfaceTests
{
    [Fact]
    public void SyntoModelSurface_IsInjected_AsInternal()
    {
        var src = ModelTestHarness.InjectedSurfaceSource("class SyntoModel<T>");

        Assert.Contains("internal", src, StringComparison.Ordinal);
        Assert.Contains("protected", src, StringComparison.Ordinal); // the Symbol member
    }

    [Fact]
    public void ProjectShim_IsInjected_AsInternal()
    {
        var src = ModelTestHarness.InjectedSurfaceSource("class Project");

        Assert.Contains("internal", src, StringComparison.Ordinal);
        Assert.Contains("Each", src, StringComparison.Ordinal);
        Assert.Contains("One", src, StringComparison.Ordinal);
    }

    /// <summary>
    /// Self-containment proof (spec §Verification 4): the injected <c>EquatableArray&lt;T&gt;</c> plus the
    /// new <c>ToEquatableArray()</c> extension compile clean on the pinned netstandard2.0 closure, and a
    /// consumer probe that calls <c>.ToEquatableArray()</c> binds — so generated <c>From</c> bodies can
    /// project sequences into the cache-safe array with no Synto runtime-package dependency.
    /// </summary>
    [Fact]
    public void InjectedToEquatableArray_CompilesAndResolves_OnNetStandard20()
    {
        var equatableArray = MatchTestHarness.InjectedSurfaceSource("struct EquatableArray");
        var extensions = MatchTestHarness.InjectedSurfaceSource("class EquatableArrayExtensions");
        const string probe = """
            using System.Collections.Generic;
            using Synto.Generators;

            internal static class ToEquatableArrayProbe
            {
                public static EquatableArray<string> Probe(IEnumerable<string> items) => items.ToEquatableArray();
            }
            """;

        var diagnostics = MatchTestHarness.CreateNetStandardClosure(equatableArray, extensions, probe)
            .GetDiagnostics(TestContext.Current.CancellationToken);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }
}

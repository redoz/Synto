using System;

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
}

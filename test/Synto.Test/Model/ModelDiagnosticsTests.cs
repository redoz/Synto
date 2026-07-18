using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Synto.Test.Model;

/// <summary>
/// Model feature diagnostics (the reserved <c>SY1036</c>–<c>SY1039</c> range, category
/// <c>Synto.Usage</c>). This suite grows as the cache-safety checks land (Tasks 5, 7, 8); at Task 3 it
/// pins only the registration-audit invariant — every descriptor the <c>SyntoModelGenerator</c> can raise
/// must be tracked in <c>src/Synto.Model/AnalyzerReleases.Unshipped.md</c> so the feature DLL stays
/// RS2008-clean.
/// </summary>
public class ModelDiagnosticsTests
{
    [Fact]
    public void ModelDescriptors_AreRegistered_InUnshippedReleases()
    {
        // Registration audit: every SY103x descriptor must be tracked in AnalyzerReleases.Unshipped.md so the
        // Model feature DLL stays RS2008-clean. Fails loudly if a later edit drops a registration.
        var content = File.ReadAllText(FindUnshippedReleasesPath());

        foreach (var id in new[] { "SY1036", "SY1037", "SY1038", "SY1039" })
            Assert.Contains(id, content, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordName_DerivedFromSpecName_StripsShapeSuffix()
    {
        // The record name is the spec-class name with a trailing `Shape` removed: PropModelShape -> PropModel.
        var result = ModelTestHarness.Run(
            """
            using Synto.Model;

            [SyntoModel]
            class PropModelShape : SyntoModel<Microsoft.CodeAnalysis.IPropertySymbol>
            {
                string Name => Symbol.Name;
            }
            """);

        Assert.Empty(result.Diagnostics);
        var tree = Assert.Single(result.GeneratedTrees.Where(t => t.ToString().Contains("record PropModel", StringComparison.Ordinal)));
        Assert.Contains("record PropModel", tree.ToString(), StringComparison.Ordinal);
        Assert.Contains("static PropModel From(", tree.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RecordName_FromAttributeName_OverridesSuffix()
    {
        // An explicit [SyntoModel(Name=...)] wins over the Shape-suffix rule.
        var result = ModelTestHarness.Run(
            """
            using Synto.Model;

            [SyntoModel(Name = "Widget")]
            class AnythingAtAll : SyntoModel<Microsoft.CodeAnalysis.IPropertySymbol>
            {
                string Name => Symbol.Name;
            }
            """);

        Assert.Empty(result.Diagnostics);
        Assert.Contains(result.GeneratedTrees, t => t.ToString().Contains("record Widget", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingSuffixAndName_ReportsSY1039_AndEmitsNoTree()
    {
        // No trailing `Shape` and no [SyntoModel(Name=...)] override: no record name can be derived -> SY1039,
        // and no record tree is emitted.
        var result = ModelTestHarness.Run(
            """
            using Synto.Model;

            [SyntoModel]
            class Foo : SyntoModel<Microsoft.CodeAnalysis.IPropertySymbol>
            {
                string Name => Symbol.Name;
            }
            """);

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "SY1039");
        AssertHasRealSpan(diag);
        Assert.DoesNotContain(result.GeneratedTrees, t => t.ToString().Contains("record ", StringComparison.Ordinal));
    }

    private static void AssertHasRealSpan(Diagnostic diag)
    {
        Assert.NotEqual(Location.None, diag.Location);
        Assert.False(diag.Location.SourceSpan.IsEmpty);
    }

    private static string FindUnshippedReleasesPath()
    {
        var directory = AppContext.BaseDirectory;
        while (directory is not null)
        {
            var candidate = Path.Combine(directory, "src", "Synto.Model", "AnalyzerReleases.Unshipped.md");
            if (File.Exists(candidate))
                return candidate;
            directory = Path.GetDirectoryName(directory);
        }

        throw new FileNotFoundException($"AnalyzerReleases.Unshipped.md not found walking up from {AppContext.BaseDirectory}");
    }
}

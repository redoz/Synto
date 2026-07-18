using System;
using System.IO;

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

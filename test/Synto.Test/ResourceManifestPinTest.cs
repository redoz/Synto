using System;
using System.Linq;

namespace Synto.Test;

/// <summary>
/// Pins the exact set of embedded manifest resources the generator assemblies ship. The set is enumerated
/// the same way each feature's <c>SurfaceInjectorBase</c> subclass and the helper loader read it — straight
/// off <c>Assembly.GetManifestResourceNames()</c> — so any later glob/embed change surfaces as a reviewable
/// Verify diff (a list-delta) rather than a silent glob footgun.
/// </summary>
public class ResourceManifestPinTest
{
    [Fact]
    public Task VerifyEmbeddedResourceManifest()
    {
        // The umbrella (Synto.Generators.dll) is reference-only: it carries no code and embeds no resources
        // (its former SurfaceInjectionGenerator was deleted in Task 10). Load it by name to pin that it stays
        // resource-free — every marker surface now lives in a feature DLL, pinned below.
        var assembly = System.Reflection.Assembly.Load("Synto.Generators");

        var resourceNames = assembly.GetManifestResourceNames()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        return Verify(string.Join("\n", resourceNames)).UseDirectory("snapshots");
    }

    // The cacheable trio (EquatableArray/DiagnosticInfo) moved into Synto.Incremental.dll (Task 4), which
    // now owns the two Synto.Runtime.* trio resources its lone IncrementalSurfaceInjector injects. Pin that
    // assembly's manifest too so the per-assembly resource split stays a reviewable Verify diff.
    [Fact]
    public Task VerifyIncrementalEmbeddedResourceManifest()
    {
        var assembly = typeof(IncrementalSurfaceInjector).Assembly;

        var resourceNames = assembly.GetManifestResourceNames()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        return Verify(string.Join("\n", resourceNames)).UseDirectory("snapshots");
    }

    // The Template marker surface + emit helpers (all Synto.Runtime.* / Synto.Helper.* resources except the
    // Match markers) moved into Synto.Template.dll (Task 7), whose TemplateSurfaceInjector injects them. Pin
    // that assembly's manifest too so the per-assembly resource split stays a reviewable Verify diff.
    [Fact]
    public Task VerifyTemplateEmbeddedResourceManifest()
    {
        var assembly = typeof(TemplateSurfaceInjector).Assembly;

        var resourceNames = assembly.GetManifestResourceNames()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        return Verify(string.Join("\n", resourceNames)).UseDirectory("snapshots");
    }

    // The Match marker surface (the Synto.Runtime.* Matching markers) moved into Synto.Match.dll (Task 8),
    // whose MatchSurfaceInjector injects them. Pin that assembly's manifest too so the per-assembly resource
    // split stays a reviewable Verify diff. After Task 8 the umbrella (Synto.Generators.dll) embeds NOTHING,
    // so VerifyEmbeddedResourceManifest's pinned list is now empty.
    [Fact]
    public Task VerifyMatchEmbeddedResourceManifest()
    {
        var assembly = typeof(MatchSurfaceInjector).Assembly;

        var resourceNames = assembly.GetManifestResourceNames()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        return Verify(string.Join("\n", resourceNames)).UseDirectory("snapshots");
    }
}

using System;
using System.Linq;

namespace Synto.Test;

/// <summary>
/// Pins the exact set of embedded manifest resources the generator assembly ships. Before Phase 1
/// collapses the hand-typed <c>&lt;EmbeddedResource&gt;</c> list into role globs, this snapshot captures
/// today's <c>Synto.Runtime.*</c> / <c>Synto.Helper.*</c> logical names off the real assembly, so any
/// later glob change surfaces as a reviewable Verify diff (a list-delta) rather than a silent glob
/// footgun. The set is enumerated the same way <see cref="SurfaceInjectionGenerator"/> and the helper
/// loader read it — straight off <c>Assembly.GetManifestResourceNames()</c>.
/// </summary>
public class ResourceManifestPinTest
{
    [Fact]
    public Task VerifyEmbeddedResourceManifest()
    {
        var assembly = typeof(SurfaceInjectionGenerator).Assembly;

        var resourceNames = assembly.GetManifestResourceNames()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        return Verify(string.Join("\n", resourceNames)).UseDirectory("snapshots");
    }
}

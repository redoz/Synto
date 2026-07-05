using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Synto.Test;

/// <summary>
/// Clone-sync guard for the two remaining hand-maintained copies of the cacheability toolkit's equatable
/// models — the generator's canonical pair (<c>src/Synto.SourceGenerator/Shared/{EquatableArray,DiagnosticInfo}.cs</c>,
/// <c>namespace Synto.Generators</c>) versus <c>Synto.Diagnostics</c>'s deliberately-independent copy
/// (<c>src/Synto.Diagnostics/{EquatableArray,DiagnosticInfo}.cs</c>, <c>namespace Synto.Diagnostics</c>).
/// Bootstrap's third copy is gone (it now <c>&lt;Compile Link&gt;</c>s the generator's pair), so these two are
/// the only copies that can silently drift.
/// </summary>
/// <remarks>
/// The two copies are intended to stay logic-identical while differing only in incidental surface: the
/// namespace they declare, their doc comments (the Diagnostics copy documents its independence; the
/// generator copy is terse), and a pre-existing, non-behavioral <c>readonly</c>-modifier drift on the
/// <c>DiagnosticInfo</c> record-struct member methods. This test normalizes those incidental differences
/// away and asserts the remaining code is textually identical, so any drift in the actual equality /
/// hashing / <c>ToDiagnostic</c> logic surfaces as a loud, build-decoupled red test rather than a silent
/// divergence between the two products. It is deliberately cheap (reads source off disk, no compilation) in
/// the same spirit as <see cref="Synto.Test.Templating.ZeroCollisionTest"/>.
///
/// Path note: the four source paths below are hard-coded relative to the repo root. Two later structural
/// tasks re-root them — the generator's <c>Shared/</c> -> <c>Incremental/</c> move (Phase 2) and the paired
/// project rename (<c>Synto.SourceGenerator</c> -> <c>Synto.Generators</c>, <c>Synto</c> -> <c>Synto.Core</c>).
/// Update these constants when either lands.
/// </remarks>
public class CloneSyncTest
{
    // SYNC: re-root on Phase 2 (Shared/ -> Incremental/) and the paired project rename.
    private const string GeneratorEquatableArray = "src/Synto.SourceGenerator/Shared/EquatableArray.cs";
    private const string GeneratorDiagnosticInfo = "src/Synto.SourceGenerator/Shared/DiagnosticInfo.cs";
    private const string DiagnosticsEquatableArray = "src/Synto.Diagnostics/EquatableArray.cs";
    private const string DiagnosticsDiagnosticInfo = "src/Synto.Diagnostics/DiagnosticInfo.cs";

    [Fact]
    public void EquatableArrayCopiesAreClonesModuloNamespaceAndComments()
    {
        AssertClone(GeneratorEquatableArray, DiagnosticsEquatableArray);
    }

    [Fact]
    public void DiagnosticInfoCopiesAreClonesModuloNamespaceAndComments()
    {
        AssertClone(GeneratorDiagnosticInfo, DiagnosticsDiagnosticInfo);
    }

    private static void AssertClone(string relativePathA, string relativePathB)
    {
        var repoRoot = FindRepoRoot();

        var pathA = Path.Combine(repoRoot, relativePathA.Replace('/', Path.DirectorySeparatorChar));
        var pathB = Path.Combine(repoRoot, relativePathB.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(File.Exists(pathA), $"Expected source file at {pathA}");
        Assert.True(File.Exists(pathB), $"Expected source file at {pathB}");

        var normalizedA = Normalize(File.ReadAllText(pathA));
        var normalizedB = Normalize(File.ReadAllText(pathB));

        Assert.True(
            string.Equals(normalizedA, normalizedB, StringComparison.Ordinal),
            $"The two copies have drifted in their actual code (beyond namespace / doc comments / readonly modifiers):\n" +
            $"  A: {relativePathA}\n  B: {relativePathB}\n" +
            "Normalized-A:\n" + normalizedA + "\n\nNormalized-B:\n" + normalizedB);
    }

    /// <summary>
    /// Strips the incidental, deliberately-divergent surface — the <c>namespace</c> declaration, XML doc
    /// comment lines, and the non-behavioral <c>readonly</c> member-method modifier — then normalizes
    /// whitespace, leaving only the load-bearing code for a textual clone comparison.
    /// </summary>
    private static string Normalize(string source)
    {
        var kept = new List<string>();

        foreach (var raw in source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.Trim();

            if (line.Length == 0)
                continue;

            // Incidental: the namespace each copy declares differs by design.
            if (line.StartsWith("namespace ", StringComparison.Ordinal))
                continue;

            // Incidental: doc comments diverge by design (the Diagnostics copy documents its independence).
            if (line.StartsWith("///", StringComparison.Ordinal))
                continue;

            // Non-behavioral: record-struct member methods carry a divergent `readonly` modifier.
            line = line.Replace("readonly ", "", StringComparison.Ordinal);

            kept.Add(line);
        }

        return string.Join("\n", kept);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Synto.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repo root (Synto.slnx) from " + AppContext.BaseDirectory);
    }
}

using System.Collections.Generic;
using Microsoft.CodeAnalysis.Text;

namespace Synto.Generators.Templating.Staging;

/// <summary>
/// The single dedupe / order / cap primitive for the <c>SY1035</c> "live template code" Info channel (spec §; plan
/// Task 2). The three emitter taps each append a <see cref="DiagnosticInfo"/> per committed live fragment; this
/// collapses the merged set to at most one highlight per source span, orders it deterministically by span, and caps
/// it at <see cref="Cap"/> so the diagnostic stream stays bounded regardless of template size. Runs once, on the
/// <c>EmitLiveRegions</c> success path only — it never sees or perturbs the error <c>Diagnostics</c> bail.
/// </summary>
internal static class LiveFragmentMerge
{
    /// <summary>The maximum number of <c>SY1035</c> highlights emitted for one template (deterministic truncation).</summary>
    public const int Cap = 100;

    public static List<DiagnosticInfo> Merge(IEnumerable<DiagnosticInfo> infos)
    {
        // Dedupe by location span (FilePath, TextSpan): at most one SY1035 per source span (R1 safety net).
        // Entries with no resolvable location cannot be ordered/highlighted by span — drop them.
        var seen = new HashSet<(string FilePath, TextSpan Span)>();
        var deduped = new List<DiagnosticInfo>();
        foreach (var info in infos)
        {
            if (info.Location is not { } loc)
                continue;
            if (seen.Add((loc.FilePath, loc.TextSpan)))
                deduped.Add(info);
        }

        // Order by span start ascending, tie-break span end ascending, then file path for full determinism.
        deduped.Sort(static (a, b) =>
        {
            var sa = a.Location!.Value;
            var sb = b.Location!.Value;
            int c = sa.TextSpan.Start.CompareTo(sb.TextSpan.Start);
            if (c != 0)
                return c;
            c = sa.TextSpan.End.CompareTo(sb.TextSpan.End);
            if (c != 0)
                return c;
            return string.CompareOrdinal(sa.FilePath, sb.FilePath);
        });

        if (deduped.Count > Cap)
            deduped.RemoveRange(Cap, deduped.Count - Cap);

        return deduped;
    }
}

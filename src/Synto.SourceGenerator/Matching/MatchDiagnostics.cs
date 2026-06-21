using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Synto;

/// <summary>
/// Matching's pattern-specific diagnostics (the reserved <c>SY12xx</c> range, category
/// <c>Synto.Matching</c>). Kept separate from <see cref="Diagnostics"/> (Templating's <c>SY0000</c>/<c>SY10xx</c>
/// family) so the two features' IDs never collide. Each descriptor is registered in
/// <c>AnalyzerReleases.Unshipped.md</c> in the same task that adds it (RS2008-clean).
/// </summary>
internal static class MatchDiagnostics
{
    private const string Category = "Synto.Matching";

    // SY1201 — a Block.Start()/Block.End() anchor used in a None pattern (the declaration body already bounds it).
    private static readonly DiagnosticDescriptor _anchorNotAllowed = new("SY1201",
        "Invalid Anchor",
        "Block anchors are not allowed in a 'None' pattern; the declaration body already bounds the match",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticInfo AnchorNotAllowed(Location? location) =>
        new(_anchorNotAllowed, LocationInfo.CreateFrom(location), EquatableArray<string>.Empty);

    // SY1205 — the option×body-shape misuse family ({0}-reason-parameterized): None/Bare on an expression body,
    // or Single on a multi-statement core. Located on the attribute.
    private static readonly DiagnosticDescriptor _malformedPatternBody = new("SY1205",
        "Malformed Pattern Body",
        "{0}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticInfo MalformedPatternBody(Location? location, string reason) =>
        new(_malformedPatternBody, LocationInfo.CreateFrom(location), new EquatableArray<string>(ImmutableArray.Create(reason)));
}

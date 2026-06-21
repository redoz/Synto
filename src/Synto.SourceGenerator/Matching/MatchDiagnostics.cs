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

    // SY1203 — a phantom foreach iterating a [Capture] param (the deferred repetition path). Located on the foreach.
    private static readonly DiagnosticDescriptor _foreachRepetitionNotSupported = new("SY1203",
        "Unsupported Repetition",
        "A phantom 'foreach' over a [Capture] parameter (repetition) is not supported in v1",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticInfo ForeachRepetitionNotSupported(Location? location) =>
        new(_foreachRepetitionNotSupported, LocationInfo.CreateFrom(location), EquatableArray<string>.Empty);

    // SY1202 — a PROVABLE anchor contradiction ({0}-reason-parameterized): a core statement before
    // Block.Start() or after Block.End(). Conservative — only the provably-dead, never merely-suspicious.
    private static readonly DiagnosticDescriptor _patternUnsatisfiable = new("SY1202",
        "Unsatisfiable Pattern",
        "{0}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticInfo PatternUnsatisfiable(Location? location, string reason) =>
        new(_patternUnsatisfiable, LocationInfo.CreateFrom(location), new EquatableArray<string>(ImmutableArray.Create(reason)));

    // SY1204 — the quantifier-placement family ({0}-reason-parameterized): a run with >1 variable-length
    // element, or a variable-length quantifier in an embedded single-statement slot. Located on the offending hole.
    private static readonly DiagnosticDescriptor _quantifierPlacementUnsupported = new("SY1204",
        "Unsupported Quantifier Placement",
        "{0}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticInfo QuantifierPlacementUnsupported(Location? location, string reason) =>
        new(_quantifierPlacementUnsupported, LocationInfo.CreateFrom(location), new EquatableArray<string>(ImmutableArray.Create(reason)));

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

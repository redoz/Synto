using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.Matching;

namespace Synto;

/// <summary>
/// A captured pattern parameter: a result-record member. Carries <see cref="Ordinal"/> = the parameter's
/// signature position so record-member order is signature order regardless of walk order.
/// </summary>
internal sealed class Capture
{
    public Capture(int ordinal, string memberName, string memberType, string localName)
    {
        Ordinal = ordinal;
        MemberName = memberName;
        MemberType = memberType;
        LocalName = localName;
    }

    public int Ordinal { get; }
    public string MemberName { get; }
    public string MemberType { get; }

    /// <summary>The runtime local the captured node binds to (<c>cap_{paramName}</c>); the record ctor argument.</summary>
    public string LocalName { get; }
}

/// <summary>
/// An ordered element of a statement run aligned by <see cref="MatchEmitter"/>'s shared core: a
/// <see cref="LiteralElement"/> (a literal statement matched structurally) or, from Task 10, a
/// <c>HoleElement</c> (a classified statement hole). Carries its data via ctors (no <c>required</c> — absent
/// on netstandard2.0).
/// </summary>
internal abstract class RunElement
{
}

/// <summary>A literal statement in a run, matched structurally via the generic walk.</summary>
internal sealed class LiteralElement : RunElement
{
    public LiteralElement(StatementSyntax statement)
    {
        Statement = statement;
    }

    public StatementSyntax Statement { get; }
}

/// <summary>
/// A classified statement hole in a run. Carries its source statement <see cref="Location"/> so the SY1204
/// quantifier-placement check (Task 15) reports on the offending hole without re-querying the markers.
/// </summary>
internal sealed class HoleElement : RunElement
{
    public HoleElement(StatementHole hole, Location location)
    {
        Hole = hole;
        Location = location;
    }

    public StatementHole Hole { get; }
    public Location Location { get; }
}

/// <summary>
/// Mutable per-pattern emit state. Introduced here in its final shape: later tasks only POPULATE
/// <see cref="Captures"/>/<see cref="BoundCaptureLocals"/>/<see cref="Diagnostics"/> or SET <see cref="Aborted"/>;
/// the abort/merge is already wired in <see cref="MatchEmitter.Emit"/>.
/// </summary>
internal sealed class MatchContext
{
    private int _tmpCounter;

    public MatchContext(MatchInfo info, MatchMarkers markers)
    {
        Info = info;
        Markers = markers;
    }

    public MatchInfo Info { get; }
    public MatchMarkers Markers { get; }

    /// <summary>The captured members, each carrying <c>Ordinal = param.Ordinal</c> (record order = signature order).</summary>
    public List<Capture> Captures { get; } = new();

    /// <summary>First-vs-reuse-site distinction for a reused capture (the non-linear equality path).</summary>
    public HashSet<string> BoundCaptureLocals { get; } = new(System.StringComparer.Ordinal);

    /// <summary>Emitter-raised diagnostics (SY1202/SY1204/SY1205), merged into the pipeline output by <c>Emit</c>.</summary>
    public List<DiagnosticInfo> Diagnostics { get; } = new();

    /// <summary>A branch set this and returned -> <c>Emit</c> emits diagnostics-only (no tree).</summary>
    public bool Aborted { get; set; }

    /// <summary>Set by the slice-emitting helpers when the body uses <c>Skip</c>/<c>Take</c>; gates the <c>using System.Linq;</c> in <see cref="MatchEmitter"/>'s output so capture-less goldens don't churn.</summary>
    public bool NeedsLinq { get; set; }

    /// <summary>
    /// The cheap companion-predicate body: a boolean expression over <c>node</c> equal to the matcher's
    /// top-level type/kind/shape gate (C-FM1 superset). Each dispatch branch sets it to the SAME guard it
    /// roots the matcher on; <see cref="MatchComposer.Compose"/> emits <c>{Name}CouldMatch</c> from it.
    /// </summary>
    public string? CouldMatchGuard { get; set; }

    /// <summary>A unique temp-local name per call.</summary>
    public string NextTmp() => "_t" + _tmpCounter++;
}

//HintName: MatchOption.g.cs
#nullable enable
namespace Synto.Matching;

/// <summary>
/// Selects the cardinality of the syntax shape a <see cref="MatchAttribute{TMatcher}"/> matcher targets.
/// </summary>
/// <remarks>
/// Unlike <c>Synto.Templating.TemplateOption</c> this is deliberately NOT a <c>[Flags]</c> enum: the
/// values are mutually-exclusive cardinalities, not bit fields that combine.
/// </remarks>
internal enum MatchOption
{
    /// <summary>Match the whole declared shape (no reduction).</summary>
    None = 0,

    /// <summary>Match the bare body of the target.</summary>
    Bare = 1,

    /// <summary>Match a single node, unwrapping the bare body to its sole element.</summary>
    Single = 2,
}

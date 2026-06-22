//HintName: ReplaceOption.g.cs
#nullable enable
namespace Synto.Matching;

/// <summary>
/// Selects how many matches <c>SyntoMatchReplaceExtensions.Replace</c> rewrites in a single pass.
/// Parallel to <see cref="MatchOption"/> and to <c>Regex.Replace</c>'s count argument.
/// </summary>
/// <remarks>
/// Unlike <c>Synto.Templating.TemplateOption</c> this is deliberately NOT a <c>[Flags]</c> enum: the
/// values are mutually-exclusive cardinalities, not bit fields that combine. <see cref="All"/> is the
/// default.
/// </remarks>
internal enum ReplaceOption
{
    /// <summary>Rewrite every non-nested (outermost-wins) match, in document order.</summary>
    All = 0,

    /// <summary>Rewrite only the first match in document order, then short-circuit.</summary>
    First = 1,
}

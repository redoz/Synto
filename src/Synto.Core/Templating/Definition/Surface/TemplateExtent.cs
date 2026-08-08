using System.Diagnostics.CodeAnalysis;

namespace Synto.Templating;

/// <summary>
/// Selects how much of a <c>[Template]</c> carrier node is quoted into the generated factory.
/// </summary>
/// <remarks>
/// Unlike <see cref="TemplateOption"/> (an orthogonal <c>[Flags]</c> switch set) this is deliberately NOT a
/// <c>[Flags]</c> enum: the values are mutually-exclusive extents, not bit fields that combine.
/// </remarks>
[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "'Single' names the single-node cardinality; it is shipped public API and the domain term, not the System.Single type.")]
public enum TemplateExtent
{
    /// <summary>Quote the whole <c>[Template]</c> carrier node (no reduction).</summary>
    Carrier = 0,

    /// <summary>Quote the carrier's body block / member list.</summary>
    Bare,

    /// <summary>Quote the sole statement / sole member, unwrapping the bare body.</summary>
    Single,
}

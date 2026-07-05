using System;

namespace Synto.Templating;

/// <summary>
/// Orthogonal switches applied to a <c>[Template]</c> factory. Extent selection lives on the separate
/// <see cref="TemplateExtent"/> enum; this <c>[Flags]</c> set carries only switches that combine freely.
/// </summary>
[Flags]
public enum TemplateOption
{
    None = 0,

    /// <summary>
    /// Preserves Trivia
    /// </summary>
    PreserveTrivia = 1
}

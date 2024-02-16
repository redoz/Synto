using System;

namespace Synto;

[Flags]
#pragma warning disable CA2217 // This doesn't seem applicable, Single implies Bare
public enum TemplateOption
#pragma warning restore CA2217
{
    None = 0, 
    /// <summary>
    /// Reduces output to only the Body of the templated method.
    /// </summary>
    Bare = 1,

    /// <summary>
    /// Unwraps BlockExpression to first Statement
    /// </summary>
#pragma warning disable CA1720 // this seems a bit much, Single should be ok?
    Single = 2 | Bare,
#pragma warning restore CA1720

    // should probably add some kind of option to minimize the output
    
    /// <summary>
    /// Preserves Trivia
    /// </summary>
    PreserveTrivia = 4
}
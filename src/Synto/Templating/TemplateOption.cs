using System;

namespace Synto;

[Flags]
public enum TemplateOption
{
    Default = 0, 
    /// <summary>
    /// Reduces output to only the Body of the templated method.
    /// </summary>
    Bare = 1,

    /// <summary>
    /// Unwraps BlockExpression to first Statement
    /// </summary>
    Single = 2 | Bare,

    // should probably add some kind of option to minimize the output
    
    /// <summary>
    /// Preserves Trivia
    /// </summary>
    PreserveTrivia = 4
}
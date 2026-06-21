using System;

namespace Synto.Templating;

[AttributeUsage(AttributeTargets.GenericParameter | AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class InlineAttribute : Attribute
{
    /// <summary>
    /// False by default
    /// </summary>
    public bool AsSyntax { get; set; }
}

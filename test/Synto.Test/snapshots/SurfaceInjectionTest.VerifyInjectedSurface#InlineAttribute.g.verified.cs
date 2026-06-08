//HintName: InlineAttribute.g.cs
#nullable enable
using System;

namespace Synto;

[AttributeUsage(AttributeTargets.GenericParameter | AttributeTargets.Parameter, AllowMultiple = false)]
internal sealed class InlineAttribute : Attribute
{
    /// <summary>
    /// False by default
    /// </summary>
    public bool AsSyntax { get; set; }
}
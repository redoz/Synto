using System;

namespace Synto;

[AttributeUsage(AttributeTargets.GenericParameter | AttributeTargets.Parameter, AllowMultiple = false)]
public class InlineAttribute : Attribute
{
    public bool AsSyntax { get; set; } = false;
}
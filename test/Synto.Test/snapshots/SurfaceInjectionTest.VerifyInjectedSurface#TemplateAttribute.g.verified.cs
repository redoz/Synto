//HintName: TemplateAttribute.g.cs
#nullable enable
using System;

namespace Synto.Templating;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Struct /* | AttributeTargets.Enum*/)]
internal sealed class TemplateAttribute : Attribute
{
    public TemplateOption Options { get; set; }

    public Type Target { get; }

    public TemplateAttribute(Type target)
    {
        Target = target;
        Options = TemplateOption.None;
    }
}

using System;

namespace Synto;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class /* | AttributeTargets.Struct | AttributeTargets.Enum*/)]
public class TemplateAttribute : Attribute
{
    public TemplateOption Options { get; set; }

    public Type Target { get; }

    public string Runtime { get; set; } = RuntimeAttribute.Default;

    public TemplateAttribute(Type target)
    {
        Target = target;
        Options = TemplateOption.Default;
    }
}
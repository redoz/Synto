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

/// <summary>
/// Generic form of <see cref="TemplateAttribute"/>: the factory target is carried by the type argument
/// <typeparamref name="T"/> instead of a <c>typeof(...)</c> constructor argument. <c>[Template&lt;Factory&gt;]</c>
/// is equivalent to <c>[Template(typeof(Factory))]</c>.
/// </summary>
/// <typeparam name="T">The partial class the generated factory is emitted onto.</typeparam>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Struct /* | AttributeTargets.Enum*/)]
internal sealed class TemplateAttribute<T> : Attribute
{
    public TemplateOption Options { get; set; }
}

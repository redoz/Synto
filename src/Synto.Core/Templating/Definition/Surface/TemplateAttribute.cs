using System;

namespace Synto.Templating;

/// <summary>
/// Targetless marker form of the template attribute. A top-level <c>[Template]</c> with no target is an error
/// (SY1033): use the generic <see cref="TemplateAttribute{T}"/> to pin the factory target. A nested
/// <c>[Template]</c> inside a <c>[Template&lt;T&gt;]</c> carrier is a sub-template that inherits the parent's target.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Struct /* | AttributeTargets.Enum*/)]
public sealed class TemplateAttribute : Attribute
{
    public TemplateOption Options { get; set; }
    public TemplateExtent Extent { get; set; }
}

/// <summary>
/// Generic form of <see cref="TemplateAttribute"/>: the factory target is carried by the type argument
/// <typeparamref name="T"/>. <c>[Template&lt;Factory&gt;]</c> emits the generated factory onto the partial
/// class <c>Factory</c>.
/// </summary>
/// <typeparam name="T">The partial class the generated factory is emitted onto.</typeparam>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Struct /* | AttributeTargets.Enum*/)]
public sealed class TemplateAttribute<T> : Attribute
{
    public TemplateOption Options { get; set; }
    public TemplateExtent Extent { get; set; }
}

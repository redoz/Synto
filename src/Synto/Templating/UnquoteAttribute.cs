using System;

namespace Synto.Templating;

/// <summary>
/// Marks a <c>[Template]</c> method parameter as an <em>unquote</em>: its value is supplied to the generated
/// factory at template-invocation time and lifted into the produced syntax (like an <c>[Inline]</c> value),
/// but — unlike <c>[Inline]</c> — an unquote parameter may also <em>drive</em> factory-time control flow
/// (loop sources, conditions) so the template body can be unrolled / specialized against it (staged control).
/// Unquote <em>locals</em> use <c>Template.Unquote&lt;T&gt;()</c> instead (C# forbids attributes on local
/// declarations); this attribute is the parameter-position escape hatch. Authored <c>public</c> here and
/// injected <c>internal</c> into the consumer compilation by <c>SurfaceInjectionGenerator</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class UnquoteAttribute : Attribute
{
}

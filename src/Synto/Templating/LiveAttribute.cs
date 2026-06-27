using System;

namespace Synto.Templating;

/// <summary>
/// Marks a <c>[Template]</c> method parameter as <em>live</em>: its value is supplied to the generated
/// factory at template-invocation time and lifted into the produced syntax (like an <c>[Inline]</c> value),
/// but — unlike <c>[Inline]</c> — a live parameter may also <em>drive</em> factory-time control flow
/// (loop sources, conditions) so the template body can be unrolled / specialized against it. Live
/// <em>locals</em> use <c>Template.Live&lt;T&gt;()</c> instead (C# forbids attributes on local
/// declarations); this attribute is the parameter-position escape hatch. Authored <c>public</c> here and
/// injected <c>internal</c> into the consumer compilation by <c>SurfaceInjectionGenerator</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class LiveAttribute : Attribute
{
}

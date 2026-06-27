using System;

namespace Synto.Templating;

/// <summary>
/// Marks a <c>[Template]</c> parameter (value or generic type) as an <em>unquote</em>: its value is supplied
/// to the generated factory at template-invocation time and <em>lifted</em> into the produced syntax — a
/// runtime value via a <c>[Runtime]</c> converter / <c>.ToSyntax()</c>, a generic type via
/// <c>typeof(T).ToTypeSyntax()</c>. A value unquote parameter may also <em>drive</em> factory-time control
/// flow (loop sources, conditions) so the template body can be unrolled / specialized against it (staged
/// control). Contrast <c>[Splice]</c>, which inserts a <em>pre-built</em> <c>ExpressionSyntax</c>/<c>TypeSyntax</c>
/// verbatim with no evaluation or lift. Unquote <em>locals</em> use <c>Template.Unquote&lt;T&gt;()</c> instead
/// (C# forbids attributes on local declarations); this attribute is the parameter/type-parameter escape hatch.
/// Authored <c>public</c> here and injected <c>internal</c> into the consumer compilation by
/// <c>SurfaceInjectionGenerator</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.GenericParameter, AllowMultiple = false)]
public sealed class UnquoteAttribute : Attribute
{
}

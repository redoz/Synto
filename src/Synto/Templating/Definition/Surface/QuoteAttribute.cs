using System;

namespace Synto.Templating;

/// <summary>
/// Marks a <c>[Template]</c> value parameter as a <em>quote</em>: its value is supplied to the generated factory
/// at template-invocation time and <em>lifted</em> into the produced syntax — a runtime value via a
/// <c>[Runtime]</c> converter / <c>.ToSyntax()</c> — exactly the same value-lift as <c>[Unquote]</c> in value
/// position. The decisive difference is that a quote is <em>never</em> a staging root: control flow driven only
/// by a quoted value stays a runtime construct (no unroll), so <c>for (int i = 0; i &lt; count; i++)</c> over a
/// <c>[Quote] int count</c> emits a real runtime loop bounded by the lifted value rather than being unrolled.
/// Contrast <c>[Unquote]</c> (a live value that may <em>drive</em> staged control flow, unrolling loops /
/// specializing conditions) and <c>[Splice]</c> (a pre-built node inserted verbatim with no evaluation or lift).
/// Restricted to the value axis (<c>AttributeTargets.Parameter</c>): a generic type parameter cannot be quoted.
/// Authored <c>public</c> here and injected <c>internal</c> into the consumer compilation by
/// <c>SurfaceInjectionGenerator</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class QuoteAttribute : Attribute
{
}

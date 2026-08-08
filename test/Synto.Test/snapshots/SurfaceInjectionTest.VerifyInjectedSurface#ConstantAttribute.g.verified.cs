//HintName: ConstantAttribute.g.cs
#nullable enable
using System;

namespace Synto.Templating;

/// <summary>
/// Marks a <c>[SyntaxBuilder]</c> method parameter whose corresponding call argument is a factory-time
/// <em>value</em> that is <em>lifted</em> to a constant <c>ExpressionSyntax</c> (a built-in literal or a
/// qualified enum-member access) via the shared value-lift — the builder-arg counterpart of <c>[Quote]</c>.
/// The synthesized facade exposes the parameter as an <c>object</c>-typed value parameter, so an author passes
/// a plain factory-time value (e.g. <c>c.Ordinal</c> or an enum member) rather than a hand-built syntax node.
/// This is a value-axis kind: contrast <c>[Quoted]</c> (quotes the call argument as an output-world syntax
/// island) and an unmarked parameter (passes the live/computed value through verbatim). Authored <c>public</c>
/// here and injected <c>internal</c> into the consumer compilation by <c>SurfaceInjectionGenerator</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
internal sealed class ConstantAttribute : Attribute
{
}

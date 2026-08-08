//HintName: TypeArgAttribute.g.cs
#nullable enable
using System;

namespace Synto.Templating;

/// <summary>
/// Marks a <c>[SyntaxBuilder]</c> method parameter that surfaces on the synthesized facade as a generic
/// <em>type parameter</em> rather than a value parameter. The corresponding call argument is a type island
/// (e.g. the <c>int</c> in <c>Cast&lt;int&gt;(x)</c>), quoted to a <c>TypeSyntax</c> and passed to the builder
/// body. A type-arg parameter is inherently quoted, so <c>[TypeArg]</c> is a <em>sibling</em> of
/// <c>[Quoted]</c> — never combine the two on one parameter. Authored <c>public</c> here and injected
/// <c>internal</c> into the consumer compilation by <c>SurfaceInjectionGenerator</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
internal sealed class TypeArgAttribute : Attribute
{
    /// <summary>
    /// Names the synthesized facade generic type parameter, decoupling it from the builder parameter name.
    /// Default (<c>null</c>) uses <c>Pascal(parameterName)</c>. For example a builder parameter
    /// <c>targetType</c> with <c>Name = "TResult"</c> surfaces as <c>&lt;TResult&gt;</c>.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// When set, constrains the synthesized facade type parameter to this base class or interface, emitting a
    /// <c>where T : Constraint</c> clause so the consumer's supplied type argument must satisfy it. Default
    /// (<c>null</c>) emits an unconstrained type parameter. A single base/interface type only.
    /// </summary>
    public Type? Constraint { get; set; }
}

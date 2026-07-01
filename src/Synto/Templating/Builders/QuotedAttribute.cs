using System;

namespace Synto.Templating;

/// <summary>
/// Marks a <c>[SyntaxBuilder]</c> method parameter whose corresponding call argument is <em>quoted</em> (an
/// output-world syntax island) rather than passed through as a live value. The parameter <em>type</em> cannot
/// disambiguate "quote this island into an <c>ExpressionSyntax</c>" from "pass a live <c>ExpressionSyntax</c>
/// value through unquoted" (deliberate metaprogramming), so binding-time is declared explicitly: an
/// <c>[Quoted]</c> parameter receives the quote of the call argument; an unmarked parameter receives the
/// live/computed value verbatim. Authored <c>public</c> here and injected <c>internal</c> into the consumer
/// compilation by <c>SurfaceInjectionGenerator</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class QuotedAttribute : Attribute
{
    /// <summary>
    /// When set, the synthesized facade value parameter is typed as this type instead of <c>object</c>.
    /// Default <c>null</c>.
    /// </summary>
    public Type? As { get; set; }
}

//HintName: QuotedAttribute.g.cs
#nullable enable
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
internal sealed class QuotedAttribute : Attribute
{
    /// <summary>
    /// When <c>true</c>, the corresponding facade element is a generic <em>type parameter</em> (the quoted
    /// argument is a type island, quoted to a <c>TypeSyntax</c>) rather than a value parameter. Default
    /// <c>false</c>.
    /// </summary>
    public bool AsTypeArg { get; set; }

    /// <summary>
    /// When set, the synthesized facade value parameter is typed as this type instead of <c>object</c>.
    /// Ignored when <see cref="AsTypeArg"/> is <c>true</c>. Default <c>null</c>.
    /// </summary>
    public Type? As { get; set; }
}

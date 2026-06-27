//HintName: SpliceAttribute.g.cs
#nullable enable
using System;

namespace Synto.Templating;

/// <summary>
/// Marks a <c>[Template]</c> parameter (value or generic type) as a <em>splice</em>: the supplied
/// <em>pre-built</em> syntax is inserted into the produced output <em>verbatim</em>, with no factory-time
/// evaluation and no value lift. A value splice parameter (<c>[Splice] ExpressionSyntax x</c>) becomes a
/// factory parameter typed <c>ExpressionSyntax</c> and every use of <c>x</c> is spliced as-is; a generic
/// type splice parameter (<c>&lt;[Splice] T&gt;</c>) becomes a factory parameter typed <c>TypeSyntax</c> and
/// every use of <c>T</c> is spliced as-is. Contrast <c>[Unquote]</c>, which evaluates a value at factory time
/// and lifts it into syntax. The local/inline counterpart is <c>Template.Splice(node)</c>. Authored
/// <c>public</c> here and injected <c>internal</c> into the consumer compilation by
/// <c>SurfaceInjectionGenerator</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.GenericParameter, AllowMultiple = false)]
internal sealed class SpliceAttribute : Attribute
{
}

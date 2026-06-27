//HintName: ReturnTypeAttribute.g.cs
#nullable enable
using System;

namespace Synto.Templating;

/// <summary>
/// Placed on a <c>[Quoted(AsTypeArg = true)]</c> <c>[SyntaxBuilder]</c> parameter to make that parameter's
/// synthesized generic type parameter the facade's <em>return type</em>. Without any <c>[ReturnType]</c>
/// parameter the synthesized facade returns a fresh <c>TResult</c> type parameter (reusing a type island as
/// the result is opt-in, never implicit). Misuse — <c>[ReturnType]</c> on a non-<c>AsTypeArg</c> parameter, or
/// more than one <c>[ReturnType]</c> parameter on a builder — is a facade-synthesis error. Authored
/// <c>public</c> here and injected <c>internal</c> into the consumer compilation by
/// <c>SurfaceInjectionGenerator</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
internal sealed class ReturnTypeAttribute : Attribute
{
}

using System;

namespace Synto.Model;

/// <summary>
/// Marks a spec class whose expression-bodied getters are pure projections over a source
/// <c>ISymbol</c>. The generator emits a sibling cache-safe <c>record</c> plus a <c>From(TSymbol)</c>
/// factory, so no Roslyn object ever leaks into an incremental generator's pipeline state.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class SyntoModelAttribute : Attribute
{
    /// <summary>
    /// The name of the generated record. Defaults to the spec-class name with a trailing <c>Shape</c>
    /// removed (e.g. <c>PropModelShape</c> emits <c>PropModel</c>).
    /// </summary>
    public string? Name { get; set; }
}

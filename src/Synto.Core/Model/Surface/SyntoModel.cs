namespace Synto.Model;

/// <summary>
/// Base class for a <see cref="SyntoModelAttribute"/> spec: its <see cref="Symbol"/> is the source symbol
/// each getter projects over. Inert at runtime — the generator reads the spec's expression-bodied getters
/// and emits a sibling cache-safe record; this type is never instantiated by consumer code.
/// </summary>
/// <typeparam name="T">The source symbol type each getter projects over (e.g. <c>IPropertySymbol</c>).</typeparam>
public class SyntoModel<T>
{
    /// <summary>
    /// The source symbol each getter projects over. Inert placeholder — never read at runtime; the
    /// generator rebinds every reference to the emitted <c>From(TSymbol)</c> factory parameter.
    /// </summary>
    protected T Symbol => default!;
}

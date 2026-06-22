using System.Collections;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Synto.Example.ObjectReader.Generator;

/// <summary>One resolved column: the member name (also the direct accessor on <c>_e.Current</c>) and its
/// fully-qualified type name (used for <c>GetFieldType</c>). Pure value type — safe to cache (C-5).</summary>
internal readonly record struct ColumnInfo(string Name, string ColumnTypeName);

/// <summary>Which diagnostic an equatable <see cref="DiagnosticInfo"/> carries; mapped back to a real
/// <c>Synto.Diagnostics</c>-generated factory call in the output stage.</summary>
internal enum DiagnosticKind
{
    /// <summary>SOR0000 — an unexpected generator exception, converted to a diagnostic (never thrown — C-5).</summary>
    InternalError,

    /// <summary>SOR0001 — a named member was not found on the target type; the column is skipped (C-2).</summary>
    MemberNotFound,

    /// <summary>SOR0002 — the member list was not compile-time-constant; the call is not intercepted.</summary>
    MembersNotConstant,
}

/// <summary>
/// A value-equatable representation of a <see cref="Location"/> so diagnostics can flow through the
/// incremental pipeline without rooting a <see cref="SyntaxTree"/> / <see cref="Compilation"/> (C-5).
/// (Mirrors <c>Synto.Diagnostics</c>'s own internal <c>LocationInfo</c> — a candidate "Synto could expose a
/// cacheability toolkit" friction finding: a generator author re-authors this primitive every time.)
/// </summary>
internal readonly record struct LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
{
    public Location ToLocation() => Location.Create(FilePath, TextSpan, LineSpan);

    public static LocationInfo? CreateFrom(Location? location)
    {
        if (location?.SourceTree is null)
        {
            return null;
        }

        return new LocationInfo(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);
    }
}

/// <summary>
/// A value-equatable description of a diagnostic carried out of the transform. The real
/// <see cref="Diagnostic"/> is materialized only in the output stage (via the <c>Synto.Diagnostics</c>-
/// generated factory) so the cached pipeline value stays free of non-cacheable Roslyn objects (C-5).
/// </summary>
internal readonly record struct DiagnosticInfo(DiagnosticKind Kind, LocationInfo? Location, EquatableArray<string> Arguments);

/// <summary>
/// The equatable per-call-site model the syntax/semantic transform flows out. Carries NO Roslyn objects
/// (no Compilation/ISymbol/SemanticModel/SyntaxNode) so the pipeline stays cacheable (C-5); emission in
/// <c>RegisterSourceOutput</c> runs from this value alone. <paramref name="Intercept"/> is <c>false</c> for a
/// diagnostics-only model (e.g. a non-constant member list — SOR0002): its diagnostics are replayed but no
/// reader/interceptor is emitted for it.
/// </summary>
internal readonly record struct ObjectReaderModel(
    string TargetTypeQualifiedName,
    string TargetTypeShortName,
    EquatableArray<ColumnInfo> Columns,
    EquatableArray<DiagnosticInfo> Diagnostics,
    string InterceptsLocationAttribute,
    bool Intercept);

/// <summary>
/// A minimal value-equatable wrapper over an array — structural equality + a stable hash so an
/// <see cref="ObjectReaderModel"/> carrying columns compares by VALUE (the incremental pipeline keys on it).
/// Hand-rolled because Synto's own internal <c>EquatableArray</c> is not part of the injected consumer
/// surface (friction: a generator author must re-author this cacheability primitive every time).
/// </summary>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly T[]? _items;

    public EquatableArray(T[] items) => _items = items;

    public int Count => _items?.Length ?? 0;

    public T this[int index] => _items![index];

    public bool Equals(EquatableArray<T> other)
    {
        if (_items is null)
        {
            return other._items is null;
        }

        if (other._items is null || _items.Length != other._items.Length)
        {
            return false;
        }

        for (int i = 0; i < _items.Length; i++)
        {
            if (!_items[i].Equals(other._items[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        if (_items is null)
        {
            return 0;
        }

        unchecked
        {
            int hash = 17;
            foreach (T item in _items)
            {
                hash = (hash * 31) + item.GetHashCode();
            }

            return hash;
        }
    }

    public IEnumerator<T> GetEnumerator() =>
        ((IEnumerable<T>)(_items ?? Array.Empty<T>())).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

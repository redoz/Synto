using System.Collections;

namespace Synto.Example.ObjectReader.Generator;

/// <summary>One resolved column: the member name (also the direct accessor on <c>_e.Current</c>) and its
/// fully-qualified type name (used for <c>GetFieldType</c>). Pure value type — safe to cache (C-5).</summary>
internal readonly record struct ColumnInfo(string Name, string ColumnTypeName);

/// <summary>
/// The equatable per-call-site model the syntax/semantic transform flows out. Carries NO Roslyn objects
/// (no Compilation/ISymbol/SemanticModel/SyntaxNode) so the pipeline stays cacheable (C-5); emission in
/// <c>RegisterSourceOutput</c> runs from this value alone.
/// </summary>
internal readonly record struct ObjectReaderModel(
    string TargetTypeQualifiedName,
    string TargetTypeShortName,
    EquatableArray<ColumnInfo> Columns,
    string InterceptsLocationAttribute);

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

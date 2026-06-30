using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Synto;

/// <summary>
/// A small immutable array wrapper with structural (value) equality, suitable for use as part of an
/// incremental generator's data model so the pipeline can cache and short-circuit correctly.
/// </summary>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
    where T : IEquatable<T>
{
    public static readonly EquatableArray<T> Empty = new(ImmutableArray<T>.Empty);

    private readonly ImmutableArray<T> _array;

    public EquatableArray(ImmutableArray<T> array) => _array = array;

    private ImmutableArray<T> Items => _array.IsDefault ? ImmutableArray<T>.Empty : _array;

    public int Count => Items.Length;

    public T this[int index] => Items[index];

    public bool Equals(EquatableArray<T> other)
    {
        var left = Items;
        var right = other.Items;

        if (left.Length != right.Length)
            return false;

        for (int i = 0; i < left.Length; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(left[i], right[i]))
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            foreach (var item in Items)
                hash = (hash * 31) + EqualityComparer<T>.Default.GetHashCode(item!);
            return hash;
        }
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

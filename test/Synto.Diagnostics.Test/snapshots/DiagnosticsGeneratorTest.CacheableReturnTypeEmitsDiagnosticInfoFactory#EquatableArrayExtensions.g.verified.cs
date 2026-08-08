//HintName: EquatableArrayExtensions.g.cs
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Synto.Generators;

/// <summary>
/// Extensions over <see cref="EquatableArray{T}"/>. <see cref="ToEquatableArray{T}"/> lets generated
/// <c>From</c> bodies (and any generator-author code) project a sequence into the cache-safe, value-equal
/// <see cref="EquatableArray{T}"/> in a single call. Authored ONCE here beside the cacheability trio and
/// embedded verbatim so the lone <c>IncrementalSurfaceInjector</c> injects it downstream as a sibling of
/// the injected <see cref="EquatableArray{T}"/> (no extra <c>using</c> needed to bind it).
/// </summary>
internal static class EquatableArrayExtensions
{
    /// <summary>
    /// Materializes <paramref name="source"/> into an <see cref="EquatableArray{T}"/> (empty when null).
    /// </summary>
    public static EquatableArray<T> ToEquatableArray<T>(this IEnumerable<T> source)
        where T : IEquatable<T> =>
        new(source is null ? ImmutableArray<T>.Empty : source.ToImmutableArray());
}

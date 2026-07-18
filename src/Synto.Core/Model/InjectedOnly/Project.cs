using System;
using System.Collections.Generic;
using Synto.Generators;

namespace Synto.Model;

/// <summary>
/// Inert projection shims recognized by the generator (by bound symbol) inside a <see cref="SyntoModelAttribute"/>
/// spec getter and rewritten into real nested-model projections in the emitted <c>From</c> body. Never invoked
/// at runtime. Authored under <c>InjectedOnly</c> (not <c>Surface</c>) because it references the injected
/// <see cref="EquatableArray{T}"/>, which is not part of the compiled <c>Synto.Core</c> surface.
/// </summary>
public static class Project
{
    /// <summary>
    /// Projects each source symbol in <paramref name="source"/> into a nested <typeparamref name="TModel"/>
    /// record. Rewritten by the generator to <c>source.Select(TModel.From).ToEquatableArray()</c>.
    /// </summary>
    public static EquatableArray<TModel> Each<TModel>(IEnumerable<object> source)
        where TModel : IEquatable<TModel> => default!;

    /// <summary>
    /// Projects a single (possibly null) source symbol into a nested <typeparamref name="TModel"/> record.
    /// Rewritten by the generator to <c>symbol is null ? null : TModel.From(symbol)</c> (nullable-aware).
    /// </summary>
    public static TModel One<TModel>(object symbol) => default!;
}

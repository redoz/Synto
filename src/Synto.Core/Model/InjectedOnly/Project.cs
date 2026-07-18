using System.Collections.Generic;

namespace Synto.Model;

/// <summary>
/// Inert projection shims recognized by the generator (by bound symbol) inside a <see cref="SyntoModelAttribute"/>
/// spec getter and rewritten into real nested-model projections in the emitted <c>From</c> body. Never invoked
/// at runtime. Authored under <c>InjectedOnly</c> (not <c>Surface</c>) because the model surface as a whole is
/// injected, not part of the compiled <c>Synto.Core</c> surface.
/// </summary>
/// <remarks>
/// The <typeparamref name="TModel"/> type argument is the <b>spec</b> class (a <see cref="SyntoModelAttribute"/>
/// type), NOT the generated record. The shim returns an <b>unconstrained</b> collection so the spec getter binds
/// even though the spec class is not <c>IEquatable</c> — the generated record's <c>EquatableArray</c> field type
/// is synthesized by the generator (which rewrites the getter's declared spec-collection type into the record
/// collection type). Keeping the return type constrained to <c>EquatableArray&lt;TModel&gt;</c> would fail to
/// bind (CS0311), leaving the shim invisible to the generator and suppressing <c>SY1038</c>.
/// </remarks>
public static class Project
{
    /// <summary>
    /// Projects each source symbol in <paramref name="source"/> into a nested model record derived from the
    /// spec type argument <typeparamref name="TModel"/>. The generator rewrites the enclosing getter's declared
    /// type from <c>IEnumerable&lt;TModel&gt;</c> to <c>EquatableArray&lt;{Record}&gt;</c> and the RHS to
    /// <c>source.Select({Record}.From).ToEquatableArray()</c>, where <c>{Record}</c> is the record derived from
    /// <typeparamref name="TModel"/>.
    /// </summary>
    public static IEnumerable<TModel> Each<TModel>(IEnumerable<object> source) => default!;

    /// <summary>
    /// Projects a single (possibly null) source symbol into a nested model record derived from the spec type
    /// argument <typeparamref name="TModel"/>. The generator rewrites the enclosing getter's declared type from
    /// <c>TModel?</c> to <c>{Record}?</c> and the RHS to <c>symbol is null ? null : {Record}.From(symbol)</c>
    /// (nullable-aware).
    /// </summary>
    public static TModel One<TModel>(object symbol) => default!;
}

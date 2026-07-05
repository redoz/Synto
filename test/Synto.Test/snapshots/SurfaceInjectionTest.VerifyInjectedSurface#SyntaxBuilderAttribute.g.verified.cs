//HintName: SyntaxBuilderAttribute.g.cs
#nullable enable
using System;

namespace Synto.Templating;

/// <summary>
/// Marks an individual <c>public static</c> method as a factory-time <em>syntax builder</em>: a method that
/// runs while the generated factory is building syntax and returns a syntax node spliced into the produced
/// output. Synto synthesizes an inert carrier-callable <em>facade</em> from the builder's parameter
/// annotations (see <c>[Quoted]</c> / <c>[ReturnType]</c>) and emits it so a <c>[Template]</c> body can call
/// the builder by its method name; at factory-build time the recognized facade call is rewritten to a
/// fully-qualified static invocation of the builder over processed arguments. This is the public
/// extensibility contract (mirrors <c>[Runtime]</c>, but method-level: builder method names are arbitrary, so
/// the mark is per-method rather than per-class). Authored <c>public</c> here and injected <c>internal</c>
/// into the consumer compilation by <c>SurfaceInjectionGenerator</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
internal sealed class SyntaxBuilderAttribute : Attribute
{
    /// <summary>
    /// The fixed value-position return type of the synthesized facade (e.g. <c>typeof(System.Type)</c> for a
    /// <c>typeof</c>-style operator). Unset → today's behavior: an <c>ExpressionSyntax</c> builder's facade gets
    /// a fresh generic <c>TResult</c> return (or a <c>[ReturnType]</c> parameter supplies it, else the concrete
    /// node type is used). When set, the facade returns this type verbatim (no fresh <c>TResult</c>), and
    /// <see cref="Facade"/> is required — a same-signature facade differing only by return type would be CS0111
    /// in the builder's own type, so it must be synthesized into a different host.
    /// </summary>
    public Type? Returns { get; set; }

    /// <summary>
    /// The author-facing type the synthesized facade is emitted into. Unset → the builder's own containing type
    /// (today's behavior, valid when the facade differs by generic arity or return type). Required whenever
    /// <see cref="Returns"/> is set, so the fixed-return facade lands in a host distinct from the builder.
    /// </summary>
    public Type? Facade { get; set; }
}

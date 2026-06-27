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
}

//HintName: Template.g.cs
#nullable enable
namespace Synto.Templating;

/// <summary>
/// The static facade hosting the live-staged template surface. Authored once here and injected
/// <c>internal</c> into the consumer compilation by <c>SurfaceInjectionGenerator</c>, so
/// <c>using static Synto.Templating.Template;</c> brings the live-staging entry points into scope.
/// Every member is inert (<c>=&gt; default!</c>): the methods exist only so a <c>[Template]</c> body
/// type-checks; the generator recognizes the calls by binding and rewrites them at factory-build time.
/// </summary>
internal static class Template
{
    /// <summary>
    /// Declares a <em>live</em> template parameter: a value supplied to the generated factory at
    /// template-invocation time rather than quoted from the template body. In declaration position
    /// (<c>var count = Parameter&lt;int&gt;();</c>) the bound variable name supplies the factory
    /// parameter name; in any other (inline) position an explicit <paramref name="parameterName"/> is
    /// required. Consuming the value lifts it into the generated syntax exactly like an
    /// <c>[Inline]</c> value.
    /// </summary>
    /// <typeparam name="T">The live value's type; becomes the factory parameter's type.</typeparam>
    /// <param name="parameterName">The factory parameter name; required in inline position.</param>
    public static T Parameter<T>(string? parameterName = null) => default!;

    /// <summary>
    /// Declares a <em>live</em> local: <c>var n = Live(expr);</c> runs <paramref name="value"/> at
    /// template-build time inside the generated factory (so <c>n</c> is a real runtime local), and any
    /// consumption of <c>n</c> is lifted into the produced syntax. This is the local-position counterpart
    /// to a <c>[Live]</c> parameter (C# forbids attributes on local declarations). The method itself is the
    /// inert identity <c>=&gt; value</c> so the template body type-checks; the generator recognizes the call
    /// by binding and rewrites it at factory-build time.
    /// </summary>
    /// <typeparam name="T">The live value's type; inferred as the local's type.</typeparam>
    /// <param name="value">The expression evaluated at factory-build time.</param>
    public static T Live<T>(T value) => value;
}

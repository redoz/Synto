namespace Synto.Templating;

/// <summary>
/// The static facade hosting the live-staged template surface. Authored once here and injected
/// <c>internal</c> into the consumer compilation by <c>SurfaceInjectionGenerator</c>, so
/// <c>using static Synto.Templating.Template;</c> brings the live-staging entry points into scope.
/// Every member is inert (<c>=&gt; default!</c>): the methods exist only so a <c>[Template]</c> body
/// type-checks; the generator recognizes the calls by binding and rewrites them at factory-build time.
/// </summary>
public static class Template
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
}

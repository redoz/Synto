namespace Synto.Templating;

/// <summary>
/// The static facade hosting the staged template surface. Authored once here and injected
/// <c>internal</c> into the consumer compilation by <c>SurfaceInjectionGenerator</c>, so
/// <c>using static Synto.Templating.Template;</c> brings the staging entry points into scope.
/// Every member is inert (<c>=&gt; default!</c>): the methods exist only so a <c>[Template]</c> body
/// type-checks; the generator recognizes the calls by binding and rewrites them at factory-build time.
/// </summary>
public static class Template
{
    /// <summary>
    /// Declares an <em>unquote</em> template parameter: a value supplied to the generated factory at
    /// template-invocation time rather than quoted from the template body. In declaration position
    /// (<c>var count = Parameter&lt;int&gt;();</c>) the bound variable name supplies the factory
    /// parameter name; in any other (inline) position an explicit <paramref name="parameterName"/> is
    /// required. Consuming the value lifts it into the generated syntax exactly like an
    /// <c>[Inline]</c> value.
    /// </summary>
    /// <typeparam name="T">The unquoted value's type; becomes the factory parameter's type.</typeparam>
    /// <param name="parameterName">The factory parameter name; required in inline position.</param>
    public static T Parameter<T>(string? parameterName = null) => default!;

    /// <summary>
    /// Declares an <em>unquote</em> local: <c>var n = Unquote(expr);</c> runs <paramref name="value"/> at
    /// template-build time inside the generated factory (so <c>n</c> is a real runtime local), and any
    /// consumption of <c>n</c> is lifted into the produced syntax. This is the local-position counterpart
    /// to an <c>[Unquote]</c> parameter (C# forbids attributes on local declarations). The method itself is the
    /// inert identity <c>=&gt; value</c> so the template body type-checks; the generator recognizes the call
    /// by binding and rewrites it at factory-build time.
    /// </summary>
    /// <typeparam name="T">The unquoted value's type; inferred as the local's type.</typeparam>
    /// <param name="value">The expression evaluated at factory-build time.</param>
    public static T Unquote<T>(T value) => value;

    /// <summary>
    /// Built-in syntax-builder <em>facade</em>: in a <c>[Template]</c> body, <c>Member&lt;TValue&gt;(instance,
    /// "Name")</c> emits a member access <c>instance.Name</c> into the produced syntax (an identifier member
    /// access, not the string literal <c>"Name"</c>). Inert (<c>=&gt; default!</c>): the generator recognizes
    /// the call by binding and rewrites it at factory-build time to the built-in <c>Member</c> builder. The
    /// hand-authored counterpart to the user <c>[SyntaxBuilder]</c> synthesis path.
    /// </summary>
    /// <typeparam name="TValue">The carrier-world type of the accessed member (carrier type-check only).</typeparam>
    /// <param name="instance">The instance whose member is accessed (an output-world syntax island).</param>
    /// <param name="name">The member name (a factory-time value).</param>
    public static TValue Member<TValue>(object instance, string name) => default!;

    /// <summary>
    /// Built-in syntax-builder <em>facade</em>: in a <c>[Template]</c> body, <c>TypeOf("Name")</c> emits the
    /// type reference <c>Name</c> (a <c>TypeSyntax</c>) into the produced syntax. Inert (<c>=&gt; default!</c>):
    /// the generator recognizes the call by binding and rewrites it at factory-build time to the built-in
    /// <c>TypeOf</c> builder.
    /// </summary>
    /// <param name="name">The type name (a factory-time value).</param>
    public static System.Type TypeOf(string name) => default!;
}

//HintName: Template.g.cs
#nullable enable
namespace Synto.Templating;

/// <summary>
/// The static facade hosting the staged template surface. Authored once here and injected
/// <c>internal</c> into the consumer compilation by <c>SurfaceInjectionGenerator</c>, so
/// <c>using static Synto.Templating.Template;</c> brings the staging entry points into scope.
/// Every member is inert (<c>=&gt; default!</c>): the methods exist only so a <c>[Template]</c> body
/// type-checks; the generator recognizes the calls by binding and rewrites them at factory-build time.
/// </summary>
internal static class Template
{
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
    /// Declares a caller-supplied <em>unquote</em> live root in the template body: a value supplied to the
    /// generated factory at template-invocation time rather than quoted from the template body. In declaration
    /// position (<c>var count = Unquote&lt;int&gt;();</c>) the bound variable name supplies the factory parameter
    /// name; in any other (inline) position an explicit <paramref name="parameterName"/> is required. This is the
    /// in-body counterpart to an <c>[Unquote]</c> parameter; consuming the value lifts it into the produced syntax
    /// exactly like an <c>[Unquote]</c> value. Inert (<c>=&gt; default!</c>) so the template body type-checks; the
    /// generator recognizes the call by its bound overload symbol and folds it to a factory parameter.
    /// <para>
    /// This overload shares the <c>Unquote</c> name with the value-source <see cref="Unquote{T}(T)"/> overload
    /// above. The two are distinguished by their bound overload — this one takes a <c>string?</c>/no argument, the
    /// other a value. When <c>T == string</c> a positional single-<c>string</c> call is genuinely ambiguous
    /// (CS0121) and must be disambiguated by naming the argument: <c>Unquote&lt;string&gt;(parameterName: "x")</c>
    /// for this parameter-source form, or <c>Unquote&lt;string&gt;(value: expr)</c> for the value-source form.
    /// </para>
    /// </summary>
    /// <typeparam name="T">The unquoted value's type; becomes the factory parameter's type. Must be explicit (it cannot be inferred from the string/omitted argument).</typeparam>
    /// <param name="parameterName">The factory parameter name; required in inline position, defaulted from the bound variable name in declaration position.</param>
    public static T Unquote<T>(string? parameterName = null) => default!;

    /// <summary>
    /// Declares an inline <em>quote</em>: <c>Quote(value)</c> emits the quoted syntax of the factory-time
    /// <paramref name="value"/> at this call position — the same value-lift as <c>Unquote(value)</c> in value
    /// position — but is the inline stage-0 → stage-1 boundary that <em>stops</em> liveness here, so an
    /// enclosing loop/condition driven through this call stays a runtime construct (no unroll). This is the only
    /// way to keep a loop whose bound is a <em>computed</em> factory-time value. The inline counterpart to a
    /// <c>[Quote]</c> parameter (mirroring how <c>Unquote(value)</c> is the inline counterpart to an
    /// <c>[Unquote]</c> parameter). Inert (<c>=&gt; value</c>) so the template body type-checks; the generator
    /// recognizes the call by binding and lifts the argument at factory-build time.
    /// </summary>
    /// <typeparam name="T">The quoted value's type; inferred from the argument.</typeparam>
    /// <param name="value">The factory-time value lifted to syntax at this site.</param>
    public static T Quote<T>(T value) => value;

    /// <summary>
    /// Declares a <em>splice</em>: <c>Splice(node)</c> inserts a pre-built <c>ExpressionSyntax</c> verbatim
    /// into the produced syntax at the call position, with no factory-time evaluation and no value lift. This
    /// is the inline/mid-expression counterpart to a <c>[Splice]</c> parameter, mirroring how
    /// <c>Unquote(value)</c> is the inline counterpart to an <c>[Unquote]</c> parameter. Inert
    /// (<c>=&gt; node</c>) so the template body type-checks; the generator recognizes the call by binding and
    /// splices the argument verbatim at factory-build time.
    /// </summary>
    /// <param name="node">The pre-built expression syntax to splice verbatim.</param>
    public static Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax Splice(Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax node) => node;
}

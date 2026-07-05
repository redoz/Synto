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
    /// <c>[Unquote]</c> value.
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

    /// <summary>
    /// In a <c>[Template]</c> body, <c>TypeOf("Name")</c> emits the type reference <c>Name</c> (a
    /// <c>TypeSyntax</c>) into the produced syntax. Hosted here — rather than as a <c>[SyntaxBuilder]</c> on
    /// <c>SyntoBuilders</c> the way <c>Member</c> migrated — because this facade must return <c>System.Type</c>
    /// in <em>value position</em> (e.g. <c>System.Type GetFieldType() =&gt; TypeOf(c.ColumnTypeName);</c>), while
    /// the paired builder in <c>SyntoBuilders</c> returns <c>ExpressionSyntax</c>. A same-signature
    /// <c>System.Type TypeOf(string)</c> facade there would be CS0111 against the builder (return-type-only
    /// difference); a synthesized generic escape hatch loses overload resolution to the builder's exact
    /// non-generic match. And unlike <c>Member</c> — whose <c>[Quoted] ExpressionSyntax</c> first parameter
    /// shields it, so a real author argument never binds to the builder and the call falls through to
    /// structural recognition instead — <c>TypeOf</c> takes only a factory-time type name that is itself often a
    /// live/staged value, so <c>[TypeArg]</c> (a compile-time type token) cannot express it either. Hence this
    /// hand-authored, <c>System.Type</c>-pinned facade stays on <c>Template</c> (a different type than
    /// <c>SyntoBuilders</c>) permanently, not as migration debt. Inert (<c>=&gt; default!</c>): the generator
    /// recognizes the call by binding and rewrites it at factory-build time to the matching builder in
    /// <c>SyntoBuilders</c>.
    /// </summary>
    /// <param name="name">The type name (a factory-time value).</param>
    public static System.Type TypeOf(string name) => default!;
}

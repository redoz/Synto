using System.Text;
using SF = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto;

/// <summary>
/// File-local helper that escapes a runtime string for placement inside a <em>regular</em> (non-verbatim,
/// non-raw) interpolated-string text token. Used by the interpolation staged-fold: when a bare staged-string
/// interpolation hole is baked into its surrounding literal text, the staged value is concatenated in via
/// <c>value.ToInterpolatedText()</c> so the fused characters render correctly as part of one
/// <c>InterpolatedStringText</c> token.
/// </summary>
/// <remarks>
/// Authored once <c>public</c> in <c>src\Synto</c>; embedded under <c>Synto.Helper.*</c> and emitted
/// <c>file static</c> by <see cref="FileLocalHelpers"/> so the injected copy can never collide with
/// <c>Synto.Core</c>'s public copy and the generated output carries zero <c>Synto.*</c> dependency. v1 targets
/// regular interpolated strings only — verbatim (<c>$@"…"</c>) and raw (<c>$"""…"""</c>) holes are a deferred
/// non-goal and the fold defers to the generated base for them, so this escaper never sees them.
/// </remarks>
public static class InterpolationSyntaxExtensions
{
    /// <summary>
    /// Escapes <paramref name="value"/> for inclusion in a regular interpolated-string text token. Full
    /// string-literal escaping — backslash, double-quote, and every control character (newline, tab,
    /// carriage-return, null, etc.) — is delegated to Roslyn's <see cref="SF.Literal(string)"/> so the
    /// fused text always forms a valid string-literal token; braces are then doubled so they render as
    /// literal braces rather than opening or closing an interpolation hole.
    /// </summary>
    public static string ToInterpolatedText(this string value)
    {
        // SyntaxFactory.Literal(string) always yields a regular (non-verbatim) quoted literal whose Text is
        // the fully-escaped source representation surrounded by double-quotes, e.g. "a\nb" for the value a<LF>b.
        string literal = SF.Literal(value).Text;

        // Strip the surrounding quotes to get just the escaped body, then double any braces (Roslyn leaves
        // braces unescaped since they are not special in a plain string literal, but they are inside an
        // interpolated-string text token).
        var builder = new StringBuilder(literal.Length);
        for (int i = 1; i < literal.Length - 1; i++)
        {
            char c = literal[i];
            switch (c)
            {
                case '{':
                    builder.Append("{{");
                    break;
                case '}':
                    builder.Append("}}");
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        return builder.ToString();
    }
}

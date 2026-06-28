using System.Text;

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
    /// Escapes <paramref name="value"/> for inclusion in a regular interpolated-string text token: backslash
    /// and double-quote are escaped for the underlying string-literal token, and braces are doubled so they
    /// render as literal braces rather than opening or closing an interpolation hole.
    /// </summary>
    public static string ToInterpolatedText(this string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
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

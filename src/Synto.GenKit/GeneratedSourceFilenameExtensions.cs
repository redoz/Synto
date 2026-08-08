using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Synto;

internal static class GeneratedSourceFilenameExtensions
{
    private static readonly SymbolDisplayFormat SymbolDisplayFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.OmittedAsContaining,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        propertyStyle: SymbolDisplayPropertyStyle.NameOnly,
        genericsOptions: SymbolDisplayGenericsOptions.None,
        memberOptions:
        SymbolDisplayMemberOptions.IncludeContainingType |
        SymbolDisplayMemberOptions.IncludeExplicitInterface,
        parameterOptions:
        SymbolDisplayParameterOptions.None,
        miscellaneousOptions:
        SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers
    );

    public static string ToGeneratedFilename(this ISymbol symbol)
    {
        if (symbol is null) throw new ArgumentNullException(nameof(symbol));

        var name = symbol.ToDisplayString(SymbolDisplayFormat);

        // Disambiguate overloaded partial methods (same name, different arity) so two [Diagnostic] overloads
        // do not both hash to the same hint name and collide inside AddSource. Arity is a pure function of the
        // symbol, so it stays deterministic per-transform (no cross-result counters).
        if (symbol is IMethodSymbol method)
            name += "`" + method.Parameters.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return name.SanitizeHintName() + ".g.cs";
    }

    /// <summary>
    /// Maps an arbitrary display string to a hint name Roslyn's <c>AddSource</c> accepts by whitelisting the
    /// characters that are legal in a generated-source hint (letters, digits, and <c>_ . , - + `</c>) and
    /// replacing every other character (angle brackets from generics, <c>?</c> from nullable types, parentheses
    /// and spaces from signatures, …) with <c>_</c>. Without this, a generic/nullable target name reaches
    /// <c>AddSource</c> verbatim and throws <see cref="ArgumentException"/>, killing the generator.
    /// </summary>
    public static string SanitizeHintName(this string raw)
    {
        if (raw is null) throw new ArgumentNullException(nameof(raw));

        var builder = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '.' || ch == ',' || ch == '-' || ch == '+' || ch == '`')
                builder.Append(ch);
            else
                builder.Append('_');
        }

        return builder.ToString();
    }
}

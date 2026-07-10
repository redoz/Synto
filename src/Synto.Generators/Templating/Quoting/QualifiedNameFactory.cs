using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto.Generators.Templating.Quoting;

/// <summary>
/// Renders a named-type or namespace symbol as its <c>global::</c>-rooted fully-qualified <see cref="NameSyntax"/>,
/// preserving generic type arguments and nested containing types (type parameters render by their bare name). Built
/// on <see cref="SymbolDisplayFormat.FullyQualifiedFormat"/> — the same primitive the decoration path uses to
/// qualify <c>[Implements&lt;T&gt;]</c> arguments — so generics and nested types render correctly by construction.
/// Runs inside the generator transform; returns a plain syntax value (no symbol/model captured).
/// </summary>
internal static class QualifiedNameFactory
{
    // FullyQualifiedFormat, but WITHOUT UseSpecialTypes — so a special type written out BY NAME (e.g. `System.Int32`
    // / `System.Object` as a type argument) renders as its global::-rooted namespace-qualified name
    // (global::System.Int32), which parses to a NameSyntax, rather than the `int` keyword (a PredefinedTypeSyntax the
    // NameSyntax cast would reject, crashing generation). Predefined keywords authored AS keywords are never visited
    // (they are PredefinedTypeSyntax, not name nodes); this only bites when a special type is spelled out by name,
    // which must still round-trip to a NameSyntax. Reference types (the common case) are unaffected.
    private static readonly SymbolDisplayFormat QualifiedFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions & ~SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    public static NameSyntax ForSymbol(ISymbol symbol)
        => (NameSyntax)SyntaxFactory.ParseTypeName(symbol.ToDisplayString(QualifiedFormat));
}

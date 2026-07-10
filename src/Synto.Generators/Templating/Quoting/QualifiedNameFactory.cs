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
    public static NameSyntax ForSymbol(ISymbol symbol)
        => (NameSyntax)SyntaxFactory.ParseTypeName(symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
}

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto.Bootstrap;

internal static class SyntaxHelpers
{

    public static NameSyntax GetTypeName(this ITypeSymbol symbol)
    {
        if (symbol.ContainingSymbol is not ITypeSymbol)
            return SyntaxFactory.IdentifierName(symbol.Name);
        return SyntaxFactory.QualifiedName(GetTypeName(symbol.ContainingType), SyntaxFactory.IdentifierName(symbol.Name));
    }

    public static NameSyntax GetQualifiedNameSyntax(this ISymbol symbol)
    {
        if (symbol.ContainingNamespace is { IsGlobalNamespace: true })
            return SyntaxFactory.IdentifierName(symbol.Name);
        return SyntaxFactory.QualifiedName(GetQualifiedNameSyntax(symbol.ContainingNamespace), SyntaxFactory.IdentifierName(symbol.Name));
    }
    public static T? GetAncestor<T>(this SyntaxNode syntax) where T : SyntaxNode
    {
        var parent = syntax?.Parent;

        if (parent is null)
            return null;

        if (parent is T t)
            return t;

        return GetAncestor<T>(parent);
    }
}
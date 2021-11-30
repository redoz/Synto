using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto
{
    internal static class SyntaxHelpers
    {

        public static NameSyntax GetTypeName(this ITypeSymbol symbol)
        {
            if (symbol.ContainingSymbol is not ITypeSymbol)
                return SyntaxFactory.IdentifierName(symbol.Name);
            return SyntaxFactory.QualifiedName(GetTypeName(symbol.ContainingType), SyntaxFactory.IdentifierName(symbol.Name));
        }

        public static NameSyntax GetNamespaceName(this ISymbol symbol)
        {
            if (symbol.ContainingNamespace is { IsGlobalNamespace: true })
                return SyntaxFactory.IdentifierName(symbol.Name);
            return SyntaxFactory.QualifiedName(GetNamespaceName(symbol.ContainingNamespace), SyntaxFactory.IdentifierName(symbol.Name));
        }
        public static T? GetAncestor<T>(this SyntaxNode? syntax) where T : SyntaxNode
        {
            var parent = syntax?.Parent;

            return parent switch
            {
                null => null,
                T t => t,
                _ => GetAncestor<T>(parent)
            };
        }
    }
}

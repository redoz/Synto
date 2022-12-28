using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto.Templating;

internal static class SyntaxHelpers
{
    public static NameSyntax? GetNamespaceName(this ISymbol symbol)
    {
        if (symbol is INamespaceSymbol { IsGlobalNamespace: true })
            return null;

        if (symbol.ContainingNamespace is { IsGlobalNamespace: true })
            return SyntaxFactory.IdentifierName(symbol.Name);
        return SyntaxFactory.QualifiedName(GetNamespaceName(symbol.ContainingNamespace)!, SyntaxFactory.IdentifierName(symbol.Name));
    }

}
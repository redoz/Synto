﻿using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto;

public static class SymbolExtensions
{
    // TODO this probably doesn't handle generic types?
    public static NameSyntax GetQualifiedNameSyntax(this ISymbol symbol)
    {
        if (symbol is null) throw new ArgumentNullException(nameof(symbol));

        if (symbol.ContainingNamespace is { IsGlobalNamespace: true })
            return SyntaxFactory.IdentifierName(symbol.Name);
        return SyntaxFactory.QualifiedName(GetQualifiedNameSyntax(symbol.ContainingNamespace), SyntaxFactory.IdentifierName(symbol.Name));
    }

    public static NameSyntax? GetNamespaceNameSyntax(this ISymbol symbol)
    {
        if (symbol is null) throw new ArgumentNullException(nameof(symbol));

        if (symbol is INamespaceSymbol { IsGlobalNamespace: true })
            return null;

        if (symbol.ContainingNamespace is { IsGlobalNamespace: true })
            return SyntaxFactory.IdentifierName(symbol.Name);

        return SyntaxFactory.QualifiedName(GetNamespaceNameSyntax(symbol.ContainingNamespace)!, SyntaxFactory.IdentifierName(symbol.Name));
    }
}
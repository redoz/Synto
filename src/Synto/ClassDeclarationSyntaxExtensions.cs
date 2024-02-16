using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto;

public static class ClassDeclarationSyntaxExtensions
{
    public static MemberDeclarationSyntax WithAncestryFrom(this MemberDeclarationSyntax target, ISymbol source)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (source is null) throw new ArgumentNullException(nameof(source));

        MemberDeclarationSyntax targetSyntax = target;

        ISymbol current = source.ContainingSymbol;

        while (current is ITypeSymbol)
        {
            var classDecls = (ClassDeclarationSyntax)current.DeclaringSyntaxReferences[0].GetSyntax();

            targetSyntax = ClassDeclaration(current.Name)
                .WithModifiers(classDecls.Modifiers)
                .AddMembers(targetSyntax);

            current = current.ContainingSymbol;
        }

        // if the template is defined in the global namespace this will return null
        var namespaceName = current.GetNamespaceNameSyntax();
        if (namespaceName is not null)
        {
            targetSyntax = FileScopedNamespaceDeclaration(namespaceName)
                .AddMembers(targetSyntax);
        }

        return targetSyntax;
    }
}
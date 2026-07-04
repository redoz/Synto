using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto;

internal static class ClassDeclarationSyntaxExtensions
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

            // Carry the ancestor's type parameters + constraints: a generic ancestor (partial class Outer<T>)
            // must be rebuilt as `Outer<T>` so the emitted nesting merges with the user's declaration instead
            // of declaring a distinct arity-0 twin beside it. WithTypeParameterList(null)/empty constraints is
            // a no-op for a non-generic ancestor, so arity-0 goldens stay byte-identical.
            targetSyntax = ClassDeclaration(current.Name)
                .WithModifiers(classDecls.Modifiers)
                .WithTypeParameterList(classDecls.TypeParameterList)
                .WithConstraintClauses(classDecls.ConstraintClauses)
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

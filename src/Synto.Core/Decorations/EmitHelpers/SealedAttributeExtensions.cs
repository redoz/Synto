using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto;

public static class SealedAttributeExtensions
{
    public static T ApplySealedAttribute<T>(this T node) where T : TypeDeclarationSyntax
    {
        var modifiers = node.Modifiers;
        for (int i = 0; i < modifiers.Count; i++)
            if (modifiers[i].Kind() == SyntaxKind.SealedKeyword)
                return node;

        // Insert 'sealed' BEFORE 'partial' when present so the modifier list stays legal C# — 'partial' must
        // appear immediately before 'class'. Appending would yield "... partial sealed class" -> CS0267.
        var sealedToken = Token(SyntaxKind.SealedKeyword);
        for (int i = 0; i < modifiers.Count; i++)
            if (modifiers[i].Kind() == SyntaxKind.PartialKeyword)
                return (T)node.WithModifiers(modifiers.Insert(i, sealedToken));

        return (T)node.WithModifiers(modifiers.Add(sealedToken));
    }
}

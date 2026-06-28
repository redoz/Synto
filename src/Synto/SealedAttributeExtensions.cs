using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto;

public static class SealedAttributeExtensions
{
    public static T ApplySealedAttribute<T>(this T node) where T : TypeDeclarationSyntax
    {
        foreach (var m in node.Modifiers)
            if (m.Kind() == SyntaxKind.SealedKeyword)
                return node;
        return (T)node.WithModifiers(node.Modifiers.Add(Token(SyntaxKind.SealedKeyword)));
    }
}

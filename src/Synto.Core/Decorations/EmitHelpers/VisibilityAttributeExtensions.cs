using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.Templating;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto;

public static class VisibilityAttributeExtensions
{
    public static T ApplyVisibilityAttribute<T>(this T node, Access access) where T : MemberDeclarationSyntax
    {
        SyntaxToken[] newTokens = AccessTokens(access);
        SyntaxTokenList result = TokenList(newTokens);
        foreach (var m in node.Modifiers)
            if (!IsAccessModifier(m.Kind()))
                result = result.Add(m);
        return (T)node.WithModifiers(result);
    }

    private static bool IsAccessModifier(SyntaxKind kind) => kind is
        SyntaxKind.PublicKeyword or
        SyntaxKind.InternalKeyword or
        SyntaxKind.PrivateKeyword or
        SyntaxKind.ProtectedKeyword or
        SyntaxKind.FileKeyword;

    private static SyntaxToken[] AccessTokens(Access access) => access switch
    {
        Access.Public => [Token(SyntaxKind.PublicKeyword)],
        Access.Internal => [Token(SyntaxKind.InternalKeyword)],
        Access.Private => [Token(SyntaxKind.PrivateKeyword)],
        Access.Protected => [Token(SyntaxKind.ProtectedKeyword)],
        Access.ProtectedInternal => [Token(SyntaxKind.ProtectedKeyword), Token(SyntaxKind.InternalKeyword)],
        Access.PrivateProtected => [Token(SyntaxKind.PrivateKeyword), Token(SyntaxKind.ProtectedKeyword)],
        Access.File => [Token(SyntaxKind.FileKeyword)],
        _ => throw new System.ArgumentOutOfRangeException(nameof(access), access, null)
    };
}

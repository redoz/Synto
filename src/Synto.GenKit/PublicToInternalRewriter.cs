using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto.GenKit;

/// <summary>
/// Rewrites the accessibility of type and delegate declarations from <c>public</c> to
/// <c>internal</c>. Members (properties, methods, fields, ...) are left untouched: their authored
/// accessibility is what the injected copy should keep, and an internal containing type already
/// hides them from outside the consumer's assembly. The Synto surface only declares top-level
/// types, so in practice this just flips each file's single public type to internal.
/// </summary>
internal sealed class PublicToInternalRewriter : CSharpSyntaxRewriter
{
    public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
        => ReplacePublicWithInternal(node, node.Modifiers, node.WithModifiers);

    public override SyntaxNode? VisitStructDeclaration(StructDeclarationSyntax node)
        => ReplacePublicWithInternal(node, node.Modifiers, node.WithModifiers);

    public override SyntaxNode? VisitEnumDeclaration(EnumDeclarationSyntax node)
        => ReplacePublicWithInternal(node, node.Modifiers, node.WithModifiers);

    public override SyntaxNode? VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
        => ReplacePublicWithInternal(node, node.Modifiers, node.WithModifiers);

    public override SyntaxNode? VisitRecordDeclaration(RecordDeclarationSyntax node)
        => ReplacePublicWithInternal(node, node.Modifiers, node.WithModifiers);

    public override SyntaxNode? VisitDelegateDeclaration(DelegateDeclarationSyntax node)
        => ReplacePublicWithInternal(node, node.Modifiers, node.WithModifiers);

    private static SyntaxNode ReplacePublicWithInternal<TNode>(
        TNode node,
        SyntaxTokenList modifiers,
        Func<SyntaxTokenList, TNode> withModifiers)
        where TNode : SyntaxNode
    {
        int index = modifiers.IndexOf(SyntaxKind.PublicKeyword);
        if (index < 0)
            return node;

        SyntaxToken oldToken = modifiers[index];
        SyntaxToken newToken = SyntaxFactory
            .Token(SyntaxKind.InternalKeyword)
            .WithTriviaFrom(oldToken);

        return withModifiers(modifiers.Replace(oldToken, newToken));
    }
}

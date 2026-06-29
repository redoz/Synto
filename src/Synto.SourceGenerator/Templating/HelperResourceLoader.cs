using System;
using System.IO;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto;

/// <summary>
/// Loads an embedded helper resource and rewrites it from a <c>public</c> namespaced class into a
/// <c>file static class</c> with no namespace, so a private copy can live in the same compilation unit as the
/// factory that uses it. The <c>public</c>→<c>file</c> outcome is byte-load-bearing (the emitted copy can never
/// escape its file, so it never collides with <c>Synto.Core</c>'s public copies); the rewriter is deliberately
/// distinct in outcome (<c>file</c>, not <c>internal</c>) from
/// <c>SurfaceInjectionGenerator.PublicToInternalRewriter</c>. Runs inside the generator; nothing captured into
/// pipeline state.
/// </summary>
internal static class HelperResourceLoader
{
    public static FileLocalHelpers.Helper Load(string resourceName)
    {
        var assembly = typeof(HelperResourceLoader).Assembly;

        string source = ReadResource(assembly, resourceName);

        var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();

        // The helper's own using directives (e.g. `using SF = ...SyntaxFactory;`) must be merged into the
        // generated compilation unit, since C# requires usings before any declaration.
        var usings = root.Usings;

        // Pull out the single top-level type and rewrite it from a public namespaced class to a
        // file-local one with no namespace.
        var rewriter = new PublicToFileRewriter();

        TypeDeclarationSyntax? declaration = null;
        foreach (var member in root.Members)
        {
            switch (member)
            {
                case NamespaceDeclarationSyntax ns:
                    declaration = FindType(ns.Members, rewriter);
                    break;
                case FileScopedNamespaceDeclarationSyntax fileNs:
                    declaration = FindType(fileNs.Members, rewriter);
                    break;
                case TypeDeclarationSyntax type:
                    declaration = (TypeDeclarationSyntax)rewriter.Visit(type)!;
                    break;
            }

            if (declaration is not null)
                break;
        }

        if (declaration is null)
            throw new InvalidOperationException($"Embedded helper resource '{resourceName}' did not contain a top-level type declaration.");

        return new FileLocalHelpers.Helper(usings, declaration);
    }

    private static TypeDeclarationSyntax? FindType(SyntaxList<MemberDeclarationSyntax> members, PublicToFileRewriter rewriter)
    {
        foreach (var member in members)
        {
            if (member is TypeDeclarationSyntax type)
                return (TypeDeclarationSyntax)rewriter.Visit(type)!;
        }

        return null;
    }

    private static string ReadResource(Assembly assembly, string resourceName)
    {
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            throw new InvalidOperationException($"Embedded helper resource '{resourceName}' was not found.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Rewrites a helper's single top-level type from <c>public</c> to the <c>file</c> access modifier.
    /// Members keep their authored accessibility (a <c>file</c> containing type already scopes them to
    /// the one compilation unit). This mirrors <c>SurfaceInjectionGenerator.PublicToInternalRewriter</c>
    /// but targets the <c>file</c> modifier so the emitted copy can never escape its file.
    /// </summary>
    private sealed class PublicToFileRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
            => ReplacePublicWithFile(node, node.Modifiers, node.WithModifiers);

        public override SyntaxNode? VisitStructDeclaration(StructDeclarationSyntax node)
            => ReplacePublicWithFile(node, node.Modifiers, node.WithModifiers);

        private static SyntaxNode ReplacePublicWithFile<TNode>(
            TNode node,
            SyntaxTokenList modifiers,
            Func<SyntaxTokenList, TNode> withModifiers)
            where TNode : SyntaxNode
        {
            int index = modifiers.IndexOf(SyntaxKind.PublicKeyword);
            if (index < 0)
                return node;

            SyntaxToken oldToken = modifiers[index];
            SyntaxToken newToken = Token(SyntaxKind.FileKeyword).WithTriviaFrom(oldToken);

            return withModifiers(modifiers.Replace(oldToken, newToken));
        }
    }
}

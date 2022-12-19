using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using Synto.Runtime;
using Synto.Utils;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;


namespace Synto;

// this needs to be a base-class because for some reason the generated source cannot see the contents of its partial class
public abstract class CSharpSyntaxQuoterBase : CSharpSyntaxVisitor<ExpressionSyntax>
{
    protected static InvocationExpressionSyntax SyntaxFactoryInvocation(string functionName, params ExpressionSyntax[] arguments)
    {
        return InvocationExpression(IdentifierName(functionName))
            .AddArgumentListArguments(Array.ConvertAll(arguments, Argument));
    }


    public virtual ExpressionSyntax Visit<TNode>(SyntaxList<TNode> nodeList) where TNode : SyntaxNode
    {
        // when attributes are filtered from the syntax tree we will end up in situations where we have nulls in this nodeList
        // originally we rewrote the syntax tree to exclude the Attribute before passing down the mutated syntax tree, but that 
        // invalidates our semantic model. This seems to fix the problem even if it's not the nicest solution.
        IEnumerable<ExpressionSyntax> quotedExprs = nodeList.Select(Visit).Where(node => node is not null)!;

        TypeSyntax elementType = ParseTypeName(typeof(TNode).Name);
        return InvocationExpression(
                GenericName(
                    Identifier(nameof(List)),
                    TypeArgumentList(
                        SingletonSeparatedList(elementType))),
            ArgumentList(SingletonSeparatedList(Argument(ToArrayLiteral(quotedExprs, elementType)))));
    }

    public virtual ExpressionSyntax Visit<TNode>(SeparatedSyntaxList<TNode> nodeList) where TNode : SyntaxNode
    {
        // when attributes are filtered from the syntax tree we will end up in situations where we have nulls in this nodeList
        // originally we rewrote the syntax tree to exclude the Attribute before passing down the mutated syntax tree, but that 
        // invalidates our semantic model. This seems to fix the problem even if it's not the nicest solution.
        var quotedExprs = nodeList.GetWithSeparators()
            .Select(item => item.IsToken ? QuoteSyntaxToken(item.AsToken()) : Visit(item.AsNode()))
            .ToList();

        for (int i = 0; i < quotedExprs.Count;)
        {
            if (quotedExprs[i] is null)
            {
                quotedExprs.RemoveAt(i); // remove null-node
                if (i < quotedExprs.Count)
                    quotedExprs.RemoveAt(i); // remove token too
            } 
            else
                i++;
        }

        TypeSyntax elementType = ParseTypeName(typeof(TNode).Name);
        return InvocationExpression(
                GenericName(
                    Identifier(nameof(SeparatedList)),
                    TypeArgumentList(
                        SingletonSeparatedList(elementType))),
                ArgumentList(SingletonSeparatedList(Argument(ToArrayLiteral(quotedExprs!, IdentifierName(nameof(SyntaxNodeOrToken)))))));
    }
    protected static ExpressionSyntax QuoteSyntaxToken(SyntaxToken token)
    {
        return SyntaxFactoryInvocation(nameof(Token), QuoteSyntaxKind(token.Kind()));
    }

    protected static ExpressionSyntax QuoteSyntaxKind(SyntaxKind kind)
    {
        return IdentifierName(kind.ToString());
    }

    protected static ExpressionSyntax ToArrayLiteral(IEnumerable<ExpressionSyntax> nodeList, TypeSyntax elementType)
    {
        var list = nodeList.ToList();
        if (list.Count == 0)
        {
            return InvocationExpression(
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName(nameof(Array)),
                    GenericName(
                        Identifier(nameof(Array.Empty)),
                        TypeArgumentList(SingletonSeparatedList(elementType)))));
        }

        return ArrayCreationExpression(
            ArrayType(elementType,
                SingletonList(ArrayRankSpecifier(SingletonSeparatedList<ExpressionSyntax>(OmittedArraySizeExpression())))),
            InitializerExpression(
                SyntaxKind.ArrayInitializerExpression,
                SeparatedList(list)));
    }

    public virtual ExpressionSyntax Visit(SyntaxKind kind)
    {
        return IdentifierName(kind.ToString());
    }

    public virtual ExpressionSyntax Visit(SyntaxToken token)
    {
        static bool TokenKindHasText(SyntaxKind kind) => SyntaxFacts.GetText(kind) != string.Empty;

        return token.Kind() switch
        {
            SyntaxKind.BadToken => SyntaxFactoryInvocation(nameof(BadToken),  Visit(token.LeadingTrivia), token.Text.ToLiteral(), Visit(token.TrailingTrivia)),
            SyntaxKind.IdentifierToken => SyntaxFactoryInvocation(nameof(Identifier), Visit(token.LeadingTrivia), token.Text.ToLiteral(), Visit(token.TrailingTrivia)),
            SyntaxKind.NumericLiteralToken => token.Value switch
            {
                byte value => SyntaxFactoryInvocation(nameof(Literal), Visit(token.LeadingTrivia), token.Text.ToLiteral(), value.ToLiteral(), Visit(token.TrailingTrivia)),
                sbyte value => SyntaxFactoryInvocation(nameof(Literal), Visit(token.LeadingTrivia), token.Text.ToLiteral(), value.ToLiteral(), Visit(token.TrailingTrivia)),
                short value => SyntaxFactoryInvocation(nameof(Literal), Visit(token.LeadingTrivia), token.Text.ToLiteral(), value.ToLiteral(), Visit(token.TrailingTrivia)),
                ushort value => SyntaxFactoryInvocation(nameof(Literal), Visit(token.LeadingTrivia), token.Text.ToLiteral(), value.ToLiteral(), Visit(token.TrailingTrivia)),
                int value => SyntaxFactoryInvocation(nameof(Literal), Visit(token.LeadingTrivia), token.Text.ToLiteral(), value.ToLiteral(), Visit(token.TrailingTrivia)),
                uint value => SyntaxFactoryInvocation(nameof(Literal), Visit(token.LeadingTrivia), token.Text.ToLiteral(), value.ToLiteral(), Visit(token.TrailingTrivia)),
                long value => SyntaxFactoryInvocation(nameof(Literal), Visit(token.LeadingTrivia), token.Text.ToLiteral(), value.ToLiteral(), Visit(token.TrailingTrivia)),
                ulong value => SyntaxFactoryInvocation(nameof(Literal), Visit(token.LeadingTrivia), token.Text.ToLiteral(), value.ToLiteral(), Visit(token.TrailingTrivia)),
                decimal value => SyntaxFactoryInvocation(nameof(Literal), Visit(token.LeadingTrivia), token.Text.ToLiteral(), value.ToLiteral(), Visit(token.TrailingTrivia)),
                double value => SyntaxFactoryInvocation(nameof(Literal), Visit(token.LeadingTrivia), token.Text.ToLiteral(), value.ToLiteral(), Visit(token.TrailingTrivia)),
                float value => SyntaxFactoryInvocation(nameof(Literal), Visit(token.LeadingTrivia), token.Text.ToLiteral(), value.ToLiteral(), Visit(token.TrailingTrivia)),
                _ => throw new NotImplementedException($"Unable to create literal token of type {token.Value?.GetType().FullName ?? "null"}")
            },
            SyntaxKind.CharacterLiteralToken => SyntaxFactoryInvocation(nameof(Literal), Visit(token.LeadingTrivia), token.Text.ToLiteral(), ((char)token.Value!).ToLiteral(), Visit(token.TrailingTrivia)),
            SyntaxKind.StringLiteralToken => SyntaxFactoryInvocation(nameof(Literal), Visit(token.LeadingTrivia), token.Text.ToLiteral(), ((string)token.Value!).ToLiteral(), Visit(token.TrailingTrivia)),

            SyntaxKind.None => SyntaxFactoryInvocation(nameof(Token), Visit(token.LeadingTrivia), Visit(SyntaxKind.None), Visit(token.TrailingTrivia)),
            //SyntaxKind.XmlEntityLiteralToken => XmlEntity(token.LeadingTrivia, token.Text, (string)token.Value!, token.TrailingTrivia),
            //SyntaxKind.XmlTextLiteralToken => XmlText((string)token.Value!),
            //SyntaxKind.XmlTextLiteralNewLineToken => XmlTextLiteral(token.Text, (string)token.Value!),

            //SyntaxKind.InterpolatedStringTextToken => Token(token.LeadingTrivia, token.Kind(), token.Text, token.ValueText, token.TrailingTrivia),
            //_ => throw new NotImplementedException($"Unable to create literal token (Kind: {token.Kind()}, Type {token.Value?.GetType().FullName ?? "null"}): '{token.ToFullString()}'")

            var tokenKind when TokenKindHasText(tokenKind) => SyntaxFactoryInvocation(nameof(Token), Visit(token.LeadingTrivia), Visit(tokenKind), Visit(token.TrailingTrivia)),
            var tokenKind => SyntaxFactoryInvocation(nameof(Token), Visit(token.LeadingTrivia), Visit(tokenKind), token.Text.ToLiteral(), ((string)token.Value!).ToLiteral(), Visit(token.TrailingTrivia)),
        };
    }

    private ExpressionSyntax Visit(SyntaxTriviaList triviaList)
    {
        // TODO this just returns an empty trivia list
        return SyntaxFactoryInvocation(nameof(TriviaList));
    }

    public virtual ExpressionSyntax Visit(SyntaxTokenList tokenList)
    {
        return SyntaxFactoryInvocation(nameof(TokenList), tokenList.Select(Visit).ToArray());
    }

    public override ExpressionSyntax? DefaultVisit(SyntaxNode node)
    {
        throw new NotImplementedException($"Unable to visit node (Kind: {node.Kind()}, Type: {node.GetType().FullName}: '{node.ToString()}'");
    }


}

public partial class CSharpSyntaxQuoter :  CSharpSyntaxQuoterBase
{
    //private readonly UsingDirectiveSet _additionalUsings;

    public static IEnumerable<UsingDirectiveSyntax> RequiredUsings()
    {
        return new List<UsingDirectiveSyntax>()
        {
            // Synto.Runtime
            UsingDirective(ParseName(typeof(LiteralSyntaxExtensions).Namespace)), 
            // System
            UsingDirective(ParseName(typeof(Array).Namespace)),
            UsingDirective(ParseName(typeof(SyntaxNodeOrToken).Namespace)),
            UsingDirective(ParseName(typeof(ArgumentSyntax).Namespace)),
            // static SyntaxFactory
            UsingDirective(ParseName(typeof(SyntaxFactory).FullName))
                .WithStaticKeyword(Token(SyntaxKind.StaticKeyword)),
            // static SyntaxKind
            UsingDirective(ParseName(typeof(SyntaxKind).FullName))
                .WithStaticKeyword(Token(SyntaxKind.StaticKeyword))
        };
    }


    public override ExpressionSyntax? VisitIdentifierName(IdentifierNameSyntax node)
    {
        // IdentifierName(node.Identifier.Text)
        return InvocationExpression(IdentifierName(nameof(IdentifierName)),
            ArgumentList(
                SingletonSeparatedList(
                    Argument(
                        LiteralExpression(
                            SyntaxKind.StringLiteralExpression,
                            Literal(
                                node.Identifier.ValueText))))));
    }

    //public override ExpressionSyntax? VisitLiteralExpression(LiteralExpressionSyntax node)
    //{
    //    LiteralExpression(node.Kind(), Literal(node.))
    //    return base.VisitLiteralExpression(node);
    //}
}

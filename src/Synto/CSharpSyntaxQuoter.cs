using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;

using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;


namespace Synto;

// this needs to be a base-class because for some reason the generates source cannot see the contents of it's partial class
public class CSharpSyntaxQuoterBase : CSharpSyntaxVisitor<ExpressionSyntax>
{
    protected static readonly NameSyntax SyntaxFactoryToken = ParseName(typeof(SyntaxFactory).FullName!);

    protected static InvocationExpressionSyntax SyntaxFactoryInvocation(string functionName, params ExpressionSyntax[] arguments)
    {
        return InvocationExpression(
            MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactoryToken,
                IdentifierName(functionName)))
            .AddArgumentListArguments(Array.ConvertAll(arguments, Argument));
    }


    protected static ExpressionSyntax ToArrayLiteral(IEnumerable<ExpressionSyntax> nodeList, TypeSyntax elementType)
    {
        var list = nodeList.ToList();
        if (list.Count == 0)
        {
            return InvocationExpression(
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    ParseTypeName(typeof(Array).FullName!),
                    GenericName(
                        Identifier(nameof(Array.Empty)),
                        TypeArgumentList(SingletonSeparatedList(elementType)))));
        }
        return ImplicitArrayCreationExpression(
            InitializerExpression(
                SyntaxKind.ArrayInitializerExpression,
                SeparatedList(list)));
    }


    public virtual ExpressionSyntax Visit<TNode>(SyntaxList<TNode> nodeList) where TNode : SyntaxNode
    {
        var quotedExprs = nodeList.Select(node => Visit(node)!);

        TypeSyntax elementType = ParseTypeName(typeof(TNode).FullName!);
        return InvocationExpression(
            MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactoryToken,
                GenericName(
                    Identifier(nameof(List)),
                    TypeArgumentList(
                        SingletonSeparatedList(elementType)))),
            ArgumentList(SingletonSeparatedList(Argument(ToArrayLiteral(quotedExprs, elementType)))));
    }

    public virtual ExpressionSyntax Visit<TNode>(SeparatedSyntaxList<TNode> nodeList) where TNode : SyntaxNode
    {
        return SyntaxFactoryInvocation(nameof(SeparatedList),
            ToArrayLiteral(nodeList.Select(t => Visit(t)!), ParseTypeName(typeof(TNode).FullName!)),
            ToArrayLiteral(nodeList.GetSeparators().Select(Visit), ParseTypeName(typeof(SyntaxToken).FullName!)));
    }

    public virtual ExpressionSyntax Visit(SyntaxKind kind)
    {
        return MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            ParseName(typeof(SyntaxKind).FullName!),
            IdentifierName(kind.ToString()));
    }

    public virtual ExpressionSyntax Visit(SyntaxToken token)
    {
        //SyntaxFactory.Token(token.LeadingTrivia, token.Kind(), token.Text, token.ValueText, token.TrailingTrivia);
        //return SyntaxFactoryInvocation(
        //    nameof(Token),
        //    SyntaxFactoryInvocation(nameof(TriviaList)),
        //    Visit(token.Kind()),
        //    Visit(token.Text),
        //    Visit(token.ValueText),
        //    SyntaxFactoryInvocation(nameof(TriviaList)));
        var to =  token.Kind() switch
        {
            SyntaxKind.BadToken => BadToken(token.LeadingTrivia, token.Text, token.TrailingTrivia),
            SyntaxKind.IdentifierToken => Identifier(token.LeadingTrivia, token.Text, token.TrailingTrivia),
            SyntaxKind.NumericLiteralToken => token.Value switch
            {
                byte value => Literal(token.LeadingTrivia, token.Text, value, token.TrailingTrivia),
                sbyte value => Literal(token.LeadingTrivia, token.Text, value, token.TrailingTrivia),
                short value => Literal(token.LeadingTrivia, token.Text, value, token.TrailingTrivia),
                ushort value => Literal(token.LeadingTrivia, token.Text, value, token.TrailingTrivia),
                int value => Literal(token.LeadingTrivia, token.Text, value, token.TrailingTrivia),
                uint value => Literal(token.LeadingTrivia, token.Text, value, token.TrailingTrivia),
                long value => Literal(token.LeadingTrivia, token.Text, value, token.TrailingTrivia),
                ulong value => Literal(token.LeadingTrivia, token.Text, value, token.TrailingTrivia),
                decimal value => Literal(token.LeadingTrivia, token.Text, value, token.TrailingTrivia),
                double value => Literal(token.LeadingTrivia, token.Text, value, token.TrailingTrivia),
                float value => Literal(token.LeadingTrivia, token.Text, value, token.TrailingTrivia),
                _ => throw new NotSupportedException($"Unable to create literal token of type {token.Value?.GetType().FullName ?? "null"}")
            },
            SyntaxKind.CharacterLiteralToken => Literal(token.LeadingTrivia, token.Text, (char)token.Value!, token.TrailingTrivia),
            SyntaxKind.StringLiteralToken => Literal(token.LeadingTrivia, token.Text, (string)token.Value!, token.TrailingTrivia),
            SyntaxKind.XmlEntityLiteralToken => XmlEntity(token.LeadingTrivia, token.Text, (string)token.Value!, token.TrailingTrivia),
            SyntaxKind.XmlTextLiteralToken => XmlText((string)token.Value!),
            SyntaxKind.XmlTextLiteralNewLineToken => XmlTextLiteral(token.Text, (string)token.Value!),

            SyntaxKind.InterpolatedStringTextToken => Token(token.LeadingTrivia ,token.Kind(), token.Text, token.ValueText, token.TrailingTrivia),     
        };
        return SyntaxFactoryInvocation(nameof(Token), Visit(token.Kind()));
    }

    public virtual ExpressionSyntax Visit(SyntaxTokenList tokenList)
    {
        return SyntaxFactoryInvocation(nameof(TokenList), tokenList.Select(Visit).ToArray());
    }

    public override ExpressionSyntax? DefaultVisit(SyntaxNode node)
    {
        throw new NotImplementedException($"Unable to visit node of type {node.GetType().FullName}");
    }


}

public partial class CSharpSyntaxQuoter :  CSharpSyntaxQuoterBase
{
    public override ExpressionSyntax? VisitIdentifierName(IdentifierNameSyntax node)
    {
        // IdentifierName(node.Identifier.Text)
        return InvocationExpression(
            MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactoryToken,
                IdentifierName(nameof(IdentifierName))),
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

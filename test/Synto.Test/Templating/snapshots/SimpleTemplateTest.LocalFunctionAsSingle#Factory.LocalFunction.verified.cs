//HintName: Factory.LocalFunction.cs
using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;
using Synto;

partial class Factory
{
    public static ExpressionStatementSyntax LocalFunction()
    {
        return ExpressionStatement(
                   List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                   InvocationExpression(
                       MemberAccessExpression(
                           SimpleMemberAccessExpression, 
                           IdentifierName("Console"), 
                           Token(DotToken), 
                           IdentifierName("WriteLine")), 
                       ArgumentList(
                           Token(OpenParenToken), 
                           SeparatedList<ArgumentSyntax>(
                               new SyntaxNodeOrToken[]{
                                   Argument(
                                       null, 
                                       Token(None), 
                                       LiteralExpression(
                                           StringLiteralExpression, 
                                           Literal(
                                               "\"Hello world\"", 
                                               "Hello world")))}), 
                           Token(CloseParenToken))), 
                   Token(SemicolonToken));
    }
}
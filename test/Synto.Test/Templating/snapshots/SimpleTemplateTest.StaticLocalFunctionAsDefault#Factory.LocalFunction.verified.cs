//HintName: Factory.LocalFunction.cs
using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;
using Synto;

partial class Factory
{
    public static LocalFunctionStatementSyntax LocalFunction()
    {
        return LocalFunctionStatement(
                   List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                   TokenList(Token(StaticKeyword)), 
                   PredefinedType(Token(VoidKeyword)), 
                   Identifier("LocalFunction"), 
                   null, 
                   ParameterList(
                       Token(OpenParenToken), 
                       SeparatedList<ParameterSyntax>(Array.Empty<SyntaxNodeOrToken>()), 
                       Token(CloseParenToken)), 
                   List<TypeParameterConstraintClauseSyntax>(Array.Empty<TypeParameterConstraintClauseSyntax>()), 
                   Block(
                       List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                       Token(OpenBraceToken), 
                       List<StatementSyntax>(
                           new StatementSyntax[] { 
                               ExpressionStatement(
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
                                               new SyntaxNodeOrToken[] { 
                                                   Argument(
                                                       null, 
                                                       Token(None), 
                                                       LiteralExpression(
                                                           StringLiteralExpression, 
                                                           Literal(
                                                               "\"Hello world\"", 
                                                               "Hello world"))) }), 
                                           Token(CloseParenToken))), 
                                   Token(SemicolonToken)) }), 
                       Token(CloseBraceToken)), 
                   null, 
                   Token(None));
    }
}
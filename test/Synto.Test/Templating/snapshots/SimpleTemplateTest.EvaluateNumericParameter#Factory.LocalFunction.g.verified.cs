//HintName: Factory.LocalFunction.g.cs
using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;
using Synto;

partial class Factory
{
    public static MethodDeclarationSyntax LocalFunction()
    {
        return MethodDeclaration(
                   List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                   TokenList(), 
                   PredefinedType(Token(VoidKeyword)), 
                   null, 
                   Identifier("LocalFunction"), 
                   null, 
                   ParameterList(
                       Token(OpenParenToken), 
                       SeparatedList<ParameterSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Parameter(
                                   List<AttributeListSyntax>(
                                       new AttributeListSyntax[] { 
                                           AttributeList(
                                               Token(OpenBracketToken), 
                                               null, 
                                               SeparatedList<AttributeSyntax>(
                                                   new SyntaxNodeOrToken[] { 
                                                       Attribute(
                                                           IdentifierName("Unquote"), 
                                                           null) }), 
                                               Token(CloseBracketToken)) }), 
                                   TokenList(), 
                                   PredefinedType(Token(IntKeyword)), 
                                   Identifier("n"), 
                                   null) }), 
                       Token(CloseParenToken)), 
                   List<TypeParameterConstraintClauseSyntax>(Array.Empty<TypeParameterConstraintClauseSyntax>()), 
                   Block(
                       List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                       Token(OpenBraceToken), 
                       List<StatementSyntax>(
                           new StatementSyntax[] { 
                               ForStatement(
                                   List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                                   Token(ForKeyword), 
                                   Token(OpenParenToken), 
                                   VariableDeclaration(
                                       PredefinedType(Token(IntKeyword)), 
                                       SeparatedList<VariableDeclaratorSyntax>(
                                           new SyntaxNodeOrToken[] { 
                                               VariableDeclarator(
                                                   Identifier("i"), 
                                                   null, 
                                                   EqualsValueClause(
                                                       Token(EqualsToken), 
                                                       LiteralExpression(
                                                           NumericLiteralExpression, 
                                                           Literal(
                                                               "0", 
                                                               0)))) })), 
                                   SeparatedList<ExpressionSyntax>(Array.Empty<SyntaxNodeOrToken>()), 
                                   Token(SemicolonToken), 
                                   BinaryExpression(
                                       LessThanExpression, 
                                       IdentifierName("i"), 
                                       Token(LessThanToken), 
                                       IdentifierName("n")), 
                                   Token(SemicolonToken), 
                                   SeparatedList<ExpressionSyntax>(
                                       new SyntaxNodeOrToken[] { 
                                           PostfixUnaryExpression(
                                               PostIncrementExpression, 
                                               IdentifierName("i"), 
                                               Token(PlusPlusToken)) }), 
                                   Token(CloseParenToken), 
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
                                                                       InterpolatedStringExpression(
                                                                           Token(InterpolatedStringStartToken), 
                                                                           List<InterpolatedStringContentSyntax>(
                                                                               new InterpolatedStringContentSyntax[] { 
                                                                                   InterpolatedStringText(
                                                                                       Token(
                                                                                           TriviaList(), 
                                                                                           InterpolatedStringTextToken, 
                                                                                           "Hello world ", 
                                                                                           "Hello world ", 
                                                                                           TriviaList())), 
                                                                                   Interpolation(
                                                                                       Token(OpenBraceToken), 
                                                                                       IdentifierName("value"), 
                                                                                       null, 
                                                                                       null, 
                                                                                       Token(CloseBraceToken)) }), 
                                                                           Token(InterpolatedStringEndToken))) }), 
                                                           Token(CloseParenToken))), 
                                                   Token(SemicolonToken)) }), 
                                       Token(CloseBraceToken))) }), 
                       Token(CloseBraceToken)), 
                   null, 
                   Token(None));
    }
}
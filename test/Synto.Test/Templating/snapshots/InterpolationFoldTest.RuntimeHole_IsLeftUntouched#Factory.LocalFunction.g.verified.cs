//HintName: Factory.LocalFunction.g.cs
#nullable enable
using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;

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
                                   List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                                   TokenList(), 
                                   PredefinedType(Token(IntKeyword)), 
                                   Identifier("i"), 
                                   null) }), 
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
                                                       InterpolatedStringExpression(
                                                           Token(InterpolatedStringStartToken), 
                                                           List<InterpolatedStringContentSyntax>(
                                                               new InterpolatedStringContentSyntax[] { 
                                                                   InterpolatedStringText(
                                                                       Token(
                                                                           TriviaList(), 
                                                                           InterpolatedStringTextToken, 
                                                                           "x ", 
                                                                           "x ", 
                                                                           TriviaList())), 
                                                                   Interpolation(
                                                                       Token(OpenBraceToken), 
                                                                       IdentifierName("i"), 
                                                                       null, 
                                                                       null, 
                                                                       Token(CloseBraceToken)), 
                                                                   InterpolatedStringText(
                                                                       Token(
                                                                           TriviaList(), 
                                                                           InterpolatedStringTextToken, 
                                                                           " y", 
                                                                           " y", 
                                                                           TriviaList())) }), 
                                                           Token(InterpolatedStringEndToken))) }), 
                                           Token(CloseParenToken))), 
                                   Token(SemicolonToken)) }), 
                       Token(CloseBraceToken)), 
                   null, 
                   Token(None));
    }
}
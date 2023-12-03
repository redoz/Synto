//HintName: Factory.LocalFunction.g.cs
using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;
using Synto;

partial class Factory
{
    public static MethodDeclarationSyntax LocalFunction<T>(T value)
    {
        ExpressionSyntax syntaxForParam_value = value.ToSyntax();
        return MethodDeclaration(
                   List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                   TokenList(), 
                   PredefinedType(Token(VoidKeyword)), 
                   null, 
                   Identifier("LocalFunction"), 
                   TypeParameterList(
                       Token(LessThanToken), 
                       SeparatedList<TypeParameterSyntax>(
                           new SyntaxNodeOrToken[] { 
                               TypeParameter(
                                   List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                                   Token(None), 
                                   Identifier("T")) }), 
                       Token(GreaterThanToken)), 
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
                                                                       syntaxForParam_value, 
                                                                       null, 
                                                                       null, 
                                                                       Token(CloseBraceToken)) }), 
                                                           Token(InterpolatedStringEndToken))) }), 
                                           Token(CloseParenToken))), 
                                   Token(SemicolonToken)) }), 
                       Token(CloseBraceToken)), 
                   null, 
                   Token(None));
    }
}
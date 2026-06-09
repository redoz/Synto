//HintName: Factory.TestClass.g.cs
#nullable enable
using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;

partial class Factory
{
    public static SyntaxList<Microsoft.CodeAnalysis.CSharp.Syntax.MemberDeclarationSyntax> TestClass()
    {
        return List<MemberDeclarationSyntax>(
                   new MemberDeclarationSyntax[] { 
                       MethodDeclaration(
                           List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                           TokenList(), 
                           PredefinedType(Token(VoidKeyword)), 
                           null, 
                           Identifier("First"), 
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
                                                                       "\"first\"", 
                                                                       "first"))) }), 
                                                   Token(CloseParenToken))), 
                                           Token(SemicolonToken)) }), 
                               Token(CloseBraceToken)), 
                           null, 
                           Token(None)), 
                       MethodDeclaration(
                           List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                           TokenList(), 
                           PredefinedType(Token(VoidKeyword)), 
                           null, 
                           Identifier("Second"), 
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
                                                                       "\"second\"", 
                                                                       "second"))) }), 
                                                   Token(CloseParenToken))), 
                                           Token(SemicolonToken)) }), 
                               Token(CloseBraceToken)), 
                           null, 
                           Token(None)) });
    }
}
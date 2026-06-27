//HintName: Factory.Build.g.cs
#nullable enable
using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;

partial class Factory
{
    public static MethodDeclarationSyntax Build(ExpressionSyntax T)
    {
        return MethodDeclaration(
                   List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                   TokenList(), 
                   PredefinedType(Token(VoidKeyword)), 
                   null, 
                   Identifier("Build"), 
                   null, 
                   ParameterList(
                       Token(OpenParenToken), 
                       SeparatedList<ParameterSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Parameter(
                                   List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                                   TokenList(), 
                                   T, 
                                   Identifier("instance"), 
                                   null) }), 
                       Token(CloseParenToken)), 
                   List<TypeParameterConstraintClauseSyntax>(Array.Empty<TypeParameterConstraintClauseSyntax>()), 
                   Block(
                       List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                       Token(OpenBraceToken), 
                       List<StatementSyntax>(
                           new StatementSyntax[] { 
                               LocalDeclarationStatement(
                                   List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                                   Token(None), 
                                   Token(None), 
                                   TokenList(), 
                                   VariableDeclaration(
                                       IdentifierName("var"), 
                                       SeparatedList<VariableDeclaratorSyntax>(
                                           new SyntaxNodeOrToken[] { 
                                               VariableDeclarator(
                                                   Identifier("x"), 
                                                   null, 
                                                   EqualsValueClause(
                                                       Token(EqualsToken), 
                                                       global::Synto.Templating.SyntoBuilders.Member(
                                                           IdentifierName("instance"), 
                                                           "Name"))) })), 
                                   Token(SemicolonToken)), 
                               ExpressionStatement(
                                   List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                                   InvocationExpression(
                                       MemberAccessExpression(
                                           SimpleMemberAccessExpression, 
                                           MemberAccessExpression(
                                               SimpleMemberAccessExpression, 
                                               IdentifierName("System"), 
                                               Token(DotToken), 
                                               IdentifierName("Console")), 
                                           Token(DotToken), 
                                           IdentifierName("WriteLine")), 
                                       ArgumentList(
                                           Token(OpenParenToken), 
                                           SeparatedList<ArgumentSyntax>(
                                               new SyntaxNodeOrToken[] { 
                                                   Argument(
                                                       null, 
                                                       Token(None), 
                                                       IdentifierName("x")) }), 
                                           Token(CloseParenToken))), 
                                   Token(SemicolonToken)) }), 
                       Token(CloseBraceToken)), 
                   null, 
                   Token(None));
    }
}
//HintName: Factory.LocalFunction.g.cs
using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;
using Synto;

partial class Factory
{
    public static MethodDeclarationSyntax LocalFunction<T>()
    {
        TypeSyntax syntaxForTypeParam_T = ParseTypeName(typeof(T).FullName!);
        return MethodDeclaration(
                   List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                   TokenList(), 
                   PredefinedType(Token(VoidKeyword)), 
                   null, 
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
                               LocalDeclarationStatement(
                                   List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                                   Token(None), 
                                   Token(None), 
                                   TokenList(), 
                                   VariableDeclaration(
                                       GenericName(
                                           Identifier("List"), 
                                           TypeArgumentList(
                                               Token(LessThanToken), 
                                               SeparatedList<TypeSyntax>(new SyntaxNodeOrToken[] { syntaxForTypeParam_T }), 
                                               Token(GreaterThanToken))), 
                                       SeparatedList<VariableDeclaratorSyntax>(
                                           new SyntaxNodeOrToken[] { 
                                               VariableDeclarator(
                                                   Identifier("list"), 
                                                   null, 
                                                   EqualsValueClause(
                                                       Token(EqualsToken), 
                                                       ImplicitObjectCreationExpression(
                                                           Token(NewKeyword), 
                                                           ArgumentList(
                                                               Token(OpenParenToken), 
                                                               SeparatedList<ArgumentSyntax>(Array.Empty<SyntaxNodeOrToken>()), 
                                                               Token(CloseParenToken)), 
                                                           null))) })), 
                                   Token(SemicolonToken)) }), 
                       Token(CloseBraceToken)), 
                   null, 
                   Token(None));
    }
}
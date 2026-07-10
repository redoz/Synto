#nullable enable
using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;
using System.Collections.Generic;
using System.Linq;

partial class Factory
{
    public static ClassDeclarationSyntax Reader(TypeSyntax TRet)
    {
        return ClassDeclaration(
                   List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                   TokenList(Token(PublicKeyword)), 
                   Token(ClassKeyword), 
                   Identifier("Reader"), 
                   null, 
                   null, 
                   null, 
                   List<TypeParameterConstraintClauseSyntax>(Array.Empty<TypeParameterConstraintClauseSyntax>()), 
                   Token(OpenBraceToken), 
                   List<MemberDeclarationSyntax>(
                       new MemberDeclarationSyntax[] { 
                           FieldDeclaration(
                               List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                               TokenList(
                                   Token(PrivateKeyword), 
                                   Token(ReadOnlyKeyword)), 
                               VariableDeclaration(
                                   QualifiedName(
                                       QualifiedName(
                                           QualifiedName(
                                               AliasQualifiedName(
                                                   IdentifierName("global"), 
                                                   Token(ColonColonToken), 
                                                   IdentifierName("System")), 
                                               Token(DotToken), 
                                               IdentifierName("Collections")), 
                                           Token(DotToken), 
                                           IdentifierName("Generic")), 
                                       GenericName(
                                           Identifier("IEnumerator"), 
                                           TypeArgumentList(
                                               Token(LessThanToken), 
                                               SeparatedList<TypeSyntax>(new SyntaxNodeOrToken[] { TRet }), 
                                               Token(GreaterThanToken)))), 
                                   SeparatedList<VariableDeclaratorSyntax>(
                                       new SyntaxNodeOrToken[] { 
                                           VariableDeclarator(
                                               Identifier("_e"), 
                                               null, 
                                               null) })), 
                               Token(SemicolonToken)), 
                           MethodDeclaration(
                               List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                               TokenList(Token(PublicKeyword)), 
                               TRet, 
                               null, 
                               Identifier("GetTypedExpr"), 
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
                                                               Identifier("built"), 
                                                               null, 
                                                               EqualsValueClause(
                                                                   Token(EqualsToken), 
                                                                   global::Synto.Templating.SyntoBuilders.Switch(
                                                                       IdentifierName("i"), 
                                                                       global::Synto.Templating.SyntoBuilders.Arm(
                                                                           LiteralExpression(
                                                                               NumericLiteralExpression, 
                                                                               Literal(0)), 
                                                                           global::Synto.Templating.SyntoBuilders.Member(
                                                                               MemberAccessExpression(
                                                                                   SimpleMemberAccessExpression, 
                                                                                   IdentifierName("_e"), 
                                                                                   Token(DotToken), 
                                                                                   IdentifierName("Current")), 
                                                                               "A")), 
                                                                       global::Synto.Templating.SyntoBuilders.Arm(
                                                                           LiteralExpression(
                                                                               NumericLiteralExpression, 
                                                                               Literal(1)), 
                                                                           global::Synto.Templating.SyntoBuilders.Member(
                                                                               MemberAccessExpression(
                                                                                   SimpleMemberAccessExpression, 
                                                                                   IdentifierName("_e"), 
                                                                                   Token(DotToken), 
                                                                                   IdentifierName("Current")), 
                                                                               "B"))))) })), 
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
                                                                   IdentifierName("built")) }), 
                                                       Token(CloseParenToken))), 
                                               Token(SemicolonToken)), 
                                           ThrowStatement(
                                               List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                                               Token(ThrowKeyword), 
                                               ObjectCreationExpression(
                                                   Token(NewKeyword), 
                                                   QualifiedName(
                                                       AliasQualifiedName(
                                                           IdentifierName("global"), 
                                                           Token(ColonColonToken), 
                                                           IdentifierName("System")), 
                                                       IdentifierName("Exception")), 
                                                   ArgumentList(
                                                       Token(OpenParenToken), 
                                                       SeparatedList<ArgumentSyntax>(Array.Empty<SyntaxNodeOrToken>()), 
                                                       Token(CloseParenToken)), 
                                                   null), 
                                               Token(SemicolonToken)) }), 
                                   Token(CloseBraceToken)), 
                               null, 
                               Token(None)) }), 
                   Token(CloseBraceToken), 
                   Token(None));
    }
}
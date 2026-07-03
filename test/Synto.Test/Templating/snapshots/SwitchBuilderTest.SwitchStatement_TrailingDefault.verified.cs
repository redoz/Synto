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
                                   GenericName(
                                       Identifier("IEnumerator"), 
                                       TypeArgumentList(
                                           Token(LessThanToken), 
                                           SeparatedList<TypeSyntax>(new SyntaxNodeOrToken[] { TRet }), 
                                           Token(GreaterThanToken))), 
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
                               Identifier("GetTypedStmt"), 
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
                                           global::Synto.Templating.SyntoBuilders.Switch(
                                               IdentifierName("i"), 
                                               global::Synto.Templating.SyntoBuilders.Case(
                                                   LiteralExpression(
                                                       NumericLiteralExpression, 
                                                       Literal(0)), 
                                                   global::Synto.Templating.SyntoBuilders.Return(
                                                       global::Synto.Templating.SyntoBuilders.Member(
                                                           MemberAccessExpression(
                                                               SimpleMemberAccessExpression, 
                                                               IdentifierName("_e"), 
                                                               Token(DotToken), 
                                                               IdentifierName("Current")), 
                                                           "A"))), 
                                               global::Synto.Templating.SyntoBuilders.Default(
                                                   global::Synto.Templating.SyntoBuilders.Return(
                                                       global::Synto.Templating.SyntoBuilders.Member(
                                                           MemberAccessExpression(
                                                               SimpleMemberAccessExpression, 
                                                               IdentifierName("_e"), 
                                                               Token(DotToken), 
                                                               IdentifierName("Current")), 
                                                           "B")))), 
                                           ThrowStatement(
                                               List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                                               Token(ThrowKeyword), 
                                               ObjectCreationExpression(
                                                   Token(NewKeyword), 
                                                   QualifiedName(
                                                       IdentifierName("System"), 
                                                       Token(DotToken), 
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
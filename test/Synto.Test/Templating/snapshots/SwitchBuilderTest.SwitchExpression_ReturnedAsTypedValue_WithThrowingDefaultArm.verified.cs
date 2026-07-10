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
    public static ClassDeclarationSyntax Reader(TypeSyntax TRet, global::System.Collections.Generic.IReadOnlyList<global::Col> cols)
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
                                           ReturnStatement(
                                               List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                                               Token(ReturnKeyword), 
                                               global::Synto.Templating.SyntoBuilders.Switch(
                                                   IdentifierName("i"), 
                                                   cols.Where(c => c.Ordinal >= 0).Select(
                                                       c => global::Synto.Templating.SyntoBuilders.Arm(
                                                           LiteralExpression(
                                                               NumericLiteralExpression, 
                                                               Literal(c.Ordinal)), 
                                                           global::Synto.Templating.SyntoBuilders.Member(
                                                               MemberAccessExpression(
                                                                   SimpleMemberAccessExpression, 
                                                                   IdentifierName("_e"), 
                                                                   Token(DotToken), 
                                                                   IdentifierName("Current")), 
                                                               c.Name))).Append(
                                                       global::Synto.Templating.SyntoBuilders.DefaultArm(
                                                           global::Synto.Templating.SyntoBuilders.Throw(
                                                               ObjectCreationExpression(
                                                                   Token(NewKeyword), 
                                                                   QualifiedName(
                                                                       AliasQualifiedName(
                                                                           IdentifierName("global"), 
                                                                           Token(ColonColonToken), 
                                                                           IdentifierName("System")), 
                                                                       IdentifierName("InvalidCastException")), 
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
                                                                                           "\"no\"", 
                                                                                           "no"))) }), 
                                                                       Token(CloseParenToken)), 
                                                                   null))))), 
                                               Token(SemicolonToken)) }), 
                                   Token(CloseBraceToken)), 
                               null, 
                               Token(None)) }), 
                   Token(CloseBraceToken), 
                   Token(None));
    }
}
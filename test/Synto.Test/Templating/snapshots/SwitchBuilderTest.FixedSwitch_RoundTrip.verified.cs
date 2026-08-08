#nullable enable
using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;

partial class Factory
{
    public static ClassDeclarationSyntax Reader()
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
                           MethodDeclaration(
                               List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                               TokenList(Token(PublicKeyword)), 
                               PredefinedType(Token(IntKeyword)), 
                               null, 
                               Identifier("GetStmt"), 
                               null, 
                               ParameterList(
                                   Token(OpenParenToken), 
                                   SeparatedList<ParameterSyntax>(
                                       new SyntaxNodeOrToken[] { 
                                           Parameter(
                                               List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                                               TokenList(), 
                                               PredefinedType(Token(IntKeyword)), 
                                               Identifier("x"), 
                                               null) }), 
                                   Token(CloseParenToken)), 
                               List<TypeParameterConstraintClauseSyntax>(Array.Empty<TypeParameterConstraintClauseSyntax>()), 
                               Block(
                                   List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                                   Token(OpenBraceToken), 
                                   List<StatementSyntax>(
                                       new StatementSyntax[] { 
                                           SwitchStatement(
                                               List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                                               Token(SwitchKeyword), 
                                               Token(OpenParenToken), 
                                               IdentifierName("x"), 
                                               Token(CloseParenToken), 
                                               Token(OpenBraceToken), 
                                               List<SwitchSectionSyntax>(
                                                   new SwitchSectionSyntax[] { 
                                                       SwitchSection(
                                                           List<SwitchLabelSyntax>(
                                                               new SwitchLabelSyntax[] { 
                                                                   CaseSwitchLabel(
                                                                       Token(CaseKeyword), 
                                                                       LiteralExpression(
                                                                           NumericLiteralExpression, 
                                                                           Literal(
                                                                               "1", 
                                                                               1)), 
                                                                       Token(ColonToken)) }), 
                                                           List<StatementSyntax>(
                                                               new StatementSyntax[] { 
                                                                   ReturnStatement(
                                                                       List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                                                                       Token(ReturnKeyword), 
                                                                       LiteralExpression(
                                                                           NumericLiteralExpression, 
                                                                           Literal(
                                                                               "10", 
                                                                               10)), 
                                                                       Token(SemicolonToken)) })), 
                                                       SwitchSection(
                                                           List<SwitchLabelSyntax>(
                                                               new SwitchLabelSyntax[] { 
                                                                   DefaultSwitchLabel(
                                                                       Token(DefaultKeyword), 
                                                                       Token(ColonToken)) }), 
                                                           List<StatementSyntax>(
                                                               new StatementSyntax[] { 
                                                                   ReturnStatement(
                                                                       List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                                                                       Token(ReturnKeyword), 
                                                                       LiteralExpression(
                                                                           NumericLiteralExpression, 
                                                                           Literal(
                                                                               "0", 
                                                                               0)), 
                                                                       Token(SemicolonToken)) })) }), 
                                               Token(CloseBraceToken)) }), 
                                   Token(CloseBraceToken)), 
                               null, 
                               Token(None)), 
                           MethodDeclaration(
                               List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                               TokenList(Token(PublicKeyword)), 
                               PredefinedType(Token(IntKeyword)), 
                               null, 
                               Identifier("GetExpr"), 
                               null, 
                               ParameterList(
                                   Token(OpenParenToken), 
                                   SeparatedList<ParameterSyntax>(
                                       new SyntaxNodeOrToken[] { 
                                           Parameter(
                                               List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                                               TokenList(), 
                                               PredefinedType(Token(IntKeyword)), 
                                               Identifier("x"), 
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
                                               SwitchExpression(
                                                   IdentifierName("x"), 
                                                   Token(SwitchKeyword), 
                                                   Token(OpenBraceToken), 
                                                   SeparatedList<SwitchExpressionArmSyntax>(
                                                       new SyntaxNodeOrToken[] { 
                                                           SwitchExpressionArm(
                                                               ConstantPattern(
                                                                   LiteralExpression(
                                                                       NumericLiteralExpression, 
                                                                       Literal(
                                                                           "1", 
                                                                           1))), 
                                                               null, 
                                                               Token(EqualsGreaterThanToken), 
                                                               LiteralExpression(
                                                                   NumericLiteralExpression, 
                                                                   Literal(
                                                                       "10", 
                                                                       10))), 
                                                           Token(CommaToken), 
                                                           SwitchExpressionArm(
                                                               DiscardPattern(Token(UnderscoreToken)), 
                                                               null, 
                                                               Token(EqualsGreaterThanToken), 
                                                               LiteralExpression(
                                                                   NumericLiteralExpression, 
                                                                   Literal(
                                                                       "0", 
                                                                       0))), 
                                                           Token(CommaToken) }), 
                                                   Token(CloseBraceToken)), 
                                               Token(SemicolonToken)) }), 
                                   Token(CloseBraceToken)), 
                               null, 
                               Token(None)) }), 
                   Token(CloseBraceToken), 
                   Token(None));
    }
}
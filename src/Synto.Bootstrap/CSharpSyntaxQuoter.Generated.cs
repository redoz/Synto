#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;
using Microsoft.CodeAnalysis.CSharp;

namespace Synto.Bootstrap;

// this was copied from the output of the CSharpSyntaxQuoterGenerator 
internal partial class CSharpSyntaxQuoter
{
    public override ExpressionSyntax? VisitQualifiedName(QualifiedNameSyntax node)
    {
        // QualifiedName(Visit(node.Left)!, Visit(node.DotToken)!, Visit(node.Right)!)
        return InvocationExpression(
                   IdentifierName(nameof(QualifiedName)),
                   ArgumentList(
                       Token(OpenParenToken),
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[]{
                               Argument(
                                   null,
                                   Token(None),
                                   Visit(node.Left)!),
                               Token(CommaToken),
                               Argument(
                                   null,
                                   Token(None),
                                   Visit(node.DotToken)!),
                               Token(CommaToken),
                               Argument(
                                   null,
                                   Token(None),
                                   Visit(node.Right)!)}),
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
    {
        // PostfixUnaryExpression(Visit(node.Kind())!, Visit(node.Operand)!, Visit(node.OperatorToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(PostfixUnaryExpression)),
                   ArgumentList(
                       Token(OpenParenToken),
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[]{
                               Argument(
                                   null,
                                   Token(None),
                                   Visit(node.Kind())!),
                               Token(CommaToken),
                               Argument(
                                   null,
                                   Token(None),
                                   Visit(node.Operand)!),
                               Token(CommaToken),
                               Argument(
                                   null,
                                   Token(None),
                                   Visit(node.OperatorToken)!)}),
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        // MemberAccessExpression(Visit(node.Kind())!, Visit(node.Expression)!, Visit(node.OperatorToken)!, Visit(node.Name)!)
        return InvocationExpression(
                   IdentifierName(nameof(MemberAccessExpression)),
                   ArgumentList(
                       Token(OpenParenToken),
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[]{
                               Argument(
                                   null,
                                   Token(None),
                                   Visit(node.Kind())!),
                               Token(CommaToken),
                               Argument(
                                   null,
                                   Token(None),
                                   Visit(node.Expression)!),
                               Token(CommaToken),
                               Argument(
                                   null,
                                   Token(None),
                                   Visit(node.OperatorToken)!),
                               Token(CommaToken),
                               Argument(
                                   null,
                                   Token(None),
                                   Visit(node.Name)!)}),
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        // BinaryExpression(Visit(node.Kind())!, Visit(node.Left)!, Visit(node.OperatorToken)!, Visit(node.Right)!)
        return InvocationExpression(
                   IdentifierName(nameof(BinaryExpression)),
                   ArgumentList(
                       Token(OpenParenToken),
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[]{
                               Argument(
                                   null,
                                   Token(None),
                                   Visit(node.Kind())!),
                               Token(CommaToken),
                               Argument(
                                   null,
                                   Token(None),
                                   Visit(node.Left)!),
                               Token(CommaToken),
                               Argument(
                                   null,
                                   Token(None),
                                   Visit(node.OperatorToken)!),
                               Token(CommaToken),
                               Argument(
                                   null,
                                   Token(None),
                                   Visit(node.Right)!)}),
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitLiteralExpression(LiteralExpressionSyntax node)
    {
        // LiteralExpression(Visit(node.Kind())!, Visit(node.Token)!)
        return InvocationExpression(
                   IdentifierName(nameof(LiteralExpression)),
                   ArgumentList(
                       Token(OpenParenToken),
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[]{
                               Argument(
                                   null,
                                   Token(None),
                                   Visit(node.Kind())!),
                               Token(CommaToken),
                               Argument(
                                   null,
                                   Token(None),
                                   Visit(node.Token)!)}),
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        // InvocationExpression(Visit(node.Expression)!, Visit(node.ArgumentList)!)
        return InvocationExpression(
                   IdentifierName(nameof(InvocationExpression)),
                   ArgumentList(
                       Token(OpenParenToken),
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[]{
                               Argument(
                                   null,
                                   Token(None),
                                   Visit(node.Expression)!),
                               Token(CommaToken),
                               Argument(
                                   null,
                                   Token(None),
                                   Visit(node.ArgumentList)!)}),
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitArgumentList(ArgumentListSyntax node)
    {
        // ArgumentList(Visit(node.OpenParenToken)!, Visit(node.Arguments)!, Visit(node.CloseParenToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(ArgumentList)),
                   ArgumentList(
                       Token(OpenParenToken),
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[]{
                               Argument(
                                   null,
                                   Token(None),
                                   Visit(node.OpenParenToken)!),
                               Token(CommaToken),
                               Argument(
                                   null,
                                   Token(None),
                                   Visit(node.Arguments)!),
                               Token(CommaToken),
                               Argument(
                                   null,
                                   Token(None),
                                   Visit(node.CloseParenToken)!)}),
                       Token(CloseParenToken)));
    }

 
    public override ExpressionSyntax? VisitArgument(ArgumentSyntax node)
    {
        // Argument(Visit(node.NameColon).OrNullLiteralExpression(), Visit(node.RefKindKeyword)!, Visit(node.Expression)!)
        return InvocationExpression(
                   IdentifierName(nameof(Argument)),
                   ArgumentList(
                       Token(OpenParenToken),
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[]{
                               Argument(
                                   null,
                                   Token(None),
                                   Visit(node.NameColon).OrNullLiteralExpression()),
                               Token(CommaToken),
                               Argument(
                                   null,
                                   Token(None),
                                   Visit(node.RefKindKeyword)!),
                               Token(CommaToken),
                               Argument(
                                   null,
                                   Token(None),
                                   Visit(node.Expression)!)}),
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitBlock(BlockSyntax node)
    {
        // Block(Visit(node.AttributeLists)!, Visit(node.OpenBraceToken)!, Visit(node.Statements)!, Visit(node.CloseBraceToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(Block)),
                   ArgumentList(
                       Token(OpenParenToken),
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[]{
                               Argument(
                                   null,
                                   Token(None),
                                   Visit(node.AttributeLists)!),
                               Token(CommaToken),
                               Argument(
                                   null,
                                   Token(None),
                                   Visit(node.OpenBraceToken)!),
                               Token(CommaToken),
                               Argument(
                                   null,
                                   Token(None),
                                   Visit(node.Statements)!),
                               Token(CommaToken),
                               Argument(
                                   null,
                                   Token(None),
                                   Visit(node.CloseBraceToken)!)}),
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitExpressionStatement(ExpressionStatementSyntax node)
    {
        // ExpressionStatement(Visit(node.AttributeLists)!, Visit(node.Expression)!, Visit(node.SemicolonToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(ExpressionStatement)),
                   ArgumentList(
                       Token(OpenParenToken),
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[]{
                               Argument(
                                   null,
                                   Token(None),
                                   Visit(node.AttributeLists)!),
                               Token(CommaToken),
                               Argument(
                                   null,
                                   Token(None),
                                   Visit(node.Expression)!),
                               Token(CommaToken),
                               Argument(
                                   null,
                                   Token(None),
                                   Visit(node.SemicolonToken)!)}),
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitAccessorDeclaration(AccessorDeclarationSyntax node)
    {
        // AccessorDeclaration(Visit(node.Kind())!, Visit(node.AttributeLists)!, Visit(node.Modifiers)!, Visit(node.Keyword)!, Visit(node.Body).OrNullLiteralExpression(), Visit(node.ExpressionBody).OrNullLiteralExpression(), Visit(node.SemicolonToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(AccessorDeclaration)),
                   ArgumentList(
                       Token(OpenParenToken),
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[]{
                               Argument(
                                   null,
                                   Token(None),
                                   Visit(node.Kind())!),
                               Token(CommaToken),
                               Argument(
                                   null,
                                   Token(None),
                                   Visit(node.AttributeLists)!),
                               Token(CommaToken),
                               Argument(
                                   null,
                                   Token(None),
                                   Visit(node.Modifiers)!),
                               Token(CommaToken),
                               Argument(
                                   null,
                                   Token(None),
                                   Visit(node.Keyword)!),
                               Token(CommaToken),
                               Argument(
                                   null,
                                   Token(None),
                                   Visit(node.Body).OrNullLiteralExpression()),
                               Token(CommaToken),
                               Argument(
                                   null,
                                   Token(None),
                                   Visit(node.ExpressionBody).OrNullLiteralExpression()),
                               Token(CommaToken),
                               Argument(
                                   null,
                                   Token(None),
                                   Visit(node.SemicolonToken)!)}),
                       Token(CloseParenToken)));
    }

}
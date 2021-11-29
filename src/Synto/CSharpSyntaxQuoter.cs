using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SF = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Synto.Helpers;

namespace Synto;

internal class CSharpSyntaxQuoter : CSharpSyntaxVisitor<ExpressionSyntax>
{


    //private static readonly NameSyntax _syntaxFactoryToken = SF.ParseName(typeof(SF).FullName);

    //internal static InvocationExpressionSyntax SyntaxFactoryInvocation(string functionName, params ExpressionSyntax[] arguments)
    //{
    //    return SF.InvocationExpression(SF.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
    //                                                             _syntaxFactoryToken,
    //                                                             SF.IdentifierName(functionName)))
    //             .AddArgumentListArguments(Array.ConvertAll(arguments, SF.Argument));
    //}

    //internal static InvocationExpressionSyntax SyntaxFactoryInvocation(string functionName, IEnumerable<ExpressionSyntax> arguments)
    //{
    //    return SyntaxFactoryInvocation(functionName, arguments.ToArray());
    //}
    public CSharpSyntaxQuoter()
    {

    }

    public override ExpressionSyntax? VisitTypeArgumentList(Microsoft.CodeAnalysis.CSharp.Syntax.TypeArgumentListSyntax node)
    {
        // Microsoft.CodeAnalysis.CSharp.SyntaxFactory.InvocationExpression(Microsoft.CodeAnalysis.CSharp.SyntaxFactory.MemberAccessExpression(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SimpleMemberAccessExpression, SyntaxFactoryType, Microsoft.CodeAnalysis.CSharp.SyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.DotToken), Microsoft.CodeAnalysis.CSharp.SyntaxFactory.IdentifierName("TypeArgumentList")), Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ArgumentList(Microsoft.CodeAnalysis.CSharp.SyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.OpenParenToken), Microsoft.CodeAnalysis.CSharp.SyntaxFactory.SeparatedList(new[]{Microsoft.CodeAnalysis.CSharp.SyntaxFactory.Argument(null, Microsoft.CodeAnalysis.CSharp.SyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.None), Visit(node.LessThanToken)!), Microsoft.CodeAnalysis.CSharp.SyntaxFactory.Argument(null, Microsoft.CodeAnalysis.CSharp.SyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.None), Visit(node.Arguments)!), Microsoft.CodeAnalysis.CSharp.SyntaxFactory.Argument(null, Microsoft.CodeAnalysis.CSharp.SyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.None), Visit(node.GreaterThanToken)!)}, new[]{Microsoft.CodeAnalysis.CSharp.SyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.CommaToken), Microsoft.CodeAnalysis.CSharp.SyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.CommaToken)}), Microsoft.CodeAnalysis.CSharp.SyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.CloseParenToken)))
        return SF.InvocationExpression(SF.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, SyntaxFactoryType, SF.Token(SyntaxKind.DotToken), SF.IdentifierName("TypeArgumentList")), SF.ArgumentList(SF.Token(SyntaxKind.OpenParenToken), SF.SeparatedList(new[] { SF.Argument(null, SF.Token(SyntaxKind.None), Visit(node.LessThanToken)!), SF.Argument(null, SF.Token(SyntaxKind.None), Visit(node.Arguments)!), SF.Argument(null, SF.Token(SyntaxKind.None), Visit(node.GreaterThanToken)!) }, new[] { SF.Token(SyntaxKind.CommaToken), SF.Token(SyntaxKind.CommaToken) }), SF.Token(SyntaxKind.CloseParenToken)));
    }


    public override ExpressionSyntax? VisitBlock(BlockSyntax node)
    {
        return SyntaxFactoryInvocation(nameof(SF.Block), node.Statements.Accept(this));
    }

    public override ExpressionSyntax? VisitExpressionStatement(ExpressionStatementSyntax node)
    {
        return SyntaxFactoryInvocation(nameof(SF.ExpressionStatement), node.Expression.Accept(this)!);
    }

    public override ExpressionSyntax? VisitAccessorDeclaration(AccessorDeclarationSyntax node)
    {
        return SyntaxFactoryInvocation(nameof(SF.AccessorDeclaration),
                                            node.Kind().QuoteSyntaxKind(),
                                            node.AttributeLists.Accept(this),
                                            node.Modifiers.QuoteSyntaxTokenList(),
                                            (node.Body?.Accept(this)).OrQuotedNullLiteral(),
                                            (node.ExpressionBody?.Accept(this)).OrQuotedNullLiteral());
    }

    public override ExpressionSyntax? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        //SF.InvocationExpression()
        List<ExpressionSyntax> arguments = new()
        {
            node.Expression.Accept(this)!
        };

        if (node.ArgumentList != null && node.ArgumentList.Arguments.Count > 0)
        {
            arguments.Add(node.ArgumentList.Accept(this)!);
        }

        return SyntaxFactoryInvocation(nameof(SF.InvocationExpression),
                                            arguments.ToArray());

    }

    public override ExpressionSyntax? VisitArgumentList(ArgumentListSyntax node)
    {

        return SyntaxFactoryInvocation(nameof(SF.ArgumentList),
            node.OpenParenToken.QuoteSyntaxToken(),
            node.Arguments.Accept(this),
            node.CloseParenToken.QuoteSyntaxToken());
    }

    public override ExpressionSyntax? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        return SyntaxFactoryInvocation(nameof(SF.MemberAccessExpression),
            node.Kind().QuoteSyntaxKind(),
            node.Expression.Accept(this)!,
            node.OperatorToken.QuoteSyntaxToken(),
            node.Name.Accept(this)!);
    }

    public override ExpressionSyntax? VisitIdentifierName(IdentifierNameSyntax node)
    {
        return SyntaxFactoryInvocation(nameof(SF.IdentifierName),
                                        SF.LiteralExpression(SyntaxKind.StringLiteralExpression, SF.Literal(node.Identifier.ValueText)));
    }

    public override ExpressionSyntax? VisitArgument(ArgumentSyntax node)
    {
        //SF.Argument(node.NameColon, node.RefKindKeyword, node.Expression)
        return SyntaxFactoryInvocation(nameof(SF.Argument),
            (node.NameColon?.Accept(this)).OrQuotedNullLiteral(),
            node.RefKindKeyword.QuoteSyntaxToken(), node.Expression.Accept(this)!);
    }

    public override ExpressionSyntax? VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        //var x = () => SF.BinaryExpression(node.Kind(), node.Left, node.OperatorToken, node.Right);
        return SyntaxFactoryInvocation(nameof(SF.BinaryExpression),
            node.Kind().QuoteSyntaxKind(),
            node.Left.Accept(this)!,
            node.OperatorToken.QuoteSyntaxToken(),
            node.Right.Accept(this)!);
    }

    public override ExpressionSyntax? VisitLiteralExpression(LiteralExpressionSyntax node)
    {
        //var x = SF.LiteralExpression(node.Kind(), node.Token);
        return SyntaxFactoryInvocation(nameof(SF.LiteralExpression),
            node.Kind().QuoteSyntaxKind(),
            SyntaxFactoryInvocation(nameof(SF.Literal), SF.LiteralExpression(node.Kind(), node.Token)));
    }

    public override ExpressionSyntax? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
    {
        //SF.ObjectCreationExpression(node.NewKeyword, node.Type, node.ArgumentList, node.Initializer)
        return SyntaxFactoryInvocation(nameof(SF.ObjectCreationExpression),
            node.NewKeyword.QuoteSyntaxToken(),
            node.Type.Accept(this)!,
            (node.ArgumentList?.Accept(this)).OrQuotedNullLiteral(),
            (node.Initializer?.Accept(this)).OrQuotedNullLiteral());
    }

    public override ExpressionSyntax? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
    {
        SyntaxFactory.LocalFunctionStatement(node.AttributeLists,
                                             node.Modifiers, 
                                             node.ReturnType, 
                                             node.Identifier, 
                                             node.TypeParameterList, 
                                             node.ParameterList, 
                                             node.ConstraintClauses, 
                                             node.Body, 
                                             node.ExpressionBody, 
                                             node.SemicolonToken);

    }

    public override ExpressionSyntax? DefaultVisit(SyntaxNode node)
    {
        throw new NotImplementedException($"Node type not implemented yet: {node.GetType().FullName}");
    }

    internal static ExpressionSyntax Quote(SyntaxNode node)
    {
        return new CSharpSyntaxQuoter().Visit(node)!;
    }
}


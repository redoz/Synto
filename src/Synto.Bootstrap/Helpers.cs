using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SF = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto.Bootstrap;

internal static class Helpers
{
    private static readonly NameSyntax SyntaxFactoryToken = SF.ParseName(typeof(SyntaxFactory).FullName);

    public static InvocationExpressionSyntax SyntaxFactoryInvocation(string functionName, params ExpressionSyntax[] arguments)
    {
        return SF.InvocationExpression(SF.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactoryToken,
                SF.IdentifierName(functionName)))
            .AddArgumentListArguments(Array.ConvertAll(arguments, SF.Argument));
    }

    public static InvocationExpressionSyntax SyntaxFactoryInvocation(string functionName, IEnumerable<ExpressionSyntax> arguments)
    {
        return SyntaxFactoryInvocation(functionName, arguments.ToArray());
    }

    //public static IEnumerable<ExpressionSyntax?> Accept<TNode>(this SyntaxList<TNode> nodeList,
    //                                                           CSharpSyntaxVisitor<ExpressionSyntax> visitor) where TNode : SyntaxNode
    //{
    //    return nodeList.Select(visitor.Visit);
    //}

    public static ExpressionSyntax Accept<TNode>(this SyntaxList<TNode> nodeList, CSharpSyntaxVisitor<ExpressionSyntax> visitor) where TNode : SyntaxNode
    {
        var quotedExprs = nodeList.Select(node => visitor.Visit(node)!);

        TypeSyntax elementType = SF.ParseTypeName(typeof(TNode).FullName);
        return SF.InvocationExpression(
            SF.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactoryToken,
                SF.GenericName(
                    SF.Identifier(nameof(SF.List)),
                    SF.TypeArgumentList(
                        SF.SingletonSeparatedList(elementType)))),
            SF.ArgumentList(SF.SingletonSeparatedList(SF.Argument(quotedExprs.ToArrayLiteral(elementType)))));
    }

    public static ExpressionSyntax Accept<TNode>(this SeparatedSyntaxList<TNode> nodeList, CSharpSyntaxVisitor<ExpressionSyntax> visitor) where TNode : SyntaxNode
    {
        return SyntaxFactoryInvocation(nameof(SF.SeparatedList),
            nodeList.Select(t => visitor.Visit(t)!).ToArrayLiteral(SF.ParseTypeName(typeof(TNode).FullName)),
            nodeList.GetSeparators().Select(st => st.QuoteSyntaxToken()).ToArrayLiteral(SF.ParseTypeName(typeof(SyntaxToken).FullName)));
    }
    public static ExpressionSyntax ToArrayLiteral(this IEnumerable<ExpressionSyntax> nodeList, TypeSyntax elementType)
    {
        var list = nodeList.ToList();
        if (list.Count == 0)
        {
            return SF.InvocationExpression(
                SF.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                    SF.ParseTypeName(typeof(Array).FullName),
                    SF.GenericName(SF.Identifier(nameof(Array.Empty)), SF.TypeArgumentList(SF.SingletonSeparatedList(elementType)))));
        }
        return SF.ImplicitArrayCreationExpression(SF.InitializerExpression(SyntaxKind.ArrayInitializerExpression,
            SF.SeparatedList(list)));
    }

    public static ExpressionSyntax QuoteSyntaxKind(this SyntaxKind kind)
    {
        return SF.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
            SF.ParseName(typeof(SyntaxKind).FullName),
            SF.IdentifierName(kind.ToString()));
    }

    public static ExpressionSyntax QuoteSyntaxToken(this SyntaxToken token)
    {
        return SyntaxFactoryInvocation(nameof(SF.Token), token.Kind().QuoteSyntaxKind());
    }

    public static ExpressionSyntax QuoteSyntaxTokenList(this SyntaxTokenList tokenList)
    {
        return SyntaxFactoryInvocation(nameof(SF.TokenList), tokenList.Select(token => token.QuoteSyntaxToken()));
    }

    public static ExpressionSyntax OrQuotedNullLiteral(this ExpressionSyntax? expr)
    {
        return expr ?? SF.LiteralExpression(SyntaxKind.NullLiteralExpression);
    }
 

}
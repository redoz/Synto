using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;

using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;


namespace Synto;

// this needs to be a base-class because for some reason the generates source cannot see the contents of it's partial class
public class CSharpSyntaxQuoterBase : CSharpSyntaxVisitor<ExpressionSyntax>
{
    protected static readonly NameSyntax SyntaxFactoryToken = ParseName(typeof(SyntaxFactory).FullName!);

    protected static InvocationExpressionSyntax SyntaxFactoryInvocation(string functionName, params ExpressionSyntax[] arguments)
    {
        return InvocationExpression(
            MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactoryToken,
                IdentifierName(functionName)))
            .AddArgumentListArguments(Array.ConvertAll(arguments, Argument));
    }


    protected static ExpressionSyntax ToArrayLiteral(IEnumerable<ExpressionSyntax> nodeList, TypeSyntax elementType)
    {
        var list = nodeList.ToList();
        if (list.Count == 0)
        {
            return InvocationExpression(
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    ParseTypeName(typeof(Array).FullName!),
                    GenericName(
                        Identifier(nameof(Array.Empty)),
                        TypeArgumentList(SingletonSeparatedList(elementType)))));
        }
        return ImplicitArrayCreationExpression(
            InitializerExpression(
                SyntaxKind.ArrayInitializerExpression,
                SeparatedList(list)));
    }


    public virtual ExpressionSyntax Visit<TNode>(SyntaxList<TNode> nodeList) where TNode : SyntaxNode
    {
        var quotedExprs = nodeList.Select(node => Visit(node)!);

        TypeSyntax elementType = ParseTypeName(typeof(TNode).FullName!);
        return InvocationExpression(
            MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactoryToken,
                GenericName(
                    Identifier(nameof(List)),
                    TypeArgumentList(
                        SingletonSeparatedList(elementType)))),
            ArgumentList(SingletonSeparatedList(Argument(ToArrayLiteral(quotedExprs, elementType)))));
    }

    public virtual ExpressionSyntax Visit<TNode>(SeparatedSyntaxList<TNode> nodeList) where TNode : SyntaxNode
    {
        return SyntaxFactoryInvocation(nameof(SeparatedList),
            ToArrayLiteral(nodeList.Select(t => Visit(t)!), ParseTypeName(typeof(TNode).FullName!)),
            ToArrayLiteral(nodeList.GetSeparators().Select(Visit), ParseTypeName(typeof(SyntaxToken).FullName!)));
    }

    public virtual ExpressionSyntax Visit(SyntaxKind kind)
    {
        return MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            ParseName(typeof(SyntaxKind).FullName!),
            IdentifierName(kind.ToString()));
    }

    public virtual ExpressionSyntax Visit(SyntaxToken token)
    {
        return SyntaxFactoryInvocation(nameof(Token), Visit(token.Kind()));
    }

    public virtual ExpressionSyntax Visit(SyntaxTokenList tokenList)
    {
        return SyntaxFactoryInvocation(nameof(TokenList), tokenList.Select(Visit).ToArray());
    }
}

public partial class CSharpSyntaxQuoter :  CSharpSyntaxQuoterBase
{
}

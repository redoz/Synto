using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SF = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Synto.Bootstrap.Helpers;

namespace Synto.Bootstrap;

internal class CSharpSyntaxQuoter : CSharpSyntaxVisitor<ExpressionSyntax>
{
    private readonly List<ExpressionSyntax> _exclude;

    public CSharpSyntaxQuoter() : this(Enumerable.Empty<ExpressionSyntax>())
    {
    }
    public CSharpSyntaxQuoter(IEnumerable<ExpressionSyntax> exclude)
    {
        _exclude = new List<ExpressionSyntax>(exclude);
    }

    public static IEnumerable<UsingDirectiveSyntax> RequiredUsings()
    {
        return new List<UsingDirectiveSyntax>()
        {
            SF.UsingDirective(SF.ParseName(typeof(SyntaxNodeOrToken).Namespace)),
            SF.UsingDirective(SF.ParseName(typeof(ArgumentSyntax).Namespace)),
            SF.UsingDirective(SF.ParseName(typeof(SF).FullName))
                .WithStaticKeyword(SF.Token(SyntaxKind.StaticKeyword)),
            SF.UsingDirective(SF.ParseName(typeof(SyntaxKind).FullName))
                .WithStaticKeyword(SF.Token(SyntaxKind.StaticKeyword))
        };
    }

    public override ExpressionSyntax? Visit(SyntaxNode? node)
    {
        if (node is ExpressionSyntax expr && _exclude.Find(other => other.IsEquivalentTo(expr)) is not null)
            return expr;

        return base.Visit(node);
    }

    public override ExpressionSyntax? VisitBlock(BlockSyntax node)
    {
        return SyntaxFactoryInvocation(nameof(SF.Block), node.Statements.Accept(this));
    }

    public override ExpressionSyntax? VisitExpressionStatement(ExpressionStatementSyntax node)
    {
        return SyntaxFactoryInvocation(nameof(SF.ExpressionStatement), Visit(node.Expression)!);
    }

    public override ExpressionSyntax? VisitAccessorDeclaration(AccessorDeclarationSyntax node)
    {
        return SyntaxFactoryInvocation(nameof(SF.AccessorDeclaration),
            node.Kind().QuoteSyntaxKind(),
            node.AttributeLists.Accept(this),
            node.Modifiers.QuoteSyntaxTokenList(),
            Visit(node.Body).OrQuotedNullLiteral(),
            Visit(node.ExpressionBody).OrQuotedNullLiteral());
    }

    public override ExpressionSyntax? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        //SF.InvocationExpression()
        List<ExpressionSyntax> arguments = new()
        {
            Visit(node.Expression)!
        };

        if (node.ArgumentList is not null && node.ArgumentList.Arguments.Count > 0)
        {
            arguments.Add(Visit(node.ArgumentList)!);
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
            Visit(node.Expression)!,
            node.OperatorToken.QuoteSyntaxToken(),
            Visit(node.Name)!);
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
            (Visit(node.NameColon)).OrQuotedNullLiteral(),
            node.RefKindKeyword.QuoteSyntaxToken(),
            Visit(node.Expression)!);
    }

    public override ExpressionSyntax? VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        //SF.BinaryExpression(node.Kind(), node.Left, node.OperatorToken, node.Right);
        return SyntaxFactoryInvocation(nameof(SF.BinaryExpression),
            node.Kind().QuoteSyntaxKind(),
            Visit(node.Left)!,
            node.OperatorToken.QuoteSyntaxToken(),
            Visit(node.Right)!);
    }

    public override ExpressionSyntax? VisitLiteralExpression(LiteralExpressionSyntax node)
    {
        //SF.LiteralExpression(node.Kind(), node.Token);
        return SyntaxFactoryInvocation(nameof(SF.LiteralExpression),
            node.Kind().QuoteSyntaxKind(),
            SyntaxFactoryInvocation(nameof(SF.Literal), SF.LiteralExpression(node.Kind(), node.Token)));
    }

    public override ExpressionSyntax? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
    {
        //SF.ObjectCreationExpression(node.NewKeyword, node.Type, node.ArgumentList, node.Initializer)
        return SyntaxFactoryInvocation(nameof(SF.ObjectCreationExpression),
            node.NewKeyword.QuoteSyntaxToken(),
            Visit(node.Type)!,
            Visit(node.ArgumentList).OrQuotedNullLiteral(),
            Visit(node.Initializer).OrQuotedNullLiteral());
    }

    public override ExpressionSyntax? VisitQualifiedName(QualifiedNameSyntax node)
    {
        //SF.QualifiedName(left, dotToken, right)
        return SyntaxFactoryInvocation(nameof(SF.QualifiedName),
            Visit(node.Left)!,
            node.DotToken.QuoteSyntaxToken(),
            Visit(node.Right)!);
    }

    public override ExpressionSyntax? VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
    {
        return SyntaxFactoryInvocation(nameof(SF.PostfixUnaryExpression),
            node.Kind().QuoteSyntaxKind(),
            Visit(node.Operand)!,
            node.OperatorToken.QuoteSyntaxToken());
    }

    public override ExpressionSyntax? DefaultVisit(SyntaxNode node)
    {
        throw new NotImplementedException($"Node type not implemented yet: {node.GetType().FullName}");
    }

    internal static ExpressionSyntax Quote(SyntaxNode node)
    {
        return new CSharpSyntaxQuoter().Visit(node)!;
    }

    internal static ExpressionSyntax Quote(SyntaxNode node, IEnumerable<ExpressionSyntax> exclude)
    {
        return new CSharpSyntaxQuoter(exclude).Visit(node)!;
    }

    internal static ExpressionSyntax Quote(SyntaxNode node, params ExpressionSyntax[] exclude)
    {
        return new CSharpSyntaxQuoter(exclude).Visit(node)!;
    }
}


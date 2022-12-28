using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;
using static Synto.Bootstrap.Helpers;


namespace Synto.Bootstrap;

internal partial class CSharpSyntaxQuoter : CSharpSyntaxVisitor<ExpressionSyntax>
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
            //UsingDirective(ParseName("Synto.CodeAnalysis")),
            UsingDirective(ParseName("Microsoft.CodeAnalysis")),
            UsingDirective(ParseName("Microsoft.CodeAnalysis.CSharp.Syntax")),
            UsingDirective(ParseName("Microsoft.CodeAnalysis.CSharp.SyntaxFactory"))
                .WithStaticKeyword(Token(StaticKeyword)),
            UsingDirective(ParseName("Microsoft.CodeAnalysis.CSharp.SyntaxKind"))
                .WithStaticKeyword(Token(StaticKeyword))
        };
    }

    public virtual ExpressionSyntax Visit<TNode>(SyntaxList<TNode> nodeList) where TNode : SyntaxNode
    {
        // when attributes are filtered from the syntax tree we will end up in situations where we have nulls in this nodeList
        // originally we rewrote the syntax tree to exclude the Attribute before passing down the mutated syntax tree, but that 
        // invalidates our semantic model. This seems to fix the problem even if it's not the nicest solution.
        IEnumerable<ExpressionSyntax> quotedExprs = nodeList.Select(Visit).Where(node => node is not null)!;

        TypeSyntax elementType = ParseTypeName(typeof(TNode).Name);
        return InvocationExpression(
                GenericName(
                    Identifier(nameof(List)),
                    TypeArgumentList(
                        SingletonSeparatedList(elementType))),
            ArgumentList(SingletonSeparatedList(Argument(ToArrayLiteral(quotedExprs, elementType)))));
    }

    public virtual ExpressionSyntax Visit<TNode>(SeparatedSyntaxList<TNode> nodeList) where TNode : SyntaxNode
    {
        // when attributes are filtered from the syntax tree we will end up in situations where we have nulls in this nodeList
        // originally we rewrote the syntax tree to exclude the Attribute before passing down the mutated syntax tree, but that 
        // invalidates our semantic model. This seems to fix the problem even if it's not the nicest solution.
        var quotedExprs = nodeList.GetWithSeparators()
            .Select(item => item.IsToken ? Visit(item.AsToken()) : Visit(item.AsNode()))
            .ToList();

        for (int i = 0; i < quotedExprs.Count;)
        {
            if (quotedExprs[i] is null)
            {
                quotedExprs.RemoveAt(i); // remove null-node
                if (i < quotedExprs.Count)
                    quotedExprs.RemoveAt(i); // remove token too
            }
            else
                i++;
        }

        TypeSyntax elementType = ParseTypeName(typeof(TNode).Name);
        return InvocationExpression(
                GenericName(
                    Identifier(nameof(SeparatedList)),
                    TypeArgumentList(
                        SingletonSeparatedList(elementType))),
                ArgumentList(SingletonSeparatedList(Argument(ToArrayLiteral(quotedExprs!, IdentifierName(nameof(SyntaxNodeOrToken)))))));
    }

    protected static ExpressionSyntax ToArrayLiteral(IEnumerable<ExpressionSyntax> nodeList, TypeSyntax elementType)
    {
        var list = nodeList.ToList();
        if (list.Count == 0)
        {
            return InvocationExpression(
                MemberAccessExpression(
                    SimpleMemberAccessExpression,
                    IdentifierName(nameof(Array)),
                    GenericName(
                        Identifier(nameof(Array.Empty)),
                        TypeArgumentList(SingletonSeparatedList(elementType)))));
        }

        return ArrayCreationExpression(
            ArrayType(elementType,
                SingletonList(ArrayRankSpecifier(SingletonSeparatedList<ExpressionSyntax>(OmittedArraySizeExpression())))),
            InitializerExpression(
                ArrayInitializerExpression,
                SeparatedList(list)));
    }

    public virtual ExpressionSyntax Visit(SyntaxKind kind)
    {
        return IdentifierName(kind.ToString());
    }

    public virtual ExpressionSyntax Visit(SyntaxToken token)
    {
        static bool TokenKindHasText(SyntaxKind kind) => SyntaxFacts.GetText(kind) != string.Empty;

        return token.Kind() switch
        {
            SyntaxKind.BadToken => SyntaxFactoryInvocation(nameof(BadToken), token.Text.ToSyntax()),
            IdentifierToken => SyntaxFactoryInvocation(nameof(Identifier), token.Text.ToSyntax()),
            NumericLiteralToken => token.Value switch
            {
                byte value => SyntaxFactoryInvocation(nameof(Literal), token.Text.ToSyntax(), value.ToSyntax()),
                sbyte value => SyntaxFactoryInvocation(nameof(Literal), token.Text.ToSyntax(), value.ToSyntax()),
                short value => SyntaxFactoryInvocation(nameof(Literal), token.Text.ToSyntax(), value.ToSyntax()),
                ushort value => SyntaxFactoryInvocation(nameof(Literal), token.Text.ToSyntax(), value.ToSyntax()),
                int value => SyntaxFactoryInvocation(nameof(Literal), token.Text.ToSyntax(), value.ToSyntax()),
                uint value => SyntaxFactoryInvocation(nameof(Literal), token.Text.ToSyntax(), value.ToSyntax()),
                long value => SyntaxFactoryInvocation(nameof(Literal), token.Text.ToSyntax(), value.ToSyntax()),
                ulong value => SyntaxFactoryInvocation(nameof(Literal), token.Text.ToSyntax(), value.ToSyntax()),
                decimal value => SyntaxFactoryInvocation(nameof(Literal), token.Text.ToSyntax(), value.ToSyntax()),
                double value => SyntaxFactoryInvocation(nameof(Literal), token.Text.ToSyntax(), value.ToSyntax()),
                float value => SyntaxFactoryInvocation(nameof(Literal), token.Text.ToSyntax(), value.ToSyntax()),
                _ => throw new NotImplementedException($"Unable to create literal token of type {token.Value?.GetType().FullName ?? "null"}")
            },
            CharacterLiteralToken => SyntaxFactoryInvocation(nameof(Literal), token.Text.ToSyntax(), ((char) token.Value!).ToSyntax()),
            StringLiteralToken => SyntaxFactoryInvocation(nameof(Literal), token.Text.ToSyntax(), ((string) token.Value!).ToSyntax()),

            None => SyntaxFactoryInvocation(nameof(Token), Visit(None)),

            var tokenKind when TokenKindHasText(tokenKind) => SyntaxFactoryInvocation(nameof(Token), Visit(tokenKind)),
            var tokenKind => SyntaxFactoryInvocation(nameof(Token), Visit(tokenKind), token.Text.ToSyntax(), ((string) token.Value!).ToSyntax()),
        };

    }

    public virtual ExpressionSyntax Visit(SyntaxTriviaList triviaList)
    {
        return SyntaxFactoryInvocation(nameof(TriviaList));
    }

    public virtual ExpressionSyntax Visit(SyntaxTokenList tokenList)
    {
        return SyntaxFactoryInvocation(nameof(TokenList), tokenList.Select(Visit).ToArray());
    }

    public override ExpressionSyntax? Visit(SyntaxNode? node)
    {
        if (node is ExpressionSyntax expr && _exclude.Find(other => other.IsEquivalentTo(expr)) is not null)
            return expr;

        return base.Visit(node);
    }


    public override ExpressionSyntax? VisitIdentifierName(IdentifierNameSyntax node)
    {
        return SyntaxFactoryInvocation(nameof(IdentifierName),
                                        LiteralExpression(StringLiteralExpression, Literal(node.Identifier.ValueText)));
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


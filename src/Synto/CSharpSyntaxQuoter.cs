using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto;

// this needs to be a base-class because for some reason the generated source cannot see the contents of its partial class
public abstract class CSharpSyntaxQuoterBase : CSharpSyntaxVisitor<ExpressionSyntax>
{
    private readonly bool _includeTrivia;

    protected CSharpSyntaxQuoterBase(bool includeTrivia)
    {
        this._includeTrivia = includeTrivia;
    }

    protected static InvocationExpressionSyntax SyntaxFactoryInvocation(string functionName, params ExpressionSyntax[] arguments)
    {
        return InvocationExpression(IdentifierName(functionName))
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
                    IdentifierName(nameof(Array)),
                    GenericName(
                        Identifier(nameof(Array.Empty)),
                        TypeArgumentList(SingletonSeparatedList(elementType)))));
        }

        return ArrayCreationExpression(
            ArrayType(elementType,
                SingletonList(ArrayRankSpecifier(SingletonSeparatedList<ExpressionSyntax>(OmittedArraySizeExpression())))),
            InitializerExpression(
                SyntaxKind.ArrayInitializerExpression,
                SeparatedList(list)));
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



    public virtual ExpressionSyntax Visit(SyntaxKind kind)
    {
        return IdentifierName(kind.ToString());
    }

    public virtual ExpressionSyntax Visit(SyntaxToken token)
    {
        static bool TokenKindHasText(SyntaxKind kind) => SyntaxFacts.GetText(kind) != string.Empty;

        // this is not very nice looking
        if (this._includeTrivia && (token.HasLeadingTrivia || token.HasTrailingTrivia)) {
            return token.Kind() switch
            {
                SyntaxKind.BadToken => SyntaxFactoryInvocation(nameof(BadToken), Visit(token.LeadingTrivia), token.Text.ToSyntax(), Visit(token.TrailingTrivia)),
                SyntaxKind.IdentifierToken => SyntaxFactoryInvocation(nameof(Identifier), Visit(token.LeadingTrivia), token.Text.ToSyntax(), Visit(token.TrailingTrivia)),
                SyntaxKind.NumericLiteralToken => token.Value switch
                {
                    byte value => SyntaxFactoryInvocation(nameof(Literal), Visit(token.LeadingTrivia), token.Text.ToSyntax(), value.ToSyntax(), Visit(token.TrailingTrivia)),
                    sbyte value => SyntaxFactoryInvocation(nameof(Literal), Visit(token.LeadingTrivia), token.Text.ToSyntax(), value.ToSyntax(), Visit(token.TrailingTrivia)),
                    short value => SyntaxFactoryInvocation(nameof(Literal), Visit(token.LeadingTrivia), token.Text.ToSyntax(), value.ToSyntax(), Visit(token.TrailingTrivia)),
                    ushort value => SyntaxFactoryInvocation(nameof(Literal), Visit(token.LeadingTrivia), token.Text.ToSyntax(), value.ToSyntax(), Visit(token.TrailingTrivia)),
                    int value => SyntaxFactoryInvocation(nameof(Literal), Visit(token.LeadingTrivia), token.Text.ToSyntax(), value.ToSyntax(), Visit(token.TrailingTrivia)),
                    uint value => SyntaxFactoryInvocation(nameof(Literal), Visit(token.LeadingTrivia), token.Text.ToSyntax(), value.ToSyntax(), Visit(token.TrailingTrivia)),
                    long value => SyntaxFactoryInvocation(nameof(Literal), Visit(token.LeadingTrivia), token.Text.ToSyntax(), value.ToSyntax(), Visit(token.TrailingTrivia)),
                    ulong value => SyntaxFactoryInvocation(nameof(Literal), Visit(token.LeadingTrivia), token.Text.ToSyntax(), value.ToSyntax(), Visit(token.TrailingTrivia)),
                    decimal value => SyntaxFactoryInvocation(nameof(Literal), Visit(token.LeadingTrivia), token.Text.ToSyntax(), value.ToSyntax(), Visit(token.TrailingTrivia)),
                    double value => SyntaxFactoryInvocation(nameof(Literal), Visit(token.LeadingTrivia), token.Text.ToSyntax(), value.ToSyntax(), Visit(token.TrailingTrivia)),
                    float value => SyntaxFactoryInvocation(nameof(Literal), Visit(token.LeadingTrivia), token.Text.ToSyntax(), value.ToSyntax(), Visit(token.TrailingTrivia)),
                    _ => throw new NotImplementedException($"Unable to create literal token of type {token.Value?.GetType().FullName ?? "null"}")
                },
                SyntaxKind.CharacterLiteralToken => SyntaxFactoryInvocation(nameof(Literal), Visit(token.LeadingTrivia), token.Text.ToSyntax(), ((char)token.Value!).ToSyntax(), Visit(token.TrailingTrivia)),
                SyntaxKind.StringLiteralToken => SyntaxFactoryInvocation(nameof(Literal), Visit(token.LeadingTrivia), token.Text.ToSyntax(), ((string)token.Value!).ToSyntax(), Visit(token.TrailingTrivia)),

                SyntaxKind.None => SyntaxFactoryInvocation(nameof(Token), Visit(token.LeadingTrivia), Visit(SyntaxKind.None), Visit(token.TrailingTrivia)),

                var tokenKind when TokenKindHasText(tokenKind) => SyntaxFactoryInvocation(nameof(Token), Visit(token.LeadingTrivia), Visit(tokenKind), Visit(token.TrailingTrivia)),
                var tokenKind => SyntaxFactoryInvocation(nameof(Token), Visit(token.LeadingTrivia), Visit(tokenKind), token.Text.ToSyntax(), ((string)token.Value!).ToSyntax(), Visit(token.TrailingTrivia)),
            };
        }
        else
        {
            return token.Kind() switch
            {
                SyntaxKind.BadToken => SyntaxFactoryInvocation(nameof(BadToken), token.Text.ToSyntax()),
                SyntaxKind.IdentifierToken => SyntaxFactoryInvocation(nameof(Identifier), token.Text.ToSyntax()),
                SyntaxKind.NumericLiteralToken => token.Value switch
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
                SyntaxKind.CharacterLiteralToken => SyntaxFactoryInvocation(nameof(Literal), token.Text.ToSyntax(), ((char)token.Value!).ToSyntax()),
                SyntaxKind.StringLiteralToken => SyntaxFactoryInvocation(nameof(Literal), token.Text.ToSyntax(), ((string)token.Value!).ToSyntax()),

                SyntaxKind.None => SyntaxFactoryInvocation(nameof(Token), Visit(SyntaxKind.None)),

                var tokenKind when TokenKindHasText(tokenKind) => SyntaxFactoryInvocation(nameof(Token), Visit(tokenKind)),
                var tokenKind => SyntaxFactoryInvocation(nameof(Token), Visit(tokenKind), token.Text.ToSyntax(), ((string)token.Value!).ToSyntax()),
            };

        }
    }

    public virtual ExpressionSyntax Visit(SyntaxTriviaList triviaList)
    {
        return SyntaxFactoryInvocation(nameof(TriviaList));
    }

    public virtual ExpressionSyntax Visit(SyntaxTokenList tokenList)
    {
        return SyntaxFactoryInvocation(nameof(TokenList), tokenList.Select(Visit).ToArray());
    }

    public override ExpressionSyntax? DefaultVisit(SyntaxNode node)
    {
        throw new NotImplementedException($"Unable to visit node (Kind: {node.Kind()}, Type: {node.GetType().FullName}: '{node}'");
    }


}

public partial class CSharpSyntaxQuoter :  CSharpSyntaxQuoterBase
{
    public CSharpSyntaxQuoter(bool includeTrivia) : base(includeTrivia)
    {
    }


    public static IEnumerable<UsingDirectiveSyntax> RequiredUsings()
    {
        return new List<UsingDirectiveSyntax>()
        {
            // System
            UsingDirective(ParseName("System")),
            UsingDirective(ParseName("Microsoft.CodeAnalysis")),
            UsingDirective(ParseName("Microsoft.CodeAnalysis.CSharp.Syntax")),
            // static SyntaxFactory
            UsingDirective(ParseName("Microsoft.CodeAnalysis.CSharp.SyntaxFactory"))
                .WithStaticKeyword(Token(SyntaxKind.StaticKeyword)),
            // static SyntaxKind
            UsingDirective(ParseName("Microsoft.CodeAnalysis.SyntaxKind"))
                .WithStaticKeyword(Token(SyntaxKind.StaticKeyword))
        };
    }

    // by specifying this here we prevent the CSharpSyntaxQuoter Generator from generating this method
    // there's a reason for it, and if we remove this we could probably rediscover what it is
    public override ExpressionSyntax? VisitIdentifierName(IdentifierNameSyntax node)
    {
        // IdentifierName(node.Identifier.Text)
        return InvocationExpression(IdentifierName(nameof(IdentifierName)),
            ArgumentList(
                SingletonSeparatedList(
                    Argument(
                        LiteralExpression(
                            SyntaxKind.StringLiteralExpression,
                            Literal(
                                node.Identifier.ValueText))))));
    }
}

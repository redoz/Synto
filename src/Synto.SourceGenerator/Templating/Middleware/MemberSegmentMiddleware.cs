using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto;

/// <summary>
/// List seam: when a member list contains one or more <c>[Splice]</c> member-generator children (keys in
/// <paramref name="memberSegments"/>), emit a <c>CollectionSyntaxExtensions.BuildList&lt;TNode&gt;(…)</c> run —
/// each fixed member quoted in place via <see cref="QuoteContext.Quote"/>, each generator contributing its
/// precomputed segment at its declaration position (so declaration order is preserved). Any other
/// <see cref="SyntaxList{TNode}"/> falls through to base list quoting unchanged. Reproduces the member-segment
/// branch of the dissolved <c>TemplateSyntaxQuoter.Visit&lt;TNode&gt;(SyntaxList&lt;TNode&gt;)</c>.
/// </summary>
internal sealed class MemberSegmentMiddleware : IListQuoteMiddleware<QuoteContext>
{
    private readonly IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> _memberSegments;

    public MemberSegmentMiddleware(IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> memberSegments)
        => _memberSegments = memberSegments;

    public ExpressionSyntax Quote<TNode>(SyntaxList<TNode> list, QuoteContext ctx, ListQuoteNext<TNode> next)
        where TNode : SyntaxNode
    {
        bool hasSegment = false;
        foreach (var node in list)
        {
            if (_memberSegments.ContainsKey(node))
            {
                hasSegment = true;
                break;
            }
        }

        if (!hasSegment)
            return next(list);

        TypeSyntax elementType = ParseTypeName(typeof(TNode).Name);

        var arguments = new List<ArgumentSyntax>();
        foreach (var node in list)
        {
            if (_memberSegments.TryGetValue(node, out var segment))
            {
                arguments.Add(Argument(segment));
            }
            else if (ctx.Quote(node) is { } quoted)
            {
                arguments.Add(Argument(quoted));
            }
        }

        return InvocationExpression(
            MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                IdentifierName(nameof(CollectionSyntaxExtensions)),
                GenericName(Identifier(nameof(CollectionSyntaxExtensions.BuildList)))
                    .WithTypeArgumentList(TypeArgumentList(SingletonSeparatedList(elementType)))))
            .WithArgumentList(ArgumentList(SeparatedList(arguments)));
    }
}

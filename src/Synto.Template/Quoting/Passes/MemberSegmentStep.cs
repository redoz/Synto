using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto.Generators.Templating.Quoting.Passes;

/// <summary>
/// Owner-keyed List-pipeline step; dispatched by <see cref="PerNodePipelineQuoter"/>. When a member list
/// contains one or more <c>[Splice]</c> member-generator children (keys in <paramref name="memberSegments"/>),
/// emit a <c>CollectionSyntaxExtensions.BuildList&lt;TNode&gt;(…)</c> run — each fixed member quoted in place via
/// <see cref="QuoteContext.Quote"/>, each generator contributing its precomputed segment at its declaration
/// position (so declaration order is preserved). Dispatched only for member lists whose owner declares at
/// least one <c>[Splice]</c> segment — the central (owner, element type) key in <see cref="PerNodePipelineQuoter"/>
/// guarantees it, so no per-step self-check is needed (see the note on <see cref="ElementType"/>). Per-owner
/// counterpart of the dissolved MemberSegmentMiddleware.
/// </summary>
internal sealed class MemberSegmentStep : IListQuoteMiddleware
{
    private readonly IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> _memberSegments;

    public MemberSegmentStep(IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> memberSegments)
        => _memberSegments = memberSegments;

    // The quoter only dispatches this step for member lists (central element-type guard). A member list is
    // keyed to its owner only when that owner declares at least one [Splice] segment member, so this step no
    // longer scans the list to decide whether to defer — the (owner, element type) key already guarantees it.
    public Type ElementType => typeof(MemberDeclarationSyntax);

    public ExpressionSyntax Quote<TNode>(SyntaxList<TNode> list, QuoteContext ctx, ListQuoteNext<TNode> next)
        where TNode : SyntaxNode
    {
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

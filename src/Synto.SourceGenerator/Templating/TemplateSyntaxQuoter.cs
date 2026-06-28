using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;


namespace Synto;

internal sealed class TemplateSyntaxQuoter : CSharpSyntaxQuoter
{
    private readonly SemanticModel _semanticModel;
    private readonly IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> _unquotedReplacements;
    private readonly HashSet<SyntaxNode> _trimNodes;
    private readonly IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> _memberSegments;


    public TemplateSyntaxQuoter(
        SemanticModel semanticModel,
        IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> unquotedReplacements,
        HashSet<SyntaxNode> trimNodes,
        bool includeTrivia,
        IReadOnlyDictionary<SyntaxNode, ExpressionSyntax>? memberSegments = null) : base(includeTrivia)
    {
        _semanticModel = semanticModel;
        _unquotedReplacements = unquotedReplacements;
        _trimNodes = trimNodes;
        _memberSegments = memberSegments ?? new Dictionary<SyntaxNode, ExpressionSyntax>();
    }

    public override ExpressionSyntax? Visit(SyntaxNode? node)
    {
        if (node is not null && _unquotedReplacements.TryGetValue(node, out var replacement))
            return replacement;

        if (node is not null && _trimNodes.Contains(node))
            return null;

        return base.Visit(node);
    }

    /// <summary>
    /// A member list that contains one or more <c>[Splice]</c> member generators is emitted as a
    /// <c>CollectionSyntaxExtensions.BuildList&lt;TNode&gt;(…)</c> run (spec §4, member axis): each fixed member is
    /// a single-node segment quoted in place, and each generator contributes its precomputed segment
    /// (<c>ListSegment&lt;TNode&gt;.Run(…)</c> for an enumerable shape, or the generator call directly for a single
    /// member) at its DECLARATION position among the siblings — so declaration order is preserved. Any other
    /// SyntaxList (no generator present) falls through to the base list quoting unchanged.
    /// </summary>
    public override ExpressionSyntax Visit<TNode>(SyntaxList<TNode> nodeList)
    {
        bool hasSegment = false;
        foreach (var node in nodeList)
        {
            if (_memberSegments.ContainsKey(node))
            {
                hasSegment = true;
                break;
            }
        }

        if (!hasSegment)
            return base.Visit(nodeList);

        TypeSyntax elementType = ParseTypeName(typeof(TNode).Name);

        var arguments = new List<ArgumentSyntax>();
        foreach (var node in nodeList)
        {
            if (_memberSegments.TryGetValue(node, out var segment))
            {
                arguments.Add(Argument(segment));
            }
            else if (Visit(node) is { } quoted)
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

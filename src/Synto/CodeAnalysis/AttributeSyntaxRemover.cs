using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto.CodeAnalysis;

public class AttributeSyntaxRemover : CSharpSyntaxRewriter
{
    private readonly AttributeSyntax _attributeSyntax;

    private AttributeSyntaxRemover(AttributeSyntax attributeSyntax) : base(visitIntoStructuredTrivia: false)
    {
        this._attributeSyntax = attributeSyntax;
    }

    public static TSyntax Remove<TSyntax>(TSyntax target, AttributeSyntax attribute) where TSyntax : SyntaxNode
    {
        return (TSyntax)new AttributeSyntaxRemover(attribute).Visit(target);
    }

    public override SyntaxNode? VisitAttributeList(AttributeListSyntax node)
    {
        if (node.Contains(this._attributeSyntax))
        {
            var attributes= node.Attributes.Remove(this._attributeSyntax);
            if (attributes.Count == 0)
                return null;

            return node.WithAttributes(attributes);
        }
        return base.VisitAttributeList(node);
    }
}
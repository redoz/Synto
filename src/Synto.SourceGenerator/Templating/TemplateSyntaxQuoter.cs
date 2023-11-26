using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;


namespace Synto.Templating;

internal class TemplateSyntaxQuoter : CSharpSyntaxQuoter
{
    public new static IEnumerable<UsingDirectiveSyntax> RequiredUsings()
    {
        return CSharpSyntaxQuoter.RequiredUsings()
            .Union(new[]
            {
                UsingDirective(IdentifierName("Synto"))
            });
    }

    private readonly SemanticModel _semanticModel;
    private readonly IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> _unquotedReplacements;
    private readonly HashSet<SyntaxNode> _trimNodes;


    public TemplateSyntaxQuoter(
        SemanticModel semanticModel,
        IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> unquotedReplacements,
        HashSet<SyntaxNode> trimNodes,
        bool includeTrivia) : base(includeTrivia)
    {
        _semanticModel = semanticModel;
        _unquotedReplacements = unquotedReplacements;
        _trimNodes = trimNodes;
    }

    public override ExpressionSyntax? Visit(SyntaxNode? node)
    {
        if (node is not null && _unquotedReplacements.TryGetValue(node, out var replacement))
            return replacement;

        if (node is not null && _trimNodes.Contains(node))
            return null;

        return base.Visit(node);
    }
}


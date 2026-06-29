using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto;

/// <summary>
/// Node seam (pre, drop): if the visited node is in <paramref name="trimNodes"/>, drop it (return
/// <see langword="null"/>) before quoting; otherwise defer. Reproduces the trim branch of the dissolved
/// <c>TemplateSyntaxQuoter.Visit(SyntaxNode?)</c>.
/// </summary>
internal sealed class TrimMiddleware : INodeQuoteMiddleware<QuoteContext>
{
    private readonly HashSet<SyntaxNode> _trimNodes;

    public TrimMiddleware(HashSet<SyntaxNode> trimNodes) => _trimNodes = trimNodes;

    public ExpressionSyntax? Quote(SyntaxNode? node, QuoteContext ctx, NodeQuoteNext next)
    {
        if (node is not null && _trimNodes.Contains(node))
            return null;

        return next(node);
    }
}

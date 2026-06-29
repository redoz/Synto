using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto;

/// <summary>
/// Node seam (pre, short-circuit): if the visited node is a key in <paramref name="replacements"/>, return the
/// mapped replacement expression WITHOUT quoting the node (does not call <c>next</c>); otherwise defer.
/// Reproduces the replacement branch of the dissolved <c>TemplateSyntaxQuoter.Visit(SyntaxNode?)</c>.
/// </summary>
internal sealed class ReplacementMiddleware : INodeQuoteMiddleware<QuoteContext>
{
    private readonly IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> _replacements;

    public ReplacementMiddleware(IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> replacements)
        => _replacements = replacements;

    public ExpressionSyntax? Quote(SyntaxNode? node, QuoteContext ctx, NodeQuoteNext next)
    {
        if (node is not null && _replacements.TryGetValue(node, out var replacement))
            return replacement;

        return next(node);
    }
}

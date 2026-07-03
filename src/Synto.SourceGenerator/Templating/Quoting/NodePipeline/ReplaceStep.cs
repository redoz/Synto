using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto.Generators.Templating;

/// <summary>
/// Terminal Node-pipeline step: emit the pre-computed replacement expression INSTEAD of quoting the node
/// (never calls <c>next</c>, so the node's subtree is not descended). Per-node counterpart of the dissolved
/// ReplacementMiddleware.
/// </summary>
internal sealed class ReplaceStep : INodeQuoteMiddleware
{
    private readonly ExpressionSyntax _replacement;

    public ReplaceStep(ExpressionSyntax replacement) => _replacement = replacement;

    public ExpressionSyntax? Quote(SyntaxNode? node, QuoteContext ctx, NodeQuoteNext next) => _replacement;
}

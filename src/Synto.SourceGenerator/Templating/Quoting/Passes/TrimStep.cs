using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto.Generators.Templating.Quoting.Passes;

/// <summary>
/// Terminal Node-pipeline step: drop the node (return null; the parent list seam filters it out, and the
/// global branch-prune already folded fully-trimmed parents into this set). Stateless — one shared instance.
/// Per-node counterpart of the dissolved TrimMiddleware.
/// </summary>
internal sealed class TrimStep : INodeQuoteMiddleware
{
    public static readonly TrimStep Instance = new();

    private TrimStep() { }

    public ExpressionSyntax? Quote(SyntaxNode? node, QuoteContext ctx, NodeQuoteNext next) => null;
}

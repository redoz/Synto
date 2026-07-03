using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto;

/// <summary>
/// The cooperation surface a <see cref="IQuoteMiddleware"/> uses to re-enter the quoter: quote a
/// child node, quote a child list, or build an array literal — all routed back through the same quoter
/// instance that is running the step chain. A middleware
/// receives the context as a parameter; it never holds <see cref="SemanticModel"/> / <see cref="ISymbol"/> /
/// <see cref="Compilation"/> state, so nothing here leaks into cached incremental-generator pipeline state.
/// Public-SHAPED but <c>internal</c> for now (see the middleware-quoter-engine plan); a future GA flip to
/// <c>public</c> is a pure accessibility change.
/// </summary>
internal abstract class QuoteContext
{
    /// <summary>Re-enter the node pipeline for <paramref name="node"/> (runs the full Node middleware chain).</summary>
    public abstract ExpressionSyntax? Quote(SyntaxNode? node);

    /// <summary>Re-enter the list pipeline for <paramref name="list"/> (runs the full List middleware chain).</summary>
    public abstract ExpressionSyntax QuoteList<TNode>(SyntaxList<TNode> list) where TNode : SyntaxNode;

    /// <summary>Build the engine's array-literal expression for <paramref name="nodes"/> of <paramref name="elementType"/>.</summary>
    public abstract ExpressionSyntax ToArrayLiteral(IEnumerable<ExpressionSyntax> nodes, TypeSyntax elementType);
}

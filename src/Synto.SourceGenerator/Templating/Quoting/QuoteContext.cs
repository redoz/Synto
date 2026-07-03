using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto.Generators.Templating;

/// <summary>
/// The cooperation surface a quote step (<see cref="INodeQuoteMiddleware"/> / <see cref="IListQuoteMiddleware"/>)
/// uses to re-enter the quoter: quote a child node, or build an array literal — both routed back through the
/// same quoter instance that is running the step chain. A step receives the context as a parameter; it never
/// holds <see cref="SemanticModel"/> / <see cref="ISymbol"/> / <see cref="Compilation"/> state, so nothing here
/// leaks into cached incremental-generator pipeline state.
/// </summary>
internal abstract class QuoteContext
{
    /// <summary>Re-enter the node pipeline for <paramref name="node"/> (runs the full Node step chain).</summary>
    public abstract ExpressionSyntax? Quote(SyntaxNode? node);

    /// <summary>Build the quoter's array-literal expression for <paramref name="nodes"/> of <paramref name="elementType"/>.</summary>
    public abstract ExpressionSyntax ToArrayLiteral(IEnumerable<ExpressionSyntax> nodes, TypeSyntax elementType);
}

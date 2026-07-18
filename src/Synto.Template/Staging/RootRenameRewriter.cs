using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Synto.GenKit;

namespace Synto.Generators.Templating.Staging;

/// <summary>
/// Rewrites live-root identifier references to their factory parameter names (identity when unchanged). A thin
/// staging-flavored alias over the shared <see cref="SymbolReferenceRenamer"/> so the template path keeps its
/// descriptive name while the pure rewrite is reused by the model path.
/// </summary>
internal sealed class RootRenameRewriter : SymbolReferenceRenamer
{
    public RootRenameRewriter(SemanticModel semanticModel, IReadOnlyDictionary<ISymbol, string> rootNames)
        : base(semanticModel, rootNames)
    {
    }
}

using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto;

/// <summary>
/// Transform-local scratch carrier for the staged-region codegen: bundles the inputs that stay constant across a
/// single <see cref="StagedRegionEmitter.Emit"/> call (semantic model, classifier partition, computed live set,
/// root renamer, base lift map, string-staged roots, accumulating emission) plus the per-scaffold run identifier.
/// Replaces the 8-argument thread-through through the scaffold builders. Never equatable and never captured into
/// pipeline state — created and discarded entirely inside the generator transform.
/// </summary>
internal sealed class StagedEmitContext
{
    public StagedEmitContext(
        SemanticModel semanticModel,
        BindingTimePartition partition,
        HashSet<ISymbol> stagedSet,
        RootRenameRewriter renamer,
        IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> baseReplacements,
        IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> stringStagedRoots,
        StagedRegionEmission emission)
    {
        SemanticModel = semanticModel;
        Partition = partition;
        StagedSet = stagedSet;
        Renamer = renamer;
        BaseReplacements = baseReplacements;
        StringStagedRoots = stringStagedRoots;
        Emission = emission;
    }

    public SemanticModel SemanticModel { get; }
    public BindingTimePartition Partition { get; }
    public HashSet<ISymbol> StagedSet { get; }
    public RootRenameRewriter Renamer { get; }
    public IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> BaseReplacements { get; }
    public IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> StringStagedRoots { get; }
    public StagedRegionEmission Emission { get; }

    /// <summary>The current scaffold's run identifier (e.g. <c>__run_0</c>); reset per region by the scaffold builder.</summary>
    public SyntaxToken RunIdentifier { get; set; }
}

using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto;

/// <summary>
/// A live control region to unroll at factory-build time (plan Task 6–7 / spec §5.2–5.3): a control statement
/// (<c>foreach</c>/<c>for</c>/<c>while</c>/<c>if</c>) whose driving expression is live. The region runs verbatim
/// in the factory body — keeping live locals/accumulators as real runtime state — while each body island
/// collects the quote of its statements; the region's owning <see cref="Container"/> block is replaced by a
/// <c>BuildList</c> of that run plus the quoted fixed siblings.
/// </summary>
internal sealed class StagedRegion
{
    public StagedRegion(StatementSyntax control, BlockSyntax container, IReadOnlyList<ISymbol> loopVariables)
    {
        Control = control;
        Container = container;
        LoopVariables = loopVariables;
    }

    /// <summary>The live control statement (its driver — iteration source / condition — is a live expression).</summary>
    public StatementSyntax Control { get; }

    /// <summary>The block that directly owns <see cref="Control"/> — the replacement key (spec §5.3).</summary>
    public BlockSyntax Container { get; }

    /// <summary>
    /// The loop variables introduced by the control statement (the <c>foreach</c> variable, the <c>for</c>
    /// declarators) — live within the scaffold (they range over live values at factory time). Empty for
    /// <c>while</c>/<c>if</c>.
    /// </summary>
    public IReadOnlyList<ISymbol> LoopVariables { get; }
}

/// <summary>The product of unrolling the live regions: scaffold preamble + per-container replacements.</summary>
internal sealed class StagedRegionEmission
{
    public List<StatementSyntax> Preamble { get; } = new();
    public Dictionary<SyntaxNode, ExpressionSyntax> ContainerReplacements { get; } = new();
    public List<DiagnosticInfo> Diagnostics { get; } = new();
}

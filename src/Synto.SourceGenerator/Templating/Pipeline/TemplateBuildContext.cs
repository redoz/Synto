using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.Templating;

namespace Synto.Generators.Templating;

/// <summary>
/// Transform-local coordinator threaded through <see cref="TemplateFactoryBuilder"/>'s ordered per-feature
/// steps. It bundles the constant inputs for one factory build, the transient analysis results the discovery
/// steps share with the emission steps, and the three cohesive accumulators the steps populate:
/// <see cref="Shape"/> (the factory skeleton), <see cref="Plan"/> (the quoter patches), and <see cref="Names"/>
/// (name uniqueness). It is a plain mutable carrier — NOT equatable and never leaves the transform; nothing
/// here is captured into cached pipeline state (only the resulting source text flows out). The step order in
/// <see cref="TemplateFactoryBuilder.Build"/> is the single place the cross-step ordering contract lives.
/// </summary>
internal sealed class TemplateBuildContext
{
    public TemplateBuildContext(
        List<DiagnosticInfo> diagnostics,
        SemanticModel semanticModel,
        UsingDirectiveSet additionalUsings,
        TemplateInfo templateInfo,
        TemplateOption options)
    {
        Diagnostics = diagnostics;
        SemanticModel = semanticModel;
        AdditionalUsings = additionalUsings;
        TemplateInfo = templateInfo;
        Options = options;
    }

    // --- Constant inputs for this build ---
    public List<DiagnosticInfo> Diagnostics { get; }
    public SemanticModel SemanticModel { get; }
    public UsingDirectiveSet AdditionalUsings { get; }
    public TemplateInfo TemplateInfo { get; }
    public TemplateOption Options { get; }

    // --- The three cohesive accumulators the plan phase populates ---
    public FactoryShape Shape { get; } = new();
    public QuotePlan Plan { get; } = new();
    public NameAllocator Names { get; } = new();

    // Maps EVERY declaration-site live-root symbol to the shared factory parameter name so the live-region
    // renamer rewrites each member's local reference to the one factory parameter.
    public Dictionary<ISymbol, string> RootNames { get; } = new(SymbolEqualityComparer.Default);

    // --- Discovery results shared across later steps ---
    public List<SpliceMemberGenerator> ValidSpliceGenerators { get; } = new();
    public HashSet<SyntaxNode> SpliceGeneratorNodes { get; } = new();
    public TemplateScope Scope { get; set; } = null!;
    public IReadOnlyList<StagedParameter> StagedParameters { get; set; } = System.Array.Empty<StagedParameter>();
    public IReadOnlyList<StagedLocal> StagedLocals { get; set; } = System.Array.Empty<StagedLocal>();
    public IReadOnlyList<StagedParameterRoot> StagedRootParameters { get; set; } = System.Array.Empty<StagedParameterRoot>();
    public IReadOnlyList<QuoteCall> QuoteCalls { get; set; } = System.Array.Empty<QuoteCall>();
    public BindingTimePartition Partition { get; set; } = null!;
    public List<StagedRegion> StagedRegions { get; set; } = new();
    public HashSet<SyntaxNode> RegionConsumedNodes { get; set; } = new();

    // Nodes on a builder call's STAGED (unmarked) argument spine — the `.Where(...).Select(...)` projection (and any
    // nested Staged args) that BuilderStep emits VERBATIM as factory-build-time code. A staged-root reference here is
    // a runtime value, not an output-world reference, so it must be skipped by the depth-zero `value.ToSyntax()` lift
    // and the scalar count-fold — exactly like a live `foreach` region's consumed nodes. Nested [Quoted]/[TypeArg]
    // islands are excluded (they are re-quoted through the pipeline, so their references still lift normally).
    public HashSet<SyntaxNode> BuilderStagedConsumedNodes { get; } = new();

    public HashSet<SyntaxNode> FoldClaimedReferences { get; } = new();
    public Dictionary<StagedParameter, List<MemberAccessExpressionSyntax>> FoldsByStagedParameter { get; } = new();

    // --- Shared lift policy + counters ---
    public ValueLift ValueLift { get; set; } = null!;
    public bool ConverterError { get; set; }
    public int StagedRegionCounter;
}

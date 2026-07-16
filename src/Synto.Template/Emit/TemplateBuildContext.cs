using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.Templating;
using Synto.Generators.Templating.BindingTime;
using Synto.Generators.Templating.Discovery;
using Synto.Generators.Templating.Staging;
using Synto.Generators.Templating.Lifting;

namespace Synto.Generators.Templating.Emit;

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
        TemplateOption options,
        SyntaxKind visibility)
    {
        Diagnostics = diagnostics;
        SemanticModel = semanticModel;
        AdditionalUsings = additionalUsings;
        TemplateInfo = templateInfo;
        Options = options;
        Visibility = visibility;
    }

    // --- Constant inputs for this build ---
    public List<DiagnosticInfo> Diagnostics { get; }
    public SemanticModel SemanticModel { get; }
    public UsingDirectiveSet AdditionalUsings { get; }
    public TemplateInfo TemplateInfo { get; }
    public TemplateOption Options { get; }

    // The access modifier the emitted factory method carries (PublicKeyword for a top-level template;
    // PrivateKeyword for a parent-driven sub-template folded onto the parent's target in Phase 2).
    public SyntaxKind Visibility { get; }

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

    // Parent-driven private sub-templates built from this carrier's nested bare [Template] children (Phase 2).
    // Populated during ResolveScopeAndTrimForeignChildren by recursively building each such child (visibility
    // private, target pinned to this carrier's), then handed to the document builder alongside the parent factory
    // so they all land in the single generated document. Empty for a carrier with no nested bare children — the
    // non-nested path returns just the parent factory, byte-identical to before.
    public List<MethodDeclarationSyntax> SubTemplates { get; } = new();
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

    // SY1035 "live template code" highlights collected by tap 2 (depth-zero value-lift holes), which fires in the
    // value-lift steps that run BEFORE StagedRegionEmitter.Emit (the emission object does not yet exist). Append-only
    // mutable scratch — NOT equatable, never cached; EmitLiveRegions concatenates it with the emission's
    // InfoDiagnostics and runs the single dedupe/order/cap merge on the success path (plan Task 2, decision O1(a)).
    public List<DiagnosticInfo> LiveFragmentInfos { get; } = new();

    // --- Shared lift policy + counters ---
    public ValueLift ValueLift { get; set; } = null!;
    public bool ConverterError { get; set; }
    public int StagedRegionCounter;
}

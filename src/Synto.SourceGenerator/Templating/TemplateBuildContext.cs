using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.Templating;

namespace Synto;

/// <summary>
/// Transform-local scratch accumulator threaded through <see cref="TemplateFactoryBuilder"/>'s ordered
/// per-feature steps. It bundles the constant inputs for one factory build plus every mutable accumulator
/// the steps populate (replacements, trim set, preamble, factory parameters/type-parameters, the per-build
/// dedup name sets, the shared lazy <see cref="ValueLift"/> instance, …). It is a plain mutable carrier — it
/// is NOT equatable and never leaves the transform; nothing here is captured into cached pipeline state (only
/// the resulting source text flows out). The explicit field set is the single place the cross-step ordering
/// contract lives: each step reads what earlier steps wrote and writes what later steps read, in the fixed
/// order <see cref="TemplateFactoryBuilder.Build"/> invokes them.
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

    // --- Replacement / trim / fold channels (consumed by the final quoter) ---

    // Which file-local helper(s) the caller emits is decided by SCANNING the finished factory syntax
    // (see TemplateDocumentBuilder.FindReferencedHelpers), so the builder no longer needs usage flags.
    public Dictionary<SyntaxNode, ExpressionSyntax> UnquotedReplacements { get; } = new();
    public HashSet<SyntaxNode> TrimNodes { get; } = new();

    // Interpolation staged-fold channel (spec 2026-06-28): string-typed staged-root REFERENCE nodes mapped to
    // their factory-time raw value accessor (the factory parameter / hoisted local). Built at EMISSION,
    // adjacent to where the staged roots are consumed; only the resulting node map leaves this scope.
    public Dictionary<SyntaxNode, ExpressionSyntax> StringStagedRoots { get; } = new();

    // --- Factory shape accumulators ---
    public List<StatementSyntax> Preamble { get; } = new();

    // The member segment each valid [Splice] generator contributes to its enclosing type's member list, keyed
    // at the generator method's declaration node so the quoter splices it at that position.
    public Dictionary<SyntaxNode, ExpressionSyntax> SpliceMemberSegments { get; } = new();

    // we use these to ensure we generate a unique type name
    public HashSet<string> ParamNames { get; } = new(StringComparer.Ordinal);
    public HashSet<string> InlinedTypeParamNames { get; } = new(StringComparer.Ordinal);

    public List<ParameterSyntax> Parameters { get; } = new();
    public List<TypeParameterSyntax> TypeParams { get; } = new();
    public HashSet<ITypeParameterSymbol> InlinedTypeParams { get; } = new(SymbolEqualityComparer.Default);

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
    public HashSet<SyntaxNode> FoldClaimedReferences { get; } = new();
    public Dictionary<StagedParameter, List<MemberAccessExpressionSyntax>> FoldsByStagedParameter { get; } = new();

    // --- Shared lift policy + counters ---
    public ValueLift ValueLift { get; set; } = null!;
    public bool ConverterError { get; set; }
    public int StagedRegionCounter;
}

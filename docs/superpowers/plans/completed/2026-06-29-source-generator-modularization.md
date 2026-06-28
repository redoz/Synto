# Refactor Plan — Synto Source-Generator Modularization

> **Status:** PARTIALLY IMPLEMENTED — Waves 1–2 landed in full plus the four Wave-3 leaf carve-outs; the headline decomposition (Wave 5) and Wave 4 are NOT delivered. Do **not** archive to `completed/`. See "Implementation progress" below. All 5 decisions remain locked (see "Locked decisions").
> **Rigor:** High — behavior-preserving refactor of cacheability-load-bearing generator code; every wave is green-gated against byte-identical snapshots + the terminal cacheability assertions. TDD discipline: no production move lands without the existing snapshot/cacheability suite green; treat any snapshot diff as a regression to fix-forward, never a re-accept.
> **Scope:** `src/Synto.SourceGenerator/` only (the umbrella generator). The `Synto.Diagnostics` package is explicitly OUT of scope (decision 2).
> **Generated:** 2026-06-29 via the `modularity-refactor-plan` workflow (5 cluster analyzers + invariant briefing + principal-engineer synthesis). All `path:line` anchors verified against source.

## Locked decisions

1. **Namespace — keep flat `namespace Synto;`** everywhere. No re-namespace pass; folder-as-namespace is deferred and can be done later as a separate snapshot-affecting change.
2. **`Synto.Diagnostics` package — excluded entirely.** Do NOT touch `src/Synto.Diagnostics/DiagnosticsGenerator.cs` or its snapshots. The `DiagnosticSourceBuilder` extraction is deferred to its own future plan. (The pre-existing modified `Synto.Diagnostics` snapshots in the working tree are unrelated to this refactor — leave them as-is; do not bundle.)
3. **`TemplateValidator` mid-build gates — keep guards physically in place** as bool guard methods the builder invokes (NO control-flow inversion, no hoist to a single pre-build pass). Diagnostic emission order relative to partial-build state must stay identical.
4. **`ValueLift` — one instance per `CreateSyntaxFactoryMethod` invocation**, the `[Runtime]` converter cache lazy (walked on first concrete non-built-in type) and shared across all three lift sites; SY1011/SY1012 fire at identical locations. Never a per-call repeated scan.
5. **Wave-5 granularity — separate commits within the wave.** The leaf carve-outs (`ValueLift`, `SpliceMemberGeneratorEmitter`, `TemplateDocumentBuilder`, `TemplateValidator`, shell) land as their own commits BEFORE the `CreateSyntaxFactoryMethod → TemplateFactoryBuilder` step decomposition, which is its own final commit, so a snapshot regression bisects to one step.

## Problem summary

The umbrella generator is dominated by two oversized units: `TemplateFactorySourceGenerator.cs` (1234 LOC, with a single ~775-LOC `CreateSyntaxFactoryMethod` orchestrating ~12 finders, ~10 feature phases, and 7+ mutable accumulators) and `MatchEmitter.cs` (803 LOC welding three independent emit subsystems — node-walk, run-alignment, composition — into one static class). Secondary offenders are `StagedRegionEmitter.cs` (600 LOC, four interleaved pipeline phases with 8-arg parameter thread-through) and the flat `Diagnostics.cs` (347 LOC mixing shared SY0000/SY1001-1004 descriptors with seven Templating-only subsystems). The debt is **concentration, not coupling**: nearly every carve-out is a pure code move of transform-internal helpers, so the work is low-behavioral-risk but must be sequenced to protect cacheability, single-source-of-truth injection, and snapshot stability.

## Target structure

End-state under `src/Synto.SourceGenerator/`. **All files keep flat `namespace Synto;`** — verified across every existing file in both folders (`Matching/*.cs`, `Templating/StagedRegionEmitter.cs:8`, `TemplateSyntaxQuoter.cs:9`). The briefing's "folder = namespace `Synto.Templating.*`" is aspirational; re-namespacing is an out-of-scope, snapshot-affecting change (see Open Questions). New files mirror existing convention.

```
src/Synto.SourceGenerator/
├── Diagnostics.cs                         (~110 LOC) SHARED: IdPrefix, SY0000, SY1001-1004 — root, both features
├── EquatableArray.cs / DiagnosticInfo.cs  (untouched cross-cutting)
├── SymbolMetadataExtensions.cs            (~40 LOC) NEW root cross-cutting: has/find-attribute, named-arg reads
│
├── Templating/
│   ├── TemplateFactorySourceGenerator.cs  (~70 LOC) pipeline shell ONLY (Initialize/GenerateTemplate/Emit)
│   ├── TemplateFactoryBuilder.cs          (~400 LOC) carved core, ordered per-feature steps over a context
│   ├── TemplateBuildContext.cs            (~40 LOC) the shared mutable accumulator object
│   ├── TemplateValidator.cs               (~120 LOC) all "is this template usable?" gates
│   ├── TemplateDocumentBuilder.cs         (~130 LOC) doc assembly + helper-scan injection + format + filename
│   ├── ValueLift.cs                       (~90 LOC) value→syntax lift policy, owns lazy [Runtime] converter cache
│   ├── SpliceMemberGeneratorEmitter.cs    (~100 LOC) splice-generator body emit + its rewriter
│   ├── TemplateDiagnostics.cs             (~250 LOC) SY1005-1021 Templating-feature registry (twin of MatchDiagnostics)
│   ├── TemplateSyntaxQuoter.cs            (~110 LOC) slimmed: Visit overrides + [Splice] BuildList dispatch
│   ├── InterpolationFold.cs               (~145 LOC) staged-fold subsystem (spec 2026-06-28)
│   ├── StagedRegionEmitter.cs             (~100 LOC) slimmed orchestrator (per-container grouping/assembly)
│   ├── StagedRegion.cs                    (~30 LOC) StagedRegion + StagedRegionEmission records
│   ├── StagedRegionFinder.cs              (~50 LOC) FindRegions + ComputeConsumedNodes
│   ├── StagedLivenessAnalysis.cs          (~55 LOC) ComputeStagedSet fixpoint + ReferencesStaged
│   ├── StagedEmitContext.cs               (~30 LOC) codegen-input carrier (kills the 8-arg thread-through)
│   ├── StagedScaffoldBuilder.cs           (~230 LOC) per-region control-flow codegen + staged predicates
│   ├── StagedHelperCallFactory.cs         (~60 LOC) nameof-bound CALL builders over Synto.Core helpers
│   ├── RootRenameRewriter.cs              (~25 LOC) promoted standalone rewriter
│   ├── BuilderCallModel.cs                (~62 LOC) BuilderArgKind/Binding/Call/CallResult DTOs
│   ├── SyntaxBuilderRegistry.cs           (~50 LOC) compilation-wide [SyntaxBuilder] discovery
│   ├── FacadeCallFinder.cs                (~95 LOC) body-walk call discovery + dispatch (keeps FindBuilderCalls seam)
│   ├── FacadeArgumentBinder.cs            (~75 LOC) call→bindings, shares FacadeShape cursor
│   ├── FacadeShape.cs                     (~170 LOC) slimmed (attr helpers move to SymbolMetadataExtensions)
│   ├── FileLocalHelpers.cs                (~110 LOC) registry table + DTOs ONLY
│   ├── HelperResourceLoader.cs            (~90 LOC) load/parse/public→file rewrite
│   ├── TemplateSyntaxQuoterInvoker.cs     (~130 LOC) in-place collapse of dup'd visitors + ParseTypeName idiom
│   ├── BindingTimeClassifier.cs           (untouched — healthy)
│   ├── Staged*Finder.cs / SpliceParameterFinder.cs  (untouched — reference family)
│   ├── SyntaxBuilderFacadeGenerator.cs    (untouched — healthy thin generator)
│   └── TrackingNames.cs                   (untouched — 2 stages: Transform/Result)
│
└── Matching/
    ├── MatchFactorySourceGenerator.cs     (untouched — 160 LOC, deliberate parallel)
    ├── MatchEmitter.cs                    (~180 LOC) slimmed orchestrator: Emit + ExtractAnchors + ScanForDeferredForeach
    ├── MatchNodeWalker.cs                 (~120 LOC) node-structural matching + shared capture seam
    ├── MatchRunAligner.cs                 (~280 LOC) statement-run alignment engine
    ├── MatchComposer.cs                   (~60 LOC) file assembly + format (snapshot-shape owner)
    ├── MatchEmitModel.cs                  (~100 LOC) Capture/RunElement/LiteralElement/HoleElement/MatchContext
    ├── MatchMarkers.cs                    (untouched — highly cohesive)
    ├── MatchDiagnostics.cs                (untouched — already the per-feature twin)
    └── MatchTrackingNames.cs              (untouched)
```

## Implementation progress

Recorded post-hoc against `experiment/modularization`. The primary objective — breaking down the
two oversized units — is met for `MatchEmitter.cs` (803 → 195 LOC) but **unmet for the larger
unit**: `TemplateFactorySourceGenerator.cs` is still **1234 LOC** (Wave 5 never ran). This plan is
therefore **not** delivered and is correctly **not** archived to `completed/`.

- **Wave 1 — LANDED.** `Diagnostics` split into shared root + `Templating/TemplateDiagnostics.cs`;
  `SymbolMetadataExtensions` extracted.
- **Wave 2 — LANDED.** Matching split into `MatchEmitModel` / `MatchNodeWalker` / `MatchRunAligner` /
  `MatchComposer`; `MatchEmitter.cs` slimmed to a 195-LOC orchestrator.
- **Wave 3 — PARTIAL.** The four leaf carve-outs landed (`StagedRegion` records,
  `RootRenameRewriter`, `StagedHelperCallFactory`, `StagedRegionFinder`), but
  `StagedLivenessAnalysis` / `StagedEmitContext` / `StagedScaffoldBuilder` were **not** extracted —
  `StagedRegionEmitter.cs` is still 431 LOC.
- **Wave 4 — NOT STARTED.** No `InterpolationFold` / `BuilderCallModel` / `SyntaxBuilderRegistry` /
  `FacadeCallFinder` / `FacadeArgumentBinder` / `HelperResourceLoader` / `TemplateSyntaxQuoterInvoker`
  collapse landed.
- **Wave 5 (HEADLINE) — NOT STARTED.** `TemplateFactorySourceGenerator.cs` is untouched at 1234 LOC;
  none of `TemplateFactoryBuilder` / `TemplateBuildContext` / `TemplateValidator` /
  `TemplateDocumentBuilder` / `ValueLift` / `SpliceMemberGeneratorEmitter` exist.

**To close this plan**, a follow-up implementation run must complete the remaining Wave-3 extractions
plus Waves 4 and 5 (the headline decomposition), each green-gated byte-identical per the verification
notes below, before the plan may be archived.

## Sequenced work (waves)

Five waves, each independently shippable and green-gateable. Ordering principle: **pure declaration/data moves and leaf-helper extractions first** (near-zero behavioral risk, no ordering invariants), **then the cohesive single-subsystem carve-outs**, **then the high-risk orchestrator decompositions last** (after their downstream collaborators are already extracted and stable). Critically — **no wave adds an `IncrementalValueProvider` stage**; every carved unit is a transform-internal helper invoked synchronously inside an existing `ForAttributeWithMetadataName` transform. Therefore no wave touches `TrackingNames.cs`/`MatchTrackingNames.cs` and the existing terminal Transform/Result assertions remain sufficient — *provided* the per-wave reviewer confirms no extraction was turned into a `SyntaxProvider`.

---

### Wave 1 — Diagnostics partition + shared symbol helper (lowest risk, unblocks everything)

**What**
- Split `Diagnostics.cs:11-105` (IdPrefix, SY0000 `InternalError`, SY1001-1004 target family with both `TargetType` and `LocationInfo` overloads) → stays as root `Diagnostics.cs` (shared registry). Move `Diagnostics.cs:107-346` (SY1005-1021) → `Templating/TemplateDiagnostics.cs`, updating call-site qualifiers in `TemplateSyntaxQuoterInvoker`, `StagedParameterFinder`, `StagedRegionEmitter`, `SyntaxBuilderFinder`, `TemplateFactorySourceGenerator` from `Diagnostics.<X>` → `TemplateDiagnostics.<X>`.
- Extract `SymbolMetadataExtensions.cs` (root): `FacadeShape.cs:166-197` (`FindAttribute`/`GetNamedBool`/`GetNamedTypeDisplay`) + `SyntaxBuilderFinder.cs:270-279` (`HasAttribute`).

**Why this order**
Diagnostics are leaf descriptors with no ordering or cacheability semantics — pure id-preserving moves. Doing this first establishes the symmetric per-feature registry (`MatchDiagnostics` already exists at `Matching/MatchDiagnostics.cs`, confirmed) that the later Templating carve-outs will reference, so they import the right qualifier from day one. `SymbolMetadataExtensions` is a tiny dedup that several later units (`FacadeArgumentBinder`, `SyntaxBuilderRegistry`) lean on.

**Risk & cacheability**
- **Layering (MEDIUM):** shared SY0000/SY1001-1004 MUST stay at project root — relocating them under `Templating/` would force Matching to reach into a sibling feature folder. The split keeps them at root precisely to avoid this.
- **Id-range integrity (blocking):** every SY id moves verbatim; `AnalyzerReleases.Unshipped.md` (RS2008) entries are unchanged because ids are unchanged. A dropped/renamed id is a finding.
- **Naming `SymbolExtensions` collision:** name the new helper `SymbolMetadataExtensions` (not `SymbolExtensions`) and keep it `internal` at the SourceGenerator root — never injected.
- No cacheability surface touched (descriptors already return `DiagnosticInfo`).

**Verification**
`dotnet test` for `Synto.Test` and the Diagnostics test project. The RS2008 analyzer must stay clean. Zero snapshot diffs expected.

---

### Wave 2 — Matching subsystem split (high value, self-contained, no generator touch)

**What**
- `MatchEmitModel.cs` ← `MatchEmitter.cs:702-803` (Capture, RunElement/LiteralElement/HoleElement, MatchContext) — pure data move first.
- `MatchNodeWalker.cs` ← `MatchEmitter.cs:129-202` (EmitNodeMatch), `:210-219` (ComputeExpressionRootGate), `:232-252` (EmitCapture), `:260-272` (EmitStatementCapture), `:279-280` (RenderCaptureArguments).
- `MatchRunAligner.cs` ← `MatchEmitter.cs:287-299, 391-469, 476-482, 491-539, 546-565, 572-603, 606-616, 619-633`.
- `MatchComposer.cs` ← `MatchEmitter.cs:642-699` (Compose).
- `MatchEmitter.cs` retains `:23-121` (Emit), `:310-357` (ExtractAnchors), `:365-375` (ScanForDeferredForeach) and delegates outward.

**Why this order**
Matching is fully decoupled from Templating and from its own generator entrypoint (`MatchFactorySourceGenerator.GenerateMatcher` calls only `MatchEmitter.Emit`). Extracting `MatchEmitModel` first gives all three emit units a shared dependency with no cyclic file coupling; the aligner depends on the walker (one direction: aligner → walker), so walker lands before/with aligner. Composer is independent. This wave can ship before the Templating orchestrator work begins.

**Risk & cacheability**
- **Cacheability:** all five splits live inside `MatchEmitter`'s emit phase, which already runs entirely inside the transform (`MatchFactorySourceGenerator.cs:55,63-95`). `MatchContext`/`Capture`/`RunElement` MUST remain transform-local scratch — never flowed across a provider boundary; only the existing equatable `MatchGenerationResult` crosses. Reviewer confirms no moved closure newly captures `Compilation`/`ISymbol`/`SemanticModel`/`SyntaxNode`; `MatchMarkers` keeps holding the `SemanticModel`, reached only via `ctx.Markers`.
- **Snapshot shape (MEDIUM):** `MatchComposer` now solely owns the generated record/method/`CouldMatch`/`Pattern` shape that `test/Synto.Test/**` pins. Any byte diff post-split is a regression, not a re-accept.
- **Namespace:** all new files declare `namespace Synto;`. Do NOT "fix" to `Synto.Matching`.
- **Generator parallelism:** `MatchFactorySourceGenerator` stays untouched.

**Verification**
`test/Synto.Test/**` matching snapshots byte-identical. Existing terminal cacheability assertion for the match pipeline still green (no new stage).

---

### Wave 3 — Staging subsystem decomposition (medium risk, isolates StagedRegionEmitter)

**What**
- `StagedRegion.cs` ← `StagedRegionEmitter.cs:17-46` (records).
- `RootRenameRewriter.cs` ← `:576-599` (promote nested rewriter).
- `StagedHelperCallFactory.cs` ← `:65-69` (nameof constants), `:483-509`, `:559-574`.
- `StagedRegionFinder.cs` ← `:72-119` (FindRegions, ComputeConsumedNodes).
- `StagedLivenessAnalysis.cs` ← `:127-169` (ComputeStagedSet) + `:468-481` (ReferencesStaged).
- `StagedEmitContext.cs` ← the carrier replacing `:171-180` inputs + the threaded params at `:229-238,266-276,322-332,368-378`.
- `StagedScaffoldBuilder.cs` ← `:229-427` (TryBuildScaffold/Control/If/RegionBody) + predicates `:434-557`.
- `StagedRegionEmitter.cs` retains `:171-227` (Emit) as slimmed orchestrator.

**Why this order**
Staging is consumed by `TemplateFactorySourceGenerator.cs:617,620,1048`, but those are stable call sites that this wave does not move. Doing staging before the headline Wave 5 means the big `CreateSyntaxFactoryMethod` decomposition calls an already-clean `StagedRegionEmitter.Emit`/finders. Order within the wave: records + rewriter + call-factory (leaf moves) → finder + liveness (phase extractions) → context object → scaffold builder (the bulk).

**Risk & cacheability**
- **Single-source-of-truth (blocking):** the nameof bindings in `StagedHelperCallFactory` (`nameof(CollectionSyntaxExtensions.*)`, `nameof(LiteralSyntaxExtensions.ToSyntax)`) MUST move **verbatim**, never respelled as string literals — they are the compile-time drift guard against `Synto.Core`. The factory emits CALLS only; never re-declares a helper.
- **Cacheability:** `StagedEmitContext` is transform-internal only and need NOT be equatable, but must not capture roots into anything that outlives the transform. No new provider stage; `TrackingNames.cs` untouched.
- **Behavior pin:** `InjectedSurfaceCompletenessTest.cs:159` asserts `CollectLiftPoints` renames live roots — keep that predicate's behavior identical when it moves into `StagedScaffoldBuilder`. SY1014 nested-region guard must fire identically.

**Verification**
`test/Synto.Test/**` staged-region snapshots byte-identical; `InjectedSurfaceCompletenessTest` green. Templating terminal cacheability assertion green.

---

### Wave 4 — Quoter + facade-finder + helper-loader splits (medium risk, prepares the core)

**What**
- `InterpolationFold.cs` ← `TemplateSyntaxQuoter.cs:111-256` (TryFoldInterpolatedContents/IsFoldableHole/BuildFusedText); quoter keeps its `Visit<TNode>` guard but delegates the body, passing `this.Visit` as a synchronous re-entry callback.
- `BuilderCallModel.cs` ← `SyntaxBuilderFinder.cs:16-77` (DTOs).
- `SyntaxBuilderRegistry.cs` ← `SyntaxBuilderFinder.cs:94-117, 281-306, 270-279` (HasAttribute now defers to `SymbolMetadataExtensions` from Wave 1).
- `FacadeCallFinder.cs` ← `SyntaxBuilderFinder.cs:120-194, 248-268` — **keeps the exact `FindBuilderCalls` entry name/signature** that `TemplateFactorySourceGenerator.cs:998` calls.
- `FacadeArgumentBinder.cs` ← `SyntaxBuilderFinder.cs:172-194, 196-246` — reuses `FacadeShape.Derive`'s `freshReturnTypeParam` cursor instead of recomputing (current `:215`).
- `HelperResourceLoader.cs` ← `FileLocalHelpers.cs:94-155, 171-200` (Load/FindType/ReadResource + PublicToFileRewriter); `FileLocalHelpers.cs` keeps the Entries registry + DTOs only.
- In-place collapse in `TemplateSyntaxQuoterInvoker.cs`: unify `:36-53`/`:55-72` duplicate visitors and the 7× `ParseTypeName(node.GetType().FullName!)` idiom (`:45,51,64,70,87,130,147`) behind one private helper. **No new file.**

**Why this order**
These are the collaborators `CreateSyntaxFactoryMethod` invokes (`FindBuilderCalls` at `:998`, the quoter at `:1011/:1085`, `FileLocalHelpers.Entries` via the document builder). Stabilizing them before Wave 5 means the core decomposition orchestrates already-clean units. The facade-finder split keeps the `FindBuilderCalls` seam byte-stable so the core is untouched by it.

**Risk & cacheability**
- **Single-source-of-truth (blocking):** `HelperResourceLoader` must still produce a `file static class` (not internal/public) — the `public`→`file` rewrite outcome is byte-load-bearing (avoids CS0121 vs `Synto.Core`'s public `ToSyntax`/`ToTypeSyntax`). `PublicToFileRewriter` must stay distinct in **outcome** from `SurfaceInjectionGenerator.PublicToInternalRewriter` (file vs internal); do not unify them onto one modifier. The `Entries` table + nameof scan keys move byte-identical.
- **Cacheability:** the `InterpolationFold` `this.Visit` callback must be a local delegate used synchronously during emission — never a captured closure that outlives the transform. No new provider stage.
- **Snapshot:** the `InterpolationFold` and `TemplateSyntaxQuoterInvoker` collapses must produce **byte-identical** output. Treat any diff as a finding.

**Verification**
`test/Synto.Test/**` interpolation-fold and facade snapshots byte-identical (the fold has its own spec 2026-06-28 fixtures). `SyntaxBuilderFacadeGenerator` snapshots unchanged. Cacheability terminal assertion green.

---

### Wave 5 — `TemplateFactorySourceGenerator` decomposition (the headline, highest risk, last)

**What**
- `TemplateFactorySourceGenerator.cs` reduced to the pipeline shell: `:20-35` Initialize, `:37-69` GenerateTemplate, `:71-78` Emit. Everything `:81-1233` moves out.
- `TemplateValidator.cs` ← `:81-123` (ValidateTemplate) + a home for the mid-build bail gates currently at `:419,554,608,631,988,1003,1063` (kept as small bool guards the builder calls — no control-flow inversion).
- `TemplateDocumentBuilder.cs` ← `:125-204` (ProcessTemplate) + `:206-243` (MergeUsings, FindReferencedHelpers).
- `ValueLift.cs` ← `:250-268` (IsBuiltInLiteralType) + `:285-342` (TryEmitValueLift), folding the by-ref converter cache into an instance.
- `SpliceMemberGeneratorEmitter.cs` ← `:1130-1182` + `:1190-1233` (the nested rewriter).
- `TemplateFactoryBuilder.cs` + `TemplateBuildContext.cs` ← `:344-1120` (CreateSyntaxFactoryMethod), carved into ordered per-feature steps over the shared accumulator.

**Why this order**
This is THE 1234-LOC problem, and it must come last: it calls into staging (Wave 3), the quoter/facade-finder/helpers (Wave 4), and the new diagnostics qualifiers (Wave 1). Decomposing it after its collaborators are stable means each extracted step delegates to an already-clean, already-green unit, and the only remaining risk is the **internal phase ordering**, isolated to this one wave. Sub-sequence within the wave: shell extraction + `ValueLift` + `SpliceMemberGeneratorEmitter` + `TemplateDocumentBuilder` + `TemplateValidator` (independent leaf carve-outs) FIRST, then the `CreateSyntaxFactoryMethod` → `TemplateFactoryBuilder` step decomposition LAST as its own commit.

**Risk & cacheability**
- **Cacheability (HIGH):** the shell extraction is the whole point — it isolates the only cacheability-load-bearing code (the equatable `TemplateGenerationResult` boundary at `:68`) from 1150 LOC of helpers. No carved method may capture `Compilation`/`SemanticModel`/`ISymbol`/`SyntaxNode` into pipeline state; they pass through the transform call stack only. `TemplateBuildContext` is transform-local scratch, not equatable, never crossing a provider. No new stage → `TrackingNames.cs` unchanged. **Per the "cacheability guards iterate ALL steps" rule:** if review finds any step accidentally became a provider, it must register a tracking name AND add a per-step `Cached`/`Unchanged` assertion — but the design intent is zero new stages.
- **Phase-order (blocking):** the step extraction MUST preserve exact order — parameter discovery order, preamble append order, the "container replacement added LAST" rule (`:1046-1069`), inline/syntax/live lifts populated before the island quoter (`:1011`) and final quoter (`:1085`). Make each cross-step ordering invariant explicit (named ordered steps over one context) instead of implicit-by-line-position. Any reordering changes output and is a finding.
- **Single-source-of-truth (HIGH):** `FindReferencedHelpers` in `TemplateDocumentBuilder` must keep deciding which file-local helpers to emit by **scanning the emitted factory's `MemberAccessExpression` names against `FileLocalHelpers.Entries`** (`:228-243`) — never via builder flags. The builder must carry no usage flags.
- **ValueLift (MEDIUM):** the converter cache stays LAZY (walked only on first concrete non-built-in type, `:314`) and shared across all three lift sites; SY1011/SY1012 fire at identical locations.
- **Filename pin:** `$"{Target.FullName}.{Source.Identifier}.g.cs"` (`:203`) unchanged.

**Verification**
Full `test/Synto.Test/**` snapshot suite byte-identical (factory name/signature/namespace, `#nullable enable` prepend, usings dedup order, filename). Templating cacheability test green with terminal Transform/Result `Cached`/`Unchanged` on an unrelated edit. Ship the leaf carve-outs and the `TemplateFactoryBuilder` step decomposition as **separate commits** so a snapshot regression bisects to one step.

## Leave-alone list

Reviewers judged these already well-factored — do NOT touch (scoping the effort):

- `Matching/MatchMarkers.cs` (349 LOC) — highly cohesive marker-resolution; trailing value types a minor smell only.
- `Matching/MatchFactorySourceGenerator.cs` (160 LOC) — canonical entrypoint, deliberate parallel to `TemplateFactorySourceGenerator`; keep parallelism.
- `Matching/MatchDiagnostics.cs` — already the per-feature twin Wave 1 mirrors.
- `Templating/BindingTimeClassifier.cs` (252 LOC) — healthy; every method serves one dataflow-partition algorithm.
- `Templating/StagedRootFinder.cs` (234), `StagedParameterFinder.cs` (228), `StagedTypeParameterFinder.cs` (124), `SpliceParameterFinder.cs` (95) — the finder family; `SpliceParameterFinder` is the reference template the others mirror.
- `Templating/SyntaxBuilderFacadeGenerator.cs` (136 LOC) — thin generator; must stay a SEPARATE `[Generator]`.
- `Templating/FacadeShape.cs` core (`:67-159`) — single source of truth for facade shape; only the attr-reading helpers leave in Wave 1.
- `SurfaceInjectionGenerator.cs` (240 LOC) — explicitly NOT split; its doc-comment (`:12-57`) encodes the single-source-of-truth + accessibility invariants adjacent to the code they constrain. Must stay a separate post-init `[Generator]`; both rewriters keep exact scope.
- `Synto.Diagnostics` package — out of scope for this umbrella-generator refactor (see Open Question 2).

## Decisions (resolved — see "Locked decisions" at top)

All five open questions are resolved and locked in the header. Summary for the implementer:

1. Namespaces stay **flat** (`namespace Synto;`) — no re-namespace pass.
2. `Synto.Diagnostics` is **out of scope** — do not touch it or its snapshots.
3. `TemplateValidator` gates stay **physically in place** — no control-flow inversion.
4. `ValueLift` is **one instance per invocation** with a lazy shared converter cache.
5. Wave 5 ships as **separate commits** (leaf carve-outs first, step decomposition last).

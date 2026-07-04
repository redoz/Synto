# File organization brainstorm — feature colocation + killing the flat embedded-resource list

**Status: DRAFT / brainstorm — for discussion, not an approved plan. Nothing has moved.**

## 1. Problem & goals

Two intertwined complaints:

1. **The embedded-resource wiring looks like a workaround, not a design.**
   `src/Synto.SourceGenerator/Synto.SourceGenerator.csproj` hand-maintains **40 `<EmbeddedResource>`
   entries** in a flat list (28 `Synto.Runtime.*` markers, 2 `Synto.RuntimeGenerated.*` shift-group
   files, 10 `Synto.Helper.*` emit-helpers), each with a hand-typed `LogicalName`. Adding a marker
   means editing a csproj in a *different project* than the file you authored, and getting the
   prefix right by copy-paste. `src/Synto/Synto.csproj` mirrors this with 5 hand-listed
   `<Compile Remove>` entries for the embed-only files, and `src/Synto.Bootstrap/Synto.Bootstrap.csproj`
   adds 3 `<Compile Include ... Link=...>` entries. Three csprojs must agree by convention that
   only exists in comments.

2. **Feature scatter.** The layout is layer-first (and, inside `Templating/`, pipeline-stage-first
   after the 2026-06-30 reorg). To answer "how does the Switch/Case stuff work?" you visit
   **8 directories across 3 projects** (worked example in §2.3). No single folder shows you a
   feature's consumer markers + generator machinery + tests together.

**North star:** open one folder → see the whole feature. Secondary goal: the csproj wiring should
*express* the injection design (roles, prefixes) instead of enumerating its instances.

**Non-goals:** merging projects (impossible — §3), changing any injected byte, changing public
namespaces, changing generated output.

## 2. Current-state map

### 2.1 Projects (the layer axis — this stays)

| Project | TFM | Ships as | Role |
|---|---|---|---|
| `src/Synto` | netstandard2.0 | **Synto.Core** | Runtime/toolkit: marker surface, emit-helpers, quoter machinery. Single source of truth for everything injected. |
| `src/Synto.SourceGenerator` | netstandard2.0 | **Synto** (analyzer pkg) | Umbrella generator: `Templating/`, `Matching/`, `Shared/` (incl. `SurfaceInjectionGenerator`). Embeds `src/Synto` files as resources. |
| `src/Synto.Bootstrap` | netstandard2.0 | not shipped | Self-host generator; `<Compile Link>`s 3 runtime files (`LiteralSyntaxExtensions.cs`, `UsingDirectiveSet.cs`, `SyntaxFormatter.cs`). |
| `src/Synto.Diagnostics` | netstandard2.0 | **Synto.Diagnostics** | Independent `[Diagnostic]` generator; self-contained, not affected here. |

Inside each project the organization is **layer/stage-first**: `src/Synto.SourceGenerator/Templating/`
splits into `Discovery/` (14 finders), `Analysis/`, `Pipeline/`, `Quoting/` (+`NodePipeline/`,
`ListPipeline/`), `Staging/`, `Builders/`, `Helpers/`, `Diagnostics/`, `Lifting/`. That structure
landed 4 days ago (`docs/superpowers/plans/2026-06-30-sourcegen-structure.md`) and fixed the
"52 files in one flat folder" problem — but it optimizes for *pipeline stage*, not *feature*.
This proposal is the next iteration, not a revert: the pipeline core keeps its stage folders;
the *plug-in features* get pulled out of them.

### 2.2 The embedded-resource inventory & the LogicalName contract

Three resource groups in `Synto.SourceGenerator.csproj` (lines 42–115):

| Group / prefix | Count | Consumed by | Mechanics |
|---|---|---|---|
| `Synto.Runtime.*` | 28 | `Shared/SurfaceInjectionGenerator.cs` | **Enumerates `assembly.GetManifestResourceNames()` and filters by prefix** (`ResourcePrefix = "Synto.Runtime."`, line 62). Rewrites `public`→`internal`, injects into every consumer. Hint name = resource name minus prefix (`TemplateAttribute.g.cs`). |
| `Synto.RuntimeGenerated.*` | 2 | same generator, shift group (line 70) | `EquatableArray.cs` / `DiagnosticInfo.cs`: dual-role (compiled **and** embedded), namespace-shifted `Synto` → `Synto.Generators` on injection. Byte-load-bearing, documented in csproj lines 81–90. |
| `Synto.Helper.*` | 10 | `Templating/Helpers/FileLocalHelpers.cs` | **Hard-coded per-helper resource constants** (lines 40–49) + `nameof`-anchored scan keys; loaded on demand, rewritten `public`→`file`, emitted per-file. |

Load-bearing facts for any reorg:

- **The LogicalName is the contract, the physical path is not.** Both consumers resolve resources
  by *name*; `SurfaceInjectionGenerator` doesn't even have a list — it discovers by prefix. So files
  can move freely **as long as each resource keeps its exact LogicalName**.
- Hint names (and therefore the `SurfaceInjectionTest.VerifyInjectedSurface#<Name>.g.verified.cs`
  snapshot filenames) derive from the LogicalName. Renaming a LogicalName renames snapshots.
- **39 of 40 LogicalNames are exactly `"<prefix>" + filename`.** The single exception:
  `Templating/Staging/Template.Parameter.cs` → `Synto.Runtime.Template.cs` (csproj line 48).
- Duplicate LogicalNames fail the build loudly (CS1508), so "filenames unique within a prefix
  group" is a machine-checked invariant, not a hope.
- The 5 `<Compile Remove>` files in `Synto.csproj` (embed-only: `SyntoBuilders.cs`,
  `ForMatchHelpers.cs`, `ForMatchProviderExtensions.cs`, `MatchReplaceExtensions.cs`,
  `Interceptors.cs`) are the second half of the same wiring, maintained by hand in a second csproj.

### 2.3 Worked example — Switch/Case builders (the scatter, concretely)

Everything that participates in "template code calls `SyntoBuilders.Switch(...)/Case(...)/Arm(...)`
and the generator expands it":

**Consumer-facing source — `src/Synto/Templating/Builders/`** (1 folder, good)
- `SyntoBuilders.cs` — the `Switch`/`Case`/`Default`/`Arm`/`WhenArm`/`DefaultArm`/`Return`/`Member`/`TypeOf` facades (lines 36–66). Embed-only (`Compile Remove`d from Synto.Core).
- `SyntaxBuilderAttribute.cs`, `QuotedAttribute.cs`, `ReturnTypeAttribute.cs`, `TypeArgAttribute.cs` — the markers.

**Generator machinery — `src/Synto.SourceGenerator/`** (3 folders)
- `Templating/Builders/SyntaxBuilderRegistry.cs` — discovers `[SyntaxBuilder]` methods.
- `Templating/Builders/FacadeShape.cs`, `FacadeArgumentBinder.cs`, `BuilderCallModel.cs` — shape + argument binding.
- `Templating/Builders/SyntaxBuilderFacadeGenerator.cs` — the `[SyntaxBuilder]`→facade generator.
- `Templating/Discovery/FacadeCallFinder.cs` — finds facade *calls* in template bodies (lives with 13 unrelated finders).
- `Templating/Quoting/NodePipeline/BuilderStep.cs` — the quote-time expansion step (lives with Trim/Replace/Decorate steps).

**Wiring** (2 csprojs)
- `Synto.SourceGenerator.csproj` lines 51–55 — 5 `<EmbeddedResource>` entries.
- `Synto.csproj` line 55 — `<Compile Remove="Templating\Builders\SyntoBuilders.cs">`.

**Tests — `test/Synto.Test/`** (3 folders)
- `Templating/SwitchBuilderTest.cs`, `Templating/SyntaxBuilderTest.cs`.
- `Templating/snapshots/SwitchBuilderTest.*.verified.cs` (6), `Templating/snapshots/SyntaxBuilderTest.*.verified.cs` (6).
- `snapshots/SurfaceInjectionTest.VerifyInjectedSurface#{SyntoBuilders,SyntaxBuilderAttribute,QuotedAttribute,ReturnTypeAttribute,TypeArgAttribute}.g.verified.cs` (5).

Total: **12 source files in 5 folders, 6 csproj lines in 2 other files, 19 test artifacts in 3 folders**
— 8 directories across 3 projects for one feature.

### 2.4 Second example — Decorations (proves the pattern, plus a bonus wart)

- Markers: `src/Synto/Templating/Decorations/` (6 files: `Access`, `IdentifierAttribute`, `VisibilityAttribute`, `SealedAttribute`, `ImplementsAttribute`, `InheritsAttribute`).
- Emit-helpers for the *same feature*: `src/Synto/Decorations/` — a **different top-level folder** (`IdentifierAttributeExtensions.cs` … `InheritsAttributeExtensions.cs`, `ClassDeclarationSyntaxExtensions.cs`). Namespace `Synto` vs the markers' `Synto.Templating`, so the split is physical-only; the runtime project itself scatters one feature across two roots.
- Generator: `Templating/Discovery/DecorationFinder.cs` + `DecorationMarkers.cs`, `Templating/Quoting/NodePipeline/DecorateStep.cs`.
- Wiring: 11 embedded-resource lines (6 markers + 5 helpers).
- Tests: `Templating/DecorationTests.cs`, `Templating/DecorationDiagnosticsTests.cs` + snapshots + 6 SurfaceInjection snapshots.

Same shape for Staging (5 `Staged*Finder`s in `Discovery/`, 3 analyses in `Analysis/`, 7 emitters in
`Staging/`) and Matching (self-contained folder — the one feature that *is* already colocated
generator-side, which is why it's the easiest place to see that colocation works).

## 3. Constraints any option must respect

1. **LogicalName contract** — every `Synto.Runtime.*` / `Synto.RuntimeGenerated.*` / `Synto.Helper.*`
   name must survive byte-identically (or the plan must own the snapshot renames). Prefix
   *discovery* is already dynamic (§2.2), so the contract is per-file names, not lists.
2. **Runtime/generator split is permanent.** Synto.Core ships standalone to consumers; the generator
   ships under `analyzers/dotnet/cs` and must stay netstandard2.0/Roslyn-5.0. They can't merge, and
   generator-internal types must never leak into the injected surface.
3. **Embed-only vs compile+embed vs compile-only** is a *per-file role* (that's what the
   `Compile Remove` list encodes). Any convention must be able to express 4 roles: marker
   (compile+`Synto.Runtime.*`), embed-only (`Synto.Runtime.*`, no compile), emit-helper
   (compile+`Synto.Helper.*`), plain compile.
4. **Bootstrap links 3 runtime files by relative path** — moving those files means touching
   `Synto.Bootstrap.csproj` (mechanical).
5. **Namespaces are decoupled from folders** in `src/Synto` (`Templating/Definition/*.cs` →
   `namespace Synto.Templating;`; `Decorations/*Extensions.cs` → `namespace Synto;`). Public API and
   injected bytes are pinned by snapshots; physical moves are namespace-neutral. Same generator-side:
   the 06-30 convention is "subfolders inside a subsystem are organizational only, same namespace".
6. **Verify snapshots are test-file-relative** (`UseDirectory("snapshots")`), so test moves carry
   their snapshot dirs (git mv, contents unchanged → no re-accepts). `SnapshotOrphanGuardTest`
   matches by *filename*, not path — move-safe.
7. **Cacheability is untouched by file moves** — nothing in the incremental pipeline keys off paths.
   A csproj edit just forces a full rebuild once.

## 4. Options

### Option A — Convention-driven roles + globbed embedding (kill the flat list; minimal moves)

Make the *role* a leaf-folder convention inside the existing feature folders of `src/Synto`, then
replace the 40-entry list with 3 globs whose LogicalName is computed:

```
src/Synto/
├── Templating/
│   ├── Definition/Surface/        TemplateAttribute.cs, QuoteAttribute.cs, TemplateOption.cs
│   ├── Staging/Surface/           SpliceAttribute.cs, RuntimeAttribute.cs, UnquoteAttribute.cs, Syntax.cs, Template.Parameter.cs*
│   ├── Builders/Surface/          SyntaxBuilderAttribute.cs, QuotedAttribute.cs, ReturnTypeAttribute.cs, TypeArgAttribute.cs
│   ├── Builders/InjectedOnly/     SyntoBuilders.cs
│   └── Decorations/Surface/       Access.cs, IdentifierAttribute.cs, … (6)
├── Matching/
│   ├── Surface/                   MatchAttribute.cs, CaptureAttribute.cs, Markers.cs, MatchOption.cs, ReplaceOption.cs
│   └── InjectedOnly/              ForMatchHelpers.cs, ForMatchProviderExtensions.cs, MatchReplaceExtensions.cs
├── Generators/InjectedOnly/       Interceptors.cs
├── Syntax/EmitHelpers/            LiteralSyntaxExtensions.cs, RuntimeTypeExtensions.cs, CollectionSyntaxExtensions.cs, InterpolationSyntaxExtensions.cs
├── Quoting/EmitHelpers/           QuoteSyntaxExtensions.cs
└── Templating/Decorations/EmitHelpers/  IdentifierAttributeExtensions.cs, … (5, moved from src/Synto/Decorations/)
```

`Synto.SourceGenerator.csproj` shrinks to:

```xml
<!-- markers: compiled into Synto.Core AND injected internal into consumers -->
<EmbeddedResource Include="..\Synto\**\Surface\*.cs"      LogicalName="Synto.Runtime.%(Filename)%(Extension)" />
<!-- embed-only surface: injected but never compiled into Synto.Core -->
<EmbeddedResource Include="..\Synto\**\InjectedOnly\*.cs" LogicalName="Synto.Runtime.%(Filename)%(Extension)" />
<!-- emit-helpers: compiled into Synto.Core AND emitted per-file as `file static class` copies -->
<EmbeddedResource Include="..\Synto\**\EmitHelpers\*.cs"  LogicalName="Synto.Helper.%(Filename)%(Extension)" />
<!-- shift group stays explicit: 2 dual-role files with byte-load-bearing caveats -->
<EmbeddedResource Include="Shared\EquatableArray.cs"  LogicalName="Synto.RuntimeGenerated.EquatableArray.cs" />
<EmbeddedResource Include="Shared\DiagnosticInfo.cs"  LogicalName="Synto.RuntimeGenerated.DiagnosticInfo.cs" />
```

and `Synto.csproj`'s 5 hand-listed removes become one glob: `<Compile Remove="**\InjectedOnly\*.cs" />`.

*The one exception:* `Template.Parameter.cs` currently maps to `Synto.Runtime.Template.cs`; under the
glob it would become `Synto.Runtime.Template.Parameter.cs` (hint `Template.Parameter.g.cs` — one
snapshot file renames, zero byte changes). Either accept that rename, rename the physical file to
`Template.cs`, or keep a single explicit `<EmbeddedResource>` override with a comment. (Decision → §6.)

- **Preserves:** all LogicalNames (modulo the one exception), the project split, all namespaces
  (folders ≠ namespaces in `src/Synto`), injected bytes → all snapshots stay green un-re-accepted.
- **Pros:** the csproj now *states the design* ("anything in a `Surface/` folder is injected
  surface") in ~5 lines; adding a marker = dropping a file in the right folder, zero csproj edits;
  the `Compile Remove` duplication disappears; role is visible in every file's path.
- **Cons:** doesn't fix cross-stage scatter in the generator or tests; one more folder level in the
  runtime project; a glob is a footgun (a stray file in `Surface/` silently ships into every
  consumer — mitigated because `SurfaceInjectionTest`/`InjectedSurfaceCompletenessTest` fail on any
  new/changed injected file, but see §6 Q2).
- **Cost/risk:** low. ~30 file moves in one project + 2 csproj rewrites + 3 Bootstrap link-path
  updates. Verifiable in one commit by "zero `.received.` files".

### Option B — Feature-first inside each project, mirrored paths ("same folder name, three projects")

Keep the project split (forced) but make **feature the primary axis in all three trees**, with
Option A's role convention embedded. The generator's `Templating/` keeps its stage folders for the
*pipeline core* but pulls each plug-in feature's pieces out of `Discovery/`/`Analysis/`/`Quoting/NodePipeline/`
into the feature folder:

```
src/Synto/Templating/Builders/            src/Synto.SourceGenerator/Templating/Builders/     test/Synto.Test/Templating/Builders/
├── Surface/ (4 markers)                  ├── FacadeCallFinder.cs        (← Discovery/)       ├── SwitchBuilderTest.cs
└── InjectedOnly/SyntoBuilders.cs         ├── SyntaxBuilderRegistry.cs                        ├── SyntaxBuilderTest.cs
                                          ├── FacadeShape.cs                                  └── snapshots/ (12 verified)
                                          ├── FacadeArgumentBinder.cs
                                          ├── BuilderCallModel.cs
                                          ├── SyntaxBuilderFacadeGenerator.cs
                                          └── BuilderStep.cs             (← Quoting/NodePipeline/)
```

Same treatment for the other plug-ins:

- `Templating/Decorations/` (generator): `DecorationFinder.cs` + `DecorationMarkers.cs` (← `Discovery/`), `DecorateStep.cs` (← `Quoting/NodePipeline/`). Runtime side additionally absorbs `src/Synto/Decorations/` → `src/Synto/Templating/Decorations/EmitHelpers/` (fixes the two-roots wart, §2.4).
- `Templating/Staging/` (generator): absorbs the 5 `Staged*Finder.cs` + `StagedRegionFinder.cs` (← `Discovery/`) and `StagedLivenessAnalysis.cs`/`BindingTimeClassifier.cs`/`BranchPruneIdentifier.cs` (← `Analysis/`) next to the 7 existing emitters. Tests: `StagedParameterTest`, `StagedRootTest`, `RunCollect*`, `SharedStagedParameterTest`, `StagingDiagnosticsTest` → `test/Synto.Test/Templating/Staging/`.
- **Pipeline core stays stage-first**: `Pipeline/` (TemplateFactorySourceGenerator, FactoryBuilder, DocumentBuilder, Validator, Scope, PlanAssembler, QuotePlan…), `Quoting/` (PerNodePipelineQuoter, QuoteStep, QuoteContext, TemplateQuoterFactory + the generic Trim/Replace steps), `Discovery/` shrinks to the core finders (QuoteCallFinder, SpliceCallFinder, ChildTemplateFinder…), `Helpers/`, `Diagnostics/`. The stages *are* the feature there; splitting them by consumer-feature would be artificial.
- `Matching/` is already the proof this shape works — one folder generator-side; mirror it runtime- and test-side with `Surface/`/`InjectedOnly/`.

After this, "how does Switch/Case work?" = open `Templating/Builders/` in whichever of the three
trees you care about — same path everywhere, and the csproj points at the convention, not the files.

- **Preserves:** LogicalNames (Option A mechanics), namespaces (generator subfolders are
  organizational-only per the 06-30 convention — moving `BuilderStep.cs` between subfolders changes
  nothing compiled), snapshot bytes, cacheability.
- **Pros:** actually delivers the north star; the feature boundary becomes the reviewable unit
  (a feature PR touches one folder-triple); future features (interpolated-string fold is queued)
  get a ready-made shape: `Surface/` + finder/step/emitter folder + test folder.
- **Cons:** churns `Templating/` again 4 days after the stage-first reorg (pure `git mv`, but
  history/muscle-memory churn is real); the "plug-in vs core" line requires judgment (§6 Q1);
  KB (`docs/kb`) needs a refresh pass afterward.
- **Cost/risk:** medium. ~25 additional generator-file moves + ~15 test moves beyond Option A.
  All namespace-neutral; risk concentrates in forgetting a snapshot dir move (Verify fails loudly)
  and in the KB/docs going stale (refresh-kb exists for exactly this).

### Option C — Physical cross-project colocation: a `features/` source tree linked into both projects

The maximalist reading of "next to each other": author everything under
`src/features/<Feature>/{surface,generator,tests}/` and have the three csprojs glob-`<Compile Include>`
with `LinkBase`, so projects become *views* over a feature-first tree.

- **Pros:** true on-disk colocation including across the runtime/generator boundary; one folder per feature, period.
- **Cons (disqualifying, in my read):** every project's contents become invisible to "what's in
  this project's folder" reasoning — the exact "weird workaround" smell this effort is trying to
  kill, now applied to *all* code instead of 40 resources; IDE link-folder UX is poor; the
  runtime-vs-generator split still forces per-file role decisions, now encoded in *two* layers of
  convention (tree + link globs); test discovery and Verify's `CallerFilePath`-relative snapshot
  resolution get surprising; tooling (`git log --follow`, code review, the KB's `path:line`
  contract) all churn maximally.
- **Cost/risk:** high, for navigability that Option B's mirrored paths deliver at ~20% of the cost.
  Included for completeness; recommend rejecting.

## 5. Recommendation

**Option B, executed in phases with Option A as Phase 1.** A alone fixes the csproj smell but leaves
the Switch/Case answer scattered across `Discovery/`, `Builders/`, and `Quoting/NodePipeline/`;
C buys marginal extra colocation at disproportionate tooling cost. B's mirrored feature paths give
the "open one folder" experience within each project, and the *cross*-project hop becomes trivial
because the path is identical on both sides. The role convention (Surface/InjectedOnly/EmitHelpers)
turns the injection model from a 40-line list into 5 self-describing globs — the wiring finally
*looks* intentional because it *is* the design, stated once.

### Phased migration (each phase independently green, no snapshot re-accepts)

- **Phase 0 — decide the one naming exception.** `Template.Parameter.cs` → either rename the file to
  `Template.cs` (LogicalName/hint/snapshot names all unchanged; preferred) or keep one explicit
  csproj override. Tiny standalone commit.
- **Phase 1 (= Option A) — runtime role folders + globs.** `git mv` ~30 files inside `src/Synto`
  into `Surface/`/`InjectedOnly/`/`EmitHelpers/` leaves; rewrite the two csproj item groups to the
  5 globs + 1 `Compile Remove` glob; update the 3 `Synto.Bootstrap.csproj` link paths
  (`LiteralSyntaxExtensions.cs` moves into `Syntax/EmitHelpers/`). **Gate:** full build + test run,
  assert zero `.received.` files (`SurfaceInjectionTest`, `InjectedSurfaceCompletenessTest`,
  `ZeroCollisionTest`, `HelperContractTests` all pin the injected/emitted bytes) and the packed
  nupkg layout unchanged.
- **Phase 2 — generator feature folders.** `git mv` the Builders/Decorations/Staging pieces out of
  `Discovery/`/`Analysis/`/`Quoting/NodePipeline/` into their feature folders (namespaces unchanged
  per the organizational-subfolder convention → compile-neutral). **Gate:** build + full suite, no
  snapshot changes at all.
- **Phase 3 — test colocation.** `git mv` the per-feature test classes **together with** their
  `snapshots/` files into `test/Synto.Test/Templating/{Builders,Decorations,Staging}/`.
  `UseDirectory("snapshots")` is test-file-relative and `SnapshotOrphanGuardTest` is path-agnostic,
  so this is mechanical. **Gate:** full suite green, no re-accepts, orphan guard green.
- **Phase 4 — docs.** Run `refresh-kb` (the KB's `path:line` links churn heavily in Phases 1–3);
  update `architecture.md`'s navigation section; add a short "feature anatomy" note documenting the
  role-folder convention (that note replaces most of the current csproj comment prose, which should
  shrink to pointers).

Everything is `git mv` + csproj edits; no code content changes anywhere except the optional
`Template.Parameter.cs` file rename. Any `.received.` file at any gate is a regression to
investigate, never to accept.

## 6. Open questions for tomorrow

1. **Where is the plug-in/core line inside `Templating/`?** Proposed: Builders, Decorations, Staging
   are features (own folders, own finders/steps/emitters); Pipeline/Quoting/Discovery-core/Helpers
   stay stage-first. But Staging is arguably *the* templating capability now, not a plug-in — pulling
   `BindingTimeClassifier` out of `Analysis/` into `Staging/` is defensible either way. How far do we
   push before we're just re-litigating the 06-30 reorg?
2. **Glob footgun tolerance.** With `**\Surface\*.cs` globbed, dropping a file in the folder ships it
   into every consumer with no csproj diff to review. The Verify gates catch it (a new injected file
   fails `SurfaceInjectionTest` until a snapshot is approved), but do we *also* want an explicit
   manifest assertion (a test pinning the exact expected resource-name set) so the review artifact is
   a readable list-diff rather than a snapshot approval?
3. **Role vocabulary and mechanism.** `Surface/` vs `InjectedOnly/` vs `EmitHelpers/` as *folders* —
   or would a filename suffix convention (`*.Surface.cs`) or MSBuild item metadata in a
   `Directory.Build.props` be less intrusive than an extra directory level? (Folders won: visible in
   every tree view and glob-able — but it's taste.)
4. **`Template.Parameter.cs`:** rename the file to `Template.cs`, or keep a single explicit override
   entry? (File rename preferred — zero contract movement — but it's the file where `Parameter<T>()`
   / `Unquote<T>()` live and the current name is descriptive.)
5. **Does Phase 3 extend to the cross-feature suites?** `SurfaceInjectionTest`'s 31 snapshots are
   per-*resource* (i.e. per-feature) but live centrally under `test/Synto.Test/snapshots/`. Splitting
   them per feature folder would complete colocation but means splitting one test class or
   parameterizing `UseDirectory` — probably not worth it. Confirm "central is fine for genuinely
   cross-cutting suites."
6. **Timing vs in-flight work.** `experimental/quote-action-pipeline` is unmerged and touches
   `Templating/Quoting/` heavily; Phases 2–3 would conflict with it wholesale. Sequence: merge (or
   abandon) that branch first, then reorganize? (Phase 1 conflicts with nothing — it only touches
   `src/Synto` + csprojs.)

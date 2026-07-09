# Inline live-template highlighting via a diagnostic analyzer (SY1035)

**Status:** Approved design — ready for implementation plan.
**Relates to:** `2026-07-05-live-template-highlight-design.md` (the original SY1035 emitter-sourced diagnostic).

## Problem

SY1035 ("live template code", Info severity, category `Synto.Staging`) is emitted by the
**source generator** during the staging emit pass (three taps in `StagedScaffoldBuilder`,
`StagedRootLifter`, `StagedRegionEmitter`/`TemplateFactoryBuilder`). Generator-reported
diagnostics surface only in the **Error List after a build** — they never enter the live
in-editor diagnostic pipeline that paints squiggles/adornments as you type. So SY1035 is
invisible inline; users only see it post-build in the Error List.

To paint inline, the diagnostic must come from a `DiagnosticAnalyzer` (analyzers run in the
live pipeline, on the post-generator compilation).

## Decision

Ship a `DiagnosticAnalyzer` that **reuses the existing generator emit path** to produce the
identical SY1035 set, rather than re-deriving liveness independently. This is a **deliberate
workaround** (see "Known debt / 1.0 gate"): the analyzer runs the whole classification+emit
pass and discards the generated factory.

Rejected alternatives:
- **Manifest** (generator emits a live-span artifact, analyzer consumes it) — introduces
  edit-time staleness/flicker and a versioned generator↔analyzer contract.
- **Front-end extraction** (a standalone `Classify` pass both consume) — the *correct* long-term
  design, but a real refactor: liveness is currently fused into the mutating emit walk (the taps
  are byproducts of lift decisions, "correct by construction"). Deferred to the 1.0 gate below.

## Architecture

Two thin, behavior-preserving seams + one new analyzer. Neither seam un-fuses classification
from emit (that is the deferred C work).

### Seam A — decouple the discovery entry from the generator context type

`GenerateTemplate` / `GenerateBareTemplate` in `TemplateFactorySourceGenerator` take
`GeneratorAttributeSyntaxContext`, a generator-driver-only struct an analyzer **cannot
construct**. Extract the TemplateInfo-building body into a core taking plain inputs:

```
TemplateInfo? BuildTemplateInfo(
    SemanticModel semanticModel,
    MemberDeclarationSyntax target,        // Method | Class | Struct — [Template] targets all three
    ImmutableArray<AttributeData> attributes,
    CancellationToken cancellationToken)
```

The generator transform calls it (unchanged behavior). The analyzer calls it too. This seam is
**mandatory** for the analyzer to exist at all; it touches no emit logic.

`[Template]`/`[Template<T>]` target `Method | Class | Struct` (`TemplateAttribute.cs:10`), so the
entry and the analyzer's gate must accept type-carrier declarations, not just methods. Carrier
processing (nested sub-templates, `ctx.SubTemplates`) already flows through `Build`.

### Seam B — `highlightsOnly` early-return in `TemplateFactoryBuilder.Build` (perf lever)

All SY1035 taps fire at or before `EmitLiveRegions(ctx)` (`TemplateFactoryBuilder.cs:93`), and the
final dedupe/order/cap merge lives inside `EmitLiveRegions` (`:897-903`). Everything after —
`EmitSpliceMemberGenerators` (`:96`) and `QuoteAndAssemble` (`:98`, factory-syntax construction) —
adds **zero** highlights.

Add `bool highlightsOnly = false` to `Build`. When set, `return` immediately after the
`EmitLiveRegions` success check: `ctx.Diagnostics` already holds the complete, merged SY1035 set,
and the analyzer skips the allocation-heavy factory build/assembly **and** never enters
`TemplateDocumentBuilder` (formatting/serialization). Default `false` keeps the generator path
byte-identical.

### New component — `LiveTemplateHighlightAnalyzer` (in `Synto.Generators`)

Same assembly as the generator (same analyzer DLL, `internal` access for free, auto-discovered
alongside the `[Generator]`). Steps:

1. **Cheap syntactic gate** — the member carries `[Template]`/`[Template<T>]` by name? Bail before
   touching the semantic model otherwise.
2. `ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None)` — never runs on generated files.
3. `BuildTemplateInfo(...)` → `Build(diagnostics, semanticModel, emptyUsings, templateInfo,
   options, highlightsOnly: true)`. `additionalUsings` is empty (usings affect emitted output only,
   not liveness classification).
4. Filter `diagnostics` to SY1035; report each at its `Location`.
5. Whole body in `try/catch` → report nothing on throw (never crash the editor). Honor
   `CancellationToken` where the API allows.

Highlights are identical to the generator's **by construction** — it is the same code executing.

## Error handling / divergence (two-tier)

The generator suppresses all SY1035 on the staging-error bail path. Errors split:

- **Structural, pre-emit predicates** — unsupported staged control shape
  (`StagedScaffoldBuilder.cs:94`) and nested staged control (`:153`). These are decidable from
  `Partition` + `StagedRegions` before emit. Because the analyzer runs the *same* `Build`, it hits
  these bails identically — no special handling needed; both sides suppress consistently.
- **Genuinely emit-time** — "staged region body could not be quoted" (`:173`), the island quoter
  actually failing. Rare. Since the analyzer runs the full pass, it *also* hits this bail, so even
  this stays consistent under Option B (this is a bonus of running the real emit vs. a front-end).

Net: because B executes the real pass, the two-tier divergence risk that would afflict a front-end
extraction (C) does **not** apply here. It is documented because C must handle it later.

## Testing

- **Highlight-location tests** — analyzer verifier (`Microsoft.CodeAnalysis.CSharp.Analyzer.Testing`
  or the project's existing analyzer-test harness if present) over representative templates:
  depth-zero live holes, maximal purely-staged expressions, live control drivers, type-carrier
  templates with nested sub-templates. Assert SY1035 spans match the emitter's set.
- **Parity test** — for a fixture set, assert the analyzer's SY1035 spans equal the generator's
  Error-List SY1035 spans (same input → same highlights).
- **Robustness** — a template that triggers a staging error (e.g. unsupported control shape)
  produces no analyzer highlights and does not throw.
- **Byte-identical guard** — existing generator snapshot tests must stay byte-identical
  (`highlightsOnly` defaults false; generator path untouched).

## Known debt / 1.0 gate

**This analyzer is a workaround and MUST be replaced before 1.0.** Running the entire
classification+emit pass on every live analysis pass, only to discard the generated factory, is
wasteful — liveness classification is fused into the mutating emit walk instead of being a reusable
product.

The proper fix (**"Option C"**): extract a standalone front-end
`Classify(SemanticModel, TemplateInfo) -> { LiveSpans, StagingErrors }` that both the generator
(for SY1035 emission) and the analyzer (for inline highlights) consume. The generator's emit then
*reads* the classification instead of tapping it mid-walk; the analyzer runs only the slim front
half. When C lands, delete `highlightsOnly` (or repoint it) and the "run-and-discard" path.

**Required deliverable:** every workaround surface carries a comment marking it as interim and
pointing at C / this 1.0 gate — the `highlightsOnly` parameter, the `BuildTemplateInfo` seam, and
the `LiveTemplateHighlightAnalyzer` class doc.

## Scope / branch

- Experimental feature work → its own `experimental/*` branch, never `main`.
- In-scope: Seam A, Seam B, the analyzer, tests, workaround comments, the 1.0-gate record.
- Out of scope: changing SY1035 severity/prominence (stays `Info`; inline renders as the faint
  suggestion underline — a separate decision); the Option C extraction itself.

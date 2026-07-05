# Live template code highlighting — design

**Status:** shipped (experimental) — landed on `experimental/live-template-highlight`
**Date:** 2026-07-05
**Feature area:** `Synto.Generators` — templating / staging emitter
**Delivery:** experimental (land on an `experimental/*` branch, not `main`)

**Resolved decisions (as shipped):**
- **O1 (Info-channel plumbing):** the two in-emitter taps (tap 1 in-region holes, tap 3 control drivers)
  append to `StagedRegionEmission.InfoDiagnostics`; tap 2 (depth-zero holes) fires before the emission object
  exists, so it appends to a `ctx.LiveFragmentInfos` accumulator on `TemplateBuildContext`. Both feed a single
  `LiveFragmentMerge.Merge` on the `EmitLiveRegions` success path only (after the error bail), never touching the
  error `Diagnostics` list.
- **O2 (depth-zero boundary):** tap 2 highlights only the *depth-zero reference lists* the four value-lift steps
  compute — not the flat `ctx.Plan.Replacements` map. `[Splice]` value/type-parameter insertions, `[TypeArg]`/
  type-parameter substitutions, and `Template.Splice` inlines are author-written output, not live values, and are
  **not** highlighted.
- **v1 scalar-fold exclusion:** a scalar member-access fold (`xs.Count` → factory-time literal) is a build-time
  evaluation the tap-2 enumeration does not list, so it is deliberately **not** highlighted in v1 (documented
  scoping decision; may be revisited).

## Summary

Surface the build-time / output-world boundary inside a `[Template]` body directly in
the editor. Every fragment the staging emitter treats as **live** — evaluated at factory
(build) time and spliced into, or driving the unroll of, generated output — gets a
low-severity `Info` diagnostic (`SY1035`), rendered as a faint suggestion underline. This
is an X-ray view of Synto's two-world staging model for the template author. It changes no
generated output.

**Correct by construction.** The highlight is emitted as a *byproduct of the staging emit
pass* — from the same per-node decisions that produce the factory output. It therefore
cannot disagree with what is generated: a fragment is highlighted iff the emitter actually
lifts/unrolls it. There is no separate, approximate liveness analysis to drift from reality.

## Motivation

A `[Template]` body silently mixes two worlds: quoted syntax that becomes generated output,
and *live* code (`Parameter<T>()`, `[Unquote]`, bound `Unquote<T>(...)` locals, and
control-flow whose driver is live) that runs at factory-build time. Which is which is
currently invisible — you infer the staging from keywords alone. Making the live regions
visible turns "read the template and infer the staging" into "see the staging."

## Non-goals

- **Custom colors / distinct styling per kind.** Diagnostic appearance is bound to
  *severity*, not to diagnostic ID — two same-severity diagnostics render identically, and
  true classification coloring is a VSIX/IDE-extension surface a NuGet-delivered
  generator/analyzer cannot contribute to. A single uniform `Info` underline is the entire
  visual budget. (This is *why* there is one diagnostic ID, not two.)
- **Highlighting consumer code.** `[Template]` methods live in the generator author's
  project; Synto runs over that project to build the factory. The underlines appear only in
  template-definition source, seen by the template author — never in downstream consumer
  code.

## User-facing behavior

- While editing a `[Template]` method, each live fragment carries an `Info` suggestion
  underline (VS: gray `⋯` under the first char; VS Code: faint underline) plus a
  Messages-list entry.
- On by default; an author who finds it noisy adds
  `dotnet_diagnostic.SY1035.severity = none` to `.editorconfig`.
- No effect on the build, the error list (it is not a warning/error), or generated output.

## Design

### Source of truth — the staging emitter

The highlight is derived from the emitter's own per-node decisions, not from the
conservative `BindingTimeClassifier` pre-pass. The emitter (`StagedRegionEmitter.Emit`,
`src/Synto.Generators/Templating/Staging/StagedRegionEmitter.cs:24`, driven from
`TemplateFactoryBuilder.EmitLiveRegions`) makes the real cut at three discrete, individually
tappable levels. A live fragment is highlighted at each of the three places the emitter
commits a fragment to factory-time evaluation:

1. **In-region splice holes** — a maximal purely-live subexpression embedded into emitted
   region output. Recorded at `CollectLiftPoints` (`StagedScaffoldBuilder.cs:266-276`),
   gated by `IsPurelyStaged` (`:278-317`). `IsPurelyStaged` already yields exactly the
   granularity we want: the *maximal* subexpression that references ≥1 live symbol, contains
   no output-world value, and is not an invocation/object-creation/mutation. For
   `i == c.Ordinal` it selects `c.Ordinal`, **not** the enclosing comparison.
2. **Out-of-region ("depth-zero") splice holes** — a live value used outside any staged
   control region (e.g. a top-level `Parameter<T>()` use). These populate the base
   replacement map via the lift steps (`LiftStagedParameters` / `LiftStagedLocals` /
   `LiftStagedRootParameters` / `LiftQuoteCalls`, `TemplateFactoryBuilder.cs:73-79` →
   `ctx.Plan.Replacements`).
3. **Control drivers** — the iteration source / condition driving a staged unroll (e.g.
   `columns` in `foreach (var c in columns)`), decided at the region-control branch in
   `StagedRegionEmitter.Emit` (`StagedRegionEmitter.cs:50-78`).

Highlighting all three yields "everything live": values spliced into output, values spliced
outside regions, and values driving unrolls. The quoted output — including the bodies of
staged loops — is never highlighted, because the emitter never lifts it.

### Diagnostic descriptor

Add one hand-written `DiagnosticDescriptor` to
`src/Synto.Generators/Templating/Diagnostics/TemplateDiagnostics.cs`, alongside the existing
`SYxxxx` descriptors (all currently `Error`; this is the first `Info`):

| Field | Value |
|---|---|
| Id | `SY1035` (next free id; highest shipped is SY1034) |
| Title | `Live template code` |
| Message | `This template fragment is live: it runs at factory time (build-time) rather than being emitted as output` |
| Category | `Synto.Staging` (new category — keeps informational staging highlights out of the `Synto.Usage` error cluster in the error list) |
| Severity | `DiagnosticSeverity.Info` |
| IsEnabledByDefault | `true` |
| WellKnownDiagnosticTags | none (explicitly **not** `Unnecessary`, which greys code out — wrong semantics for live code) |

A `LiveFragment(Location? location)` factory returns a `DiagnosticInfo`, mirroring the
existing `ImpossibleCut(...)` factory (same `LocationInfo` cacheability discipline). Add the
`SY1035` row to `AnalyzerReleases.Unshipped.md`.

### The emission taps

At each of the three sites above, immediately convert the fragment's location via
`LocationInfo.CreateFrom(node.GetLocation())` into a `TemplateDiagnostics.LiveFragment`
`DiagnosticInfo` and add it to the emission's Info-diagnostic collection. The primary tap is
a single line beside the hole-recording at `StagedScaffoldBuilder.cs:270`; the depth-zero and
control-driver taps mirror it at their respective sites.

### The bail-condition wrinkle

`EmitLiveRegions` currently treats *any* emission diagnostic as fatal:
`if (liveRegionEmission.Diagnostics.Count > 0) return false;`
(`TemplateFactoryBuilder.cs:935`). That is safe today because every emission diagnostic is an
`Error`. Adding `Info` diagnostics to the same list would wrongly abort the factory.

**Resolution:** carry the `SY1035` Info diagnostics on a **separate list** on
`StagedRegionEmission` (e.g. `InfoDiagnostics`), distinct from the error-bearing
`Diagnostics`. The existing error-bail check at `:935` stays untouched; the Info list is
merged into `ctx.Diagnostics` on the success path only (next to the existing
`ctx.Diagnostics.AddRange(...)` at `:931`). This keeps error semantics and cacheability
flow-out identical to today.

### Cap and ordering

- Emit at most **100** `SY1035` diagnostics per template body — a safety valve only. With
  the emitter's tight `IsPurelyStaged` granularity this rarely fires (far fewer nodes than a
  "contains-a-live-identifier" over-approximation would produce).
- When capping, take fragments in **document order** (ascending span start) so truncation is
  deterministic across builds (snapshot and incremental-cache hygiene).
- **Silent truncation** at the cap — no summary "N more" diagnostic in this version.

### Cacheability

Emission follows the existing SY1013 pattern exactly: syntax locations are converted to
equatable `LocationInfo` at emit time and carried as `DiagnosticInfo`. The emit pass holds a
`SemanticModel` (`StagedEmitContext.SemanticModel`) but it is never stored — only the
`LocationInfo` is. No `SyntaxNode`, `ISymbol`, `SemanticModel`, or `Compilation` is captured
into pipeline state. `CacheabilityAssert.AllStepsCachedOrUnchanged` must stay green.

## Testing

- **Granularity tests** (mirror `test/Synto.Test/Templating/StagingDiagnosticsTest.cs`):
  assert `SY1035` locations for a template covering (a) a `Parameter<T>()` used in a nested
  expression — highlight the parameter use, not the enclosing expression; (b) an in-region
  splice like `c.Ordinal` inside `i == c.Ordinal` — highlight `c.Ordinal` only; (c) a staged
  `foreach`/`while`/`if` driver — highlighted; (d) the staged loop *body*'s quoted output —
  **not** highlighted; (e) a loop-variable-dependent expression inside a staged body —
  highlighted (proves the loop-variable case works, since the emitter's live set tracks it).
- **Bail-isolation test:** a template that produces both an error emission diagnostic and
  live fragments still bails on the error (Info list does not suppress the error path); a
  clean template surfaces the Info diagnostics.
- **Cap test:** a template with >100 live fragments produces exactly 100 diagnostics at the
  100 lowest-span-start locations (deterministic).
- **Cacheability:** the existing cacheability guard stays green with the new taps.
- **No-output-change:** existing snapshot outputs remain byte-identical (the taps add
  diagnostics only; they do not alter emitted syntax).

## Open questions / future enhancements

None blocking. Later, if wanted: a "N more suppressed" summary diagnostic when the cap fires;
splitting into two ids (spliced value vs. control driver) only if independent suppression is
ever requested (non-breaking to add later).

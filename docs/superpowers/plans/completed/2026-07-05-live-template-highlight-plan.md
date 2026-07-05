# Live template code highlighting (SY1035) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL — use `superpowers:subagent-driven-development` (recommended) or
> `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax. Each
> task is an independently green unit (builds + tests pass on its own) ending in a commit. Use
> `superpowers:test-driven-development` inside every task: write the failing test first, then the implementation,
> then confirm green.

**Design:** `docs/superpowers/specs/2026-07-05-live-template-highlight-design.md` (approved).
**Branch:** work continues on `experimental/live-template-highlight` — an **experimental feature; never `main`**.
**Delivery:** experimental. Nothing here changes generated output; it adds an `Info` diagnostic only.

**Goal:** Ship `SY1035` — an `Info`-severity diagnostic that highlights every *live* (factory-time) fragment in a
`[Template]` body, sourced **from the staging emit pass** so it is correct by construction (a fragment is
highlighted iff the emitter actually lifts/unrolls it). Three tap sites (in-region splice holes, out-of-region
"depth-zero" holes, control drivers), a separate Info-diagnostic channel that must not perturb the existing
error-bail, a deterministic 100-fragment cap, and byte-identical generated output.

**Tech stack:** C# / Roslyn incremental source generators; runtime carriers `netstandard2.0`; xUnit v3 +
Microsoft.Testing.Platform; Verify snapshots. Diagnostic id prefix `SY` (`Incremental.Diagnostics.IdPrefix`).

---

## Global constraints

- **Cacheability is sacred.** Every fragment location is converted to the value-equatable `LocationInfo` via
  `LocationInfo.CreateFrom(node.GetLocation())` **at emit time**, carried as an equatable
  `DiagnosticInfo` exactly like `SY1013`. **No** `SyntaxNode`, `ISymbol`, `SemanticModel`, or `Compilation` may
  enter any equatable/cached pipeline value. `CacheabilityAssert.AllStepsCachedOrUnchanged` (`test/Synto.Test/CacheabilityAssert.cs:38`)
  must stay green (Task 7 verifies).
- **Runtime/generator layering.** All work is inside `src/Synto.Generators` (the generator) + `test/Synto.Test`.
  No change to `src/Synto.Core` (the consumer/runtime surface). No new public API, no attribute, no marker.
- **Correct by construction — no second liveness analysis.** Do **not** re-derive liveness from
  `BindingTimeClassifier`. Every highlight is emitted as a byproduct at the three points the emitter already
  commits a fragment to factory-time evaluation. If the emitter does not lift it, it is not highlighted.
- **Byte-identical output.** Taps add `DiagnosticInfo`s only; they never touch emitted syntax. All existing
  snapshots across all projects must remain byte-identical (Task 7). If any `.g.cs` snapshot moves, STOP — a tap
  wrongly touched output.
- **The Info channel must never abort the factory.** `EmitLiveRegions` bails on *any* entry in
  `StagedRegionEmission.Diagnostics` (`TemplateFactoryBuilder.cs:935`). `SY1035` Info diagnostics MUST NOT flow
  through that list. They travel on a **separate** channel merged into `ctx.Diagnostics` on the **success path
  only** (Task 2). Error semantics stay byte-for-byte as today.
- **Green gate (every task):** `rtk dotnet build`, `rtk dotnet test`, `rtk dotnet format whitespace --verify-no-changes`
  for the touched projects. A task is not done until all three are green.
- **Test invocation note (SDK 10 MTP):** run a single project/filter with raw
  `dotnet test --project <csproj> --filter-class <Fully.Qualified.TestClass>` (or `--filter-method`). Do **not**
  use `rtk dotnet test --filter …` — it mangles MTP args (project memory: *dotnet test MTP invocation*).
- **No git pre-commit hooks.** Format via `dotnet format whitespace` + CI; do not add hooks.

---

## Verified code anchors (checked against HEAD on this branch)

| Anchor | Location | Notes |
|---|---|---|
| Diagnostic descriptors | `src/Synto.Generators/Templating/Diagnostics/TemplateDiagnostics.cs` | all existing SY-ids are `Error`; SY1035 is the first `Info`. `IdPrefix = Incremental.Diagnostics.IdPrefix` (`:11`). |
| Shape to mirror | `TemplateDiagnostics.ImpossibleCut` descriptor `:128-133`, factory `:135-140` | descriptor + `DiagnosticInfo` factory pattern. |
| `DiagnosticInfo` / `LocationInfo` | `src/Synto.Generators/Incremental/DiagnosticInfo.cs` (namespace `Synto.Generators`) | `LocationInfo.CreateFrom` `:14`; `DiagnosticInfo.ToDiagnostic` `:30-37`. **Drift note:** the design cited "`DiagnosticInfo.cs:30-37`" with no path; the file lives under `Incremental/`, namespace `Synto.Generators` (visible unqualified from `TemplateDiagnostics`). |
| Analyzer release tracking | `src/Synto.Generators/AnalyzerReleases.Unshipped.md` (highest listed: `SY1034`, `:40`); `AnalyzerReleases.Shipped.md` is empty (pre-release) | add the `SY1035` row to **Unshipped**. |
| Staging emitter entry | `src/Synto.Generators/Templating/Staging/StagedRegionEmitter.cs:24` (`Emit`) | driven from `EmitLiveRegions`. |
| Driver | `TemplateFactoryBuilder.EmitLiveRegions` `:917`; `StagedRegionEmitter.Emit` call `:919`; `ctx.Diagnostics.AddRange(...)` `:931`; **error bail** `:935` | success-path merge point is right after `:935`. |
| Tap 1 — in-region holes | `StagedScaffoldBuilder.CollectLiftPoints` `:266-276` (record site `:270`), gated by `IsPurelyStaged` `:278-317` | maximal purely-live subexpression granularity (e.g. `c.Ordinal`, not `i == c.Ordinal`). |
| Tap 2 — depth-zero holes | value-lift steps `TemplateFactoryBuilder.cs:73-79`; the depth-zero reference lists are `depthZeroReferences` (`:538`, added `:558-559`), `liveLocalDepthZeroReferences` (`:593`, added `:612-613`), and the equivalents in `LiftStagedRootParameters` (~`:701`) and `LiftQuoteCalls` (~`:807`) | **Drift note:** the design listed the four steps at `:73-79`; line `:78` is actually `LiftQuoteParameters` and `:79` is `LiftQuoteCalls`. Tap 2's live-hole set = the *depth-zero* reference lists these steps compute, NOT the whole `ctx.Plan.Replacements` map (which also holds `[TypeArg]`/type-param subs `:260`, `[Splice]`-param subs `:282`, scalar folds `:511`, `Template.Splice` inlines `:884` — see Open decision O2). |
| Tap 3 — control drivers | region-control branch in `StagedRegionEmitter.Emit` `:50-78` (region matched at `:55`) | driver = `foreach` source / `for`\|`while`\|`if` condition (pre-rename node). |
| Info channel container | `StagedRegionEmission` (`StagedRegion.cs:38-43`); error list `Diagnostics` is `List<DiagnosticInfo>` `:42` | add the separate `InfoDiagnostics` channel here (Task 2). |
| Cacheability guard | `test/Synto.Test/CacheabilityAssert.cs:38` (`AllStepsCachedOrUnchanged`) | |
| IVT for unit tests | `src/Synto.Generators/Synto.Generators.csproj:26` (`InternalsVisibleTo Include="Synto.Test"`) | lets tests call `TemplateDiagnostics.LiveFragment(...)` and inspect the descriptor directly. |
| Test convention | `test/Synto.Test/Templating/StagingDiagnosticsTest.cs` (end-to-end id/span asserts); `BindingTimeClassifierTest.cs` (partition style) | |

No anchor was materially wrong; the two drift notes above (DiagnosticInfo path/namespace; the exact identity of the two lift steps at `:78-79`) are the only corrections.

---

## The contract (pin behaviors, not bodies)

Implementers finalize exact `SyntaxFactory`/method bodies. The binding contract is:

- **Descriptor `SY1035`** — Id `SY1035`; Title `Live template code`; Message
  `This template fragment is live: it runs at factory time (build-time) rather than being emitted as output`;
  **Category `Synto.Staging`** (new); Severity `DiagnosticSeverity.Info`; `IsEnabledByDefault: true`; **no**
  `WellKnownDiagnosticTags` (explicitly not `Unnecessary`).
- **Factory** — `TemplateDiagnostics.LiveFragment(Location? location)` returns a `DiagnosticInfo` (no message
  args), mirroring `ImpossibleCut(...)`'s `LocationInfo.CreateFrom` discipline.
- **Info channel** — a collection separate from `StagedRegionEmission.Diagnostics`, carrying `SY1035`
  `DiagnosticInfo`s. It is merged into `ctx.Diagnostics` **only on the `EmitLiveRegions` success path** (after the
  `:935` error bail). The error list and its bail are untouched.
- **Three taps** produce one `LiveFragment` per committed live fragment:
  1. **In-region hole** — at `CollectLiftPoints` `:270`, the maximal purely-live subexpression being lifted.
  2. **Depth-zero hole** — each reference node in the four value-lift steps' depth-zero reference lists.
  3. **Control driver** — the live driving expression of each unrolled region (source/condition), pre-rename.
- **Dedupe** — the merged set is deduped by location span (`(FilePath, TextSpan)`): at most one `SY1035` per
  source span (see Risk R1).
- **Cap + ordering** — after dedupe, order by span start ascending (tie-break span end ascending), take the first
  **100**, silently truncate the rest. Deterministic across builds.
- **Merge site** — dedupe + order + cap happen once, at the success-path merge in `EmitLiveRegions`.

### Open decisions (settle in-task; do not pre-bake)

- **O1 — Info-channel plumbing shape.** Design mandates the channel live on `StagedRegionEmission`
  (`InfoDiagnostics`). Taps 1 & 3 fire *inside* `StagedRegionEmitter.Emit`, so they append there directly. Tap 2
  fires in the value-lift steps that run *before* `Emit` (the emission object does not yet exist). Two acceptable
  carriers for tap 2's fragments: **(a)** a `ctx`-level accumulator (e.g. `ctx.LiveFragmentInfos`) that
  `EmitLiveRegions` concatenates with `emission.InfoDiagnostics` before dedupe/cap; **(b)** thread the depth-zero
  live-hole node collection into `StagedRegionEmitter.Emit` and let the emitter convert them. **Default: (a)** —
  it keeps the lift steps' one-line taps local and avoids widening `Emit`'s signature. Whichever is chosen, the
  single dedupe/order/cap runs once at the merge site.
- **O2 — depth-zero set boundary.** Tap 2 highlights **only** the live *value* holes the four value-lift steps
  compute (`depthZeroReferences` etc.). It must **not** highlight: `[TypeArg]`/type-parameter substitutions
  (`:260`), verbatim `[Splice]`-parameter substitutions (`:282`), or `Template.Splice(node)` inlines (`:884`) —
  those insert author-written output syntax, not a factory-time value. **Scalar member-access folds** (`:511`,
  e.g. `columns.Count` → literal) *are* build-time evaluations; the spec's tap-2 enumeration does **not** list
  them, so v1 leaves them **unhighlighted** — recorded here as a deliberate scoping decision. Do not iterate the
  flat `ctx.Plan.Replacements` map for tap 2 (it mixes all of the above); tap at the depth-zero reference lists.

### Risks the spec did not fully close

- **R1 — cross-tap double count.** By construction the three taps are largely disjoint (tap 2 excludes
  region-consumed refs via `!ctx.RegionConsumedNodes.Contains(...)`; tap 1 walks only region *bodies*; tap 3 owns
  the *control header*). But nested spans / a driver that is itself a lifted value could collide. The
  dedupe-by-span contract is the safety net and is mandatory (Task 6), not optional.
- **R2 — existing "no diagnostics" tests.** Several templating tests assert `Assert.Empty(runResult.Diagnostics)`
  **unfiltered** (`ChildTemplateTest.cs:210,321`; `DecorationTests.cs:95,118`; `GenericTemplateAttributeTest.cs:50`;
  `SimpleTemplateTest.cs:438`). Any of those templates that contain live fragments will now legitimately surface
  `SY1035` and the assertion will fail. Task 7 audits each and narrows the assertion to
  `Severity == DiagnosticSeverity.Error` (or asserts the expected `SY1035` set) where the template is live — this
  is not a regression, it is the feature. Most other templating tests already filter by `Error` and are unaffected.

---

## Task 1 — `SY1035` descriptor + `LiveFragment` factory + `Synto.Staging` category + release row

**Goal:** Land the descriptor, its `DiagnosticInfo` factory, the new `Synto.Staging` category, and the analyzer
release row. No tap, no behavior change — a clean standalone unit proved by a direct descriptor unit test (IVT).

**Files:**
- Modify `src/Synto.Generators/Templating/Diagnostics/TemplateDiagnostics.cs` — add the `_liveFragment`
  `DiagnosticDescriptor` (fields per the contract; `DiagnosticSeverity.Info`; category literal `"Synto.Staging"`;
  no tags) and a `public static DiagnosticInfo LiveFragment(Location? location)` mirroring `ImpossibleCut` `:135-140`
  (empty `MessageArgs`).
- Modify `src/Synto.Generators/AnalyzerReleases.Unshipped.md` — add row `SY1035 | Synto.Staging | Info | Diagnostics`
  (mandatory: RS2008 release-tracking would otherwise warn/error under the project's warning gate).
- Create `test/Synto.Test/Templating/LiveFragmentDiagnosticTest.cs` — a direct unit test using IVT.

**Test (write first, red):**
- Assert `LiveFragment(someLocation).Descriptor` has `Id == "SY1035"`, `DefaultSeverity == DiagnosticSeverity.Info`,
  `Category == "Synto.Staging"`, `IsEnabledByDefault`, and `WellKnownDiagnosticTags` empty (no `Unnecessary`).
- Assert the returned `DiagnosticInfo.Location` is the `LocationInfo` of the passed location (round-trips via
  `ToDiagnostic()` to a real span), and `ToDiagnostic().Id == "SY1035"`.

**Acceptance:** the unit test passes; `rtk dotnet build` clean (RS2008 satisfied); no snapshot moves anywhere;
green gate for `src/Synto.Generators` + `test/Synto.Test`.

**Commit:** `feat(templating): add SY1035 "Live template code" Info descriptor (Synto.Staging category)`
(footer: `Plan: docs/superpowers/plans/2026-07-05-live-template-highlight-plan.md` / `Plan-Tasks: 1`).

---

## Task 2 — separate Info channel + bail isolation + the merge/dedupe/cap primitive

**Goal:** Add the `SY1035` Info channel distinct from the error `Diagnostics`, and the success-path merge that
dedupes, orders, and caps. No tap yet — the channel is empty, so behavior is unchanged. Prove the error-bail is
untouched, and prove the merge primitive (dedupe / ascending order / 100-cap) as a pure unit.

**Files:**
- Modify `src/Synto.Generators/Templating/Staging/StagedRegion.cs` — add `List<DiagnosticInfo> InfoDiagnostics`
  to `StagedRegionEmission` (next to `Diagnostics` `:42`).
- Modify `src/Synto.Generators/Templating/Emit/TemplateFactoryBuilder.cs` — in `EmitLiveRegions`, **after** the
  `:935` error bail (success path only), build the combined Info set (per O1: `emission.InfoDiagnostics` plus any
  `ctx` depth-zero accumulator), run the dedupe/order/cap primitive, and `ctx.Diagnostics.AddRange(...)` the
  result. The existing `AddRange` of error diagnostics `:931` and the bail `:935` stay exactly as-is. If O1(a)
  chosen, add the `ctx.LiveFragmentInfos` accumulator field to `TemplateBuildContext` (append-only scratch; not
  equatable — matches the existing mutable-scratch discipline).
- Add the dedupe/order/cap primitive as an internal static helper (co-located near the emitter or as a small
  `LiveFragmentMerge` helper) so it is unit-testable via IVT.
- Modify `test/Synto.Test/Templating/LiveFragmentDiagnosticTest.cs` (or a sibling) — unit tests for the primitive.

**Tests (write first, red):**
- **Primitive unit test:** feed a synthetic `IEnumerable<DiagnosticInfo>` with (a) two entries sharing one span
  → one survives; (b) out-of-order spans → output ascending by span start (tie-break span end); (c) 150 distinct
  spans → exactly 100 survive, and they are the 100 lowest span-starts.
- **Bail-isolation (end-to-end, mirror `StagingDiagnosticsTest`):** a template that triggers an error emission
  diagnostic (e.g. the nested-region `SY1014` case from `StagingDiagnosticsTest.UnsupportedStagedShape_ReportsSY1014`)
  still reports `SY1014` and the factory bails; assert **no** `SY1035` is emitted on the error path (there are none
  yet, but this locks the invariant before taps exist).

**Acceptance:** primitive + bail tests pass; every existing test still green; no snapshot moves. Green gate.

**Commit:** `refactor(templating): add SY1035 Info channel + success-path merge/dedupe/cap (no taps yet)`.

---

## Task 3 — Tap 1: in-region splice holes (`CollectLiftPoints`)

**Goal:** Highlight each maximal purely-live subexpression the emitter lifts inside a staged region body. This is
the primary tap and yields the design's target granularity.

**Files:**
- Modify `src/Synto.Generators/Templating/Staging/StagedScaffoldBuilder.cs` — at the hole record in
  `CollectLiftPoints` (`:270`, inside the `IsPurelyStaged` branch), also append
  `TemplateDiagnostics.LiveFragment(expression.GetLocation())` to `emission.InfoDiagnostics`
  (reachable via `StagedEmitContext.Emission`; thread it into `CollectLiftPoints` if not already in scope — it is
  called from `TryBuildRegionBody` which has `context`).
- Create `test/Synto.Test/Templating/LiveTemplateHighlightTest.cs` — the durable granularity harness (model on
  `StagingDiagnosticsTest`: `RunAndGetDiagnostics` + span assertions).

**Tests (write first, red) — assert `SY1035` spans:**
- In-region `c.Ordinal` inside `i == c.Ordinal` (a staged `foreach (var c in cols)` whose body uses
  `i == c.Ordinal`, `i` a runtime parameter): a `SY1035` covers **`c.Ordinal`** exactly, **not** the enclosing
  comparison, and not `i`.
- The staged loop **body's quoted output** (a plain quoted statement with no live subexpression) carries **no**
  `SY1035`.
- A loop-variable-dependent live expression inside a staged body is highlighted (proves the emitter's live set
  tracks the loop variable).

**Acceptance:** granularity tests pass; no `.g.cs` snapshot moves; green gate.

**Commit:** `feat(templating): SY1035 tap 1 — highlight in-region live splice holes`.

---

## Task 4 — Tap 2: out-of-region (depth-zero) holes

**Goal:** Highlight each depth-zero live-value hole (a live root used outside any staged region). Per O2, tap the
four value-lift steps' depth-zero reference lists — not the flat replacement map.

**Files:**
- Modify `src/Synto.Generators/Templating/Emit/TemplateFactoryBuilder.cs` — at each of the four value-lift sites,
  for each node in the depth-zero reference list (`depthZeroReferences` `:538`; `liveLocalDepthZeroReferences`
  `:593`; the equivalents in `LiftStagedRootParameters` ~`:701` and `LiftQuoteCalls` ~`:807`), append
  `TemplateDiagnostics.LiveFragment(reference.GetLocation())` to the tap-2 carrier chosen in O1 (default:
  `ctx.LiveFragmentInfos`). Do **not** tap the `[TypeArg]`/`[Splice]`/`Template.Splice`/scalar-fold replacement
  sites (`:260`, `:282`, `:884`, `:511`).
- Modify `test/Synto.Test/Templating/LiveTemplateHighlightTest.cs` — add depth-zero cases.

**Tests (write first, red):**
- A top-level `Parameter<T>()` used inside a **nested** expression (e.g. `M(p + 1)` where `p = Parameter<int>()`):
  the `SY1035` covers the **parameter use** node, not the enclosing expression.
- A region-less template that only uses depth-zero live values still emits `SY1035` (confirms `EmitLiveRegions`
  runs and merges even with zero staged regions).
- **Negative (O2):** a `[Splice]`/`Template.Splice` insertion and a `[TypeArg]`/type-param substitution produce
  **no** `SY1035` (they are not live *values*). A scalar-fold site (`columns.Count`) produces no `SY1035` in v1
  (documented scoping decision).

**Acceptance:** depth-zero + negative tests pass; no snapshot moves; green gate.

**Commit:** `feat(templating): SY1035 tap 2 — highlight depth-zero live holes`.

---

## Task 5 — Tap 3: control drivers

**Goal:** Highlight the live driving expression of each unrolled region (the iteration source / condition).

**Files:**
- Modify `src/Synto.Generators/Templating/Staging/StagedRegionEmitter.cs` — at the region-control match (`:55`,
  where `regionByControl.TryGetValue(statement, out var region)` succeeds), append a `LiveFragment` for the
  driver of `region.Control` (the **pre-rename** node): `foreach` → `.Expression`; `while`/`if` → `.Condition`;
  `for` → `.Condition` (and, if desired for completeness, initializers/incrementors — v1 minimum is the condition/
  source, matching "the driver"). Append to `emission.InfoDiagnostics`. Confirm this driver node is not already
  covered by tap 1 (it is the control header, which tap 1 never visits) — R1 dedupe still applies.
- Modify `test/Synto.Test/Templating/LiveTemplateHighlightTest.cs` — add control-driver cases.

**Tests (write first, red):**
- `columns` in `foreach (var c in columns)` (a staged unroll) carries a `SY1035`; the loop **body's** quoted
  output does not.
- A live `while`/`if` condition driving an unroll carries a `SY1035`.

**Acceptance:** control-driver tests pass; no snapshot moves; green gate.

**Commit:** `feat(templating): SY1035 tap 3 — highlight live control drivers`.

---

## Task 6 — cap, deterministic ordering, and cross-tap dedupe (harden + prove)

**Goal:** Lock the end-to-end cap/order/dedupe behavior now that all three taps feed the merge. (The primitive
landed in Task 2; this task proves it under real multi-tap emission and closes R1.)

**Files:**
- Modify `test/Synto.Test/Templating/LiveTemplateHighlightTest.cs` — add cap + dedupe + ordering cases.
- Adjust the merge primitive only if a real cross-tap collision is observed (else no source change).

**Tests (write first):**
- **Cap:** a template that produces **>100** live fragments emits **exactly 100** `SY1035`s, located at the 100
  lowest span-starts, deterministically (run twice, identical set/order).
- **Dedupe (R1):** if any construct is found where two taps target the same span, exactly one `SY1035` results.
  (If none can be constructed, add a primitive-level test that two `DiagnosticInfo`s with equal `LocationInfo`
  collapse to one — already in Task 2; reference it and note no natural end-to-end collision exists.)
- **Ordering:** for a template with several live fragments, `SY1035` spans come back in ascending document order.

**Acceptance:** cap/order/dedupe tests pass; green gate.

**Commit:** `test(templating): SY1035 cap/order/dedupe — deterministic 100-fragment truncation`.

---

## Task 7 — cacheability, byte-identical snapshots, existing-test audit, spec + memory

**Goal:** Prove the sacred invariants and reconcile the tree: incremental cacheability green, all `.g.cs`
snapshots byte-identical, the unfiltered "no diagnostics" tests (R2) reconciled, and the spec/memory updated.

**Files:**
- Modify (R2 audit) any of `test/Synto.Test/Templating/ChildTemplateTest.cs` (`:210,321`),
  `DecorationTests.cs` (`:95,118`), `GenericTemplateAttributeTest.cs` (`:50`), `SimpleTemplateTest.cs` (`:438`)
  whose template is live: narrow `Assert.Empty(runResult.Diagnostics)` to
  `Assert.Empty(runResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))`, or assert the exact
  expected `SY1035` set. Templates with no live fragments stay unchanged.
- Modify `docs/superpowers/specs/2026-07-05-live-template-highlight-design.md` — status → shipped (experimental);
  record the resolved O1/O2 decisions and the v1 scalar-fold exclusion.
- Modify `C:\Users\redoz\.claude\projects\C--dev-Synto\memory\MEMORY.md` (+ pointer note) — one fact: "SY1035
  live-template highlighting shipped on `experimental/live-template-highlight` (Info, `Synto.Staging`; three
  emitter taps; separate Info channel off the error-bail; deterministic 100-cap; byte-identical output)."

**Tests / verification:**
- `CacheabilityAssert.AllStepsCachedOrUnchanged` green for the templating generator (run the existing
  incremental guard tests, e.g. `SpliceMemberGeneratorTest`'s incremental case and the ObjectReader
  `ObjectReaderIncrementalTests`). If red, a non-equatable value leaked — STOP, switch to
  `superpowers:systematic-debugging`; the fix is to ensure only `LocationInfo`/`DiagnosticInfo` cross into
  pipeline state.
- **Full-suite byte-identical check:** `rtk dotnet test` across all projects; confirm **zero** `.received.cs`
  snapshot diffs (taps add diagnostics only). Any moved `.g.cs` snapshot is a bug — STOP.
- Re-run the whole suite after the R2 audit; all green.

**Acceptance:** cacheability guard green; no snapshot churn; all tests green; spec + memory updated; final green gate.

**Commit:** `docs(spec,memory): mark SY1035 live-template highlighting shipped; reconcile diagnostic-assert tests`.

---

## Self-review

1. **Spec coverage.** Descriptor fields + `Synto.Staging` category + no-`Unnecessary` tag → Task 1. `LiveFragment`
   factory mirroring `ImpossibleCut` → Task 1. Separate Info list off the error-bail, merged success-path only →
   Task 2 (bail-isolation test). Three taps at the exact emitter sites → Tasks 3/4/5 with the spec's granularity
   tests (`c.Ordinal` not the comparison; body-output not highlighted; loop-variable case; depth-zero parameter
   use; control driver). 100-cap + document-order determinism → Tasks 2 (primitive) + 6 (end-to-end). Cacheability
   + byte-identical → Task 7. AnalyzerReleases row → Task 1.
2. **Contract-first.** Behaviors/contracts/interfaces are pinned (descriptor fields, factory signature, channel
   separation, tap sites, dedupe/order/cap); no method bodies prescribed. The two O-decisions (channel plumbing,
   depth-zero boundary) are settled in-task, not pre-baked.
3. **Beyond the spec.** Surfaced: R1 (cross-tap double-count → mandatory dedupe-by-span), R2 (unfiltered
   `Assert.Empty(...Diagnostics)` tests that will break → audited in Task 7), O2 (the flat replacement map mixes
   non-live entries; tap the depth-zero lists instead; scalar folds deliberately unhighlighted in v1), and O1
   (tap-2 fires before the emission object exists → `ctx` accumulator default).
4. **Anchors.** Verified against HEAD; two minor drifts corrected (DiagnosticInfo path/namespace; the identity of
   the lift steps at `:78-79`). No anchor was load-bearingly wrong.
5. **Workflow.** Seven ordered, independently-green TDD tasks, each test-first, each ending in a conventional
   commit with `Plan:`/`Plan-Tasks:` trailers. Branch discipline: `experimental/live-template-highlight`, never
   `main`.

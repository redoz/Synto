# Synto internal DLL layering + single-package delivery

**Status:** design (approved for planning)
**Date:** 2026-07-15
**Category:** Synto packaging / project structure
**Gates:** `[SyntoModel]` plan and cacheable-diagnostics plan both build on this — land this first.

## Problem

Two in-flight features — `[SyntoModel]` (symbol→model) and cacheable diagnostics for consumers —
both need the same injected **cacheable trio** in the consumer compilation: `EquatableArray<T>`,
`DiagnosticInfo`, `LocationInfo`. Today that trio is:

- **duplicated** as generator-internal copies (`Synto.Generators/Incremental/EquatableArray.cs` and
  `Synto.Diagnostics/EquatableArray.cs`, byte-identical bar namespace), and
- **injected** into consumers only by the `Synto` package (`Synto.Generators` embeds
  `Incremental/EquatableArray.cs` + `DiagnosticInfo.cs` under `LogicalName="Synto.Runtime.*"`;
  `SurfaceInjectionGenerator` post-init `AddSource`s them as `internal`).

If a second package (`Synto.Diagnostics`) also injected the trio, a consumer referencing **both** would
get two `internal Synto.Generators.EquatableArray<T>` definitions → **CS0101**. And cacheable
diagnostics' headline use-case (`ModelResult(TypeModel.From(symbol), bag.ToEquatableArray())`)
references both features. So the collision is the main path, not a corner case.

The deeper question this surfaced: **why multiple packages at all?** Synto's generator packages leave
**zero trace** in the consumer's shipped output — they inject `internal` source and ship no runtime
assembly (`Synto.Core.dll` is only an analysis-time reference, `PrivateAssets=all`). So a package split
buys only à-la-carte + product identity, while costing the entire collision + version-lockstep problem.

## Decision

Two axes, decided independently:

### Delivery — ONE published generator package

Ship **one** generator nupkg (`Synto`). `Synto.Core` stays a separate published **runtime lib** (the one
non-zero-trace artifact — consumers reference + execute it directly). `Synto.Diagnostics` is **retired as
a separately published package**; `[Diagnostic]` becomes a `Synto` feature exactly like
`[Template]`/`[Match]`/`[SyntoModel]`.

Rationale: zero-trace ⇒ bundling costs nothing shipped; one package ⇒ **one injector ⇒ CS0101 impossible
by construction**, no cross-package version skew, simplest consumer story. Idle generators are ~free
(`ForAttributeWithMetadataName` visits no syntax when its attribute is absent). À-la-carte was not a
committed product goal. **Reversible:** splitting to multi-package later is a pack-flag change plus the
single-injector invariant (already held) — not paid for now (YAGNI).

### Internal structure — one DLL per part

Delivery = one nupkg does **not** mean one assembly. Split the internals into **one DLL per part** for
compile-**enforced** module boundaries (`Synto.Match.dll` literally cannot reach into
`Synto.Template.dll` without a project reference — folders don't enforce that; assemblies do). All feature
DLLs are packed under `analyzers/dotnet/cs/` in the single `Synto` nupkg via the existing
`SyntoGetDependencyTargetPaths` multi-assembly pack target.

## Layering (arrows = depends-on, all generator DLLs are netstandard2.0 analyzers)

```
Synto.GenKit          ← generator-authoring plumbing: SurfaceInjectorBase, PublicToInternalRewriter,
   (no Synto deps)       resource scan, + shared dep-free syntax/naming helpers (UsingDirectiveSet,
                         NameAllocator, SyntaxFormatter, ToGeneratedFilename). BOTTOM of the stack.
      ▲
Synto.Incremental     ← the cacheable CONSUMER data surface: EquatableArray / DiagnosticInfo /
                         LocationInfo + DiagnosticBag / Drain / ToEquatableArray, + the ONE injector
                         of that trio (a SurfaceInjectorBase subclass), + shared generator
                         GenerationResult/Emit scaffolding*. refs GenKit.
      ▲
Synto.Template  Synto.Match  Synto.Model  Synto.Diagnostics
                         one generator each: finder/emit + its OWN surface source + its OWN surface
                         injector subclass. refs GenKit (base) + Incremental (trio their emitted
                         code/diagnostics use). Features do NOT reference each other.
      ▲
Synto (umbrella, PackageId=Synto)   ← refs all feature DLLs; pack target bundles them into one nupkg.

Synto.Core            ← runtime lib. SEPARATE axis (consumer-referenced + executed, not build plumbing).
```

\* The `{ string? FileName, string? Source, EquatableArray<DiagnosticInfo> Diagnostics }` result + the
`Emit`-reports-diagnostics-then-`AddSource` shape are copy-pasted across
`TemplateFactorySourceGenerator` / `MatchFactorySourceGenerator` / `DiagnosticsGenerator` (and the new
`SyntoModelGenerator`). Extract a shared base — it lands in **`Synto.Incremental`** (not GenKit) because it
references `DiagnosticInfo`, keeping GenKit dependency-free at the bottom.

## Key mechanisms

### Single trio injector; per-feature marker injectors — no collision

Generalize today's `SurfaceInjectionGenerator` (which scans `typeof(SurfaceInjectionGenerator).Assembly`)
into a `SurfaceInjectorBase` in `Synto.GenKit` that scans **`GetType().Assembly`** — the *deriving*
assembly's `Synto.Runtime.*` resources. Then:

- Each feature DLL embeds only **its own** marker surface and carries a 3-line
  `[Generator] class TemplateSurfaceInjector : SurfaceInjectorBase {}` that injects it.
- The **trio** is injected by exactly **one** subclass, in `Synto.Incremental`.

N injectors, **disjoint** injected type-sets (Template markers ≠ Match markers ≠ trio) → no CS0101, even
though several injectors run in one consumer compilation. This invariant — *only `Synto.Incremental`
injects the trio* — is what keeps the single-package delivery collision-proof **and** a future split
reversible (a multi-package world ships `Synto.Incremental.dll` once, shared transitively, still one
injector).

### Consolidate the trio

Retire the duplicate generator-internal `EquatableArray`/`DiagnosticInfo` copies onto the single
`Synto.Incremental` definitions (referenced by every generator DLL at analysis time). The consumer-facing
injected copy is the same single definition. **No third copy.**

Injected **namespace**: keep `Synto.Generators` for the injected trio for now (zero re-pointing of the
existing template/match generated references). Moving to a neutral namespace (`Synto`) is a separable,
low-value churn decision — deferred.

## Migration order (each step green-gateable)

1. **`Synto.GenKit`** — new DLL. Move `SurfaceInjectionGenerator` → `SurfaceInjectorBase`
   (`GetType().Assembly` scan) + `PublicToInternalRewriter`; move the dep-free syntax/naming helpers.
   Existing injected-surface snapshots stay byte-identical.
2. **`Synto.Incremental`** — new DLL. Move the trio + add `DiagnosticBag`/`Drain`/`ToEquatableArray`
   + the single trio injector + shared `GenerationResult`/`Emit` base. Consolidate the duplicate copies.
3. **Split feature DLLs** — carve `Synto.Template` / `Synto.Match` / `Synto.Model` / `Synto.Diagnostics`
   out of today's `Synto.Generators` (+ existing `Synto.Diagnostics`), each self-contained, each with its
   own surface injector subclass. Snapshots byte-identical throughout.
4. **Umbrella `Synto`** — `PackageId=Synto`, refs all feature DLLs; extend `SyntoGetDependencyTargetPaths`
   to pack every analyzer DLL + `Synto.GenKit.dll`/`Synto.Incremental.dll` once, in the same analyzer load
   context. Retire `Synto.Diagnostics` as a separate published package.
5. Guard: `ResourceManifestPinTest` / `SurfaceInjectionTest` updated per moved resources; full suite +
   examples green, 0 warnings (`TreatWarningsAsErrors` global).

## Reuse discipline

The `SurfaceInjectorBase` generalization is the *genuine* shared-seam extraction (not an `isX` flag):
today's single injector becomes a base whose per-assembly resource scan serves every feature identically.
The trio consolidation removes a fork rather than adding a variant. Both match the `[SyntoModel]` /
cacheable-diagnostics specs' reuse discipline.

## Relationship to the feature plans

Both feature plans predate this decision and must be repointed:

- **cacheable-diagnostics plan** — drop its "Task 7 packaging decision" and the standalone-injection tasks;
  `DiagnosticBag`/`Drain`/`ToEquatableArray` land in `Synto.Incremental`; `[Diagnostic]` cacheable form
  emits against the single injected trio.
- **`[SyntoModel]` plan** — its "open-Q2 EquatableArray home" collapses to "use the single
  `Synto.Incremental` trio"; `Synto.Model` becomes a feature DLL; `ToEquatableArray` moves to
  `Synto.Incremental`.

## Open / deferred

1. **`Synto.GenKit` name + scope** — default: `GenKit`, *broad* (injection mechanism **plus** shared
   dep-free syntax/naming helpers). Narrow alternative (`Synto.Injection`, injection-only) if the helpers
   stay put. Internal DLL ⇒ rename is cheap.
2. **Injected trio namespace** — keep `Synto.Generators` (chosen, zero churn) vs move to `Synto`.
3. **Whether `Synto.Model` is its own DLL or folds into `Synto.Template`** — default its own, for symmetry.
4. **Exact pack wiring** for multiple analyzer assemblies + shared-dep DLLs in one nupkg (targets detail).

## Non-goals

- Shipping any of the generator DLLs as a consumer runtime reference (source-injection model is unchanged;
  `Synto.Core` remains the only runtime-referenced lib).
- Multi-package à-la-carte delivery (explicitly deferred; reversible).
- Moving the injected trio to a neutral namespace now (deferred churn).

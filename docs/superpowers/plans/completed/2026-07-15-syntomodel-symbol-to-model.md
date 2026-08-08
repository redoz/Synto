# `[SyntoModel]` Symbol-to-Model Generator — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give incremental-generator authors a `[SyntoModel]` spec class whose expression-bodied getters are pure projections over a source `ISymbol`, from which Synto emits a sibling cache-safe `record` plus a `From(TSymbol)` factory.

**Architecture:** A **new, separate** `IIncrementalGenerator` (`SyntoModelGenerator`) discovers `[SyntoModel]` spec classes via `ForAttributeWithMetadataName`, and — entirely inside its transform, so no `ISymbol`/`SemanticModel`/`SyntaxNode` leaks into pipeline state — validates field-type cache-safety, lifts each getter RHS into a `From` factory (rebinding the `Symbol` root to the factory parameter), rewrites `Project.Each/One` shim calls to real nested-model projections, and returns an equatable `ModelGenerationResult { FileName, Source, Diagnostics }`. Consumer surface (`[SyntoModel]`, `SyntoModel<T>`, `Project`) is authored once under `src/Synto.Core/Model/Surface/` and injected `internal` by this feature DLL's own `ModelSurfaceInjector : SurfaceInjectorBase` (mirroring `MatchSurfaceInjector`), fed by the `..\Synto.Core\Model\**\Surface\*.cs` embed in `Synto.Model.csproj`. This mirrors, almost one-for-one, the existing `MatchFactorySourceGenerator` and its `MatchTestHarness`.

**Tech Stack:** C#, Roslyn (`Microsoft.CodeAnalysis.CSharp`), incremental source generators (`netstandard2.0`), xUnit + `Verify` snapshots. Generator project: `src/Synto.Model` (its own feature DLL — refs `Synto.GenKit` + `Synto.Incremental`, bundled into the umbrella `Synto` nupkg). Surface source: `src/Synto.Core`. Tests: `test/Synto.Test`.

## Global Constraints

- **No Roslyn objects in the model.** A generated `[SyntoModel]` record MUST NEVER contain an `ISymbol`, `Compilation`, or `Location` (or any type derived from them). Fields are limited to: C# primitive, `string`, an `enum`, `EquatableArray<…>`, or another `[SyntoModel]` record. This is enforced mechanically (symbol/syntax facts only — **no** LLM/heuristic judgement), matching Synto's existing generator-diagnostic discipline.
- **Cacheability preserved.** All semantic work happens inside the transform; the value flowing through the pipeline is the equatable `ModelGenerationResult` only. Every tracked step must stay `Cached`/`Unchanged` on an unrelated edit (`CacheabilityAssert.AllStepsCachedOrUnchanged`).
- **Accumulate-and-continue diagnostics.** Cache-safety violations drop the offending field and continue collecting, mirroring `TemplateValidator` / `DecorationFinder`. Do not bail the whole spec on the first bad getter.
- **New diagnostic IDs continue the `SY10xx` sequence**, category `"Synto.Usage"`, `DiagnosticSeverity.Error`, `isEnabledByDefault: true`. Next free block is **`SY1036+`** (final numbers assigned during implementation; `SY12xx` is owned by Matching, `SY1005`–`SY1035` by Templating). Every new descriptor MUST be registered in this feature DLL's own `src/Synto.Model/AnalyzerReleases.Unshipped.md` (RS2008 — AnalyzerReleases ownership sits on each feature DLL), guarded by a registration-audit test.
- **Single source of truth for surface.** Consumer-facing types are authored ONCE under `src/Synto.Core/**/Surface/` and injected verbatim/rewritten by the feature DLL's `ModelSurfaceInjector : SurfaceInjectorBase`; never hand-duplicate them into the generator. `ResourceManifestPinTest` pins the exact embedded-resource set — update it when adding surface files.
- **Reuse-refactor discipline (spec §Reuse discipline).** Where a shared template-path mechanism does not cleanly serve the model path, **extract the common seam behind a fitting abstraction** so it genuinely serves both — never thread an `isModel` flag through template code, and never force the model path into a template-shaped hole (e.g. do NOT route model getters through `ValueLift`'s value→syntax conversion; the model path emits plain runtime projections).

## Resolved open questions (spec §Open questions)

These were left open by the spec; this plan resolves them as the default. Flagged so a reviewer can veto before Phase 1.

1. **IDs / category** — `SY1036+`, category `Synto.Usage` (see Global Constraints).
2. **Consumer-facing `EquatableArray`** — **reuse the copy already injected today.** `src/Synto.Incremental/EquatableArray.cs` is already embedded (via `Synto.Incremental.csproj`) under `LogicalName="Synto.Runtime.EquatableArray.cs"` and injected verbatim into every consumer as `Synto.Generators.EquatableArray<T>` (injected namespace unchanged; injected once by `IncrementalSurfaceInjector`). Do **not** create a third copy. Add only the missing `ToEquatableArray()` extension (Task 2), which is **shared with the cacheable-diagnostics plan** — whichever plan runs first authors it in `Synto.Incremental`; the second reuses it (do NOT author a second copy).
3. **`Project.One` on a `null` source symbol** — emit `null` (nullable-aware): the rewrite is `sym is null ? null : TModel.From(sym)`, and the field's declared type must be a nullable `TModel?`. No diagnostic for null itself.
4. **Generated record shape** — emit `sealed record`; keep the compiler-default `ToString` (YAGNI — no suppression until a concrete need appears).

---

## File Structure

**New (generator) — `src/Synto.Model/`** (its own feature DLL, sibling to `src/Synto.Match/` and `src/Synto.Template/`; new `Synto.Model.csproj` refs `Synto.GenKit` + `Synto.Incremental` + `Synto.Core` (PrivateAssets), with `InternalsVisibleTo("Synto.Test")` and the `..\Synto.Core\Model\**\Surface\*.cs` embed — mirror `Synto.Match.csproj`):

- `ModelSurfaceInjector.cs` — the feature DLL's `[Generator] ModelSurfaceInjector : SurfaceInjectorBase {}` (mirror `MatchSurfaceInjector.cs`); injects the embedded `Synto.Runtime.*` Model surface as `internal`.
- `SyntoModelGenerator.cs` — the `[Generator]`; FAWMN discovery, transform core, `Emit`. Responsibility: orchestration only (mirror `MatchFactorySourceGenerator.cs`).
- `ModelInfo.cs` — parsed, equatable spec description built inside the transform (spec-class name, resolved record name, `TSymbol` display, ordered list of projected fields with declared-type + name + RHS-as-string-or-node-metadata). Holds no live Roslyn objects on the pipeline boundary.
- `ModelValidator.cs` — cache-safety checks (field-type equatability, symbol-leak, `Project` element type), accumulate-and-continue. Mirror `TemplateValidator`.
- `ModelFactoryBuilder.cs` — emits the `record` + `From` factory syntax. Responsibility: pure syntax construction from `ModelInfo`.
- `ModelGenerationResult.cs` — equatable `{ FileName, Source, EquatableArray<DiagnosticInfo> Diagnostics }`. Mirror `TemplateGenerationResult`.
- `ModelTrackingNames.cs` — `Transform` / `Result` tracking-name constants (test-visible). Mirror `MatchTrackingNames`.
- `ModelDiagnostics.cs` — `SY1036+` descriptor factory. Mirror `TemplateDiagnostics` shape.

**New (surface, authored once) — `src/Synto.Core/Model/Surface/`:**

- `SyntoModelAttribute.cs` — `[SyntoModel]` with `string? Name { get; set; }`.
- `SyntoModel.cs` — `public class SyntoModel<T> { protected T Symbol => default!; }` (inert base).
- `Project.cs` — inert shims `Each<TModel>(IEnumerable<TSymbol>) => default!` / `One<TModel>(TSymbol) => default!`.
- `EquatableArrayExtensions.cs` — `ToEquatableArray<T>(this IEnumerable<T>)` (see Task 2 for placement decision).

Namespace for all consumer surface: **`Synto.Model`** (mirrors `Synto.Matching` / `Synto.Templating`).

**Reuse-refactor (extraction) — `src/Synto.Template/`:**

- The `Symbol` → factory-parameter rebind reuses/extracts the symbol-reference renamer currently embedded in the staging path (`src/Synto.Template/Staging/RootRenameRewriter.cs`, driven by the staged-lift `ctx.RootNames` wiring). See Task 6.

**New (feature DLL wiring):**

- `src/Synto.Model/Synto.Model.csproj` — new feature-DLL project embedding `..\Synto.Core\Model\**\Surface\*.cs` under `Synto.Runtime.%(Filename)%(Extension)` (mirror `Synto.Match.csproj`); refs `Synto.GenKit` + `Synto.Incremental` + `Synto.Core` (PrivateAssets); `InternalsVisibleTo("Synto.Test")`. Confirm the surface embed by test.
- `src/Synto.Model/AnalyzerReleases.Shipped.md` / `AnalyzerReleases.Unshipped.md` — the feature DLL's own release-tracking files; register new `SY1036+` descriptors in `AnalyzerReleases.Unshipped.md`.

**Modified:**
- `test/Synto.Test/ResourceManifestPinTest*` — extend the pinned resource set for new surface files.

**New tests — `test/Synto.Test/Model/`:**

- `ModelTestHarness.cs` — in-memory harness (clone of `Match/MatchTestHarness.cs`).
- `ModelDiagnosticsTests.cs`, `ModelSnapshotTests.cs` (+ `snapshots/`), `ModelSurfaceTests.cs`, `ModelCacheabilityTests.cs`.

---

## Phase 1 — Generator

### Task 1: Author + inject the consumer surface

**Files:**
- Create: `src/Synto.Core/Model/Surface/SyntoModelAttribute.cs`, `SyntoModel.cs`, `Project.cs`
- Test: `test/Synto.Test/Model/ModelSurfaceTests.cs`
- Modify: `test/Synto.Test/ResourceManifestPinTest*` (extend pinned set)

**Interfaces:**
- Produces: `Synto.Model.SyntoModelAttribute` (`public string? Name { get; set; }`); `Synto.Model.SyntoModel<T>` (`protected T Symbol => default!;`); `Synto.Model.Project` (`public static EquatableArray<TModel> Each<TModel>(IEnumerable<...>) => default!;`, `public static TModel One<TModel>(... symbol) => default!;`). All `public` in source (rewritten to `internal` on injection).

**Notes for the implementer:** This feature DLL provides its own `ModelSurfaceInjector : SurfaceInjectorBase` (mirror `MatchSurfaceInjector`), fed by the `..\Synto.Core\Model\**\Surface\*.cs` embed in `Synto.Model.csproj`; `SurfaceInjectorBase` flips top-level `public` → `internal` on injection. Author the injector + csproj embed alongside these surface files. `Project.Each`/`One` are inert shims recognized later by bound symbol; keep the arity/signatures shaped so an authored spec type-checks (`Each` returns `EquatableArray<TModel>`, `One` returns `TModel`).

- [ ] **Step 1: Write failing surface test.** In `ModelSurfaceTests.cs`, assert the injected surface is present and internal:
  ```csharp
  [Fact]
  public void SyntoModelSurface_IsInjected_AsInternal()
  {
      var src = ModelTestHarness.InjectedSurfaceSource("class SyntoModel");
      Assert.Contains("internal", src, StringComparison.Ordinal);
      Assert.Contains("protected", src, StringComparison.Ordinal); // Symbol member
  }
  ```
  (Reuse the `InjectedSurfaceSource` helper pattern from `MatchTestHarness`; it will live in `ModelTestHarness` from Task 4 — if authoring this before Task 4, inline a `ModelSurfaceInjector` run.)
- [ ] **Step 2: Run it, verify FAIL** (surface files not authored / `ModelTestHarness` absent). Run: `dotnet test test/Synto.Test --filter-class Synto.Test.Model.ModelSurfaceTests`
- [ ] **Step 3: Author the three surface files** under `src/Synto.Core/Model/Surface/`, namespace `Synto.Model`, each a single `public` top-level type, no leading header comment (leading trivia leaks into byte-identical injected snapshots — see csproj note).
- [ ] **Step 4: Run surface test, verify PASS.**
- [ ] **Step 5: Update `ResourceManifestPinTest`** to include the new `Synto.Runtime.SyntoModelAttribute.cs` / `SyntoModel.cs` / `Project.cs` resources; run it green.
- [ ] **Step 6: Commit.**
  ```bash
  rtk git add src/Synto.Core/Model/Surface test/Synto.Test/Model/ModelSurfaceTests.cs test/Synto.Test/ResourceManifestPinTest*
  rtk git commit -m "feat(model): inject [SyntoModel]/SyntoModel<T>/Project consumer surface"
  ```

### Task 2: `ToEquatableArray()` extension on the already-injected `EquatableArray`

**Files:**
- Create: `src/Synto.Incremental/EquatableArrayExtensions.cs` (beside the trio — see note below; NOT under `Synto.Core/Model/Surface/`, because it is shared with the cacheable-diagnostics plan)
- Test: `test/Synto.Test/Model/ModelSurfaceTests.cs` (self-containment proof)

**Interfaces:**
- Produces: `EquatableArray<T> ToEquatableArray<T>(this IEnumerable<T> source) where T : IEquatable<T>` visible in the consumer compilation (so generated `From` bodies can call `.ToEquatableArray()`).

**Decision (resolves open Q2):** reuse the already-injected `Synto.Generators.EquatableArray<T>` (embedded verbatim today from `src/Synto.Incremental/EquatableArray.cs`). Add `ToEquatableArray` as a **sibling injected internal extension** in the **`Synto.Generators` namespace** (injected namespace unchanged), authored beside the trio in `src/Synto.Incremental/` so it sits beside the injected struct and binds without an extra `using`. This extension is **shared with the cacheable-diagnostics plan** — whichever plan runs first authors it in `Synto.Incremental`; the second reuses it, so do **not** author a second copy (and do **not** create a third `EquatableArray` copy). Generated code fully-qualifies the array type as `global::Synto.Generators.EquatableArray<T>` to avoid any name-resolution ambiguity.

- [ ] **Step 1: Write failing self-containment test** — compile the injected `EquatableArray` + the new extension on the pinned `netstandard2.0` closure (clone `CompileInjectedGeneratorUtilitiesOnNetStandard20` and assert no error diagnostics, and that `ToEquatableArray` resolves).
- [ ] **Step 2: Run, verify FAIL.**
- [ ] **Step 3: Author `ToEquatableArray`** as an injected internal static extension (single source of truth; embedded via a `Synto.Runtime.*` `<EmbeddedResource>` in `Synto.Incremental.csproj`, matching how `EquatableArray.cs` is embedded there). Skip if the cacheable-diagnostics plan already authored it in `Synto.Incremental`.
- [ ] **Step 4: Run, verify PASS.**
- [ ] **Step 5: Extend `ResourceManifestPinTest`** for the new resource; green.
- [ ] **Step 6: Commit.**

### Task 3: `ModelDiagnostics` descriptors + release registration

**Files:**
- Create: `src/Synto.Model/ModelDiagnostics.cs`
- Modify: `src/Synto.Model/AnalyzerReleases.Unshipped.md`
- Test: `test/Synto.Test/Model/ModelDiagnosticsTests.cs`

**Interfaces:**
- Produces: `ModelDiagnostics.NonEquatableFieldType(Location?, string typeName, bool isSymbolLike)`, `SymbolLeakInProjection(Location?, string typeName)`, `ProjectElementNotSyntoModel(Location?, string typeArg)`, `NameSuffixMissing(Location?, string specName)` — each returning `DiagnosticInfo` (mirror `TemplateDiagnostics` factory shape). Assign IDs `SY1036`–`SY1039`.
- The non-equatable message, when the type is `ISymbol`/`Compilation`/`Location`-derived, appends: *"would leak symbol into cached model; project it to a value (e.g. `.Name`, `.ToDisplayString()`)."*

- [ ] **Step 1: Write failing registration-audit test** (clone `Sy12xxDescriptors_AreRegistered_InUnshippedReleases`):
  ```csharp
  [Fact]
  public void ModelDescriptors_AreRegistered_InUnshippedReleases()
  {
      var content = File.ReadAllText(FindUnshippedReleasesPath());
      foreach (var id in new[] { "SY1036", "SY1037", "SY1038", "SY1039" })
          Assert.Contains(id, content, StringComparison.Ordinal);
  }
  ```
- [ ] **Step 2: Run, verify FAIL.**
- [ ] **Step 3: Author `ModelDiagnostics`** (4 descriptors, category `Synto.Usage`) and add the 4 rows to `src/Synto.Model/AnalyzerReleases.Unshipped.md`.
- [ ] **Step 4: Run, verify PASS**; also run a full `dotnet build src/Synto.Model` to confirm no RS2008.
- [ ] **Step 5: Commit.**

### Task 4: `SyntoModelGenerator` skeleton + `ModelTestHarness` + cacheability

**Files:**
- Create: `src/Synto.Model/SyntoModelGenerator.cs`, `ModelGenerationResult.cs`, `ModelTrackingNames.cs`
- Create: `test/Synto.Test/Model/ModelTestHarness.cs`, `ModelCacheabilityTests.cs`
- Modify: `src/Synto.Model/Synto.Model.csproj` — ensure its `InternalsVisibleTo("Synto.Test")` is present; confirm `ModelTrackingNames` is visible.

**Interfaces:**
- Produces: `SyntoModelGenerator : IIncrementalGenerator`; `ModelGenerationResult(string? FileName, string? Source, EquatableArray<DiagnosticInfo> Diagnostics)`; `ModelTrackingNames.Transform` / `.Result`.
- Consumes: `ModelTestHarness.Run(string)` / `.CreateCompilation` / `.InjectedSurfaceSource` (clone of `MatchTestHarness`, referencing the public `Synto.Core` markers + Roslyn).

**Notes:** FAWMN on `typeof(SyntoModelAttribute).FullName`. The transform does all work over plain inputs and returns the equatable result; `RegisterSourceOutput` `Emit` reports diagnostics then `AddSource`. At this task the emitter is a **stub** (returns `null` Source for a well-formed spec) — assert only on diagnostics + cacheability, not on generated trees (mirror `WellFormedMatch_ReadsTypeArg_PassesValidation`).

- [ ] **Step 1: Write failing "well-formed spec, no diagnostics" test** using a minimal spec:
  ```csharp
  const string src = """
  using Synto.Model;
  class PropModelShape : SyntoModel<Microsoft.CodeAnalysis.IPropertySymbol>
  {
      string Name => Symbol.Name;
  }
  """;
  var result = ModelTestHarness.Run(src);
  Assert.Empty(result.Diagnostics);
  ```
- [ ] **Step 2: Run, verify FAIL** (`ModelTestHarness` / generator absent).
- [ ] **Step 3: Author the harness + generator skeleton** (FAWMN, equatable result, stub emitter, tracking names, `Emit`).
- [ ] **Step 4: Run, verify PASS.**
- [ ] **Step 5: Add the unrelated-edit cacheability test** (clone `Generator_IsIncremental_OnUnrelatedEdit`; assert `AllStepsCachedOrUnchanged` over `Transform`+`Result`). Run green.
- [ ] **Step 6: Commit.**

### Task 5: Record name derivation + field/record emission

**Files:**
- Create: `src/Synto.Model/ModelInfo.cs`, `ModelFactoryBuilder.cs`
- Test: `test/Synto.Test/Model/ModelSnapshotTests.cs` (+ `snapshots/`), `ModelDiagnosticsTests.cs`

**Interfaces:**
- Produces: `ModelInfo` (record name resolved from spec-class name minus trailing `Shape`, or `[SyntoModel(Name=…)]`; `TSymbol` display string; ordered `ProjectedField { TypeSyntax DeclaredType, string Name, ... }` list built from **instance expression-bodied getters only** — static/field/method/block-bodied members ignored). `ModelFactoryBuilder.Build(ModelInfo) → CompilationUnitSyntax` emitting `sealed record {Name}({fields}) { public static {Name} From({TSymbol} symbol) => new(...); }`.
- **`ProjectedField.DeclaredType` is copied verbatim from the getter's declared type EXCEPT for nested-model getters whose RHS binds a `Project.Each/One` shim — those are rewritten in Task 7 (spec-collection → `EquatableArray<Record>` / `Record?`).** Task 5's own snapshot fixture stays scalar (no `Project` getters) so this exception does not bite here.

**Notes:** At this task the `From` body may reference `Symbol.*` verbatim (rebind lands in Task 6) — snapshot will still be valid C# but not yet parameter-bound; keep the snapshot fixture's getters trivial (e.g. a constant/`Symbol`-free RHS) so this task's snapshot compiles, OR gate the compile-assert until Task 6. Prefer: this task's snapshot fixture uses a single `Symbol.Name` getter and Task 6 turns the snapshot green end-to-end.

- [ ] **Step 1: Write failing name-derivation test** — `PropModelShape` → record named `PropModel`; and the missing-suffix diagnostic: a spec class `Foo` (no `Shape`, no `Name`) reports `SY1039`.
- [ ] **Step 2: Run, verify FAIL.**
- [ ] **Step 3: Implement `ModelInfo` extraction + `ModelFactoryBuilder`** (positional record + `From`; getter type = field type, getter name = field name).
- [ ] **Step 4: Run, verify PASS.**
- [ ] **Step 5: Add a first snapshot test** (`Verify`) for a two-field spec (`Name`, `Required`) with `Symbol`-free-or-trivial RHS; accept snapshot.
- [ ] **Step 6: Commit.**

### Task 6: `Symbol` → factory-parameter rebind (reuse-refactor)

**Files:**
- Modify/extract: `src/Synto.Template/Staging/RootRenameRewriter.cs` → extract a plain, template-agnostic symbol-reference renamer usable by both paths (do NOT add an `isModel` flag). NOTE: this rewriter lives in the `Synto.Template` feature DLL; if the model path must consume it, hoist the pure renamer into a shared home (`Synto.GenKit` or `Synto.Incremental`) rather than making `Synto.Model` depend on `Synto.Template` (feature DLLs never reference each other).
- Modify: `src/Synto.Model/ModelFactoryBuilder.cs` (apply the renamer to each getter RHS)
- Test: `test/Synto.Test/Model/ModelSnapshotTests.cs`, `ModelDiagnosticsTests.cs`

**Interfaces:**
- Produces: a shared renamer with contract *"given a bound root symbol and a target identifier, rewrite every reference to that symbol into the target identifier."* Both the template control-region wiring (currently `StagedRootLifter` step 3 via `ctx.RootNames` + `RootRenameRewriter`) and the model `From` builder consume it. Model path rebinds the `SyntoModel<T>.Symbol` property references → the `From` parameter identifier (`symbol`).

**Notes (reuse discipline):** Inspect `RootRenameRewriter` first. If it is already a clean symbol→name rewriter with no template-only coupling, reuse it directly. If it reaches into `TemplateBuildContext`/staging state, extract the pure rewrite into a standalone type (e.g. `SymbolReferenceRenamer`) that `RootRenameRewriter` (or `StagedRootLifter`) then wraps, and have the model builder call the standalone type. The model path must NOT touch `ValueLift`/`StagedRootLifter`'s value→syntax conversion — model getters stay plain runtime projections.

- [ ] **Step 1: Write failing end-to-end test** — spec `string NamespaceName => Symbol.ContainingNamespace.ToDisplayString();` produces a `From(INamedTypeSymbol symbol)` whose body reads `symbol.ContainingNamespace.ToDisplayString()` (assert the generated tree contains `symbol.ContainingNamespace`, and does **not** contain a bare `Symbol.` reference).
- [ ] **Step 2: Run, verify FAIL** (RHS still says `Symbol.`).
- [ ] **Step 3: Extract/reuse the renamer** and apply it in `ModelFactoryBuilder`. If extraction touches the template path, run the **full** `Synto.Test` suite to prove the template snapshots stay byte-identical.
- [ ] **Step 4: Run model + template tests, verify PASS** (byte-identical template snapshots).
- [ ] **Step 5: Turn the Task 5 snapshot end-to-end green** (accept updated snapshot now that `From` is parameter-bound).
- [ ] **Step 6: Commit.**

### Task 7: `Project.Each` / `Project.One` rewrite

**Files:**
- Modify: `src/Synto.Model/ModelFactoryBuilder.cs` (or a small `ProjectCallRewriter.cs`)
- Test: `test/Synto.Test/Model/ModelSnapshotTests.cs`, `ModelDiagnosticsTests.cs`

**Surface prerequisite (Option 2 — already applied to the shim):** `Project.Each<TModel>` returns the **unconstrained** `IEnumerable<TModel>` (NOT `EquatableArray<TModel>`, which is defined `where T : IEquatable<T>` — a spec class is not `IEquatable`, so `EquatableArray<Spec>` fails to bind (CS0311), the shim call goes unbound, and `SY1038` can never fire). The type argument is the **spec** class, so the nested-model getter is **authored** with a spec-collection declared type — `IEnumerable<PropModelShape> Props => Project.Each<PropModelShape>(…)` and `PropModelShape? Owner => Project.One<PropModelShape>(…)`. The generator is responsible for rewriting that declared type into the record collection/nullable type on the emitted record field (see Step 3b). Do NOT re-add any `IEquatable` constraint or re-widen the shim return to `EquatableArray`.

**Interfaces:**
- Produces: recognition of `Project.Each<TModel>(seq)` / `Project.One<TModel>(sym)` **by bound symbol** (the injected `Project` shims), rewritten in the emitted `From` body to:
  - `Project.Each<PropModelShape>(seq)` → `seq.Select(PropModel.From).ToEquatableArray()`
  - `Project.One<PropModelShape>(sym)` → `sym is null ? null : PropModel.From(sym)` (nullable-aware)
- The `TModel` type argument is the **spec** class; the generated call targets its **record** name's `From` (name-derived per Task 5). Emits `SY1038` if the type arg is not a `[SyntoModel]` spec class in the same compilation.
- **Field-type rewrite (generator-only, consumer surface stays clean):** for a getter whose RHS is a bound `Project.Each<Spec>` / `Project.One<Spec>` shim call, the emitted record field's **declared type** is synthesized from the resolved record name, NOT copied from the getter's spec-collection type:
  - `IEnumerable<PropModelShape>` (getter) → `EquatableArray<PropModel>` (record field)
  - `PropModelShape?` / `PropModelShape` (getter) → `PropModel?` (record field, nullable-aware)
  - This resolves the Task 5 assumption that `ProjectedField.DeclaredType` is copied verbatim from the getter — for `Project`-shim getters the field type is REWRITTEN here.

**Notes:** Recognize the shim calls the way `QuoteCallFinder`/`SpliceCallFinder` recognize inert shims — by the bound method symbol, resolved against the injected/public `Project` type. Carrier-binding invariant: `PropModel.From` is a sibling generated record in the same compilation; nested-model forward references are fine because both records are emitted.

- [ ] **Step 1: Write failing nested-model test** — a `TypeModelShape` with `IEnumerable<PropModelShape> Props => Project.Each<PropModelShape>(Symbol.GetMembers().OfType<IPropertySymbol>());` emits a record whose `Props` field is declared `EquatableArray<PropModel>` with RHS `...Select(PropModel.From).ToEquatableArray()`, and both records are generated.
- [ ] **Step 2: Run, verify FAIL.**
- [ ] **Step 3: Implement the `Project.Each/One` RHS rewrite** + `SY1038` for a non-`[SyntoModel]` element type.
- [ ] **Step 3b: Implement the field-type rewrite** — when a getter's RHS binds to a `Project` shim, synthesize the emitted field's declared type from the resolved record name (`EquatableArray<Record>` for `Each`, `Record?` for `One`) instead of copying the getter's spec-collection type.
- [ ] **Step 4: Run, verify PASS**; add a `Project.One` null-aware snapshot.
- [ ] **Step 5: Commit.**

### Task 8: Cache-safety diagnostics (field-type + symbol-leak)

**Files:**
- Create: `src/Synto.Model/ModelValidator.cs`
- Modify: `src/Synto.Model/SyntoModelGenerator.cs` (invoke validator, accumulate-and-continue)
- Test: `test/Synto.Test/Model/ModelDiagnosticsTests.cs`

**Interfaces:**
- Produces: `ModelValidator.Validate(List<DiagnosticInfo>, ModelInfo)` — for each field: declared type must be primitive / `string` / `enum` / `EquatableArray<…>` / another `[SyntoModel]` record, else `SY1036` (with the symbol-like message tail when the type derives from `ISymbol`/`Compilation`/`Location`). Also check the **lifted RHS result type** (`SY1037`) so a getter declared `object` but returning a symbol is still caught. Drops the offending field, continues.

- [ ] **Step 1: Write failing tests** — a getter `INamedTypeSymbol Bad => Symbol;` reports `SY1036` with the "would leak symbol" tail; a getter `object Leak => Symbol;` reports `SY1037`; and a spec with one bad + one good field still emits the good field (accumulate-and-continue).
- [ ] **Step 2: Run, verify FAIL.**
- [ ] **Step 3: Implement `ModelValidator`** and wire it (drop-and-continue; a fully-invalid spec emits no record but reports every violation).
- [ ] **Step 4: Run, verify PASS**; add a `AllStepsCachedOrUnchanged` guard for a spec that reports diagnostics (diagnostics must be carried cacheably as `DiagnosticInfo`).
- [ ] **Step 5: Commit.**

### Task 9: Dog-food + end-to-end green

**Files:**
- Modify: an existing example that extracts a model from symbols (prefer `src/…/Synto.Example.ObjectReader*` if it has a symbol-extraction step), else add a focused end-to-end fixture under `test/Synto.Test/Model/`.
- Test: full `Synto.Test` + example build.

**Notes:** Per spec Phase 1 and the `objectreader-dogfood-drives-features` memory, drive one real consumer through `[SyntoModel]`. Mine any friction. If ObjectReader has no symbol-extraction surface, a focused test model is acceptable (spec allows the fallback).

- [ ] **Step 1: Identify or author the dog-food consumer;** write an end-to-end test asserting the generated record is cache-safe (structural equality: two `From` calls over equal inputs produce equal records; over different inputs, unequal).
- [ ] **Step 2: Run, verify FAIL / gaps.**
- [ ] **Step 3: Address friction** (fold any new gaps back as small follow-up tasks if material).
- [ ] **Step 4: Run full `dotnet test test/Synto.Test` + example build, verify green** (0 warnings — `TreatWarningsAsErrors` is global).
- [ ] **Step 5: Commit.**

---

## Phase 2 — Analyzer completion provider

Deferred, independently shippable. Builds on Phase 1 with no changes to the generator's output.

> **PARKED — owner ruling 2026-07-19. Not being implemented from this plan.**
> A `CompletionProvider` requires `Microsoft.CodeAnalysis.Features` (+ `Workspaces` for tests) — an IDE-layer
> dependency with zero repo precedent (`LiveTemplateHighlightAnalyzer` is a compiler-layer `DiagnosticAnalyzer`,
> so it dodged this). Task 10's `src/Synto.Model` target is bundled into the compiler-loaded `Synto` nupkg;
> dragging heavy IDE Features/Workspaces assemblies into that package conflicts with the single-package
> DLL-layering model and risks analyzer-load failures. **Ruling:** if built, the completion provider ships in a
> NEW, separate IDE-facing package OUTSIDE the compiler nupkg (its own project + NuGet id) — Tasks 10/11 below
> must be re-pitched against that package, not `src/Synto.Model`.
> **Decision: park indefinitely.** The SY1036–SY1039 diagnostics (compiler-layer, already shipping in Phase 1)
> deliver authoring feedback without the Features dependency; that is deemed sufficient. Revisit only if a
> concrete `[SyntoModel]` authoring pain surfaces that autocomplete would remove. The Task 10/11 sketch below is
> retained for reference only.

### Task 10: Completion provider skeleton (in-spec-class trigger)

**Files:**
- Create: `src/Synto.Model/SyntoModelCompletionProvider.cs` (or the analyzer project if completion providers live separately — follow where `LiveTemplateHighlightAnalyzer` and any existing `CompletionProvider` live)
- Test: `test/Synto.Test/Model/ModelCompletionTests.cs`

**Interfaces:**
- Produces: a `CompletionProvider` that activates only when the cursor is inside the body of a `[SyntoModel]` spec class.

- [ ] **Step 1: Write failing test** — cursor inside a `[SyntoModel]` class body offers at least one completion; cursor outside offers none.
- [ ] **Step 2–5:** implement trigger detection; run; commit.

### Task 11: Equatable-projection completions over `T`'s members

**Files:**
- Modify: `src/Synto.Model/SyntoModelCompletionProvider.cs`
- Test: `test/Synto.Test/Model/ModelCompletionTests.cs`

**Interfaces:**
- Produces: completions of the form `string Name => Symbol.Name;`, `... => Symbol.X.ToDisplayString();`, enum/bool member projections, and nested-model `Project.Each/One` suggestions — restricted to **known cache-safe** projections of `T`'s members (name / display-string / enum / bool / nested `[SyntoModel]`). Never offers a projection whose result would trip `SY1036`.

- [ ] **Step 1: Write failing test** — for `SyntoModel<IPropertySymbol>`, completions include `string Name => Symbol.Name;` and a display-string projection, and exclude any raw-symbol projection.
- [ ] **Step 2–5:** implement member enumeration + cache-safe filtering; run; commit.

---

## Verification (end-to-end)

1. **Unit + snapshot:** `dotnet test test/Synto.Test --filter-namespace Synto.Test.Model` — all model tests green; snapshots accepted and stable on re-run.
2. **Cacheability:** the `AllStepsCachedOrUnchanged` guards (Tasks 4, 8) prove no `ISymbol`/`Compilation`/`SyntaxNode` leaked into pipeline state (an unrelated-edit re-run keeps `Transform`/`Result` `Cached`/`Unchanged`).
3. **Structural equality (the whole point):** the Task 9 end-to-end test proves two `From` calls over equal symbol inputs yield `.Equals`-equal records, and unequal inputs yield unequal records — the model is cache-safe **by construction**.
4. **Self-containment:** the injected surface + `EquatableArray`/`ToEquatableArray` compile clean on the pinned `netstandard2.0` closure (Task 2), proving a consumer needs only the `Synto` generator package.
5. **Registration hygiene:** `dotnet build src/Synto.Model` is RS2008-clean; the registration-audit test (Task 3) confirms every `SY1036+` descriptor is in `src/Synto.Model/AnalyzerReleases.Unshipped.md`.
6. **Full suite:** `dotnet test test/Synto.Test` + the ObjectReader example build are green with zero warnings (global `TreatWarningsAsErrors`).

## Self-review notes

- **Spec coverage:** authoring surface (Task 1), `SyntoModel<T>`/`Project`/`EquatableArray` injection (Tasks 1–2), name derivation + record/`From` emission (Tasks 5–6), `Project.Each/One` rewrite (Task 7), all four cache-safety diagnostics (Tasks 3, 7, 8), phasing (P1 generator / P2 analyzer), reuse-refactor seams (Task 6 renamer; Task 1 surface glob = no-code reuse), dog-food (Task 9). All spec §Open questions resolved above.
- **Reuse-refactor is explicit** at Task 6 (renamer extraction, with a full-suite byte-identical guard on the template path) and Task 2 (reuse the injected `EquatableArray`, no third copy). The plan deliberately keeps the model path **off** `ValueLift`/`StagedRootLifter`'s value→syntax conversion (per spec §Reuse discipline — model getters emit plain runtime projections, not quoted syntax).
- **Not planned here (correctly):** Feature 2 (cacheable diagnostics for consumers) — separate later cycle. Non-goals from spec (FAWMN/`RegisterSourceOutput` wiring, per-field DSL, symbol round-trip) are out of scope.

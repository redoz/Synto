# `[Quote]` Value-Marker Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Plan style:** This plan pins **contracts, interfaces, signatures, and tests** — not prescribed implementation bodies (matching `2026-06-27-live-staged-templates.md`). Each task states exact public/internal signatures, the locked behavior, and concrete test scenarios; the implementer chooses the generator implementation that satisfies them. **Test code is the spec** — write it as given, adapting only to exact harness helper names already present in `test/Synto.Test`.
>
> **Source of truth:** `docs/superpowers/specs/2026-06-27-quote-value-marker-design.md` (committed at `d0123df3` on bookmark `experimental`). All open questions in that spec §6 are **resolved** — there is no decision gate; surface is locked below.

**Goal:** Add `[Quote]` — a value-parameter marker (plus an inline `Quote(value)` form) that lifts a factory-time value into the emitted syntax exactly like `[Unquote]` in value position, but is **never a staging root**, so control flow driven only by quoted values stays a real runtime loop instead of unrolling.

**Architecture:** Purely additive on the existing `TemplateSyntaxQuoter` seam (`unquotedReplacements : Dictionary<SyntaxNode, ExpressionSyntax>` + `trimNodes`). A new value-param finder (modeled on `SpliceParameterFinder`) discovers `[Quote]` parameters and their use-sites; a new call finder (modeled on `SpliceCallFinder`) discovers inline `Quote(value)`. Both reuse the existing value→syntax conversion (built-in `.ToSyntax()` / `[Runtime]` converter) that today lives inline in the `[Unquote]` orchestrator section — extracted to a shared helper first (Task 2). Crucially, `[Quote]` symbols are **never** fed to `BindingTimeClassifier` as `StagedRoot`s, and an inline `Quote(...)` call is taught to the classifier as an **output-world boundary** that halts liveness propagation. The quoter is not touched; the pipeline value stays the equatable `TemplateGenerationResult`.

**Tech Stack:** C# / .NET 10 SDK (`global.json` pin), Roslyn `Microsoft.CodeAnalysis.CSharp`, `netstandard2.0` for generator + runtime, xUnit v3 (MTP v2) + Verify snapshots, jujutsu (jj) over git.

## Global Constraints

Every task implicitly includes these (from the spec + `principles.md` + `architecture.md`):

- **Cacheability is sacred.** Never capture `Compilation`/`ISymbol`/`SemanticModel`/`SyntaxNode` into pipeline state. ALL new analysis runs inside the `ForAttributeWithMetadataName` transform of `TemplateFactorySourceGenerator`; the only value flowing out stays the equatable `TemplateGenerationResult` (generated text + `EquatableArray<DiagnosticInfo>`). Tasks 3 and 4 each ship an incremental-caching guard that asserts **all** tracked steps are `Cached`/`Unchanged` on an unrelated edit (per `CacheabilityAssert`; the guard must iterate every tracked step, not just the terminal one).
- **The quoter is not modified.** `src/Synto/CSharpSyntaxQuoter*.cs` and `src/Synto.Bootstrap/CSharpSyntaxQuoter*.cs` stay byte-identical. `[Quote]` is expressed only through `unquotedReplacements`/`trimNodes` entries. If any task finds it *must* touch a quoter `Visit*` method, **stop and raise it** — that contradicts the design.
- **Surface is minimal, `internal`, single-source-of-truth.** The `[Quote]` marker + `Quote` facade are authored once under `src/Synto/Templating/`, embedded in `Synto.SourceGenerator.csproj` under the `Synto.Runtime.*` LogicalName prefix, and injected `internal` by `SurfaceInjectionGenerator` (which iterates ALL `Synto.Runtime.*` resources — no generator code change needed). The generator must never re-declare the marker.
- **No forced runtime dependency in consumer output.** `[Runtime]` converters used by a `[Quote]` lift are called fully-qualified as static methods, exactly like the `[Unquote]` path. Generated output references only BCL + Roslyn + the consumer's own code; zero `Synto.*` runtime dependency.
- **Failures are diagnostics, not exceptions.** `[Quote]` of a custom type with **no** `[Runtime]` converter reuses the existing `SY1011`; with **multiple** converters reuses `SY1012` (the `[Unquote]` converter diagnostics). **No new `SY####` descriptors** are introduced (spec §6.3: no `[Quote]`-specific diagnostics). Value-axis enforcement is mechanical: `[AttributeUsage(AttributeTargets.Parameter)]` makes `[Quote]` on a generic-type-param a C# `CS0592` at the carrier — no Synto diagnostic.
- **Platform:** `netstandard2.0` everywhere for generator + runtime; `EnforceExtendedAnalyzerRules` stays on.
- **Green gate (run before every commit):** `dotnet build -c Debug` (0 errors; analyzer **warnings** are findings, not gate failures) · `dotnet test --no-build -c Debug` (all green — **never** pass `--nologo`; MTP filter syntax is `-- --filter-method "*Name*"`) · `dotnet format whitespace` then `dotnet format whitespace --verify-no-changes` (no diffs; whitespace scope only).
- **Snapshots:** rebaseline via Verify-accept (move `*.received.*` over `*.verified.*`) + delete orphans; the snapshot orphan guard enforces every snapshot maps to a test method. **NEVER hand-edit a `.verified.cs`.**
- **Commits:** Conventional Commits, one commit per task, **no AI/Claude footer**. VCS is **jj** (`jj commit -m "…"`); **never** `git`.
- **Branch / integration:** work lands on bookmark **`experimental`** (currently at `d0123df3`); **no push**; do not move the bookmark by hand (the operator/integration advances it). Keep the parked operator files **`.rtk/filters.toml`** and **`CLAUDE.md`** OUT of every commit (they live uncommitted in the working copy) — commit by-path.

## Locked Names (resolved in spec §6 — do not re-litigate)

- Attribute: `Synto.Templating.QuoteAttribute`, `[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]` (**value axis only** — no `GenericParameter`). Authored `public`, injected `internal`.
- Inline facade: `Synto.Templating.Template.Quote<T>(T value) => value` (inert identity, mirrors `Unquote<T>(T value)`). Type inferred from the argument.
- **No** `Quote<T>()` caller-supplied declaration form (redundant with `Parameter<T>()` + `Quote(…)`).
- Embed LogicalName: `Synto.Runtime.QuoteAttribute.cs`. The `Quote` facade ships inside the existing `Synto.Runtime.Template.cs` resource (it is a member of `Template.Parameter.cs`).

## File Structure

**Create (runtime surface, `src/Synto/Templating/`):**
- `QuoteAttribute.cs` — the `[Quote]` value-parameter marker. Mirror `UnquoteAttribute.cs` (doc-comment contrasting with `[Unquote]`: same value-lift, never a staging root).

**Create (generator, `src/Synto.SourceGenerator/Templating/`):**
- `QuoteParameterFinder.cs` — discovers `[Quote]` parameters + their identifier use-sites (mirror `SpliceParameterFinder.cs`). Returns `QuoteParameter(ParameterSyntax parameter, IReadOnlyList<IdentifierNameSyntax> references)`.
- `QuoteCallFinder.cs` — discovers inline `Template.Quote<T>(value)` invocations by binding (mirror `SpliceCallFinder.cs`). Returns `QuoteCall(InvocationExpressionSyntax invocation, ExpressionSyntax valueArgument)`.

**Modify:**
- `src/Synto/Templating/Template.Parameter.cs` — add `Quote<T>(T value)` facade (mirror `Unquote<T>` at lines 24-34).
- `src/Synto.SourceGenerator/Synto.SourceGenerator.csproj` — add the `Synto.Runtime.QuoteAttribute.cs` `EmbeddedResource` (alongside the other `Synto.Runtime.*` markers, ~line 49).
- `src/Synto.SourceGenerator/Templating/TemplateFactorySourceGenerator.cs` — (Task 2) extract the value→syntax conversion now inline in the `[Unquote]` value-param section (~lines 597-664) into a reusable helper; (Tasks 3-4) wire the two new finders' results into `unquotedReplacements`/`trimNodes` + factory-parameter generation, **without** seeding `StagedRoot`s.
- `src/Synto.SourceGenerator/Templating/BindingTimeClassifier.cs` — (Task 4) `ReferencesStaged` must treat an inline `Quote(...)` invocation as an output-world boundary: identifiers nested inside a `Quote(...)` argument do **not** make an enclosing expression/control construct staged.
- `examples/Synto.Examples/Program.cs` + `test/Synto.Test/RoundTripTests.cs` — (Task 5) a `[Quote]` compact-loop round-trip + demo (the sibling of the `[Unquote]` `Loop` that unrolls).
- `docs/superpowers/notes/2026-06-27-live-staged-templates-friction.md` — (all tasks) append genuine friction findings.

**Reuse (do not modify):** the quoter files; `SurfaceInjectionGenerator.cs` (iterates `Synto.Runtime.*` as-is); `RuntimeConverterFinder.cs` (the converter discovery `[Quote]` reuses); `StagedRootFinder`/`StagedParameterFinder`/`BindingTimeClassifier` seeding (do not add `[Quote]` to the root set).

---

### Task 1: Runtime surface — `[Quote]` attribute + `Quote<T>()` facade + injection

**Files:**
- Create: `src/Synto/Templating/QuoteAttribute.cs`
- Modify: `src/Synto/Templating/Template.Parameter.cs` (add `Quote<T>`)
- Modify: `src/Synto.SourceGenerator/Synto.SourceGenerator.csproj` (embed)
- Test: `test/Synto.Test/SurfaceInjectionTest.cs` (snapshot-driven; new `#QuoteAttribute` snapshot + updated `#Template` snapshot)

**Interfaces:**
- Produces: `public sealed class QuoteAttribute : Attribute` with `[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]`; `public static T Quote<T>(T value) => value;` on `Template`.

- [ ] **Step 1: Author `QuoteAttribute.cs`** — copy `UnquoteAttribute.cs` shape; restrict usage to `AttributeTargets.Parameter`; doc-comment states "same value-lift as `[Unquote]`, but **never** a staging root — control flow driven only by a quoted value stays a runtime construct (no unroll). Contrast `[Unquote]` (live, drives staged control) and `[Splice]` (verbatim node)."
- [ ] **Step 2: Add the `Quote<T>` facade** to `Template.Parameter.cs` mirroring `Unquote<T>` (inert `=> value`; doc-comment: "the inline stage-0 → stage-1 boundary — emits the quoted syntax of `value` at this site and stops liveness here, so an enclosing loop/condition stays a runtime construct").
- [ ] **Step 3: Add the `EmbeddedResource`** to `Synto.SourceGenerator.csproj` (`Include="..\Synto\Templating\QuoteAttribute.cs" LogicalName="Synto.Runtime.QuoteAttribute.cs"`).
- [ ] **Step 4: Run the injection snapshot test; accept new/updated snapshots.** `dotnet test --no-build -c Debug -- --filter-method "*VerifyInjectedSurface*"` produces a `*.received.*` for `#QuoteAttribute` and an updated `#Template`. Verify-accept (move received→verified). Re-run: PASS. Confirm no orphan-guard failure.
- [ ] **Step 5: Green gate + commit (by-path; exclude `.rtk/filters.toml`, `CLAUDE.md`).**

```bash
jj commit -m "feat(templating): inject [Quote] attribute + Template.Quote facade" \
  src/Synto/Templating/QuoteAttribute.cs src/Synto/Templating/Template.Parameter.cs \
  src/Synto.SourceGenerator/Synto.SourceGenerator.csproj 'glob:"test/Synto.Test/snapshots/**"'
```

---

### Task 2: Extract the value→syntax conversion into a shared helper (refactor, behavior-preserving)

The built-in-`.ToSyntax()` / `[Runtime]`-converter / `SY1011`/`SY1012` logic currently lives **inline** in the `[Unquote]` value-param section of `TemplateFactorySourceGenerator.cs` (~lines 597-664). Task 3 and Task 4 must reuse it verbatim. Extract it first so there is one implementation (DRY).

**Files:**
- Modify: `src/Synto.SourceGenerator/Templating/TemplateFactorySourceGenerator.cs`
- Test: existing `RuntimeConverterTest.cs`, `SimpleTemplateTest.cs`, `RoundTripTests.cs` (must stay green — this is a pure refactor)

**Interfaces:**
- Produces: a private helper on the orchestrator, e.g. `bool TryEmitValueLift(ITypeSymbol valueType, ExpressionSyntax valueAccess, ImmutableArray<INamedTypeSymbol> runtimeClasses, out ExpressionSyntax liftedSyntax, out DiagnosticInfo? diagnostic)` — returns the conversion expression (`value.ToSyntax()` for built-ins; `global::Ns.Converter.ToSyntax(value)` for a single `[Runtime]` converter) or a `SY1011`/`SY1012` diagnostic. Exact signature is the implementer's choice; the contract is "one method both the `[Unquote]` value path and the new `[Quote]` paths call."
- Note: the **generic-type-param** branch (`typeof(T).ToTypeSyntax()`, ~lines 606-614) is `[Unquote]`-only and stays out of the shared helper — `[Quote]` is value-axis only.

- [ ] **Step 1: Confirm the current behavior is pinned** — run `dotnet test --no-build -c Debug -- --filter-method "*RuntimeConverter*"` and the `[Unquote]` snapshot tests; all green (baseline).
- [ ] **Step 2: Extract** the inline conversion (built-in detection + converter discovery + `SY1011`/`SY1012`) into the shared helper; rewrite the `[Unquote]` value-param section to call it. No behavior change.
- [ ] **Step 3: Run the full suite; verify ZERO snapshot diffs and ZERO new/changed diagnostics.** `dotnet test --no-build -c Debug`. Expected: all green, no `*.received.*` produced. (A snapshot diff here means the refactor changed behavior — revert and redo.)
- [ ] **Step 4: Green gate + commit (by-path).**

```bash
jj commit -m "refactor(templating): extract value->syntax lift shared by [Unquote] and [Quote]" \
  src/Synto.SourceGenerator/Templating/TemplateFactorySourceGenerator.cs
```

---

### Task 3: `[Quote]` parameter — finder + value-lift emit (never a staging root)

A `[Quote] T x` parameter becomes a factory parameter `x : T`; every use of `x` is replaced by the lifted syntax (via Task 2's helper); the parameter is trimmed from the emitted carrier body. It is **not** seeded into `BindingTimeClassifier`, so a control construct referencing only `[Quote]` values stays `Quoted` (no unroll) — automatically, with no classifier change.

**Files:**
- Create: `src/Synto.SourceGenerator/Templating/QuoteParameterFinder.cs` (mirror `SpliceParameterFinder.cs`)
- Modify: `src/Synto.SourceGenerator/Templating/TemplateFactorySourceGenerator.cs` (wire results; call Task 2 helper; add factory param + `unquotedReplacements` + `trimNodes`)
- Test: `test/Synto.Test/Templating/InjectedSurfaceCompletenessTest.cs` (compile-assert), `test/Synto.Test/Templating/SimpleTemplateTest.cs` (snapshot + cacheability), `test/Synto.Test/Templating/BindingTimeClassifierTest.cs` (stays-quoted)

**Interfaces:**
- Consumes: Task 2 `TryEmitValueLift`; `RuntimeConverterFinder` (already used by the orchestrator).
- Produces: `QuoteParameterFinder.FindQuoteParameters(SemanticModel, SyntaxNode) : IEnumerable<QuoteParameter>` where `QuoteParameter { ParameterSyntax Parameter; IReadOnlyList<IdentifierNameSyntax> References; }`.

- [ ] **Step 1: Write the headline failing compile-assert test** in `InjectedSurfaceCompletenessTest` — `QuoteParam_InLoopCondition_KeepsRuntimeLoop_Compiles()`. Template source (const string, references ONLY injected surface + Roslyn + std, both generators in the driver):

```csharp
[Template(typeof(Factory), Options = TemplateOption.Bare)]
static void Loop([Quote] int count) {
    int ret = 0;
    for (int i = 0; i < count; i++) ret++;
}
```

Assert: generator diagnostics empty; factory file exists; **post-generation `output.GetDiagnostics()` has no errors** (the compile gate); and the generated factory text **contains** `for (int i = 0; i < ` and `< count` is gone in favor of a literal-bounded loop bound spliced from the `count` parameter (assert the factory takes an `int count` parameter and the loop survives — NOT four `ret++` statements). Mirror the structure of `SpliceGenericTypeArg_InTypePosition_Compiles`.

- [ ] **Step 2: Run it; verify it fails** — `[Quote]` is injected (Task 1) but unrecognized, so `count` is emitted as a free identifier → `output.GetDiagnostics()` reports `CS0103`/unbound `count`, OR the factory has no `count` parameter. Expected: FAIL at the compile gate.
- [ ] **Step 3: Implement** `QuoteParameterFinder` + orchestrator wiring: discover `[Quote]` params, generate the factory parameter (name + type, fully-qualified if needed), lift each reference via Task 2's helper into a `syntaxForQuote_<name>` preamble local, map each reference identifier in `unquotedReplacements`, trim the parameter. **Do not** add the symbol to the `StagedRoot` set.
- [ ] **Step 4: Run; verify PASS.**
- [ ] **Step 5: Add the equivalence + converter + classifier snapshot/unit tests:**
  - `SimpleTemplateTest.QuoteValue_InValuePosition_EmitsSameLiteralAsUnquote` (snapshot): `[Quote] int n` returned/used in value position emits the identical literal lift as the existing `[Unquote] int` snapshot.
  - `SimpleTemplateTest.QuoteCustomType_EmitsRuntimeConverterCall` (snapshot, modeled on `RuntimeConverterTest`): `[Quote] Rgb color` emits the `global::…ToSyntax(color)` converter call, **not** a literal — guards the "`[Literal]` would be a misnomer" rationale.
  - `BindingTimeClassifierTest.QuoteParam_DrivingControl_StaysQuoted`: a `for`/`foreach` whose driver references only a `[Quote]` parameter classifies as `Quoted` (not `StagedControl`).
  - `SimpleTemplateTest.QuoteParamTemplate_IsIncrementalOnUnrelatedEdit` (cacheability): mirror `GeneratorIsIncrementalOnUnrelatedEdit` with a `[Quote]` template; `CacheabilityAssert` shows **all** tracked steps `Cached`/`Unchanged`.
- [ ] **Step 6: Run all; accept snapshots; green gate + commit (by-path, include new snapshots).**

```bash
jj commit -m "feat(templating): [Quote] value parameter lifts without staging (loop kept)" \
  src/Synto.SourceGenerator/Templating/QuoteParameterFinder.cs \
  src/Synto.SourceGenerator/Templating/TemplateFactorySourceGenerator.cs \
  'glob:"test/Synto.Test/**"'
```

---

### Task 4: Inline `Quote(value)` — finder + emit + classifier liveness boundary

`Quote(x)` lifts the factory-time value `x` to syntax **at the call site** and is an **output-world boundary**: a live argument inside `Quote(...)` does not make the enclosing construct staged. This is the only way to keep a loop whose bound is a **computed** factory-time value.

**Files:**
- Create: `src/Synto.SourceGenerator/Templating/QuoteCallFinder.cs` (mirror `SpliceCallFinder.cs`)
- Modify: `src/Synto.SourceGenerator/Templating/TemplateFactorySourceGenerator.cs` (wire; lift the argument at the call node)
- Modify: `src/Synto.SourceGenerator/Templating/BindingTimeClassifier.cs` (`ReferencesStaged` boundary)
- Test: `InjectedSurfaceCompletenessTest.cs` (compile-assert), `BindingTimeClassifierTest.cs` (boundary), `SimpleTemplateTest.cs` (cacheability)

**Interfaces:**
- Consumes: Task 2 `TryEmitValueLift`; the classifier's existing `_live` set.
- Produces: `QuoteCallFinder.FindQuoteCalls(SemanticModel, SyntaxNode) : IEnumerable<QuoteCall>` where `QuoteCall { InvocationExpressionSyntax Invocation; ExpressionSyntax ValueArgument; }`. The set of `Quote(...)` invocation nodes is also passed to `BindingTimeClassifier` so it can shield them.

- [ ] **Step 1: Write the failing compile-assert test** `QuoteCall_OverComputedBound_KeepsRuntimeLoop_Compiles()`:

```csharp
[Template(typeof(Factory), Options = TemplateOption.Bare)]
static void Loop([Unquote] int count) {
    int ret = 0;
    var bound = Unquote(count);               // live computed local
    for (int i = 0; i < Quote(bound); i++)    // Quote shields liveness -> runtime loop
        ret++;
}
```

Assert: generator diagnostics empty; factory exists; **post-generation compile has no errors**; generated text keeps the `for` loop (a literal-bounded runtime loop), **not** an unroll. Contrast pin: a sibling assertion (or the existing `StagedFor_Unrolls`) shows the same loop **without** `Quote(...)` unrolls — proving `Quote(...)` is what shields it.
- [ ] **Step 2: Run; verify it fails** — without the classifier boundary, `for (i < Quote(bound))` references the live `bound`, so `ReferencesStaged(condition)` is true → the loop is `StagedControl` → unrolled (or the `Quote` call is unrecognized). Expected: FAIL (output is unrolled / `Quote` unbound).
- [ ] **Step 3: Implement** `QuoteCallFinder` + orchestrator wiring (lift `ValueArgument` via Task 2's helper, map the **invocation node** in `unquotedReplacements` to the lifted syntax) **and** the classifier boundary: `ReferencesStaged` must not count identifiers nested inside a recognized `Quote(...)` invocation; such a `Quote(...)` node classifies as `Quoted` regardless of its argument's liveness.
- [ ] **Step 4: Run; verify PASS** (loop kept, compiles).
- [ ] **Step 5: Add boundary unit + cacheability tests:**
  - `BindingTimeClassifierTest.QuoteCall_ShieldsLiveArg_FromStagingContainer`: a `for` whose condition is `Quote(<live>)` classifies `Quoted`; the same condition as a bare `<live>` classifies `StagedControl` (the contrast).
  - `SimpleTemplateTest.QuoteCallTemplate_IsIncrementalOnUnrelatedEdit` (cacheability): all tracked steps `Cached`/`Unchanged`.
- [ ] **Step 6: Run all; accept snapshots; green gate + commit (by-path).**

```bash
jj commit -m "feat(templating): inline Quote(value) lift + classifier liveness boundary" \
  src/Synto.SourceGenerator/Templating/QuoteCallFinder.cs \
  src/Synto.SourceGenerator/Templating/TemplateFactorySourceGenerator.cs \
  src/Synto.SourceGenerator/Templating/BindingTimeClassifier.cs \
  'glob:"test/Synto.Test/**"'
```

---

### Task 5: Round-trip + demo + friction wrap-up

Prove the feature end-to-end against the re-parse round-trip harness and dog-food it in the examples; close out the friction log.

**Files:**
- Modify: `test/Synto.Test/RoundTripTests.cs` (add `QuoteLoop` — the compact-loop sibling of the unrolling `Loop` Test5)
- Modify: `examples/Synto.Examples/Program.cs` (a `[Quote]` demo beside the `[Unquote]` `Loop`)
- Modify: `docs/superpowers/notes/2026-06-27-live-staged-templates-friction.md`

**Interfaces:**
- Consumes: the Task 3 `[Quote]` parameter path.

- [ ] **Step 1: Write the round-trip test** `RoundTripTests.QuoteLoop`: a `[Quote] int count` template; assert `Factory.QuoteLoop(4)` produces a **literal-bounded runtime loop** block:

```csharp
string expected = """
                  {
                      int ret = 0;
                      for (int i = 0; i < 4; i++)
                      {
                          ret++;
                      }
                  }
                  """;
```

(The deliberate counterpoint to Test5, whose `[Unquote] count` unrolls to four `ret++`. A comment should name the contrast and cite spec §3.)
- [ ] **Step 2: Run; verify PASS.**
- [ ] **Step 3: Add the `Program.cs` demo** — a `[Quote]`-bounded loop beside the existing `[Unquote] Loop`, printing both shapes so the example output shows kept-loop vs unrolled.
- [ ] **Step 4: Append friction findings** to the friction note (seam strain, any place the classifier boundary felt sharp, DRY wins/losses from the Task 2 extraction). Empty is acceptable — do not manufacture.
- [ ] **Step 5: Full green gate** (`build` · `test --no-build` · `format whitespace --verify-no-changes`) + commit (by-path).

```bash
jj commit -m "test(templating): [Quote] compact-loop round-trip + example demo" \
  test/Synto.Test/RoundTripTests.cs examples/Synto.Examples/Program.cs \
  docs/superpowers/notes/2026-06-27-live-staged-templates-friction.md
```

---

## Self-Review (against spec `2026-06-27-quote-value-marker-design.md`)

- **§3 contract (value-lift, never a staging root, diverges only in control position):** Task 3 (param: stays-quoted classifier test + loop-kept compile-assert) + Task 4 (inline: shield + compile-assert). ✓
- **§3 value-position equivalence with `[Unquote]`:** Task 3 Step 5 (`QuoteValue_InValuePosition_EmitsSameLiteralAsUnquote`). ✓
- **§4 two forms, no `Quote<T>()`:** Task 1 (`[Quote]` + `Quote<T>(T value)` only). ✓
- **§4 computed-bound only expressible via `Quote(value)`:** Task 4 headline test. ✓
- **§5 purely additive, `[Unquote]` unchanged:** Task 2 is behavior-preserving (zero snapshot diffs); no task edits `[Unquote]` semantics or the `27c7b1d0` commit. ✓
- **§6.1 value axis only:** Task 1 `AttributeTargets.Parameter`; Global Constraint notes `CS0592` is the mechanical enforcement. ✓
- **§6.2 no `Quote<T>()`; §6.3 no diagnostics:** Locked Names + Global Constraints (reuse `SY1011`/`SY1012`, no new descriptors). ✓
- **§7 `[Runtime]`-converter `[Quote]` emits constructor call, not literal:** Task 3 Step 5 (`QuoteCustomType_EmitsRuntimeConverterCall`). ✓
- **§7 cacheability:** Task 3 + Task 4 each ship an incremental guard asserting all tracked steps cached. ✓
- **Placeholder scan:** no TBD/TODO; every task has concrete test scenarios + asserts. ✓
- **Type consistency:** `QuoteParameter`/`QuoteCall`/`TryEmitValueLift`/`FindQuoteParameters`/`FindQuoteCalls` used consistently across tasks. ✓

# `[Unquote]` value-params lift on splice — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Spec:** `docs/superpowers/specs/2026-07-05-unquote-param-lift-on-splice-design.md` (approved).

**Goal:** Make `[Unquote] T x` value-parameters mean the same thing as the in-body `Unquote<T>()` spellings — lift-on-splice, control-without-lift — by extracting a shared `StagedRootLifter` core, then dog-food the shipped carrier-field live root so ObjectReader declares `columns` ONCE instead of six times.

**Architecture:** `LiftStagedRootParameters` (the `[Unquote]` param path) today eager-lifts every marked parameter via `ValueLift.TryEmitValueLift`, so a converterless, control-only param dies with SY1008 even though its value is never spliced. `LiftStagedParameters` (the in-body path) already partitions references into depth-zero vs consumed (live-region / `[Splice]`-generator / builder-staged-spine / fold-claimed) and lifts only depth-zero. The fix extracts that shared core into `StagedRootLifter`, parameterized by an adapter over the two staged-root shapes plus a per-path **lift strategy** delegate: param path keeps `ValueLift.TryEmitValueLift` (converter discovery + SY1008/SY1009), in-body path keeps direct `value.ToSyntax()` (byte-identical existing output — the seam exists precisely so in-body snapshots cannot move). The dog-food change is pure example-source refactoring on the already-shipped carrier field-initializer root (branch Task 1, commit `b0f5cc00`); it needs no generator change and must produce byte-identical ObjectReader output.

**Tech Stack:** C# Roslyn incremental source generators; jj (jj-native workflow); `dotnet test` (SDK 10 / MTP); Verify snapshot tests.

## Global Constraints

- **Branch/base:** implementation lands on `experimental/carrier-scoped-live-root`. **NEVER touch `main`** or any other run's bookmarks. `main` has MOVED to `ed1f3ad4` ("feat(templating): adopt [Constant] on Case; migrate ObjectReader TypedGetter labels") — the carrier branch (`b0f5cc00`) and this spec's docs commits sit on a pre-`[Constant]` line, so **Task 1 rebases the branch onto current `main` before any implementation**. The ObjectReader `TypedGetter`/`Case` call sites on `main` differ from the stale line — Task 4 edits the **post-rebase** file, never text remembered from the old line.
- **Test invocation convention (verbatim, SDK 10 MTP):** raw `dotnet test --project test/Synto.Test/Synto.Test.csproj --filter-method "*Name*"` (SDK 10 MTP — NOT rtk, NOT bare `--filter`). Class filter: `--filter-class`. ObjectReader byte-identical proof: `dotnet test --project examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Tests/Synto.Example.ObjectReader.Tests.csproj`.
- **TDD:** each task writes its failing test first, then the minimal implementation. Generator tests go in `test/Synto.Test/`. (Task 1 is preflight and Task 4 is an output-preserving refactor — their executable spec is the existing suites staying green/byte-identical; see the memory ruling "generated output: semantic not byte-identical" for showcase refactors, which here is even stronger: byte-identical is expected and asserted.)
- **Byte-identical guardrails:** every existing `Synto.Test` snapshot and the ObjectReader suite (10/10) stay byte-identical — **zero `.received.cs` files anywhere** after each task. The per-path lift seam (in-body keeps direct `value.ToSyntax()`) exists to *guarantee* in-body output is unchanged; Task 2's verification checks this explicitly.
- **Diagnostics are SY1008 / SY1009** — `TemplateDiagnostics.NoRuntimeConverter` (missing converter) and `TemplateDiagnostics.AmbiguousRuntimeConverter` (more than one), defined at `src/Synto.Generators/Templating/Diagnostics/TemplateDiagnostics.cs:56` and `:70`. The spec's "SY1011/SY1012" mentions (and the `ValueLift.cs` doc comment) are **stale numbering** — reference the descriptor methods, assert the real IDs (existing tests `NoRuntimeConverterReportsSY1008` / `AmbiguousRuntimeConvertersReportSY1009` in `test/Synto.Test/Templating/RuntimeConverterTest.cs` are the precedent).
- **Cacheability:** all work stays semantic, inside the transform; no new pipeline steps; every touched step keeps its tracking name; `CacheabilityAssert.AllStepsCachedOrUnchanged` guards stay green.
- **Step order is load-bearing:** `TemplateFactoryBuilder.Build` (`src/Synto.Generators/Templating/Emit/TemplateFactoryBuilder.cs:32-107`) documents that any step reordering changes generated output. The shared helper is *called from* the existing step positions (`LiftStagedParameters` at `:73`, `LiftStagedRootParameters` at `:77`, after `InitValueLift` at `:76`); no step moves. Spec §2.3: the in-body path does not consume `ctx.ValueLift`, so no reordering is required.
- **Non-goals (spec §2.4):** no lift-mechanism unification (in-body is NOT migrated onto `ValueLift`); no new diagnostic for a control-only `[Unquote]` param (silence matches the in-body path); `TypedGetter`'s locals are NOT converted to `[Unquote]` params (spec §3.2 supersedes the old carrier plan's Task 2).
- **Contracts, not syntax:** tasks pin interfaces + observable behavior. Test snippets below are the behavioral contract; implementation bullets name responsibilities and seams, not literal code.

---

## File Structure

**New:**
- `src/Synto.Generators/Templating/Lifting/StagedRootLifter.cs` — the shared staged-root lift core (spec §2.2). Lives beside `ValueLift.cs`; `internal static`.
- `test/Synto.Test/Templating/UnquoteParamLiftOnSpliceTest.cs` — the §2.1 behavior table as tests (control-only / mixed / converter diagnostics / cacheability).

**Modified:**
- `src/Synto.Generators/Templating/Emit/TemplateFactoryBuilder.cs` — `LiftStagedParameters` (~`:485-569`) and `LiftStagedRootParameters` (~`:650-725`) both delegate their shared core to `StagedRootLifter`. Param-path specifics stay put: the generic type-parameter declaration block (`:670-678`) runs before the shared call; the `ctx.ConverterError` bail stays with the param path. In-body-path specifics stay put: the scalar-fold emission (`FoldsByStagedParameter`, `:498-513`) and the string-typed decision points.
- `examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Generator/ReaderTemplate.cs` — Task 4 dog-food (post-rebase state).

**Unchanged (load-bearing):**
- `src/Synto.Generators/Templating/Lifting/ValueLift.cs` — the param path's lift strategy, untouched.
- `src/Synto.Generators/Templating/Discovery/StagedRootFinder.cs` (`StagedParameterRoot`, `StagedLocal`) and `Discovery/StagedParameterFinder.cs` (`StagedParameter`) — the shapes the adapter maps over; discovery itself does not change.
- Every existing snapshot under `test/Synto.Test/snapshots/` and `examples/.../snapshots/`.

---

## Task 1: Preflight — rebase `experimental/carrier-scoped-live-root` onto current `main`

**Files:**
- No source edits authored by hand. The rebase carries `da98a336` (carrier spec), `7ea9afa7` (carrier plan), `b0f5cc00` (Task 1: carrier field-initializer live root — touches only `src/Synto.Generators/Templating/Discovery/StagedParameterFinder.cs`, `test/Synto.Test/Templating/CarrierFieldRootTest.cs`, and its two snapshots), plus this spec's docs commit(s) if they sit on the same line.

**Interfaces:**
- Consumes: `main` at `ed1f3ad4` or later ("adopt [Constant] on Case; migrate ObjectReader TypedGetter labels").
- Produces: `experimental/carrier-scoped-live-root` descending from current `main`, all suites green — the base every later task builds on. Tasks 2-4 assume the carrier field-initializer root (`CarrierFieldRootTest`) works on the `[Constant]`-era code.

**Contract to satisfy:** the branch (and the spec/plan docs commits on its line) end up stacked on current `main`; nothing else moves; `main` itself is untouched.

- [ ] **Step 1: Rebase.** From the workflow's own workspace (do NOT reparent the shared default `@`):

```bash
jj rebase -b experimental/carrier-scoped-live-root -d main
```

If the spec commit (`docs(spec): [Unquote] value-param lift-on-splice design`, was `1bce2449`) is a sibling of the branch tip rather than an ancestor, rebase its line too (`jj rebase -s <spec-commit> -d <rebased-branch-tip>` or `-d main` — end state: everything this plan needs descends from current `main`).

- [ ] **Step 2: Resolve conflicts, if any.** Expected: none or trivial — `b0f5cc00` touches only `StagedParameterFinder.cs` + its test/snapshots, and `main`'s `[Constant]`/`TemplateExtent` work touched different regions. If `StagedParameterFinder.cs` conflicts, keep BOTH: main's structure + the branch's field-declaration recognition (a `FieldDeclarationSyntax` single-declarator `= Unquote<T>()` initializer registers as a declaration-position site, deduped by `(name, T)`).

- [ ] **Step 3: Verify the rebased base is green.**

Run: `dotnet build`
Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj`
Run: `dotnet test --project examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Tests/Synto.Example.ObjectReader.Tests.csproj`
Expected: all PASS, including `CarrierFieldRootTest` (proves the shipped field-root feature survived the rebase onto the `[Constant]` state).

- [ ] **Step 4: Assert zero snapshot drift.**

```bash
git status --porcelain | grep -i "received" ; echo "exit=$?"
```

Expected: no `.received.cs` / `.received.txt` files (exit=1 from grep).

- [ ] **Step 5: No commit needed** — the rebase rewrote the commits in place. Record the new branch tip change-id in the task notes for later tasks.

---

## Task 2: Control-only `[Unquote]` value-param compiles — shared `StagedRootLifter`

**Files:**
- Create: `src/Synto.Generators/Templating/Lifting/StagedRootLifter.cs`
- Modify: `src/Synto.Generators/Templating/Emit/TemplateFactoryBuilder.cs` (`LiftStagedParameters`, `LiftStagedRootParameters`)
- Test: `test/Synto.Test/Templating/UnquoteParamLiftOnSpliceTest.cs` (new; mirror `CarrierFieldRootTest.cs` harness conventions — same references block, `TemplateFactorySourceGenerator` driver, Verify `UseDirectory("snapshots")`, `TestContext.Current.CancellationToken`)

**Interfaces:**
- Consumes: `ctx.RegionConsumedNodes`, `ctx.SpliceGeneratorNodes`, `ctx.BuilderStagedConsumedNodes`, `ctx.FoldClaimedReferences`, `ctx.RootNames`, `ctx.Plan` (Replacements/Trim/StringRoots), `ctx.Shape` (Parameters/Preamble), `ctx.Names.ParamNames`, `ctx.LiveFragmentInfos` — all on `TemplateBuildContext`; `ValueLift.TryEmitValueLift` (param-path strategy only).
- Produces: `internal static class StagedRootLifter` with one entry point both paths call per staged root. Suggested shape (pin the *responsibilities*, adjust the signature as implementation demands):
  - an adapter input carrying: resolved `paramName` (post-dedupe), the factory-parameter `TypeSyntax` (resolved by the CALLER — see warning below), `IReadOnlyList<SyntaxNode> references`, the root `ISymbol`s to wire, the trim node(s), whether the value type is `System.String`, and a **lift-strategy delegate** `Func<ExpressionSyntax /*valueAccess*/, ExpressionSyntax /*conversion*/>` (in-body: direct `value.ToSyntax()` invocation node; param: the conversion `TryEmitValueLift` already produced).
  - Tasks 3-4 rely on the behavior, not the signature.

**Contract to satisfy (spec §2.1 + §2.2):** the helper owns, in this order-preserving way:
1. **Reference partition** — `depthZeroReferences` = references minus (`RegionConsumedNodes` ∪ `SpliceGeneratorNodes` ∪ `BuilderStagedConsumedNodes` ∪ `FoldClaimedReferences`) — the identical partition `LiftStagedParameters` computes today at `TemplateFactoryBuilder.cs:538`.
2. **Gated lift** — emit the `syntaxForParam_<name>` preamble local, add a `Plan.Replacements` entry per depth-zero reference, and add the SY1035 tap-2 `LiveFragmentInfos` highlight per depth-zero reference, **only if** `depthZeroReferences.Count > 0`. Control-only ⇒ no preamble local, no replacements, no converter demand.
3. **Control-region wiring** — `ctx.RootNames[symbol] = paramName` for every root symbol (mirrors `:517-518`), so unrolled control-region references rewrite to the raw factory parameter (this is NEW for the param path — today it works only by name coincidence).
4. **String-roots** — when the value type is `string`, map EVERY reference (depth-zero AND consumed) in `Plan.StringRoots` to `IdentifierName(paramName)` (mirrors `:527-531` / `:717-721`).
5. **Trim + Parameter** — `Plan.Trim` the carrier node(s); append the factory `Parameter` to `ctx.Shape.Parameters`.

Path-specific pieces that stay OUTSIDE the helper, in their current homes:
- **Param path** (`LiftStagedRootParameters`): name dedupe against `ctx.Names.ParamNames`; the generic type-parameter declaration block (`[Unquote] T` with open `T`); calling `ValueLift.TryEmitValueLift` to build the conversion **only when the partition is non-empty** — a control-only param must never reach the converter — and the `ctx.ConverterError` bail on a failed lift; the factory-parameter `TypeSyntax` resolution (declared syntax type for built-ins, fully-qualified display for concrete non-built-ins, the declared identifier for inlined type params). For a **control-only** param of a concrete non-built-in type, emit the fully-qualified declared type (same rule as the in-body path — the generated file must resolve it without the carrier's usings).
- **In-body path** (`LiftStagedParameters`): the scalar-fold emission (`FoldsByStagedParameter`), name dedupe, its always-fully-qualified parameter type, and the direct `value.ToSyntax()` conversion node (**unchanged** — this is the byte-identical seam; do NOT route it through `ValueLift`).

**Warnings:**
- Do not change the relative append order of `Shape.Preamble` / `Shape.Parameters` entries for any input that generates output today — order is load-bearing and snapshots are byte-compared.
- Narrowing the param path's tap-2 highlights to depth-zero refs is intended (mirrors in-body); consumed refs are covered by the region emitter's own SY1035 taps and `LiveFragmentMerge` dedupes by location — the staging/highlight tests must stay green as-is.
- `[Unquote]` on a *type parameter* (e.g. ObjectReader's `TypedGetter<[Unquote] TRet>`) flows through `StagedTypeParameterFinder`/`LiftStagedTypeParameters`, NOT this path — untouched.

- [ ] **Step 1: Write the failing test.** In new `test/Synto.Test/Templating/UnquoteParamLiftOnSpliceTest.cs` (harness copied from `CarrierFieldRootTest.cs`):

```csharp
// Spec §2.1 "Control-only": a converterless [Unquote] param whose every reference is
// control-consumed compiles clean, drives unrolling, and never asks for a converter. (Was SY1008.)
private const string ControlOnlySource =
    """
    using Synto.Templating;
    using System.Collections.Generic;
    partial class Factory {}
    public class TestClass {
        [Template<Factory>]
        void Build([Unquote] IReadOnlyList<int> ordinals) {
            foreach (var o in ordinals)
                System.Console.WriteLine(o);
        }
    }
    """;

[Fact]
public async Task ControlOnlyUnquoteParam_NoConverter_CompilesAndUnrolls()
{
    var compilation = CompilationWithSource(ControlOnlySource);
    var result = _driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
    var runResult = result.GetRunResult();

    // No SY1008/no errors: the value is never spliced, so no converter is required.
    Assert.Empty(runResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

    var doc = runResult.Results.SelectMany(r => r.GeneratedSources)
        .Single(s => s.HintName.Contains("Factory.Build", StringComparison.Ordinal))
        .SourceText.ToString();

    // The caller-supplied factory parameter exists (fully-qualified declared type)...
    Assert.Contains("IReadOnlyList<int> ordinals)", doc, StringComparison.Ordinal);
    // ...drives a factory-time (scaffold) foreach, not a quoted output loop...
    Assert.Contains("foreach (var o in ordinals)", doc, StringComparison.Ordinal);
    Assert.DoesNotContain("ForEachStatement", doc, StringComparison.Ordinal);
    // ...and no value lift is emitted at all (no preamble local, no ToSyntax on the param).
    Assert.DoesNotContain("syntaxForParam_ordinals", doc, StringComparison.Ordinal);

    var ret = await Verify(result).UseDirectory("snapshots");
    Assert.NotEmpty(ret.Files);
}
```

- [ ] **Step 2: Run it — verify it fails on the bug.**

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj --filter-method "*ControlOnlyUnquoteParam*"`
Expected: FAIL — the empty-diagnostics assertion trips on `SY1008` ("No value-to-syntax converter found for inlined type 'System.Collections.Generic.IReadOnlyList<int>'"), and no `Factory.Build` document is generated (the `ConverterError` bail).

- [ ] **Step 3: Implement.** Create `StagedRootLifter.cs` per the contract above; rewrite the shared cores of `LiftStagedParameters` and `LiftStagedRootParameters` as calls into it with their per-path strategies. Keep the existing step call sites and order in `Build` untouched.

- [ ] **Step 4: New test green + accept its NEW snapshot only.**

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj --filter-method "*ControlOnlyUnquoteParam*"`
Expected: PASS after accepting the test's own new `.verified.*` files (a brand-new test has no prior snapshot — this is the only snapshot acceptance allowed in this task).

- [ ] **Step 5: Full-suite byte-identical proof.**

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj`
Run: `dotnet test --project examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Tests/Synto.Example.ObjectReader.Tests.csproj`
Expected: all PASS; zero `.received.cs` (`git status --porcelain | grep -i received` finds nothing). Any existing-snapshot diff means the in-body seam leaked — fix the implementation, never re-accept an existing snapshot in this task. The cacheability guards (e.g. `--filter-method "*GeneratorIsIncremental*"`) are part of the suite and must be green.

- [ ] **Step 6: Commit.**

```
feat(templating): [Unquote] value-params lift on splice via shared StagedRootLifter

Plan: unquote-param-lift-on-splice
Plan-Tasks: Task 2
```

---

## Task 3: Pin the §2.1 table — mixed param, converter diagnostics, cacheability

**Files:**
- Modify: `test/Synto.Test/Templating/UnquoteParamLiftOnSpliceTest.cs` (add tests; extend the references block with the Roslyn assemblies, mirroring `RuntimeConverterTest.References`, for the `[Runtime]` converter sources)
- Possibly modify: `src/Synto.Generators/Templating/Lifting/StagedRootLifter.cs` / `TemplateFactoryBuilder.cs` (only if a test exposes a Task-2 gap)

**Interfaces:**
- Consumes: Task 2's behavior (partition + gated lift + `RootNames` wiring) and `TemplateTrackingNames.GenericTransform` / `TemplateTrackingNames.GenericResult` + `CacheabilityAssert.AllStepsCachedOrUnchanged` (existing, `test/Synto.Test/CacheabilityAssert.cs`).
- Produces: regression locks only — no new production surface.

**Contract to satisfy (spec §2.1 rows 1 and 3 + §4):**
- **Mixed** (control + spliced, converter present): depth-zero refs lift via the discovered `[Runtime]` converter; control refs rewrite to the raw factory parameter and the region unrolls.
- **Spliced-anywhere, no converter**: still SY1008 (converter genuinely required); **ambiguous**: still SY1009 — the existing pure-spliced locks (`RuntimeConverterTest.NoRuntimeConverterReportsSY1008` / `AmbiguousRuntimeConvertersReportSY1009`) must still pass untouched; this task adds the control+spliced variant of the SY1008 case.
- **Cacheability**: an unrelated-tree edit leaves all tracked steps Cached/Unchanged for the new control-only shape.

TDD note: these tests pin behavior Task 2 introduced as one unit (the partition cannot land piecemeal). Write each test, run it; if any FAILS, that is a Task-2 implementation gap — fix it in `StagedRootLifter` in this task and note it in the commit. If they pass first run, they are the executable-spec regression locks the spec's §4 demands.

- [ ] **Step 1: Write the mixed-param test.**

```csharp
// Spec §2.1 "Mixed": one [Unquote] param both drives a live foreach (control) and is spliced
// (depth-zero). Depth-zero lifts via the [Runtime] converter; control refs rewrite to the raw
// factory parameter and the loop unrolls.
private const string MixedWithConverterSource =
    """
    using System;
    using System.Collections.Generic;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Synto.Templating;
    using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
    partial class Factory {}
    [Runtime]
    public static class OrdinalsConverter {
        public static ExpressionSyntax ToSyntax(this IReadOnlyList<int> value) => IdentifierName("ords");
    }
    public class TestClass {
        [Template<Factory>]
        void Build([Unquote] IReadOnlyList<int> ordinals) {
            foreach (var o in ordinals)
                Console.WriteLine(o);
            Console.WriteLine(ordinals);   // depth-zero -> lifts via the converter
        }
    }
    """;
```

Assertions (same run/inspect pattern as Task 2): no error diagnostics; generated doc contains the fully-qualified converter call (`global::OrdinalsConverter.ToSyntax(ordinals)`) and the `syntaxForParam_ordinals` preamble local; the scaffold `foreach (var o in ordinals)` is present; `ForEachStatement` is absent; Verify snapshot (new).

- [ ] **Step 2: Write the control+spliced-no-converter test.** Same template source minus the `[Runtime]` converter class: assert exactly one error, `Assert.Single(runResult.Diagnostics, d => d.Id == "SY1008")`, with a real span (mirror `RuntimeConverterTest.AssertHasRealSpan`). The spliced reference makes the converter genuinely required — control usage elsewhere must not mask it.

- [ ] **Step 3: Write the cacheability guard.** Mirror `RuntimeConverterTest.GeneratorIsIncrementalWithRuntimeConverter` over `ControlOnlySource`: run with `trackIncrementalGeneratorSteps: true`, add an unrelated tree, re-run, then `CacheabilityAssert.AllStepsCachedOrUnchanged(result, new[] { TemplateTrackingNames.GenericTransform, TemplateTrackingNames.GenericResult })`.

- [ ] **Step 4: Run the new tests.**

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj --filter-class "*UnquoteParamLiftOnSpliceTest*"`
Expected: PASS (accepting only the mixed test's own NEW snapshot). If the mixed test fails on the control-ref rewrite or the diagnostics test double-fires, fix the `StagedRootLifter` partition/`RootNames` wiring here.

- [ ] **Step 5: Full suites still byte-identical.**

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj`
Run: `dotnet test --project examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Tests/Synto.Example.ObjectReader.Tests.csproj`
Expected: all PASS; zero `.received.cs`; existing `RuntimeConverterTest` SY1008/SY1009 locks untouched and green.

- [ ] **Step 6: Commit.**

```
test(templating): pin [Unquote] lift-on-splice mixed/converter/cacheability contract

Plan: unquote-param-lift-on-splice
Plan-Tasks: Task 3
```

---

## Task 4: Dog-food — `columns` declared ONCE as a carrier field in ObjectReader

**Files:**
- Modify: `examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Generator/ReaderTemplate.cs` (the POST-REBASE file — `main` migrated the `TypedGetters()` `Case` labels to `[Constant]` raw values; read the file fresh before editing)
- Verify only (must NOT change): `examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Tests/snapshots/**` (all 10 byte-identical)

**Interfaces:**
- Consumes: the shipped carrier field-initializer live root (branch Task 1, `b0f5cc00`): a single-declarator field `= Unquote<T>()` initializer is a declaration-position staged parameter, `(name, T)`-deduped with any leftover sites, seeded as a live root for every member, and stripped from quoted output. NOT the §2 generator fix — every parent-scope `columns` reference is control-only or fold-claimed, so this task is independent of Tasks 2-3 (spec §3).
- Produces: example-source refactor only; no interface changes.

**Contract to satisfy (spec §3):**
- `ObjectReaderTemplate<T>` declares the live column list ONCE as a carrier field: `private static readonly EquatableArray<ColumnInfo> columns = Unquote<EquatableArray<ColumnInfo>>();` placed with the other fields at the top of the class.
  - **`static` is required** (a deliberate reconciliation of the spec's illustrative non-static snippet): the `[Splice] TypedGetters()` member-generator is a `static` method — after its local is deleted, its `columns` references bind to the field, and an instance field would be CS0120 (the carrier is real, compiling C#). The finder recognizes the field syntactically regardless of modifiers, and the field is stripped from quoted output, so staticness cannot affect generated output. Instance members reading a static field is legal C#.
- Delete the six parent-scope `var columns = Unquote<EquatableArray<ColumnInfo>>();` declaration lines — in `FieldCount` (getter), `GetName`, `GetOrdinal`, `GetFieldType`, `GetValue`, and `[Splice] TypedGetters()` — and change **nothing else** (no reshaping bodies into expression forms, no comment rewrites beyond lines that describe the deleted redeclarations). The `(name, T)` dedupe folds field + any leftover site into the same single factory parameter.
- **`TypedGetter` keeps its body locals** (`columns`, `clrTypeName`, `typeLabel`): it is a nested `[Template]` with its own factory scope; `TemplateScopedWalker` skips foreign child subtrees, so the parent's field is not seeded into it. Do not touch it (spec §3.2 — this supersedes the carrier plan's old Task 2).
- Output is **byte-identical**: every `columns` reference in the parent carrier is a foreach driver (region-consumed), the `columns.Count` scalar fold (fold-claimed), or build-time code inside the `[Splice]` generator — none is spliced, so no converter and no output change.

- [ ] **Step 1: Baseline.** Confirm the ObjectReader suite is green on the branch tip (it is, from the previous task's Step 5) and read the post-rebase `ReaderTemplate.cs` in full.

- [ ] **Step 2: Edit.** Add the single static readonly field; delete the six declaration lines. Trim any now-stale comment fragments that specifically narrate the per-member redeclaration (keep everything else verbatim).

- [ ] **Step 3: Carrier still compiles.**

Run: `dotnet build examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Generator/Synto.Example.ObjectReader.Generator.csproj`
Expected: SUCCESS, no new warnings that fail the build (the inert `Unquote<...>()` initializer returns `default!`; if an analyzer objects to the lowercase static field name in this example project, suppress locally alongside the file's existing pragma block rather than renaming — the name IS the factory parameter name and must stay `columns`).

- [ ] **Step 4: Byte-identical proof (the test for this task).**

Run: `dotnet test --project examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Tests/Synto.Example.ObjectReader.Tests.csproj`
Expected: 10/10 PASS, zero `.received.cs`. Any received file means a parent-scope `columns` reference was NOT control-only/fold-claimed — stop and diagnose (do not re-accept); the likely culprit is an accidental body reshape from Step 2.

- [ ] **Step 5: Full generator suite unaffected.**

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj`
Expected: all PASS, zero `.received.cs`.

- [ ] **Step 6: Commit.**

```
refactor(objectreader): declare columns once as a carrier field (dog-food field-init live root)

Plan: unquote-param-lift-on-splice
Plan-Tasks: Task 4
```

---

## Verification (whole plan, spec §4)

- Control-only converterless `[Unquote]` param: compiles with no error diagnostics, drives unrolling, never appears in output (Task 2 — was SY1008).
- Spliced `[Unquote]` param still lifts; genuinely missing converter still SY1008; ambiguous still SY1009 (Task 3 + existing `RuntimeConverterTest` locks).
- Mixed param: depth-zero lifts, control refs rewrite via `RootNames` (Task 3).
- Full `Synto.Test` green; cacheability all-steps-cached guards green (Tasks 2-3).
- ObjectReader 10/10 byte-identical, zero `.received.cs` (Task 4).

## Out of scope (spec §5)

- Primary-ctor `[Unquote]` param spelling for `columns` (viable after §2, not adopted — ObjectReader uses the field spelling).
- Converting `TypedGetter`'s locals to `[Unquote]` params (explicitly dropped, §3.2).
- Migrating the in-body lift onto `ValueLift` (§2.4 non-goal).
- Updating/archiving the superseded Task 2 of `docs/superpowers/plans/2026-07-05-carrier-scoped-live-root.md` — left to the session owner.

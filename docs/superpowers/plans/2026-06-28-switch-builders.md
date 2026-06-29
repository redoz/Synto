# Switch Builders — list-shaped `[SyntaxBuilder]` helpers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Each task is an independently green unit (builds + tests pass on its own) ending in a `rtk jj commit` step. Use superpowers:test-driven-development inside every task: write the failing test/snapshot first, then the implementation, then confirm green.

**Goal:** Ship a data-driven `switch` **statement** and **expression** as runtime `[SyntaxBuilder]` helpers in `Synto`, so a `[Template]` body can author `switch (i) { case <ordinal>: … }` / a switch-expression with one section/arm per item in a live-staged collection — something plain authored C# cannot express (you cannot `foreach`-generate `case` labels). This is the first worked example of the reusable **list-shaped builder** recipe (host node + element-builder list driven by LINQ over a live collection). No new generator *concepts*; it rides the existing `[SyntaxBuilder]` mechanism — though the empirical gate (Task 1) is explicitly permitted to surface *bounded* generator work if the spike proves it necessary.

**Architecture:** A pair of `[SyntaxBuilder]` shapes shipped in `src/Synto/Templating/SyntoBuilders.cs` (authored `public`, `<Compile Remove>`d from Synto.Core, injected `internal` into the consumer's generator compilation by `SurfaceInjectionGenerator` — exactly like the existing `Member`/`TypeOf`). A **host** builder (`Switch`, two overloads chosen by element type) takes a `[Quoted]` governing expression plus a `params` list of child nodes; **element** builders (`Case`/`Default` for sections, `Arm`/`WhenArm`/`DefaultArm` for arms, plus a trivial `Return` statement builder) produce the list items. The element list is authored as an ordinary LINQ projection over a live collection (`cols.Where(...).Select(c => Case(...))`) which the generator unrolls at factory-build time; element bodies are orthogonal — they can come from a trivial builder (`Return(Member<TRet>(_e.Current, c.Name))`) or, later, a child `[Template]`. Unlike `Member`/`TypeOf` (hand-authored facades on `Template`, recognized by binding in `FacadeCallFinder.TryBuiltIn`), the switch builders are user-style `[SyntaxBuilder]` methods: their inert facades are auto-synthesized by `SyntaxBuilderFacadeGenerator` and recognized structurally by name in `FacadeCallFinder.FindBuilderCalls`. **No hand-authored facade declarations on `Template` are added.**

**Tech Stack:** C# / Roslyn incremental source generators; carriers target `netstandard2.0`; xUnit v3 + Microsoft.Testing.Platform; Verify (`Verify.SourceGenerators`, `Verify.XunitV3`) snapshots; jj (use `rtk jj`).

## Global Constraints

- **Cacheable, syntax-only inputs.** Every builder parameter is equatable syntax/data: `ExpressionSyntax`, `TypeSyntax`, `StatementSyntax`, `SwitchSectionSyntax`, `SwitchExpressionArmSyntax`, primitives, `EquatableArray<…>`. **No `ISymbol`/`SemanticModel`** enters builder arguments — switch construction stays inside incremental-generator cacheability rules (nodes in, nodes out). The existing cacheability guards (`SpliceMemberGeneratorTest.SpliceMemberGeneratorTemplate_IsIncrementalOnUnrelatedEdit`, `ObjectReaderIncrementalTests`) MUST stay green; assert ALL tracked steps Cached/Unchanged, not just the terminal step.
- **No git pre-commit hooks.** Format via `dotnet format whitespace` + CI; do not add hooks.
- **Trivia / formatting.** Built nodes must normalize byte-identically after `dotnet format whitespace --verify-no-changes`. Builders emit minimally-trivia'd nodes and rely on the formatter; snapshot gates assume the formatter has run.
- **Bounded generator work is expected, and is scoped to Task 1.** This surface *requires* bounded generator widening (return-shape acceptance, overload disambiguation, and `params` emission/binding); the spike is the empirical gate that proves and lands it. The **authorized files** are `FacadeShape.cs`, `FacadeCallFinder.cs`, `FacadeArgumentBinder.cs`, `SyntaxBuilderFacadeGenerator.cs`, plus the runtime surface `SyntoBuilders.cs`. (The former monolithic `SyntaxBuilderFinder.cs` was split during the 2026-06-29 source-generator modularization: builder discovery + the `buildersByName` index + SY1018 now live in `FacadeCallFinder.cs`; per-call argument binding — formerly `TryUser` — now lives in `FacadeArgumentBinder.cs`; builder *enumeration* lives in `SyntaxBuilderRegistry.cs`.) **STOP and re-scope only if a change goes beyond these five files** — do not silently widen.
- **Out of scope (v1):** type patterns / property patterns (would need `[Quoted(AsTypeArg = true)]` / pattern-builder overloads — later); implicit if-chain→switch peephole (designed-out); the interpolated-string constant-folding gap (recorded as the next list-shaped-builder candidate, not built here).
- **Green gate (every task):** `rtk dotnet build`, `rtk dotnet test`, `rtk dotnet format whitespace --verify-no-changes` for the touched projects. A task is not done until all three are green for what it touched.

## The v1 builder surface (the contract — exact bodies are the implementer's choice)

Authored in `src/Synto/Templating/SyntoBuilders.cs` as `public static` `[SyntaxBuilder]` methods. Signatures are the binding contract; **do not paste full method bodies into the plan** — they are one-liners over `SyntaxFactory` the implementer finalizes during Task 1. `[Quoted]` ⇒ the call argument is quoted (author writes `i`, it becomes `IdentifierName("i")`); unmarked ⇒ a staged factory-build-time value (the projected list).

```
// host (statement) — element type selects this overload
[SyntaxBuilder] SwitchStatementSyntax     Switch([Quoted] ExpressionSyntax governing, params IEnumerable<SwitchSectionSyntax> sections);
// host (expression) — element type selects this overload
[SyntaxBuilder] SwitchExpressionSyntax    Switch([Quoted] ExpressionSyntax governing, params IEnumerable<SwitchExpressionArmSyntax> arms);

// statement sections
[SyntaxBuilder] SwitchSectionSyntax       Case([Quoted] ExpressionSyntax label, params IEnumerable<StatementSyntax> body);
[SyntaxBuilder] SwitchSectionSyntax       Default(params IEnumerable<StatementSyntax> body);

// expression arms
[SyntaxBuilder] SwitchExpressionArmSyntax Arm([Quoted] ExpressionSyntax patternConstant, [Quoted] ExpressionSyntax value);
[SyntaxBuilder] SwitchExpressionArmSyntax WhenArm([Quoted] ExpressionSyntax patternConstant, [Quoted] ExpressionSyntax guard, [Quoted] ExpressionSyntax value);
[SyntaxBuilder] SwitchExpressionArmSyntax DefaultArm([Quoted] ExpressionSyntax value);   // `_ =>` discard arm

// trivial body builder so the common case needs no child template
[SyntaxBuilder] StatementSyntax           Return([Quoted] ExpressionSyntax value);
```

**Observable behaviors (the snapshot/round-trip expectations):**
- `Switch(i, sections)` with a constant-label `Case(<const>, Return(<expr>))` projection ⇒ a `switch (i) { case <const>: return <expr>; … }` statement; an appended `Default(...)` ⇒ a trailing `default:` section.
- `Switch(i, arms)` with `Arm(<const>, <value>)` ⇒ a `i switch { <const> => <value>, … }` expression; `WhenArm(<const>, <guard>, <value>)` ⇒ `<const> when <guard> => <value>`; `DefaultArm(<value>)` ⇒ `_ => <value>`.
- The governing expression is quoted from the author's identifier; section/arm lists are staged (produced by the LINQ projection at factory-build time).
- Each built node normalizes byte-identically after `dotnet format whitespace`.

> **Open decision (settle in Task 1, do not pre-bake):** the exact label/pattern parameter shape for `Case`/`Arm`/`WhenArm` (a `[Quoted] ExpressionSyntax` constant vs. a pattern carrier) is finalized against whatever the synthesized facade actually accepts. The contract is "constant label/pattern is the quote of the author's expression; body/value is syntax." Likewise, whether `Return`/trivial body builders share the file with the switch builders is a packaging detail — keeping them all in `SyntoBuilders.cs` is the default.

---

## Task 1: Empirical gate — prove the three residual unknowns in isolation (spike)

This is the gate for the whole plan. Before ANY runtime surface is finalized, a standalone composition test must prove the three things the design rests on but that are currently unproven (spec "Empirical gate"). It is permitted to surface — and, within the five authorized files only (see Global Constraints), to fix — bounded generator work.

**The three unknowns this task must resolve:**
1. **Quoted-vs-staged binding for `Case`/`Return`/`Arm` (PRIMARY), over a LINQ projection into a staged builder argument.** The spike's first job is to **resolve the `[Quoted]`-vs-staged markers** on `Case`'s label, `Return`'s value, and the arm pattern/value — defaulting toward **STAGED**. Reason: in the motivating case `c.Ordinal` is a **build-time `int`** and `Member<TRet>(…)` is an **already-built node** passed through; marking either `[Quoted]` would emit `case c.Ordinal:` with `c` out of output scope (a CS error). The spike picks the real markers and the surface (Task 2) ships them. Mechanically this rides on: `cols.Where(...).Select(c => Case(c.Ordinal, Return(Member<TRet>(_e.Current, c.Name))))` evaluating at factory-build time into the host `Switch`'s staged list arg — i.e. a `[SyntaxBuilder]` call (`Case`/`Arm`) *inside a live `.Select` lambda* feeding a host builder. `foreach`/`.Where` over a live collection is proven; builder-call-inside-a-live-projection-as-a-builder-arg is not (`FacadeCallFinder.FindBuilderCalls` descends all `InvocationExpressionSyntax` via `DescendantNodesAndSelf` at `FacadeCallFinder.cs:40`, so lambda-nested facade calls *should* be discovered — confirm it). The marker resolution is the deliverable; the descent is the substrate that makes it observable.
2. **Overloaded `[SyntaxBuilder]` discovery.** Two `Switch` overloads differing only by list element type. ⚠️ **Predicted gap:** `FacadeCallFinder.FindBuilderCalls` indexes builders by simple name (`buildersByName`, `FacadeCallFinder.cs:29`) and reports **SY1018 ambiguous** whenever `candidates.Count > 1` (`FacadeCallFinder.cs:56–58`; descriptor `TemplateDiagnostics.cs:196`). Two `Switch` overloads will trip this. **Fix: bind by shared binding-shape compatibility.** When same-name candidates share a *compatible binding shape* (both `Switch` overloads are `[Quoted governing, Staged list]`), bind with the shared shape (e.g. `candidates[0]`) and let the **C# compiler** pick the real overload from the typed projection argument; emit SY1018 **only on a genuine shape conflict** (this keeps the existing `AmbiguousBuilder`/SY1018 test green). **AVOID** typing the `cols.Select(c => Case(...))` projection to read its element type: the nested `Case` is a structurally-recognized *unbound* facade the `SemanticModel` cannot type, and doing so risks pulling an `ITypeSymbol` into pipeline state (a cacheability hazard).
3. **`params IEnumerable<T>` facade synthesis + binding (INTENTIONAL generator work).** ⚠️ **Predicted gap:** `FacadeShape.Derive` emits a staged param's type via `ToDisplayString` (FacadeShape.cs:101) and `SyntaxBuilderFacadeGenerator.AppendFacadeMethod` emits `type name` with no `params` modifier (SyntaxBuilderFacadeGenerator.cs:104-114) — so the facade currently flattens `params IEnumerable<T>` to plain `IEnumerable<T>`. **Decision: keep `params`, teach the generator** (not a graceful degrade). Two bounded changes: (a) teach `SyntaxBuilderFacadeGenerator.AppendFacadeMethod` to **emit the `params` modifier** on the synthesized facade parameter; (b) teach `FacadeArgumentBinder.Bind` (the per-call binder formerly `SyntaxBuilderFinder.TryUser`, `FacadeArgumentBinder.cs:16`) to **bind the EXPANDED form** — collect trailing args into the params-collection binding — instead of binding only the first and silently dropping the rest. Task 1 *and* Task 2 tests must cover **both** the projection form (`Switch(i, cols.Select(...))`) and the expanded form (`Switch(i, Case(...), Case(...))`). Note: this raises the consumer's generator-project LangVersion floor to C# 13 (params collections) — acceptable, the repo is `preview`.

> **Also predicted (return-shape gate):** `FacadeShape.Derive` accepts a builder return type only when its FQ display is exactly `ExpressionSyntax` or `TypeSyntax` (FacadeShape.cs:72-80, else **SY1017**; descriptor `TemplateDiagnostics.cs:182`). Every switch builder returns something else (`SwitchStatementSyntax`, `SwitchSectionSyntax`, `SwitchExpressionArmSyntax`, `StatementSyntax`, and `SwitchExpressionSyntax`, which is *not literally* `ExpressionSyntax`). Widening this acceptance set (to the relevant syntax-node return types) is the most likely first bounded fix. Make the minimal widening that lets the v1 surface synthesize; do not generalize beyond the return types this surface needs. **Critical detail:** a node-typed builder's facade must return its **concrete node type verbatim** (i.e. `freshReturnTypeParam = false`), NOT a fresh `TResult` — otherwise the two `Switch` facades become return-type-generic and overload resolution collapses. `FacadeShape`'s return-display path currently knows only `System.Type` / `[ReturnType]` / fresh-`TResult`; the widening must add a "verbatim concrete syntax-node return type" path.

**Files:**
- Create: `test/Synto.Test/Templating/SwitchBuilderTest.cs` — the durable composition proof.
- Create (via accept): `test/Synto.Test/Templating/snapshots/SwitchBuilderTest.*.verified.cs`.
- Possibly modify (only if the spike proves necessary, bounded): `src/Synto.SourceGenerator/Templating/FacadeShape.cs` (return-shape set; `params` preservation) and/or `src/Synto.SourceGenerator/Templating/FacadeCallFinder.cs` (same-name overload disambiguation) and/or `src/Synto.SourceGenerator/Templating/FacadeArgumentBinder.cs` (expanded-form binding) and/or `src/Synto.SourceGenerator/Templating/SyntaxBuilderFacadeGenerator.cs` (emit `params`).
- Likely modify: `src/Synto/Templating/SyntoBuilders.cs` — a *minimal* subset of the switch builders needed to exercise the spike (the full surface lands in Task 2). The spike needs **both `Switch` overloads declared** (statement + expression — required to actually trigger and then clear SY1018, even if only some are called), plus `Case`, `Arm`, and `Return`. Adding these here means the SurfaceInjection snapshot **will move**; see Step note.

**Interfaces:**
- Consumes: the existing `[SyntaxBuilder]` mechanism (`FacadeCallFinder`, `FacadeArgumentBinder`, `FacadeShape`, `SyntaxBuilderFacadeGenerator`); `Member<TRet>` over a `[Splice] TRet` type param (already proven by `SyntaxBuilderTest`).
- Produces: the blessed pattern + the resolved label/pattern parameter shape that Task 2 ships. Whatever generator widening this task makes is the substrate Tasks 2–4 rely on.

- [ ] **Step 1: Write the failing composition test**

  Model the harness on `SpliceMemberGeneratorTest.MemberGenerator_CanonicalShape` and `SyntaxBuilderTest.VerifyBuilderTemplate` in the same folder — copy the metadata-reference set + `CompilationWithSource(...)` verbatim (the `SyntoCoreAssembly`-referencing variant), and use the two-driver pattern from `VerifyBuilderTemplate`: snapshot `TemplateFactorySourceGenerator` output, then post-generation-compile with `SyntaxBuilderFacadeGenerator` + `TemplateFactorySourceGenerator` and assert zero error diagnostics (so the synthesized facade actually resolves). Author `[Template]`(s) whose bodies build a data-driven switch over a live `Parameter<IReadOnlyList<Col>>()` collection. The gate must, concretely:
  - **Declare BOTH `Switch` overloads** (statement + expression) in `SyntoBuilders.cs` even though only some are called — declaring both is what actually triggers SY1018 and proves the disambiguation fix clears it.
  - **Exercise one section projection AND one arm projection** — `Switch(i, cols.Where(...).Select(c => Case(c.Ordinal, Return(Member<TRet>(_e.Current, c.Name)))))` (statement) *and* `Switch(i, cols.Where(...).Select(c => Arm(c.Ordinal, Member<TRet>(_e.Current, c.Name))))` (expression) — so the `SwitchExpressionArmSyntax` return-shape is proven here and does not surface as surprise generator work inside Task 2.
  - **Exercise BOTH call forms** (since `params` is being kept): the projection form (`Switch(i, cols.Select(...))`) *and* the expanded comma form (`Switch(i, Case(...), Case(...))`).

  This exercises unknowns #1, #2, and #3 at once. Keep templates self-contained (a local `Col` record, an inert `_e`), mirroring `ChildTemplateTest`'s self-contained style.

- [ ] **Step 2: Run it; classify the failure**

  Run:
  ```
  rtk dotnet test test/Synto.Test/Synto.Test.csproj --filter "SwitchBuilderTest"
  ```
  **Decision tree:**
  - If it fails ONLY because Verify has no `.verified.cs` yet (new received snapshot) AND the post-generation compile is clean ⇒ the mechanism already holds; go to Step 4.
  - If it fails at **generation time** — a generator diagnostic (SY1017 return-shape, SY1018 ambiguous-overload) or a post-generation compile error (facade not resolving, `params` not emitted, expanded form not binding) ⇒ STOP and switch to **superpowers:systematic-debugging**. This is exactly what the gate exists to find. Work the predicted sites in order: return-shape set / verbatim concrete return type (`FacadeShape.cs:72-80`), overload disambiguation via shared binding-shape (`FacadeCallFinder.cs:54-58`), `params` emission (`SyntaxBuilderFacadeGenerator.cs:104-114`) and expanded-form binding (`FacadeArgumentBinder.Bind`, `FacadeArgumentBinder.cs:16`). Keeping `params` is a decision, not optional — both the projection and expanded forms must end up working; do not degrade to projection-only.
  - If a fix would require touching files **other than** `FacadeShape.cs` / `FacadeCallFinder.cs` / `FacadeArgumentBinder.cs` / `SyntaxBuilderFacadeGenerator.cs` / `SyntoBuilders.cs` ⇒ STOP and re-scope with the user; the spike has found something larger than "bounded."

- [ ] **Step 3: Make generation clean (bounded generator work, only if Step 2 demanded it)**

  Apply the minimal widening proven necessary, keeping each change scoped to the v1 surface (do not generalize the return-shape set beyond the switch node types; disambiguate overloads by **shared binding-shape compatibility** — bind with the shared shape and let the C# compiler resolve the real overload, emitting SY1018 only on a genuine shape conflict — **not** by typing the projection's element type). Add/extend a focused diagnostics test if a new diagnostic branch is introduced. Re-run the suite to confirm no existing `SyntaxBuilderTest` / `SpliceMemberGeneratorTest` / `InjectedSurfaceCompletenessTest` regressions (those pin the SY1015–SY1021 behaviors and the injected-surface compile); the existing `AmbiguousBuilder`/SY1018 test MUST stay green.

- [ ] **Step 4: Review the received snapshot, then accept**

  Confirm the generated factory: (a) the governing expr is quoted (`IdentifierName("i")` / equivalent), (b) the `.Where(...).Select(...)` unrolls at factory-build time into a staged `SwitchSectionSyntax` list fed to `SyntaxFactory.SwitchStatement(...)` via the builder, (c) the per-section body is `return _e.Current.<Name>;`. If correct, accept (`.received.cs` → `.verified.cs`, stage it).

- [ ] **Step 5: Confirm PASS, then green-gate**

  ```
  rtk dotnet test test/Synto.Test/Synto.Test.csproj --filter "SwitchBuilderTest"
  rtk dotnet build
  rtk dotnet test
  rtk dotnet format whitespace --verify-no-changes
  ```
  Note: if Step 1 added builders to `SyntoBuilders.cs`, the `SurfaceInjectionTest.VerifyInjectedSurface#SyntoBuilders.g.verified.cs` snapshot grows — re-accept it (the surface intentionally gains members; this is expected, not a behavior change).

- [ ] **Step 6: Commit**

  ```
  rtk jj commit -m "test(templating): switch-builder composition gate — overloaded Switch + Case projection over live collection

  Plan: docs/superpowers/plans/2026-06-28-switch-builders.md
  Plan-Tasks: 1"
  ```

---

## Task 2: Ship the full runtime switch-builder surface + snapshot tests

Land the complete v1 surface (all overloads + element builders + `Return`) in `SyntoBuilders.cs`, with snapshot tests pinning each shape. Builds on whatever Task 1 proved/widened; the inert facades are auto-synthesized.

**Files:**
- Modify: `src/Synto/Templating/SyntoBuilders.cs` — add the v1 builders from "The v1 builder surface" (statement `Switch`, expression `Switch`, `Case`, `Default`, `Arm`, `WhenArm`, `DefaultArm`, `Return`), each `[SyntaxBuilder]`, with the label/pattern parameter shape settled in Task 1.
- Modify (re-accept): `test/Synto.Test/snapshots/SurfaceInjectionTest.VerifyInjectedSurface#SyntoBuilders.g.verified.cs` — the injected surface now includes the switch builders.
- Modify: `test/Synto.Test/Templating/SwitchBuilderTest.cs` — add snapshot tests for the shapes not already covered by Task 1 (expression form with `Arm`/`WhenArm`/`DefaultArm`; statement form with a trailing `Default`).
- Create (via accept): the corresponding `snapshots/SwitchBuilderTest.*.verified.cs`.

**Interfaces:**
- Consumes: the Task-1 substrate (return-shape acceptance, overload disambiguation, facade synthesis).
- Produces: the public `SyntoBuilders.Switch/Case/Default/Arm/WhenArm/DefaultArm/Return` surface, injected `internal` into consumer generators. Task 5 (dog-food) calls exactly these.

- [ ] **Step 1: Write the new snapshot tests (failing)** — one `[Fact]` per uncovered shape (expression `Switch` over `Arm` projection; `WhenArm` guard; `DefaultArm`; statement `Switch` with appended `Default`). Reuse the Task-1 harness helpers. Run `rtk dotnet test --filter "SwitchBuilderTest"`; expect new-received-snapshot failures (generation must be clean — if not, it's a Task-1 regression; fix there).
- [ ] **Step 2: Add the remaining builders to `SyntoBuilders.cs`** so every facade call in the new tests resolves. Keep bodies minimal `SyntaxFactory` one-liners; rely on the formatter for trivia.
- [ ] **Step 3: Review + accept** each received snapshot (governing quoted, arms/sections staged, `when`/`_` rendered correctly, byte-identical after format).
- [ ] **Step 4: Re-accept the SurfaceInjection snapshot** (`SyntoBuilders.g.verified.cs`) — verify the diff is purely the new builder methods, nothing else.
- [ ] **Step 5: Green-gate**
  ```
  rtk dotnet build
  rtk dotnet test
  rtk dotnet format whitespace --verify-no-changes
  ```
- [ ] **Step 6: Commit**
  ```
  rtk jj commit -m "feat(synto): data-driven switch statement/expression [SyntaxBuilder] surface

  Plan: docs/superpowers/plans/2026-06-28-switch-builders.md
  Plan-Tasks: 2"
  ```

---

## Task 3: Verbatim fixed-switch round-trip test (no new surface) — independent

Cheap confirmation that a hand-written **fixed** `switch` statement and switch **expression** quote verbatim through a `[Template]`. No new surface; pure coverage. Independent of Tasks 1–2 — sequence it wherever it reads cleanly (it can run first or last).

**Files:**
- Modify: `test/Synto.Test/Templating/SwitchBuilderTest.cs` (or a sibling `SwitchRoundTripTest.cs` if cleaner) — add a `[Fact]` authoring a `[Template]` whose body contains a literal `switch (x) { case 1: …; default: …; }` and a literal `x switch { 1 => …, _ => … }`, run `TemplateFactorySourceGenerator`, snapshot via the existing `VerifyTemplate`-style harness.
- Create (via accept): the corresponding `snapshots/*.verified.cs`.

**Interfaces:**
- Consumes: nothing new — the quoter handles arbitrary nodes generically; this just pins that `SwitchStatementSyntax`/`SwitchExpressionSyntax` are among them.
- Produces: regression coverage.

- [ ] **Step 1:** Write the round-trip `[Fact]`(s). Run `rtk dotnet test --filter "..."`; expect new-received-snapshot failure.
- [ ] **Step 2:** Review + accept. **Decision:** if a fixed switch does NOT round-trip verbatim (generation error / malformed output), that is a pre-existing quoter bug — STOP, file it separately, and mark this test `Skip` with the bug reference. It is NOT part of the builder work and must not pull scope into this plan.
- [ ] **Step 3: Green-gate**
  ```
  rtk dotnet build
  rtk dotnet test
  rtk dotnet format whitespace --verify-no-changes
  ```
- [ ] **Step 4: Commit**
  ```
  rtk jj commit -m "test(templating): fixed switch statement + expression round-trip through [Template]

  Plan: docs/superpowers/plans/2026-06-28-switch-builders.md
  Plan-Tasks: 3"
  ```

---

## Task 4: Sync spec, docs, and memory

Mark the switch-builder surface shipped, **correct the spec's two now-false design premises**, record the list-shaped-builder recipe as blessed, and note the next candidate (interpolated-string folding). **Sequenced before the dog-food (Task 5) on purpose:** the dog-food is optional/parkable (a pure scope choice now that child-templates has landed), so the shipped feature must be documented even if the dog-food is later parked — running this last would let a park strand the shipped feature undocumented.

**Files:**
- Modify: `docs/superpowers/specs/2026-06-28-switch-builders-design.md` — status → shipped, and **overwrite the two premises this plan's review found false**: (a) the overloaded-builder "recognition binds to the resolved `IMethodSymbol`, so it *should* hold" claim — it actually trips SY1018; replace with the **shared-binding-shape disambiguation** that was implemented; and (b) the "no generator change is required to add a builder" claim — false for this surface; record the **bounded generator widening actually done** (return-shape acceptance, overload disambiguation, `params` emission/binding). Record that `params` was **kept** and the facade generator taught to emit/bind it (not degraded to projection-only), and the settled label/pattern marker shape (defaulted toward STAGED).
- Modify: `C:\Users\redoz\.claude\projects\C--dev-Synto\memory\MEMORY.md` + a pointer note — record that switch builders shipped, the list-shaped-builder recipe is the reusable pattern, and the interpolated-string constant-fold gap is the next candidate.
- Modify: note in the spec/plan whether the dog-food (Task 5) ran or was deferred (child-templates has landed, so it is no longer a blocker — deferral, if any, is purely a sequencing/scope choice).

- [ ] **Step 1:** Update the spec status line + open-decisions, and overwrite the two now-false premises with what the Task-1 spike actually settled: label/pattern marker shape, `params` kept + generator taught to emit/bind it, and the bounded generator widening done.
- [ ] **Step 2:** Update memory: one fact for "switch builders shipped + list-shaped-builder recipe blessed"; one for "interpolated-string constant-fold = next list-shaped-builder candidate" if not already captured.
- [ ] **Step 3: Commit**
  ```
  rtk jj commit -m "docs(spec): mark switch builders shipped; correct overload + no-generator-change premises; record recipe + next candidate

  Plan: docs/superpowers/plans/2026-06-28-switch-builders.md
  Plan-Tasks: 4"
  ```

---

## Task 5: Dog-food — collapse ObjectReader typed getters to the switch builders (LAST)

Refactor the ObjectReader typed-getter body from `foreach + if (i == c.Ordinal) return …` into the switch-builder form, behavior-preserving against the ObjectReader snapshot gate. This mines getter friction per the dog-food principle.

> **⚠️ Sequencing — read before starting.** The spec sequences this dog-food *after* the child-templates narrow case. **That child-templates work has since LANDED** (implemented per plan `2026-06-28-nested-child-template-and-count-fold.md`): the 12 typed getters are now generated from a **single nested child `[Template]`** — `TypedGetter<[Splice] TRet>(int i)` at `ReaderTemplate.cs:107–117` — fanned out by the `[Splice]` generator `TypedGetters()` at `ReaderTemplate.cs:119–135`. So this task rewrites **one** child-template body, not 12 hand-written methods. Still re-confirm the present form in Step 1 (do not assume line numbers), but expect the child template. This remains the **LAST** task and stays **deferrable** — it must never make the switch surface depend on the ObjectReader refactor. (The docs/memory sync ran in Task 4, so parking this leaves the shipped feature documented.)

**Files:**
- Modify: `examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Generator/ReaderTemplate.cs` — rewrite the single child-template body `TypedGetter<[Splice] TRet>(int i)` (currently `foreach (var c in columns.Where(c => c.ColumnTypeName == clrTypeName)) if (i == c.Ordinal) return Member<TRet>(_e.Current, c.Name);` at `ReaderTemplate.cs:107–117`) to build the lookup via `Switch(i, columns.Where(...).Select(c => Case(...)))` (statement form) or the switch-expression form, falling through to the existing `throw new InvalidCastException($"Field {i} is not {typeLabel} column.")`.
- Unchanged but load-bearing as the gate: `examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Tests/snapshots/ObjectReaderSnapshotTests.Generates_Specialized_Reader_For_Person#ObjectReader.g.verified.cs`, `ObjectReaderBehaviorTests.cs`, `ObjectReaderIncrementalTests.cs`.

**Interfaces:**
- Consumes: `SyntoBuilders.Switch/Case/Return` (Task 2), injected into the example generator; `Member<TRet>`; `EquatableArray<ColumnInfo>`.
- Produces: the typed getters expressed via the switch builders. No new public surface.

- [ ] **Step 1: Characterize the baseline (and the current getter form).** Run the example tests; confirm green. Read `ReaderTemplate.cs` to re-confirm the child-template form (`TypedGetter<[Splice] TRet>` at `ReaderTemplate.cs:107–117`, fanned out by `TypedGetters()`) and its exact lines/body before editing. This determines the exact edit and confirms the output cannot stay byte-identical.
  ```
  rtk dotnet test examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Tests/Synto.Example.ObjectReader.Tests.csproj
  ```
- [ ] **Step 2: Decide the snapshot gate.** The `foreach+if` lookup and a `switch` lookup are *different generated C#*, so the existing `#ObjectReader.g.verified.cs` will **not** stay byte-identical — this is the expected, intended change for a switch dog-food (unlike the child-templates collapse, which was byte-identical). The gate is **behavior preservation**: `ObjectReaderBehaviorTests` (runtime semantics) and `ObjectReaderIncrementalTests` (cacheability — ALL steps Cached/Unchanged on unrelated edit) MUST stay green; the snapshot is re-accepted to the new switch-shaped output after reviewing the diff is *only* the lookup form (same getters, same return types, same exception messages/labels, same order). If you instead intend a byte-identical migration, STOP — that is not achievable for a foreach→switch change and indicates a misread.
- [ ] **Step 3: Rewrite the getter body(ies)** to the switch-builder form. Preserve: the per-getter CLR-type filter, the return-type, the `$"Field {i} is not {label} column."` exception text (incl. `a`/`an` and `Single` for float), and getter order.
- [ ] **Step 4: Build the example generator.**
  ```
  rtk dotnet build examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Generator/Synto.Example.ObjectReader.Generator.csproj
  ```
- [ ] **Step 5: Run example tests; review + accept the snapshot.** Behavior + incremental green; snapshot diff confined to the lookup form. Accept the new snapshot.
  ```
  rtk dotnet test examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Tests/Synto.Example.ObjectReader.Tests.csproj
  ```
- [ ] **Step 6: Full green-gate.**
  ```
  rtk dotnet build
  rtk dotnet test
  rtk dotnet format whitespace --verify-no-changes
  ```
- [ ] **Step 7: Commit**
  ```
  rtk jj commit -m "refactor(objectreader): build typed-getter lookup via switch builders (dog-food)

  Plan: docs/superpowers/plans/2026-06-28-switch-builders.md
  Plan-Tasks: 5"
  ```

---

## Self-Review

**1. Spec coverage** (`2026-06-28-switch-builders-design.md`):
- "Ship first: data-driven switch statement (`Switch`/`Case`/`Default`) + expression (`Switch`/`Arm`/`WhenArm`/`DefaultArm`) + `Return`, all runtime `[SyntaxBuilder]`" → Task 2 surface contract. ✓
- "Empirical gate — spike the three unproven things first" → Task 1, each unknown called out with its predicted gap + fix site; generation-time failure ⇒ systematic-debugging; `params` kept and the generator taught to emit/bind it (no graceful-degrade). ✓
- "Single overloaded `Switch` chosen by element type" → Task 1 unknown #2 — at the C# author surface the element type selects the overload, while the generator disambiguates by **shared binding-shape** (not by typing the projection) + Task 2 both overloads. ✓
- "Cheap side-task: verbatim switch round-trip, no new surface" → Task 3, with the "separate bug file if it doesn't round-trip" escape. ✓
- "Dog-food: ObjectReader typed getters, sequenced after child-templates" → Task 5, LAST (after the docs/memory sync in Task 4, so a park cannot strand the shipped feature undocumented); child-templates has since landed, so this targets the single `TypedGetter` child-template body, behavior-preserving (not byte-identical, since foreach→switch changes generated text). ✓
- "Cacheable, syntax-only inputs; nodes in, nodes out" → Global Constraints + incremental guards stay green (all steps). ✓
- "Trivia normalizes after format" → Global Constraints + each green-gate runs `format whitespace --verify-no-changes`. ✓
- "v1 excludes type/property patterns + if-chain peephole; interpolated-string fold is next candidate, not built" → Global Constraints (out of scope) + Task 4 (record candidate). ✓
- "Open decisions: label/pattern shape; `Return` packaging" → deferred to Task 1 settle + noted as a packaging default. ✓

**2. Contract-first check:** Builder *signatures* + observable behaviors are pinned; **no full method bodies** are prescribed (explicit instruction in the surface section). The implementer chooses `SyntaxFactory` bodies and the final label/pattern shape. ✓

**3. Generator-change framing:** Bounded generator work is *expected* (not "none anticipated") and is confined to Task 1, scoped to five authorized files — `FacadeShape.cs` / `FacadeCallFinder.cs` / `FacadeArgumentBinder.cs` / `SyntaxBuilderFacadeGenerator.cs` / `SyntoBuilders.cs` (with real file:line predictions: SY1017 return-shape + verbatim concrete return type at FacadeShape.cs:72-80, SY1018 overload via shared-binding-shape at FacadeCallFinder.cs:54-58, `params` emission at SyntaxBuilderFacadeGenerator.cs:104-114 + expanded-form binding in `FacadeArgumentBinder.Bind` at FacadeArgumentBinder.cs:16), with a STOP/re-scope if a fix reaches beyond those five files. ✓

**4. Workflow compatibility:** Every task is a green unit ending in `rtk jj commit` carrying `Plan:` + `Plan-Tasks:` trailers, footer-clean (no AI footer), conventional message. Tasks are independently verifiable; Task 3 is independent; Task 5 (dog-food) is deferrable, sequenced after the Task 4 docs/memory sync. ✓

**5. Placeholder scan:** No "TBD"/"similar to above"/"handle edge cases". Each task names concrete files, concrete commands, concrete decision points. The one intentionally conditional path (Task 1 generation failure ⇒ systematic-debugging with enumerated fix sites; Task 5 deferral) is explicit, not hand-waved. ✓

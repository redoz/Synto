# Ambient sub-template scope — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Each task is an independently green unit (builds + tests pass on its own) ending in a `rtk jj commit` step. Use superpowers:test-driven-development inside every task: write the failing test/snapshot first, then the implementation, then confirm green.

**Spec:** `docs/superpowers/specs/2026-07-09-ambient-sub-template-scope-design.md` (owner-approved for planning, 2026-07-09; opt-out name = `Static`, dead-ambient reference = plain C# error, both owner-confirmed).

**Goal:** Make a nested (non-generic) `[Template]` default to sharing its owner carrier's class-level scope — the child drops the owner binding (type param + field-position `= Unquote<…>()` roots) from its own signature and reads it directly in its body; the generator appends each referenced owner binding as an **OPTIONAL trailing parameter** (`= default`) on the generated child factory (which stays a **real, fully-callable** top-level `internal static Factory.X<T>` member), and `SpliceGeneratorBodyRewriter` **injects the owner-binding argument** at each call site in output — and add a `bool Static` opt-out on the non-generic `TemplateAttribute` only, restoring today's isolated top-level `internal static Factory.X` emission with the owner binding as an explicit author-declared param. Roots (`[Template<T>]`) are untouched and cannot express `Static`.

**Architecture:** Everything stays inside the existing generator architecture — semantic work inside the `ForAttributeWithMetadataName` transform, the only pipeline state the equatable result type, failures as located `SY*` diagnostics. Pitched at **contracts, behaviors, and interfaces**: each task states the behavior change, the seams it touches (`path:line`), and the green-gate test; the TDD implementer writes the bodies. **De-risked in two phases** (mirroring `2026-07-04-sub-templates-and-generic-template-attribute.md`): Phase 1 adds the `Static` bit and pins today's isolated lowering behind it **as a byte-identical no-op** (default still emits the isolated form — zero behavior change, fully certain). Phase 2 implements the ambient default via the resolved §8.2 mechanic — the generator **appends owner class-level bindings as optional trailing params** on the child factory and `SpliceGeneratorBodyRewriter` **injects the owner-binding arg** at ambient-child call sites (no nested local function, no closure, no `TemplateScopedWalker` surgery) — validates that mechanic on a fresh minimal template as its explicit first implementation tasks, migrates the ObjectReader dog-food, and re-validates. The crux `[Splice]`/owner-body → sub-template call-referencing mechanic was resolved up front (Task 3 spike, spec §8.2); Phase 1 has already shipped value (the `Static` opt-out) independent of Phase 2.

**Tech Stack:** C# / Roslyn incremental source generators (`netstandard2.0`, Roslyn 5.0 floor), xUnit v3 on Microsoft.Testing.Platform (`net10.0`), Verify snapshot testing, jj (Jujutsu) VCS.

## Global Constraints

- **Experimental feature ⇒ experimental branch.** New surface, not a fix: lands on `experimental/ambient-sub-template-scope` (already created; spec committed there), never directly on `main` (repo owner rule — `experimental-features-own-branch`).
- **VCS is jj (Jujutsu).** `rtk jj commit` / `rtk jj status`; never `git add`. Every task ends in a commit carrying the `Plan:` / `Plan-Tasks:` trailers shown in its commit step (the `implement-plan` workflow reads `Plan-Tasks` mechanically).
- **Tests run under MTP (SDK 10).** Raw `dotnet test --project <csproj>` — do NOT route `dotnet test` through `rtk`; scope with `-- --filter-method "*Name*"` (never bare `--filter`, never `--filter-class`) (`dotnet-test-mtp-invocation`). `rtk` is fine for `dotnet build` / `dotnet format`.
- **Snapshot accepts are mechanical but eyeballed:** run with `$env:DiffEngine_Disabled='true'`, overwrite `.verified.*` with `.received.*`. Semantic equivalence + green tests is the bar (`generated-output-semantic-not-byte-identical`) EXCEPT where a task explicitly promises **byte-identical** (Phase 1's `Static` regression pin does). An unexplained output-shape delta is a finding, not a rubber stamp. Deleted generated documents ⇒ delete the orphaned `.verified.cs`.
- **Incremental cacheability is sacred:** no `Compilation`/`ISymbol`/`SemanticModel`/`SyntaxNode` in pipeline *state*; all new work runs inside the transform and flows out as the equatable result. The ambient lowering threads the owner-binding set as pure equatable data (names/types/lift strategy — no symbols) into the child build and rewrites `[Splice]` call args syntactically, so nothing symbolic enters pipeline state; the guards (`CacheabilityAssert.AllStepsCachedOrUnchanged` in `SimpleTemplateTest.cs:497,542,798,847`; `ObjectReaderIncrementalTests.cs:32`) must stay green (`cacheability-guard-iterate-all-steps`).
- **Single-source-of-truth injected surface:** the `Static` bit is authored once on `TemplateAttribute` in `src/Synto.Core/Templating/Definition/Surface/TemplateAttribute.cs` and injected `internal` by `SurfaceInjectionGenerator`. Never re-declare a marker in the generator. The bit is a **dedicated property on the non-generic attribute** — NOT a `TemplateOption` flag (that enum is shared with `TemplateAttribute<T>` and would re-leak `Static` onto roots).
- **No new diagnostic.** A dead-ambient reference (child names an identifier that is neither its own input nor an owner class-level binding) is left to the ordinary C# "does not exist in the current context" compiler error (owner decision). No SY10xx allocated; `AnalyzerReleases.Unshipped.md` untouched.
- **Format gate:** `rtk dotnet format whitespace --verify-no-changes` clean at the end of every task. No git pre-commit hooks (`no-git-pre-commit-hooks`).
- **Green gate (every task):** `rtk dotnet build` (full solution — proves the self-host/bootstrap chain still builds in lockstep), `dotnet test --project test/Synto.Test/Synto.Test.csproj` (+ any other project a task names), format gate, then commit.
- **Dog-food is a first-class gate.** Surface/lowering changes are validated by **building the ObjectReader example**, not only `Synto.Test` (`dogfood-validate-by-building-example`): `rtk dotnet build examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Demo/` + its 10 example tests.

---

## Phase 1 — the `Static` opt-out bit (pure surface, byte-identical, zero behavior change)

End state of Phase 1: `TemplateAttribute` carries a `bool Static`; the generator reads it; and **both** `Static = true` and the default emit exactly today's isolated `internal static` sub-template — so the whole suite stays byte-identical. This lands the surface and the plumbing with no risk; Phase 2 flips the default's lowering.

### Task 1: Author `bool Static` on the non-generic `TemplateAttribute`; thread it into the sub-template build (read-only, no lowering change)

The bit lands on the surface and is carried through discovery into the recursive sub-template build, but nothing yet branches on it — the isolated lowering stays the only path, so output is byte-identical.

**Files:**
- Modify: `src/Synto.Core/Templating/Definition/Surface/TemplateAttribute.cs:11-15` (add `public bool Static { get; set; }` to the **non-generic** `TemplateAttribute` only; leave `TemplateAttribute<T>` at `:24-28` untouched)
- Modify: `src/Synto.Generators/Templating/Emit/TemplateInfo.cs` (`CreateSubTemplate` — read the `Static` named argument off the nested `[Template]`'s attribute data onto a new equatable `bool IsStatic` field on the sub-template's `TemplateInfo`; the value is pure data, no symbol captured)
- Re-accept: `test/Synto.Test/snapshots/SurfaceInjectionTest.VerifyInjectedSurface#TemplateAttribute.g.verified.cs` (delta = the added `Static` property, nothing else)
- Test: extend `test/Synto.Test/Templating/TemplateAttributeFormTest.cs`

**Interfaces (contract):**
- `TemplateAttribute` gains `bool Static { get; set; }` (default `false`); same `AttributeUsage`; authored `public`, injected `internal`. `TemplateAttribute<T>` is unchanged and has **no** `Static` — a root literally cannot name it (compile error if attempted), which is the no-leak guarantee.
- `TemplateInfo` gains an equatable `bool IsStatic` populated only for sub-templates (`CreateSubTemplate`); top-level `TemplateInfo.Create` leaves it `false`. It participates in equality (cacheability) but drives no branch yet.
- Reading the named argument reuses the existing `Options` named-argument reading shape in `TemplateInfo` (error-kind skipped, default `false`).

**Failing-test intent:**
- Surface fact: the injected `TemplateAttribute` exposes a settable `bool Static` (assert against the re-accepted surface snapshot).
- No-leak fact: `[Template<Factory>(Static = true)]` on a top-level template is a **C# compile error** (member does not exist) — encode as a negative/won't-compile expectation in `TemplateAttributeFormTest` (the generic form has no such member).
- Byte-identical fact: an existing nested-sub-template test (`ChildTemplateTest.ChildTemplate_MemberGeneratorInvokesChildFactory`) produces output **byte-identical** to before this task (the bit is inert).

**Steps:**
- [ ] Write the three facts above; the `Static`-settable fact fails (property absent).
- [ ] Verify failure; rest of suite green.
- [ ] Add the property; thread `IsStatic` through `CreateSubTemplate`/`TemplateInfo` equality; branch on nothing.
- [ ] Re-accept `#TemplateAttribute.g.verified.cs` (only the new property). All other snapshots **byte-identical**.
- [ ] Full gate: `rtk dotnet build` + `dotnet test --project test/Synto.Test/Synto.Test.csproj` + format clean.
- [ ] Commit:
  ```
  rtk jj commit -m "feat(templating): TemplateAttribute.Static bit (non-generic only), threaded into sub-template TemplateInfo — inert

  Plan: docs/superpowers/plans/2026-07-09-ambient-sub-template-scope.md
  Plan-Tasks: 1"
  ```

### Task 2: Branch the sub-template lowering on `IsStatic` — both arms emit today's isolated form (regression pin)

Introduce the lowering fork with **both** arms pointing at the existing isolated emission, so `Static = true` and the default are provably interchangeable. This is the seam Phase 2 Task 5 splits.

**Files:**
- Modify: `src/Synto.Generators/Templating/Emit/TemplateFactoryBuilder.cs:187-197` (`ResolveScopeAndTrimForeignChildren` — where the recursive child `Build(..., SyntaxKind.InternalKeyword)` runs; introduce a single decision point `childInfo.IsStatic ? EmitIsolated(...) : EmitAmbient(...)` where **both** currently call the existing isolated `Build` path and append to `ctx.SubTemplates`)
- Test: extend `test/Synto.Test/Templating/ChildTemplateTest.cs`

**Interfaces (contract):**
- A named private seam pair on `TemplateFactoryBuilder` — `EmitIsolatedSubTemplate(ctx, childInfo)` and `EmitAmbientSubTemplate(ctx, childInfo)` (names the implementer's; the contract is: two entry points selected by `IsStatic`, both returning the same `List<MethodDeclarationSyntax>`-into-`ctx.SubTemplates` effect). In this task **both bodies are the current isolated recursion verbatim**.
- No change to `ctx.SubTemplates` fold in `TemplateDocumentBuilder.cs:23-34`.

**Failing-test intent:**
- Equivalence fact: a nested sub-template authored `[Template(Static = true)]` and the same one authored `[Template]` produce **byte-identical** factory documents in this task (both isolated) — pin as a snapshot-equality assertion. (Phase 2 Task 5 is what makes the default arm diverge.)

**Steps:**
- [ ] Write the equivalence fact; it passes trivially only once the fork exists routing both to isolated — first assert it compiles against the new seam names (fails: seams absent).
- [ ] Verify failure; suite green.
- [ ] Add the `IsStatic` fork with both arms = existing isolated recursion.
- [ ] Full gate + `ChildTemplateTest` green; all snapshots **byte-identical**.
- [ ] Commit:
  ```
  rtk jj commit -m "refactor(templating): fork sub-template emission on IsStatic — both arms isolated (Phase-2 seam)

  Plan: docs/superpowers/plans/2026-07-09-ambient-sub-template-scope.md
  Plan-Tasks: 2"
  ```

---

## Phase 2 — ambient lowering for the default (optional trailing params + call-arg injection)

End state: the default (`Static = false`) sub-template's generated `internal static Factory.X` factory carries each referenced owner class-level binding as an **OPTIONAL trailing parameter** (`= default`); owner-body / `[Splice]` calls to it drop the owner argument (`Factory.X<T>(varyingArgs…)`), and `SpliceGeneratorBodyRewriter` **re-injects the owner-binding arg** in output (`Factory.X<T>(varyingArgs…, columns)`). The child factory stays a **real, fully-callable** top-level member — no nested local function, no closure. `Static = true` still emits today's isolated form (owner binding an explicit author-declared param). ObjectReader's `TypedGetter` drops its `columns` param.

### Task 3: SPIKE — resolve the `[Splice]`/owner-body → ambient-sub-template call-referencing mechanic

> **✅ RESOLVED 2026-07-09 (owner-approved). Findings written into spec §8.2. Summary:**
> - **There is no generic-arg → `TypeSyntax` call rewrite** (the thing the spec §5/§8.2 assumed "already fires"). The `[Splice] TRet` snapshot authors the `TypeSyntax` positionally by hand (`ChildTemplateTest.cs:78,80`); the `[Unquote] TRet` path (ObjectReader) keeps a **generic** factory + `typeof(TRet).ToTypeSyntax()` (`TemplateFactoryBuilder.cs:225-256`). `SpliceGeneratorBodyRewriter` touches only `Unquote` (`SpliceMemberGeneratorEmitter.cs:85-129`).
> - **ObjectReader self-hosts** (`…Generator.csproj` references `Synto.Generators` `OutputItemType="Analyzer"` on its own project), so the carrier `[Splice]` body must bind against the generated `Factory.TypedGetter` in the same compilation. A **closure / nested-local-fn** lowering deletes that top-level member and breaks self-host binding — **rejected** (and it's strictly more machinery). A separate inert bind-stub works (local-fn-shadows-`using static` is probe-proven) but leaves an unusable phantom — **rejected**.
> - **Chosen mechanic: auto-thread owner class-level bindings as OPTIONAL trailing params on the child factory; the rewriter injects the arg in output.** Author drops only the owner arg from the existing `Factory.X<T>(…)` call (no marker, no phantom, no closure). Bind target stays the real usable factory. Tasks 4–5 below are re-scoped to this.

**This task was a de-risking spike, not a feature increment.** It resolved the one mechanic the spec flagged as unproven (spec §5, §8.2) and wrote its answer back into the spec before any ambient emission is built. It ends in a committed decision note, not generator code.

**The question the spike answered, concretely:** today a `[Splice]` generator body is copied verbatim (`SpliceMemberGeneratorEmitter.cs:33-56`; `SpliceGeneratorBodyRewriter` only drops `Unquote` decls / resolves inline `Unquote("name")`), and it references the child as `Factory.TypedGetter<int>(columns, …)` — a real `internal static` method (`TemplateFactoryBuilder.cs:187-197`). The spike established, with a runnable probe:
1. **How `Factory.TypedGetter<int>(columns, …)` in the carrier `[Splice]` source resolves at carrier compile time today** — against what declaration, and whether the generic `<int>` + `columns` arg in output (`…#Factory.Reader.g.verified.cs:17-27`) comes from a call-site rewrite or is authored verbatim / produced at factory runtime. **Answer (Finding A):** no generic-arg → `TypeSyntax` call rewrite exists; the `[Unquote] TRet` path keeps a generic factory + `typeof(TRet).ToTypeSyntax()` and `SpliceGeneratorBodyRewriter` touches only `Unquote` (spec §8.2).
2. **How an ambient child stays bindable at self-host carrier-compile time while the terse authored form drops the owner arg** — given ObjectReader self-hosts (`…Generator.csproj` references `Synto.Generators` as an `Analyzer` on its own project), the carrier `[Splice]` body must bind against the generated `Factory.TypedGetter` in the *same* compilation, so any lowering that deletes that top-level member (a nested local function / closure) breaks the bind (Finding B). **Answer (resolved mechanic):** keep the child factory a **real top-level `internal static Factory.X<T>` member** and append each referenced owner binding as an **OPTIONAL trailing param** (`= default`) — the columns-dropped call `Factory.X<T>(varyingArgs…)` binds it in World A with the owner arg defaulted/inert; `SpliceGeneratorBodyRewriter` (extended) **injects the owner-binding arg** in World B (`Factory.X<T>(varyingArgs…, columns)`). No nested local function, no closure, no `TemplateScopedWalker` surgery, no phantom stub, no generic-arg lift (spec §8.2).

**Files:**
- Read: `SpliceMemberGeneratorEmitter.cs`, `TemplateFactoryBuilder.cs:80-107,187-197`, `ReaderTemplate.cs:116-140`, the two `ChildTemplateTest` snapshots (`…#Factory.TypedGetter.g.verified.cs`, `…#Factory.Reader.g.verified.cs`), and how the ObjectReader.Generator project resolves `Factory.TypedGetter` at compile time (self-host).
- Modify: `docs/superpowers/specs/2026-07-09-ambient-sub-template-scope-design.md` (replace §8.2 with the resolved mechanic; if the finding changes the surface, add an owner-decision note and STOP for owner sign-off before Task 4).

**Exit criterion (contract):** a written, code-referenced answer to both questions with the exact reference surface + rewrite site the ambient lowering will use, sufficient for Tasks 4–5 to cite `path:line`. If (2) has no clean answer that keeps the carrier compiling, the spike's output is an owner-facing note proposing the surface change — Phase 2 pauses there rather than guessing.

**Steps:**
- [ ] Trace question 1 to ground (probe the ObjectReader self-host + the rewrite/authoring of the child call).
- [ ] Decide question 2's reference surface; write it into the spec §8.2 with `path:line`.
- [ ] If the surface must change from the spec, write the owner-decision note and flag for sign-off.
- [ ] Commit (docs only):
  ```
  rtk jj commit -m "docs(spec): resolve ambient sub-template call-referencing mechanic (spike)

  Plan: docs/superpowers/plans/2026-07-09-ambient-sub-template-scope.md
  Plan-Tasks: 3"
  ```

### Task 4: Emit the ambient arm — append owner class-level bindings as OPTIONAL trailing factory params, behind `Static = false`, on a NEW nested template not yet called from a `[Splice]`

Build the ambient emission in isolation on a fresh minimal nested `[Template]` that reads an owner class-level binding **without declaring it as its own param** and is **not yet called from a `[Splice]`** — so the append-optional-param + reference-resolution is proven before the call-arg injection (Task 5) is wired. (The child factory stays a normal top-level `internal static` member — NO nested local function, NO closure; the resolved §8.2 mechanic.)

**Files:**
- Modify: `src/Synto.Generators/Templating/Emit/TemplateFactoryBuilder.cs` — `ResolveScopeAndTrimForeignChildren` (`:171-199`) passes the owner's discovered class-level bindings (name + type + lift strategy — the parent's staged roots, e.g. the `columns` field-position `= Unquote<…>()` root) into the recursive child `Build(…, InternalKeyword)` (`:191`). Inside the child build (`EmitAmbientSubTemplate` fork seam from Task 2): for each owner binding **referenced in the child body but not among the child's own declared roots**, append an **optional trailing factory parameter** (`<type> <name> = default`, after the child's own params) and register its references so they resolve to that parameter (reuse the staged-root reference-replacement path). `Static = true` children skip this entirely (author declares the param themselves — today's shape).
- Modify: `src/Synto.Generators/Templating/Emit/TemplateInfo.cs` / `TemplateBuildContext` — thread the owner-binding set (pure data: names/types/lift, no symbols) so it flows into the child build as equatable state.
- Modify: `src/Synto.Generators/Templating/Emit/TemplateScopedWalker.cs:27-34` only if the ambient child's owner-binding reference must be discoverable (confirm the walk still excludes the child's own roots but the owner-binding names reach the child build).
- Test: new case in `test/Synto.Test/Templating/ChildTemplateTest.cs` + its snapshot.

**Interfaces (contract):**
- An ambient (`Static = false`) sub-template's generated `internal static` factory gains **one optional trailing parameter per referenced owner class-level binding** (`= default`), in owner-declaration order, appended after the child's own params. The child body's references to that binding resolve to the appended parameter. Return-shape, generic-`TRet` handling, and the child's own `[Unquote]` params are unchanged.
- A `Static = true` child is byte-identical to Phase 1 (author declares the binding as its own param; no append).
- Cacheability: the owner-binding set is pure data; nothing symbolic enters pipeline state.

**Failing-test intent:**
- Ambient-append fact: a default `[Template]` child that reads the owner's field-position `= Unquote<…>()` root **without declaring it**, not called from any `[Splice]`, emits a top-level `internal static` factory carrying that binding as an **optional trailing param** and resolves the body references to it — snapshot the child factory and assert the optional trailing param + resolved references (no required leading param).
- Opt-out fact: a sibling child that DECLARES the binding as its own `[Unquote]` param and is marked `[Template(Static = true)]` still emits today's isolated form (byte-identical to Phase 1).

**Steps:**
- [ ] Write both facts; ambient-append fact fails (owner binding unresolved / not appended).
- [ ] Implement owner-binding discovery + threading into the child build; append the optional trailing param and resolve references in `EmitAmbientSubTemplate`.
- [ ] Accept the new ambient snapshot (eyeball: optional trailing `= default` param present, references resolved, child's own params intact); `Static` snapshot **byte-identical**.
- [ ] Full gate + cacheability guards green.
- [ ] Commit:
  ```
  rtk jj commit -m "feat(templating): ambient sub-template arm — append owner bindings as optional trailing factory params

  Plan: docs/superpowers/plans/2026-07-09-ambient-sub-template-scope.md
  Plan-Tasks: 4"
  ```

### Task 5: Wire the ambient call-arg injection — `[Splice]`/owner-body calls to an ambient child get the owner-binding arg appended in output

Using Task 3's resolved mechanic, make a `[Splice]` generator (or owner-body call) that invokes an ambient sub-template — writing the columns-dropped form `Factory.Child<T>(varyingArgs…)` — emit `Factory.Child<T>(varyingArgs…, ownerBinding)` in output, appending the in-scope owner-binding argument to match the optional trailing param from Task 4. The generic type arg stays a generic arg (NO positional-`TypeSyntax` lift — Finding A).

**Files:**
- Modify: `src/Synto.Generators/Templating/Staging/SpliceMemberGeneratorEmitter.cs` — extend `SpliceGeneratorBodyRewriter` (`:85-129`, today `Unquote`-only): recognize an invocation whose target is an ambient sub-template's factory (`Factory.<child>` where `<child>` is a `Static = false` sub-template of this carrier) and **append the owner-binding argument(s)** — the in-scope owner factory parameter of the same name (e.g. `columns`) — after the written args, in the Task-4 param order. A call to a `Static = true` sub-template is left verbatim (author already passes the arg).
- Test: `test/Synto.Test/Templating/ChildTemplateTest.cs` — a `[Splice]` that invokes the ambient child with the owner arg omitted.

**Interfaces (contract):**
- A `[Splice]` body call `Factory.Child<T>(varyingArgs…)` to an ambient sub-template lowers to `Factory.Child<T>(varyingArgs…, ownerBinding)` in output — `Factory.` qualifier and generic `<T>` preserved, owner-binding arg **appended** (not dropped, not lifted). A `Static = true` sub-template call is unchanged (`Factory.Child<T>(ownerBinding, varyingArgs…)` as authored).
- The rewriter needs to know which sub-templates are ambient (`Static = false`) and their appended-param order — surfaced from Task 4's owner-binding set on the carrier's scope.

**Failing-test intent:**
- End-to-end fact: an owner carrier with a field-position owner root, an ambient child reading it (no own param), and a `[Splice]` invoking the child once per type with the owner arg omitted, generates a factory whose splice local function calls `Factory.Child<T>(varyingArgs…, columns)` with the owner arg appended — snapshot the owner factory.

**Steps:**
- [ ] Write the end-to-end fact; fails (owner arg not injected → arg count mismatch or wrong output).
- [ ] Implement the append in `SpliceGeneratorBodyRewriter` per Task 3.
- [ ] Accept the snapshot (eyeball the appended owner arg + preserved generic form).
- [ ] Full gate + cacheability green.
- [ ] Commit:
  ```
  rtk jj commit -m "feat(templating): ambient sub-template call-arg injection — append owner binding at [Splice] call sites

  Plan: docs/superpowers/plans/2026-07-09-ambient-sub-template-scope.md
  Plan-Tasks: 5"
  ```

### Task 6: Migrate the ObjectReader dog-food — `TypedGetter` goes ambient, drops its `columns` param

Flip the showcase to the ambient default and prove the dog-food end-to-end.

**Files:**
- Modify: `examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Generator/ReaderTemplate.cs:116-140` (drop `[Unquote] EquatableArray<ColumnInfo> columns` from `TypedGetter`'s signature; the body already reads `columns` — it now resolves to the owner carrier's `static readonly … columns` field at `:50` as an ambient owner binding; the `[Splice] TypedGetters()` loop drops ONLY the `columns` **argument** from each call — keeping the `Factory.TypedGetter<…>("…","…")` qualifier + generic form (Task 3's resolved mechanic; NOT a bare call). The generated `Factory.TypedGetter` gains `columns` as an optional trailing param (Task 4) and the rewriter re-injects the arg in output (Task 5), so generated getter bodies are semantically equivalent to today; the factory signature + call-arg order shift is an expected snapshot delta, not byte-identical.)
- Re-accept: any ObjectReader generated snapshots that shift shape.
- Test: `examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Tests/` (all 10) + `ObjectReaderIncrementalTests.cs:32`.

**Interfaces (contract):** ObjectReader generated getters stay **semantically equivalent** (each getter's body identical modulo the sub-template's lowering placement); the 10 example tests stay green; incremental cacheability guard green.

**Failing-test intent:**
- Dog-food fact: `rtk dotnet build examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Demo/` builds 0 errors and the 10 example tests pass with `TypedGetter` carrying no `columns` param and `TypedGetters()` passing no `columns` arg.

**Steps:**
- [ ] Edit `ReaderTemplate.cs` to the ambient shape.
- [ ] `rtk dotnet build …Demo/` — expect 0 errors.
- [ ] `dotnet test --project examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Tests/Synto.Example.ObjectReader.Tests.csproj` — 10 pass (drop `--nologo`; MTP).
- [ ] Re-accept shifted ObjectReader snapshots (semantic-equivalent; eyeball). Full `Synto.Test` gate green incl. cacheability.
- [ ] Commit:
  ```
  rtk jj commit -m "refactor(objectreader): TypedGetter goes ambient — drops columns param, reads owner binding

  Plan: docs/superpowers/plans/2026-07-09-ambient-sub-template-scope.md
  Plan-Tasks: 6"
  ```

### Task 7: `ChildTemplateTest` covers both modes; docs + KB sync

Lock both lowerings under test and sync the navigation docs.

**Files:**
- Modify: `test/Synto.Test/Templating/ChildTemplateTest.cs` (ensure a primary ambient-default snapshot AND a `Static = true` isolated snapshot exist; the `Static` one is the byte-identical regression pin from Phase 1)
- Modify: `docs/kb/subsystems/templating.md`, `docs/kb/glossary.md` (add the ambient-default sub-template model + `[Template(Static)]` opt-out; `path:line` links to `TemplateAttribute.cs`, the ambient emitter, the call-rewrite site) — or run the `refresh-kb` skill
- Modify: mark the spec **SHIPPED** with the branch + date.

**Interfaces (contract):** both modes have a green snapshot; KB reflects the new default + opt-out with live `path:line` anchors.

**Failing-test intent:** the suite already encodes both; this task is coverage completeness + docs. Gate = full green + KB builds/links resolve.

**Steps:**
- [ ] Ensure both-mode snapshots present and green.
- [ ] Sync KB (or `refresh-kb`) + flip spec status to SHIPPED.
- [ ] Full gate green.
- [ ] Commit:
  ```
  rtk jj commit -m "test,docs(templating): both-mode sub-template coverage + KB sync for ambient scope

  Plan: docs/superpowers/plans/2026-07-09-ambient-sub-template-scope.md
  Plan-Tasks: 7"
  ```

---

## Self-review — spec coverage

- Spec §2 goal 1 (ambient default) → Tasks 4, 5, 6. Goal 2 (`Static` opt-out) → Tasks 1, 2, 4. Goal 3 (no leak to root) → Task 1 (negative compile fact).
- Spec §3 (capture set = owner class-level bindings; varying inputs stay params; C# shadowing) → Task 4 contract + failing-test intent.
- Spec §4 (surface: `bool Static` on non-generic only, not a `TemplateOption` flag) → Task 1 + Global Constraints.
- Spec §5 (two lowerings; `Static` = today's path; `TemplateScope` still excludes staging both modes) → Tasks 2, 4; walker note in Task 4.
- Spec §6 validation: cacheability parity → Global Constraints + Tasks 4/5/6 gates. Semantic-equiv on flip → Task 6. Dog-food build → Task 6. `Static` byte-identical → Tasks 1/2 pins.
- Spec §7 migration → Task 6 (ObjectReader) + Task 7 (`ChildTemplateTest` both modes).
- Spec §8 open decisions: §8.1 name `Static` (owner-confirmed) → surface tasks; §8.2 call mechanic → **Task 3 spike (front-loaded)**; §8.3 dead-ambient = C# error → Global Constraints (no diagnostic); §8.4 `ChildTemplateTest` split → Task 7.

**Known risk — RESOLVED (2026-07-09).** Task 3 (the spike) landed: the ambient call-referencing mechanic is **auto-thread owner bindings as optional trailing factory params + inject the arg at `[Splice]` call sites** (spec §8.2, owner-approved). No closure, no nested-local-fn, no phantom stub, no new call surface. The rejected closure lowering was what threatened self-host compilation; the resolved mechanic keeps the child factory a normal top-level member, so the carrier always binds. Phase 1 (the `Static` opt-out) still ships value independently and does not depend on Phase 2.

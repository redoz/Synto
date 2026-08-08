# Sub-templates + generic `[Template<T>]` attribute — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Each task is an independently green unit (builds + tests pass on its own) ending in a `rtk jj commit` step. Use superpowers:test-driven-development inside every task: write the failing test/snapshot first, then the implementation, then confirm green.

**Spec:** `docs/superpowers/specs/2026-07-04-sub-templates-and-generic-template-attribute-design.md` (approved-for-planning).

> **DECISION (owner, 2026-07-04) — sub-template visibility is `internal static`, NOT `private static`.**
> Task 6's ObjectReader migration exposed a spec-internal contradiction: spec §5.3 said `private static`, but
> §2 states as a non-goal that this must *not* be "invisible to sibling carriers targeting the same class" —
> only `internal` satisfies that, and ObjectReader is a self-compiling dog-food whose carrier `[Splice]` source
> names `Factory.TypedGetter(...)` across a class boundary (`private` → CS0122). Resolution: sub-templates emit
> **`internal static`**. This flip — the `TemplateFactoryBuilder` recursion call (`SyntaxKind.InternalKeyword`)
> plus the landed Task 5 assertions/snapshot (`ChildTemplateTest`, `TemplateAttributeFormTest`) — **already
> landed** in the prep commit that precedes Task 6. **Wherever this plan or the spec says `private static` / "private"
> for a *sub-template*, read `internal static`.** With `internal`, Task 6 (ObjectReader nested-bare migration)
> and Task 7 (SY1034) are both feasible as written.

**Goal:** Replace the single `[Template(typeof(T))]` surface with two marker attributes — `TemplateAttribute<T>` (generic; carries the target) and parameterless `TemplateAttribute` (targetless) — and make a nested `[Template]` inside a `[Template]` carrier a **parent-driven private sub-template**: built by the parent's own build, emitted `private static` on the parent's target, folded into the parent's single generated document. This supersedes the shipped "child-template narrow case" *sibling* generation model for nested templates (the nested method today independently generates its own `public static` factory in its own `.g.cs`).

**Architecture:** Everything stays inside the existing generator architecture — all semantic work inside the `ForAttributeWithMetadataName` transform, the only pipeline state the equatable `TemplateGenerationResult`, failures as located `SY*` diagnostics. The plan is pitched at **contracts, behaviors, and interfaces**: each task states the behavior change, the seams it touches (`path:line`), and the green-gate test; the TDD implementer writes the bodies. The spec's §8.1 open decision is **encoded as the recommended option**: the sibling generation path for nested templates stays alive through all of Phase 1 (nested templates remain `[Template<X>]` and keep generating public siblings), so Phase 1 is a pure surface swap; Phase 2 flips nested templates to parent-driven and then deletes the sibling behavior. Two new diagnostic IDs are allocated up front: **SY1033** (SY-A: top-level `[Template]` requires a target) and **SY1034** (SY-B: nested sub-template must not specify a target). The spec's proposed SY1029/SY1030 are **not** used: SY1029 is already shipped (`AnalyzerReleases.Unshipped.md:26`, named-args-on-facade) and SY1030–SY1032 are reserved/earmarked by the in-flight `2026-07-04-issues-triage-fixes.md` plan (its Tasks 9, 10, 14). Both new IDs must be added to `src/Synto.SourceGenerator/AnalyzerReleases.Unshipped.md` (RS2008).

**Tech Stack:** C# / Roslyn incremental source generators (`netstandard2.0`, Roslyn 5.0 floor), xUnit v3 on Microsoft.Testing.Platform (`net10.0`), Verify snapshot testing, jj (Jujutsu) VCS.

## Global Constraints

- **Experimental feature ⇒ experimental branch.** This is new surface, not a fix: the work lands on an `experimental/sub-templates` bookmark, never directly on `main` (repo owner rule).
- **VCS is jj (Jujutsu), not git.** `rtk jj commit` / `rtk jj status`; never `git add`. Every task ends in a commit carrying the `Plan:` / `Plan-Tasks:` trailers shown in its commit step.
- **Tests run under MTP (SDK 10).** Raw `dotnet test --project <csproj>` — do NOT route `dotnet test` through `rtk`; scope with `-- --filter-method "*Name*"` (never bare `--filter`, never `--filter-class`). `rtk` is fine for `dotnet build` / `dotnet format`.
- **Snapshot accepts are mechanical but eyeballed:** run with `$env:DiffEngine_Disabled='true'`, overwrite `.verified.*` with `.received.*`. Semantic equivalence + green tests is the bar (per project calibration) except where a task explicitly promises **byte-identical**. An unexplained output-shape delta is a finding, not a rubber stamp. Deleted generated documents ⇒ delete the orphaned `.verified.cs` (the `SnapshotOrphanGuardTest` polices leftovers).
- **Incremental cacheability is sacred:** no `Compilation`/`ISymbol`/`SemanticModel`/`SyntaxNode` in pipeline *state*; all new work (child recursion, nesting checks, target-from-type-argument) runs inside the transform and flows out as `TemplateGenerationResult`. The new second FAWMN pipeline (Task 3) gets its **own tracking names** so the existing all-steps cacheability guards (`CacheabilityAssert.AllStepsCachedOrUnchanged`, `ObjectReaderIncrementalTests`) stay unambiguous and green.
- **Single-source-of-truth injected surface:** both attribute classes are authored once in `src/Synto/Templating/Definition/TemplateAttribute.cs` (already wired as `<EmbeddedResource>` at `src/Synto.SourceGenerator/Synto.SourceGenerator.csproj:43`) and injected `internal` by `SurfaceInjectionGenerator`. Never re-declare a marker in the generator.
- **Consumer LangVersion floor rises to C# 11** (generic attributes). Acceptable pre-GA (repo builds with `LangVersion=preview`, `Directory.Build.props:12`; test harnesses parse at Latest/Preview). Any harness or example that pins < 11 gets bumped inside the task that trips it; the floor is documented in Phase 3.
- **In-flight overlap (spec §8.4):** the `2026-07-04-issues-triage-fixes.md` plan is partially landed on this tree (SY1029, `SanitizeHintName` at `TemplateDocumentBuilder.cs:103`, the defensive `Emit`) and its remaining tasks touch `TemplateDocumentBuilder.cs:34–40` (Task 14) and `TemplateInfo.cs` (Task 1). Before starting, check which triage tasks have landed on the base revision and rebase expectations accordingly; if a cited line has drifted, the seam name (method) is the contract, not the line number.
- **Format gate:** `rtk dotnet format whitespace --verify-no-changes` clean at the end of every task. No git pre-commit hooks.
- **Green gate (every task):** `rtk dotnet build` (full solution — this also proves the self-host/bootstrap chain still builds in lockstep), `dotnet test --project test/Synto.Test/Synto.Test.csproj` (+ the other projects a task names), format gate, then commit.

---

## Phase 1 — attribute split + migration

End state of Phase 1: `[Template<X>]` is the only way to pin a target; the `Type` ctor is gone; a bare top-level `[Template]` reports SY1033. **Nested templates still carry `[Template<X>]` and still generate today's public siblings** (the §8.1 pure-surface-swap choice) — parent-driven ownership arrives in Phase 2.

### Task 1: Author `TemplateAttribute<T>` and teach the generator to read the target from the type argument (additive — old ctor still works)

The generic attribute and its discovery path land **alongside** the existing `TemplateAttribute(Type)` form so the whole suite stays green with zero migration in this task.

**Files:**
- Modify: `src/Synto/Templating/Definition/TemplateAttribute.cs` (add `public sealed class TemplateAttribute<T> : Attribute` next to the existing class — same authored file, so the existing `<EmbeddedResource>` at `Synto.SourceGenerator.csproj:43` and the injected copy pick it up with no csproj change)
- Modify: `src/Synto.SourceGenerator/Templating/Pipeline/TemplateFactorySourceGenerator.cs` (`Initialize`, :26 — register a second `ForAttributeWithMetadataName` source on the generic metadata name ``Synto.Templating.TemplateAttribute`1``, flowing into the same `GenerateTemplate`/`Emit` shape)
- Modify: `src/Synto.SourceGenerator/Templating/Pipeline/TemplateInfo.cs` (`Create`, :26 — accept the generic attribute form: target recovered from the attribute class's type argument instead of `ConstructorArguments[0]`)
- Modify: `src/Synto.SourceGenerator/Templating/Discovery/ChildTemplateFinder.cs` (:35 — carrier/child recognition must match **both** attribute symbols; `TemplateScope.ForCarrier` (`src/Synto.SourceGenerator/Templating/Pipeline/TemplateScope.cs:23`) rides on it)
- Re-accept: `test/Synto.Test/snapshots/SurfaceInjectionTest.VerifyInjectedSurface#TemplateAttribute.g.verified.cs` (injected surface gains the generic class)
- Test: new `test/Synto.Test/Templating/TemplateAttributeFormTest.cs` (or extend `SimpleTemplateTest.cs`)

**Interfaces (contract):**
- `TemplateAttribute<T>`: no constructor argument; retains `TemplateOption Options { get; set; }`; same `AttributeUsage` (`Method | Class | Struct`) as the non-generic form; authored `public`, injected `internal` — single source of truth.
- Generic discovery: the new FAWMN registration uses the arity-1 metadata name; its transform reuses `GenerateTemplate` unchanged except that `TemplateInfo.Create` now derives `TargetType` from the attribute's type argument when the attribute is the generic form (`ConstructorArguments` is empty there). The existing `Options` named-argument guard behavior (error-kind skipped, defaults `None`) applies identically to both forms. `TargetType.GetReferenceLocation()` (`TemplateInfo.cs:95`) must yield a sensible location for the generic form (the type-argument syntax) — the diagnostics that use it stay located.
- Carrier/child recognition (`ChildTemplateFinder`, `TemplateScope`) treats a method/class carrying **either** attribute form as a template; matching the generic form matches on the symbol's **original definition** (any `T`).
- Pipeline/tracking: the second source flows the same equatable `TemplateGenerationResult`; give its steps distinct tracking names (e.g. `TemplateTrackingNames.GenericTransform`/`.GenericResult` — naming is the implementer's, uniqueness is the contract). Cacheability call-out: nothing symbolic enters pipeline state.

**Failing-test intent:**
- Driver fact: a top-level `[Template<Factory>]` method produces a factory **identical in content** to the same template written `[Template(typeof(Factory))]` (compare the two runs' generated text, or pin a snapshot per form and eyeball equality).
- Nested-recognition fact: a carrier declared with `[Template<Factory>]` containing a nested `[Template<Factory>]` child still passes the existing leak-guard behavior (clone the `ChildTemplateTest.ChildTemplate_SpliceValueParam_DoesNotLeakIntoParentFactory` shape onto the generic form) — proves `TemplateScope`/`ChildTemplateFinder` recognize the generic carrier and child.
- Options fact: `[Template<Factory>(Options = TemplateOption.Bare)]` produces the Bare factory.

**Steps:**
- [ ] Write the failing facts above (generic form produces nothing today — FAWMN never fires).
- [ ] Verify they fail; existing suite green.
- [ ] Implement the attribute, the second FAWMN source, the `TemplateInfo.Create` type-argument path, and the both-forms carrier recognition.
- [ ] Re-accept `#TemplateAttribute.g.verified.cs` (delta = the added generic class, nothing else). All other snapshots **byte-identical**.
- [ ] Full gate: `rtk dotnet build` + `dotnet test --project test/Synto.Test/Synto.Test.csproj` + `dotnet test --project test/Synto.Bootstrap.Test/Synto.Bootstrap.Test.csproj` + format clean.
- [ ] Commit:
  ```
  rtk jj commit -m "feat(templating): TemplateAttribute<T> — target from the generic type argument, alongside the Type ctor

  Plan: docs/superpowers/plans/2026-07-04-sub-templates-and-generic-template-attribute.md
  Plan-Tasks: 1"
  ```

---

### Task 2: Migrate every `[Template(typeof(X))]` → `[Template<X>]` (tests, examples, ObjectReader)

Mechanical but wide; done while **both** forms still work so each edit is trivially safe. `src/` authors no `[Template(typeof(...))]` of its own (verified — the self-host/bootstrap chain consumes the attribute type only via `typeof(TemplateAttribute)` in the generator, covered by the build gate), so the sweep is tests + examples.

**Files (the migration sweep — every `[Template(typeof(` occurrence):**
- `test/Synto.Test/Templating/*.cs` — `SimpleTemplateTest` (25), `SyntaxBuilderTest` (17), `InterpolationFoldTest` (9), `DecorationTests` (9), `SwitchBuilderTest` (7), `DecorationDiagnosticsTests` (7), `StagingDiagnosticsTest` (6), `RunCollectShapesTest` (6), `ChildTemplateTest` (6), `StagedParameterTest` (5), `SpliceMemberGeneratorTest` (4), `RuntimeConverterTest` (4), `StagedRootTest` (2), `TemplateInfoGuardTest` (2), `RunCollectTest` (1), `SharedStagedParameterTest` (1), `ZeroCollisionTest` (1), `InjectedSurfaceCompletenessTest` (10)
- `test/Synto.Test/RoundTripTests.cs` (14), `test/Synto.Test/Match/MatchReplaceTests.cs` (1)
- `examples/Synto.Examples/Program.cs` (12)
- `examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Generator/ReaderTemplate.cs` (:27 carrier, :107 nested child — **both** become `[Template<Factory>]` in this task; the nested one flips to bare `[Template]` only in Phase 2) + the example's `README.md` code sample

**Interfaces (contract):**
- Pure attribute-form rewrite: `[Template(typeof(X))]` → `[Template<X>]`, `[Template(typeof(X), Options = O)]` → `[Template<X>(Options = O)]`. No other source change.
- **Expected snapshot outcome: byte-identical everywhere.** The template attribute is trimmed from quoted output (`TemplateFactoryBuilder.cs:34`), so generated text must not move. Any received delta is a finding (stop, investigate — most likely a location-sensitive diagnostic assertion, fix the assertion, not the output).
- `TemplateInfoGuardTest`'s malformed-`Options` facts keep their guard semantics on the generic form.

**Steps:**
- [ ] Sweep the rewrite across the files above (`rtk grep -n "Template(typeof"` to enumerate; zero occurrences left outside `docs/` and this plan's spec when done).
- [ ] Full gate: `rtk dotnet build`, `dotnet test --project test/Synto.Test/Synto.Test.csproj`, `dotnet test --project examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Tests/Synto.Example.ObjectReader.Tests.csproj`, `dotnet test --project test/Synto.Diagnostics.Test/Synto.Diagnostics.Test.csproj`, `dotnet test --project test/Synto.Bootstrap.Test/Synto.Bootstrap.Test.csproj`. Snapshots byte-identical (no accepts expected).
- [ ] Format clean.
- [ ] Commit:
  ```
  rtk jj commit -m "refactor(tests,examples): migrate [Template(typeof(X))] to [Template<X>]

  Plan: docs/superpowers/plans/2026-07-04-sub-templates-and-generic-template-attribute.md
  Plan-Tasks: 2"
  ```

---

### Task 3: Complete the split — parameterless `TemplateAttribute`, `Type` ctor removed, SY1033 wired, SY1034 authored

Clean break (pre-GA, no compat shim per `principles.md` Simplicity): the `TemplateAttribute(Type)` constructor is deleted and the non-generic attribute becomes the targetless marker. The non-generic FAWMN registration exists purely to catch top-level misuse (SY-A); nested bare `[Template]` produces nothing yet (interim — the parent takes ownership in Phase 2).

**Files:**
- Modify: `src/Synto/Templating/Definition/TemplateAttribute.cs` (:12 — remove the `Type target` ctor and the `Target` property; the class keeps `Options` only)
- Modify: `src/Synto.SourceGenerator/Templating/Pipeline/TemplateFactorySourceGenerator.cs` (:26 — the arity-0 FAWMN registration's transform: top-level ⇒ SY1033, nested-in-carrier ⇒ `null`)
- Modify: `src/Synto.SourceGenerator/Templating/Pipeline/TemplateInfo.cs` (`Create` no longer accepts a `Type` ctor arg path)
- Modify: `src/Synto.SourceGenerator/Templating/Diagnostics/TemplateDiagnostics.cs` (two new descriptors + factory helpers, matching the existing `DiagnosticInfo`-returning shape, e.g. `TemplateDiagnostics.cs:11–23`)
- Modify: `src/Synto.SourceGenerator/AnalyzerReleases.Unshipped.md` (SY1033, SY1034 rows)
- Add: a small shared "is this node nested inside a `[Template]`/`[Template<T>]` carrier" check in `src/Synto.SourceGenerator/Templating/Discovery/` — an ancestor *type declaration* itself carrying either attribute form (spec §4); reuse the existing carrier machinery/symbol matching from Task 1 rather than a bespoke walk. Used by **both** FAWMN transforms (the generic one consumes it in Task 7).
- Re-accept: `test/Synto.Test/snapshots/SurfaceInjectionTest.VerifyInjectedSurface#TemplateAttribute.g.verified.cs` (ctor gone, parameterless form)
- Test: `test/Synto.Test/Templating/TemplateAttributeFormTest.cs`

**Interfaces (contract):**
- `TemplateAttribute` (non-generic): parameterless; retains `Options`; same `AttributeUsage`. The `Type` ctor is **gone** — any residual `typeof` usage is a compile error the build gate catches.
- **New diagnostic SY1033 (SY-A):** *"Template '{0}' declared outside a [Template] carrier must specify a target type — use [Template<T>]"* (`Synto.Usage`, Error, located at the attribute). Reported by the arity-0 transform for a top-level bare `[Template]`; no factory produced.
- **New diagnostic SY1034 (SY-B):** *"Sub-template '{0}' nested inside a [Template] carrier must not specify a target — use [Template]"* (`Synto.Usage`, Error, located at the attribute/type argument). **Authored + registered in this task; its report site is wired in Task 7** — firing it now would kill the still-alive sibling path (§8.1 interim). The unused helper is documented as pending-Phase-2.
- Interim behavior pinned: a nested bare `[Template]` yields **no output and no diagnostic** (transform returns `null`) — a characterization fact documents this as temporary and is superseded by Task 5.
- Cacheability: the nesting check is pure syntax/symbol work inside the transform.

**Failing-test intent:**
- Driver fact: top-level `[Template]` (bare, on a method) ⇒ exactly one SY1033, located, no generated source, no CS8785.
- Characterization fact (green on write after implementation): nested bare `[Template]` inside a `[Template<X>]` carrier ⇒ no output, no diagnostic, parent unaffected (interim pin).
- Guard: `[Template<X>]` top-level unaffected (existing suite).

**Steps:**
- [ ] Write the SY1033 fact (fails: today a bare `[Template]` doesn't even compile against the ctor-only attribute — the fact lands with the new surface) and the interim characterization fact.
- [ ] Implement: ctor removal, parameterless class, arity-0 transform (SY1033 / nested-null), both descriptors, registry rows, the shared nesting check.
- [ ] Re-accept `#TemplateAttribute.g.verified.cs`; all other snapshots byte-identical.
- [ ] Full gate incl. ObjectReader + Bootstrap + Diagnostics test projects (proves no stray ctor usage anywhere); format clean.
- [ ] Commit:
  ```
  rtk jj commit -m "feat(templating): parameterless [Template]; Type ctor removed; SY1033 top-level-needs-target (SY1034 authored, wired in Phase 2)

  Plan: docs/superpowers/plans/2026-07-04-sub-templates-and-generic-template-attribute.md
  Plan-Tasks: 3"
  ```

**Phase 1 risks / verification:** Migration is wide but mechanical; the danger is a snapshot that *should* be byte-identical moving — treat any delta as a finding. C# 11 consumer floor is accepted and documented (Phase 3). The bootstrap/self-host chain never authors `[Template(typeof(...))]`, but the full-solution build in every task is the lockstep gate the spec asks for. End of Phase 1 the tree is releasable: same behavior as today, new surface only.

---

## Phase 2 — parent-driven private sub-templates

End state of Phase 2: a nested bare `[Template]` is built by its parent (private, folded into the parent's document); a nested `[Template<T>]` is SY1034; the sibling generation path for nested templates is dead. The existing `*_DoesNotLeakIntoParentFactory` guarantees hold throughout.

### Task 4: Generalize the assembly seams — visibility threading + method-list document (behavior-preserving)

Pure refactor that creates the two seams Task 5 plugs into. **All snapshots byte-identical** — that is the gate.

**Files:**
- Modify: `src/Synto.SourceGenerator/Templating/Pipeline/TemplateFactoryBuilder.cs` (`Build`, :24 — gains an emitted-visibility input, default public; the single hardcoded site `QuoteAndAssemble`'s `.AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword))` at :944 honors it. Threading a parameter vs. post-processing the returned method's modifiers is the implementer's choice; "exactly one substantive change inside the ~675-LOC unit" is the constraint — spec §5.3)
- Modify: `src/Synto.SourceGenerator/Templating/Pipeline/TemplateDocumentBuilder.cs` (`Build`, :22 — the `buildFactory` seam widens from `Func<UsingDirectiveSet, MethodDeclarationSyntax?>` to a **method-list** producer; `AddMembers` at :36–38 receives all methods; `FindReferencedHelpers` (:54, :128) scans **all** emitted methods so a helper referenced only by a sub-template is still injected — its completeness-by-scanning property is the contract; document file name stays keyed on the **parent's** source identifier at :103)
- Modify: `src/Synto.SourceGenerator/Templating/Pipeline/TemplateFactorySourceGenerator.cs` (:54–58 — the call site adapts to the widened seam, still passing a single-method list)

**Interfaces (contract):**
- `TemplateFactoryBuilder.Build`: same inputs/behavior plus a visibility choice; `public` callers produce today's output token-for-token.
- `TemplateDocumentBuilder.Build`: an empty/null method list bails exactly as a null method does today (:31); one method reproduces today's document byte-for-byte (member order, helper injection, usings merge, `#nullable enable`, formatting, hint name).
- No pipeline-state change; no new diagnostics.

**Failing-test intent:** none new — this task is gated by the **entire existing snapshot suite staying byte-identical** (the strongest available characterization). Optionally add one direct unit fact pinning that a two-method list lands both methods on the target and `FindReferencedHelpers` sees helpers referenced only by the second method (pre-proves the Task 5 seam; internal access via `InternalsVisibleTo`).

**Steps:**
- [ ] (Optional but recommended) write the two-method seam unit fact — red against the old signature, green after.
- [ ] Implement both seam generalizations.
- [ ] Full `Synto.Test` + Bootstrap + ObjectReader suites green; **zero snapshot accepts**.
- [ ] Format clean.
- [ ] Commit:
  ```
  rtk jj commit -m "refactor(templating): thread factory visibility; document builder assembles a method list (behavior-preserving)

  Plan: docs/superpowers/plans/2026-07-04-sub-templates-and-generic-template-attribute.md
  Plan-Tasks: 4"
  ```

---

### Task 5: Parent builds its children — recursion at the scope seam, `private static`, one document

The core of the feature (spec §5.2–§5.4). The recursion attaches where the parent already enumerates children: `ResolveScopeAndTrimForeignChildren` (`TemplateFactoryBuilder.cs:155`) — today it only trims `ctx.Scope.ForeignChildren` from the parent's quoted output (:158–159).

**Files:**
- Modify: `src/Synto.SourceGenerator/Templating/Pipeline/TemplateFactoryBuilder.cs` (:155 — for each foreign child, additionally construct the child `TemplateInfo` and recursively call `TemplateFactoryBuilder.Build` (:24) with visibility private; the trim stays)
- Modify: `src/Synto.SourceGenerator/Templating/Pipeline/TemplateInfo.cs` — the **second construction path** alongside `Create(GeneratorAttributeSyntaxContext)` (e.g. a nested/child factory method; exact name is the implementer's): `Source` = the child method node, `Target` = **the parent's `Target`** (pinned — reuse the parent's `TargetType`), `Options` = read from the child's `[Template]` attribute (in-transform, via the shared `SemanticModel`; same error-tolerant guard semantics as `Create`), `AttributeSyntax` = the child's attribute (so the child build trims it from the child's quoted output exactly like `TemplateFactoryBuilder.cs:34` does for a parent)
- Modify: `src/Synto.SourceGenerator/Templating/Pipeline/TemplateFactorySourceGenerator.cs` / the `TemplateDocumentBuilder.Build` call (:54–58) — the parent's build now returns the parent factory **plus** the child methods into the Task-4 method-list seam (how the child methods travel from the recursion to the document list — via the build context or a widened return — is the implementer's; the contract is one document, parent factory first, children in declaration order)
- Modify: `test/Synto.Test/Templating/ChildTemplateTest.cs` — the nested fixtures (`ChildTemplate_NestedInParentCarrier`, `ChildTemplate_SpliceValueParam_DoesNotLeakIntoParentFactory`) flip their nested child from `[Template<Factory>]` to bare `[Template]`
- Snapshots: **delete** the orphaned `test/Synto.Test/Templating/snapshots/ChildTemplateTest.ChildTemplate_NestedInParentCarrier#Factory.TypedGetter.g.verified.cs` (the child no longer produces its own document); **re-accept** `...#Factory.Reader.g.verified.cs` (now contains `private static TypedGetter` after the public parent factory). The *standalone*-child fixture (`ChildTemplate_MemberGeneratorInvokesChildFactory`) is top-level and stays `[Template<Factory>]`/public — its snapshots must stay **byte-identical**.

**Interfaces (contract):**
- **Generation contract (spec §5.2–§5.4):** a nested bare `[Template]` method inside a carrier is built by the parent's transform run: emitted `private static` on the parent's target, in the parent's single `{Target.FullName}.{parentSource.Identifier}.g.cs`. No independent `.g.cs`, no arity-0 transform output (already `null` since Task 3). "Private" means off the public API surface, not invisible to sibling carriers targeting the same class (approved scope decision, spec §2).
- **Diagnostics policy (decision):** a failing child build reports its (child-located) diagnostics into the same list and is **omitted** from the method list; the parent factory still emits (mirrors today's independent-sibling failure isolation).
- **Recursion depth:** one level — the foreign children of this carrier. A child's own nested templates are out of scope v1 (note: `Build` is `TemplateInfo`-keyed and entrypoint-independent, so deeper nesting composes later; do not build it now).
- **Leak guarantees preserved:** the child subtree remains trimmed from the parent's quoted output and skipped by the parent's `TemplateScopedWalker`-based finders — the recursion adds generation, it must not remove any exclusion. `ChildTemplate_SpliceValueParam_DoesNotLeakIntoParentFactory` and `DecorationTests.DecorationOnNestedChild_DoesNotLeakToParentFactory` are the named guards.
- Cacheability: recursion runs wholly inside the parent's transform; child methods are syntax handed to the document builder, never pipeline state.

**Failing-test intent:**
- Re-pointed nested fixture: `ChildTemplate_NestedInParentCarrier` with a bare-`[Template]` child ⇒ **one** generated document (`Factory.Reader.g.cs`) containing the public parent factory and the `private static TypedGetter(...)` sub-template; post-gen compile clean (the parent's `[Splice]` generator invoking `Factory.TypedGetter(...)` from inside the same class proves `private` suffices).
- Helper-injection fact: a sub-template whose body is the only user of an emitted helper (e.g. `.ToSyntax()` via an unquoted value) ⇒ the helper's `file static class` is injected into the shared document (pins the `FindReferencedHelpers` all-methods contract).
- Options fact: a nested `[Template(Options = TemplateOption.Bare)]` child honors its own options.
- Leak guards: the two `*_DoesNotLeak*` facts stay green on the bare-child form.

**Steps:**
- [ ] Flip the nested fixtures to bare `[Template]` + write the new facts; run — fails (nested bare produces nothing per the Task-3 interim pin; delete/supersede that characterization fact now).
- [ ] Implement the child `TemplateInfo` path, the recursion at :155, and the method-list emission.
- [ ] Delete the orphaned child-document snapshot; re-accept the parent-document snapshot (eyeball: same factory bodies as the old sibling output, modulo `private` and placement). `SnapshotOrphanGuardTest` green.
- [ ] Full `Synto.Test` + Bootstrap suites green; standalone-child + all non-nested snapshots byte-identical; format clean.
- [ ] Commit:
  ```
  rtk jj commit -m "feat(templating): parent-driven private sub-templates — nested [Template] builds via the parent into one document

  Plan: docs/superpowers/plans/2026-07-04-sub-templates-and-generic-template-attribute.md
  Plan-Tasks: 5"
  ```

---

### Task 6: Migrate the remaining nested consumers (ObjectReader dog-food + DecorationTests)

Move every remaining nested `[Template<X>]` onto the new bare-`[Template]` sub-template surface, with the ObjectReader example as the behavior gate.

**Files:**
- Modify: `examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Generator/ReaderTemplate.cs` (:107 — the nested `TypedGetter` child becomes bare `[Template]`; the carrier at :27 stays `[Template<Factory>]`) + refresh the stale in-file comments describing the sibling model (:101–106)
- Modify: `test/Synto.Test/Templating/DecorationTests.cs` (:250 `DecorationOnNestedChild_DoesNotLeakToParentFactory` fixture's nested child → bare `[Template]`) and any other nested fixture the sweep finds (`rtk grep` for method-level `[Template<` inside `[Template`-carrier classes)
- Re-accept: any `DecorationTests`/other affected `test/Synto.Test` snapshots (same delta class as Task 5: child folds into parent document as `private`); delete newly-orphaned child-document `.verified.cs` files
- Gate (unchanged files): `examples/.../snapshots/ObjectReaderSnapshotTests.Generates_Specialized_Reader_For_Person#ObjectReader.g.verified.cs`, `ObjectReaderBehaviorTests.cs`, `ObjectReaderIncrementalTests.cs`

**Interfaces (contract):**
- ObjectReader's generated **consumer output** (`ObjectReader.g.cs`) is expected **byte-identical** — the getters the `[Splice]` generator produces don't change; only the generator-internal `Factory.TypedGetter` becomes `internal static` (see DECISION note) and moves into the parent's document inside the example-generator compilation. `internal` (not `private`) is exactly what lets the carrier's `[Splice]` source keep naming `Factory.TypedGetter(...)` across the class boundary in the same assembly — that is the whole reason the visibility is `internal`. If the final output moves anyway, the delta must be nil-semantic and explained (per the semantic-equivalence calibration).
- `ObjectReaderBehaviorTests` (runtime semantics) and `ObjectReaderIncrementalTests` (**all steps** Cached/Unchanged on unrelated edit) green — the incremental gate now also covers the recursion path under real dog-food load.

**Failing-test intent:** none new — this is a consumer migration gated by the strongest existing suites. The red step is trivial: after flipping ReaderTemplate's nested attribute, the example-generator build/tests only pass if Tasks 3–5 delivered the contract.

**Steps:**
- [ ] Flip ReaderTemplate's nested child + DecorationTests fixture (+ sweep stragglers) to bare `[Template]`.
- [ ] `rtk dotnet build` then `dotnet test --project examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Tests/Synto.Example.ObjectReader.Tests.csproj` — behavior + incremental + snapshot green (accepts only per the contract above).
- [ ] Full `Synto.Test` green; re-accept/delete the DecorationTests-class snapshots as in Task 5.
- [ ] Format clean.
- [ ] Commit:
  ```
  rtk jj commit -m "refactor(objectreader,tests): nested children ride the bare [Template] sub-template surface

  Plan: docs/superpowers/plans/2026-07-04-sub-templates-and-generic-template-attribute.md
  Plan-Tasks: 6"
  ```

---

### Task 7: Enforce SY1034 — nested `[Template<T>]` is an error; the nested sibling path is dead

With zero legitimate nested-generic usages left (Tasks 5–6), the generic FAWMN transform gains the nesting check and the §8.1 interim ends.

**Files:**
- Modify: `src/Synto.SourceGenerator/Templating/Pipeline/TemplateFactorySourceGenerator.cs` (the generic-source transform: target node nested-in-carrier ⇒ report **SY1034**, produce no factory — reusing the Task-3 shared nesting check)
- Modify: `src/Synto.SourceGenerator/Templating/Diagnostics/TemplateDiagnostics.cs` (the SY1034 helper gains its call site; drop the pending-Phase-2 note)
- Test: `test/Synto.Test/Templating/TemplateAttributeFormTest.cs`

**Interfaces (contract):**
- Declaration-rules matrix complete (spec §4): top-level `[Template<T>]` ✅ public factory on `T`; top-level `[Template]` ❌ SY1033; nested `[Template]` ✅ private sub-template on the parent's target; nested `[Template<T>]` ❌ SY1034, no factory (neither sibling nor sub-template).
- The parent build is unaffected by an SY1034 child: the child is still a foreign child (trimmed + walk-skipped) but is **not** built as a sub-template — the parent's document emits without it, the error stands alone against the child.

**Failing-test intent:**
- Driver fact: nested `[Template<Factory>]` inside a `[Template<Factory>]` carrier ⇒ exactly one located SY1034, **no** `Factory.TypedGetter`-style sibling document, parent factory still generated.
- Guard: the full suite proves no legitimate usage trips it (would have been caught in Task 6's sweep).

**Steps:**
- [ ] Write the failing fact (today the nested generic still generates a public sibling).
- [ ] Implement the check + report.
- [ ] Full `Synto.Test` + ObjectReader + Bootstrap suites green; no snapshot deltas expected.
- [ ] Format clean.
- [ ] Commit:
  ```
  rtk jj commit -m "feat(templating): SY1034 — nested [Template<T>] must not pin a target; nested sibling generation removed

  Plan: docs/superpowers/plans/2026-07-04-sub-templates-and-generic-template-attribute.md
  Plan-Tasks: 7"
  ```

**Phase 2 risks / verification:** The recursion touches the headline ~675-LOC build unit whose step **order is load-bearing** (`TemplateFactoryBuilder.cs:12–21`) — the recursion must attach at :155 without reordering the parent's steps; byte-identical non-nested snapshots are the tripwire. Snapshot churn is confined to nested-fixture documents (child doc deleted + parent doc gains a private method); anything wider is a finding. If the in-flight triage plan's Task 14 (`TemplateDocumentBuilder.cs:34–40` type-parameter rebuild) lands before/during this phase, rebase Task 4's seam over it — the contracts compose (clean-shell rebuild vs. method-list is orthogonal). The `experimental/quote-action-pipeline` lineage is already in this tree (PlanAssembler/PerNodePipelineQuoter at `TemplateFactoryBuilder.cs:925–927`); the recursion reuses `TemplateQuoterFactory.Create` per child untouched.

---

## Phase 3 — docs/KB

### Task 8: KB refresh + surface documentation

**Files:**
- Run the `refresh-kb` skill (KB anchors drift heavily: `TemplateAttribute.cs`, `TemplateInfo.cs`, `TemplateFactorySourceGenerator.cs`, `TemplateFactoryBuilder.cs`, `TemplateDocumentBuilder.cs`, `ChildTemplateFinder.cs`)
- Modify: `docs/kb/subsystems/templating.md` — the `[Template]` surface (two attribute forms, the declaration-rules matrix, SY1033/SY1034), the parent-driven sub-template generation model (one document, private children), the death of the nested-sibling model
- Modify: `docs/kb/glossary.md` (and the surface/index entries `refresh-kb` flags) — `[Template<T>]` / `[Template]` / "sub-template" entries; note the C# 11 consumer floor
- Modify: `docs/superpowers/specs/2026-07-04-sub-templates-and-generic-template-attribute-design.md` — status → shipped; record the closed open-decisions (§8.1 resolved as "sibling path alive through Phase 1"; §8.2 IDs landed as SY1033/SY1034 because SY1029–SY1032 were taken/reserved)

**Steps:**
- [ ] Invoke `refresh-kb`; review its diff (path:line anchors + the 5-type registry in `docs/kb/conventions.md` if the attribute split touches it).
- [ ] Update `templating.md`, glossary/surface entries, and the spec status per the contract above.
- [ ] Full gate green (docs-only — build/test/format all unaffected but run once for the commit discipline).
- [ ] Commit:
  ```
  rtk jj commit -m "docs(kb,spec): sub-templates + [Template<T>] shipped — KB refresh, surface docs, spec status

  Plan: docs/superpowers/plans/2026-07-04-sub-templates-and-generic-template-attribute.md
  Plan-Tasks: 8"
  ```

**Phase 3 risks / verification:** none technical; the only failure mode is stale KB anchors — `refresh-kb` owns that.

---

## Deferred / out of scope (do NOT implement here)

- **Cross-carrier name-collision detection on a shared target** (spec §2) — two carriers targeting one class with same-named members stays undetected (future work). Likewise same-document collision between a parent factory and a same-named child.
- **Recursion beyond one level** (a sub-template's own nested `[Template]`s) — `Build` is entrypoint-independent so it composes later; not built now.
- **Nested `[Template]` on a nested *type*** (class/struct child carriers) — child discovery is method-level (`ChildTemplateFinder`), unchanged.
- **Compat shim for `[Template(typeof(T))]`** — deliberately none (pre-GA clean break, spec §3).
- **SY1030** — left to its earmark in the issues-triage plan's Task-9 follow-up (splice-axis diagnostic); not consumed here.

## Self-Review

- **Spec coverage:** §3 two-attribute surface → Tasks 1, 3 · §4 declaration matrix + diagnostics → Tasks 3, 7 (SY-A/SY-B as SY1033/SY1034) · §5.1 two FAWMN sources → Tasks 1, 3 · §5.2 recursion at `ResolveScopeAndTrimForeignChildren` + second `TemplateInfo` path → Task 5 · §5.3 visibility at the single :944 site → Task 4 · §5.4 method-list document / all-methods helper scan / parent-keyed file name → Tasks 4–5 · §6 migration + snapshot re-accepts → Tasks 2, 5, 6 · §7 phase gates → phase notes · §8.1 → encoded (sibling alive through Phase 1, deleted in Phase 2 Task 7) · §8.2 → IDs re-numbered against the live registry · §8.3 C# 11 floor → Global Constraints + Phase 3 docs · §8.4 in-flight overlap → Global Constraints + Phase 2 risk note. ✓
- **Individually greenable + ordered:** every task ends fully green (build + named test projects + format + commit); the additive-then-migrate-then-remove sequence (Tasks 1→2→3) and the seams-then-recursion-then-consumers-then-enforcement sequence (4→5→6→7) never leave a red intermediate. ✓
- **Contracts, not code:** every task states behavior/interface + seams (`path:line` verified against the current tree) + gating tests; no method bodies prescribed; implementer-choice points are explicitly marked. ✓
- **Invariants honored:** zero-dependency consumer surface untouched; single-source-of-truth injection (one authored attribute file, one embedded resource); netstandard2.0/Roslyn-5.0 unaffected; cacheability — all new work in-transform, second pipeline distinctly tracked, all-steps guards named as gates; snapshots pin output shape with byte-identical vs. semantic-re-accept stated per task. ✓
- **Leak guarantees:** `ChildTemplate_SpliceValueParam_DoesNotLeakIntoParentFactory` + `DecorationOnNestedChild_DoesNotLeakToParentFactory` named as must-stay-green gates in Tasks 5–6. ✓

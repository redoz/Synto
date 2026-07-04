# Issues-Triage Fixes — High #1–6 + Medium #7–24 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close every high (#1–6) and medium (#7–24) finding from the 2026-07-03 codebase-quality evaluation (`issues.md`), ordered by the assessment's "Top 3 do first": close the SY0000-net escape hatches (#3, #4), fix facade-call recognition (#1, folding in #7), un-block the staged-binding LINQ shape (#2), then the remaining highs (#5, #6), then the mediums. Low findings #25–36 are explicitly out of scope.

**Architecture:** Every fix stays inside the existing generator architecture — semantic work in the `ForAttributeWithMetadataName` transform, equatable value types (`*GenerationResult`, `EquatableArray<DiagnosticInfo>`, `LocationInfo`) as the only pipeline state, failures surfaced as located `SY*`/`SDG*` diagnostics, never exceptions. The plan is pitched at **contracts, behaviors, and interfaces**: each task states the behavior change, the affected files, the interface (signatures / diagnostic IDs + messages / snapshot deltas), and a failing-test intent. The TDD implementer writes the method bodies. Four new templating diagnostic IDs are allocated up front so tasks cannot collide: **SY1029** (named args on facade calls, #7), **SY1030** (unsupported `Parameter<T>()` shape, #9), **SY1031** (invalid `[SyntaxBuilder]` declaration, #10), **SY1032** (unsupported target/ancestor kind, #15). Each new ID must also be added to `src/Synto.SourceGenerator/AnalyzerReleases.Unshipped.md` (RS2008).

**Tech Stack:** C# / Roslyn incremental source generators (`netstandard2.0`, Roslyn 5.0 floor), xUnit v3 on Microsoft.Testing.Platform (`net10.0`), Verify snapshot testing, jj (Jujutsu) VCS.

## Global Constraints

- **VCS is jj (Jujutsu), not git.** Use `jj commit` / `jj status`. Never `git add`. The two pre-existing CRLF-only snapshots under `test/Synto.Diagnostics.Test/snapshots/*DiagnosticsAttribute.g.verified.cs` are EOL noise — **Task 7 (#21) is the one task that normalizes them**; no other task may stage or touch them.
- **Tests run under MTP (SDK 10).** Use raw `dotnet test --project <csproj>` — do NOT route `dotnet test` through `rtk` (it mangles MTP args). The green gate per task is the full `test/Synto.Test/Synto.Test.csproj` run, plus `test/Synto.Diagnostics.Test/Synto.Diagnostics.Test.csproj` and `test/Synto.Bootstrap.Test/Synto.Bootstrap.Test.csproj` for tasks that touch those areas (called out per task).
- **Snapshot accept is mechanical:** run failing snapshot tests with `$env:DiffEngine_Disabled='true'`, then overwrite the `.verified.cs`/`.verified.txt` with the `.received.*` sibling. Eyeball every accepted delta — an unexplained output-shape change is a finding, not a rubber stamp. Per the project's calibration, **semantic equivalence + green tests** is the bar for re-accepts, not byte-identity (except where a task explicitly promises byte-identical).
- **Incremental cacheability is sacred:** no `ISymbol`/`SemanticModel`/`SyntaxNode` may enter pipeline *state* — all semantic work stays inside the generator transform (nodes/values in, equatable results out). Tasks that touch a transform or a pipeline value type (Tasks 1, 2, 10, 19) call this out explicitly; every other task must simply not regress it.
- **Single-source-of-truth injected surface:** consumer-facing marker/helper types are authored once in `src/Synto/...` and injected `internal` via `<EmbeddedResource>` (LogicalName `Synto.Runtime.<Name>.cs`) in `src/Synto.SourceGenerator/Synto.SourceGenerator.csproj`; emitted helpers are injected `file`-scoped from the same authored sources (`FileLocalHelpers`). Fixes to authored helper files (Tasks 6, 17) automatically fix both the public copy and the injected copy — never fork a second definition.
- **Format gate:** `dotnet format whitespace --verify-no-changes` must be clean (whitespace scope only) at the end of every task. No git pre-commit hooks — the gate is CI/workflow.
- **Every task ends in a `jj commit`** with a conventional message; the working tree must be green (build + tests + format) at each commit.

---

### Task 1: Move `TemplateInfo.Create` inside the SY0000 net; guard malformed `Options` (#3)

Closes **#3** (high). `TemplateInfo.Create(syntaxContext)` runs before the `try` in `GenerateTemplate`, and inside it `(TemplateOption)namedArgs.Value.Value!` null-unboxes when the user is mid-edit (`Options = <error>` → `TypedConstantKind.Error`, `Value == null`). The NRE escapes the transform → opaque CS8785, entire generator output dropped during ordinary typing. Same unguarded pattern: `attr.ApplicationSyntaxReference!` at `TemplateInfo.cs:35`.

**Files:**
- Modify: `src/Synto.SourceGenerator/Templating/Pipeline/TemplateFactorySourceGenerator.cs` (`GenerateTemplate`, ~:37–72 — restructure so the `try` wraps the `TemplateInfo.Create` call)
- Modify: `src/Synto.SourceGenerator/Templating/Pipeline/TemplateInfo.cs` (`Create`, :26–65 — null-tolerant guards)
- Test: `test/Synto.Test/Templating/SimpleTemplateTest.cs` (or a new `TemplateInfoRobustnessTest.cs`)

**Interfaces (contract):**
- `GenerateTemplate` behavior: **no reachable input state may throw out of the transform.** Any exception from `TemplateInfo.Create` is converted to `Diagnostics.InternalError` exactly like exceptions from `Validate`/`Build` (the result still flows out as an equatable `TemplateGenerationResult`). Cacheability note: this is a pure control-flow move inside the transform — no pipeline-state change.
- `TemplateInfo.Create` behavior: a named `Options` argument whose `TypedConstant.Kind == TypedConstantKind.Error` or whose boxed value is not an `int`/`TemplateOption` is **skipped** (defaults to `TemplateOption.None`), not unboxed; a null `ApplicationSyntaxReference` returns `null` instead of throwing.

**Steps:**
- [ ] **Write the failing test.** Driver fact: a `[Template(typeof(Factory), Options = (Synto.Templating.TemplateOption)nonexistent)]` source (an unresolvable `Options` expression — mid-keystroke shape). Assert the generator run result carries **no thrown exception** (`runResult.Results[0].Exception is null`) and **no CS8785** in the run diagnostics; either no output or an `SY*` diagnostic is acceptable. Add a second case: well-formed `Options = TemplateOption.Bare` still produces the Bare factory (guard against over-skipping).
- [ ] Run `dotnet test --project test/Synto.Test/Synto.Test.csproj` — the new fact FAILS (exception recorded / CS8785 present). All others pass.
- [ ] Implement the two guards + the try-scope move per the contract above.
- [ ] Full suite green: `dotnet test --project test/Synto.Test/Synto.Test.csproj`.
- [ ] `dotnet format whitespace --verify-no-changes` clean.
- [ ] `jj commit -m "fix(templating): TemplateInfo.Create runs inside the SY0000 net; malformed Options no longer NREs the transform (#3)"`

---

### Task 2: Hint names unique + sanitized by construction; defensive `Emit` in all four generators (#4)

Closes **#4** (high, systemic). Hint-name construction can collide (same-named `[Template]` overloads / same-named templates in different carriers targeting one type; two `[Diagnostic]` partial-method overloads; same-named `[Match]` methods) or contain invalid characters (generic targets put `<`/`>` in the name; builder-facade sanitization misses `?`/`*`). `AddSource` then throws **inside `RegisterSourceOutput`'s emit action — outside the transform try/catch** → CS8785, all output dropped.

**Files:**
- Modify: `src/Synto/Emit/GeneratedSourceFilenameExtensions.cs` (the shared home — internal in Synto.Core, `InternalsVisibleTo` both generator assemblies)
- Modify: `src/Synto.SourceGenerator/Templating/Pipeline/TemplateDocumentBuilder.cs` (:103 — hint construction)
- Modify: `src/Synto.SourceGenerator/Matching/Emit/MatchComposer.cs` (:83 — `TargetFullName` includes type parameters)
- Modify: `src/Synto.SourceGenerator/Templating/Builders/SyntaxBuilderFacadeGenerator.cs` (:82–95 — replace the blacklist `Replace` chain with the shared whitelist helper)
- Modify: `src/Synto.Diagnostics/DiagnosticsGenerator.cs` (:336 — filename; plus its `Emit`)
- Modify: the `Emit`/`RegisterSourceOutput` action of all four shipped generators (`TemplateFactorySourceGenerator.cs:74–81`, `MatchFactorySourceGenerator`, `SyntaxBuilderFacadeGenerator.cs:31–33`, `DiagnosticsGenerator.cs:40` + its emit)
- Tests: `test/Synto.Test/Templating/` (new driver facts), `test/Synto.Diagnostics.Test/DiagnosticsGeneratorTest.cs`, `test/Synto.Test/Match/` (generic-target fact)

**Interfaces (contract):**
- New shared helper (signature illustrative): `internal static string SanitizeHintName(this string raw)` — **whitelists** the characters Roslyn accepts in hint names (letters, digits, `_ . , - + \``; implementer verifies the exact allowed set against Roslyn's `AddSource` validation) and maps everything else to `_`. All four hint-construction sites route through it.
- **Uniqueness by construction:** each generator's hint incorporates a deterministic, in-transform disambiguator for the known collision classes — for Diagnostics, the partial method's parameter signature/arity (flip `GeneratedSourceFilenameExtensions`'s `SymbolDisplayFormat` away from `ParameterOptions.None`, or append an explicit arity suffix); for Templating, the template carrier's containing-type qualification and/or parameter signature so two same-named templates targeting one type get distinct hints; for Matching, the (sanitized) generic target name plus the match method's signature where needed. Disambiguators must be **pure functions of the symbol** (no counters, no cross-result state) — cacheability requires per-transform determinism.
- **Defensive `Emit`:** each generator's output action wraps `AddSource` in a `try/catch (ArgumentException)` that reports a located internal-error diagnostic (SY0000 / SDG0000 family; message names the offending hint) instead of letting the exception kill the generator. This code runs outside the cached pipeline, so constructing the `Diagnostic` directly there is fine.
- **Expected churn:** changing hint construction renames generated files, which renames Verify snapshot files (`…#<hint>.verified.cs`). Renames are mechanical: delete the orphaned `.verified.cs`, accept the `.received.cs` under the new name (the `SnapshotOrphanGuardTest` will police leftovers). Keep the common-case names as close to today's as possible to minimize churn.

**Failing-test intent:**
- Templating: two same-named `[Template]` methods in different carrier classes targeting one factory type → both factories emitted (distinct hints) or a located SY diagnostic — never CS8785 / silent generator death.
- Templating: a generic target `partial class Factory<T>` → generation does not throw on the `<`/`>` hint (pairs with Task 14, which makes the *content* land on the right type; here only assert no crash).
- Diagnostics: two `[Diagnostic]` partial-method overloads with the same name → both emitted with distinct hints (run `test/Synto.Diagnostics.Test`).
- Builders: a `[SyntaxBuilder]` with an `int?` parameter → facade hint contains no `?`.

**Steps:**
- [ ] Write the failing driver facts above (each asserts no run exception, no CS8785, and the expected number of generated sources).
- [ ] Run both affected test projects — new facts FAIL as described.
- [ ] Implement `SanitizeHintName` + per-generator disambiguators + the four defensive `Emit` wrappers.
- [ ] Accept mechanically-renamed snapshots (`$env:DiffEngine_Disabled='true'`; eyeball that content is unchanged, only file names moved). `SnapshotOrphanGuardTest` green.
- [ ] Full gate: `dotnet test --project test/Synto.Test/Synto.Test.csproj` + `--project test/Synto.Diagnostics.Test/Synto.Diagnostics.Test.csproj` + `--project test/Synto.Bootstrap.Test/Synto.Bootstrap.Test.csproj`.
- [ ] `dotnet format whitespace --verify-no-changes` clean.
- [ ] `jj commit -m "fix(generators): hint names sanitized+unique by construction; Emit degrades AddSource failure to a diagnostic (#4)"`

---

### Task 3: Facade recognition requires bind-failure; named arguments diagnosed (#1, #7)

Closes **#1** (high) and **#7** (medium) — same files. Today any invocation whose *simple name* matches a registered builder (`Switch`, `Case`, `Default`, `Arm`, `Return`, … — injected into every consumer assembly) is hijacked, even when it successfully binds to the user's own method (`settings.Default()`, `validator.Return(x)`), and `BuilderStep` drops the receiver — silent wrong generated code. Separately, argument binding is strictly positional: `ArgumentSyntax.NameColon` is never consulted, so reordered named arguments silently mispair quoted/staged treatment (both in `FacadeArgumentBinder.Bind` and `FacadeCallFinder.TryBuiltIn`).

**Files:**
- Modify: `src/Synto.SourceGenerator/Templating/Discovery/FacadeCallFinder.cs` (:52–70 recognition gate; :76–98 `TryBuiltIn` named-arg guard)
- Modify: `src/Synto.SourceGenerator/Templating/Builders/FacadeArgumentBinder.cs` (:30–76 named-arg guard)
- Modify: `src/Synto.SourceGenerator/Templating/Diagnostics/TemplateDiagnostics.cs` (+ `AnalyzerReleases.Unshipped.md`) — new descriptor
- Test: `test/Synto.Test/Templating/SyntaxBuilderTest.cs`

**Interfaces (contract):**
- **Recognition rule (#1):** an invocation is treated as a user-facade call **only when** `semanticModel.GetSymbolInfo(invocation)` yields no symbol or an error/candidate-only result (the synthesized facade is absent from the analyzed compilation, so genuine facade calls do not bind), **or** the bound symbol is the builder implementation itself (the `FacadeMatchesBuilder` self-facade case, where the template binds directly to the real injected builder). A successfully-bound, unrelated method is never rewritten. All checks stay inside the transform.
- **New diagnostic (#7):** `SY1029` — *"Named arguments are not supported on builder facade calls"* (`Synto.Usage`, Error, located at the argument). Any `NameColon` on a recognized facade call (user or built-in `Template.Member`/`TypeOf`) reports SY1029 and bails that call (v1 policy: diagnose, don't reorder). Factory helper: `TemplateDiagnostics.NamedArgumentOnFacade(Location?, string builderName)`.

**Failing-test intent:**
- (#1) A template whose body calls a *bound* user method named `Default()` on a receiver: assert the generated factory **quotes the call verbatim** (output contains the quoted `settings.Default()` member-access shape, zero occurrences of `SyntoBuilders`), and no diagnostics.
- (#1) Guard the other side: an unbound `Case(...)`/`Switch(...)` facade call still resolves to the builder (existing `SwitchBuilderTest`/`SyntaxBuilderTest` snapshots stay green — byte-identical expected).
- (#7) A facade call with a reordered named argument (`Cast(x: expr, t: …)`-style against a user builder, and a named-arg `Template.Member` call) → SY1029 reported, no factory.

**Steps:**
- [ ] Write the three failing facts.
- [ ] Verify they fail (hijack happens today; named args silently mispair today).
- [ ] Implement the bind-failure gate, the self-facade allowance, and the SY1029 guard at both binder sites; register SY1029 in `AnalyzerReleases.Unshipped.md`.
- [ ] Full `Synto.Test` suite green; existing builder snapshots byte-identical.
- [ ] Format clean.
- [ ] `jj commit -m "fix(builders): facade recognition requires bind-failure; named args on facade calls report SY1029 (#1, #7)"`

---

### Task 4: Un-block the staged-binding LINQ shape — no spurious SY1013 for symbols declared inside the binding expression (#2)

Closes **#2** (high). `FindImpossibleCuts` flags any `ILocalSymbol`/`IParameterSymbol` not in `_live` — but lambda parameters, `out var`s, and pattern variables declared *inside the binding expression itself* (`Unquote(liveItems.Select(x => x.Length).Sum())`) are never in `_live`, so a perfectly factory-time-evaluable binding hard-bails with SY1013. This is exactly the shape the ObjectReader dog-food says users write first.

**Files:**
- Modify: `src/Synto.SourceGenerator/Templating/Analysis/BindingTimeClassifier.cs` (`FindImpossibleCuts`, :230–251)
- Test: `test/Synto.Test/Templating/` (e.g. `StagedRootTest.cs` or a new `BindingTimeClassifierTest.cs` driver fact + snapshot)

**Interfaces (contract):**
- `FindImpossibleCuts` behavior: an identifier is **not** an impossible cut when its symbol is *declared within the binding expression itself* — decided by declaring-syntax span containment (the symbol's `DeclaringSyntaxReferences[0]` span lies within `expression.FullSpan` on the same tree), or equivalently by pre-collecting the locally-declared symbols of the expression and skipping them. Lambda parameters, `out var`, pattern variables, and (defensively) `IRangeVariableSymbol` query variables all fall out of this rule. Symbols declared *outside* the expression that are genuinely output-world still cut (SY1013 unchanged for the true-positive case). All work stays transform-local.

**Failing-test intent:**
- Executed round-trip: a template with a live collection root and `Unquote(liveItems.Select(x => x.Length).Sum())` → no SY1013; the factory is generated and (via the existing Emit+Invoke tail pattern) produces the expected folded literal. Add sibling shape coverage: an `out var` and an `is`-pattern variable inside a binding expression.
- Regression guard: a binding expression referencing a genuine output-world local still reports SY1013 (clone from the existing SY1013 coverage).

**Steps:**
- [ ] Write the failing facts (LINQ shape + out-var/pattern shapes + the true-positive guard).
- [ ] Verify: LINQ/out-var/pattern facts fail with SY1013 today; the guard passes.
- [ ] Implement the declared-within-expression exclusion.
- [ ] Full `Synto.Test` suite green (existing SY1013 tests unchanged).
- [ ] Format clean.
- [ ] `jj commit -m "fix(templating): symbols declared inside a binding expression no longer trigger spurious SY1013 (#2)"`

---

### Task 5: Diagnostics generated partial is a clean shell (#5)

Closes **#5** (high). `parent.WithMembers(List(members))` at `DiagnosticsGenerator.cs:315–318` clones the *user's* `ClassDeclarationSyntax` — keeping its `AttributeLists`, `BaseList`, and (C# 12) primary-constructor `ParameterList`. Any `AllowMultiple=false` attribute duplicates across the two partials (CS0579); a primary constructor duplicates (compile error). Breaks a consumer's build under ordinary conditions in a separately-shipped product.

**Files:**
- Modify: `src/Synto.Diagnostics/DiagnosticsGenerator.cs` (:315–318)
- Test: `test/Synto.Diagnostics.Test/DiagnosticsGeneratorTest.cs`

**Interfaces (contract):**
- The generated partial is rebuilt as a clean shell (mirroring `MatchComposer`): `ClassDeclaration(parent.Identifier).WithModifiers(parent.Modifiers).WithTypeParameterList(parent.TypeParameterList).WithConstraintClauses(parent.ConstraintClauses)` + only the new members. **Kept:** identifier, modifiers, type parameters + constraints. **Dropped:** attribute lists, base list, primary-constructor parameter list. (Carrying type parameters here also pre-fixes this generator's slice of #15.)

**Failing-test intent:**
- Driver fact: a partial class carrying `[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]`, a base list, and a C# 12 primary constructor, with one `[Diagnostic]` partial method → the post-generation compilation has **no errors** (no CS0579, no duplicate-ctor), and the rendered generated partial contains none of attribute/base-list/parameter-list text.
- Existing snapshots: expected byte-identical (current goldens have plain classes); any delta must be explained.

**Steps:**
- [ ] Write the failing fact (post-gen compile currently reports CS0579 / duplicate primary ctor).
- [ ] Verify it fails.
- [ ] Implement the clean-shell rebuild.
- [ ] `dotnet test --project test/Synto.Diagnostics.Test/Synto.Diagnostics.Test.csproj` + full `Synto.Test` green.
- [ ] Format clean.
- [ ] `jj commit -m "fix(diagnostics): generated partial is a clean shell — no cloned attributes/base list/primary ctor (#5)"`

---

### Task 6: `ApplySealedAttribute` emits a legal modifier order (#6)

Closes **#6** (high). `sealed` is appended after all existing modifiers, so decorating a quoted `public partial class C` yields `public partial sealed class C` → CS0267 in the consumer's generated file. Ships doubly (public Synto.Core API **and** the injected file-local helper) — both fixed by the one authored source (single-source-of-truth).

**Files:**
- Modify: `src/Synto/Decorations/SealedAttributeExtensions.cs` (:10–15)
- Test: `test/Synto.Test/Templating/DecorationTests.cs` (rendered-output assertion) + a direct unit test on the extension (Synto.Test has `InternalsVisibleTo`)

**Interfaces (contract):**
- `ApplySealedAttribute<T>` behavior: the resulting modifier list is **legal C#** — `sealed` is inserted *before* the `partial` token when one exists (mirroring `ApplyVisibilityAttribute`'s prepend approach), appended otherwise; idempotent when `sealed` already present (existing early-return kept).

**Failing-test intent:**
- Unit: `ApplySealedAttribute` on a `public partial class C` declaration → modifier text `public sealed partial` (assert token order, and that `ParseMemberDeclaration` of the rendered text yields no CS0267).
- Rendered-output: a `[Sealed]`-decorated template over a partial class → the *rendered* generated declaration contains `sealed partial class`. (Full executed end-to-end decoration coverage lands in Task 20 / #23; here the rendered assertion is the focused regression test.)

**Steps:**
- [ ] Write both failing tests.
- [ ] Verify they fail (`partial sealed` today).
- [ ] Implement the insert-before-`partial` fix.
- [ ] Full `Synto.Test` suite green; re-accept any decoration snapshot whose modifier order legitimately changes (semantic re-accept, eyeballed).
- [ ] Format clean.
- [ ] `jj commit -m "fix(decorations): ApplySealedAttribute inserts sealed before partial (CS0267) (#6)"`

---

### Task 7: Normalize the Diagnostics injected-attribute line endings (#21)

Closes **#21** (medium) — deliberately early among the mediums because it is **dirtying the working tree right now** (the two CRLF snapshots). The post-init `DiagnosticsAttribute` raw-string text inherits the generator source file's checkout line endings; Windows re-accepts write CRLF while CI writes LF — a determinism defect, not just test noise.

**Files:**
- Modify: `src/Synto.Diagnostics/DiagnosticsGenerator.cs` (`InjectAttribute`, :43–46)
- Renormalize: `test/Synto.Diagnostics.Test/snapshots/DiagnosticsGeneratorTest.WithBlockScopedNamespaces#Synto.Diagnostics.DiagnosticsAttribute.g.verified.cs` and `...WithFileScopedNamespace#....verified.cs` (the two currently-modified files)
- Test: `test/Synto.Diagnostics.Test/DiagnosticsGeneratorTest.cs`

**Interfaces (contract):**
- Post-init emission is **checkout-independent**: the injected source text contains no `\r` (normalize at the `AddSource` site, e.g. `.Replace("\r\n", "\n")` on the raw string). The two snapshots are re-accepted once in their now-stable LF form and stop flapping.
- New guard fact: run the generator's post-init output and assert the emitted `DiagnosticsAttribute` text contains no `'\r'`. (A repo-wide no-CR sweep across the other generators' post-init output is noted as optional follow-up, not in scope.)

**Steps:**
- [ ] Write the failing no-CR fact (fails on a CRLF checkout).
- [ ] Implement the normalization; re-run `test/Synto.Diagnostics.Test` with `$env:DiffEngine_Disabled='true'`; accept the two renormalized snapshots (this is the one sanctioned touch of those files).
- [ ] `jj status` — tree otherwise clean; full Diagnostics + Synto.Test suites green.
- [ ] Format clean.
- [ ] `jj commit -m "fix(diagnostics): injected-attribute text is EOL-normalized; snapshots stop flapping (#21)"`

---

### Task 8: Element/member mutation of a live target is staged, and assignments are never lifted as values (#8)

Closes **#8** (medium). `IsStagedMutation` only recognizes a mutation whose target is a *bare identifier* resolving to a live symbol. `counts[i] = v;` / `acc.Total = v;` (all roots live) resolve to an indexer/property symbol not in the staged set → the statement is treated as a quoted island and `CollectLiftPoints` lifts the whole assignment as `.ToSyntax()` — generated output receives the assignment's *value* (`5;`) while the mutation runs as a factory-time side effect. Silent nonsense for a plausible accumulator pattern.

**Files:**
- Modify: `src/Synto.SourceGenerator/Templating/Staging/StagedScaffoldBuilder.cs` (`IsStagedMutation` :211–226; `IsPurelyStaged` :248–274)
- Test: `test/Synto.Test/Templating/RunCollectShapesTest.cs`

**Interfaces (contract):**
- `IsStagedMutation`: the mutation *target* is resolved by **walking element/member access down to its root expression** (`counts[i]` → `counts`; `acc.Total` → `acc`); the mutation is staged when the root's symbol is in the staged set.
- `IsPurelyStaged`: **refuses assignment expressions** (and `++`/`--`) as lift candidates — lifting an assignment as a value is never meaningful; such shapes either stage (per the rule above) or surface the existing `SY1014` `UnsupportedStagedShape` bail rather than silently mis-emitting.

**Failing-test intent:**
- Executed run-collect fact: a staged loop accumulating via element access (`totals[c.Ordinal] = …;` style, all roots live) and one via member access (`acc.Total += …;`) → the factory executes and the generated output contains **no bare-value statement** (`5;`) from the assignment; the accumulator value observed after invocation is correct. (Clone the Emit+Invoke tail from `RunCollectShapesTest.MutableAccumulationAcrossIterations_Works`.)

**Steps:**
- [ ] Write the failing executed fact(s).
- [ ] Verify they fail (value-statement in output today).
- [ ] Implement the root-walk + assignment refusal.
- [ ] Full `Synto.Test` green; re-accept any legitimately-changed staged snapshots (semantic re-accept).
- [ ] Format clean.
- [ ] `jj commit -m "fix(staging): element/member mutations of live roots stage correctly; assignments never lift as values (#8)"`

---

### Task 9: #9 is already subsumed by SY1010 — pin the coverage; drop the unreachable SY1030 (#9)

Closes **#9** (medium) **as already-handled**. The issue's premise ("residual `Parameter<T>()` shapes flow verbatim into the factory with no SY diagnostic") is **stale**. Splice `Parameter<T>()` folding is owned by the unquote-axis `StagedParameterFinder`, which runs first and reports the fatal **SY1010** (`_unquoteParameterMissingName`, `TemplateDiagnostics.cs:84–86`) for every non-foldable shape (inline zero-arg / no name, compound initializer, non-constant name). SY1010 bails at `DiscoverStagedRootsAndClassify` (`TemplateFactoryBuilder.cs:273–281`) **before** `EmitSpliceMemberGenerators` (:77), so no residue ever reaches `SpliceMemberGeneratorEmitter` — the originally-planned SY1030 residue scan would be unreachable dead code. Verified empirically (both originally-planned #9 shapes produce SY1010 and no factory). Resolution: **no SY1030, no emitter change** — instead pin the existing SY1010 coverage with characterization tests so the "residue" cannot silently regress.

**Files:**
- Test only: `test/Synto.Test/Templating/SpliceMemberGeneratorTest.cs` (add characterization facts).
- No production change: `SpliceMemberGeneratorEmitter.cs` untouched; no new diagnostic ID registered; `AnalyzerReleases.Unshipped.md` unchanged.

**Interfaces (contract):**
- Characterization: for a `[Splice]` member generator whose body uses a non-foldable `Parameter<T>()` — inline zero-arg `Parameter<int>()`, compound initializer `int n = Parameter<int>() + 1;`, non-constant name `Parameter<int>(nameof(x))` — the generator reports a **located SY1010 error and emits no factory**. The failure is diagnosed, not silent, so #9's leak scenario does not reproduce.
- No new public/diagnostic surface; no snapshot delta.

**Test intent (characterization, not red-first):**
- The behavior is already correct, so these facts are **green on write** — they exist to lock it against regression. Assert: an SY1010 error located at the offending `Parameter<T>()` invocation, and no factory member produced for that generator. Keep one supported-shape control (`var x = Parameter<int>();`) still green.

**Steps:**
- [ ] Add the characterization facts (the non-foldable shapes → SY1010 + no factory; one supported control stays green).
- [ ] Confirm they pass on the current tree (documents #9 as already-diagnosed by SY1010).
- [ ] Full `Synto.Test` green.
- [ ] Format clean.
- [ ] `jj commit -m "test(staging): pin SY1010 coverage of non-foldable Parameter<T>() in [Splice] generators; #9 subsumed, no SY1030 (#9)"`

**Follow-up option (not taken — owner decision):** if SY1010's generic "missing parameter name" message reads too vague on the splice axis, a dedicated splice-specific diagnostic (the original SY1030) can be added later by moving splice-parameter folding off the unquote axis (exclude splice bodies from `StagedParameterFinder`); that touches the `TemplateFactoryBuilder` gate and re-accepts the canonical splice snapshot, so it is deferred as its own task rather than bundled here.

---

### Task 10: Builder-declaration misuse reports located SY diagnostics (#10)

Closes **#10** (medium). Gaps: `[TypeArg]` + `[Quoted]` combined (documented forbidden) silently half-ignored; `[Quoted] params` drops `IsParams` (call shape changes silently); duplicate generated type-parameter names / `TResult` collisions → CS0692; a `private static` `[SyntaxBuilder]` passes registry filtering and fails CS0122; non-partial or interface containers → CS0260/CS0261. All user errors, all surfacing inside generated files today.

**Files:**
- Modify: `src/Synto.SourceGenerator/Templating/Builders/FacadeShape.cs` (`Derive` — extend `annotationError`)
- Modify: `src/Synto.SourceGenerator/Templating/Builders/SyntaxBuilderRegistry.cs` (:27–30 — accessibility filter)
- Modify: `src/Synto.SourceGenerator/Templating/Builders/SyntaxBuilderFacadeGenerator.cs` (report declaration-site diagnostics; container-shape checks)
- Modify: `src/Synto.SourceGenerator/Templating/Diagnostics/TemplateDiagnostics.cs` (+ `AnalyzerReleases.Unshipped.md`)
- Test: `test/Synto.Test/Templating/SyntaxBuilderTest.cs`

**Interfaces (contract):**
- `FacadeShape.Derive` `annotationError` additionally covers: **(a)** `[TypeArg]` + `[Quoted]` on one parameter → error ("never combine…"); **(b)** duplicate facade generic-parameter names, including a user `[TypeArg(Name=…)]` colliding with the fresh `TResult` → error; **(c)** `[Quoted] params` → `FacadeParam.IsParams` is **propagated** for `[Quoted]` exactly as for staged params (facade keeps `params`, `FacadeArgumentBinder`'s existing trailing-run binding applies per-kind); if quoting multiple trailing args proves unsound in `BuilderStep`, the fallback contract is an `annotationError` rejecting the combination — either way, never silent shape change.
- New diagnostic: `SY1031` — *"Invalid [SyntaxBuilder] declaration '{0}': {1}"* (`Synto.Usage`, Error, located at the builder method/container identifier). Reasons: builder not public/internal-accessible from the consumer compilation (doc says "public"); containing type (chain) not `partial`; container is an interface. Reported from the **facade generator's own pipeline** at the declaration site.
- Pipeline shape change (cacheability call-out): `SyntaxBuilderFacadeGenerator`'s transform result widens from `(string HintName, string Source)?` to an **equatable** result carrying optional source + `EquatableArray<DiagnosticInfo>` (mirroring `TemplateGenerationResult`); `RegisterSourceOutput` reports the diagnostics and skips `AddSource` for invalid builders. No symbol/node may ride in the result. (Task 19 then pins this pipeline with tracking names + a cacheability guard.)

**Failing-test intent:**
- One driver fact per misuse class (combined markers; `[Quoted] params` call-shape preserved — post-gen compile of an expanded multi-arg call succeeds; duplicate generic name; `private` builder; non-partial container; interface container): assert the located SY1015/SY1031 diagnostic fires and **no cryptic CS error appears in generated files** (post-gen compile has no CS0692/CS0122/CS0260/CS0261 originating from `*.SyntaxBuilderFacade.g.cs`).

**Steps:**
- [ ] Write the failing facts (post-gen compile errors today, no SY diagnostics).
- [ ] Verify they fail.
- [ ] Implement the `Derive` extensions, the registry/facade-generator checks, the equatable result widening, and SY1031; register the ID.
- [ ] Full `Synto.Test` green; existing facade snapshots byte-identical for valid builders.
- [ ] Format clean.
- [ ] `jj commit -m "fix(builders): declaration misuse reports located SY1015/SY1031 instead of cryptic CS errors in generated files (#10)"`

---

### Task 11: Scope `FacadeCallFinder` to the template; fix `TransplantCarrierUsings` over/under-transplanting (#11, #12)

Closes **#11** and **#12** (medium) — adjacent code in the same pipeline. `FacadeCallFinder` is the only per-template finder that LINQ-scans the whole carrier instead of rebasing on `TemplateScopedWalker`, so foreign child-`[Template]` subtrees leak into the parent's builder pass (double-reported errors, spurious using transplants, polluted `BuilderStagedConsumedNodes`). And `TransplantCarrierUsings` (a) transplants **all** carrier usings whenever **any** builder call exists, and (b) its `StartsWith("Synto", …)` exclusion also eats legitimate namespaces like `Syntonic.Helpers`/`SyntoX`.

**Files:**
- Modify: `src/Synto.SourceGenerator/Templating/Discovery/FacadeCallFinder.cs` (:41 — scope-aware traversal)
- Modify: `src/Synto.SourceGenerator/Templating/Pipeline/TemplateFactoryBuilder.cs` (`ResolveBuilderCalls` :779 — thread `ctx.Scope`; `TransplantCarrierUsings` :385–400)
- Test: `test/Synto.Test/Templating/SyntaxBuilderTest.cs` (or the child-template test file)

**Interfaces (contract):**
- `FacadeCallFinder.FindBuilderCalls(SemanticModel, SyntaxNode body, TemplateScope scope)` — new `scope` parameter; the traversal **skips foreign child-`[Template]` subtrees** exactly as `TemplateScopedWalker`-derived finders do (either convert the finder to a `TemplateScopedWalker` or filter the descendant scan by `scope`).
- `TransplantCarrierUsings`: **(a)** the exclusion becomes exact-or-dotted-prefix: `usingName == "Synto" || usingName.StartsWith("Synto.")` — `Syntonic.*`/`SyntoX.*` transplant normally; **(b)** the builder-call trigger is gated on builder calls that actually carry at least one **staged (verbatim) argument binding** — quoted-only builder calls no longer pull in all carrier usings. (The "collect namespaces the verbatim spine actually references" refinement is explicitly deferred — see Deferred.)

**Failing-test intent:**
- (#11) Carrier with a parent template and a nested child template whose body contains a builder-arg error → the error is reported **once** (against the child), and the parent's factory still generates.
- (#12a) Carrier with `using SyntoX.Helpers;` needed by a live region's verbatim scaffold → the factory compiles (using transplanted).
- (#12b) A template whose only builder call has purely quoted args → the generated factory gains **no** transplanted carrier usings (assert e.g. `using System.Linq;` absent from output).

**Steps:**
- [ ] Write the three failing facts.
- [ ] Verify they fail as described.
- [ ] Implement the scope threading + the two transplant fixes.
- [ ] Full `Synto.Test` green; re-accept snapshots whose using lists legitimately shrink (eyeballed semantic re-accepts).
- [ ] Format clean.
- [ ] `jj commit -m "fix(templating): FacadeCallFinder is template-scoped; carrier-usings transplant no longer over/under-transplants (#11, #12)"`

---

### Task 12: `TryQuote` never fails silently — bodyless templates get a diagnostic (#13)

Closes **#13** (medium). A `Bare` template on a bodyless method (abstract/extern/partial-definition) hits neither the block-body nor expression-body branch; `Expression` and `Error` both stay null and the caller only reports when `error is { }` — no factory, no explanation. The false-with-null-error contract invites future misuse.

**Files:**
- Modify: `src/Synto.SourceGenerator/Templating/Quoting/TemplateSyntaxQuoterInvoker.cs` (:47–64 `VisitMethodLike`; `TryQuote` :20–30)
- Caller check stays at `src/Synto.SourceGenerator/Templating/Pipeline/TemplateFactoryBuilder.cs:930–940` (should need no change once the contract holds)
- Test: `test/Synto.Test/Templating/SimpleTemplateTest.cs`

**Interfaces (contract):**
- **Tightened contract: `TryQuote` returning `false` implies `error != null`.** The both-null fall-through (no body, no expression body) sets `Error = TemplateDiagnostics.BareSourceCannotBeEmpty(...)` (SY1005 — reuse; a dedicated "template must have a body" message string on the same descriptor family is acceptable if the SY1005 text misleads). Document the invariant on `TryQuote`'s doc-comment; optionally assert it (`Debug.Assert`) at the return.

**Failing-test intent:**
- Driver fact: `[Template(typeof(Factory), Options = TemplateOption.Bare)]` on a bodyless partial-definition/extern method → exactly one SY1005-family diagnostic, no generated source, no silence.

**Steps:**
- [ ] Write the failing fact (silent no-output today).
- [ ] Verify it fails.
- [ ] Implement the both-null diagnostic + contract doc.
- [ ] Full `Synto.Test` green.
- [ ] Format clean.
- [ ] `jj commit -m "fix(templating): bodyless template reports SY1005 instead of silent no-output; TryQuote false implies error (#13)"`

---

### Task 13: Implement trivia quoting — `TemplateOption.PreserveTrivia` preserves whitespace and comments (#14)

Closes **#14** (medium, correctness/consequences) — by **implementing** the option, not removing it (owner decision). Today `Visit(SyntaxTriviaList)` always returns an empty `TriviaList()` (`// TODO this isn't really supported`), so `PreserveTrivia` pays the wider token-emission cost while silently preserving nothing. The wiring on both sides of that no-op already exists: `TemplateFactoryBuilder.cs:927` passes `includeTrivia: ctx.Options.HasFlag(TemplateOption.PreserveTrivia)` into `TemplateQuoterFactory.Create`, and `CSharpSyntaxQuoterBase.Visit(SyntaxToken)` (:119–147) already emits the trivia-carrying factory overloads via `Visit(token.LeadingTrivia)`/`Visit(token.TrailingTrivia)` when the flag is set — the *only* gap is the empty `Visit(SyntaxTriviaList)` (implementer: confirm this before coding; the task targets that gap, nothing wider). Scope is deliberately bounded to **whitespace and comments** (whitespace, end-of-line, `//` single-line, `/* */` multi-line); structured trivia (XML doc comments, `#region`/`#endregion`, preprocessor directives, `#nullable`, disabled text) stays deferred — see Deferred.

**Files:**
- Modify: `src/Synto/Quoting/CSharpSyntaxQuoter.cs` (`CSharpSyntaxQuoterBase.Visit(SyntaxTriviaList)` :183–187 — the core no-op; plus whatever small per-trivia quoting helper it needs)
- Keep unchanged: `src/Synto/Templating/Definition/TemplateOption.cs` (`PreserveTrivia = 4` stays public) and the `TemplateFactoryBuilder.cs:927` wiring (already correct) — no injected-surface snapshot delta expected.
- Do NOT sync into: `src/Synto.Bootstrap/CSharpSyntaxQuoter.cs` (:157–160) — the bootstrap copy has no trivia mode at all (its `Visit(SyntaxToken)` has no trivia branch and it never runs with trivia on), and `Visit(SyntaxTriviaList)` is not the per-node-kind logic the SYNC NOTE governs. Leave it inert; a one-line divergence comment is welcome.
- Test: new `test/Synto.Test/Templating/PreserveTriviaTest.cs` (executed round-trip via the Emit+`Assembly.Load`+Invoke tail pattern in `SwitchBuilderTest.cs:242–309`)

**Interfaces (contract):**
- `Visit(SyntaxTriviaList)` quotes each **supported** trivia into the corresponding `SyntaxFactory` call — `SyntaxKind.WhitespaceTrivia` → `Whitespace(text)`, `EndOfLineTrivia` → `EndOfLine(text)`, `SingleLineCommentTrivia`/`MultiLineCommentTrivia` → `Comment(text)` — and combines them via the existing `SyntaxFactoryInvocation(nameof(TriviaList), …)` shape (a private `Visit(SyntaxTrivia)`-shaped helper is fine). So with the flag set, the generated factory *reconstructs* each token's leading/trailing trivia at factory run time.
- **Unsupported (structured/directive) trivia kinds are skipped** — dropped from the emitted list, exactly their observable behavior today — never thrown on. Bounding rule: whitespace + comments in, everything else out.
- **Default path untouched:** `includeTrivia: false` (no flag) keeps hitting the bare token branch — zero delta to any existing snapshot (byte-identical guard below). Staged-island/fixed-segment sub-quoters (`StagedScaffoldBuilder.cs:167`, `StagedRegionEmitter.cs:70`) keep their hard-coded `includeTrivia: false` in this task (bound noted in Deferred).
- Implementer note: the trivia-branch token forms (:119–147) have never executed with *real* trivia (no golden ever set the flag). If the executed fact trips a latent overload/shape bug in that branch for whitespace/comment trivia, fixing it is in scope; anything reachable only via structured trivia is not.
- Cacheability: pure syntax construction inside the quoter, driven only by the template's own tokens — no pipeline-state change.

**Failing-test intent:**
- Executed round-trip: a template with `Options = TemplateOption.PreserveTrivia` whose body carries a `// marker` comment, a `/* block */` comment, and meaningful indentation → compile the generated factory, `Assembly.Load`, invoke it, render the returned node with `ToFullString()` (NOT `NormalizeWhitespace()` — that would clobber the thing under test) and assert the comment texts survive and the whitespace/newline shape is retained. Executed assertion is the arbiter, not (only) a snapshot.
- Guard (no leakage): the same template *without* the flag renders with no comment text, and the full existing snapshot suite stays **byte-identical** (default path untouched).

**Steps:**
- [ ] Write the failing executed fact + the no-flag guard.
- [ ] Verify: flagged fact FAILS today (empty `TriviaList()` strips everything); guard passes.
- [ ] Implement per-kind trivia quoting in `Visit(SyntaxTriviaList)` per contract (whitespace/EOL/comments quoted; structured kinds skipped).
- [ ] Full gate: `dotnet test --project test/Synto.Test/Synto.Test.csproj` + `--project test/Synto.Bootstrap.Test/Synto.Bootstrap.Test.csproj`. Bootstrap/self-host goldens are **expected byte-identical** (the edit lives in the hand-written `CSharpSyntaxQuoterBase`, not the bootstrap-generated half, and self-host quoting never runs with trivia on); if any bootstrap snapshot moves anyway, the delta must be whitespace/comment-quoting-only — re-accept mechanically (`$env:DiffEngine_Disabled='true'`, overwrite `.verified` with `.received`), eyeballed; any other delta is a finding, stop and investigate.
- [ ] Format clean: `dotnet format whitespace --verify-no-changes`.
- [ ] `jj commit -m "feat(quoting): PreserveTrivia quotes whitespace and comments; structured trivia deferred (#14)"`

---

### Task 14: Compose family carries type parameters; validators reject unsupported target/ancestor kinds (#15)

Closes **#15** (medium, systemic across the compose family). Rebuilt partials and ancestors carry no `TypeParameterList`, so a generic target/ancestor gets a *different*, arity-0 type declared beside it — compiles cleanly, factory silently lands on the wrong type. Validation walks stop silently at the first non-`ClassDeclarationSyntax` ancestor (partial `struct`/`record`), after which `WithAncestryFrom`'s hard cast throws `InvalidCastException` → opaque SY0000. (The DiagnosticsGenerator rebuild slice was already fixed in Task 5; hint-name `<`/`>` sanitization landed in Task 2.)

**Files:**
- Modify: `src/Synto/Decorations/ClassDeclarationSyntaxExtensions.cs` (`WithAncestryFrom` :19–27)
- Modify: `src/Synto.SourceGenerator/Templating/Pipeline/TemplateDocumentBuilder.cs` (:34–40 target rebuild)
- Modify: `src/Synto.SourceGenerator/Templating/Pipeline/TemplateValidator.cs` (:38–57 ancestry walk)
- Modify: `src/Synto.SourceGenerator/Matching/Emit/MatchComposer.cs` (:52–56 target rebuild) and `Matching/Pipeline/MatchFactorySourceGenerator.cs` (`ValidateTarget` ancestry)
- Modify: `src/Synto.SourceGenerator/Templating/Diagnostics/TemplateDiagnostics.cs` (+ `AnalyzerReleases.Unshipped.md`)
- Tests: `test/Synto.Test/Templating/SimpleTemplateTest.cs`, `test/Synto.Test/Match/MatchSnapshotTests.cs` (or diagnostics tests)

**Interfaces (contract):**
- **Rebuilds carry generics:** every clean-shell rebuild (target + each ancestor in `WithAncestryFrom`) preserves `Identifier`, `Modifiers`, `TypeParameterList`, and `ConstraintClauses` — a generic partial target/ancestor merges with the user's declaration instead of spawning an arity-0 twin.
- **Validators reject what compose can't honor:** the ancestry walks in `TemplateValidator` and `MatchFactorySourceGenerator.ValidateTarget` no longer stop silently at a non-class ancestor: a partial `struct`/`record`/`interface` ancestor (or target, where not already covered by SY1002) reports the new located diagnostic and bails **before** `WithAncestryFrom` can throw.
- New diagnostic: `SY1032` — *"Template/match target '{0}' has an unsupported ancestor '{1}' ({2}); only (partial) class ancestors are supported"* (`Synto.Usage`, Error, located at the ancestor identifier). Matching reuses the same descriptor via its shared `Diagnostics` access or mirrors it in `MatchDiagnostics` — one message text, implementer picks the registry per existing convention.

**Failing-test intent:**
- Executed/compiled fact: `[Template(typeof(Factory<T>))]`-style generic partial target → post-gen compile has exactly one `Factory<T>` (assert rendered output declares `partial class Factory<T>` and the compilation binds the factory member on the generic type; no arity-0 `Factory` declared).
- Ancestor fact: target nested inside a `partial struct` → SY1032 located at the struct, **no SY0000**.
- Matching sibling: a `[Match]` target on a generic containing class → matcher lands on the generic type (snapshot/compile assertion).

**Steps:**
- [ ] Write the failing facts (arity-0 twin / opaque SY0000 today).
- [ ] Verify they fail.
- [ ] Implement the rebuild + validator contracts; register SY1032.
- [ ] Full `Synto.Test` green; goldens for non-generic targets byte-identical (rebuild is additive-only for arity-0).
- [ ] Format clean.
- [ ] `jj commit -m "fix(compose): rebuilt partials/ancestry carry type parameters; unsupported ancestor kinds report SY1032 (#15)"`

---

### Task 15: Matching — reused statement captures don't emit duplicate locals (#16)

Closes **#16** (medium). Expression captures handle parameter reuse (unique temp + `IsEquivalentTo(topLevel: false)` guard, `MatchNodeWalker.cs:127–147`), but `EmitStatementCapture` (:155–167) never checks `BoundCaptureLocals` — a pattern using the same statement hole twice declares `cap_s` twice and duplicates the record member → non-compiling generated source, no diagnostic. The `Exactly(n)` slice path (`MatchRunAligner.cs:265–275`) has the same hole.

**Files:**
- Modify: `src/Synto.SourceGenerator/Matching/Emit/MatchNodeWalker.cs` (`EmitStatementCapture`)
- Modify: `src/Synto.SourceGenerator/Matching/Emit/MatchRunAligner.cs` (the `Exactly` capture emission)
- Test: `test/Synto.Test/Match/` (snapshot + executed round-trip fact)

**Interfaces (contract):**
- `EmitStatementCapture` (and the `Exactly` path) mirror the expression-capture reuse branch exactly: when `ctx.BoundCaptureLocals` already contains `cap_{name}`, narrow into a **unique temp** (`ctx.NextTmp()`) and require `IsEquivalentTo(cap_{name}, topLevel: false)`; add **no** new record member. First site unchanged. Non-linear statement matching thereby gets the same semantics expressions already have.

**Failing-test intent:**
- Driver fact: a `[Match]` pattern using the same `Stmt` capture parameter twice → the generated matcher **compiles** (no CS0128), the capture record has **one** member for that name, and (executed) a block whose two hole sites hold structurally different statements does **not** match while identical statements do.

**Steps:**
- [ ] Write the failing fact (CS0128 in post-gen compile today).
- [ ] Verify it fails.
- [ ] Implement the reuse branches.
- [ ] Full `Synto.Test` green (Match snapshots for single-use captures byte-identical).
- [ ] Format clean.
- [ ] `jj commit -m "fix(matching): reused statement captures emit equivalence guards, not duplicate locals (#16)"`

---

### Task 16: Matching — variable-run alignment scans its free degree of freedom instead of pinning (#17)

Closes **#17** (medium, pre-release-calibrated). `_var` always absorbs the *entire* remaining slack: with no anchors a `{ a(); x.Some(); b(); }` pattern only matches blocks whose **last** statement is `b()` (under-matching the intended "contains" semantics), and with `anchorEnd` only, fixed elements before the variable are pinned to the block start. Both silent.

**Files:**
- Modify: `src/Synto.SourceGenerator/Matching/Emit/MatchRunAligner.cs` (:100–198 — the anchor-configuration emission)
- Test: `test/Synto.Test/Match/` (executed round-trip facts + snapshot re-accepts)

**Interfaces (contract):**
- **Single-DOF scan:** for any anchor configuration that is not fully bounded, the run has exactly one free degree of freedom (the run offset `_o`, or equivalently the variable length `_var`, given the ≤1-variable rule SY1204 already enforces). The emitted matcher **scans that degree** (a bounded loop over the valid split points, first match wins — deterministic) instead of pinning it. Fully-bounded (both anchors) emission is already correct and stays byte-identical.
- `SY1204` is extended **only** for shapes that genuinely exceed one degree of freedom after this change (none are currently expressible under the ≤1-variable rule; the extension is a guard, not a behavior change).
- **Expected churn:** goldens for under-matching shapes change shape (scan loop instead of a pinned offset) — re-accept after eyeballing; the executed round-trip facts are the semantic arbiter.

**Failing-test intent:**
- Executed fact (no anchors): pattern `{ a(); <Some>; b(); }` against a block `{ a(); x(); b(); c(); }` — should **match** (run is contained), currently does not because `b()` isn't last.
- Executed fact (anchorEnd only): pattern with a fixed lead + variable + anchored tail against a block with extra leading statements — the fixed lead floats to the correct offset.
- Regression guards: fully-bounded facts (`None_*`, existing `Bare_*`) stay green.

**Steps:**
- [ ] Write the two failing executed facts + keep the regression guards.
- [ ] Verify they fail (no match today).
- [ ] Implement the single-DOF scan; extend SY1204's guard.
- [ ] Full `Synto.Test` green; re-accept changed Match goldens (eyeballed).
- [ ] Format clean.
- [ ] `jj commit -m "fix(matching): variable-run alignment scans the free split instead of silently pinning (#17)"`

---

### Task 17: Formatter list-stack symmetry; non-finite float/double literals (#18, #19)

Closes **#18** and **#19** (medium) — two contained `src/Synto` runtime fixes. **(#18)** `CSharpSyntaxRewriter` routes non-separated `SyntaxList<T>` elements through `VisitListElement` too, but only the `SeparatedSyntaxList` overload pushes `_listCount` — an empty-stack `Peek()` throws (`InvalidOperationException` → opaque SY0000 for the whole template), and a non-empty stack peeks the *enclosing* list's count (wrong indent). **(#19)** `SyntaxFactory.Literal(double.NaN).Text` is `"NaN"` — an identifier — so unquoting a non-finite value produces CS0103 in generated code; ships twice (public API + injected file-local helper) from the one authored source.

**Files:**
- Modify: `src/Synto/Formatting/SyntaxFormatter.cs` (add the symmetric `VisitList<TNode>(SyntaxList<TNode>)` override, :60–82 context)
- Modify: `src/Synto/Syntax/LiteralSyntaxExtensions.cs` (`ToSyntax(double)` :71–74, `ToSyntax(float)` :66–69)
- Tests: `test/Synto.Test/` (direct unit tests — `InternalsVisibleTo` covers the internal formatter; `RuntimeTypeExtensionsTests.cs` is the sibling pattern for helper unit tests)

**Interfaces (contract):**
- **(#18)** `SyntaxFormatter` overrides the **non-separated** `VisitList<TNode>(SyntaxList<TNode>)` with the same `_depth++/_listCount.Push(list.Count)/…/Pop()/_depth--` protocol as the separated overload, so `VisitListElement`'s `Peek()` always sees *its own* list's count — fixing both the crash and the wrong-count indent decision (a bare `Count > 0` guard is explicitly not enough).
- **(#19)** `ToSyntax(double)` / `ToSyntax(float)` special-case non-finite values (`NaN`, `PositiveInfinity`, `NegativeInfinity`) to the corresponding **member-access expressions** (`double.NaN`, `float.PositiveInfinity`, …) instead of a numeric literal; finite values unchanged. Single authored source ⇒ the injected `file`-local helper copy is fixed by the same edit (no snapshot changes expected — no golden contains non-finite values).

**Failing-test intent:**
- (#18) Unit: `SyntaxFormatter.Format` over a compilation unit whose invocation argument is an interpolated string with multiple content parts (a non-separated `SyntaxList<InterpolatedStringContentSyntax>` under `_indentOn`) → no throw, and the interpolated contents get no spurious list indentation (pin the rendered text).
- (#19) Unit: `double.NaN.ToSyntax()` renders `double.NaN`; `float.NegativeInfinity.ToSyntax()` renders `float.NegativeInfinity`; parse-check each rendered expression has no diagnostics. Plus one executed template fact unquoting `double.NaN` → post-gen compile clean (no CS0103).

**Steps:**
- [ ] Write the failing unit tests (+ the executed NaN fact).
- [ ] Verify: #18 test throws or mis-indents today; #19 renders bare `NaN`.
- [ ] Implement both contracts.
- [ ] Full `Synto.Test` + `Synto.Bootstrap.Test` green (formatter is shared with bootstrap output paths); existing goldens byte-identical.
- [ ] Format clean.
- [ ] `jj commit -m "fix(runtime): formatter tracks non-separated lists symmetrically; non-finite float/double quote as member access (#18, #19)"`

---

### Task 18: List-seam dispatch keyed on (owner, element type) centrally (#24)

Closes **#24** (medium, maintainability). List steps are keyed on `nodeList[0].Parent`, so *every* `SyntaxList<T>` of a keyed owner (a type's `AttributeLists` as well as its `Members`) dispatches through that owner's steps; each step must re-implement the "is this actually my list" fall-through check (documented as load-bearing in `InterpolationFoldStep.cs:27` and `MemberSegmentStep.cs:29–40`). The default for the next list step written is wrong unless its author rediscovers the invariant.

**Files:**
- Modify: `src/Synto.SourceGenerator/Templating/Quoting/PerNodePipelineQuoter.cs` (:61–78 — central element-type check)
- Modify: `src/Synto.SourceGenerator/Templating/Quoting/PlanAssembler.cs` (`BuildListSteps` — carry element type per step)
- Modify: the list-step interface + `src/Synto.SourceGenerator/Templating/Quoting/ListPipeline/InterpolationFoldStep.cs`, `.../MemberSegmentStep.cs` (delete the now-central self-guards)
- Test: existing suite (byte-identical) + one targeted regression fact

**Interfaces (contract):**
- The list-step contract gains a declared element type — illustrative: `Type ElementType { get; }` on the list-step interface (or the step map value becomes `(Type ElementType, Step)`). `PerNodePipelineQuoter.Visit<TNode>(SyntaxList<TNode>)` composes **only** the steps whose `ElementType == typeof(TNode)` for the owner — the "is this my list" check happens once, centrally; per-step fall-through guards are deleted.
- **Behavior-preserving:** all existing snapshots byte-identical (the central check subsumes the per-step guards). Known residual limitation — an owner with two same-element-type lists, and empty lists never dispatching — is documented at the dispatch site and stays out of scope (no current step needs either).

**Failing-test intent:**
- Regression fact: a template whose quoted type declaration has both `AttributeLists` and a `MemberSegments`-keyed `Members` list → the attribute list round-trips untouched (not routed through the member step). This is today implicitly guarded by the step's self-check; the fact makes the invariant survive the guard's deletion.

**Steps:**
- [ ] Write the regression fact (passes today via self-guards — it is the safety net for the refactor).
- [ ] Implement the element-type keying + delete the per-step guards.
- [ ] Full `Synto.Test` green, snapshots **byte-identical**.
- [ ] Format clean.
- [ ] `jj commit -m "refactor(quoting): list-seam dispatch keyed on (owner, element type) centrally; per-step self-guards deleted (#24)"`

---

### Task 19: Cacheability guard for `SyntaxBuilderFacadeGenerator` (#22)

Closes **#22** (medium, testability). The facades pipeline is the only shipped generator with no `WithTrackingName` and no all-steps cacheability test — safe by construction today (equatable result, especially after Task 10), which is exactly the unverified state the guard exists to pin. A future `Combine`/symbol capture would regress silently.

**Files:**
- Modify: `src/Synto.SourceGenerator/Templating/Builders/SyntaxBuilderFacadeGenerator.cs` (:23–33 — tracking names)
- Create/modify: a tracking-names const holder consistent with `TemplateTrackingNames`/`MatchTrackingNames` convention (e.g. `Templating/Builders/BuilderTrackingNames.cs`)
- Test: `test/Synto.Test/Templating/SyntaxBuilderTest.cs` (or a new `SyntaxBuilderCacheabilityTest.cs`), using the existing `test/Synto.Test/CacheabilityAssert.cs`

**Interfaces (contract):**
- Pipeline steps tracking-named (`BuilderTrackingNames.Transform`, `.Result` — matching the sibling generators' two-step convention).
- New fact cloned from `test/Synto.Bootstrap.Test/CSharpSyntaxQuoterGeneratorTest.cs:29–55` (`Generator_IsIncremental_OnUnrelatedEdit`): run the driver over a `[SyntaxBuilder]` fixture, apply an unrelated edit, re-run, and `CacheabilityAssert.AllStepsCachedOrUnchanged` over **all** tracked steps (per the established all-steps rule).

**Steps:**
- [ ] Write the failing fact (fails: no tracking names ⇒ no tracked steps found / assert configuration).
- [ ] Add the tracking names.
- [ ] Fact green; full `Synto.Test` green (tracking names don't change output).
- [ ] Format clean.
- [ ] `jj commit -m "test(builders): tracking names + all-steps cacheability guard for SyntaxBuilderFacadeGenerator (#22)"`

---

### Task 20: Execute decorations and user builders end-to-end (#23)

Closes **#23** (medium, testability). No test invokes a *generated* decorated factory and asserts the rendered declaration, and no executed proof exists that a user builder's `[Quoted]` parameter receives the argument's quote at factory run time. Issue #6 is exactly the bug class this gap admits. The Emit+`Assembly.Load`+Invoke tail in `SwitchBuilderTest.cs:242–309` is ready to copy.

**Files:**
- Modify: `test/Synto.Test/Templating/DecorationTests.cs` (one executed decoration fact)
- Modify: `test/Synto.Test/Templating/SyntaxBuilderTest.cs` (extend one `VerifyBuilderTemplate` case with the executed tail)

**Interfaces (contract / test intent):**
- **Executed decoration fact:** a template over a `partial class` decorated with a marker set including `[Sealed]` (+ `[Visibility]`) → compile the generated factory, `Assembly.Load`, invoke it, render the returned declaration, and assert the rendered text carries `public sealed partial class …` (the Task 6 fix proven at the consumer-visible layer, not just rendered-string level).
- **Executed user-builder fact:** a user `[SyntaxBuilder]` with a `[Quoted]` parameter → invoke the generated factory and assert the builder received the *quote* of the call argument (the invoked result renders the quoted island, e.g. the cast target type appears in the rendered output).

**Steps:**
- [ ] Write both executed facts (they should pass against the post-Task-6/Task-3 codebase — if either fails, it has found a real bug: fix under its parent issue's contract before proceeding).
- [ ] Full `Synto.Test` green.
- [ ] Format clean.
- [ ] `jj commit -m "test(templating): executed end-to-end coverage for decorations and user [SyntaxBuilder] quoting (#23)"`

---

### Task 21: HELD for owner review — defer the Synto.Core public-surface audit (#20)

**#20 is intentionally HELD for owner sign-off and is NOT implemented in this run.** It is a 1.0 one-way-door decision (which public types Synto.Core freezes vs internalizes) that the repo owner wants to review before it lands. The full audit spec is preserved at `docs/superpowers/plans/drafts/2026-07-04-synto-core-surface-audit.md` for a follow-up implement run. This task performs **no public-surface change and no `src/**` change** — it only records the deferral so the plan can close cleanly.

**Files:**
- Modify (doc only): the Deferred section of this plan (add the #20-held bullet with a pointer to the draft). No `src/**` change; no new test; no snapshot delta.

**Interfaces (contract):**
- `Synto.dll`'s exported types are exactly what Tasks 1–20 leave — unchanged. No accessibility edits, no csproj comment change, no `PublicSurfaceTest` in this run.

**Steps:**
- [ ] Append a bullet to the Deferred section recording the hold: *"#20 Synto.Core public-surface audit — HELD for owner review; spec in `drafts/2026-07-04-synto-core-surface-audit.md`; do not implement here."*
- [ ] Full gate green (tree is behavior-unchanged — build/test/format all pass with only the doc note added).
- [ ] `jj commit -m "docs(plan): hold #20 Synto.Core public-surface audit for owner review (#20)"`

---

## Deferred / out of scope (do NOT implement here)

- **Low findings #25–36 — entirely out of scope.** Explicitly: #25 (builder-staged spine root-renaming), #26 (`ReturnsStatement` from `candidates[0]` — note Task 3 touches the neighboring code; do not fix #26 opportunistically), #27 (SY1018-vs-SY1017 precision), #28 (`WhenArm`/`DefaultArm` execution), #29 (base-quoter fallback hardening), #30 (`BranchPruneIdentifier` allowlist), #31 (grouped edge-input diagnostics sweep), #32 (finder `Dictionary` ordering), #33 (in-transform hot-path churn), #34 (latent maintainability traps), #35 (`SyntoBuilders` LangVersion floor), #36 (grouped test-infra gaps).
- **Structured trivia quoting** (#14 residual): `TemplateOption.PreserveTrivia` is kept and implemented (Task 13) for **whitespace and comments only** (whitespace/end-of-line, `//`, `/* */`). XML doc comments, `#region`/`#endregion`, `#if`/preprocessor directives, `#nullable`, and disabled-text trivia are still dropped from quoted output — structured trivia quotes as nodes, not strings, and needs its own design pass. Also bounded: `PreserveTrivia` applies to the main template quote path only; staged-island/fixed-segment sub-quoters (`StagedScaffoldBuilder.cs:167`, `StagedRegionEmitter.cs:70`) keep `includeTrivia: false` — threading the option through staged regions belongs to the same follow-up.
- **Reference-driven usings transplant** (#12's "better" fix — collect the namespaces the verbatim spine actually references): the prefix fix + staged-arg gating close the reported defects; the reference-driven collector is a follow-up.
- **Repo-wide post-init no-CR sweep** beyond `DiagnosticsGenerator` (#21 optional extension): only the Diagnostics generator's output is normalized + guarded here.
- **`(owner, list identity)` full disambiguation** for same-element-type sibling lists and empty-list dispatch (#24 residual): documented limitation; no current step requires it.
- **KB refresh** (`refresh-kb` skill) after this plan lands — several `docs/kb` path:line anchors will drift (FacadeCallFinder, MatchRunAligner, TemplateOption). Run it as a follow-up, not inside a task.

## Self-Review

- **Coverage / traceability (every in-scope issue → exactly one task):** #1 → Task 3 · #2 → Task 4 · #3 → Task 1 · #4 → Task 2 · #5 → Task 5 · #6 → Task 6 · #7 → Task 3 · #8 → Task 8 · #9 → Task 9 · #10 → Task 10 · #11 → Task 11 · #12 → Task 11 · #13 → Task 12 · #14 → Task 13 · #15 → Task 14 · #16 → Task 15 · #17 → Task 16 · #18 → Task 17 · #19 → Task 17 · #20 → Task 21 · #21 → Task 7 · #22 → Task 19 · #23 → Task 20 · #24 → Task 18. All 24 in-scope issues covered; #25–36 excluded and listed in Deferred. ✓
- **Ordering matches the assessment's "Top 3 do first":** SY0000-net escapes (Tasks 1–2 = #3, #4), facade recognition folding #7 (Task 3 = #1+#7), staged-LINQ un-block (Task 4 = #2), remaining highs (Tasks 5–6 = #5, #6), then mediums with the tree-dirtying #21 first (Task 7). ✓
- **Green-gateable units:** every task ends with the full relevant `dotnet test --project` run(s), `dotnet format whitespace --verify-no-changes`, and a `jj commit`; snapshot re-accepts are called out per task with byte-identical vs semantic-equivalence expectations stated. ✓
- **Cross-task interactions handled:** Task 5 pre-fixes DiagnosticsGenerator's slice of #15 (Task 14 doesn't double-touch); Task 2's hint sanitization precedes Task 14's generic-target support (hints with `<`/`>`); Task 10's pipeline widening precedes Task 19's cacheability pin; Task 6's rendered assertion vs Task 20's executed decoration fact are distinct layers, both traceable to their own issue. New diagnostic IDs (SY1029–SY1032) allocated once, no collisions, each registered in `AnalyzerReleases.Unshipped.md`. ✓
- **Constraints honored:** all semantic work stays in-transform; the only pipeline-value change (Task 10) stays equatable and is explicitly pinned by Task 19; single-source-of-truth respected (Tasks 6, 17 fix authored sources once; Task 13 leaves the injected `TemplateOption` surface untouched and does not fork the trivia fix into the bootstrap copy; Task 21 audits the injection rewriter before internalizing); jj-only VCS; MTP invocation form; the two CRLF snapshots touched only by Task 7. ✓
- **Altitude:** every task states contract/behavior + files + interface (signatures, diagnostic IDs + message strings, snapshot deltas) + failing-test intent; no full method bodies prescribed. ✓

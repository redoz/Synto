# Interpolation Staged-Fold — bake build-time staged strings into literal text Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Each task is an independently green unit (builds + tests pass on its own) ending in a `rtk jj commit` step. Use superpowers:test-driven-development inside every task: write the failing test/snapshot first, then the implementation, then confirm green.

**Goal:** Make the staging-aware quoter honor Synto's staging contract for interpolated strings — when a **bare staged-string** interpolation hole (no alignment clause, no format clause, in a *regular* `$"…"` string) appears in a `[Template]` body, **bake its build-time value into the surrounding literal text** instead of emitting a runtime hole — so `$"Field {i} is not {typeLabel} column."` (with `typeLabel` a staged `Parameter<string>` and `i` a runtime parameter) generates a factory that builds `$"Field {i} is not Boolean column."`. This is a default, automatic **correctness fix** to the staging contract, not an opt-in feature; it adds **no public surface**. The runtime string a consumer ultimately gets is unchanged — only the *generated factory source* changes (the staged value is baked in rather than re-emitted as a hole), which is why existing snapshots that interpolate a staged string legitimately move.

**Architecture:** Three coordinated pieces inside the existing quoter stack. (1) A new injected file-local escaper helper (`InterpolationSyntaxExtensions.ToInterpolatedText`), authored in `src/Synto` and injected by `FileLocalHelpers` exactly like `LiteralSyntaxExtensions.ToSyntax` — it escapes a runtime string for *regular* interpolated-text context (`{`→`{{`, `}`→`}}`, plus quote/backslash for the literal token). (2) A hand-written **list-level** fold on `TemplateSyntaxQuoter` — an override that sees the whole `InterpolatedStringExpressionSyntax` / its `Contents` list (NOT a single `Interpolation`), so it can fuse a foldable hole with its flanking `InterpolatedStringText` runs into one token. This mirrors the quoter's **existing** `Visit<TNode>(SyntaxList<TNode>)` override at `TemplateSyntaxQuoter.cs:51` (a plain virtual override of a generated base method — *not* "suppression"; see Global Constraints). The generated base keeps handling every non-foldable interpolation case. (3) A type-metadata channel threaded into the `TemplateSyntaxQuoter` constructor identifying which staged-root reference nodes are string-typed, so the fold fires *only* for bare string holes. The "is this a string staged root" decision is made during **emission** and produces only equatable generated source — no `ITypeSymbol`/`SemanticModel` enters cached pipeline state.

**Tech Stack:** C# / Roslyn incremental source generators; runtime carriers target `netstandard2.0`; xUnit v3 + Microsoft.Testing.Platform; Verify (`Verify.SourceGenerators`, `Verify.XunitV3`) snapshots; jj (use `rtk jj`).

## Global Constraints

- **Default & automatic; no marker, no public surface.** This is a correctness fix to the staging contract. Do **not** add any attribute, builder, option, or API. Opting out of baking a known build-time value has no use case.
- **v1 scope is bare staged-STRING holes in regular `$"…"` strings only — do not widen.** Fold *only* an interpolation hole whose expression is a string-typed staged root **and** that has **no `AlignmentClause` and no `FormatClause`** **and** whose containing interpolated string is a **regular** `$"…"` (NOT verbatim `$@"…"`, NOT raw `$"""…"""`). Leave UNCHANGED: format/alignment holes (`{x:N2}`, `{x,5}`), non-string staged values (`int`/`bool`/`enum`/`float`/…), genuinely-runtime holes (`{i}`), and any hole in a verbatim/raw interpolated string. When in doubt, do not fold.
- **List-level fold, NOT a per-hole override (load-bearing — a single-hole override cannot reach its sibling text).** The fuse must produce **one** `InterpolatedStringText` token spanning `<literalBefore> + value.ToInterpolatedText() + <literalAfter>`, but those flanking literal runs live in *sibling* `InterpolatedStringTextSyntax` nodes — a `VisitInterpolation(InterpolationSyntax)` override receives only the `{…}` hole and cannot see or merge them. So the fold lives at the **interpolated-string-expression / contents-list level**. Default site: override `VisitInterpolatedStringExpression(InterpolatedStringExpressionSyntax)` and rebuild its `Contents` (walking the list, fusing each foldable hole into its neighboring text). Acceptable alternative: special-case `SyntaxList<InterpolatedStringContentSyntax>` inside the existing `Visit<TNode>(SyntaxList<TNode>)` override (`TemplateSyntaxQuoter.cs:51`). Implementer picks one; both reach all siblings. **Do NOT implement this as a `VisitInterpolation` override** — that is the rejected, non-working site.
- **Plain virtual override, not "suppression."** `TemplateSyntaxQuoter` is a *subclass* of the generated `CSharpSyntaxQuoter` (`TemplateSyntaxQuoter.cs:11`); overriding a generated `Visit…` method here is ordinary virtual dispatch (exactly how `Visit<TNode>(SyntaxList<TNode>)` at `:51` and `Visit(SyntaxNode?)` at `:32` already work). It does **not** "suppress" base-method generation. (The `CSharpSyntaxQuoter.VisitIdentifierName` hand-override at `src/Synto/CSharpSyntaxQuoter.cs:225-240` is a *same-partial-class* suppression — a different mechanism; cite it only as precedent for hand-authoring a visit method, and do not copy its "prevent the Generator from generating this method" comment into the subclass.)
- **Cacheable, emission-time decision (load-bearing).** Build the string-staged-root channel during the generation transform, **adjacent to where the staged roots are already consumed** (around `TemplateFactorySourceGenerator.cs:648/697/784`), as plain locals — exactly like the existing `unquotedReplacements` (`Dictionary<SyntaxNode,…>`) and `trimNodes` (`HashSet<SyntaxNode>`) locals. The channel and the quoter are constructed at emission and never stored. **NEVER** put `ITypeSymbol`/`SemanticModel`/`Compilation`/`SyntaxNode` into a cached/equatable pipeline record. The incremental guards MUST stay green and assert the tracked steps Cached/Unchanged: `test/Synto.Test/Templating/SpliceMemberGeneratorTest.cs:177` (`SpliceMemberGeneratorTemplate_IsIncrementalOnUnrelatedEdit`, via `CacheabilityAssert.AllStepsCachedOrUnchanged` over `[Transform, Result]`, `:193-195`) and `examples/.../ObjectReaderIncrementalTests.cs:10` (`Transform_IsCachedOnUnrelatedEdit`).
- **Fuse to ONE token, don't emit adjacent text nodes.** Fusing the hole's escaped value with its adjacent literal runs into a single `InterpolatedStringText` token (via factory-time string concatenation) is the robust path to byte-identical output. Do not emit two adjacent `InterpolatedStringText` content nodes (Roslyn does not normally produce adjacent text tokens; emitting them risks malformed/odd rendering).
- **No git pre-commit hooks.** Format via `dotnet format whitespace` + CI; do not add hooks.
- **Trivia / formatting.** Generated factories must normalize byte-identically after `rtk dotnet format whitespace --verify-no-changes`.
- **jj-managed repo.** Use `rtk jj` / `rtk dotnet`. Fileset paths containing `#` must be double-quoted.
- **Green gate (every task):** `rtk dotnet build`, `rtk dotnet test`, `rtk dotnet format whitespace --verify-no-changes` for the touched projects. A task is not done until all three are green for what it touched.

## Preflight — working-copy hygiene (do this BEFORE Task 1)

The default workspace may carry pre-existing changes unrelated to this plan that must **never** be bundled into a fold commit: two modified `test/Synto.Diagnostics.Test/snapshots/…DiagnosticsAttribute.g.verified.cs` files, and a stray added/untracked `docs/superpowers/plans/2026-06-28-child-templates-objectreader-getters.md` (a different workstream's artifact). Because `jj commit` snapshots the **entire** working copy (no staging index), a bare commit would sweep these in.

- [ ] Run `rtk jj status`. If those unrelated items are present in the working copy this plan executes against, **park them on a separate change before Task 1** (e.g. `rtk jj split` them out, or move them onto their own `rtk jj new` change) so each task's commit captures only that task's work. Do NOT modify, commit, or delete the child-templates plan file as part of this plan.
- [ ] If `implement-plan` runs this in an isolated jj workspace that did **not** inherit the dirty default-workspace state, `rtk jj status` will be clean and no parking is needed — proceed. The point is to not *depend* on that ambiguity: confirm the tree is clean of the three unrelated items before the first commit.

After preflight, each task's bare `rtk jj commit` safely captures only that task's own files (prior tasks already committed theirs). The Task 2 / Task 4 "do not bundle" notes remain as belt-and-suspenders.

## The contract (pin behaviors, not bodies)

The implementer finalizes exact `SyntaxFactory`/escaper bodies. The binding contract is:

- **Escaper helper (new, injected like the existing helpers):** a `public static` extension authored in `src/Synto`, contract `string ToInterpolatedText(this string value)` — returns the value escaped for placement inside a *regular* `InterpolatedStringText` token's text: `{`→`{{`, `}`→`}}`, and quote/backslash handled so the resulting characters are valid in the emitted regular string-literal token. (Exact method name is the implementer's choice; `ToInterpolatedText` is the default and the name FileLocalHelpers keys injection on.) v1 targets regular interpolated strings only; verbatim/raw are deferred (the fold defers to base for them, so the escaper never sees them).
- **Type-metadata channel (new constructor input to `TemplateSyntaxQuoter`):** identifies which staged-root **reference nodes** are string-typed. **Default minimal form: a `HashSet<SyntaxNode>` of the string staged-root reference nodes** (see Open decision below). Threaded through every `TemplateSyntaxQuoter` constructor call site, defaulting to empty only where no template-body holes can occur.
- **Fold behavior (at the interpolated-string-expression / contents-list level):** for each `InterpolationSyntax` hole in the contents whose `Expression` is in the string-staged-root channel **and** `AlignmentClause is null` **and** `FormatClause is null` **and** whose containing string is regular (not verbatim/raw), emit factory code that fuses the hole's **escaped value** with its adjacent literal runs into a single `InterpolatedStringText` token (factory-time `<literalBefore> + value.ToInterpolatedText() + <literalAfter>`). Every other hole and every other interpolated string defers to the generated base behavior (emitted unchanged).
- **Boundary / adjacency:** a foldable hole may be flanked by other holes (runtime or non-foldable) or sit at the string start/end; it then folds into whatever literal text actually adjoins it (possibly empty). Multiple foldable holes in one string fold independently.

> **Open decision (settle in Task 2, do not pre-bake):** the exact channel shape. The **default is `HashSet<SyntaxNode>`** of string staged-root reference nodes. The one thing the fold needs beyond membership is the **factory-time value accessor** for the hole (e.g. `typeLabel`) so it can emit `<accessor>.ToInterpolatedText()`. If recovering that accessor from the existing `_unquotedReplacements[node]` (`<accessor>.ToSyntax()`) proves fragile, upgrade the channel to a `Dictionary<SyntaxNode, ExpressionSyntax>` (reference node → raw value accessor) — a strictly-internal change. Pick the form that keeps the fold readable; do NOT add type symbols to the channel.

---

## Task 1: Inject the `ToInterpolatedText` escaper helper (no quoter change yet)

Author the interpolation-text escaper in `src/Synto`, wire it into the `FileLocalHelpers` scan-based injection (embedded resource + `HelperEntry`), and prove its escaping semantics with a direct unit test. This task touches **only** the runtime helper surface and its injection registration — the quoter is untouched, so it is a clean standalone green unit. Mirrors how `LiteralSyntaxExtensions.ToSyntax` / `CollectionSyntaxExtensions.BuildList` are authored and injected.

**Files:**
- Create: `src/Synto/InterpolationSyntaxExtensions.cs` — a `public static class InterpolationSyntaxExtensions` with `public static string ToInterpolatedText(this string value)`. Author it as a normal namespaced `public` class in `namespace Synto;` (the injector rewrites `public`→`file` at emit time — see `FileLocalHelpers.PublicToFileRewriter`, `FileLocalHelpers.cs:165-188`). Model the file shape on `src/Synto/LiteralSyntaxExtensions.cs:1-15`.
- Modify: `src/Synto.SourceGenerator/Synto.SourceGenerator.csproj` — add an `<EmbeddedResource>` line mirroring `:94-97`, e.g. `<EmbeddedResource Include="..\Synto\InterpolationSyntaxExtensions.cs" LogicalName="Synto.Helper.InterpolationSyntaxExtensions.cs" />` (the `Synto.Helper.` prefix is what `FileLocalHelpers.ResourcePrefix` consumes, `FileLocalHelpers.cs:43`).
- Modify: `src/Synto.SourceGenerator/Templating/FileLocalHelpers.cs` — add a private `const string InterpolationResource = ResourcePrefix + "InterpolationSyntaxExtensions.cs";` next to `:45-48`, and a new `Entries` element `new HelperEntry(nameof(InterpolationSyntaxExtensions.ToInterpolatedText), Load(InterpolationResource))` mirroring `:56-64` (the scan key is taken via `nameof` so a rename stays in lock-step).
- Modify (test): `test/Synto.Test/HelperContractTests.cs` — this file **already exists** and pins helper contracts directly (against the `SyntoCore::Synto` public-Core reference). Add focused unit `[Fact]`s asserting the escaper's output for the hazard characters and a clean string.

**Interfaces:**
- Produces: `InterpolationSyntaxExtensions.ToInterpolatedText(string)` (runtime helper, injected `file`-local into consumer factories); a new `FileLocalHelpers.Entries` scan key. Task 2's fold emits calls to `ToInterpolatedText`, which triggers this injection.

- [ ] **Step 1: Write the failing escaper unit test**

  In `test/Synto.Test/HelperContractTests.cs` add `[Fact]`s asserting (against the `Synto.InterpolationSyntaxExtensions` symbol referenced via the test's existing `SyntoCore::Synto` reference):
  - `"plain".ToInterpolatedText()` ⇒ `"plain"` (no change).
  - a value containing `{` ⇒ `{{`; a value containing `}` ⇒ `}}`.
  - a value containing `"` and `\` ⇒ each escaped so the characters are valid inside the emitted *regular* string-literal token.
  - a combined value (e.g. `a{b}"c\d`) round-trips to the fully-escaped form.

- [ ] **Step 2: Run it; verify it fails to compile/resolve** (helper not authored yet)

  ```
  rtk dotnet test test/Synto.Test/Synto.Test.csproj --filter "HelperContractTests"
  ```
  Expected: FAIL — `ToInterpolatedText` is undefined.

- [ ] **Step 3: Author the escaper** in `src/Synto/InterpolationSyntaxExtensions.cs` (minimal: ordered `Replace`/`StringBuilder` over the hazard classes; escape backslash first, then quote, then `{`/`}` per regular interpolated-text rules). Add the csproj embedded-resource line and the `FileLocalHelpers` entry.

- [ ] **Step 4: Run the unit test; verify PASS**

  ```
  rtk dotnet test test/Synto.Test/Synto.Test.csproj --filter "HelperContractTests"
  ```
  Expected: PASS.

- [ ] **Step 5: Green-gate**
  ```
  rtk dotnet build
  rtk dotnet test
  rtk dotnet format whitespace --verify-no-changes
  ```
  Note: registering a new `FileLocalHelpers.Entries` key does NOT by itself inject the helper anywhere — injection is scan-triggered (`FileLocalHelpers.cs:56-64`), so no existing factory snapshot moves in this task. If one does, stop — something else changed.

- [ ] **Step 6: Commit** (bare commit is safe after Preflight; captures only this task's files)
  ```
  rtk jj commit -m "feat(synto): inject ToInterpolatedText escaper helper for interpolation folding

  Plan: docs/superpowers/plans/2026-06-28-interpolation-staged-fold.md
  Plan-Tasks: 1"
  ```

---

## Task 2: Type-metadata channel + list-level fold — fold the core bare-string case

Thread the string-staged-root channel into `TemplateSyntaxQuoter` and add the hand-written list-level fold that bakes a bare staged-string hole into contiguous interpolated text. Prove it with the core snapshot test, exercising **both** staged-root kinds.

**Files:**
- Modify: `src/Synto.SourceGenerator/Templating/TemplateSyntaxQuoter.cs` — add the channel field + constructor parameter (default minimal: `HashSet<SyntaxNode>` of string staged-root reference nodes; see Open decision) next to the existing `_unquotedReplacements` field (`:14`) and constructor (`:19-30`); add the **list-level** fold (default: override `VisitInterpolatedStringExpression`, rebuilding `Contents`; alternative: special-case `SyntaxList<InterpolatedStringContentSyntax>` in the existing `Visit<TNode>(SyntaxList<TNode>)` override at `:51`). It is a plain virtual override — add a short comment saying so and pointing at `:51` as the existing precedent. **Do not** author a `VisitInterpolation` override.
- Modify: `src/Synto.SourceGenerator/Templating/TemplateFactorySourceGenerator.cs` — build the string-staged-root channel as a local **adjacent to where staged roots are already consumed** (~`:648/697/784`), then pass it into the **main-body** quoter constructor at `:971` (the `TemplateSyntaxQuoter quoter = new(…)` passed to `TemplateSyntaxQuoterInvoker.TryQuote` at `:979`). Membership rule: include each `StagedParameterRoot` reference node (`StagedRootFinder.cs:62-63`) whose `StagedParameterRoot.Type` (`:59`, an `ITypeSymbol`) has `.SpecialType == SpecialType.System_String`; and each `StagedLocal` reference node (`StagedRootFinder.cs:38`) whose `Unquote<T>` type argument is `System.String` — recovered at emission via `(semanticModel.GetSymbolInfo(StagedLocal.StagedCall).Symbol as IMethodSymbol)?.TypeArguments[0].SpecialType == SpecialType.System_String` (`StagedCall` is the `Unquote<T>(…)` invocation, `StagedRootFinder.cs:29`). Do NOT cache the symbol — only the resulting `HashSet<SyntaxNode>` of nodes leaves this scope. (`SpecialType` is already used in this file, e.g. `:251-266`.)
- Modify: `src/Synto.SourceGenerator/Templating/StagedRegionEmitter.cs` — thread the **same** string-staged-root set into the staged-region quoter sites `:216` (fixed sibling of a staged region) and `:402` (island inside a staged region). **Do NOT default these to empty:** the motivating ObjectReader hole — `throw new InvalidCastException($"Field {i} is not {typeLabel} column.")` — is emitted *inside / as a sibling of* a staged per-column region, so it is quoted by one of these sites, NOT the depth-0 main body. Defaulting empty here would make the fold never fire for the case that motivates the feature (and break Task 4's byte-identical child-templates claim). The builder-island site `TemplateFactorySourceGenerator.cs:898` quotes `[Quoted]` builder arguments (not template-body holes); pass the same set for uniformity (harmless) or an empty set — but it is not the getter-body path.
- Create: `test/Synto.Test/Templating/InterpolationFoldTest.cs` — the durable fold proof. Model the harness on `SimpleTemplateTest` (`:10-71`): copy the metadata-reference set + `CompilationWithSource` + `VerifyTemplate` shape (snapshot `TemplateFactorySourceGenerator` output via `Verify(result).UseDirectory("snapshots")`).
- Create (via accept): `test/Synto.Test/Templating/snapshots/InterpolationFoldTest.*.verified.cs`.

**Interfaces:**
- Consumes: `InterpolationSyntaxExtensions.ToInterpolatedText` (Task 1); `StagedParameterRoot.Type`/`.References`, `StagedLocal.StagedCall`/`.References`.
- Produces: the folding `TemplateSyntaxQuoter`. Tasks 3–4 rely on this mechanism.

- [ ] **Step 1: Write the failing core snapshot tests — one per staged-root kind.** In `InterpolationFoldTest.cs`, author self-contained `[Template]`s (mirror `SimpleTemplateTest`'s in-source consumer style) covering **both** channel-population paths, since they are independently fallible:
  - `Parameter<string>` root: a body `return $"Field {i} is not {label} column.";` where `label` is a `Parameter<string>()` staged root and `i` is a runtime parameter. `[Fact] BareStagedString_ParameterRoot_FoldsIntoLiteralText`.
  - `Unquote<string>` local: the same body where `label` comes from `var label = Unquote<string>(…)` / a `[Unquote] string label`. `[Fact] BareStagedString_UnquoteLocal_FoldsIntoLiteralText`.

  Run:
  ```
  rtk dotnet test test/Synto.Test/Synto.Test.csproj --filter "InterpolationFoldTest"
  ```
  Expected: FAIL — new received snapshots (no `.verified.cs` yet). If instead the run errors at generation time, that is the mechanism not yet existing — proceed to Step 3.

- [ ] **Step 2: Add the channel to the constructor + all template-body call sites** (`TemplateFactorySourceGenerator.cs:971`, `StagedRegionEmitter.cs:216`, `:402`; `:898` uniformly-or-empty) so they compile. Build the real string-staged-root set adjacent to the staged-root consumption (~`:648/697/784`) per **Files** above.

- [ ] **Step 3: Implement the list-level fold** per the contract: override `VisitInterpolatedStringExpression` (or special-case the contents list in `Visit<TNode>(SyntaxList<TNode>)`), and for each hole that is in the channel ∧ has no `AlignmentClause`/`FormatClause` ∧ sits in a regular (non-verbatim, non-raw) string, fuse the escaped value (`<accessor>.ToInterpolatedText()`) with its adjacent literal runs into a single `InterpolatedStringText` token; every other hole/string defers to the generated base. Add the "plain virtual override (see `:51`)" comment.

- [ ] **Step 4: Review the received snapshots, then accept.** For **both** root kinds confirm: (a) the runtime hole `{i}` is preserved; (b) the staged `{label}` is gone — its value fused into one contiguous `InterpolatedStringText` token built with `label.ToInterpolatedText()` + surrounding literal text; (c) the escaper helper (`InterpolationSyntaxExtensions`) is injected into the generated file (scan-triggered). If correct, accept (`.received.cs` → `.verified.cs`, stage).

- [ ] **Step 5: Re-accept the snapshot churn this fold introduces — across ALL projects.** Run `rtk dotnet test`. Any snapshot in **any** project (Synto.Test, the `examples/Synto.Example.ObjectReader` example, etc.) whose template interpolates a staged string now legitimately changes (the value is correctly baked). **Runtime output is unchanged — only the generated source text changes** — so this is not a behavior regression. Review each diff to confirm it is *only* the staged-string fold (runtime holes untouched), then re-accept. Do **NOT** bundle the two pre-existing unrelated `Synto.Diagnostics.Test` `DiagnosticsAttribute.g.verified.cs` edits or the stray child-templates plan file — Preflight parked them; keep them out.

- [ ] **Step 6: Confirm the incremental guards stay green** (the channel must not leak symbols into pipeline state):
  ```
  rtk dotnet test test/Synto.Test/Synto.Test.csproj --filter "SpliceMemberGeneratorTest"
  rtk dotnet test examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Tests/Synto.Example.ObjectReader.Tests.csproj --filter "ObjectReaderIncrementalTests"
  ```
  Expected: PASS (tracked steps Cached/Unchanged). If either fails, a non-equatable value entered cached state — STOP and switch to superpowers:systematic-debugging; the classification must happen at emission, never in cached pipeline data.

- [ ] **Step 7: Green-gate**
  ```
  rtk dotnet build
  rtk dotnet test
  rtk dotnet format whitespace --verify-no-changes
  ```

- [ ] **Step 8: Commit**
  ```
  rtk jj commit -m "fix(templating): bake bare staged-string interpolation holes into literal text

  Plan: docs/superpowers/plans/2026-06-28-interpolation-staged-fold.md
  Plan-Tasks: 2"
  ```

---

## Task 3: Guard + escaping + boundary coverage (non-goals stay unchanged)

Pin the behavior boundaries the spec enumerates plus the two boundaries review surfaced (non-string staged values; verbatim/raw strings). These are the regression fence around Task 2's mechanism; an escaping failure here is fixed in the Task-1 helper (or the Task-2 fold), not by widening scope.

**Files:**
- Modify: `test/Synto.Test/Templating/InterpolationFoldTest.cs` — add one `[Fact]` per case below.
- Create (via accept): the corresponding `test/Synto.Test/Templating/snapshots/InterpolationFoldTest.*.verified.cs`.

**Interfaces:**
- Consumes: the Task-2 folding quoter + Task-1 escaper.
- Produces: regression coverage for the v1 boundary.

- [ ] **Step 1: Write the failing `[Fact]`s** (new received snapshots), covering exactly:
  - **Escaping round-trip** — a staged string whose value contains `{`, `}`, `"`, `\` folds correctly (baked text valid; escapes via `ToInterpolatedText`). `Escaping_HazardCharacters_RoundTrip`.
  - **Runtime hole untouched** — `$"x {i} y"` where `i` is a genuine runtime parameter (not a staged root) stays a hole. `RuntimeHole_IsLeftUntouched`.
  - **Non-string staged untouched (non-goal)** — a *staged* root that is NOT a string (e.g. `Parameter<int>()` / `Unquote<bool>`) in a bare hole stays a hole (it is in `_unquotedReplacements` but absent from the string channel). `NonStringStaged_IsLeftUntouched`.
  - **Format/alignment guard (non-goal)** — a staged-string hole *with* a format clause (`{label:N2}`) AND one with an alignment clause (`{label,5}`) are each left as holes. `StagedString_WithFormatOrAlignment_IsLeftUntouched`.
  - **Verbatim/raw guard (non-goal)** — a bare staged-string hole inside `$@"…"` (and, if practical, `$"""…"""`) is left as a hole (v1 targets regular strings only). `StagedString_InVerbatimOrRawString_IsLeftUntouched`.
  - **Mixed** — `$"a {runtime} b {stagedString} c"` folds only the staged part; the runtime hole and both literal runs survive. `Mixed_FoldsOnlyStagedPart`.
  - **Boundary/adjacency** — a foldable hole at string start/end and a foldable hole flanked by another hole each fold into the adjoining (possibly empty) literal text; two foldable holes fold independently. `Boundary_StartEnd_AndAdjacentHoles_FoldIndependently`.

  Run:
  ```
  rtk dotnet test test/Synto.Test/Synto.Test.csproj --filter "InterpolationFoldTest"
  ```
  Expected: FAIL — new received snapshots.

- [ ] **Step 2: Review each received snapshot.** Confirm the guard cases emit **unchanged** holes (runtime / non-string-staged / format / alignment / verbatim / raw) and the fold cases bake correctly. **Decision:** if the escaping case is wrong (malformed / over- or under-escaped), STOP and switch to superpowers:systematic-debugging — fix the escaper (`src/Synto/InterpolationSyntaxExtensions.cs`, Task 1) and/or the fusing (Task 2). Do **not** address it by narrowing or widening the fold scope. Accept once correct (`.received.cs` → `.verified.cs`, stage).

- [ ] **Step 3: Confirm PASS, then green-gate**
  ```
  rtk dotnet test test/Synto.Test/Synto.Test.csproj --filter "InterpolationFoldTest"
  rtk dotnet build
  rtk dotnet test
  rtk dotnet format whitespace --verify-no-changes
  ```

- [ ] **Step 4: Commit**
  ```
  rtk jj commit -m "test(templating): guard interpolation fold boundaries — escaping, runtime/non-string/format holes, verbatim, mixed, adjacency

  Plan: docs/superpowers/plans/2026-06-28-interpolation-staged-fold.md
  Plan-Tasks: 3"
  ```

---

## Task 4: Sweep remaining churn, sync spec + memory, record the child-templates unblock (final)

Confirm no toolkit-wide snapshot churn remains across **all** projects, mark the fold shipped in the spec, and record (in this plan's spec + memory) that this fold is the **root-cause fix** that unblocks child-templates Task 3 (the ObjectReader getter collapse becomes byte-identical once this lands). Behavior-preserving framing: the fold changes only generated source (runtime output unchanged), so any existing snapshot that interpolates a staged string legitimately changes — not a regression.

> **Scope guard:** do NOT modify or commit the stray child-templates plan file (`docs/superpowers/plans/2026-06-28-child-templates-objectreader-getters.md`) — it is a parked, separate-workstream artifact (handoff: do not bundle it). Record the unblock in *this* plan's spec + memory only; the child-templates workstream resumes from its own `Plan-Tasks` trailers when picked up.

**Files:**
- Modify: `docs/superpowers/specs/2026-06-28-interpolation-staged-fold-design.md` — status line → shipped (v1: bare staged-string holes in regular strings); resolve the two "Open decisions" (`:118-124`): the settled channel form (default `HashSet<SyntaxNode>` or the upgraded map, whichever Task 2 chose) and that the escaper shipped as a new helper file (`InterpolationSyntaxExtensions`). Note the fold lives at the interpolated-string-expression (list) level, and that verbatim/raw strings are an explicit deferred non-goal.
- Modify: `C:\Users\redoz\.claude\projects\C--dev-Synto\memory\MEMORY.md` + a pointer note — one fact: "interpolation staged-fold shipped (bare staged-string holes in regular strings bake into literal text by default); it is the root-cause fix unblocking child-templates Task 3 (its getter collapse is now byte-identical; resume via that plan's `Plan-Tasks` trailers)."

- [ ] **Step 1: Repo-wide churn sweep.** Run the full suite; review that any *remaining* changed snapshots across every project are purely the staged-string fold (per-task gates already caught per-project churn; this is the toolkit-wide confirmation).
  ```
  rtk dotnet test
  ```
  Re-accept only fold-driven diffs. Do **NOT** touch the two pre-existing unrelated `DiagnosticsAttribute.g.verified.cs` working-tree edits or the child-templates plan file.

- [ ] **Step 2: Update the spec** — status → shipped; resolve the two open decisions (`:118-124`) with what Task 2 settled (channel form; escaper as a new `*SyntaxExtensions` helper file; list-level fold; verbatim/raw deferred).

- [ ] **Step 3: Update memory** — add the one fact above plus its `MEMORY.md` pointer line.

- [ ] **Step 4: Final green-gate**
  ```
  rtk dotnet build
  rtk dotnet test
  rtk dotnet format whitespace --verify-no-changes
  ```

- [ ] **Step 5: Commit**
  ```
  rtk jj commit -m "docs(spec): mark interpolation staged-fold shipped; record child-templates unblock

  Plan: docs/superpowers/plans/2026-06-28-interpolation-staged-fold.md
  Plan-Tasks: 4"
  ```

---

## Self-Review

**1. Spec coverage** (`2026-06-28-interpolation-staged-fold-design.md`):
- "Default/automatic, no marker, no public surface; correctness fix" → Global Constraints + Goal. ✓
- "v1 folds ONLY bare staged-string holes (no alignment/format); non-string/runtime/format-alignment left unchanged" → Global Constraints (scope) + Task 2 (predicate) + Task 3 guards (incl. the added non-string-staged and verbatim/raw guards). ✓
- "Mechanism (1): hand-written override using a blessed pattern" → corrected to a **list-level** plain virtual override (`VisitInterpolatedStringExpression` / `Visit<TNode>(SyntaxList)`), anchored to the existing `TemplateSyntaxQuoter.cs:51` precedent, NOT a `VisitInterpolation` override (which cannot reach sibling text) and NOT "suppression" (it's a subclass override). ✓
- "Mechanism (2): type-metadata channel; minimal = `HashSet<SyntaxNode>`; exact form a settle-in-task decision" → Task 2 + Open-decision note. ✓
- "Mechanism (3): build-time fold+fuse into one `InterpolatedStringText` token via injected escaper" → Task 1 (escaper + injection) + Task 2 (fuse to one token). ✓
- "Mechanism (4): adjacent-hole/boundary handling; multiple foldable holes fold independently" → contract + Task 3 boundary case. ✓
- "Cacheability: decision at emission, equatable output only, no `ITypeSymbol`/`SemanticModel` in cached state; guards stay green" → Global Constraints (channel built as locals adjacent to `:648/697/784`) + Task 2 Step 6 (both named guard tests). ✓
- "Testing: core fold; escaping; runtime untouched; format/alignment untouched; mixed" → Task 2 (core, both root kinds) + Task 3 (the rest, plus non-string-staged and verbatim/raw guards). ✓
- "Toolkit-wide churn re-accepted across all projects, runtime output unchanged; don't bundle pre-existing changes" → Task 2 Step 5 + Task 4 Step 1, all-project scope, with Preflight parking the unrelated state. ✓
- "Relationship: root-cause fix unblocking child-templates Task 3 (byte-identical), resume via trailers" → Task 4 (spec + memory only; the stray plan file is left parked). ✓
- "Open decisions: channel form; escaper as new vs. extended helper" → deferred to Task 2 settle / Task 1 default (new helper file), recorded in Task 4. ✓

**2. Contract-first check:** Escaper signature + fold *behavior* + channel *contract* are pinned; **no full method bodies** prescribed. The override *site* (list-level) and constructor *anchors* (`:971`, `:216`, `:402`) are pinned because review proved the per-hole site and the `:898` anchor were wrong — these are correctness constraints, not body prescriptions. The only prescriptive content is commit-message trailers. ✓

**3. Generator-change framing:** Bounded and expected — `TemplateSyntaxQuoter.cs` (list-level fold + channel), the channel-build at `TemplateFactorySourceGenerator.cs` (~`:648/697/784`) threaded into `:971` + the two `StagedRegionEmitter.cs` sites, `FileLocalHelpers.cs` + csproj (helper injection), and the new `src/Synto/InterpolationSyntaxExtensions.cs`. STOP-and-rescope triggers explicit: a non-equatable value tripping the incremental guards (Task 2 Step 6) and malformed escaping (Task 3 Step 2) both route to systematic-debugging within these files — **not** a scope widening (no format/alignment folding, no non-string folding, no verbatim/raw, no public surface). ✓

**4. Workflow compatibility:** Preflight parks the dirty unrelated working-copy state so per-task bare `rtk jj commit`s capture only their own files (jj has no staging index). Every task is a green unit ending in a commit carrying `Plan:` + `Plan-Tasks:` trailers, footer-clean, conventional message. Each task green-gates build + test + `format whitespace --verify-no-changes`. Tasks ordered by dependency (helper → mechanism → guards → docs); each independently reviewable. ✓

**5. Placeholder scan:** No "TBD"/"similar to above"/"handle edge cases". Each task names concrete files (with verified line anchors), commands, test names, and decision points. Conditional paths (channel-form upgrade; escaping failure → debug Task-1 helper; verbatim/raw → defer) are explicit. ✓

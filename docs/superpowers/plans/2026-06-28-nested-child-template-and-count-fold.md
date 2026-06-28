# Nested Child Template + Staged Scalar Count-Fold Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a child `[Template]` live inside its parent `[Template]` carrier, and let a staged scalar like `columns.Count` fold to a literal — so the ObjectReader dog-food lifts the column list once and derives everything from it (no stranded child carrier, no redundant `Parameter<int>()`).

**Architecture:** Two independent generator capabilities in `Synto.SourceGenerator`, each proven first by a focused generator unit test, then exercised by collapsing redundancy in the ObjectReader example. Both example changes must leave the generated reader **byte-identical** (the existing `ObjectReaderSnapshotTests` snapshot does not change).

**Tech Stack:** Roslyn incremental source generators (netstandard2.0), xUnit + Verify snapshot tests, SDK 10 MTP.

## Global Constraints

- **Branch:** experimental feature work — land on an `experimental/*` branch, never `main` (see memory `experimental-features-own-branch`).
- **Byte-identical output:** the generated `ObjectReader` (snapshot `examples/.../snapshots/ObjectReaderSnapshotTests.Generates_Specialized_Reader_For_Person#ObjectReader.g.verified.cs`) MUST be unchanged by Tasks 2 and 4. Factory *signatures* change; emitted reader source does not.
- **Cacheability:** no `ISymbol` / `SemanticModel` / `SyntaxNode` may enter cached pipeline state — all new analysis stays inside the `GenerateTemplate` transform, same as existing finders (see `principles.md`).
- **Test runner:** `dotnet test --project <csproj>` (raw MTP; do not let rtk rewrite args — memory `dotnet-test-mtp-invocation`).
- **Contracts over syntax:** tasks pin behaviors/interfaces; the implementer chooses the exact generator code (memory `plans-pin-contracts-not-syntax`).

---

## File Structure

**Capability 1 — nested child template (Task 1–2)**
- `src/Synto.SourceGenerator/Templating/TemplateFactorySourceGenerator.cs` — when processing a **class-level** `[Template]`, discover method-level `[Template]` members, trim them from the quoted output, and exclude their subtrees from the parent's staging analysis.
- `src/Synto.SourceGenerator/Templating/ChildTemplateFinder.cs` (Create, optional) — finder for method-level `[Template]` members of a type, mirroring `SpliceMemberGeneratorFinder`.
- `test/Synto.Test/Templating/ChildTemplateTest.cs` — add a nested-carrier variant.
- `examples/.../Generator/ReaderTemplate.cs`, `GetterTemplate.cs` (Delete) — move the child in, drop the standalone carrier.

**Capability 2 — staged scalar count-fold (Task 3–4)**
- `src/Synto.SourceGenerator/Templating/TemplateFactorySourceGenerator.cs` (+ `BindingTimeClassifier.cs` / a small fold finder as needed) — fold a live scalar expression rooted at a staged parameter to a literal.
- `test/Synto.Test/Templating/SimpleTemplateTest.cs` (+ snapshot) — count-fold unit test.
- `examples/.../Generator/ReaderTemplate.cs`, `ObjectReaderGenerator.cs` — derive `FieldCount` from `columns.Count`; drop the `int fieldCount` factory parameter and its call-site argument.

---

## Task 1: Nested `[Template]` excluded from its parent's quoting + staging

**Files:**
- Modify: `src/Synto.SourceGenerator/Templating/TemplateFactorySourceGenerator.cs` (the `GenerateTemplate`/`CreateSyntaxFactoryMethod` transform)
- Create (optional): `src/Synto.SourceGenerator/Templating/ChildTemplateFinder.cs`
- Test: `test/Synto.Test/Templating/ChildTemplateTest.cs` (+ new Verify snapshot under `snapshots/`)

**Interfaces:**
- Consumes: existing `trimNodes` set and the `spliceGeneratorNodes` exclusion pattern in `TemplateFactorySourceGenerator`; `ForAttributeWithMetadataName` already emits a factory per method-level `[Template]` independently.
- Produces: a parent class-level `[Template]` whose generated output contains **none** of its nested `[Template]` members, and whose factory signature contains **none** of the nested members' staged parameters.

**Contract (pin this, not the exact code):**
1. A method carrying `[Template(typeof(...))]` and declared **inside** a type carrying class-level `[Template]` is a *child template*. It is discovered by the existing attribute pipeline and generates its own factory (already true — verify, don't rebuild).
2. When processing the **parent** class-level template: every child-template method node is added to `trimNodes` (so it is never quoted into the parent's output), and **every node in the child method's subtree** is added to the same exclusion set used for `spliceGeneratorNodes` — so the child's `Parameter<…>()`, `Member<…>()`, and live `foreach` are **not** seen as the parent's staged parameters / live regions. No child parameter may leak into the parent factory signature.
3. A `[Splice]` member-generator (executed, not quoted) and a child `[Template]` (independently quoted into its own factory) must coexist in the same carrier.

- [ ] **Step 1: Write the failing test** — add `ChildTemplate_NestedInParentCarrier` to `ChildTemplateTest.cs`. Reuse the existing `Reader` carrier but move `TypedGetter<[Splice] TRet>` (and an inert `_e`) **inside** `Reader`, keeping the `[Splice] Getters()` member-generator that invokes `Factory.TypedGetter(...)`. Run generators; Verify the result. Assertions beyond the snapshot:
  - the `Factory.Reader` factory text contains **no** `TypedGetter` identifier and **no** `Parameter<` call;
  - a `Factory.TypedGetter` factory is generated;
  - the `Factory.Reader` factory parameter list contains no `string` parameter sourced from the child's `Parameter<string>()`.

- [ ] **Step 2: Run it, confirm it fails** — `dotnet test --project test/Synto.Test/Synto.Test.csproj --filter "FullyQualifiedName~ChildTemplateTest.ChildTemplate_NestedInParentCarrier"`. Expected: FAIL (today the child is quoted into `Factory.Reader` and/or its params leak).

- [ ] **Step 3: Implement** — in the class-level path of `TemplateFactorySourceGenerator`, discover method-level `[Template]` members of the target type, add each to `trimNodes`, and add their descendant nodes to the staging-exclusion set (the set checked alongside `spliceGeneratorNodes` at every staged-parameter/region/depth-zero-reference consumption point). Keep all work inside the transform.

- [ ] **Step 4: Run it, confirm it passes** — same filter as Step 2. Expected: PASS. Then run the whole `ChildTemplateTest` + `SimpleTemplateTest` classes to confirm no regression in existing child/standalone behavior.

- [ ] **Step 5: Commit** — `feat(templating): a [Template] method nested in a [Template] carrier is a sibling child template (trimmed from parent output + staging)`.

## Task 2: Move ObjectReader's `TypedGetter` into the parent carrier

**Files:**
- Modify: `examples/.../Generator/ReaderTemplate.cs` (add `TypedGetter` to `ObjectReaderTemplate<T>`, beside `TypedGetters()`)
- Delete: `examples/.../Generator/GetterTemplate.cs`
- Test: `examples/.../Tests/ObjectReaderSnapshotTests.cs` (existing snapshot must not change)

**Contract:** `TypedGetter<[Splice] TRet>(int i)` lives inside `ObjectReaderTemplate<T>` and binds the real `_e` (`IEnumerator<T>`). The standalone `GetterTemplate` carrier and its duplicated inert `_e` are gone. Generated reader is byte-identical.

- [ ] **Step 1: Move the method** — relocate `TypedGetter` (verbatim body: `Parameter<EquatableArray<ColumnInfo>>()` / `Parameter<string>()` / `Member<TRet>(_e.Current, c.Name)`) into `ObjectReaderTemplate<T>` directly above/below `TypedGetters()`. Delete `GetterTemplate.cs`. Update the `// ---- cast-less typed getters` comment block to note the child now lives in-carrier.
- [ ] **Step 2: Build the generator** — `dotnet build examples/.../Generator/Synto.Example.ObjectReader.Generator.csproj`. Expected: the carrier compiles (`_e.Current` is `T`; only `_e.Current` is quoted).
- [ ] **Step 3: Run the snapshot test** — `dotnet test --project examples/.../Tests/Synto.Example.ObjectReader.Tests.csproj --filter "FullyQualifiedName~ObjectReaderSnapshotTests"`. Expected: PASS with **no** snapshot diff (byte-identical). If Verify reports a `.received`, the move changed output — investigate, do not accept.
- [ ] **Step 4: Run the example behavior + cacheability tests** — full `Synto.Example.ObjectReader.Tests` project. Expected: PASS.
- [ ] **Step 5: Commit** — `refactor(objectreader): co-locate TypedGetter child template inside ObjectReaderTemplate; delete GetterTemplate carrier`.

## Task 3: Fold a staged scalar member-access to a literal

**Files:**
- Modify: `src/Synto.SourceGenerator/Templating/TemplateFactorySourceGenerator.cs` (+ `BindingTimeClassifier.cs` and/or a small fold finder as the implementer judges)
- Test: `test/Synto.Test/Templating/SimpleTemplateTest.cs` (+ new Verify snapshot)

**Interfaces:**
- Consumes: the staged-root machinery (`StagedRootFinder` / `StagedParameterFinder`) — `columns` from `Parameter<EquatableArray<T>>()` is already a known staged root; built-in literal-type detection (`IsBuiltInLiteralType`) and the `value.ToSyntax()` lift already exist.
- Produces: a live scalar expression rooted at a staged parameter, of a built-in literal type, used in **value position**, lifted via `(<expr>).ToSyntax()` and spliced as a literal — with **no** standalone `Parameter<T>()` required.

**Contract (pin this, not the exact code):**
- Minimum viable scope: a **member-access** whose receiver is a staged-root parameter and whose result type is a built-in literal type (covers `columns.Count` → `int` literal; `EquatableArray<T>.Count` exists at `src/Synto.SourceGenerator/EquatableArray.cs:23`). The whole member-access node is the lift unit (not the bare receiver reference).
- The expression must be live (factory-computable) and **not** consumed by a live control region. Folding emits `(columns.Count).ToSyntax()` keyed at the member-access node.
- Out of scope for now (note in code, do not implement): arbitrary live method calls, multi-hop chains, non-literal result types. Keep it to the `.Count`/`.Length`-shaped scalar member-access on a staged root. If a broader live expression of literal type is trivially covered by the same frontier logic, that's acceptable, but do not chase it.

- [ ] **Step 1: Write the failing test** — add `StagedScalarMemberAccess_FoldsToLiteral` to `SimpleTemplateTest.cs`: a template member `int N() { var xs = Parameter<EquatableArray<int>>(); return xs.Count; }` (mirror the file's existing compilation harness). Verify the generated factory. Assert the factory has **no** parameter sourced from a scalar `Parameter<int>()` and the body lifts `xs.Count` (e.g. emits `.ToSyntax()` over `xs.Count`, not a literal baked at generator-author time).
- [ ] **Step 2: Run it, confirm it fails** — `dotnet test --project test/Synto.Test/Synto.Test.csproj --filter "FullyQualifiedName~SimpleTemplateTest.StagedScalarMemberAccess_FoldsToLiteral"`. Expected: FAIL (today `xs.Count` is not recognized as a foldable staged scalar).
- [ ] **Step 3: Implement** — recognize the staged-scalar member-access frontier and emit the `(<expr>).ToSyntax()` lift, keyed at the member-access node, all inside the transform. Reuse `IsBuiltInLiteralType` + the existing `ToSyntax` lift path.
- [ ] **Step 4: Run it, confirm it passes** — same filter. Then run all of `SimpleTemplateTest` + `ChildTemplateTest` + the diagnostics generator tests to confirm no regression.
- [ ] **Step 5: Commit** — `feat(templating): fold a staged-root scalar member-access (e.g. columns.Count) to a literal`.

## Task 4: Derive ObjectReader `FieldCount` from `columns.Count`

**Files:**
- Modify: `examples/.../Generator/ReaderTemplate.cs` (`FieldCount` getter + the `Factory` partial doc-comment signature at `:188`)
- Modify: `examples/.../Generator/ObjectReaderGenerator.cs:291` (drop the `model.Columns.Count` argument)
- Test: `examples/.../Tests/ObjectReaderSnapshotTests.cs` (snapshot must not change)

**Contract:** `FieldCount` is expressed as `columns.Count` over the already-lifted column list; the `Parameter<int>()` and the `int fieldCount` factory parameter are gone; `ObjectReaderGenerator` calls `Factory.ObjectReaderTemplate(elementType, model.Columns)` (no count arg). Generated reader byte-identical (`FieldCount => <N>;` with the same literal `N`).

- [ ] **Step 1: Rewrite `FieldCount`** — body becomes `var columns = Parameter<EquatableArray<ColumnInfo>>(); return columns.Count;`. Update the `Factory` doc-comment (`:188`) to the new signature `ObjectReaderTemplate(TypeSyntax T, EquatableArray<ColumnInfo> columns)`.
- [ ] **Step 2: Update the call site** — `ObjectReaderGenerator.cs:291` → `Factory.ObjectReaderTemplate(elementType, model.Columns)`. Update the nearby comments that mention the `int fieldCount` / "degenerate" parameter.
- [ ] **Step 3: Build the generator** — `dotnet build` the Generator project. Expected: success; the generated factory now has no `fieldCount` parameter.
- [ ] **Step 4: Run the snapshot + behavior tests** — full `Synto.Example.ObjectReader.Tests`. Expected: PASS, **no** snapshot diff (the emitted `FieldCount` literal is unchanged).
- [ ] **Step 5: Commit** — `refactor(objectreader): derive FieldCount from columns.Count; drop redundant int fieldCount parameter`.

## Task 5: Full green-gate + friction-log capture

**Files:**
- Modify: relevant playbook / friction notes only if the implementer hits a sharp edge worth recording.

- [ ] **Step 1: Build the whole solution** — `dotnet build` at the repo root. Expected: success, no new warnings.
- [ ] **Step 2: Run the full test suite** — `dotnet test` across `Synto.Test`, `Synto.Diagnostics.Test`, and `Synto.Example.ObjectReader.Tests`. Expected: all PASS, no snapshot diffs.
- [ ] **Step 3: Format gate** — `dotnet format whitespace` (scope per memory `no-git-pre-commit-hooks`). Expected: clean.
- [ ] **Step 4: Commit** — only if Step 1–3 produced changes: `chore: green-gate after nested-child-template + count-fold`.

---

## Self-Review

- **Spec coverage:** Capability 1 (nested child) → Tasks 1–2; Capability 2 (count-fold) → Tasks 3–4; integration/green-gate → Task 5. Both user-stated asks (`GetterTemplate` should live in the main template; `Parameter<int>()` should be `columns.Count`) are covered.
- **Type consistency:** `TypedGetter<[Splice] TRet>(int i)`, `Factory.ObjectReaderTemplate(TypeSyntax, EquatableArray<ColumnInfo>)`, `EquatableArray<T>.Count` — names match across tasks and against the real code (`ReaderTemplate.cs`, `ObjectReaderGenerator.cs:291`, `EquatableArray.cs:23`).
- **Risk — staging leak (Task 1):** the load-bearing assertion is that the child's `Parameter<…>()` calls do not become parent factory parameters; Step 1's parameter-list assertion guards it explicitly.
- **Risk — non-identical output (Tasks 2, 4):** guarded by the unchanged `ObjectReaderSnapshotTests` snapshot; a `.received` file is a failure, not an accept.

# Live-template highlight analyzer (SY1035 inline) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Paint the existing SY1035 "live template code" diagnostic *inline in the editor* by adding a
`DiagnosticAnalyzer` that reuses the generator's emit path to produce the identical highlight set.

**Architecture:** Two thin, behavior-preserving seams plus one new analyzer, all in `Synto.Generators`.
Seam A decouples template discovery from the generator-only `GeneratorAttributeSyntaxContext` so an
analyzer can drive it. Seam B adds a `highlightsOnly` early-return to `TemplateFactoryBuilder.Build` that
stops right after the SY1035 taps, skipping the throwaway factory assembly + serialization. The analyzer
runs the shared core with `highlightsOnly: true` and reports the SY1035 diagnostics it collects. This is a
**deliberate workaround**; the proper front-end `Classify` extraction (Option C) is a hard 1.0 gate and is
out of scope here.

**Tech Stack:** Roslyn incremental source generators + analyzers (`netstandard2.0`, Roslyn 5.0 floor); C#;
Verify snapshot tests + `CSharpGeneratorDriver`; Roslyn `Compilation.WithAnalyzers` for analyzer tests
(no new NuGet); jj (Jujutsu) over git; MTP v2 (`dotnet test`).

**Design source:** `docs/superpowers/specs/2026-07-09-live-template-highlight-analyzer-design.md`
(approved 2026-07-09). This plan implements exactly that shape; it does not redesign.

## Global Constraints

- **jj-native, off `main`.** All work lands on bookmark `experimental/live-template-highlight-analyzer`
  (already created, based on `main`, holding the design spec). Never move `main` (memory:
  experimental-features-own-branch). Each task ends with a `jj commit` carrying the trailers below. **This
  plan only DESCRIBES commit steps — the implementer runs them.**
- **No git hooks.** Format via `dotnet format whitespace`; CI/green-gate is the enforcement (memory:
  no-git-pre-commit-hooks).
- **Test invocation (MTP / SDK 10):** raw `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug`.
  NOT via rtk. Filter (if needed) via `--filter-method` / `--filter-class`, NOT `--filter` (memory:
  dotnet-test-mtp-invocation).
- **Cacheability is a hard invariant.** No `Compilation`/`ISymbol`/`SemanticModel`/`SyntaxNode` may enter
  cached generator pipeline state. Seams A/B are transform-internal only. The analyzer is a separate
  component and does not touch the generator's incremental pipeline.
- **Generated OUTPUT is a one-way contract — byte-identical.** `highlightsOnly` defaults `false`; the
  generator path is untouched. Every existing Verify snapshot in `test/Synto.Test` must stay byte-identical
  (no snapshot re-accepts expected in this plan).
- **Failures are diagnostics, not exceptions** on the generator side; the analyzer additionally wraps its
  whole body in try/catch and reports nothing on throw (never crash the editor).
- **Workaround comments are a required deliverable.** Every workaround surface (the `highlightsOnly`
  parameter, the plain-input discovery seam, the analyzer class) carries a comment marking it interim and
  pointing at Option C / the 1.0 gate. Reference:
  `docs/superpowers/specs/2026-07-09-live-template-highlight-analyzer-design.md` §"Known debt / 1.0 gate".

**Commit-message trailer block (append to every task's commit):**

```
Plan: docs/superpowers/plans/2026-07-09-live-template-highlight-analyzer.md
Plan-Tasks: <this task's number>
```

---

## Orientation (read before starting)

- `src/Synto.Generators/Templating/TemplateFactorySourceGenerator.cs` — `GenerateTemplate` (`:74`) is the
  per-template transform: `TemplateInfo.Create(syntaxContext)` (`:99`) → `TemplateValidator.Validate` →
  `TemplateDocumentBuilder.Build(… TemplateFactoryBuilder.Build(…))` (`:104-107`), returning a
  `TemplateGenerationResult(fileName, source, diagnostics)`. `Emit` (`:123`) reports each diagnostic via
  `DiagnosticInfo.ToDiagnostic()` (`:126`).
- `src/Synto.Generators/Templating/Emit/TemplateFactoryBuilder.cs` — `Build(...)` (`:32`), signature
  `(List<DiagnosticInfo> diagnostics, SemanticModel, UsingDirectiveSet additionalUsings, TemplateInfo,
  TemplateOption options, SyntaxKind visibility = PublicKeyword)`. All SY1035 taps fire at/before
  `EmitLiveRegions(ctx)` (`:93`); the dedupe/order/cap merge into `ctx.Diagnostics` lives *inside*
  `EmitLiveRegions` (`:897-903`). `EmitSpliceMemberGenerators` (`:96`) and `QuoteAndAssemble` (`:98`) add
  NO SY1035.
- `src/Synto.Generators/Templating/Diagnostics/TemplateDiagnostics.cs` — `_liveFragment` descriptor
  (`:424`, id `SY1035`, `Info`, category `Synto.Staging`) and `LiveFragment(Location?)` factory (`:431`).
- `TemplateInfo.Create` currently consumes only `SemanticModel`, `TargetNode`, and `Attributes` off the
  `GeneratorAttributeSyntaxContext` — confirm exact fields when implementing Seam A.
- `[Template]`/`[Template<T>]` target `Method | Class | Struct` (`src/Synto.Core/Templating/Definition/
  Surface/TemplateAttribute.cs:10`) — the discovery seam and analyzer gate accept type carriers, not just
  methods.
- No analyzer-test harness exists yet (`test/Synto.Test` tests generators via `CSharpGeneratorDriver`).
  Analyzer tests use Roslyn's built-in `compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(
  new LiveTemplateHighlightAnalyzer())).GetAnalyzerDiagnosticsAsync()` — no new package.

---

## Task 1: Seam A — plain-input `TemplateInfo.Create` overload

**Files:**
- Modify: `src/Synto.Generators/Templating/Discovery/TemplateInfo.cs` (the `Create` factory)
- Modify: `src/Synto.Generators/Templating/TemplateFactorySourceGenerator.cs:99` (delegate to new overload)
- Test: `test/Synto.Test/Templating/*` (existing suite — behavior-preserving guard)

**Interfaces:**
- Produces: `static TemplateInfo? TemplateInfo.Create(SemanticModel semanticModel,
  MemberDeclarationSyntax targetNode, ImmutableArray<AttributeData> attributes, CancellationToken ct)`.
  The existing `Create(GeneratorAttributeSyntaxContext)` becomes a thin wrapper that unpacks
  `syntaxContext.SemanticModel` / `.TargetNode` / `.Attributes` and delegates to it.
- Consumes: nothing new.

- [ ] **Step 1:** Identify exactly which members of `GeneratorAttributeSyntaxContext` `Create` reads today
  (expected: `SemanticModel`, `TargetNode`, `Attributes`). If it reads anything else, that field becomes a
  parameter of the new overload.
- [ ] **Step 2:** Add the plain-input overload; move the body into it; the context overload delegates.
  Add a one-line comment on the plain overload: *"Discovery entry decoupled from
  GeneratorAttributeSyntaxContext so the SY1035 highlight analyzer can drive discovery — interim
  workaround, see spec §Known debt / 1.0 gate (Option C)."*
- [ ] **Step 3:** Build the generators project. Run:
  `dotnet build src/Synto.Generators/Synto.Generators.csproj -c Debug` — expected: succeeds.
- [ ] **Step 4:** Run the full generator suite to prove behavior-preserving (no snapshot drift):
  `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug` — expected: all green, zero snapshot
  changes.
- [ ] **Step 5:** Commit (`jj commit`) with the trailer block, `Plan-Tasks: 1`.

---

## Task 2: Seam B — `highlightsOnly` early-return in `TemplateFactoryBuilder.Build`

**Files:**
- Modify: `src/Synto.Generators/Templating/Emit/TemplateFactoryBuilder.cs:32` (signature) and the body
  around `EmitLiveRegions(ctx)` (`:93-94`)
- Test: `test/Synto.Test/Templating/LiveTemplateHighlightTest.cs` (new unit assertion; file already exists)

**Interfaces:**
- Produces: `Build(..., SyntaxKind visibility = PublicKeyword, bool highlightsOnly = false)`. When
  `highlightsOnly` is `true`, `Build` returns immediately after the `EmitLiveRegions` success check (before
  `EmitSpliceMemberGenerators`/`QuoteAndAssemble`); `ctx.Diagnostics` holds the complete, merged SY1035 set;
  the return value's factory-methods list is empty/`null` (callers in highlight mode ignore it).
- Consumes: nothing new.

- [ ] **Step 1: Write the failing test.** In `LiveTemplateHighlightTest.cs`, add a test that builds a
  `Compilation` for a template containing at least one live fragment (e.g. a depth-zero live hole and a live
  control driver), calls `TemplateFactoryBuilder.Build(diagnostics, semanticModel, <empty UsingDirectiveSet>,
  templateInfo, options, highlightsOnly: true)`, and asserts `diagnostics` filtered to id `SY1035` has the
  SAME span set as the generator produces for that template (reuse an existing fixture's expected SY1035
  spans). Assert the returned factory list is empty/null.
- [ ] **Step 2: Run — expect FAIL** (parameter `highlightsOnly` does not exist / diagnostics empty):
  `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug --filter-method <TestName>`.
- [ ] **Step 3: Implement.** Add the `highlightsOnly` parameter (default `false`). After the existing
  `if (!EmitLiveRegions(ctx)) return null;` / success point at `:93-94`, when `highlightsOnly` is set,
  `return` the current (empty) factory result WITHOUT calling `EmitSpliceMemberGenerators` /
  `QuoteAndAssemble`. Add a comment: *"highlightsOnly: the SY1035 analyzer reuses this pass for inline
  highlights and discards the factory; all SY1035 taps have fired and merged by here (EmitLiveRegions
  :897-903), so we skip the assembly tail. Interim workaround — Option C (front-end Classify) is the 1.0
  fix; see spec §Known debt."*
- [ ] **Step 4: Run — expect PASS** (same command as Step 2).
- [ ] **Step 5: Byte-identical guard.** Run the full suite:
  `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug` — expected: all green, zero snapshot
  changes (default `false` path untouched).
- [ ] **Step 6:** Commit (`jj commit`), `Plan-Tasks: 2`.

---

## Task 3: Shared generate core + SY1035 descriptor exposure

**Files:**
- Modify: `src/Synto.Generators/Templating/TemplateFactorySourceGenerator.cs` (extract core from
  `GenerateTemplate`)
- Modify: `src/Synto.Generators/Templating/Diagnostics/TemplateDiagnostics.cs` (expose descriptor)
- Test: `test/Synto.Test/Templating/*` (behavior-preserving guard)

**Interfaces:**
- Produces:
  - `static TemplateGenerationResult? TemplateFactorySourceGenerator.TryGenerate(SemanticModel semanticModel,
    MemberDeclarationSyntax target, ImmutableArray<AttributeData> attributes, bool highlightsOnly,
    CancellationToken ct)` — the body of today's `GenerateTemplate` (`:74-121`) taking plain inputs. When
    `highlightsOnly` is `true`, it skips `TemplateDocumentBuilder` and calls
    `TemplateFactoryBuilder.Build(..., highlightsOnly: true)` directly (discarding the factory); otherwise
    identical to today. Returns the same `TemplateGenerationResult` (diagnostics always populated). The
    nested-carrier SY1034 check and `TemplateValidator.Validate` gate run in BOTH modes so the analyzer
    highlights exactly the templates the generator would build.
  - `internal static DiagnosticDescriptor TemplateDiagnostics.LiveFragmentDescriptor` (exposes `_liveFragment`).
  - Confirm `DiagnosticInfo` exposes its descriptor id (for filtering by `"SY1035"`); if not, add an
    internal `Id`/`Descriptor` accessor. `ToDiagnostic()` already exists.
- Consumes: `TemplateInfo.Create(SemanticModel, MemberDeclarationSyntax, ImmutableArray<AttributeData>, ct)`
  (Task 1); `TemplateFactoryBuilder.Build(..., highlightsOnly)` (Task 2).

- [ ] **Step 1:** Extract `GenerateTemplate`'s body (`:88-120`) into `TryGenerate(sm, target, attributes,
  highlightsOnly, ct)`. `GenerateTemplate(GeneratorAttributeSyntaxContext)` becomes a wrapper:
  unpack fields, call `TryGenerate(..., highlightsOnly: false, ct)`. Thread `highlightsOnly` into the
  `TemplateDocumentBuilder`-vs-direct-`Build` branch.
- [ ] **Step 2:** Add `internal static DiagnosticDescriptor LiveFragmentDescriptor => _liveFragment;` and,
  if needed, the `DiagnosticInfo` id accessor. Comment the core entry as interim (Option C reference).
- [ ] **Step 3:** Build: `dotnet build src/Synto.Generators/Synto.Generators.csproj -c Debug` — succeeds.
- [ ] **Step 4:** Full suite (behavior-preserving):
  `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug` — all green, zero snapshot changes.
- [ ] **Step 5:** Commit (`jj commit`), `Plan-Tasks: 3`.

---

## Task 4: `LiveTemplateHighlightAnalyzer`

**Files:**
- Create: `src/Synto.Generators/Templating/Diagnostics/LiveTemplateHighlightAnalyzer.cs`
- Test: `test/Synto.Test/Templating/LiveTemplateHighlightAnalyzerTest.cs` (created in Task 5)

**Interfaces:**
- Produces: `[DiagnosticAnalyzer(LanguageNames.CSharp)] public sealed class LiveTemplateHighlightAnalyzer :
  DiagnosticAnalyzer` with `SupportedDiagnostics => ImmutableArray.Create(
  TemplateDiagnostics.LiveFragmentDescriptor)`.
- Consumes: `TemplateFactorySourceGenerator.TryGenerate(..., highlightsOnly: true, ct)` (Task 3);
  `TemplateDiagnostics.LiveFragmentDescriptor` (Task 3); `DiagnosticInfo.ToDiagnostic()`.

- [ ] **Step 1:** Implement `Initialize`: `EnableConcurrentExecution()`;
  `ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None)`; register a symbol/syntax action for
  `[Template]`-bearing declarations. Register on `MethodDeclarationSyntax`, `ClassDeclarationSyntax`,
  `StructDeclarationSyntax` (the three attribute targets), or a `SymbolAction` over
  `Method | NamedType`.
- [ ] **Step 2: Cheap syntactic gate first.** Before any semantic-model work, bail unless the declaration
  carries an attribute whose simple name is `Template`/`TemplateAttribute` (or the generic `Template<...>`).
  Only then obtain the semantic model, resolve the `[Template]`/`[Template<T>]` `AttributeData`, and confirm
  the attribute symbol is Synto's `TemplateAttribute`/`TemplateAttribute<T>` by metadata name.
- [ ] **Step 3:** Call `TryGenerate(semanticModel, decl, attributes, highlightsOnly: true, ct)`. For each
  `DiagnosticInfo` whose id == `SY1035`, `context.ReportDiagnostic(info.ToDiagnostic())`. Ignore all other
  diagnostic ids (errors are the generator's job; the analyzer only paints highlights).
- [ ] **Step 4:** Wrap Steps 2-3 in `try/catch` (catch-all, `CA1031`-suppressed like the generator) →
  report nothing on throw. Honor `ct`.
- [ ] **Step 5:** Class-level XML doc marking this a **workaround**: it runs the whole classification+emit
  pass per live analysis and discards the factory; the front-end `Classify` extraction (Option C) is the
  hard 1.0 fix. Reference the spec.
- [ ] **Step 6:** Build: `dotnet build src/Synto.Generators/Synto.Generators.csproj -c Debug` — succeeds.
- [ ] **Step 7:** Commit (`jj commit`), `Plan-Tasks: 4`.

---

## Task 5: Analyzer tests (parity, location, type-carrier, robustness)

**Files:**
- Create: `test/Synto.Test/Templating/LiveTemplateHighlightAnalyzerTest.cs`

**Interfaces:**
- Consumes: `LiveTemplateHighlightAnalyzer` (Task 4). Uses Roslyn
  `compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new LiveTemplateHighlightAnalyzer()))
  .GetAnalyzerDiagnosticsAsync()`; build the `Compilation` with the same helper the existing generator
  tests use (locate it in `test/Synto.Test`).

- [ ] **Step 1: Parity test (failing first).** For a fixture template with known live fragments, collect
  the generator's SY1035 spans (run the generator via the existing `CSharpGeneratorDriver` harness and
  filter diagnostics to `SY1035`) and the analyzer's SY1035 spans (via `WithAnalyzers`). Assert the two span
  sets are equal. Run — expect FAIL (analyzer not yet wired / no infra):
  `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug --filter-class LiveTemplateHighlightAnalyzerTest`.
- [ ] **Step 2:** Make it pass (analyzer from Task 4). Run — expect PASS.
- [ ] **Step 3: Location coverage.** Add cases asserting exact SY1035 locations for: a depth-zero live hole,
  a maximal purely-staged expression, a live control driver (`foreach`/`if`), and a **type-carrier**
  `[Template]` (class/struct) with a nested sub-template. Run — expect PASS.
- [ ] **Step 4: Robustness.** A template that triggers a staging error (e.g. an unsupported staged control
  shape) yields ZERO analyzer highlights and does NOT throw. Run — expect PASS.
- [ ] **Step 5: Non-template negative.** A method/class without `[Template]` yields no analyzer diagnostics
  and does not bind a semantic model unnecessarily (behavioral: no SY1035). Run — expect PASS.
- [ ] **Step 6:** Full suite green + zero snapshot changes:
  `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug`.
- [ ] **Step 7:** Commit (`jj commit`), `Plan-Tasks: 5`.

---

## Self-review notes

- **Spec coverage:** Seam A → Task 1; Seam B (perf lever) → Task 2; shared core + descriptor exposure →
  Task 3; analyzer (gate, generated-code-off, try/catch, workaround doc) → Task 4; tests incl. parity,
  type-carrier, robustness, byte-identical guard → Task 5. Two-tier error consistency is automatic under
  Option B (the analyzer runs the same `Build`, hitting the same bails) — covered by Task 5 Step 4, no
  dedicated code. Workaround comments → Tasks 1/2/3/4 (required deliverable). 1.0-gate/Option C → out of
  scope, recorded in spec + memory.
- **Type consistency:** `TryGenerate` / `TemplateInfo.Create` plain overloads / `Build(..., highlightsOnly)`
  / `LiveFragmentDescriptor` names are used identically across tasks.
- **No new NuGet:** analyzer tests use built-in `WithAnalyzers`.

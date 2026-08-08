# Synto — codebase quality evaluation (issues triage)

**Date:** 2026-07-10 · **Evaluator:** Claude (Fable 5), `/evaluate` workflow — baseline + triage + 2 reviewer rounds (discovery, cross-pollinated verification) + synthesis
**Scope:** working tree (in-flight switch-expression typed-returns branch, = local `main` tip fd4021e) + full `src/` / `test/` / `examples/` sweep across four quality dimensions plus the principal-engineer consequences lens. All findings survived Round 2 adversarial verification against code; duplicates across reviewers/dimensions merged (see Verification Notes).
**Standards:** `.claude/playbook/standards.md` severities, calibrated per `project-phase.md` (pre-release). Findings tagged `pre-release` are down-rated per that calibration; generated-code-doesn't-compile, escaping exceptions, and one-way doors kept at full severity.
**This file is local only — not synced to GitHub, no issues created anywhere.**

**Baseline:** `dotnet build Synto.slnx --no-restore -c Debug` — 14 projects, 0 errors, 0 warnings. `dotnet test --no-build -c Debug` (MTP v2, xUnit v3 + Verify) — 410 total, 0 failed. (A full compile during the format run surfaces 41 pre-existing analyzer warnings; incremental build does not re-emit them. `TreatWarningsAsErrors` is not set.)

**Headline:** no critical findings. Incremental-cacheability discipline is uniformly clean — zero Compilation/ISymbol/SyntaxNode captured into pipeline state, the all-steps CacheabilityAssert guard enforces it, every perf finding is tuning not a caching breach. The two HIGH findings both sit in (or are exposed by) the uncommitted working tree: C1 (generated ObjectReader no longer self-contained) and C2 (builder overload resolution silently binds the wrong builder). As a change-review of the in-flight branch the verdict would be **NEEDS WORK** — C1+T1 before the branch lands, C2 before `[SyntaxBuilder]` sees any external consumer.

## Quality Scorecard

| Dimension       | Score             | Critical | High | Medium | Low |
|-----------------|-------------------|----------|------|--------|-----|
| Correctness     | Needs improvement | 0        | 2    | 4      | 1   |
| Maintainability | Good enough       | 0        | 0    | 3      | 11  |
| Performance     | Good enough       | 0        | 0    | 3      | 5   |
| Testability     | Good enough       | 0        | 0    | 3      | 3   |
| **Total**       |                   | **0**    | **2**| **13** | **20** |

**Quality Gate: PASS** — (1) hard gate, zero critical correctness: met; (2) Maintainability + Performance at Good enough or better: met; (3) Testability not Broken: met. Caveat: Correctness scores *Needs improvement* on the strength of the two HIGHs, both working-tree-exposed.

## Summary table

| # | Sev | Dimension | Area | Title |
|---|-----|-----------|------|-------|
| C1 | high | correctness | `examples/…/ReaderTemplate.cs:40-212` + `ObjectReaderGenerator.cs:257-269` + snapshot | ✔ RESOLVED (experimental/quote-time-type-qualification) — Generated ObjectReader output no longer self-contained; fails to compile for ImplicitUsings-disabled consumers |
| C2 | high | correctness | `Templating/Builders/FacadeCallFinder.cs:111-118,168-190` + `FacadeArgumentBinder.cs:110-111` | Builder overload resolution silently binds the wrong builder: shared-shape key omits ContainingType, ReturnsStatement, IsParams |
| C3 | medium | correctness | `FacadeCallFinder.cs:138-156` | `NarrowByTypeArgArity` zero-type-arg branch wrong; arity mismatch misreported as "ambiguous" |
| C4 | medium | correctness | `FacadeShape.cs:100-109,288-294` | TypeSyntax-derived returns silently reclassified as fresh-TResult expression facades |
| C5 | medium | correctness | `SyntaxBuilderFacadeGenerator.cs:62-138` | Facade generator transform has no catch-all exception→diagnostic guard |
| C6 | medium | correctness | `StagedScaffoldBuilder.cs:266-279` + `SyntaxFormatter.cs:60-73` | All-build-time-hole interpolated strings on the whole-string staged-lift path emit invalid multiline factory C# |
| C7 | low (pre-release) | correctness/consequences | `InjectedOnly/SyntoBuilders.cs:36-43` | Injected surface requires C# 13 with no guard or documentation |
| M1 | medium | maintainability | `TemplateFactoryBuilder.cs:32-45,103-108` | `highlightsOnly` mode flag threads the SY1035 analyzer through the whole factory-build pass |
| M2 | medium | maintainability | `ValueLift.cs`, `TemplateFactoryBuilder.cs`, `RuntimeConverterFinder.cs` | Consumer-contract helper name `ToSyntax` hand-spelled at six generator sites |
| M3 | medium | maintainability | 6 Synto.Core files | Six Synto.Core files violate the documented folder=namespace contract |
| M4 | low | maintainability | `FacadeCallFinder.cs:126-137,168` | Working-tree editing slip: stacked `<summary>` blocks; ShareBindingShape lost its load-bearing doc |
| M5 | low | maintainability | quoter SYNC NOTEs, `FacadeShape.cs:52` | Dangling doc refs (nonexistent `.claude/rules/maintainability.md`, dissolved `SyntaxBuilderFinder`) |
| M6 | low | maintainability | 6 anchors | Six stale `src\Synto` path refs (should be `src\Synto.Core` post-reorg) |
| M7 | low | maintainability | `TemplateBuildContext.cs:49-50`, `TemplateFactoryBuilder.cs:198-205` | Docs describe superseded behavior (sub-template visibility, SY1034 not-yet-landed) |
| M8 | low | maintainability | 8 anchors | Eight copies of the `while(!set.Add(name)) name+='_'` loop; one TODO contradicts NameAllocator doc |
| M9 | low | maintainability | `SymbolMetadataExtensions.cs:34-43` | `GetNamedBool` is dead (zero call sites; [TypeArg]-migration leftover) |
| M10 | low | maintainability | `FacadeShape.cs:280-302` | `IsExpressionSyntax` is the third byte-identical base-type-walk helper |
| M11 | low (pre-release) | maintainability | ~30 SY* DiagnosticDescriptor pairs | Hand-rolled descriptors are exactly what the in-repo `[Diagnostic]` generator eliminates |
| M12 | low (pre-release) | maintainability | `GetQualifiedNameSyntax` | ✔ RESOLVED (experimental/quote-time-type-qualification) — Live TODO: generic containing types render without type arguments |
| M13 | low | maintainability | ObjectReader generated header | Stale `#pragma warning disable CS1522` — typed getters now switch expressions with throwing default arm |
| M14 | low | maintainability | `FacadeCallFinder.GetTypeArgumentCount` + `FacadeArgumentBinder.GetTypeArguments` | Same two invocation shapes pattern-matched independently; can desync narrowing from binding |
| P1 | medium | performance | `SyntaxBuilderRegistry.cs:24-43`, `DecorationFinder.cs:428-467` | Per-template work scales with the whole compilation, not the template |
| P2 | medium | performance | `BindingTimeClassifier.cs:142-224` | Quadratic GetSymbolInfo volume; `while(changed)` fixpoint re-walks all declarators |
| P3 | medium | performance | `FacadeCallFinder.cs:70-84` | Unconditional GetSymbolInfo per invocation (consumed only inside the cheap-lookup branch) |
| P4 | low | performance | SY1035 analyzer | Re-runs full TryGenerate per declaration per pass (documented debt; improves with P1-P3) |
| P5 | low | performance | UsingDirectiveSet/MergeUsings | Linear IsEquivalentTo scans (tiny sets) |
| P6 | low | performance | `RequiredUsings()` | Re-parses five directives per call, twice per document — cache in static ImmutableArray |
| P7 | low | performance | `SyntaxFormatter` | Per-element `DescendantTrivia().Any()` + ParseLeadingTrivia re-parse (bounded) |
| P8 | low | performance | Bootstrap | Per-iteration `GetTypeByMetadataName` + unindexed member scans (dev-time only, never ships) |
| T1 | medium | testability | `GeneratorHarness.cs:43-47,95-108` | ObjectReader harness never compiles generated output — the gap that let C1 land green |
| T2 | medium | testability | `FacadeCallFinder.cs:138-156` + `SwitchBuilderTest.cs` | In-flight narrowing layer covered only on positive paths |
| T3 | medium | testability | `SwitchBuilderTest.cs:20-31` et al. | 33 test files copy-paste reference plumbing |
| T4 | low (pre-release) | testability | (C6 repro) | No pinning test for C6 — the known latent bug lives only in session memory |
| T5 | low (pre-release) | testability | CacheabilityAssert | Four unlinked per-project copies; use the sanctioned `<Compile Link>` pattern |
| T6 | low (pre-release) | testability | SnapshotOrphanGuard | Matches by simple type name + any method; runs only in Synto.Test — three snapshot projects unprotected |

---

## High

### C1. Generated ObjectReader output no longer self-contained; fails to compile for ImplicitUsings-disabled consumers
**Severity:** high · **Dimension:** correctness
**Location:** `examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Generator/ReaderTemplate.cs:40-212` + `ObjectReaderGenerator.cs:257-269` + `Tests/snapshots/ObjectReaderSnapshotTests.Generates_Specialized_Reader_For_Person#ObjectReader.g.verified.cs:1-11`
The working-tree diff de-qualifies the template body in 24 hunks (`global::System.Collections.Generic.IEnumerator<T>` → `IEnumerator<T>`, likewise `Type`, `DBNull`, `InvalidCastException`, `InvalidOperationException`, `ArgumentNullException`, …). Template body identifiers are quoted **lexically**, and `ObjectReaderGenerator.Emit()` composes the output compilation unit with **zero using directives** (`CompilationUnit().AddMembers(FileScopedNamespaceDeclaration(...))`; header adds only `// <auto-generated/>`, `#nullable enable`, one pragma). The output compiles today only because all five example csprojs set `<ImplicitUsings>enable</ImplicitUsings>` (grep-verified; no Directory.Build.props supplies it globally). Any consumer with implicit usings disabled — the compiler default absent the SDK prop, common for libraries — gets CS0246 from generated code. Flagship dog-food showcase teaching the fragile pattern. Genuine product gap: `[Implements<System.Data.IDataReader>]` decorations resolve **semantically** (base list stays `global::System.Data.IDataReader`) while body names copy **verbatim**; `TransplantCarrierUsings` (TemplateFactoryBuilder.cs:94) carries usings into the *factory method* only, never into the quoted output document. Three reviewers independently converged.
**Direction:** short term, `Emit()` prepends `using System;` / `using System.Collections.Generic;` (or revert to `global::`-qualified names) and re-accept the snapshot (semantic-equivalence rule applies). Product fix per the dog-food rule: qualify body identifiers semantically at quote time (the transform holds the SemanticModel), or expose the template file's using set from the factory via the existing `UsingDirectiveSet` machinery. Pair with T1 so the class of defect cannot land green again.
**✔ Resolved (2026-07-10, `experimental/quote-time-type-qualification`):** took the product fix — the quoter now emits every type/namespace-bound simple name as `global::`-rooted at quote time (`QualificationFinder` + `QualifyStep`, `SemanticModel`-free/cacheability-preserving), qualifying concrete type arguments too. The interim hardcoded usings were removed from `ObjectReaderGenerator.Emit()`; the ObjectReader snapshot re-accepts to `global::…IEnumerator<global::Person>` (qualified outer + preserved inner splice) and compiles under the `Generated_Reader_Compiles_Without_ImplicitUsings` gate. Deferred pre-1.0 edge: a splice nested in a *generic* qualifier `Left` (`Outer<[Splice] X>.Inner`) — documented in `QualifyStep`.

### C2. Builder overload resolution silently binds the wrong builder: shared-shape key omits ContainingType, ReturnsStatement, and IsParams
**Severity:** high · **Dimension:** correctness *(promoted from two convergent Round-1 mediums — same root, silent-wrong-output endpoints)*
**Location:** `src/Synto.Generators/Templating/Builders/FacadeCallFinder.cs:111-118,168-190` + `FacadeArgumentBinder.cs:110-111`
`ShareBindingShape` declares same-name overloads interchangeable and `Bind()` consumes `resolved[0]` as representative, but the key covers only FreshReturnTypeParam + GenericParameters.Count + parameter-kind sequence. Three silent-wrong-output axes: (a) **ContainingType** — two same-name same-shape builders in different classes (user `Return([Quoted] ExpressionSyntax)` alongside injected `SyntoBuilders.Return`; Member/Switch/Case/Arm/Return/Throw are common verbs) bind whichever sorts first alphabetically in the registry — deterministic but arbitrary, emitting the wrong builder's call with no diagnostic; (b) **ReturnsStatement** — same-key overloads returning StatementSyntax-derived vs. other concrete nodes stamp `resolved[0]`'s flag onto the BuilderCall, which decides statement-position splicing, so placement follows discovery order; (c) **IsParams** — a params/non-params pair with identical kind sequences diverges in the binder's trailing-argument collection. The "C# compiler resolves the real one" escape hatch repairs none of these. The shipped Switch pair is safe only by accident of the fresh-TResult flag. `[SyntaxBuilder]` is a shipped user-facing surface; every silent-wrong-output path undercuts the core trust promise, and the fix is cheap now with no contract change.
**Direction:** fold ContainingType, ReturnsStatement, and per-parameter IsParams into the shared-shape key so any overload set that would bind, place, or collect differently is rejected as SY1018 (or a dedicated cross-type collision diagnostic); optionally disambiguate cross-type collisions by the call's syntactic qualifier before diagnosing.

---

## Medium — Correctness

### C3. `NarrowByTypeArgArity` zero-type-arg branch wrong; arity mismatch misreported as "ambiguous"
**Severity:** medium · **Dimension:** correctness
**Location:** `src/Synto.Generators/Templating/Builders/FacadeCallFinder.cs:138-156`
The zero-arg branch tests `!shape.FreshReturnTypeParam` where the invariant holds for ANY generic facade ([TypeArg] parameters are never inferable either — the facade emits no value slot for them). A `{ F(string), F([TypeArg, ReturnType] t) }` set survives narrowing on a legitimate `F("x")` call and emits spurious SY1018. Also: a wrong-arity call falls back to the full candidate list (`return narrowed.Count > 0 ? narrowed : candidates;`) and reads as "ambiguous" rather than arity mismatch; underivable candidates are silently skipped, diverging undocumented from ShareBindingShape's treat-as-conflict rule. Latent for user builders; worst case is a spurious/misleading diagnostic, not wrong output.
**Direction:** uniform predicate `shape.GenericParameters.Count == callTypeArgCount` on both branches; distinct wording for the no-arity-match fallback.

### C4. TypeSyntax-derived returns silently reclassified as fresh-TResult expression facades
**Severity:** medium · **Dimension:** correctness
**Location:** `src/Synto.Generators/Templating/Builders/FacadeShape.cs:100-109,288-294`
The new is-a bridge is asymmetric: `returnsExpression` uses the base-chain-walking `IsExpressionSyntax` while `returnsType` exact-matches only bare `TypeSyntax`. Since TypeSyntax derives from ExpressionSyntax, every TypeSyntax-DERIVED return (NameSyntax, QualifiedNameSyntax, GenericNameSyntax, PredefinedTypeSyntax, …) now gains an uninferable leading TResult type parameter — contradicting both the in-file "TypeSyntax … is excluded here" comment and the documented concrete-verbatim facade promise, and changing user builders' call shape. No in-tree builder hits it (operator builders shielded by fixed `Returns`).
**Direction:** add an `IsTypeSyntax` base-chain twin, compute `!IsTypeSyntax(rt) && IsExpressionSyntax(rt)`, decide the intended handling deliberately, and pin with a facade-shape test returning a NameSyntax-derived node.

### C5. `SyntaxBuilderFacadeGenerator` transform has no catch-all exception→diagnostic guard
**Severity:** medium · **Dimension:** correctness
**Location:** `src/Synto.Generators/…/SyntaxBuilderFacadeGenerator.cs:62-138`
`Build()` performs symbol walking, `FacadeShape.Derive`, `ValidateDeclaration` (calls `reference.GetSyntax()`), and facade text synthesis with no try/catch; only the output-stage `AddSource` catches (ArgumentException). TemplateFactorySourceGenerator and MatchFactorySourceGenerator both wrap their whole transform and convert to `Diagnostics.InternalError` — the established "failure is a diagnostic, never an exception" convention this generator misses. An unexpected exception on a mid-keystroke declaration crashes host analysis.
**Direction:** wrap Build's body in the same catch-all → `FacadeGenerationResult(..., InternalError(ex))` pattern.

### C6. All-build-time-hole interpolated strings on the whole-string staged-lift path emit invalid multiline factory C#
**Severity:** medium · **Dimension:** correctness · *(location CORRECTED from the Round-1 InterpolationFold.cs claim — see Verification Notes)*
**Location:** `src/Synto.Generators/…/StagedScaffoldBuilder.cs:266-279` + `SyntaxFormatter.cs:60-73`
`CollectLiftPoints` lifts any maximal purely-staged expression verbatim as `(expr).ToSyntax()` with no `InterpolatedStringExpressionSyntax` guard, so `$"item {list.Count}"` (every hole staged, non-root) is embedded verbatim in factory source; `SyntaxFormatter.VisitListElement` then stamps `\n`+indent leading trivia onto every element of any multi-element list inside an invocation — including the interpolated string's Contents list — splitting a regular `$"..."` across physical lines: invalid C# that fails post-generation compilation. Matches the memory-tracked latent bug witnessed 2026-07-05; the fold/per-hole channels are proven safe by InterpolationFoldTest snapshots. No repro test exists (see T4).
**Direction:** pin with a repro test, then exclude InterpolatedStringExpressionSyntax from whole-node lifts (descend to lift holes individually) or teach SyntaxFormatter never to inject newline trivia inside interpolated-string Contents.

---

## Low — Correctness

### C7. Injected surface requires C# 13 with no guard or documentation
**Severity:** low (pre-release, PE) · **Dimension:** correctness/consequences
**Location:** `src/Synto.Core/Templating/Builders/InjectedOnly/SyntoBuilders.cs:36-43`
Injected consumer source uses `params IEnumerable<T>` (C# 13) and `file`-scoped helpers (C# 11); masked in-repo by `Directory.Build.props` LangVersion=preview, but a real netstandard2.0 generator project defaults to 7.3 and hits raw CS86xx inside injected code. Effectively part of the package contract.
**Direction:** document the LangVersion floor; consider SurfaceInjectionGenerator checking `LanguageVersion` and reporting a clear SY diagnostic.

---

## Maintainability

### M1. `highlightsOnly` mode flag threads the SY1035 analyzer through the whole factory-build pass
**Severity:** medium · **Location:** `TemplateFactoryBuilder.cs:32-45,103-108`
Mid-sequence early return makes the ~20-step build conditional on a mode flag. Self-documented interim debt; Option C (front-end Classify extraction) is the tracked hard pre-1.0 gate. **Action:** none new — but refuse any SECOND mode flag on Build/TryGenerate until Option C lands.

### M2. Consumer-contract helper name `ToSyntax` hand-spelled at six generator sites
**Severity:** medium · **Location:** `ValueLift.cs:89,122`; `TemplateFactoryBuilder.cs:522,543,590`; `RuntimeConverterFinder.cs:68`
A centralized `nameof`-based constant + invocation factory already exist (`StagedHelperCallFactory.ToSyntaxMethod`/`ToSyntaxCall`). Discovery (`GetMembers("ToSyntax")`) and emitted-call names can drift apart with no compiler check. **Direction:** route through StagedHelperCallFactory / `nameof(LiteralSyntaxExtensions.ToSyntax)`; Bootstrap's copy is blessed-duplicate, leave it.

### M3. Six Synto.Core files violate the documented folder=namespace contract
**Severity:** medium · **Location:** `Quoting/CSharpSyntaxQuoter.cs:9`, `Emit/UsingDirectiveSet.cs:9`, `Emit/GeneratedSourceFilenameExtensions.cs:6`, `Symbols/SymbolExtensions.cs:6`, `Syntax/SyntaxListExtensions.cs:5`, `Decorations/ClassDeclarationSyntaxExtensions.cs:6`
All declare bare `namespace Synto` outside both documented exceptions, while `Formatting/SyntaxFormatter.cs` follows the rule. Half-and-half defeats the navigation contract; pre-1.0 with all InternalsVisibleTo consumers in-repo is the cheap alignment moment. **Direction:** move to folder namespaces (CSharpSyntaxQuoterBase couples to the Bootstrap-emitted partial — change both sides together) or document a third exception.

### Low (compact)
- **M4** — Editing slip in working tree: two stacked `<summary>` blocks on `NarrowByTypeArgArity` (`FacadeCallFinder.cs:126-137`); ShareBindingShape (line 168) lost its doc, including the load-bearing underivable-is-a-conflict invariant. Move the first summary back before committing.
- **M5** — Dangling doc refs: both quoter SYNC NOTEs point at nonexistent `.claude/rules/maintainability.md M4`; `FacadeShape.cs:52` crefs dissolved type `SyntaxBuilderFinder`.
- **M6** — Six stale `src\Synto` path references (should be `src\Synto.Core` since the reorg): `SurfaceInjectionGenerator.cs:20`, `FileLocalHelpers.cs:15`, both csproj comments, two EmitHelpers files.
- **M7** — `TemplateBuildContext.cs:49-50` documents sub-template visibility as Private; code deliberately passes Internal (`TemplateFactoryBuilder.cs:205`). `TemplateFactoryBuilder.cs:198-199` describes SY1034 as not-yet-landed; it shipped.
- **M8** — Eight copies of the `while (!set.Add(name)) name += '_';` loop (TemplateFactoryBuilder ×7, DecorationFinder ×1), one carrying a TODO that directly contradicts NameAllocator's inline-by-design class doc. Decide once.
- **M9** — `GetNamedBool` (`SymbolMetadataExtensions.cs:34-43`) is dead — zero call sites; tracked leftover from the [TypeArg] migration. Delete.
- **M10** — `IsExpressionSyntax` is the third byte-identical base-type-walk helper in `FacadeShape.cs:280-302`. Collapse to one `DerivesFrom(type, fqn)`.
- **M11** — *(pre-release)* ~30 hand-rolled SY* DiagnosticDescriptor pairs are exactly what the in-repo Synto.Diagnostics `[Diagnostic]` generator eliminates; dogfooding would also validate the product boundary.
- **M12** — *(pre-release)* `GetQualifiedNameSyntax` live TODO: generic containing types render without type arguments; currently safe on namespace chains, undocumented beyond the TODO. **✔ Resolved (2026-07-10):** `GetQualifiedNameSyntax` was dead (no callers) and is deleted; quote-time qualification renders generics/nested types via `SymbolDisplayFormat.FullyQualifiedFormat`.
- **M13** — Stale `#pragma warning disable CS1522` in the ObjectReader generated header: typed getters are now switch expressions with an always-present throwing default arm; CS1522 no longer applies. Drop + re-accept snapshot.
- **M14** — `GetTypeArgumentCount` (FacadeCallFinder) and `FacadeArgumentBinder.GetTypeArguments` pattern-match the same two invocation shapes independently; a shape added to one silently desynchronizes narrowing from binding. Share one helper.

---

## Performance — *(all tagged pre-release; cacheability intact everywhere)*

### P1. Per-template work scales with the whole compilation, not the template
**Severity:** medium · **Location:** `SyntaxBuilderRegistry.cs:24-43` (eager from FacadeCallFinder ctor :50), `DecorationFinder.cs:428-467`
FacadeCallFinder's constructor eagerly walks every assembly type (nested included), `GetMembers()` each, attribute-checks every static method — before any invocation is seen, so a zero-facade-call template pays the full walk; the determinism sort recomputes `ToDisplayString` in the comparator; DecorationFinder re-walks `EnumerateAssemblyTypes` per decoration attribute, up to twice. All inside the FAWMN transform per keystroke, doubled by the SY1035 analyzer. `ValueLift._runtimeClasses ??=` one field away shows the wanted lazy pattern. **Direction:** lazy/per-Compilation-cached registry (ConditionalWeakTable is safe — nothing outlives the transform); share one assembly enumeration across the three finders; precompute sort keys. Bundle with Option C.

### P2. `BindingTimeClassifier` performs quadratic GetSymbolInfo volume
**Severity:** medium · **Location:** `BindingTimeClassifier.cs:142-155,176-197,199-224`
`ClassifyNodes` calls `ReferencesStaged` for EVERY expression node; `ReferencesStaged` re-binds every descendant identifier — an identifier at depth d re-binds once per ancestor expression. `PropagateStaging`'s `while(changed)` fixpoint re-walks all declarators per round. Dominant per-template cost; class-extent carriers (~200-line ObjectReader) make n non-trivial. **Direction:** bind each identifier once into a liveness map, answer from precomputed per-node liveness, drive the fixpoint from a def-use worklist. Pure refactor; snapshots stay byte-identical.

### P3. Unconditional GetSymbolInfo per invocation
**Severity:** medium · **Location:** `FacadeCallFinder.cs:70-84`
`boundSymbol` computed at :72 for every invocation but consumed only inside the branch guarded by the cheap `_buildersByName` lookup. Every non-builder invocation pays a semantic bind for nothing, per keystroke, doubled by SY1035. Behavior-identical two-line reorder; the file is already in the working-tree diff.

### Low
- **P4** — SY1035 analyzer re-runs full TryGenerate per declaration per pass (documented debt; mitigations verified: syntactic gate, catch-all degrade; improves automatically with P1-P3).
- **P5** — UsingDirectiveSet/MergeUsings linear IsEquivalentTo scans (tiny sets).
- **P6** — `RequiredUsings()` re-parses five directives per call, twice per document — cache in static ImmutableArray.
- **P7** — SyntaxFormatter per-element `DescendantTrivia().Any()` + ParseLeadingTrivia re-parse (once per document, bounded).
- **P8** — Bootstrap per-iteration `GetTypeByMetadataName` + unindexed member scans (~240 iterations, dev-time only, never ships).

---

## Testability

### T1. ObjectReader GeneratorHarness never compiles generated output — the gap that let C1 land green
**Severity:** medium · **Location:** `GeneratorHarness.cs:43-47,95-108`
`Verify()`/`Run()` call `driver.RunGenerators(compilation)` and only snapshot/join text; no `RunGeneratorsAndUpdateCompilation` + `GetDiagnostics()` gate exists in the example suite (SwitchBuilderTest.cs:76-88 has exactly this gate). Crucially, the harness compilation is built from TRUSTED_PLATFORM_ASSEMBLIES with no MSBuild GlobalUsings — an implicit-usings-FREE environment — so one compile assertion would have failed the working-tree change immediately with CS0246, exactly modeling the real at-risk consumer. **Direction:** add the post-generation compile assertion; it closes the general gap and pins using-self-containedness for free.

### T2. In-flight narrowing layer covered only on positive paths
**Severity:** medium · **Location:** `FacadeCallFinder.cs:138-156` + `SwitchBuilderTest.cs`
No test reaches (a) the documented "returns input unchanged — let SY1018 decide" fallback (e.g. `Switch<int,string>(...)`), or (b) the consumer-facing surface change: bare `Switch(...)` in VALUE position now narrows to the statement overload with undefined, undocumented consumer experience. The only SY1018 test predates arity narrowing. **Direction (before landing the branch):** mismatched-arity case asserting the diagnostic; bare value-position `Switch(...)` asserting the pinned outcome (VerifySwitchTemplate's post-gen compile is the natural assertion point).

### T3. 33 test files copy-paste reference plumbing
**Severity:** medium · **Location:** `SwitchBuilderTest.cs:20-31` et al.
Each declares its own Corlib/NetStandard/System.Runtime statics (hard-pinned `System.Runtime, Version=8.0.0.0` identities resolving on net10 only via forwarding) plus a private CreateCompilation. The right shape exists twice in-repo (MatchTestHarness; GeneratorHarness via TRUSTED_PLATFORM_ASSEMBLIES). The copy count has ~tripled since issues.md #36 triaged it at low — the predicted drift is compounding. **Direction:** extract a shared TemplateTestHarness modeled on MatchTestHarness.

### Low
- **T4** — *(pre-release)* no pinning test for C6 — the known latent bug lives only in session memory; add a quarantined repro now.
- **T5** — *(pre-release)* CacheabilityAssert exists as four unlinked per-project copies; use the sanctioned `<Compile Link>` pattern.
- **T6** — *(pre-release)* SnapshotOrphanGuard matches by simple type name + any method (no [Fact] requirement) and runs only in Synto.Test — three snapshot-bearing projects unprotected.

---

## Domain Expert Assessment (Consequences)

Three principal-engineer calls this round:

1. **Promotion of C2 to HIGH** (from two convergent mediums): both formulations share one root — `resolved[0]` bound as representative under an under-specified interchangeability key — and both terminate in *silent wrong generated output* on a shipped user-facing surface. Every user builder named with a common verb (Return, Member, Case…) is a collision waiting for an alphabetical coin-flip. The fix is a key widening + diagnostic — no contract change — making now the cheap moment.
2. **C1 exposes a product gap, not just a showcase bug.** The dog-food rule says mine ObjectReader friction for features: decorations resolve semantically while quoted body identifiers copy lexically, and no mechanism exists to carry a template's using context into composed output. The short-term example fix is trivial; the real deliverable is quote-time semantic qualification or factory-exposed using sets. T1's harness gap is the enforcement half of the same story — "generated output compiles" must be an asserted property, not an accident of csproj settings.
3. **One-way-door watch (C7):** the injected surface's C# 13 language floor is part of the package contract consumers compile against. Undocumented today, invisible in-repo (LangVersion=preview masks it). Cheap to document/guard now; an onboarding cliff after first external consumers.

## Root Causes

1. **Lexical quoting with no using-context carry** → C1, T1, M13. Template-authoring ergonomics (de-qualification) outran the product's ability to keep composed output self-contained; the showcase harness had no compile gate to catch it.
2. **Representative-overload binding under an under-specified shape key** → C2, C3, T2, M14. The switch-builders generator widening (overloads, arity narrowing, fresh-TResult bridge) outpaced the resolution layer: the interchangeability key, the narrowing predicate, and the negative-path tests all lag the new surface.
3. **Asymmetric is-a migration** → C4, M10. IsExpressionSyntax moved to base-chain walking while returnsType stayed exact-match, and the helper was copy-pasted rather than unified.
4. **Doc drift under high in-flight velocity** → M4, M5, M6, M7, M13. The reorg + rename + sub-template waves left comments describing superseded behavior; several are load-bearing (visibility rationale, SYNC NOTEs the architecture doc tells reviewers to trust).
5. **Missing shared test infrastructure** → T1, T3, T5, T6. Four suites each grow their own harness/guard copies; every gap is per-suite and every fix is n-way.
6. **Whole-compilation scans inside per-template transforms** → P1, P2, P3, P4. Finders eagerly enumerate the assembly and re-bind semantics per node; all doubled by the SY1035 analyzer sharing TryGenerate. Cacheability discipline held — the cost is latency, not correctness.

## Prioritized Action Plan

**Immediate (before the working-tree branch lands):**
1. **C1 + T1 together:** add `using System;` / `using System.Collections.Generic;` to `ObjectReaderGenerator.Emit()` (or restore `global::`), re-accept the snapshot, and add the GeneratorHarness post-generation compile gate that would have caught it.
2. **C3:** uniform `GenericParameters.Count == callTypeArgCount` predicate in NarrowByTypeArgArity.
3. **C4:** IsTypeSyntax base-walk exclusion + facade-shape test pinning TypeSyntax-derived returns.
4. **T2:** the two negative SwitchBuilder tests (mismatched arity; bare `Switch(...)` in value position).
5. **P3:** move GetSymbolInfo inside the matched-name branch (two lines, same file as C3/C4).
6. **M4 + M13:** restore ShareBindingShape's doc comment; drop the stale CS1522 pragma.

**Next sprint:**
7. **C2:** widen the shared-shape key (ContainingType, ReturnsStatement, IsParams) + collision diagnostic.
8. **C5:** catch-all → InternalError guard on `SyntaxBuilderFacadeGenerator.Build`.
9. **C6 + T4:** quarantined repro test for the all-build-time-hole lift, then the CollectLiftPoints or SyntaxFormatter fix.
10. **M2:** route the six `ToSyntax` literals through StagedHelperCallFactory/nameof.
11. **M3:** resolve folder=namespace for the six Synto.Core files (or document exception #3).
12. **T3:** extract TemplateTestHarness (TRUSTED_PLATFORM_ASSEMBLIES) per issues.md #36.
13. **Product follow-up from C1:** spec the using-context carry mechanism (quote-time semantic qualification vs. factory-exposed using set).

**Backlog (pre-1.0, tagged):**
14. P1 + P2 perf work, bundled with the Option C front-end Classify extraction (the tracked 1.0 IDE-latency gate; M1 and P4 close with it).
15. C7 LangVersion floor: document + SurfaceInjectionGenerator guard diagnostic.
16. Mechanical sweeps: M5, M6, M7 doc fixes; M8 name-allocator decision; M9 delete GetNamedBool; M10/M14 helper unification.
17. T5 CacheabilityAssert single-sourcing; T6 orphan-guard tightening + scope extension.
18. M11 Synto.Diagnostics dogfood spike; M12 GetQualifiedNameSyntax TODO resolution; P5-P8 as-touched.

## Strengths

- **Incremental-cacheability discipline held everywhere.** Zero findings of Compilation/ISymbol/SyntaxNode captured into pipeline state — the core discipline the phase doc calls non-negotiable — and the all-steps CacheabilityAssert guard enforces it across suites. Every perf finding is tuning, not a caching breach.
- **Failure-as-diagnostic is established convention, not aspiration.** Both major transforms (Template, Match) wrap their entire pass in catch-all → InternalError; the SY1035 analyzer degrades staging faults to zero highlights. C5 is one generator missing an existing pattern, not a missing pattern.
- **The executable spec is strong enough to refute plausible bug reports.** Round 2 rejected the InterpolationFold formulation of C6 *because verified snapshots proved that path correct* — and separately refuted two coverage claims via snapshot-pinned diagnostics and an executing-factory test.
- **Debt is self-documenting and tracked.** The highlightsOnly workaround names its own 1.0 fix (Option C) in code; deferred boundaries live in memory/spec with explicit gates; determinism is engineered (sorted registry, capped diagnostics).
- **Dog-fooding works as designed.** The ObjectReader showcase surfaced a real product gap (using-context carry) exactly as the friction-probe rule intends — the HIGH finding is the process succeeding, caught pre-commit in the working tree.

## Verification Notes (rejected / merged, for the audit trail)

**Rejected in Round 2 verification:**
1. **InterpolationFold.cs:41-64 as the multiline-fold bug location** — disproven by the executable spec: `InterpolationFoldTest.Escaping_HazardCharacters_RoundTrip` and `Boundary_StartEnd_AndAdjacentHoles_FoldIndependently` cover exactly the claimed repro shape (all-staged holes) and their verified snapshots show a single valid fused token. The genuine latent bug lives on the whole-string `ToSyntax()` lift path + SyntaxFormatter newline injection — re-filed as C6 at the corrected location.
2. **"SY1006 has zero test coverage"** (claimed twice) — refuted: `SimpleTemplateTest.FunctionWithMultipleStatementAsSingle` (:207) pins `Id: SY1006` with a real span in its verified snapshot.
3. **"Statement-Switch skips postGenCompile / no switch-builder factory executed"** — stale against the working tree: both statement tests now run the default post-gen compile, and `SwitchStatement_ProjectionOverEquatableArray_ExecutesWithoutSpuriousLift` executes the generated factory in-process.

**Consolidations (not rejections):**
4. The two standalone mediums on ShareBindingShape (cross-containing-type interchangeability; key omits ReturnsStatement/IsParams) were confirmed and **promoted/merged into C2 (HIGH)** — same root defect, silent-wrong-output endpoints.
5. The ObjectReader de-qualification finding was filed five times across two dimensions and two severities; **merged into C1 at HIGH** — flagship showcase + non-compiling generated output for ImplicitUsings-disabled consumers keeps full severity per project-phase. The applicable self-containedness statement is architecture.md:38, not principles.md:66.
6. NarrowByTypeArgArity (×2 → C3), IsExpressionSyntax/TypeSyntax (×2 → C4), facade-generator guard (×2 → C5), highlightsOnly (×2 → M1), ToSyntax literals (×2 → M2), bare namespaces (×2 → M3), stacked summaries (×3 → M4), registry walk (×2 → P1), classifier O(n²) (×2 → P2), eager GetSymbolInfo (×2 → P3), narrowing tests (×2 → T2), harness copy-paste (×2 → T3) — all pairwise duplicates merged with the stronger evidence retained.

# Rename `Parameter<T>()` → `Unquote<T>()` (authoring-surface taxonomy §3.1)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Collapse the injected live-root facade `Template.Parameter<T>()` into the `Unquote`
name, so one concept ("caller-supplied live root / value lifted into produced syntax") is spelled
`Unquote` across all three forms. This is the ruling in
`docs/superpowers/specs/2026-07-05-authoring-surface-taxonomy.md` §3.1. It is a **breaking rename of
an inert pre-GA facade** — no runtime behavior changes; the generated factory output is unchanged.

**Design source of truth:** `docs/superpowers/specs/2026-07-05-authoring-surface-taxonomy.md` §3.1
(and §2 pattern map). Read it before starting.

## The contract (what "done" means)

After the rename the `Template` facade hosts **two `Unquote<T>` overloads**, and the injected surface
covers the one live-root concept across three spellings:

| Contract | Spelling | Overload bound | Meaning |
|---|---|---|---|
| `[Unquote] T x` | parameter attribute | (n/a — `UnquoteAttribute`) | caller-supplied live root, signature form |
| `Unquote<T>()` / `Unquote<T>(parameterName: "x")` | in-body call, **no value / string name** | `Unquote<T>(string? parameterName = null)` | caller-supplied live root declared in body (folds to a factory parameter) |
| `var n = Unquote<T>(value: expr)` / `Unquote(expr)` | in-body call, **value arg** | `Unquote<T>(T value)` | in-body live local run at factory-build time (unchanged existing form) |

Precise overload signatures on `Template`:
- **Value-source (existing, unchanged):** `public static T Unquote<T>(T value) => value;` — `T` inferred from the argument.
- **Parameter-source (this rename — was `Parameter`):** `public static T Unquote<T>(string? parameterName = null) => default!;` — `T` is **explicit** (cannot be inferred from a string/omitted arg).

**Overload resolution & the `T == string` boundary.** Because both overloads take one parameter, a
positional single-`string` argument with `T == string` is genuinely ambiguous. The contract resolves
it by **explicit argument naming**, and the generator recognizes each call **by its bound overload
symbol, never by method name alone** (both overloads are named `Unquote`):

- `Unquote<int>()`, `Unquote<int>("name")`, `Unquote<string>()` → bind the **parameter-source** overload → live-root parameter.
- `Unquote(expr)` / `Unquote<int>(value: expr)` (non-string or named `value`) → bind the **value-source** overload → live local.
- `Unquote<string>("x")` (positional, `T == string`) → **CS0121 ambiguous by design**; the author must write `Unquote<string>(parameterName: "x")` or `Unquote<string>(value: "x")`. This asymmetry is intended (spec §3.1) — it is the price of two live behaviors sharing one name.

## Global Constraints

- **jj-native, off `main`.** All work lands on bookmark `experimental/constant-facade`. Never move `main`.
- **No git hooks.** Format via `dotnet format whitespace`; CI/green-gate is the enforcement.
- **Test invocation (MTP / SDK 10):** raw `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug`. Not via rtk. Filter (if needed) via `--filter-method` / `--filter-class`, NOT `--filter`.
- **Generated factory output is unchanged.** The rename touches only *recognition of the call* and *authoring spelling* — the factory parameter it lifts to (name from the bound local / explicit `parameterName`, type `T`) is identical. Therefore **every `Factory.*.g.verified.cs` snapshot stays byte-identical**; the only surface snapshot that moves is the injected-surface `Template` snapshot.
- **Recognition is by bound overload symbol.** Do NOT distinguish the two `Unquote` overloads by string name — resolve the overload identity (e.g. resolve both `Unquote` `IMethodSymbol`s off the `Template` type once and compare `method.OriginalDefinition`/`ConstructedFrom`, or inspect the bound overload's single parameter: `parameterName`/`string` vs `value`/`T`). This is the one correctness-critical mechanism in the change.
- **Additive → migrate → remove.** The rename is atomically breaking, so it is staged in three independently-green units (below) rather than one big edit: add the new overload + recognition alongside the old name, migrate all callers, then delete the old name. Each task ends with a clean build + green suite.

## Orientation (read before starting)

Facade (runtime surface, `src/Synto.Core/Templating/`):
- `Staging/Surface/Template.cs:22` — `public static T Parameter<T>(string? parameterName = null) => default!;` (the method being renamed). `Unquote<T>(T value)` local overload is already at `:34`.

Generator recognition (`src/Synto.Generators/Templating/`):
- `Discovery/StagedParameterFinder.cs:135` — `method.Name == nameof(Template.Parameter)` gate; this finder resolves the **parameter-source** live roots (declaration + inline sites → factory parameters, incl. SY1010/SY1013 naming diagnostics).
- `Discovery/StagedRootFinder.cs:168-177` — `VisitInvocationExpression` currently registers a **local** for any `Unquote<T>(...)` call with one type argument. After the rename this over-matches: it would capture parameter-source `Unquote<T>()` calls as locals. Must be narrowed to the **value-source** overload only.
- `Staging/SpliceMemberGeneratorEmitter.cs:124` — second `nameof(Template.Parameter)` gate (rewrites `Parameter<T>()` sites inside a `[Splice]` member generator to the folded factory parameter).

Diagnostic message text (`src/Synto.Generators/Templating/Diagnostics/TemplateDiagnostics.cs`):
- `:88` (SY1010, `UnquoteParameterMissingName`), `:102` (duplicate-name), `:270` ("inputs are supplied via `Parameter<T>()`") — message strings that name `Parameter<T>()`. The diagnostic *IDs* and the `UnquoteParameterMissingName` method name do NOT change; only the human message text.

Comments that name the facade (doc-only): `SpliceMemberGeneratorEmitter.cs:19,81,82`, `BindingTimeClassifier.cs:29,47`, `TemplateFactoryBuilder.cs:288,449,947`, `ChildTemplateFinder.cs:14`, `StagedParameterFinder.cs:13,34,43,52`.

Tests authoring the facade (all these `Parameter<...>()` string-template inputs migrate to `Unquote<...>()`):
- `test/Synto.Test/Templating/InjectedSurfaceCompletenessTest.cs` (multiple RICH templates + the `RenamedRootInRegionTemplate` explicit-name case), `StagedParameterTest.cs`, `StagedRootTest.cs`, `SharedStagedParameterTest.cs`, `RunCollectTest.cs`, `InterpolationFoldTest.cs` (`BareStagedString_ParameterRoot_...`), `SpliceMemberGeneratorTest.cs`, `BindingTimeClassifierTest.cs`, and any other `Synto.Test` file whose template body calls `Parameter<...>()` (grep to find them all).
- Snapshot to re-accept: `test/Synto.Test/snapshots/SurfaceInjectionTest.VerifyInjectedSurface#Template.g.verified.cs` (the injected `Template` facade text).
- `InjectedSurfaceCompletenessTest` is assertion-based (compiles the consumer template against the injected surface) — it is the mechanical proof the renamed surface binds.

Consumer dog-food:
- `examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Generator/ReaderTemplate.cs` — `Unquote<EquatableArray<ColumnInfo>>()` (was `Parameter<...>()`) for `columns` and `Unquote<string>()` for `clrTypeName`. Both are `T != string` (columns) / no-arg (clrTypeName) → bind the parameter-source overload unambiguously, no explicit naming needed. ObjectReader snapshot is unchanged by this rename.

KB docs to update (naming only): `docs/kb/subsystems/templating.md` (live-roots list), `docs/kb/glossary.md`.

---

## Task 1 — Add the parameter-source `Unquote<T>()` overload + recognition, alongside the old `Parameter` (additive; green)

Introduce the new name and teach the generator to recognize it, **without removing `Parameter`** — so nothing that still says `Parameter<...>()` breaks. This isolates the correctness-critical overload-disambiguation logic in one reviewable unit.

- [ ] **Test first.** Add a test proving the new parameter-source `Unquote<T>()` in-body form is recognized and lifts to a factory parameter, mirroring the existing `Parameter<T>()` cases in `StagedParameterTest`/`StagedRootTest`: cover `Unquote<T>()` (declaration position, name from the local), `Unquote<T>(parameterName: "x")` (inline, explicit name), and a `T == string` case that binds the parameter overload via `parameterName:` — assert the factory parameter is produced (snapshot or post-gen compile). Also add/keep a guard that `Unquote(expr)` / `Unquote<T>(value: expr)` still registers a **local**, not a parameter (regression guard for the disambiguation). Run it red.
- [ ] In `Template.cs`, add `public static T Unquote<T>(string? parameterName = null) => default!;` **beside** the existing `Unquote<T>(T value)` and the still-present `Parameter<T>(...)`. Give it the unified XML doc (the three-spellings contract above); note the `T == string` named-argument rule.
- [ ] In `StagedParameterFinder` and `SpliceMemberGeneratorEmitter`, recognize **both** the existing `Parameter` method AND the new parameter-source `Unquote` overload as parameter-source roots. Use the **bound-overload-symbol** discriminator (resolve the two `Unquote` overloads once; the parameter-source one is the overload whose single parameter is `parameterName`/`string`, not `value`/`T`).
- [ ] In `StagedRootFinder.VisitInvocationExpression`, narrow the local-recognition to the **value-source** `Unquote<T>(T value)` overload only, so the new parameter-source `Unquote<T>()`/`Unquote<T>("x")` calls are NOT captured as locals. Same overload-symbol discriminator.
- [ ] **Gate:** build clean; full `Synto.Test` green (existing `Parameter` behavior unchanged, new `Unquote()` form works, `Unquote(value)` locals still work). Re-accept the injected-surface `Template` snapshot (now shows both `Parameter` and the new `Unquote(string?)` overload — a benign additive diff). No `Factory.*.g` snapshot moves.

## Task 2 — Migrate all callers `Parameter<...>()` → `Unquote<...>()` (mechanical; green)

With both names recognized, flip every authoring site to the new name.

- [ ] Migrate every `Synto.Test` template-input `Parameter<...>()` → `Unquote<...>()` (grep the whole `test/` tree; includes the files listed in Orientation). Preserve explicit names: `Parameter<T>("columns")` → `Unquote<T>("columns")` where `T != string` (binds the parameter overload unambiguously). For any `T == string` positional-string site, switch to `Unquote<string>(parameterName: "…")`.
- [ ] Migrate ObjectReader `ReaderTemplate.cs`: `Parameter<EquatableArray<ColumnInfo>>()` → `Unquote<EquatableArray<ColumnInfo>>()` and `Parameter<string>()` → `Unquote<string>()`. Update the adjacent comment that says "build-time `Parameter<...>()` inputs".
- [ ] **Gate:** build clean; full `Synto.Test` green; ObjectReader example builds and its snapshot test is green. Every `Factory.*.g.verified.cs` **byte-identical** (the lifted factory parameter is unchanged). The ObjectReader snapshot is unchanged.

## Task 3 — Remove `Parameter`, retire recognition + docs (cleanup; green)

Delete the old name now that no caller uses it.

- [ ] Remove `Parameter<T>()` from `Template.cs`.
- [ ] Remove the `Parameter`-method branch from `StagedParameterFinder` and `SpliceMemberGeneratorEmitter` — leave only the parameter-source `Unquote`-overload recognition from Task 1.
- [ ] Update the human message text of SY1010/SY1013 and the SY-diagnostic at `TemplateDiagnostics.cs:270` to name `Unquote<T>()` instead of `Parameter<T>()`. Do NOT change diagnostic IDs or the `UnquoteParameterMissingName` symbol. If any diagnostics test asserts exact old message text, update the expectation (prefer asserting on the ID).
- [ ] Update the doc-only comments listed in Orientation (`SpliceMemberGeneratorEmitter`, `BindingTimeClassifier`, `TemplateFactoryBuilder`, `ChildTemplateFinder`, `StagedParameterFinder`) and the KB (`docs/kb/subsystems/templating.md` live-roots list, `docs/kb/glossary.md`) to spell the live root `Unquote`.
- [ ] **Gate:** build clean; full `Synto.Test` green. Re-accept the injected-surface `Template` snapshot (now `Unquote` only — no `Parameter` member). Assert (grep) that no `Parameter` **facade** symbol remains on `Template` and no `nameof(...Template.Parameter)` reference survives in `src/` (Roslyn's own `ParameterSyntax`/`Parameter(...)` factory usages are unrelated and stay).

---

## Verification (whole change)

- `Unquote<T>()` and `Unquote<T>(parameterName:"x")` parameter-source overloads present in `SurfaceInjectionTest.VerifyInjectedSurface#Template.g.verified.cs`; no `Parameter` member remains.
- `InjectedSurfaceCompletenessTest` (all RICH templates, now `Unquote<...>()`) compiles against the injected surface — proves the renamed surface binds.
- Full `Synto.Test` green; all `Factory.*.g` snapshots byte-identical; ObjectReader example green with an unchanged snapshot.
- `dotnet format whitespace --verify-no-changes` clean.
- The value-source `Unquote(expr)` local form is untouched and still recognized (regression guard from Task 1).

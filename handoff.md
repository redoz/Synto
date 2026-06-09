# Synto — .NET 10 + MTP v2 Migration & Packaging Redesign — Handoff

> **CURRENT STATE (updated):** Both threads below are **DONE**. The .NET 10 + MTP v2 migration
> and the packaging redesign (source-injection, standalone `Synto.Core`, file-local helpers)
> landed in HEAD `c915258`; the test suite is **green** (the §5 `Synto.Diagnostics.Test`
> runtime-load failure is **resolved**). A subsequent quality-evaluation remediation pass
> tightened the `Synto.Core` public surface to a **curated** set (internal by default — only the
> 5 markers + 3 emitted helpers public; see §6 item 1, corrected below), moved the templating
> markers to `namespace Synto.Templating`, applied the equatable-pipeline discipline to
> `Synto.Diagnostics` and `Synto.Bootstrap`, and removed a stray `Debugger.Launch()`.
> The sections below are retained as the design rationale; treat the *status* lines in them as
> historical, superseded by this block.
>
> _Original status as of this handoff: migration mechanics **done and building**; one test fails
> for a known reason; packaging redesign **decided but not yet implemented**._

---

## 1. Goal

Two threads of work, in order:

1. **Build modernization** — move all runnable projects to **.NET 10** and switch the test stack
   from VSTest + coverlet to **Microsoft Testing Platform (MTP) v2**.
2. **Packaging redesign** — replace the previous "complex and hacky" hand-packing of `Synto.dll`
   with a clean, intentional NuGet package model. This also fixes the one remaining test failure,
   which is a *symptom* of the current reference/packaging structure.

### Product vision (drives the naming/packaging — DECIDED)

Synto = **"SYNtax TOolkit"**, not a templating library. **Templating is the first capability;
Matching is next** (match on syntax), with more to follow. The umbrella name `Synto` therefore
belongs to the **whole toolkit**, not to templating alone.

- **One generator package, feature areas inside it (DECIDED).** The shipped `Synto` package is the
  *umbrella generator* containing all syntax capabilities — `Templating` today, `Matching` next,
  future features later — as feature folders/namespaces inside the single
  `src/Synto.SourceGenerator` project (already shaped this way: `Templating/` subfolder). Consumers
  add **one** package `Synto` and get the whole toolkit. We explicitly chose **not** to split each
  feature into its own package (no `Synto.Matching` package + meta-package); the dev-time,
  source-injected surface has near-zero cost, so à-la-carte installs aren't worth the versioning
  overhead.
- **`Synto.Diagnostics` stays a separate package** — see Section 6 for the full rationale.

---

## 2. Source-control state

- Tooling: **jj** (Jujutsu) over the Git repo. Remote `origin` = https://github.com/redoz/Synto.
- Repo is on **detached HEAD at commit `01d1146d`**.
- `01d1146d` (already committed this session) = "Adopt central package management; pin .NET 10 SDK
  and Roslyn 5.0 baseline; consolidate analyzer packaging into Directory.Build.targets; add
  RuntimeTypeExtensions ToTypeSyntax tests".
- **Working copy is dirty** with the .NET 10 + MTP migration edits (Section 4) — **not yet committed**.

Commit the working copy with `jj commit -m "..."` once the migration is validated (or fold it into
the packaging work — see Section 6).

---

## 3. Architecture (the mental model you need)

Four projects under `src/`, but only **two are meant to ship**:

| Project | Role | TFM | Ships as |
|---|---|---|---|
| `src/Synto` | **Runtime/toolkit library** — marker attributes (`TemplateAttribute`, `InlineAttribute`, `RuntimeAttribute`), `TemplateOption`, `Syntax`/`Syntax<T>` delegates, plus syntax helpers (`OrNullLiteralExpression`, `ToTypeSyntax`, formatters, list/symbol extensions) | netstandard2.0 | `Synto.Core` (today `IsPackable=false`) |
| `src/Synto.Bootstrap` | Bootstrap generator used to **build Synto itself** (self-host chain). Not shipped. | netstandard2.0 | — |
| `src/Synto.SourceGenerator` | **The shipped umbrella generator** ("SYNtax TOolkit") — holds all syntax features as folders/namespaces: `Templating/` today, `Matching/` next, more later | netstandard2.0 | `Synto` |
| `src/Synto.Diagnostics` | **The shipped diagnostics generator** (separate product/package — see Section 6) | netstandard2.0 | `Synto.Diagnostics` |

Shared packaging logic lives in **`Directory.Build.targets`**, gated on
`<IsSyntoGeneratorPackage>true</IsSyntoGeneratorPackage>`. It hand-injects `Synto.dll` next to each
generator under `analyzers/dotnet/cs` (required — Roslyn does not load an analyzer's NuGet deps into
the analyzer load context), and currently also into `lib/netstandard2.0` for the `Synto` package via
the `SyntoRuntimeIsConsumerDependency` knob.

### Key facts established by reading the generated snapshots (drove the design)
- **Consumer-facing surface is tiny.** Of all of `Synto.dll`, only **two** runtime helpers ever
  appear in consumer-generated output:
  - `OrNullLiteralExpression` — 219 occurrences — `src/Synto/QuoteSyntaxExtensions.cs` (3 lines)
  - `ToTypeSyntax` — 1 occurrence — `src/Synto/RuntimeTypeExtensions.cs` (~81 lines, self-contained)
  - Everything else (`CSharpSyntaxQuoter`, `SyntaxFormatter`, `UsingDirectiveSet`, `SymbolExtensions`,
	list extensions) is **generator-internal** — 0 occurrences in consumer output.
- **Consumer-authored markers** used in hand-written consumer code: `TemplateAttribute`,
  `InlineAttribute`, `RuntimeAttribute`, `TemplateOption`, `Syntax`/`Syntax<T>`. All small,
  depend only on System + Roslyn.
- **`Synto.Diagnostics` generated output is self-contained** — it emits its own `DiagnosticAttribute`
  + factory and calls **no** Synto runtime helpers. So Synto.dll is only needed there at analysis time.
- **Precedent already exists:** `DiagnosticsGenerator` already injects its `DiagnosticAttribute` via
  `RegisterPostInitializationOutput(InjectAttribute)` — see `src/Synto.Diagnostics/DiagnosticsGenerator.cs`.
- Generator type lookups are **string/metadata based** already
  (`ForAttributeWithMetadataName(typeof(TemplateAttribute).FullName!)`,
  `GetTypeByMetadataName(typeof(InlineAttribute).FullName!)`), so they keep working unchanged when the
  consumer's copy of the surface comes from injected source instead of a referenced DLL — the generator
  assembly still carries its own copy for the `typeof`.

---

## 4. What's DONE (working copy, uncommitted) — the .NET 10 + MTP migration

All of this **builds clean (0 errors)** on SDK `10.0.300`.

- **`global.json`** — added MTP runner for `dotnet test`:
  ```json
  { "sdk": { ... }, "test": { "runner": "Microsoft.Testing.Platform" } }
  ```
- **`Directory.Packages.props`** — removed `Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`,
  `coverlet.collector`; renamed `xunit.v3` → **`xunit.v3.mtp-v2` @ 3.2.2** (the MTP-**v2** variant of
  the three packages xUnit ships: `mtp-v1` / `mtp-v2` / `mtp-off`); added
  **`Microsoft.Testing.Extensions.CodeCoverage` @ 18.7.0**; bumped `Verify.XunitV3` → 31.19.0 and
  `Verify.DiffPlex` → 3.2.0 for xUnit v3 3.2.2 compatibility.
- **All three test csproj** (`Synto.Test`, `Synto.Bootstrap.Test`, `Synto.Diagnostics.Test`) —
  `net9.0` → **`net10.0`**, added `<UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>`,
  swapped to the MTP package set above (added `IsTestProject` to Bootstrap.Test which lacked it).
- **`examples/Synto.Examples/Synto.Examples.csproj`** — `net9.0` → `net10.0` (TFM only).
- **CI**: `.github/workflows/{build,publish,codeql}.yml` — `dotnet-version: '9'` → `'10'`.

### Validation result
`dotnet test` runs under the MTP runner and discovers/executes **35 tests**:
- `Synto.Test` — **passing**
- `Synto.Bootstrap.Test` — **passing** (an initial first-run Verify snapshot blip self-resolved on
  re-run; byte-compared identical afterward — not a real regression)
- `Synto.Diagnostics.Test` — **2 failing** — see Section 5.

---

## 5. The one remaining failure (root cause confirmed) — ✅ RESOLVED

> **RESOLVED in `c915258`.** The packaging redesign (§6) made the runtime a normally-referenced
> package, so the `deps.json`/runtime-load failure below no longer occurs; `Synto.Diagnostics.Test`
> is green. Retained below for the root-cause record.

```
System.IO.FileNotFoundException : Could not load file or assembly 'Synto, Version=0.2.0.0'
  at Synto.Diagnostics.Test.DiagnosticsGeneratorTest.GeneratorDriver(...) DiagnosticsGeneratorTest.cs:59
```

`DiagnosticsGeneratorTest` calls `typeof(TemplateAttribute).Assembly.Location` — it loads the **Synto
runtime** at test time. But `Synto.Diagnostics.Test` only references `Synto.Diagnostics`, which
references `Synto` with `PrivateAssets="all"`. So `Synto.dll` is **copy-local but absent from
`deps.json`**. VSTest's testhost probed the output folder (worked); **MTP runs the test as a real app
and resolves via `deps.json` (fails).** It is a reference-graph issue the migration merely *exposed* —
not a packaging defect.

**Minimal fix (independent of the redesign):** add a direct
`<ProjectReference Include="..\..\src\Synto\Synto.csproj" />` to `Synto.Diagnostics.Test.csproj`
(exactly what `Synto.Test` already has). The packaging redesign in Section 6 also resolves it because
the runtime becomes a normally-referenced package.

---

## 6. DECIDED packaging redesign (NOT yet implemented)

Confirmed with the user over several rounds. **This is the design to implement next.**

### Three packages
1. **`Synto.Core`** — the runtime/toolkit library, **published standalone** as a first-class package
   (`lib/netstandard2.0/Synto.dll`). `IsPackable` is true; `PackageId=Synto.Core`.
   **Public API = a CURATED surface (corrected decision):** only the 5 markers
   (`TemplateAttribute`, `InlineAttribute`, `RuntimeAttribute`, `TemplateOption`, `Syntax`/`Syntax<T>`)
   and the 3 emitted helpers (`ToSyntax`, `ToTypeSyntax`, `OrNullLiteralExpression`) are `public`.
   All generator-internal machinery (`CSharpSyntaxQuoter*`, `SyntaxFormatter`, `UsingDirectiveSet`,
   `SymbolExtensions`, the list extensions) is `internal` with `[InternalsVisibleTo]` to the
   generator/diagnostics assemblies. (The earlier "public API = the full toolkit" wording was
   walked back: it's a toolkit, but we don't expose every internal helper — pieces genuinely
   useful standalone get promoted to public deliberately, not by default.)
2. **`Synto`** — the **umbrella generator** ("SYNtax TOolkit"), containing all syntax features —
   `Templating` today, `Matching` next — as folders/namespaces inside the single
   `src/Synto.SourceGenerator` project. Ships the generator + `Synto.dll` under
   `analyzers/dotnet/cs` (analysis-time, unchanged). **No forced runtime dependency and no `lib/`** —
   instead it **injects the small consumer surface as source** (see below). New features add their
   own injected markers/helpers under their feature namespace; they do **not** become new packages.
3. **`Synto.Diagnostics`** — the diagnostics generator, **kept as its own package**, **dev-only
   dependency, no runtime dependency** (its generated output is self-contained). Largely unchanged.

#### Why `Synto.Diagnostics` is a separate package (DECIDED — keep it split)
- **Different product, different audience (primary reason).** It is a general-purpose tool for *any*
  Roslyn generator author who wants to stop hand-writing `DiagnosticDescriptor` boilerplate. That is
  orthogonal to "I want to template/match C# syntax." A templating/matching consumer shouldn't be
  forced to pull in a diagnostics generator, and a generator author who only wants tidy diagnostics
  shouldn't have to take the whole syntax toolkit.
- **Dev-only, self-contained.** `developmentDependency=true`, ships under `analyzers/dotnet/cs`, and
  its generated output calls **no** Synto runtime helpers. Folding it into the umbrella `Synto`
  package would drag an unrelated generator into every toolkit consumer's build.
- **Dogfooding does NOT require shipping them together.** A nice future goal is to use
  `Synto.Diagnostics` *inside* Synto: today `src/Synto.SourceGenerator/Diagnostics.cs` **hand-writes**
  every `DiagnosticDescriptor` (`SY0000`, `SY1001`–`SY1007`). Replacing those with
  `[Diagnostic]`-annotated partial methods + an `OutputItemType="Analyzer"` reference to
  `Synto.Diagnostics` would dogfood it — mirroring how `src/Synto` already consumes `Synto.Bootstrap`
  as an analyzer for self-hosting. That is a *build-time analyzer reference*, entirely independent of
  packaging; the two packages stay separate while still being usable together.
  - ⚠️ **Bootstrap caveat if/when dogfooding:** it makes `Synto.Diagnostics` a build-time dependency
    of the toolkit generator, so a clean build must produce `Synto.Diagnostics` before
    `Synto.SourceGenerator` — same ordering constraint the existing Bootstrap chain already lives
    with. (Not part of the current redesign; noted for later.)

### Source-injection (the core of the redesign)
Instead of forcing consumers to depend on the runtime DLL, the **`Synto` generator injects the
consumer-facing surface as source** (via `RegisterPostInitializationOutput`, same pattern as
`DiagnosticsGenerator` already uses). The surface to inject:
- Marker types: `TemplateAttribute`, `InlineAttribute`, `RuntimeAttribute`, `TemplateOption`,
  `Syntax` / `Syntax<T>`.
- Runtime helpers actually emitted into consumer code: `OrNullLiteralExpression`, `ToTypeSyntax`.

Result: the consumer's generated code is **fully self-contained — no runtime package dependency**.
`Synto.Core` remains available standalone for anyone who wants the toolkit at runtime directly.

**Toolkit growth:** because `Synto` is the umbrella generator, each future feature injects **its own**
surface under its own feature namespace (e.g. `Synto.Templating.*` today, `Synto.Matching.*` next).
Keep the injection wiring per-feature so capabilities can be added without new packages and without
entangling one feature's markers/helpers with another's.

### Injection accessibility / style — **DECISIONS**
- **`internal`** accessibility (not public) — avoids polluting the consumer's public API and reduces
  collision risk when multiple Synto-consuming assemblies are referenced together.
- **File-scoped** injection (latest decision) — use file-scoped **namespaces** throughout the
  injected sources.
  - ⚠️ **Implementation nuance to resolve:** if "file-scoped" was intended as the C# `file` **access
	modifier** (not just file-scoped namespaces), note that it can apply to the **generator-only
	helpers** (`OrNullLiteralExpression`, `ToTypeSyntax`) only if they are injected into the *same*
	compilation unit that uses them — `file` types are invisible across files. The **consumer-authored
	markers** (`TemplateAttribute`, etc.) **cannot** be `file`-scoped, because the consumer references
	them from their *own* hand-written files. Safe default: file-scoped **namespaces** + `internal`
	types for the surface. Confirm intent before relying on the `file` modifier.

### Known follow-up the user flagged (do NOT fix now)
There are **two duplicate copies** of some quoter/helper sources today (`src/Synto` and
`src/Synto.Bootstrap` link the same files: `LiteralSyntaxExtensions.cs`, `UsingDirectiveSet.cs`,
`Formatting/SyntaxFormatter.cs`). The user dislikes the duplication but explicitly deferred it
("something we can look at later"). When wiring injection, prefer a **single source of truth**
(shared/linked file) for the injected helpers so they can't drift from what the quoter emits.

---

## 7. Conflict watch-outs for implementation

- **CS0436 (type defined in source conflicts with imported type):** `Synto.Test` and `Synto.Examples`
  both run the generator **and** reference the runtime `Synto` project directly. If the generator
  injects `internal` copies of types that also exist (public) in the referenced `Synto`/`Synto.Core`,
  those projects may get duplicate-type conflicts/warnings. `TreatWarningsAsErrors` is **not** set
  anywhere, so this is a warning today — but resolve cleanly (e.g. don't reference the runtime in
  projects that also run the generator, gate injection, or rely on `internal` vs `public` resolution
  order). Validate `Synto.Test` and `Synto.Examples` specifically after wiring injection.
- **Self-host / bootstrap chain:** `src/Synto` consumes `Synto.Bootstrap` as an analyzer to build
  itself; don't break that when changing references/packability.

---

## 8. Suggested next steps (implementation order)

1. **Inject the surface** from `Synto.SourceGenerator` via `RegisterPostInitializationOutput`
   (markers + the 2 helpers), file-scoped namespaces, `internal`, single source of truth for helpers.
2. **`src/Synto`** → `IsPackable=true` so **`Synto.Core`** publishes as a real package
   (`lib/netstandard2.0`).
3. **`Directory.Build.targets`** → drop the `SyntoRuntimeIsConsumerDependency` `lib/`-injection branch
   for the `Synto` package (keep the `analyzers/dotnet/cs` injection). `Synto.Diagnostics` stays
   dev-only, no runtime dep.
4. **Fix `Synto.Diagnostics.Test`** (Section 5) — add the direct `ProjectReference` to `src/Synto`.
5. **Reconcile `Synto.Test` / `Synto.Examples`** references vs injected surface (Section 7).
6. **Validate:** `dotnet build` then `dotnet test` (all 35 green under MTP), and `dotnet pack -c Release`
   to confirm `Synto.Core`, `Synto`, `Synto.Diagnostics` package layouts are correct.
7. **Commit** with jj.

---

## 9. Handy commands (PowerShell)

```powershell
# Build / test (MTP runner picked up from global.json)
cd C:\dev\Synto
dotnet build --no-restore -c Debug
dotnet test  --no-build  -c Debug

# Re-run only the currently failing project
dotnet test test\Synto.Diagnostics.Test\Synto.Diagnostics.Test.csproj --no-build -c Debug

# Inspect package output after pack
dotnet pack -c Release
Get-ChildItem artifacts\*.nupkg

# jj
jj status
jj commit -m "<message>"
```

### Notable files
- `global.json`, `Directory.Packages.props`, `Directory.Build.targets`, `Directory.Build.props`
- `src/Synto.SourceGenerator/Templating/TemplateFactorySourceGenerator.cs` (where injection goes)
- `src/Synto.Diagnostics/DiagnosticsGenerator.cs` (`InjectAttribute` = injection precedent)
- `src/Synto/QuoteSyntaxExtensions.cs`, `src/Synto/RuntimeTypeExtensions.cs` (the 2 emitted helpers)
- `src/Synto/Templating/*` (marker types: Template/Inline/Runtime attributes, TemplateOption, Syntax)
- `test/Synto.Diagnostics.Test/DiagnosticsGeneratorTest.cs:59` (the failing runtime load)

### Pre-existing warnings (not introduced here, ignore)
- `NU1507` — two NuGet sources under central package management (github + nuget.org).
- A handful of `CA1508/CA1724/CA1812/CA1852` analyzer warnings in `src` and `examples`.

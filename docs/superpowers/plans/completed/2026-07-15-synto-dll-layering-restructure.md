# Synto Internal DLL Layering + Single-Package Restructure — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restructure Synto's single generator assembly into one DLL per part (`Synto.GenKit` → `Synto.Incremental` → `Synto.Template` / `Synto.Match` / `Synto.Diagnostics`) with compile-enforced module boundaries, all bundled into ONE published `Synto` nupkg, with the cacheable trio (`EquatableArray<T>` / `DiagnosticInfo` / `LocationInfo`) injected into consumers by exactly ONE injector so the package is CS0101-proof.

**Architecture:** Pure code-move + project-split refactor. Today one `Synto.Generators.dll` (PackageId=`Synto`) carries every generator, embeds the whole injected surface, and runs a single `SurfaceInjectionGenerator` that scans its own assembly for `Synto.Runtime.*` resources. We generalize that generator into an abstract `SurfaceInjectorBase` (scans `GetType().Assembly`) living in a new bottom DLL `Synto.GenKit`, carve the trio + a single trio-injector into `Synto.Incremental`, split each feature into its own DLL with its own marker-only surface + its own 3-line injector subclass, and repurpose `Synto.Generators.csproj` as the umbrella that references and packs all feature DLLs into one nupkg. The invariant *only `Synto.Incremental` injects the trio* + *each feature injector injects only its own disjoint markers* keeps N injectors in one consumer compilation collision-free.

**Tech Stack:** C#, Roslyn (`Microsoft.CodeAnalysis.CSharp`), incremental source generators (`netstandard2.0` analyzers), MSBuild multi-assembly analyzer packaging, xUnit + Verify snapshot tests, SDK 10 MTP.

## Global Constraints

- **This is a pure restructure — output must not change.** Byte-identical snapshots ARE the green gate. Every code-move task re-runs the affected `SurfaceInjectionTest` (injected consumer surface) and `ResourceManifestPinTest` (embedded-resource manifest) and the feature snapshot suites; a byte diff that is NOT an intended, explained resource-move is a regression, not an accept.
- **`TreatWarningsAsErrors` is global** (`Directory.Build.props`, `--no-incremental` solution-wide). Every task ends at 0 warnings, 0 errors.
- **Single-injector invariant (the CS0101 proof):** the trio is injected by EXACTLY ONE injector, in `Synto.Incremental`. Every other injector (`Synto.Template`, `Synto.Match`, `Synto.Diagnostics`) injects ONLY its own disjoint marker set (Template markers ≠ Match markers ≠ Diagnostics markers ≠ trio). No feature DLL embeds the trio.
- **Injected trio namespace is frozen:** the injected trio stays `namespace Synto.Generators` (spec §107, §150 — deferred churn). Do NOT re-point the namespace; existing generated Template/Match references depend on it.
- **Injected trio source is `internal` and injected VERBATIM.** `PublicToInternalRewriter` is a no-op on it (already `internal`). Only the `public` marker surface authored under `Synto.Core/**/Surface` gets flipped to `internal`. Do NOT add leading header comments to any embedded-resource file — leading trivia leaks into byte-identical injected snapshots.
- **Diagnostic-release tracking is PER ASSEMBLY (RS2008).** Each generator DLL that ships `SY`-prefixed descriptors and references `Microsoft.CodeAnalysis.Analyzers` needs its OWN `AnalyzerReleases.Shipped.md` / `AnalyzerReleases.Unshipped.md`. The single root `AnalyzerReleases.Unshipped.md` (all SY0000–SY1205) must be split per owning DLL.
- **Do NOT touch `main`.** HEAD is detached off `main`; commit each task on the current detached HEAD, like the sibling committed spec/plan. Never rebase/reset onto or push `main`.
- **Commit with pathspec only** (`git add <path> && git commit <path> -m …`). NEVER sweep in the pre-existing staged file `docs/superpowers/plans/2026-07-12-live-highlight-option-c.md` (staged, not ours) or other pre-existing dirty files (`Directory.Build.props`, `AnalyzerReleases.Unshipped.md` if pre-dirty, `TemplateDiagnostics.cs`). End every commit message with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- **Test invocation (SDK 10 MTP, raw `dotnet test`; do NOT use rtk's dotnet-test rewrite):** `dotnet test test/Synto.Test --filter-class <FQN>`, `dotnet test test/Synto.Diagnostics.Test --filter-class <FQN>`. Use `--filter-class` / `--filter-method`, never `--filter`.
- **Snapshot acceptance (Verify):** a failing snapshot writes `*.received.*` next to `*.verified.*`. After confirming the diff is an intended resource-move (never a content change), accept by replacing the `.verified.` content with the `.received.` and deleting `.received.`.

## Ground-truth facts (verified against current code — do not re-derive)

- Single injector today: `src/Synto.Generators/SurfaceInjectionGenerator.cs:58` (`[Generator]`), scans `typeof(SurfaceInjectionGenerator).Assembly` at `:81`, filters `ResourcePrefix = "Synto.Runtime."` (`:62`), `RegisterPostInitializationOutput` (`:69-76`). `PublicToInternalRewriter` is a nested `private sealed class` at `:141` — NOT its own file.
- A SECOND copy of the public→internal rewrite lives in `src/Synto.Generators/Templating/Prelude/HelperResourceLoader.cs` (comments at `:17`, `:88` say it "mirrors `SurfaceInjectionGenerator.PublicToInternalRewriter`"). Fold both into GenKit.
- Trio embedding today: `src/Synto.Generators/Synto.Generators.csproj:62-63` embeds `Incremental\EquatableArray.cs` and `Incremental\DiagnosticInfo.cs` (the latter ALSO declares `LocationInfo` — the trio is 2 files, LocationInfo rides inside DiagnosticInfo.cs). Marker surface globbed at `:48-49` from `..\Synto.Core\**\Surface` + `**\InjectedOnly`. Emit-helper scan glob at `:75` (`Synto.Helper.*`, unrelated to trio).
- Duplicate trio (to consolidate): `src/Synto.Diagnostics/EquatableArray.cs` and `src/Synto.Diagnostics/DiagnosticInfo.cs` are internal-only copies (NOT injected). They differ from the Generators copies by namespace AND an extra `<remarks>` XML-doc block (`EquatableArray.cs:12-15`). These are the ones to DELETE; the injected consumer copy is the no-remarks `Synto.Generators` copy, so consolidation leaves injected snapshots untouched.
- The 4 dep-free helpers are all SAFE-TO-MOVE (only generator projects reference them): `UsingDirectiveSet` (`src/Synto.Core/Emit/UsingDirectiveSet.cs`; refs in Synto.Generators, Synto.Bootstrap, Synto.Diagnostics), `SyntaxFormatter` (`src/Synto.Core/Formatting/SyntaxFormatter.cs`; same three), `GeneratedSourceFilenameExtensions`/`ToGeneratedFilename` (`src/Synto.Core/Emit/GeneratedSourceFilenameExtensions.cs`; Synto.Diagnostics only), `NameAllocator` (`src/Synto.Generators/Templating/Emit/NameAllocator.cs`; internal to Synto.Generators). **Synto.Bootstrap references `UsingDirectiveSet` + `SyntaxFormatter`** (`CSharpSyntaxQuoterGenerator.cs:101,309`) → it gains a `Synto.GenKit` reference.
- The shared Emit shape (`{ string? FileName, string? Source, EquatableArray<DiagnosticInfo> Diagnostics }` + `foreach ReportDiagnostic` then `if FileName!=null && Source!=null → AddSource` in a `CA1031` try/catch degrading to `InternalError`) appears in `TemplateGenerationResult.cs:8` + `TemplateFactorySourceGenerator.cs:149-161`, `MatchGenerationResult.cs:9` + `MatchFactorySourceGenerator.cs:102-114`, `DiagnosticGenerationResult.cs:9` + `DiagnosticsGenerator.cs:117-132`. Two VARIANTS are out of scope for consolidation: `FacadeGenerationResult` (field is `HintName`, not `FileName`; `Templating/Builders/`) and `QuoterGenerationResult` (`Synto.Bootstrap/`).
- The `SY0000` (internal error) + `SY1001` (target not partial) descriptors + `IdPrefix="SY"` live in `src/Synto.Generators/Incremental/Diagnostics.cs` — these are SHARED root descriptors used by the Emit base, so they move to `Synto.Incremental`. Template SY1005–SY1034 in `Templating/Diagnostics/TemplateDiagnostics.cs`; SY1035 (Info) in `Templating/Diagnostics/LiveTemplateHighlightAnalyzer.cs`; Match SY1201–SY1205+ in `Matching/Diagnostics/MatchDiagnostics.cs`.
- Only 3 `[Generator]` classes exist: `SurfaceInjectionGenerator`, `TemplateFactorySourceGenerator`, `MatchFactorySourceGenerator`. `LiveTemplateHighlightAnalyzer` is a `[DiagnosticAnalyzer]` (Template feature). `DiagnosticsGenerator` is in `src/Synto.Diagnostics`.
- csproj inventory: `Synto.Core` (plain lib, PackageId `Synto.Core`), `Synto.Generators` (PackageId **`Synto`**, `IsSyntoGeneratorPackage=true` — TODAY'S umbrella), `Synto.Bootstrap` (plain, self-hosts the quoter generator, NOT a package), `Synto.Diagnostics` (PackageId default `Synto.Diagnostics`, `IsSyntoGeneratorPackage=true`, `developmentDependency=true` — retire as a separate package).
- Packaging mechanism (`Directory.Build.targets`): `None … PackagePath="analyzers/dotnet/cs"` items at `:44-46` do the actual PACKING (LICENSE + `$(AssemblyName).dll` + `Synto.Core.dll`). `SyntoGetDependencyTargetPaths` at `:54-60` is a GetTargetPath HOOK (single-assembly: injects only `Synto.Core.dll` so Roslyn resolves it at analyzer load). "Multi-assembly pack" means extending BOTH: pack all feature DLLs, and hook all of them for resolution.

## End-state DLL layout (target)

```
Synto.GenKit         (netstandard2.0 analyzer, NO Synto deps) — bundled, not standalone-packed
  SurfaceInjectorBase (abstract, GetType().Assembly scan), PublicToInternalRewriter,
  HelperResourceLoader (folded), UsingDirectiveSet, SyntaxFormatter,
  GeneratedSourceFilenameExtensions, NameAllocator
    ▲
Synto.Incremental    (refs GenKit) — bundled
  EquatableArray / DiagnosticInfo / LocationInfo (injected trio, namespace Synto.Generators),
  Diagnostics.cs (SY0000/SY1001 + IdPrefix), shared GenerationResult + Emit base,
  ONE trio injector: [Generator] IncrementalSurfaceInjector : SurfaceInjectorBase {}
    ▲
Synto.Template   Synto.Match   Synto.Diagnostics     (each refs GenKit + Incremental; NOT each other)
  one generator + its OWN marker surface embed + its OWN [Generator] SurfaceInjector subclass
  + its OWN AnalyzerReleases.{Shipped,Unshipped}.md
    ▲
Synto  (umbrella; repurposed Synto.Generators.csproj, PackageId=Synto) — refs all feature DLLs;
       pack target bundles every analyzer DLL into analyzers/dotnet/cs of one nupkg.

Synto.Core  — runtime lib, SEPARATE axis, unchanged as a published lib (helpers moved out are
              build-time-only; confirm they were not part of its intended public API).
Synto.Bootstrap — gains a GenKit reference (uses UsingDirectiveSet + SyntaxFormatter).
```

**Scope call-out — `Synto.Model` is NOT created here.** The `[SyntoModel]` feature DLL is created by the `[SyntoModel]` plan, not this restructure. Do NOT create an empty `Synto.Model` project. This plan covers GenKit / Incremental / Template / Match / Diagnostics + the umbrella only.

---

## Task 0: Pin the green baseline

**Files:**
- Test only (read/run): `test/Synto.Test`, `test/Synto.Diagnostics.Test`.

**Interfaces:**
- Produces: a recorded, reproducible baseline (build clean + the four guard suites green) that every later task's byte-identical gate is measured against.

- [ ] **Step 1: Full clean build.** Run: `dotnet build Synto.sln --no-incremental`. Expected: PASS, 0 warnings, 0 errors. If not clean on the untouched tree, STOP and report — do not start the restructure on a red base.
- [ ] **Step 2: Run the guard suites and confirm green.**
  - `dotnet test test/Synto.Test --filter-class Synto.Test.SurfaceInjectionTest`
  - `dotnet test test/Synto.Test --filter-class Synto.Test.ResourceManifestPinTest`
  - `dotnet test test/Synto.Diagnostics.Test --filter-class Synto.Diagnostics.Test.DiagnosticsGeneratorTest`
  Expected: PASS.
- [ ] **Step 3: Record the baseline manifest.** Note the current set of `Synto.Runtime.*` and `Synto.Helper.*` LogicalNames pinned by `ResourceManifestPinTest` and the injected-surface files pinned by `SurfaceInjectionTest`. This is the reference for "which resource genuinely moved" in later accepts. No commit (read-only baseline).

---

## Phase 1 — `Synto.GenKit` (bottom of the stack)

### Task 1: Create the `Synto.GenKit` project

**Files:**
- Create: `src/Synto.GenKit/Synto.GenKit.csproj`
- Modify: `Synto.sln` (add project)

**Interfaces:**
- Produces: an empty `netstandard2.0` analyzer class library with NO reference to `Synto.Core` and NO `Synto` dependency, ready to receive the plumbing. It is bundled by the umbrella (Phase 4), not standalone-packed, so it does NOT set `IsSyntoGeneratorPackage`.

- [ ] **Step 1: Author the csproj.**
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
      <TargetFramework>netstandard2.0</TargetFramework>
      <RootNamespace>Synto.GenKit</RootNamespace>
      <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
      <!-- Bundled into the Synto package by the umbrella; not itself a package. -->
      <IsPackable>false</IsPackable>
    </PropertyGroup>
    <ItemGroup>
      <PackageReference Include="Microsoft.CodeAnalysis.CSharp" />
    </ItemGroup>
  </Project>
  ```
- [ ] **Step 2: Add to solution.** Run: `dotnet sln Synto.sln add src/Synto.GenKit/Synto.GenKit.csproj`
- [ ] **Step 3: Build.** Run: `dotnet build src/Synto.GenKit/Synto.GenKit.csproj`. Expected: PASS (empty project compiles).
- [ ] **Step 4: Commit.**
  ```bash
  git add src/Synto.GenKit/Synto.GenKit.csproj Synto.sln
  git commit src/Synto.GenKit/Synto.GenKit.csproj Synto.sln -m "build(genkit): scaffold Synto.GenKit netstandard2.0 analyzer project

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
  ```

### Task 2: Extract `SurfaceInjectorBase` + `PublicToInternalRewriter` into GenKit

**Files:**
- Create: `src/Synto.GenKit/SurfaceInjectorBase.cs`, `src/Synto.GenKit/PublicToInternalRewriter.cs`
- Modify: `src/Synto.Generators/SurfaceInjectionGenerator.cs` (becomes a 3-line concrete subclass), `src/Synto.Generators/Templating/Prelude/HelperResourceLoader.cs` (drop mirrored rewriter, use GenKit's), `src/Synto.Generators/Synto.Generators.csproj` (ref GenKit)
- Modify: `test/Synto.Test/…SurfaceInjectionTest` harness if it instantiates the generator by concrete type
- Test: `test/Synto.Test` — `SurfaceInjectionTest`

**Interfaces:**
- Produces: `public abstract class SurfaceInjectorBase : IIncrementalGenerator` in `namespace Synto.GenKit`. It performs today's logic but scans `GetType().Assembly` (the DERIVING assembly) instead of `typeof(SurfaceInjectionGenerator).Assembly`, with `protected virtual string ResourcePrefix => "Synto.Runtime.";`. It calls `RegisterPostInitializationOutput`, loads each embedded resource whose LogicalName starts with `ResourcePrefix`, and injects it (flipping `public`→`internal` via `PublicToInternalRewriter`, a no-op on already-`internal` source).
- Produces: `public sealed class PublicToInternalRewriter : CSharpSyntaxRewriter` (moved verbatim out of the nested class + the `HelperResourceLoader` mirror; single canonical copy).
- Consumes (by Task 6, 8, 9, 10): each feature DLL declares `[Generator] internal sealed class <Feature>SurfaceInjector : SurfaceInjectorBase {}` — a 3-line subclass that injects only its own assembly's embedded resources.

- [ ] **Step 1: Move the rewriter.** Cut the nested `PublicToInternalRewriter` from `SurfaceInjectionGenerator.cs:141`, make it a top-level `public sealed class` in `src/Synto.GenKit/PublicToInternalRewriter.cs`, `namespace Synto.GenKit`. Delete the mirrored implementation in `HelperResourceLoader.cs` and repoint its call site to `Synto.GenKit.PublicToInternalRewriter`.
- [ ] **Step 2: Move the base.** Copy the body of `SurfaceInjectionGenerator` into `src/Synto.GenKit/SurfaceInjectorBase.cs` as `public abstract class SurfaceInjectorBase : IIncrementalGenerator`, `namespace Synto.GenKit`. Replace `typeof(SurfaceInjectionGenerator).Assembly` with `GetType().Assembly`. Extract the `"Synto.Runtime."` literal into `protected virtual string ResourcePrefix => "Synto.Runtime.";`. Remove `[Generator]` from the base (subclasses carry it).
- [ ] **Step 3: Reduce the original to a concrete subclass** so Synto.Generators (which still embeds the whole surface this phase) keeps injecting byte-identically:
  ```csharp
  // src/Synto.Generators/SurfaceInjectionGenerator.cs
  using Microsoft.CodeAnalysis;
  using Synto.GenKit;

  namespace Synto.Generators;

  [Generator]
  public sealed class SurfaceInjectionGenerator : SurfaceInjectorBase { }
  ```
- [ ] **Step 4: Reference GenKit.** In `src/Synto.Generators/Synto.Generators.csproj` add `<ProjectReference Include="..\Synto.GenKit\Synto.GenKit.csproj" />` (analyzer-flavored wiring is deferred to Phase 4; a plain ProjectReference is fine for build correctness now).
- [ ] **Step 5: Run the injection guard, expect byte-identical.** Run: `dotnet test test/Synto.Test --filter-class Synto.Test.SurfaceInjectionTest`. Expected: PASS with NO snapshot diffs (same resources, same injector logic, only the base class moved). If the harness constructs the generator by concrete `new SurfaceInjectionGenerator()`, that still works (concrete subclass retained). If a diff appears, it is a bug in the move — fix, do not accept.
- [ ] **Step 6: Full build.** Run: `dotnet build Synto.sln --no-incremental`. Expected: 0 warnings, 0 errors.
- [ ] **Step 7: Commit.**
  ```bash
  git add src/Synto.GenKit/SurfaceInjectorBase.cs src/Synto.GenKit/PublicToInternalRewriter.cs src/Synto.Generators/SurfaceInjectionGenerator.cs src/Synto.Generators/Templating/Prelude/HelperResourceLoader.cs src/Synto.Generators/Synto.Generators.csproj
  git commit <same paths> -m "refactor(genkit): extract SurfaceInjectorBase (GetType().Assembly scan) + PublicToInternalRewriter

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
  ```

### Task 3: Move the dep-free helpers into GenKit

**Files:**
- Move: `src/Synto.Core/Emit/UsingDirectiveSet.cs`, `src/Synto.Core/Formatting/SyntaxFormatter.cs`, `src/Synto.Core/Emit/GeneratedSourceFilenameExtensions.cs`, `src/Synto.Generators/Templating/Emit/NameAllocator.cs` → `src/Synto.GenKit/`
- Modify: `src/Synto.Bootstrap/Synto.Bootstrap.csproj` (add GenKit ref), `src/Synto.Core/Synto.Core.csproj` (only if it globs those folders explicitly)
- Test: `test/Synto.Test`, `test/Synto.Diagnostics.Test` (full build is the gate)

**Interfaces:**
- Produces: `UsingDirectiveSet`, `SyntaxFormatter`, `GeneratedSourceFilenameExtensions` (`ToGeneratedFilename`), `NameAllocator` all in `namespace Synto.GenKit` (or keep their current namespaces and just relocate the files — pick whichever keeps call sites unchanged; verify the 6 Synto.Generators refs + 2 Bootstrap refs + 2 Diagnostics refs still resolve).

- [ ] **Step 1: Confirm these types are not part of Synto.Core's intended public API.** They are referenced only by generator projects (verified). Confirm no `<InternalsVisibleTo>`-independent public consumer depends on them; pre-release, treat as build-time plumbing. If any is genuinely public Synto.Core API, STOP and flag (narrow-GenKit fallback: leave that one in Synto.Core). Otherwise proceed.
- [ ] **Step 2: Physically move the four files** into `src/Synto.GenKit/`. Keep their existing namespaces if that leaves call sites untouched; otherwise add a `using` at each of the (few) call sites. NameAllocator's only ref is `Templating/Emit/TemplateBuildContext.cs:56`.
- [ ] **Step 3: Repoint Synto.Bootstrap.** Add `<ProjectReference Include="..\Synto.GenKit\Synto.GenKit.csproj" />` to `src/Synto.Bootstrap/Synto.Bootstrap.csproj` (it uses `UsingDirectiveSet` + `SyntaxFormatter`).
- [ ] **Step 4: Full clean build.** Run: `dotnet build Synto.sln --no-incremental`. Expected: 0 warnings, 0 errors (all cross-project refs now resolve helpers from GenKit).
- [ ] **Step 5: Run the two guard suites** to prove no behavior/manifest drift: `SurfaceInjectionTest`, `ResourceManifestPinTest` (helpers are not embedded resources, so manifest is unchanged; this confirms nothing regressed).
- [ ] **Step 6: Commit.**
  ```bash
  git add src/Synto.GenKit/ src/Synto.Core/ src/Synto.Generators/ src/Synto.Bootstrap/Synto.Bootstrap.csproj
  git commit <same paths> -m "refactor(genkit): relocate dep-free syntax/naming helpers into Synto.GenKit

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
  ```

---

## Phase 2 — `Synto.Incremental` (cacheable consumer data surface)

### Task 4: Create `Synto.Incremental` and move the trio + root diagnostics

**Files:**
- Create: `src/Synto.Incremental/Synto.Incremental.csproj`
- Move: `src/Synto.Generators/Incremental/EquatableArray.cs`, `Incremental/DiagnosticInfo.cs` (holds `LocationInfo`), `Incremental/Diagnostics.cs` (SY0000/SY1001 + `IdPrefix`) → `src/Synto.Incremental/`
- Modify: `src/Synto.Generators/Synto.Generators.csproj` (drop the two trio `EmbeddedResource` lines, add them to Incremental's csproj; ref Incremental), `Synto.sln`
- Modify: `test/Synto.Test/…ResourceManifestPinTest` snapshot (trio now embedded in a different assembly)
- Test: `SurfaceInjectionTest`, `ResourceManifestPinTest`

**Interfaces:**
- Produces: `Synto.Incremental` project, `netstandard2.0` analyzer, `<ProjectReference … Synto.GenKit>`, `IsPackable=false`. It embeds the trio under `Synto.Runtime.*` (moved verbatim from Synto.Generators.csproj:62-63). The trio types stay `namespace Synto.Generators` (frozen).
- Produces: `Synto.Generators.Diagnostics` (SY0000/SY1001) + `IdPrefix` now compiled in `Synto.Incremental`; Synto.Generators references them transitively.

- [ ] **Step 1: Author the csproj** (mirror GenKit; add the trio embed and Analyzers ref for the SY descriptors):
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
      <TargetFramework>netstandard2.0</TargetFramework>
      <RootNamespace>Synto.Generators</RootNamespace>
      <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
      <IsPackable>false</IsPackable>
    </PropertyGroup>
    <ItemGroup>
      <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" PrivateAssets="all" />
      <PackageReference Include="Microsoft.CodeAnalysis.CSharp" />
    </ItemGroup>
    <ItemGroup>
      <ProjectReference Include="..\Synto.GenKit\Synto.GenKit.csproj" />
    </ItemGroup>
    <!-- Trio injected VERBATIM (already internal, namespace Synto.Generators). No header comments. -->
    <ItemGroup>
      <EmbeddedResource Include="EquatableArray.cs" LogicalName="Synto.Runtime.EquatableArray.cs" />
      <EmbeddedResource Include="DiagnosticInfo.cs" LogicalName="Synto.Runtime.DiagnosticInfo.cs" />
    </ItemGroup>
    <ItemGroup>
      <InternalsVisibleTo Include="Synto.Test" />
    </ItemGroup>
  </Project>
  ```
- [ ] **Step 2: Move the three files** into `src/Synto.Incremental/` (namespaces unchanged — all are `Synto.Generators`). Delete the two `<EmbeddedResource Include="Incremental\…">` lines from `src/Synto.Generators/Synto.Generators.csproj:62-63`.
- [ ] **Step 3: Wire refs.** `dotnet sln Synto.sln add src/Synto.Incremental/Synto.Incremental.csproj`; add `<ProjectReference Include="..\Synto.Incremental\Synto.Incremental.csproj" />` to `src/Synto.Generators/Synto.Generators.csproj`.
- [ ] **Step 4: Full build.** Run: `dotnet build Synto.sln --no-incremental`. Expected: 0 warnings — but the trio is now injected by Synto.Incremental's future injector, NOT yet. At THIS step Synto.Generators no longer embeds the trio and Incremental has no injector yet, so consumers would lose the trio. To keep this step green and byte-identical, ADD the trio injector in the SAME task — see Step 5.
- [ ] **Step 5: Add the lone trio injector now** (this is the single-injector invariant; do it here so the trio never goes un-injected mid-migration):
  ```csharp
  // src/Synto.Incremental/IncrementalSurfaceInjector.cs
  using Microsoft.CodeAnalysis;
  using Synto.GenKit;

  namespace Synto.Generators;

  [Generator]
  public sealed class IncrementalSurfaceInjector : SurfaceInjectorBase { }
  ```
  This scans `Synto.Incremental.dll` (which embeds ONLY the trio) and injects the trio. Meanwhile Synto.Generators' `SurfaceInjectionGenerator` now scans an assembly whose embedded set no longer includes the trio, so it injects only markers — no double injection, no CS0101.
- [ ] **Step 6: Update the test harness generator list.** Wherever `SurfaceInjectionTest` / consumers instantiate the injector, ensure BOTH `SurfaceInjectionGenerator` and `IncrementalSurfaceInjector` run (the real consumer gets both DLLs). Run: `dotnet test test/Synto.Test --filter-class Synto.Test.SurfaceInjectionTest`. Expected: byte-identical injected surface (trio still injected exactly once, markers unchanged). Accept nothing content-wise.
- [ ] **Step 7: Re-point the manifest pin.** Run: `dotnet test test/Synto.Test --filter-class Synto.Test.ResourceManifestPinTest`. Expected: FAIL — the trio LogicalNames moved out of Synto.Generators' manifest into Synto.Incremental's. If the pin test asserts a single-assembly manifest, split/extend it to pin each assembly's manifest (Synto.Generators markers, Synto.Incremental trio). Accept the intended move. Re-run PASS.
- [ ] **Step 8: Commit.**
  ```bash
  git add src/Synto.Incremental/ src/Synto.Generators/Synto.Generators.csproj Synto.sln test/Synto.Test/
  git commit <same paths> -m "refactor(incremental): carve trio + root diagnostics into Synto.Incremental with lone trio injector

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
  ```

### Task 5: Consolidate the duplicate trio (retire the Synto.Diagnostics copies)

**Files:**
- Delete: `src/Synto.Diagnostics/EquatableArray.cs`, `src/Synto.Diagnostics/DiagnosticInfo.cs`
- Modify: `src/Synto.Diagnostics/Synto.Diagnostics.csproj` (ref `Synto.Incremental`), any Synto.Diagnostics files that used the local copies (namespace change `Synto.Diagnostics` → `Synto.Generators` for these types)
- Test: `test/Synto.Diagnostics.Test` — `DiagnosticsGeneratorTest`

**Interfaces:**
- Consumes: `Synto.Generators.EquatableArray<T>` / `DiagnosticInfo` / `LocationInfo` from `Synto.Incremental` (the single definition).
- Produces: `Synto.Diagnostics` pipeline now uses the Incremental trio for its own analysis-time state; no third copy remains.

- [ ] **Step 1: Reference Incremental.** Add `<ProjectReference Include="..\Synto.Incremental\Synto.Incremental.csproj" />` to `src/Synto.Diagnostics/Synto.Diagnostics.csproj`.
- [ ] **Step 2: Delete the two duplicate files** (`Synto.Diagnostics/EquatableArray.cs`, `Synto.Diagnostics/DiagnosticInfo.cs` — the remarks-carrying `namespace Synto.Diagnostics` copies).
- [ ] **Step 3: Repoint internal references.** Anything in `src/Synto.Diagnostics/**` referencing `Synto.Diagnostics.EquatableArray`/`DiagnosticInfo`/`LocationInfo` now resolves `Synto.Generators.*` from Incremental. Add `using Synto.Generators;` where needed.
- [ ] **Step 4: Build + test.** Run: `dotnet build Synto.sln --no-incremental` (0 warnings) then `dotnet test test/Synto.Diagnostics.Test --filter-class Synto.Diagnostics.Test.DiagnosticsGeneratorTest`. Expected: PASS, snapshots byte-identical (Synto.Diagnostics does NOT inject the trio today, so no injected-surface change; the deleted copies were internal-only).
- [ ] **Step 5: Commit.**
  ```bash
  git add src/Synto.Diagnostics/
  git commit src/Synto.Diagnostics/ -m "refactor(incremental): consolidate Synto.Diagnostics onto the single Incremental trio

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
  ```

### Task 6: Extract the shared `GenerationResult` + `Emit` base into Incremental

**Files:**
- Create: `src/Synto.Incremental/GenerationResult.cs` (or `GeneratorEmitExtensions.cs`)
- Modify: `src/Synto.Generators/Templating/Emit/TemplateGenerationResult.cs` + `TemplateFactorySourceGenerator.cs:149-161`, `src/Synto.Generators/Matching/Emit/MatchGenerationResult.cs` + `MatchFactorySourceGenerator.cs:102-114`, `src/Synto.Diagnostics/DiagnosticGenerationResult.cs` + `DiagnosticsGenerator.cs:117-132`
- Test: `test/Synto.Test` (Template + Match snapshot suites), `test/Synto.Diagnostics.Test`

**Interfaces:**
- Produces: `Synto.Generators.GenerationResult` = `record struct GenerationResult(string? FileName, string? Source, EquatableArray<DiagnosticInfo> Diagnostics)` (or an interface + shared `Emit` extension). Produces `static void Emit(this SourceProductionContext ctx, in GenerationResult result)` implementing the `foreach ReportDiagnostic` → `if FileName!=null && Source!=null → AddSource` in the `CA1031` try/catch degrading to `SY0000` InternalError.
- Consumes: Template/Match/Diagnostic generators call the shared `Emit`; their per-feature result records either alias or are replaced by `GenerationResult`.

**Note (scope):** consolidate ONLY the three that share the exact `{ FileName, Source, Diagnostics }` shape. Leave `FacadeGenerationResult` (`HintName` field) and `QuoterGenerationResult` (Synto.Bootstrap) as-is — different shape / different assembly; folding them is out of scope (spec §77 lists only the three). This task is behavior-preserving reuse; if it risks a snapshot diff, it is separable — a reviewer may defer it without blocking the CS0101 goal.

- [ ] **Step 1: Confirm baseline green** for all three suites (Template snapshots, Match snapshots, Diagnostics snapshots).
- [ ] **Step 2: Author the shared `GenerationResult` + `Emit`** in Synto.Incremental, copied from the current Template implementation (the canonical one at `TemplateFactorySourceGenerator.cs:149-161`).
- [ ] **Step 3: Migrate the three generators** onto the shared type/extension. Replace each local result record + inline Emit loop with the shared ones.
- [ ] **Step 4: Build + run all three suites.** Run: `dotnet build Synto.sln --no-incremental`, then the Template/Match/Diagnostics snapshot suites. Expected: 0 warnings, ALL snapshots byte-identical (behavior-preserving). Any diff = bug in the extraction.
- [ ] **Step 5: Commit.**
  ```bash
  git add src/Synto.Incremental/ src/Synto.Generators/ src/Synto.Diagnostics/
  git commit <same paths> -m "refactor(incremental): unify GenerationResult + Emit across Template/Match/Diagnostics

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
  ```

---

## Phase 3 — Split feature DLLs (compile-enforced module boundaries)

Each feature DLL: `netstandard2.0` analyzer, `IsPackable=false`, refs `Synto.GenKit` + `Synto.Incremental` (never another feature), embeds ONLY its own marker surface, declares its OWN `[Generator] <Feature>SurfaceInjector : SurfaceInjectorBase {}`, and owns its OWN `AnalyzerReleases.{Shipped,Unshipped}.md`.

### Task 7: Carve `Synto.Template`

**Files:**
- Create: `src/Synto.Template/Synto.Template.csproj`, `src/Synto.Template/TemplateSurfaceInjector.cs`, `src/Synto.Template/AnalyzerReleases.Shipped.md` + `AnalyzerReleases.Unshipped.md`
- Move: `src/Synto.Generators/Templating/**` → `src/Synto.Template/` (includes `TemplateFactorySourceGenerator`, `Diagnostics/{TemplateDiagnostics.cs (SY1005–SY1034), LiveTemplateHighlightAnalyzer.cs (SY1035), TrackingNames.cs}`, `Builders/**` (Facade), `Decorations/**`, `Discovery/**`, `Emit/**` (minus NameAllocator, already in GenKit), `Lifting/**`, `Prelude/FileLocalHelpers.cs`, `Quoting/**`, `Staging/**`, `BindingTime/**`)
- Modify: `Synto.Generators.csproj` (drop the Templating embed globs for Template markers; will be re-homed here), `Synto.sln`
- Test: Template snapshot + highlight suites, `SurfaceInjectionTest`, `ResourceManifestPinTest`

**Interfaces:**
- Produces: `[Generator] internal sealed class TemplateSurfaceInjector : SurfaceInjectorBase { }` in `namespace Synto.Templating` (scans `Synto.Template.dll`, which embeds only the Template marker surface).
- Produces: `Synto.Template.dll` with SY1005–SY1035 owned here (own AnalyzerReleases).

- [ ] **Step 1: Author the csproj.** `netstandard2.0`, `IsPackable=false`, `Microsoft.CodeAnalysis.Analyzers` (for RS2008) + `Microsoft.CodeAnalysis.CSharp`, `<ProjectReference>` GenKit + Incremental, `<AdditionalFiles>` its own two AnalyzerReleases md, and the Template-only marker embed:
  ```xml
  <EmbeddedResource Include="..\Synto.Core\Templating\**\Surface\*.cs" LogicalName="Synto.Runtime.%(Filename)%(Extension)" />
  <EmbeddedResource Include="..\Synto.Core\Templating\**\InjectedOnly\*.cs" LogicalName="Synto.Runtime.%(Filename)%(Extension)" />
  <EmbeddedResource Include="..\Synto.Core\Templating\**\EmitHelpers\*.cs" LogicalName="Synto.Helper.%(Filename)%(Extension)" />
  ```
  (Confirm the Synto.Core surface folder layout is feature-partitioned under `Templating/`; if markers live in a flat `Synto.Core/**/Surface`, partition the globs by the marker's owning feature so each feature DLL embeds only its own — verify against `ResourceManifestPinTest`.)
- [ ] **Step 2: Move the `Templating/**` subtree** into `src/Synto.Template/` (namespaces unchanged, `Synto.Templating`). Add `TemplateSurfaceInjector.cs`.
- [ ] **Step 3: Split the AnalyzerReleases.** Move the SY1005–SY1035 entries out of the root `src/Synto.Generators/AnalyzerReleases.Unshipped.md` into `src/Synto.Template/AnalyzerReleases.Unshipped.md`; mirror shipped. Leave SY0000/SY1001 with Incremental (Task 11 finalizes ownership) and SY12xx with Match.
- [ ] **Step 4: Wire solution + refs.** Add project to sln; remove Template embed globs from `Synto.Generators.csproj`.
- [ ] **Step 5: Build + guards.** `dotnet build Synto.sln --no-incremental` (0 warnings, RS2008 clean per-assembly), then Template snapshot suite + `LiveTemplateHighlightAnalyzer` tests + `SurfaceInjectionTest` (byte-identical markers, injected once by `TemplateSurfaceInjector`) + `ResourceManifestPinTest` (accept the Template markers moving to Synto.Template's manifest). Update harness generator lists so Template tests run `TemplateSurfaceInjector` + `IncrementalSurfaceInjector`.
- [ ] **Step 6: Commit.** (paths: `src/Synto.Template/ src/Synto.Generators/ Synto.sln test/Synto.Test/`)

### Task 8: Carve `Synto.Match`

**Files:**
- Create: `src/Synto.Match/Synto.Match.csproj`, `MatchSurfaceInjector.cs`, `AnalyzerReleases.{Shipped,Unshipped}.md`
- Move: `src/Synto.Generators/Matching/**` → `src/Synto.Match/` (`MatchFactorySourceGenerator`, `Diagnostics/{MatchDiagnostics.cs (SY1201+), MatchTrackingNames.cs}`, `Discovery/MatchMarkers.cs`, `Emit/**`)
- Assign `src/Synto.Generators/Symbols/SymbolMetadataExtensions.cs`: probe its referencing projects first — if Match-only → move to Synto.Match; if used by Template too → move to `Synto.GenKit` (shared, dep-free).
- Modify: `Synto.Generators.csproj` (drop Match marker embed), `Synto.sln`
- Test: Match snapshot suite, `SurfaceInjectionTest`, `ResourceManifestPinTest`

**Interfaces:**
- Produces: `[Generator] internal sealed class MatchSurfaceInjector : SurfaceInjectorBase { }` in `namespace Synto.Matching` (scans `Synto.Match.dll`, embeds only Match markers).

- [ ] **Step 1: Probe `SymbolMetadataExtensions` usage** (grep its refs across projects) and record its home (Match-only → Synto.Match; cross-feature → GenKit).
- [ ] **Step 2: Author csproj** (mirror Task 7's shape, Match marker embed globs, own AnalyzerReleases with SY1201+).
- [ ] **Step 3: Move `Matching/**`** + `SymbolMetadataExtensions.cs` per Step 1; add `MatchSurfaceInjector.cs`.
- [ ] **Step 4: Split AnalyzerReleases** — SY12xx → `src/Synto.Match/AnalyzerReleases.Unshipped.md`.
- [ ] **Step 5: Build + guards** (0 warnings, Match snapshots byte-identical, `SurfaceInjectionTest` markers injected once by `MatchSurfaceInjector`, `ResourceManifestPinTest` accept Match-marker move). Update Match harness generator list.
- [ ] **Step 6: Commit.** (paths: `src/Synto.Match/ src/Synto.Generators/ Synto.sln test/Synto.Test/`)

### Task 9: Convert `Synto.Diagnostics` package → feature DLL

**Files:**
- Modify: `src/Synto.Diagnostics/Synto.Diagnostics.csproj` (drop `IsSyntoGeneratorPackage`, `developmentDependency`, `PackageReadmeFile`, its `None … PackagePath`; set `IsPackable=false`; ref GenKit + Incremental)
- Create: `src/Synto.Diagnostics/DiagnosticsSurfaceInjector.cs`, `src/Synto.Diagnostics/AnalyzerReleases.{Shipped,Unshipped}.md` only if it ships SY/analyzer-tracked descriptors (its IDs are `SDG*`, and it does NOT reference `Microsoft.CodeAnalysis.Analyzers` today, so RS2008 does not apply — no AnalyzerReleases needed; confirm)
- Modify: `src/Synto.Diagnostics/DiagnosticsGenerator.cs` (its marker embed + injector wiring)
- Test: `test/Synto.Diagnostics.Test`, `SurfaceInjectionTest`

**Interfaces:**
- Produces: `[Generator] internal sealed class DiagnosticsSurfaceInjector : SurfaceInjectorBase { }` injecting ONLY the `[Diagnostic]` marker surface (`DiagnosticsAttribute`), disjoint from trio + Template + Match markers.
- Produces: `Synto.Diagnostics.dll` is bundled by the umbrella, no longer its own nupkg.

- [ ] **Step 1: De-package the csproj.** Remove `IsSyntoGeneratorPackage`, `developmentDependency`, `PackageReadmeFile`, and the `Synto.Diagnostics.NuGet.md` `None` item. Add `IsPackable=false`, `<ProjectReference>` GenKit + Incremental (Incremental already added in Task 5).
- [ ] **Step 2: Give it its own injector + marker embed.** Add `DiagnosticsSurfaceInjector.cs`; embed the `[Diagnostic]` marker surface under `Synto.Runtime.*` from its own source (or from `Synto.Core/Diagnostics/**/Surface` if that is where the marker is authored — confirm where `DiagnosticsAttribute` surface lives).
- [ ] **Step 3: Build + test.** `dotnet build Synto.sln --no-incremental` (0 warnings; confirm no `Synto.Diagnostics.nupkg` is produced on Release). `dotnet test test/Synto.Diagnostics.Test --filter-class Synto.Diagnostics.Test.DiagnosticsGeneratorTest`. Expected: PASS byte-identical.
- [ ] **Step 4: Commit.** (paths: `src/Synto.Diagnostics/ test/Synto.Diagnostics.Test/`)

---

## Phase 4 — Umbrella pack + retire the `Synto.Diagnostics` package

### Task 10: Repurpose `Synto.Generators.csproj` as the `Synto` umbrella

**Files:**
- Modify: `src/Synto.Generators/Synto.Generators.csproj` (becomes umbrella), `Directory.Build.targets` (multi-assembly pack + resolution)
- Delete (if fully emptied): any now-empty `Synto.Generators` folders; the residual `SurfaceInjectionGenerator.cs` concrete subclass is removed if the umbrella carries no `[Generator]` of its own (the trio injector lives in Incremental; each feature has its own).

**Interfaces:**
- Produces: the `Synto` nupkg bundling `Synto.GenKit.dll`, `Synto.Incremental.dll`, `Synto.Template.dll`, `Synto.Match.dll`, `Synto.Diagnostics.dll`, `Synto.Core.dll` under `analyzers/dotnet/cs`.

- [ ] **Step 1: Decide the umbrella body.** After Tasks 7–9, `Synto.Generators` has no generator code left except the concrete `SurfaceInjectionGenerator` subclass. Since the trio injector now lives in Incremental and each feature injects its own markers, the old `SurfaceInjectionGenerator` (which scanned Synto.Generators.dll's now-empty `Synto.Runtime.*` set) is dead — DELETE it. `Synto.Generators.csproj` keeps `PackageId=Synto` + `IsSyntoGeneratorPackage=true` and becomes a reference-only umbrella:
  ```xml
  <ItemGroup>
    <ProjectReference Include="..\Synto.GenKit\Synto.GenKit.csproj" />
    <ProjectReference Include="..\Synto.Incremental\Synto.Incremental.csproj" />
    <ProjectReference Include="..\Synto.Template\Synto.Template.csproj" />
    <ProjectReference Include="..\Synto.Match\Synto.Match.csproj" />
    <ProjectReference Include="..\Synto.Diagnostics\Synto.Diagnostics.csproj" />
  </ItemGroup>
  ```
  (Optional cleaner end-state: rename the project folder to `src/Synto/Synto.csproj`. Lower churn to keep the `Synto.Generators` folder as the umbrella anchor — flag the rename as a reviewer option; default: keep the folder, it already owns `PackageId=Synto`.)
- [ ] **Step 2: Extend the pack (correction (b) — pack + resolution are two mechanisms).** In `Directory.Build.targets`:
  - Extend the `None … PackagePath="analyzers/dotnet/cs"` item group (`:44-46`) to pack every referenced feature DLL, not just `$(AssemblyName).dll` + `Synto.Core.dll`. Use the resolved reference paths of the umbrella's `ProjectReference`s (e.g. an item group over `@(ReferencePath)` filtered to the Synto.* feature assemblies, or explicit `$(OutputPath)Synto.GenKit.dll` … lines).
  - Extend `SyntoGetDependencyTargetPaths` (`:54-60`) to add EACH bundled DLL as a `TargetPathWithTargetPlatformMoniker` so Roslyn resolves all of them at analyzer load (today it adds only `Synto.Core.dll`).
- [ ] **Step 3: Pack + inspect.** Run: `dotnet pack src/Synto.Generators/Synto.Generators.csproj -c Release`. Unzip the produced `Synto.*.nupkg` and confirm `analyzers/dotnet/cs/` contains all six DLLs (GenKit, Incremental, Template, Match, Diagnostics, Core) and NO separate `Synto.Diagnostics.nupkg` was produced. Confirm no NU5128/pack-analysis errors (NoPackageAnalysis already set).
- [ ] **Step 4: End-to-end consumer smoke (CS0101 proof).** Build the example/consumer that references the packed `Synto` package (or an in-repo consumer compilation running all injectors) and confirm: the trio is injected exactly once, all feature markers present, ZERO CS0101 / CS0436. Reuse `examples/` if it references the package.
- [ ] **Step 5: Commit.** (paths: `src/Synto.Generators/ Directory.Build.targets`)

### Task 11: Guards-green sweep + AnalyzerReleases finalization

**Files:**
- Modify: whichever `AnalyzerReleases.*.md` still need per-assembly correctness (Incremental owns SY0000/SY1001; Template SY10xx; Match SY12xx; the root `src/Synto.Generators/AnalyzerReleases.*.md` is deleted or emptied since the umbrella ships no descriptors)
- Test: full suite

**Interfaces:**
- Produces: the whole solution green, byte-identical, 0 warnings, with per-assembly RS2008 correctness.

- [ ] **Step 1: Finalize AnalyzerReleases ownership.** Move SY0000/SY1001 into `src/Synto.Incremental/AnalyzerReleases.Unshipped.md` (add `Microsoft.CodeAnalysis.Analyzers` ref + `<AdditionalFiles>` there — Incremental already refs Analyzers per Task 4). Delete the now-empty root `src/Synto.Generators/AnalyzerReleases.*.md`. Confirm each generator DLL that declares SY descriptors has its OWN complete, RS2008-clean release files.
- [ ] **Step 2: Full clean build.** `dotnet build Synto.sln --no-incremental`. Expected: 0 warnings, 0 errors, no RS2008.
- [ ] **Step 3: Run the full guard set.**
  - `dotnet test test/Synto.Test` (all suites — snapshots byte-identical, ResourceManifestPinTest(s) green, SurfaceInjectionTest green)
  - `dotnet test test/Synto.Diagnostics.Test`
  Expected: all PASS, no unaccepted `.received.*`.
- [ ] **Step 4: Commit.** (paths: the AnalyzerReleases files touched)

---

## End-to-end Verification

1. **Clean build:** `dotnet build Synto.sln --no-incremental` → 0 warnings, 0 errors, no RS2008.
2. **Byte-identical output gate:** `SurfaceInjectionTest` + all Template/Match/Diagnostics snapshot suites pass with no content diffs — only resource-manifest moves were accepted. This proves the restructure changed structure, not behavior.
3. **Single-injector / CS0101 proof:** a consumer compilation running all injectors (Task 10 Step 4) gets the trio exactly once, every feature marker present, zero CS0101/CS0436.
4. **Per-assembly RS2008:** each generator DLL owns complete `AnalyzerReleases` for its SY descriptors; root release files gone.
5. **Single-package:** `dotnet pack` produces ONE `Synto` nupkg with all six DLLs under `analyzers/dotnet/cs`; no `Synto.Diagnostics` nupkg.
6. **Compile-enforced boundaries:** `Synto.Match` cannot reference `Synto.Template` internals (no project reference) and vice versa; both go through `GenKit` (base) + `Incremental` (trio).

## Non-goals / deferred (spec §145, §154)

- **`Synto.Model` DLL is NOT created here** — the `[SyntoModel]` plan creates it as a feature DLL. Do not scaffold an empty project.
- **`DiagnosticBag` / `Drain` / `ToEquatableArray` are NOT added here** — they do not exist yet. This restructure establishes `Synto.Incremental` as their HOME; the repointed cacheable-diagnostics and `[SyntoModel]` plans author them into `Synto.Incremental`.
- **Injected trio namespace stays `Synto.Generators`** (moving to a neutral `Synto` namespace is deferred churn).
- **Multi-package à-la-carte delivery** is explicitly deferred (reversible — a future split ships `Synto.Incremental.dll` once, shared transitively, still one trio injector).
- **`FacadeGenerationResult` / `QuoterGenerationResult` are NOT folded** into the shared Emit base (different shape / different assembly).
- **GenKit name/scope** defaulted to broad `Synto.GenKit` (injection mechanism + shared dep-free helpers); internal DLL, rename is cheap if a reviewer prefers narrow `Synto.Injection`.

## Self-review notes

- **Spec coverage:** Delivery=ONE nupkg (Tasks 10–11); one DLL per part (Tasks 1–9); GenKit bottom w/ base+rewriter+helpers (Tasks 1–3); Incremental trio + single injector + consolidate dup + Emit base (Tasks 4–6); feature DLLs w/ own injector + own markers + own AnalyzerReleases (Tasks 7–9); umbrella multi-assembly pack + retire Synto.Diagnostics package (Tasks 10–11). Migration order matches spec §111 (GenKit → Incremental+consolidate → split features → umbrella+retire → guards green).
- **Corrections absorbed:** (a) dup includes `DiagnosticInfo.cs` + a remarks block, consolidation deletes the Synto.Diagnostics copies leaving injected snapshot untouched (Task 5); (b) pack + resolution are two separate mechanisms, extend BOTH (Task 10 Step 2); (c) `PublicToInternalRewriter` has a mirror in `HelperResourceLoader.cs`, folded (Task 2); helpers split across Core/Generators, all SAFE-TO-MOVE, Bootstrap gains a GenKit ref (Task 3).
- **Byte-identical is the through-line:** every move task re-runs `SurfaceInjectionTest` + the relevant snapshot suite and accepts ONLY resource-manifest moves in `ResourceManifestPinTest`.

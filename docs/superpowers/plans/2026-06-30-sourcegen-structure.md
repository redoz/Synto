# Reorganize Synto.SourceGenerator into a managed folder + namespace structure

## Context

`src/Synto.SourceGenerator` grew organically into a flat layout: every file declares a single
`namespace Synto;` ("fake folder-only org"), and the `Templating/` folder alone holds ~52 files in
one directory. This is hard to navigate and the folders carry no compile-time meaning. Because the
generator project is **not user-facing** (consumers only see the injected internal marker surface, not
these types), there is no consumer-surface cost to giving it real namespaces — and real namespaces buy
compile-time enforcement of the subsystem boundaries.

Decisions (confirmed with the user):
- **Per-subsystem namespaces** under root `Synto.Generators.*`: `Synto.Generators` (shared infra),
  `Synto.Generators.Templating`, `Synto.Generators.Matching`. Subfolders *inside* a subsystem are
  organizational only (same namespace) — so the bulk edit is uniform per subsystem.
- **Whole project in one pass** (Templating + Matching + shared root files).

Why `Synto.Generators.*` is the lucky choice: it nests under `Synto`, so C# outward name-lookup keeps
generator files' implicit access to runtime `Synto.*` types and to `Synto.Generators` shared types
with **no new usings**. Churn is therefore concentrated in the **test project** (`Synto.Test` nests
under `Synto`, not `Synto.Generators`, so its direct references to *moved* generator-internal types need
an explicit `using`).

## Load-bearing constraint — two files MUST stay `namespace Synto;`

`EquatableArray.cs` and `DiagnosticInfo.cs` are **dual-role**: compiled into the generator assembly
*and* embedded as shift-group resources (`Synto.RuntimeGenerated.*`) that `SurfaceInjectionGenerator`'s
`NamespaceShiftRewriter` retargets `Synto` → `Synto.Generators` at injection
(`SurfaceInjectionGenerator.cs:214`, csproj `:86-89`). `Synto.Test` references the generator with
`OutputItemType="Analyzer"` **and** `ReferenceOutputAssembly="True"` (`Synto.Test.csproj:81`), so its
compilation holds **both** the internal copy and the injected copy. The shift keeps them at distinct
FQNs (`Synto.*` vs `Synto.Generators.*`); collapsing the internal copy to `Synto.Generators` would
reintroduce a CS0436 duplicate. **Therefore these two files keep `namespace Synto;`** — they move into
`Shared/` physically, and only their csproj `<EmbeddedResource Include=...>` paths change to `Shared\…`.
The exception is documented in the csproj block above those Includes — NOT as an in-file comment, because
the resource is injected verbatim through `NamespaceShiftRewriter` (which round-trips trivia), so any
added in-file comment would leak into the byte-identical injected-surface snapshots. The shift machinery
and `Synto.RuntimeGenerated.` group stay exactly as-is. Injected snapshots stay byte-identical (verified against
`SurfaceInjectionTest.VerifyInjectedSurface#EquatableArray.g.verified.cs` → `namespace Synto.Generators;`).

## Target structure

```
src/Synto.SourceGenerator/
├── Shared/                         (namespace Synto.Generators; EXCEPT the 2 dual-role files)
│   ├── Diagnostics.cs              → namespace Synto.Generators
│   ├── SymbolMetadataExtensions.cs → namespace Synto.Generators
│   ├── SurfaceInjectionGenerator.cs→ namespace Synto.Generators
│   ├── EquatableArray.cs           → KEEP namespace Synto  (dual-role, see above)
│   └── DiagnosticInfo.cs           → KEEP namespace Synto  (dual-role, see above)
├── Templating/                     (every file → namespace Synto.Generators.Templating)
│   ├── Pipeline/      Template{FactorySourceGenerator,BuildContext,FactoryBuilder,DocumentBuilder,
│   │                  GenerationResult,Info,Validator,Scope,ScopedWalker}
│   ├── Discovery/     *Finder.cs (all 14) + DecorationMarkers
│   ├── Analysis/      BindingTimeClassifier, StagedLivenessAnalysis, BranchPruneIdentifier
│   ├── Staging/       StagedRegionEmitter, StagedScaffoldBuilder, StagedEmitContext,
│   │                  StagedHelperCallFactory, StagedRegion, RootRenameRewriter, SpliceMemberGeneratorEmitter
│   ├── Quoting/       TemplateQuoterFactory, TemplateSyntaxQuoterInvoker, InterpolationFold
│   │   └── Middleware/  Replacement, Trim, Decoration, InterpolationFold, MemberSegment (the existing folder)
│   ├── Builders/      Facade{Shape,CallFinder→Discovery,ArgumentBinder}, SyntaxBuilder{FacadeGenerator,Registry}, BuilderCallModel
│   ├── Lifting/       ValueLift
│   ├── Helpers/       FileLocalHelpers, HelperResourceLoader
│   └── Diagnostics/   TemplateDiagnostics, TrackingNames
└── Matching/                       (every file → namespace Synto.Generators.Matching)
    ├── Pipeline/      MatchFactorySourceGenerator, MatchInfo, MatchGenerationResult
    ├── Emit/          MatchComposer, MatchEmitter, MatchNodeWalker, MatchRunAligner, MatchEmitModel
    └── Discovery/     MatchMarkers   (+ Diagnostics/: MatchDiagnostics, MatchTrackingNames)
```
(`*CallFinder.cs` matches the `*Finder → Discovery` rule; `FacadeShape`/`ArgumentBinder` go to `Builders`.)

## Execution (4 gated stages, in the jj working copy — off `main`)

**A. Folders + moves** — `git mv` each `.cs` into its target subfolder by name-pattern (rules above).
The `Middleware/` folder moves wholesale under `Quoter/`. No namespace change yet → `dotnet build
src/Synto.SourceGenerator/Synto.SourceGenerator.csproj` stays green. Assert zero `.cs` left directly in
`Templating/` and `Matching/` roots.

**B. Namespaces** — bulk rewrite the single `namespace Synto;` line per subsystem:
Templating files → `namespace Synto.Generators.Templating;`, Matching files →
`namespace Synto.Generators.Matching;`, the 3 non-dual-role shared files → `namespace Synto.Generators;`.
**Exclude `Shared/EquatableArray.cs` and `Shared/DiagnosticInfo.cs`.** Rebuild the generator — expected
green with ~zero new usings (outward lookup). Add usings only where the compiler flags CS0246.

**C. csproj** — update the two moved embedded-resource Includes to `Shared\EquatableArray.cs` /
`Shared\DiagnosticInfo.cs` (keep their `Synto.RuntimeGenerated.` LogicalNames). Nothing else in the
csproj moves (other resources point at `..\Synto`). Rebuild generator.

**D. Tests** — `dotnet test --project test/Synto.Test/Synto.Test.csproj` (raw dotnet, MTP — not via rtk).
For each CS0246 in the test build, add the matching `using Synto.Generators[.Templating|.Matching];`
(known sites: `SurfaceInjectionTest` → `Synto.Generators`; `MatchDiagnosticsTests` →
`Synto.Generators.Matching` for `MatchTrackingNames`; templating cacheability tests → `Synto.Generators.Templating`
for `TrackingNames`). `PipelineEquatabilityTests` needs **no** change (EquatableArray/DiagnosticInfo stay `Synto`).

## Verification

- `dotnet build src/Synto.SourceGenerator/Synto.SourceGenerator.csproj` green after each of A/B/C.
- Full suite green: `dotnet test --project test/Synto.Test/Synto.Test.csproj` (and `Synto.Bootstrap.Test`,
  `Synto.Diagnostics.Test` unaffected — separate projects with their own copies of these types).
- **No snapshot re-accepts**: the injected surface is byte-identical (shift untouched, 2 files keep `Synto`),
  and generated factory output references runtime namespaces, not generator internals. Any `.received.cs`
  appearing is a regression to investigate, not to accept.
- Spot-check: `git log --follow` on a moved file shows history preserved (git mv).

## Out of scope

`src/Synto.Bootstrap` (self-contained, its own `EquatableArray`/`DiagnosticInfo`/`TrackingNames`) and
`src/Synto.Diagnostics` are untouched. No code behavior changes — pure structure/namespace.

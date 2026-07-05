---
type: Project
title: Synto.Generators
description: The primary Roslyn generators — templates, matching, syntax-builder facades, and marker surface injection. Assembly Synto.Generators, ships as the Synto package.
resource: src/Synto.Generators
layer: generator
depends_on: [Synto.Core, Microsoft.CodeAnalysis.CSharp]
emits: [template factory methods, match matchers, syntax-builder facades, injected marker surface]
tags: [generator, templating, matching, incremental]
timestamp: 2026-07-05T00:00:00Z
---

# Responsibility

The consumer-facing generator package (assembly/project **`Synto.Generators`**,
folder was `src/Synto.SourceGenerator` before the 2026-07-05 reorg; it still ships
as the `Synto` package). Reads `[Template]`, `[Match<T>]`, and `[SyntaxBuilder]`
and emits the factory/matcher code, plus injects the internal marker surface post-init.

Its tree follows **folder = namespace** under `Synto.Generators.*`, with the injected
generator-author surface (`EquatableArray`, `DiagnosticInfo` under `Incremental/`) as the
`Synto.Generators.*` exception. **Feature passes live with their feature** (builder passes
under `Builders/`, decoration passes under `Templating/Decorations/`); only core passes
stay in `Templating/Quoting/Passes/`. See [layering](/architecture/layering.md).

# Key files

- `Templating/TemplateFactorySourceGenerator.cs:21` — `[Template]` → factory; thin
  `[Generator]` shell (`Initialize` `:23`) over the [templating pipeline](/subsystems/templating.md).
- `Templating/Emit/TemplateFactoryBuilder.cs:30` (ordered build steps; `Build` `:32`),
  `Templating/Emit/TemplateBuildContext.cs:24` (accumulator), `Templating/Emit/TemplateDocumentBuilder.cs:21`
  (assembly; `Build` `:23`), `Templating/Emit/TemplateValidator.cs:16`, `Templating/Lifting/ValueLift.cs:18`,
  `Templating/Staging/SpliceMemberGeneratorEmitter.cs:15` — the carved templating core.
- `Templating/Discovery/*Finder.cs`, `Templating/BindingTime/BindingTimeClassifier.cs:102`,
  `Templating/Emit/TemplateScope.cs:24`, `Templating/Quoting/TemplateQuoterFactory.cs:16` +
  `Templating/Quoting/PerNodePipelineQuoter.cs:19` (+ `Templating/Quoting/Passes/`,
  `Templating/Quoting/InterpolationFold.cs:18`), and the staging family
  (`Templating/Staging/StagedRegionEmitter.cs:22`, `Templating/Discovery/StagedRegionFinder.cs:13`,
  `Templating/Staging/StagedLivenessAnalysis.cs:12`, `Templating/Staging/StagedEmitContext.cs:17`,
  `Templating/Staging/StagedScaffoldBuilder.cs:20`) — the templating machinery.
- `Templating/Builders/FacadeCallFinder.cs:22` / `Templating/Builders/SyntaxBuilderRegistry.cs:12` /
  `Templating/Builders/FacadeArgumentBinder.cs:15` / `Templating/Builders/BuilderCallModel.cs` —
  syntax-builder facade resolution.
- `Templating/Diagnostics/TemplateDiagnostics.cs:7` (SY1005–SY1034), `Incremental/Diagnostics.cs:9`
  (shared SY0000/SY1001–4), `Symbols/SymbolMetadataExtensions.cs:10` (shared attribute reads).
- `Matching/MatchFactorySourceGenerator.cs:23` — `[Match<T>]` → matcher; emit decomposed
  into `MatchEmitter`/`MatchNodeWalker`/`MatchRunAligner`/`MatchComposer` (under `Matching/Emit/`). See
  [matching](/subsystems/matching.md).
- `Templating/Builders/SyntaxBuilderFacadeGenerator.cs:26` — `[SyntaxBuilder]` → facades.
- `SurfaceInjectionGenerator.cs:59` (generator root) — post-init injection of the internal markers;
  `Templating/Prelude/FileLocalHelpers.cs:34` — the `file`-scoped helper registry it scans against.

# Entry points

Four `[Generator]` classes (see the [incremental pipeline](/architecture/incremental-pipeline.md)
table). The big one is `TemplateFactorySourceGenerator`.

# Invariants

- References the [runtime](/projects/synto-runtime.md) (`Synto.Core`) with `PrivateAssets="all"`.
- Must honor the [cacheability contract](/architecture/incremental-pipeline.md):
  equatable result records, every stage tracking-named.
- Independent of the other generators — no generator → generator edges.

# Related

- [Templating](/subsystems/templating.md) · [Matching](/subsystems/matching.md)
- [Incremental pipeline](/architecture/incremental-pipeline.md)

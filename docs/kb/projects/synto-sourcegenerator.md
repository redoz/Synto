---
type: Project
title: Synto.SourceGenerator
description: The primary Roslyn generators — templates, matching, syntax-builder facades, and marker surface injection.
resource: src/Synto.SourceGenerator
layer: generator
depends_on: [Synto, Microsoft.CodeAnalysis.CSharp]
emits: [template factory methods, match matchers, syntax-builder facades, injected marker surface]
tags: [generator, templating, matching, incremental]
timestamp: 2026-06-29T00:00:00Z
---

# Responsibility

The consumer-facing generator package. Reads `[Template]`, `[Match<T>]`, and
`[SyntaxBuilder]` and emits the factory/matcher code, plus injects the internal
marker surface post-init.

# Key files

- `Templating/TemplateFactorySourceGenerator.cs:17` — `[Template]` → factory; thin
  `[Generator]` shell (~83 LOC after the 2026-06-29 decomposition) over the
  [templating pipeline](/subsystems/templating.md).
- `Templating/TemplateFactoryBuilder.cs:24` (ordered build steps), `TemplateBuildContext.cs`
  (accumulator), `TemplateDocumentBuilder.cs:22` (assembly), `TemplateValidator.cs:18`,
  `ValueLift.cs:16`, `SpliceMemberGeneratorEmitter.cs:24` — the carved templating core.
- `Templating/*Finder.cs`, `BindingTimeClassifier.cs`, `TemplateScope.cs`,
  `TemplateSyntaxQuoter.cs:30` (+ `InterpolationFold.cs`), and the staging family
  (`StagedRegionEmitter.cs:20`, `StagedRegionFinder.cs`, `StagedLivenessAnalysis.cs`,
  `StagedEmitContext.cs`, `StagedScaffoldBuilder.cs:19`) — the templating machinery.
- `Templating/FacadeCallFinder.cs:19` / `SyntaxBuilderRegistry.cs:17` / `FacadeArgumentBinder.cs:16`
  / `BuilderCallModel.cs` — syntax-builder facade resolution.
- `Templating/TemplateDiagnostics.cs:6` (SY1005–1021), `Diagnostics.cs` (shared SY0000/SY1001–4),
  `SymbolMetadataExtensions.cs:9` (shared attribute reads).
- `Matching/MatchFactorySourceGenerator.cs:20` — `[Match<T>]` → matcher; emit decomposed
  into `MatchEmitter`/`MatchNodeWalker`/`MatchRunAligner`/`MatchComposer`. See
  [matching](/subsystems/matching.md).
- `Templating/SyntaxBuilderFacadeGenerator.cs:20` — `[SyntaxBuilder]` → facades.
- `SurfaceInjectionGenerator.cs:58` — post-init injection of the internal markers.

# Entry points

Four `[Generator]` classes (see the [incremental pipeline](/architecture/incremental-pipeline.md)
table). The big one is `TemplateFactorySourceGenerator`.

# Invariants

- References the [runtime](/projects/synto-runtime.md) with `PrivateAssets="all"`.
- Must honor the [cacheability contract](/architecture/incremental-pipeline.md):
  equatable result records, every stage tracking-named.
- Independent of the other generators — no generator → generator edges.

# Related

- [Templating](/subsystems/templating.md) · [Matching](/subsystems/matching.md)
- [Incremental pipeline](/architecture/incremental-pipeline.md)

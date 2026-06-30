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

- `Templating/Pipeline/TemplateFactorySourceGenerator.cs:17` — `[Template]` → factory; thin
  `[Generator]` shell (~83 LOC after the 2026-06-29 decomposition) over the
  [templating pipeline](/subsystems/templating.md).
- `Templating/Pipeline/TemplateFactoryBuilder.cs:24` (ordered build steps), `Templating/Pipeline/TemplateBuildContext.cs`
  (accumulator), `Templating/Pipeline/TemplateDocumentBuilder.cs:22` (assembly), `Templating/Pipeline/TemplateValidator.cs:18`,
  `Templating/Lifting/ValueLift.cs:16`, `Templating/Staging/SpliceMemberGeneratorEmitter.cs:24` — the carved templating core.
- `Templating/Discovery/*Finder.cs`, `Templating/Analysis/BindingTimeClassifier.cs`, `Templating/Pipeline/TemplateScope.cs`,
  `Templating/Quoting/TemplateQuoterFactory.cs:19` + `Templating/Quoting/Middleware/` (+ `Templating/Quoting/InterpolationFold.cs`), and the staging family
  (`Templating/Staging/StagedRegionEmitter.cs:20`, `Templating/Discovery/StagedRegionFinder.cs`, `Templating/Analysis/StagedLivenessAnalysis.cs`,
  `Templating/Staging/StagedEmitContext.cs`, `Templating/Staging/StagedScaffoldBuilder.cs:19`) — the templating machinery.
- `Templating/Discovery/FacadeCallFinder.cs:19` / `Templating/Builders/SyntaxBuilderRegistry.cs:17` / `Templating/Builders/FacadeArgumentBinder.cs:16`
  / `Templating/Builders/BuilderCallModel.cs` — syntax-builder facade resolution.
- `Templating/Diagnostics/TemplateDiagnostics.cs:6` (SY1005–1021), `Shared/Diagnostics.cs` (shared SY0000/SY1001–4),
  `Shared/SymbolMetadataExtensions.cs:9` (shared attribute reads).
- `Matching/Pipeline/MatchFactorySourceGenerator.cs:20` — `[Match<T>]` → matcher; emit decomposed
  into `MatchEmitter`/`MatchNodeWalker`/`MatchRunAligner`/`MatchComposer`. See
  [matching](/subsystems/matching.md).
- `Templating/Builders/SyntaxBuilderFacadeGenerator.cs:20` — `[SyntaxBuilder]` → facades.
- `Shared/SurfaceInjectionGenerator.cs:58` — post-init injection of the internal markers.

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

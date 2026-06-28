---
type: Project
title: Synto.SourceGenerator
description: The primary Roslyn generators — templates, matching, syntax-builder facades, and marker surface injection.
resource: src/Synto.SourceGenerator
layer: generator
depends_on: [Synto, Microsoft.CodeAnalysis.CSharp]
emits: [template factory methods, match matchers, syntax-builder facades, injected marker surface]
tags: [generator, templating, matching, incremental]
timestamp: 2026-06-28T00:00:00Z
---

# Responsibility

The consumer-facing generator package. Reads `[Template]`, `[Match<T>]`, and
`[SyntaxBuilder]` and emits the factory/matcher code, plus injects the internal
marker surface post-init.

# Key files

- `Templating/TemplateFactorySourceGenerator.cs:17` — `[Template]` → factory; the
  orchestrator of the [templating pipeline](/subsystems/templating.md) (~28 stages).
- `Templating/*Finder.cs`, `BindingTimeClassifier.cs`, `StagedRegionEmitter.cs`,
  `TemplateScope.cs`, `TemplateSyntaxQuoter.cs` — the templating machinery.
- `Matching/MatchFactorySourceGenerator.cs:20` — `[Match<T>]` → matcher. See
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

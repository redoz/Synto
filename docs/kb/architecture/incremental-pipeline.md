---
type: Architecture
title: Incremental pipeline & cacheability contract
description: The IIncrementalGenerator shape, tracking names, and why only equatable structs flow through cached state.
tags: [incremental, cacheability, roslyn, performance]
status: stable
relates: [projects/synto-sourcegenerator, subsystems/templating, subsystems/matching]
timestamp: 2026-06-28T00:00:00Z
---

# Responsibility

Capture the rules every Synto generator obeys so its pipeline stays cacheable —
the contract that makes edits cheap and the tests that enforce it.

# The shape

All Synto generators are `IIncrementalGenerator`s with an
`Initialize(IncrementalGeneratorInitializationContext)`. There are six
`[Generator]` entry points across the repo:

| Generator | File:Line | Trigger |
|-----------|-----------|---------|
| `TemplateFactorySourceGenerator` | `src/Synto.SourceGenerator/Templating/TemplateFactorySourceGenerator.cs:17` | `[Template]` (`ForAttributeWithMetadataName`) |
| `MatchFactorySourceGenerator` | `src/Synto.SourceGenerator/Matching/MatchFactorySourceGenerator.cs:20` | `[Match<T>]` |
| `SyntaxBuilderFacadeGenerator` | `src/Synto.SourceGenerator/Templating/SyntaxBuilderFacadeGenerator.cs:20` | `[SyntaxBuilder]` |
| `SurfaceInjectionGenerator` | `src/Synto.SourceGenerator/SurfaceInjectionGenerator.cs:58` | post-init marker injection |
| `DiagnosticsGenerator` | `src/Synto.Diagnostics/DiagnosticsGenerator.cs:17` | `[Diagnostic]` |
| `CSharpSyntaxQuoterGenerator` | `src/Synto.Bootstrap/CSharpSyntaxQuoterGenerator.cs:15` | the `CSharpSyntaxQuoter` class (`CreateSyntaxProvider`) |

# Cacheability contract

- **Every pipeline stage is tagged** with `.WithTrackingName(...)` from a
  `TrackingNames.cs` (one per generator project, e.g.
  `src/Synto.SourceGenerator/Templating/TrackingNames.cs:8`). Tests assert each
  tracked step is `Cached`/`Unchanged` on unrelated edits.
- **Only immutable, structurally-equatable values cross cached boundaries** —
  never a `SyntaxNode`, `SemanticModel`, or `Compilation`. The enablers:
  - `EquatableArray<T>` — content-equality array wrapper
    (`src/Synto.SourceGenerator/EquatableArray.cs:12`); nested by all result records.
  - `DiagnosticInfo` / `LocationInfo` — record structs serializing diagnostics to
    primitives (`src/Synto.SourceGenerator/DiagnosticInfo.cs:10`); the live
    `Diagnostic` is materialized only at emit time.
  - Result records (`TemplateGenerationResult`, `MatchGenerationResult`,
    `DiagnosticGenerationResult`, `QuoterGenerationResult`) hold only equatable fields.
- **All semantic work happens inside the transform**, so what flows downstream is
  already reduced to equatable data.

# Invariants

- A new tracked step **must** get a name in the project's `TrackingNames.cs` and
  an assertion in the incremental test — terminal-only assertions have an
  upstream blind spot. (See the project's cacheability tests; harden to iterate
  **all** steps.)
- Adding a Roslyn object to a result record breaks caching silently — guard it
  with the equatable wrappers above.

# Related

- [Templating subsystem](/subsystems/templating.md) — the largest pipeline.
- [Synto.SourceGenerator](/projects/synto-sourcegenerator.md)

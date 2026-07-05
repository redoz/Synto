---
type: Architecture
title: Runtime vs generator layering
description: The four assemblies, their layers, and the one-way dependency arrow from generators to the runtime.
tags: [layering, packaging, dependencies]
status: stable
relates: [projects/synto-runtime, projects/synto-sourcegenerator, projects/synto-bootstrap, projects/synto-diagnostics]
timestamp: 2026-07-05T00:00:00Z
---

# Responsibility

Explain *which* assembly does what and *which way* dependencies point, so you
open the right project on the first try. The four `src/*` assemblies (renamed in
the 2026-07-05 reorg — folders now `Synto.Core`, `Synto.Generators`,
`Synto.Bootstrap`, `Synto.Diagnostics`) look alike on disk (each carries its own
`EquatableArray.cs`, `DiagnosticInfo.cs`, `TrackingNames.cs`) but occupy distinct layers.

# The layers

| Assembly (folder) | Ships as | Layer | Role |
|----------|----------|-------|------|
| [Synto.Core](/projects/synto-runtime.md) (`src/Synto.Core`) | `Synto.Core` | `runtime` | Consumer-referenced. Ships the public marker attributes + extension helpers + the quoter machinery. |
| [Synto.Generators](/projects/synto-sourcegenerator.md) (`src/Synto.Generators`) | `Synto` | `generator` | The Roslyn generators that consume `[Template]`/`[Match]`/`[SyntaxBuilder]` and emit factory code. |
| [Synto.Bootstrap](/projects/synto-bootstrap.md) (`src/Synto.Bootstrap`) | *(not shipped)* | `bootstrap` | Build-time only. Generates `CSharpSyntaxQuoter.Generated.cs` for the runtime. See [bootstrap](/architecture/bootstrap.md). |
| [Synto.Diagnostics](/projects/synto-diagnostics.md) (`src/Synto.Diagnostics`) | `Synto.Diagnostics` | `diagnostics` | Generator for `[Diagnostic]` → descriptor declarations. |

# Dependency arrow

Dependencies point **toward the runtime**; the runtime depends on nobody in this
repo:

```
Synto.Generators ──────┐
Synto.Diagnostics ─────┼──▶ Synto.Core (runtime) ──▶ Microsoft.CodeAnalysis.CSharp
Synto.Bootstrap ───────┘    (links a few source files, see bootstrap.md)
```

- The generators reference `Synto.Core.csproj` with `PrivateAssets="all"` — the
  consumer of a generated factory does **not** transitively pick up the runtime
  as a public dependency.
- `Synto.Bootstrap` does not project-reference the runtime; it *links* a few
  source files (`Syntax/EmitHelpers/LiteralSyntaxExtensions.cs`, `Emit/UsingDirectiveSet.cs`,
  `Formatting/SyntaxFormatter.cs`) — see `src/Synto.Bootstrap/Synto.Bootstrap.csproj:12-15`.
  It is also the one sanctioned place that `<Compile Link>`s the generator's canonical
  `Incremental/EquatableArray.cs` + `Incremental/DiagnosticInfo.cs` pair (`:18-19`).

# Invariants

- **No generator → generator dependency.** Each generator is independent and
  talks only to the runtime + Roslyn.
- **Generated factory code must be runtime-free where it matters**: custom-type
  lifts go through `[Runtime]` converters called fully-qualified, so generated
  code does not force a `Synto.Core` dependency on the consumer.
- Packaging/nuget specifics live in `.claude/playbook/architecture.md` — this
  concept stays focused on the code-navigation arrow.

# Related

- [Bootstrap loop](/architecture/bootstrap.md)
- [Incremental pipeline](/architecture/incremental-pipeline.md)

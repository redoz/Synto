---
type: Project
title: Synto.Diagnostics
description: Incremental generator turning [Diagnostic]-annotated members into DiagnosticDescriptor declarations.
resource: src/Synto.Diagnostics
layer: diagnostics
depends_on: [Synto, Microsoft.CodeAnalysis.CSharp]
emits: [DiagnosticDescriptor declarations, injected DiagnosticAttribute marker]
tags: [generator, diagnostics]
timestamp: 2026-06-28T00:00:00Z
---

# Responsibility

A small, self-contained generator: compiles `[Diagnostic]` metadata into
descriptor declarations and injects its own `DiagnosticAttribute` marker post-init.
Independent of the templating/matching generators.

# Key files

- `DiagnosticsGenerator.cs:17` — `[Generator]`; post-inits `DiagnosticAttribute`
  (`:22`), transforms `[Diagnostic]` via `ForAttributeWithMetadataName` →
  `DiagnosticGenerationResult` (`TrackingNames.cs:33-35`).
- `Diagnostics.cs`, `TargetInfo.cs`, `DiagnosticGenerationResult.cs` — the model.

# Entry points

- `DiagnosticsGenerator.cs:17` → `Initialize`.

# Invariants

- References the [runtime](/projects/synto-runtime.md) with `PrivateAssets="all"`.
- Same [cacheability contract](/architecture/incremental-pipeline.md) as the other
  generators (its own `EquatableArray` / `DiagnosticInfo` copies).
- Snapshot tests live in `test/Synto.Diagnostics.Test/`.

# Related

- [Incremental pipeline](/architecture/incremental-pipeline.md) · [Layering](/architecture/layering.md)

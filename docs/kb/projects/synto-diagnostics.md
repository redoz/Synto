---
type: Project
title: Synto.Diagnostics
description: Incremental generator turning [Diagnostic]-annotated members into DiagnosticDescriptor declarations.
resource: src/Synto.Diagnostics
layer: diagnostics
depends_on: [Synto.Core, Microsoft.CodeAnalysis.CSharp]
emits: [DiagnosticDescriptor declarations, injected DiagnosticAttribute marker]
tags: [generator, diagnostics]
timestamp: 2026-07-05T00:00:00Z
---

# Responsibility

A small, self-contained generator: compiles `[Diagnostic]` metadata into
descriptor declarations and injects its own `DiagnosticAttribute` marker post-init.
Independent of the templating/matching generators.

# Key files

- `DiagnosticsGenerator.cs:18` — `[Generator]`; `Initialize` (`:20`) post-inits
  `DiagnosticAttribute` (declared `:58`, registered `:22`), transforms `[Diagnostic]`
  via `ForAttributeWithMetadataName` → `DiagnosticGenerationResult` (`TrackingNames.cs:10-11`).
- `Diagnostics.cs`, `TargetInfo.cs`, `DiagnosticGenerationResult.cs` — the model.

# Entry points

- `DiagnosticsGenerator.cs:20` → `Initialize`.

# Invariants

- References the [runtime](/projects/synto-runtime.md) with `PrivateAssets="all"`.
- Same [cacheability contract](/architecture/incremental-pipeline.md) as the other
  generators (its own `EquatableArray` / `DiagnosticInfo` copies).
- Snapshot tests live in `test/Synto.Diagnostics.Test/`.

# Related

- [Incremental pipeline](/architecture/incremental-pipeline.md) · [Layering](/architecture/layering.md)

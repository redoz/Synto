---
type: Project
title: Synto.Bootstrap
description: Build-time generator that emits the runtime quoter's generated half by quoting a copy of itself.
resource: src/Synto.Bootstrap
layer: bootstrap
depends_on: [Microsoft.CodeAnalysis.CSharp]
emits: [CSharpSyntaxQuoter.Generated.cs]
tags: [bootstrap, codegen, self-hosting]
timestamp: 2026-06-28T00:00:00Z
---

# Responsibility

Runs **only during Synto's own build**. Carries a standalone copy of the quoter
plus the cacheability infra (`EquatableArray`, `DiagnosticInfo`) and generates the
per-`SyntaxKind` `Visit` methods that complete the runtime quoter. The full
mechanism is in [bootstrap loop](/architecture/bootstrap.md).

# Key files

- `CSharpSyntaxQuoterGenerator.cs:15` — `[Generator]`; finds `CSharpSyntaxQuoter`,
  enumerates `CSharpSyntaxVisitor` methods (`:76-95`), emits the partial.
- `CSharpSyntaxQuoter.cs:22` — the bootstrap (input) quoter.
- `CSharpSyntaxQuoter.Generated.cs` — the committed mirror of the generated output.
- `Synto.Bootstrap.csproj:12-15` — *links* source files from the runtime rather
  than project-referencing it.

# Entry points

- `CSharpSyntaxQuoterGenerator.cs:15` → `Initialize`.

# Invariants

- The committed `CSharpSyntaxQuoter.Generated.cs` **must stay in sync** with what
  the generator emits; a snapshot test (`src/Synto/CSharpSyntaxQuoter.cs:11-18`)
  guards drift.
- Ships as an analyzer in the runtime's build, not to consumers.

# Related

- [Bootstrap loop](/architecture/bootstrap.md) · [Quoting](/subsystems/quoting.md)

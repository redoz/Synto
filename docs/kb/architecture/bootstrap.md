---
type: Architecture
title: The bootstrap (self-hosting quoter) loop
description: How CSharpSyntaxQuoter.Generated.cs is produced by a quoter that quotes itself.
tags: [bootstrap, quoting, self-hosting, codegen]
status: stable
relates: [projects/synto-bootstrap, projects/synto-runtime, subsystems/quoting]
timestamp: 2026-06-28T00:00:00Z
---

# Responsibility

Demystify the "what generates what" circularity. The runtime quoter is half
hand-authored and half generated, and the generator is itself a quoter — this is
the most common orientation stumble in the repo.

# The loop

1. The runtime quoter base is hand-authored:
   `CSharpSyntaxQuoterBase : CSharpSyntaxVisitor<ExpressionSyntax>` at
   `src/Synto/CSharpSyntaxQuoter.cs:21`, with the concrete partial at
   `src/Synto/CSharpSyntaxQuoter.cs:204`.
2. The **other half** — a `Visit` method for every Roslyn `SyntaxKind` — is
   generated into `src/Synto/.../generated/.../CSharpSyntaxQuoter.cs` by
   `Synto.Bootstrap`'s generator, then mirrored into the committed
   `src/Synto.Bootstrap/CSharpSyntaxQuoter.Generated.cs`.
3. The generator that emits it, `CSharpSyntaxQuoterGenerator`
   (`src/Synto.Bootstrap/CSharpSyntaxQuoterGenerator.cs:15`), is itself built on
   a standalone copy of the quoter (`src/Synto.Bootstrap/CSharpSyntaxQuoter.cs:22`).
   It finds the `CSharpSyntaxQuoter` class, enumerates every method on
   `CSharpSyntaxVisitor` (`CSharpSyntaxQuoterGenerator.cs:76-95`), and quotes
   the construction of each node kind.

So: **the bootstrap quoter quotes the runtime quoter into existence**, and the
runtime quoter is what every other subsystem ([templating](/subsystems/templating.md),
[matching](/subsystems/matching.md)) builds on.

# Entry points

- `src/Synto.Bootstrap/CSharpSyntaxQuoterGenerator.cs:15` — `[Generator]`,
  `Initialize`.
- `src/Synto.Bootstrap/CSharpSyntaxQuoter.cs:22` — the bootstrap quoter input.

# Invariants

- **The committed `CSharpSyntaxQuoter.Generated.cs` must stay in sync** with what
  the generator would emit. Drift is caught by a snapshot test referenced at
  `src/Synto/CSharpSyntaxQuoter.cs:11-18`. When the runtime quoter's surface
  changes, the generated half is regenerated and re-committed.
- `Synto.Bootstrap` runs **only during Synto's own build** — it is not part of
  the consumer-facing generator set.

# Related

- [Synto.Bootstrap project](/projects/synto-bootstrap.md)
- [Quoting subsystem](/subsystems/quoting.md)

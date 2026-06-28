---
type: Project
title: Synto (runtime)
description: Consumer-referenced runtime — marker attributes, extension helpers, and the CSharpSyntaxQuoter machinery.
resource: src/Synto
layer: runtime
depends_on: [Microsoft.CodeAnalysis.CSharp]
emits: []
tags: [runtime, quoter, attributes]
timestamp: 2026-06-28T00:00:00Z
---

# Responsibility

The assembly consumers reference. Hosts the public marker surface
(`[Template]`, `[Match]`, `[Splice]`, decorations, …), the extension helpers
the generated factories call, and the hand-authored half of the quoter.

# Key files

- `src/Synto/CSharpSyntaxQuoter.cs:21` — `CSharpSyntaxQuoterBase` (the quoter; other
  half is generated, see [bootstrap](/architecture/bootstrap.md)).
- `src/Synto/Templating/` — the template consumer surface: `TemplateAttribute.cs`,
  `Template.Parameter.cs`, `UnquoteAttribute.cs`, `SpliceAttribute.cs`,
  `QuoteAttribute.cs`, `SyntaxBuilderAttribute.cs`, `Syntax.cs`, `SyntoBuilders.cs`,
  plus the [decoration](/subsystems/decorations.md) attributes.
- `src/Synto/Matching/` — the [matching](/subsystems/matching.md) consumer surface.
- `src/Synto/*SyntaxExtensions.cs`, `Formatting/SyntaxFormatter.cs`,
  `UsingDirectiveSet.cs` — the [quoting](/subsystems/quoting.md) helper layer.

# Entry points

- Quoter: `src/Synto/CSharpSyntaxQuoter.cs:204`.
- Lift helper most generated code calls: `.ToSyntax()` in
  `src/Synto/LiteralSyntaxExtensions.cs`.

# Invariants

- Depends on **nothing** in this repo (only Roslyn) — it is the bottom of the
  [dependency arrow](/architecture/layering.md).
- A few source files are *linked* (not referenced) into
  [Synto.Bootstrap](/projects/synto-bootstrap.md) for the build-time quoter.

# Related

- [Quoting subsystem](/subsystems/quoting.md) · [Templating subsystem](/subsystems/templating.md)
- [Layering](/architecture/layering.md)

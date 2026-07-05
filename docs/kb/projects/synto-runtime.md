---
type: Project
title: Synto.Core (runtime)
description: Consumer-referenced runtime — marker attributes, extension helpers, and the CSharpSyntaxQuoter machinery. Ships as the Synto.Core package.
resource: src/Synto.Core
layer: runtime
depends_on: [Microsoft.CodeAnalysis.CSharp]
emits: []
tags: [runtime, quoter, attributes]
timestamp: 2026-07-05T00:00:00Z
---

# Responsibility

The assembly consumers reference (project + package **`Synto.Core`**; the folder
was `src/Synto` before the 2026-07-05 reorg). Hosts the public marker surface
(`[Template]`, `[Match]`, `[Splice]`, decorations, …), the extension helpers the
generated factories call, and the hand-authored half of the quoter.

Its source follows **folder = namespace with two exceptions** — the consumer
surface stays `namespace Synto.*` however deep the role folders (`Surface/`,
`InjectedOnly/`, `EmitHelpers/`) nest. See [layering](/architecture/layering.md).

# Key files

- `src/Synto.Core/Quoting/CSharpSyntaxQuoter.cs:21` — `CSharpSyntaxQuoterBase` (the quoter;
  other half is generated, see [bootstrap](/architecture/bootstrap.md)); concrete partial at `:237`.
- `src/Synto.Core/Templating/` — the template consumer surface, grouped by feature with the
  markers under a `Surface/` leaf: `Definition/Surface/` (`TemplateAttribute.cs`,
  `QuoteAttribute.cs`, `TemplateOption.cs`), `Staging/Surface/` (`Template.cs`,
  `UnquoteAttribute.cs`, `SpliceAttribute.cs`, `RuntimeAttribute.cs`),
  `Builders/Surface/` (`SyntaxBuilderAttribute.cs`, `QuotedAttribute.cs`, `ReturnTypeAttribute.cs`,
  `TypeArgAttribute.cs`) + `Builders/InjectedOnly/SyntoBuilders.cs`, and `Decorations/Surface/`
  ([decoration](/subsystems/decorations.md) markers).
- `src/Synto.Core/Matching/Surface/` + `Matching/InjectedOnly/` — the [matching](/subsystems/matching.md) consumer surface.
- `src/Synto.Core/Syntax/EmitHelpers/*SyntaxExtensions.cs`, `Syntax/SyntaxListExtensions.cs`,
  `Quoting/EmitHelpers/QuoteSyntaxExtensions.cs`, `Formatting/SyntaxFormatter.cs`,
  `Emit/UsingDirectiveSet.cs`, `Symbols/SymbolExtensions.cs` — the [quoting](/subsystems/quoting.md) helper layer.

# Entry points

- Quoter: `src/Synto.Core/Quoting/CSharpSyntaxQuoter.cs:237` (concrete).
- Lift helper most generated code calls: `.ToSyntax()` in
  `src/Synto.Core/Syntax/EmitHelpers/LiteralSyntaxExtensions.cs:8`.

# Invariants

- Depends on **nothing** in this repo (only Roslyn) — it is the bottom of the
  [dependency arrow](/architecture/layering.md).
- A few source files are *linked* (not referenced) into
  [Synto.Bootstrap](/projects/synto-bootstrap.md) for the build-time quoter.

# Related

- [Quoting subsystem](/subsystems/quoting.md) · [Templating subsystem](/subsystems/templating.md)
- [Layering](/architecture/layering.md)

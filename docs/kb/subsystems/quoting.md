---
type: Subsystem
title: Quoting
description: Turn a syntax tree into the ExpressionSyntax that rebuilds it — the foundation every other subsystem stands on.
resource: src/Synto.Core
project: projects/synto-runtime
entrypoints: ["src/Synto.Core/Quoting/CSharpSyntaxQuoter.cs:21", "src/Synto.Core/Quoting/CSharpSyntaxQuoter.cs:237"]
tags: [quoting, quoter, runtime]
timestamp: 2026-07-05T00:00:00Z
---

# Responsibility

Given a Roslyn `SyntaxNode`, produce the `ExpressionSyntax` that, when executed,
reconstructs that node. This is the primitive [templating](/subsystems/templating.md)
and [matching](/subsystems/matching.md) build on.

# Key files

- `src/Synto.Core/Quoting/CSharpSyntaxQuoter.cs:21` — `CSharpSyntaxQuoterBase :
  CSharpSyntaxVisitor<ExpressionSyntax>`; concrete partial at `:237`.
  Per-`SyntaxKind` `Visit` methods are generated — see [bootstrap](/architecture/bootstrap.md).
- `src/Synto.Core/Quoting/CSharpSyntaxQuoter.cs:58` — `Visit<TNode>(SyntaxList<TNode>)` → `List<T>`
  factory calls; `:112` — `Visit(SyntaxToken)` (trivia handling).
- Helper extensions the quoted output and generated factories use:
  - `Syntax/EmitHelpers/LiteralSyntaxExtensions.cs:8` — `.ToSyntax()` for `string`/`bool`/`int`/… literals.
  - `Syntax/EmitHelpers/InterpolationSyntaxExtensions.cs:29` — `.ToInterpolatedText()` (escape for holes).
  - `Syntax/EmitHelpers/CollectionSyntaxExtensions.cs:29` — `BuildList<TNode>()` (fixed nodes + runs).
  - `Syntax/SyntaxListExtensions.cs:9` — `Capture<T>()`.
  - `Quoting/EmitHelpers/QuoteSyntaxExtensions.cs:8` — `OrNullLiteralExpression()`.
- Support: `Formatting/SyntaxFormatter.cs:18` (`Format<T>()`),
  `Emit/UsingDirectiveSet.cs` (dedupe usings),
  `Emit/GeneratedSourceFilenameExtensions.cs:24` (`.g.cs` names).

# Entry points

- `src/Synto.Core/Quoting/CSharpSyntaxQuoter.cs:237` — the concrete quoter.
- Templating extends this via `PerNodePipelineQuoter`
  (`src/Synto.Generators/Templating/Quoting/PerNodePipelineQuoter.cs:19`) — a subclass that
  dispatches per-node / per-owner step pipelines natively at the Node/List seams, built by
  `TemplateQuoterFactory` (see [templating](/subsystems/templating.md)).

# Invariants

- The generated half of the quoter must stay in sync with the hand-authored base
  — guarded by snapshot test (`src/Synto.Core/Quoting/CSharpSyntaxQuoter.cs:11-18`).

# Related

- [Bootstrap loop](/architecture/bootstrap.md) · [Templating](/subsystems/templating.md)

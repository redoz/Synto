---
type: Subsystem
title: Quoting
description: Turn a syntax tree into the ExpressionSyntax that rebuilds it — the foundation every other subsystem stands on.
resource: src/Synto
project: projects/synto-runtime
entrypoints: ["src/Synto/CSharpSyntaxQuoter.cs:21", "src/Synto/CSharpSyntaxQuoter.cs:204"]
tags: [quoting, quoter, runtime]
timestamp: 2026-06-28T00:00:00Z
---

# Responsibility

Given a Roslyn `SyntaxNode`, produce the `ExpressionSyntax` that, when executed,
reconstructs that node. This is the primitive [templating](/subsystems/templating.md)
and [matching](/subsystems/matching.md) build on.

# Key files

- `src/Synto/CSharpSyntaxQuoter.cs:21` — `CSharpSyntaxQuoterBase :
  CSharpSyntaxVisitor<ExpressionSyntax>`; concrete partial at `:204`.
  Per-`SyntaxKind` `Visit` methods are generated — see [bootstrap](/architecture/bootstrap.md).
- `src/Synto/CSharpSyntaxQuoter.cs:58` — `Visit(SyntaxList<TNode>)` → `List<T>`
  factory calls; `:112` — `Visit(SyntaxToken)` (trivia handling).
- Helper extensions the quoted output and generated factories use:
  - `LiteralSyntaxExtensions.cs:8` — `.ToSyntax()` for `string`/`bool`/`int`/… literals.
  - `InterpolationSyntaxExtensions.cs:29` — `.ToInterpolatedText()` (escape for holes).
  - `CollectionSyntaxExtensions.cs:29` — `BuildList<TNode>()` (fixed nodes + runs).
  - `SyntaxListExtensions.cs:9` — `Capture<T>()`.
  - `QuoteSyntaxExtensions.cs:8` — `OrNullLiteralExpression()`.
- Support: `Formatting/SyntaxFormatter.cs:18` (`Format<T>()`),
  `UsingDirectiveSet.cs` (dedupe usings),
  `GeneratedSourceFilenameExtensions.cs:24` (`.g.cs` names).

# Entry points

- `src/Synto/CSharpSyntaxQuoter.cs:204` — the concrete quoter.
- Templating extends this via `TemplateSyntaxQuoter` (see [templating](/subsystems/templating.md)).

# Invariants

- The generated half of the quoter must stay in sync with the hand-authored base
  — guarded by snapshot test (`CSharpSyntaxQuoter.cs:11-18`).

# Related

- [Bootstrap loop](/architecture/bootstrap.md) · [Templating](/subsystems/templating.md)

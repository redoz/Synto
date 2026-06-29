---
type: Subsystem
title: Matching
description: Pattern-match, capture, and replace over syntax — a [Match<T>] DSL that generates matchers and Replace/ForMatch APIs.
resource: src/Synto.SourceGenerator/Matching
project: projects/synto-sourcegenerator
entrypoints: ["src/Synto.SourceGenerator/Matching/MatchFactorySourceGenerator.cs:20", "src/Synto/Matching/MatchAttribute.cs:11"]
tags: [matching, capture, replace, pattern]
timestamp: 2026-06-29T00:00:00Z
---

# Responsibility

The mirror image of [quoting](/subsystems/quoting.md): instead of building syntax,
recognize it. A `[Match<TMatcher>]` method declares a *pattern* (with `[Capture]`
holes); the generator emits a matcher plus `Matched<T>` capture records and
`Replace` / `ForMatch` consumer APIs.

# Key files

Generator (`src/Synto.SourceGenerator/Matching/`). `MatchEmitter` was decomposed
(2026-06-29) from 803 LOC into an orchestrator + walker + aligner + composer + model:
- `MatchFactorySourceGenerator.cs:20` — `[Generator]`; `Initialize` (`:47`),
  `GenerateMatcher` (`:63`), `ValidateTarget` (`:114`, the partial/declared checks).
- `MatchInfo.cs:14` — transform-local pattern model (`Create` `:52`); never cached.
- `MatchEmitter.cs:18` — orchestrator: `Emit` (`:20`) routes markers → deferred-foreach
  pre-scan → anchor split → run align → compose.
- `MatchNodeWalker.cs:24` — `EmitNodeMatch`: recursive structural type/kind/child guards,
  capturing at holes (non-linear equality via `IsEquivalentTo`).
- `MatchRunAligner.cs:22` — `BuildRun` + anchored-run emission; statement-run alignment
  with the ≤1-variable-length quantifier rule (SY1204).
- `MatchComposer.cs:27` — `Compose`: assembles the record + matcher + `CouldMatch`
  predicate + `Pattern` descriptor (owns the generated-output snapshot shape).
- `MatchEmitModel.cs` — `Capture`/`RunElement`/`LiteralElement`/`HoleElement`/`MatchContext`
  data types (transform-local scratch).
- `MatchMarkers.cs:22` — resolves marker symbols (`[Capture]`, `Stmt`, `Statement`);
  `Create` (`:59`), `TryGetCapture` (`:195`).
- `MatchDiagnostics.cs:12` — the per-feature SY12xx registry.

Consumer surface (`src/Synto/Matching/`):
- `MatchAttribute.cs:11` — `[Match<TMatcher>]`, takes a `MatchOption` cardinality (`:14`).
- `CaptureAttribute.cs:10` — `[Capture]` (any `ExpressionSyntax`) and `[Capture<TNode>]` (`:20`, narrowed).
- `MatchOption.cs:10` — `None` / `Bare` / `Single`.
- `Markers.cs:11` — `Stmt` (capturing) and `Statement` (wildcard) quantifiers:
  `One`/`Opt`/`Some`/`All`/`Exactly(n)`.
- `MatchReplaceExtensions.cs:28` — `Replace<TRoot,TMatch>()`, outermost-wins single pass.
- `ForMatchProviderExtensions.cs:27` — `ForMatch<TMatch>()`, hooks matching onto the
  incremental pipeline (cheap predicate + full matcher → `Matched<TMatch>`).

# Entry points

- `MatchFactorySourceGenerator.cs:20` → `Initialize`.

# Invariants

- Same [cacheability contract](/architecture/incremental-pipeline.md) — `MatchInfo`
  holds Roslyn semantics and is transform-local; only equatable
  `MatchGenerationResult` crosses cached boundaries (tracked via `MatchTrackingNames.cs`).

# Related

- [Quoting](/subsystems/quoting.md) · [Incremental pipeline](/architecture/incremental-pipeline.md)

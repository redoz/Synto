---
type: Subsystem
title: Matching
description: Pattern-match, capture, and replace over syntax — a [Match<T>] DSL that generates matchers and Replace/ForMatch APIs.
resource: src/Synto.Generators/Matching
project: projects/synto-sourcegenerator
entrypoints: ["src/Synto.Generators/Matching/MatchFactorySourceGenerator.cs:23", "src/Synto.Core/Matching/Surface/MatchAttribute.cs:11"]
tags: [matching, capture, replace, pattern]
timestamp: 2026-07-05T00:00:00Z
---

# Responsibility

The mirror image of [quoting](/subsystems/quoting.md): instead of building syntax,
recognize it. A `[Match<TMatcher>]` method declares a *pattern* (with `[Capture]`
holes); the generator emits a matcher plus `Matched<T>` capture records and
`Replace` / `ForMatch` consumer APIs.

# Key files

Generator (`src/Synto.Generators/Matching/`; folder = namespace `Synto.Generators.Matching.*`).
`MatchEmitter` is decomposed into an orchestrator + walker + aligner + composer + model:
- `MatchFactorySourceGenerator.cs:23` — `[Generator]` (feature root); `Initialize` (`:49`),
  `GenerateMatcher` (`:65`), `ValidateTarget` (`:127`, the partial/declared checks).
- `Emit/MatchInfo.cs:14` — transform-local pattern model (`Create` `:52`); never cached.
- `Emit/MatchEmitter.cs:20` — orchestrator: `Emit` (`:22`) routes markers → deferred-foreach
  pre-scan → anchor split → run align → compose.
- `Emit/MatchNodeWalker.cs:18` — `EmitNodeMatch` (`:26`): recursive structural type/kind/child guards,
  capturing at holes (non-linear equality via `IsEquivalentTo`).
- `Emit/MatchRunAligner.cs:17` — `BuildRun` (`:24`) + anchored-run emission; statement-run alignment
  with the ≤1-variable-length quantifier rule (SY1204).
- `Emit/MatchComposer.cs:18` — `Compose` (`:27`): assembles the record + matcher + `CouldMatch`
  predicate + `Pattern` descriptor (owns the generated-output snapshot shape).
- `Emit/MatchEmitModel.cs` — `Capture`/`RunElement`/`LiteralElement`/`HoleElement`/`MatchContext`
  data types (transform-local scratch).
- `Discovery/MatchMarkers.cs:23` — resolves marker symbols (`[Capture]`, `Stmt`, `Statement`);
  `Create` (`:60`), `TryGetCapture` (`:196`).
- `Diagnostics/MatchDiagnostics.cs:12` — the per-feature SY12xx registry; `Diagnostics/MatchTrackingNames.cs` names the stages.

Consumer surface (`src/Synto.Core/Matching/`, markers under `Surface/`, emitted extensions under `InjectedOnly/`):
- `Surface/MatchAttribute.cs:11` — `[Match<TMatcher>]`, takes a `MatchOption` cardinality (`:14`).
- `Surface/CaptureAttribute.cs:10` — `[Capture]` (any `ExpressionSyntax`) and `[Capture<TNode>]` (`:20`, narrowed).
- `Surface/MatchOption.cs:10` — `None` / `Bare` / `Single`.
- `Surface/Markers.cs:11` — `Stmt` (capturing) and `Statement` (wildcard) quantifiers:
  `One`/`Opt`/`Some`/`All`/`Exactly(n)`.
- `InjectedOnly/MatchReplaceExtensions.cs:28` — `Replace<TRoot,TMatch>()`, outermost-wins single pass.
- `InjectedOnly/ForMatchProviderExtensions.cs:27` — `ForMatch<TMatch>()`, hooks matching onto the
  incremental pipeline (cheap predicate + full matcher → `Matched<TMatch>`).

# Entry points

- `MatchFactorySourceGenerator.cs:23` → `Initialize`.

# Invariants

- Same [cacheability contract](/architecture/incremental-pipeline.md) — `MatchInfo`
  holds Roslyn semantics and is transform-local; only equatable
  `MatchGenerationResult` crosses cached boundaries (tracked via `Diagnostics/MatchTrackingNames.cs`).

# Related

- [Quoting](/subsystems/quoting.md) · [Incremental pipeline](/architecture/incremental-pipeline.md)

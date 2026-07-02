# Quote Action Pipeline (Scope C) — Design Spec

**Date:** 2026-07-03 · **Branch:** `experimental/quote-action-pipeline` (off `main`, nothing pushed) ·
**Bar:** byte-identical, 269/269 `Synto.Test` green + zero snapshot diffs at every commit.

## Goal

Finish Scope C: make the **Transform (quote phase) name zero features**. Today `TemplateQuoterFactory`
hard-codes six feature middleware in a fixed order; the quote fold "knows" replacement, trim, decoration,
builder-call, interpolation-fold and member-segment. End-state: the **Plan** attaches ordered *steps* to
nodes/lists; the Transform is a generic fold that runs opaque steps; `TemplateQuoterFactory` shrinks to
"wire the node→steps and list→steps maps into the engine." All feature knowledge lives in the Plan.

This is a **decoupling** refactor, not a behavior change. The prior partial landing (reverted at git
`361c9a6b`) did the Node seam only and was judged low-value on a behavior axis — the wrong axis. The value
is that the Transform stops naming features; finishing the List seam + moving the fold into the Plan removes
the remaining feature-awareness.

## Load-bearing invariants (verified this session — do not regress)

1. **The quote stage is semantic-model-free.** `QuoteContext` holds no `SemanticModel`/`ISymbol`/`Compilation`
   (documented incremental-cacheability invariant); there are zero semantic-model references in the entire
   quoting path. All semantic work is plan-stage. The plan/quote split is clean.
2. **The plan's patches are output-domain, keyed by original node.** Each `QuotePlan` entry maps an *original
   node* to a fragment of *generated output* (e.g. `IdentifierName(paramName)` — a reference to a factory
   parameter that exists only in the emitted method; `result.ApplyX(...)`; `BuilderType.Method(args)`;
   `BuildList<T>(...)`; folded literal text). These cannot be expressed as edits to an *input* C# tree, so the
   side-plan consumed at the input→output boundary (the quoter) is **fundamental**, not a workaround. "Rewrite
   the tree first, then quote plainly" is therefore not available (only `Trim`, a pure drop, is input-domain,
   and it already has a trivial mechanism — not worth a rewrite pass).
3. **The input tree is never rewritten**, so original node instances stay stable — which is exactly what makes
   the identity-keyed step dictionaries (reference-equality lookup) valid.

## The runtime engine is out of scope

`src/Synto/Quoting/{MiddlewareSyntaxQuoter,QuoteContext,QuoteMiddleware}.cs` are **not touched**. Steps compose
*as* existing middleware: node steps implement `INodeQuoteMiddleware<QuoteContext>`, list steps implement
`IListQuoteMiddleware<QuoteContext>`. The engine already dispatches both seams and composes each seam's
partition last-to-first with `base` as terminal. Both pipeline middleware live on the `Synto.SourceGenerator`
side.

## Design

### Two symmetric pipeline middleware

Both seams reduce to the **same discipline**: *look up the thing being visited in a plan map keyed by
`SyntaxNode`, run its ordered step chain (terminal = base quote), else defer to `next`.*

| | Node pipeline | List pipeline |
|---|---|---|
| registered as | one `INodeQuoteMiddleware` | one `IListQuoteMiddleware` |
| map | `IReadOnlyDictionary<SyntaxNode, ImmutableArray<INodeQuoteMiddleware<QuoteContext>>>` | `IReadOnlyDictionary<SyntaxNode, ImmutableArray<IListQuoteMiddleware<QuoteContext>>>` |
| lookup key | the visited node | the visited list's **owner** = `list.Count > 0 ? list[0].Parent : null` |
| terminal | base node-quote (`next`) | base list-quote (`next`) |
| chain composition | pre-composed once (non-generic `NodeQuoteNext`) | per-call inside `Quote<TNode>` (generic `ListQuoteNext<TNode>`) |

`NodePipelineMiddleware` is the reverted artifact (`361c9a6b`), revived verbatim. `ListPipelineMiddleware` is
its new mirror.

**Node steps** (per-node ordered list, fixed precedence **Replace → Builder → Trim → Decorate**; across
different nodes order is irrelevant):
- `ReplaceStep` — terminal, emits the pre-computed replacement (never calls `next`).
- `BuilderStep` — terminal, re-entrant builder-call assembly (Task 6, below).
- `TrimStep` — terminal drop (`null`); shared singleton.
- `DecorateStep` — post/wrap: `next` then fold `.ApplyX(args)` invocations.

**List steps** (owner-keyed; at most one per list today, but the pipeline supports an ordered chain for
uniformity):
- `InterpolationFoldStep` — the current `InterpolationFoldMiddleware`, re-cast as an `IListStep`. Owner =
  the `InterpolatedStringExpressionSyntax`; its owner has exactly one relevant list, so no collision.
- `MemberSegmentStep` — the current `MemberSegmentMiddleware`, re-cast as an `IListStep`. Owner = the
  `TypeDeclarationSyntax`.

**Owner-key collision residual:** a type declaration owns several lists (attributes, base types, members) that
all share it as `.Parent`. When the engine visits a non-member list of a keyed type decl, `MemberSegmentStep`
is found but **self-checks** (it scans for keyed generator children and finds none) and defers. That self-check
is exactly what `MemberSegmentMiddleware` already does — so it is free and already correct. The generic-over-
`TNode` awkwardness (and `InterpolationFold`'s single cast) stays **sealed inside the steps**, never leaking
into the plan or factory.

### Builder-call as a node step (reconcile Task 6)

`BuilderCallMiddleware` (landed after the revert) becomes `BuilderStep` — terminal, assembles the
fully-qualified `BuilderType.Method(args)` invocation, each `[Quoted]`/`[TypeArg]` island quoted by
**re-entering** the full pipeline via `ctx.Quote` (so nested builder calls expand and a `Splice` inside a
`[Quoted]` arg composes). **Its bug #1 / #2 fixes and the two snapshot tests that pin them must be preserved.**
Precedence within a node: after `ReplaceStep`, before `TrimStep`. The live-region island quoters must keep
threading `StagedEmitContext.BuilderCalls` so builder calls inside live regions still expand (the ObjectReader
child-template dog-food exercises builder-in-live-region — watch `ChildTemplateTest`).

### Plan owns the fold; factory is feature-blind

The channel→steps fold moves **out of `TemplateQuoterFactory`** into a plan-stage assembler that produces both
maps from the `QuotePlan` channels:

- Input: the existing channels (`Replacements`, `BuilderCalls`, `Trim`, `DecorationHooks` → node steps;
  `StringRoots`, `MemberSegments` → list steps, re-keyed to owner nodes).
- Output: `(nodeSteps, listSteps)` — two `Dictionary<SyntaxNode, ImmutableArray<…>>`.
- Precedence is encoded **here, hand-written, never derived** (no topo-sort, no ranks). Across nodes, order is
  irrelevant.

`TemplateQuoterFactory.Create` shrinks to `Create(nodeSteps, listSteps, includeTrivia)` — it wraps the two
maps in `NodePipelineMiddleware` + `ListPipelineMiddleware` and hands them to the engine. It names no feature.

**The assembler must be callable from all three `Create` sites**, each of which builds a quoter from its own
(partial) channels:
- `TemplateFactoryBuilder.QuoteAndAssemble` (`~:875`) — the main body quoter.
- `StagedRegionEmitter` (`:68`) and `StagedScaffoldBuilder` (`:165`) — live-region island quoters (fixed
  siblings / lift islands). These inherit the step model automatically once they call the shrunken `Create`.

So the assembler is a shared helper (a `QuotePlan.ToStepMaps()` method or a small `PlanAssembler`), not inlined
at one call site.

### Retire the old global feature middleware

The six feature middleware are superseded by steps. Retire `ReplacementMiddleware`, `TrimMiddleware`,
`DecorationMiddleware`, `BuilderCallMiddleware`; the two list middleware become `InterpolationFoldStep` /
`MemberSegmentStep` (structurally identical — they already implement `IListQuoteMiddleware`). Migrate
`QuoteMiddlewareTest` to exercise the **step** types (the steps *are* middleware, so the per-feature behavior
tests carry over with a type rename). One mechanism, no dead path.

## Subsumed prior tasks

- **Old Task 7 (re-home live-region container assembly onto re-entry):** subsumed. Live-region container
  assembly already installs terminal replacements via the `Replacements` channel → those flow through
  `ReplaceStep` like any other replacement. No special re-homing.
- **Old Task 9 (covering-replace ⊥ Layer-B diagnostic):** subsumed structurally. Per-node step precedence makes
  the short-circuit explicit — a `ReplaceStep` terminating before `Decorate`/segment is now visible in the step
  list rather than an implicit global-order accident. No separate diagnostic task.

## Staging — three byte-identical, green units

Each unit: `dotnet build Synto.slnx -c Debug` clean + `dotnet test --project test/Synto.Test/Synto.Test.csproj
-c Debug` = 269/269, zero snapshot diffs; then `jj describe` + advance bookmark
`experimental/quote-action-pipeline`.

1. **Node seam as steps + Builder reconcile.** Revive `NodePipelineMiddleware`/`ReplaceStep`/`TrimStep`/
   `DecorateStep` (git `361c9a6b`); add `BuilderStep` from current `BuilderCallMiddleware` (keep bug #1/#2
   fixes + tests). Factory folds the four node channels into the node-step map (fold still *in* the factory for
   now); list seam unchanged (still the two list middleware). Byte-identical.
2. **List seam as steps.** Add `ListPipelineMiddleware` (owner-keyed mirror of `NodePipelineMiddleware`); re-cast
   `InterpolationFoldMiddleware`/`MemberSegmentMiddleware` as `InterpolationFoldStep`/`MemberSegmentStep`; factory
   folds the two list channels (re-keyed to owner nodes) into the list-step map. Byte-identical.
3. **Move the fold into the Plan; shrink the factory.** Extract both folds into the plan-stage assembler,
   callable from all three `Create` sites; shrink `Create` to `(nodeSteps, listSteps, includeTrivia)`; retire the
   old global middleware; migrate `QuoteMiddlewareTest`. Factory names zero features. Byte-identical.

## Non-goals / scope guard

- **Do not touch the runtime engine** (`MiddlewareSyntaxQuoter`, `QuoteContext`, `QuoteMiddleware`).
- **No multi-pass tree rewrite** (would invalidate the semantic model / break identity keying).
- **Fold-native pruning** and any **SeparatedList/Token/etc. seam** conversion are out of scope — keep the
  existing global branch-prune pass to protect byte-identical.
- **Name-minting logic is byte-sensitive** — do not refactor `NameAllocator` during this work.
- If an **unrelated** snapshot diffs, STOP and report (may reveal a pre-existing latent case) — do not
  re-accept.

## Verification end-state

`TemplateQuoterFactory` names no feature; `NodePipelineMiddleware` + `ListPipelineMiddleware` run opaque ordered
steps keyed by `SyntaxNode`; the plan-stage assembler owns all channel→steps folding with hand-written
precedence; all three `Create` sites share it; the runtime engine is untouched; 269/269 green with zero
snapshot diffs.

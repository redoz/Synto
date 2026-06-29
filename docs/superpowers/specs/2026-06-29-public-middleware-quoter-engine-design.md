# Public middleware quoting engine — design spec

**Date:** 2026-06-29
**Status:** Design (pre-plan)
**Author:** session handoff → spec
**Tracking branch:** experimental/* (per user pref; NOT main)

## Problem

`TemplateSyntaxQuoter` (`src/Synto.SourceGenerator/Templating/TemplateSyntaxQuoter.cs`) is a
subclass of the generated `CSharpSyntaxQuoter` that hard-codes **five distinct, unrelated
templating features** into two overridden visit seams:

| Feature | Channel field | Seam | Behavior |
|---|---|---|---|
| Unquote replacement | `_unquotedReplacements` | `Visit(SyntaxNode?)` (pre) | short-circuit: return the replacement expression instead of quoting |
| Trim / prune | `_trimNodes` | `Visit(SyntaxNode?)` (pre) | return `null` (drop the node) |
| Post-quote decoration | `_postQuoteHooks` | `Visit(SyntaxNode?)` (post) | wrap the base quote in chained `.Apply…(args)` invocations |
| Splice member segment | `_memberSegments` | `Visit<TNode>(SyntaxList<TNode>)` | replace the list with a `CollectionSyntaxExtensions.BuildList<TNode>(…)` run |
| Interpolation staged-fold | `_stringStagedRoots` / `InterpolationFold` | `Visit<TNode>(SyntaxList<TNode>)` | fuse foldable interpolation holes into flanking literal text |

The quoter "knows about" each feature by name. The user's stated requirement: the quoter should
**know nothing about the step it performs** — each feature becomes a pluggable middleware, and the
engine becomes feature-agnostic. This is the agreed *price* for keeping `InterpolationFold` (which
was nearly cut): the refactor stops `InterpolationFold` from leaking feature knowledge into the
quoter.

Scope decision (settled in brainstorming, then refined 2026-06-29 — do not re-litigate): build it
as a **generic-`TContext`, 7-seam middleware quoting ENGINE** in Core (`Synto`), with Synto's own
templating as its first consumer.

**Accessibility — INTERNAL now, public-SHAPED (decided 2026-06-29).** During planning we discovered
the handoff's "public engine + never touch the base" is technically inconsistent: a public class
cannot derive from the `internal` `CSharpSyntaxQuoter` base, and flipping the base to `public`
(the generator copies the base's modifiers — `CSharpSyntaxQuoterGenerator.cs:282`
`.WithModifiers(targetClass.Modifiers)`) would expose the entire generated quoter (~100 `VisitXxx`
methods) as frozen public API — a far larger one-way door than brainstorming assumed.
`project-phase.md` flags the Core public surface as keep-full-severity ("change freely now, freeze
at GA"). **Decision: the engine, the 7 seam interfaces, `QuoteContext`, and the 5 middleware are all
`internal` now; the `CSharpSyntaxQuoter`/`CSharpSyntaxQuoterBase` chain stays `internal` and
untouched.** The surface is *public-SHAPED* — the generic `TContext` is threaded through everything
so that the eventual GA flip to `public` is a pure accessibility change, no structural rework. This
gets the user's actual stated requirement (the quoter "knows nothing about the step it performs")
and keeps `InterpolationFold` without opening the public-API door prematurely.

## Goals

1. The engine contains **zero feature-specific knowledge**. It only owns: seam dispatch, middleware
   partitioning, chain composition, and the empty-partition fast path.
2. Synto's 5 features are re-expressed as **internal middleware** with byte-identical output.
3. The middleware surface is **public-SHAPED but `internal` now** — structured exactly as a public
   surface would be (so a future GA flip is a pure accessibility change), but not yet exposed.
4. Cooperating middleware can share **typed state** via a generic `TContext` — kept now for the
   public-shaped future even though Synto's own consumer needs no shared state.
5. **Pure refactor**: every snapshot is byte-identical. A moved snapshot means behavior changed.
6. Incremental-generator cacheability preserved: no `ITypeSymbol`/`SemanticModel`/`Compilation`/
   `SyntaxNode` captured into cached pipeline state. Middleware are built at **emission**.

## Non-goals

- No change to the generated `CSharpSyntaxQuoter` base or the `Synto.Bootstrap` copy (SYNC NOTE).
- No new templating feature, no behavior change, no snapshot churn.
- **No public API exposed at all** — everything new is `internal`; the GA flip to `public` is
  explicitly deferred (out of scope for this plan).
- No flip of `CSharpSyntaxQuoter`/`CSharpSyntaxQuoterBase` accessibility; the base is untouched.
- `InterpolationFold`'s internal folding algorithm is preserved as-is; only its *wiring* changes.

## Constraints (hard)

- **netstandard2.0** target → **no default interface methods**. A middleware implements only the
  seam interfaces it handles.
- **Do NOT touch** `CSharpSyntaxQuoter.Generated.cs` (self-host generated half) or
  `src/Synto.Bootstrap/CSharpSyntaxQuoter*.cs` (bootstrap self-host INPUT). Breaking either breaks
  the self-host chain; only `Synto.Bootstrap.Test` snapshot guards divergence. The engine is a NEW
  SUBCLASS of `CSharpSyntaxQuoter`; never modify the generated/bootstrap base.
- **Precedence preserved EXACTLY**: a replaced node is NEVER decorated (replacement short-circuits
  before the post/decoration step); a trimmed node is dropped before quoting. Encode precedence via
  registration order.
- **Cacheability**: build middleware lists at emission. The two incremental guard tests must stay
  green (`SpliceMemberGeneratorTest.IsIncrementalOnUnrelatedEdit`,
  `ObjectReaderIncrementalTests.Transform_IsCachedOnUnrelatedEdit`).
- **No public API door opened.** Everything new is `internal`; the `CSharpSyntaxQuoter` base stays
  `internal`. Generic `<TContext>` is threaded through all 7 seam interfaces + the engine now (cheap
  to change while internal) purely to make the future GA flip a pure accessibility change.

## Why this shape (rejected alternatives — do not re-open)

- **Why 7 seams / more than one pipeline, not one unified delegate chain:** `SyntaxList<TNode>` and
  `SeparatedSyntaxList<TNode>` are **structs**, not `SyntaxNode`; `SyntaxKind` is an enum;
  `SyntaxToken`/`SyntaxTriviaList`/`SyntaxTokenList` are structs. The base
  (`CSharpSyntaxQuoterBase`) exposes 7 typed virtual visit seams (one node + six struct/enum
  collection seams). They cannot be unified into one delegate chain: a generic `TNode` cannot be a
  generic delegate *instance* in C#, and type-erasure (the only unification) was rejected (erases
  list element type, forces a mutable `Result`, allocates per visit).
- **Design evolution, each rejected for the next:** three role-interfaces → middleware-with-two-`next`
  (rejected: two `next`s on one mw is weird) → single ASP.NET-style tagged-union context (rejected:
  erases list type, mutable `Result`, per-visit alloc) → two typed base-class pipelines (rejected
  once user required "register ONE mw for both seams": C# single-inheritance blocks it) → **ONE
  marker interface + per-seam INTERFACES** (chosen; interfaces because one class can implement
  several; no default-interface-methods needed since a mw implements only the seams it handles —
  netstandard2.0-safe).
- **`QuoteContext` is a concrete class, NOT an interface** (ASP.NET `HttpContext` precedent; one
  implementer). Generic `TContext` lets cooperating middleware share typed state.
- **Scope:** brainstorming chose ALL-IN public; planning refined this to **internal-now /
  public-shaped** once the true cost surfaced (a public engine forces the whole generated quoter
  public — see Accessibility decision above). The GA flip is deferred, not abandoned: the shape is
  preserved so the flip is a one-line-per-type accessibility change.
- **InterpolationFold reframed honestly:** it buys nothing at runtime (the C# compiler constant-folds
  the un-folded `{"const"}` hole); its only real payoff is byte-identical snapshots for the
  child-templates Task 3 path — in tension with the project's own "semantic-not-byte-identical"
  stance. Kept only because the middleware refactor stops it leaking into the quoter.

## Design

### Component map (all NEW, `internal`, in Core `Synto`, next to `CSharpSyntaxQuoter`)

All types below are `internal` (public-shaped; GA flip deferred).

```
Synto (Core) — all internal
├── QuoteContext                         abstract base; the per-visit cooperation surface
├── MiddlewareSyntaxQuoter<TContext>     the engine: subclass of CSharpSyntaxQuoter
├── IQuoteMiddleware                     marker interface
├── INodeQuoteMiddleware<TContext>       seam 1
├── IListQuoteMiddleware<TContext>       seam 2
├── ISeparatedListQuoteMiddleware<TContext>  seam 3
├── ITokenQuoteMiddleware<TContext>      seam 4
├── ITriviaListQuoteMiddleware<TContext> seam 5
├── ITokenListQuoteMiddleware<TContext>  seam 6
└── IKindQuoteMiddleware<TContext>       seam 7
```

### `QuoteContext` (internal abstract class; public-shaped)

The per-visit cooperation surface handed to every middleware. **Concrete class, not interface.**
It exposes the engine's quoting primitives so middleware can recurse/quote without holding a
reference to the quoter directly:

- `ExpressionSyntax? Quote(SyntaxNode? node)` — re-enter the node pipeline (synchronous re-entry;
  this is today's `Visit(SyntaxNode?)`).
- `ExpressionSyntax QuoteList<TNode>(SyntaxList<TNode> list) where TNode : SyntaxNode` — re-enter the
  list pipeline.
- `ExpressionSyntax ToArrayLiteral(IEnumerable<ExpressionSyntax> nodes, TypeSyntax elementType)` —
  expose the base array-literal builder (today `protected static` on `CSharpSyntaxQuoterBase`).

> Contract note: `QuoteContext` delegates each primitive to its owning `MiddlewareSyntaxQuoter`. The
> exact method set is the minimal one the 5 internal middleware require (the two `InterpolationFold`
> callbacks `Visit` + `ToArrayLiteral`, plus node/list re-entry). Additional primitives may be added
> if a seam middleware needs them, but the surface stays minimal — every method here is a future-GA
> commitment once the surface flips public.

`TContext : QuoteContext` is the generic parameter threaded through the engine and all seam
interfaces. Synto's own consumer uses **`TContext = QuoteContext`** directly (the base needs no
extra shared state — each feature's channel dictionaries are captured by the middleware instance,
not shared through context). The generic exists for *external* cooperating-middleware scenarios.

### Seam interfaces (internal; public-shaped)

`IQuoteMiddleware` is an empty marker. Each seam interface is `IQuoteMiddleware`-derived and carries
exactly one method, shaped after ASP.NET middleware (`(target, context, next)` with a single `next`
per seam):

| Interface | Method (shape) | Base seam it intercepts |
|---|---|---|
| `INodeQuoteMiddleware<TContext>` | `ExpressionSyntax? Quote(SyntaxNode? node, TContext ctx, NodeQuoteNext next)` | `Visit(SyntaxNode?)` |
| `IListQuoteMiddleware<TContext>` | `ExpressionSyntax Quote<TNode>(SyntaxList<TNode> list, TContext ctx, ListQuoteNext<TNode> next)` | `Visit<TNode>(SyntaxList<TNode>)` |
| `ISeparatedListQuoteMiddleware<TContext>` | `ExpressionSyntax Quote<TNode>(SeparatedSyntaxList<TNode> list, TContext ctx, SeparatedListQuoteNext<TNode> next)` | `Visit<TNode>(SeparatedSyntaxList<TNode>)` |
| `ITokenQuoteMiddleware<TContext>` | `ExpressionSyntax Quote(SyntaxToken token, TContext ctx, TokenQuoteNext next)` | `Visit(SyntaxToken)` |
| `ITriviaListQuoteMiddleware<TContext>` | `ExpressionSyntax Quote(SyntaxTriviaList trivia, TContext ctx, TriviaListQuoteNext next)` | `Visit(SyntaxTriviaList)` |
| `ITokenListQuoteMiddleware<TContext>` | `ExpressionSyntax Quote(SyntaxTokenList tokens, TContext ctx, TokenListQuoteNext next)` | `Visit(SyntaxTokenList)` |
| `IKindQuoteMiddleware<TContext>` | `ExpressionSyntax Quote(SyntaxKind kind, TContext ctx, KindQuoteNext next)` | `Visit(SyntaxKind)` |

The `…Next` types are delegates matching each seam's `(target) → result` signature (the generic list
seams' `next` carries the element type — `ListQuoteNext<TNode>` etc. — which is why these chains are
composed per-call, see below). `next` invokes the rest of the chain; calling the base quoting is the
terminal `next`.

> Contract note: whether `…Next` are named delegate types or `Func<…>` is an implementation choice;
> named delegates are preferred for the public surface (clearer signatures, room for `ref struct`
> evolution). Pin the *shape* `(target, ctx, next) → result` and the terminal-is-base-quote rule.

### `MiddlewareSyntaxQuoter<TContext>` (internal engine; public-shaped)

```
internal class MiddlewareSyntaxQuoter<TContext> : CSharpSyntaxQuoter
    where TContext : QuoteContext
```

(`internal class`, not `sealed` — public-shaped for a future GA flip to `public`; the base
`CSharpSyntaxQuoter` stays `internal` and untouched, so derivation is accessibility-consistent.)

- **Ctor** takes one `IReadOnlyList<IQuoteMiddleware>` (plus `bool includeTrivia` forwarded to
  `base`, and whatever is needed to obtain a `TContext` per visit — see context lifetime below).
- **Partition once** in the ctor: `OfType<I…QuoteMiddleware<TContext>>()` into 7 per-seam arrays. A
  middleware that implements several seam interfaces lands in **multiple** partitions; registration
  order is preserved within each partition.
- **Node chain pre-composed once** in the ctor (node seam is non-generic, so the whole
  `next`-chain can be built ahead of time). The **generic** list/separated-list seams cannot be
  pre-composed (the chain closes over `TNode`), so they are composed **per-call** inside the seam
  override.
- **Every one of the 7 base seams is overridden** with an **empty-partition guard**: if that seam's
  partition is empty, call `base.<Visit>` directly (zero overhead — the no-middleware fast path,
  matching today's behavior where only Node and List seams are touched). Otherwise run the chain,
  whose terminal `next` calls `base.<Visit>`.

**Context lifetime / cacheability:** the engine produces a `TContext` bound to *this transform's*
emission (it carries only the channel data the middleware captured, never live Roslyn symbols). The
context is a transform-local object; it never flows into cached pipeline state. Exact construction
(ctor-supplied factory `Func<TContext>` vs. a single reused instance) is an implementation detail to
settle in the plan — the constraint is: no `SemanticModel`/`ITypeSymbol`/`Compilation`/live
`SyntaxNode` graph captured anywhere that outlives the transform.

### The 5 internal middleware (in `Synto.SourceGenerator/Templating`, `internal`)

Default `TContext = QuoteContext`. Each captures its channel dictionary as constructor state.

| Middleware | Seam(s) | Replaces | Behavior (unchanged) |
|---|---|---|---|
| `ReplacementMiddleware` | Node (pre, short-circuit) | `_unquotedReplacements` | if node in map → return replacement, do NOT call `next` |
| `TrimMiddleware` | Node (pre, drop) | `_trimNodes` | if node in set → return `null`, do NOT call `next` |
| `DecorationMiddleware` | Node (post) | `_postQuoteHooks` | `var r = next(node); if r≠null && node has hooks → fold chained `.Apply…(args)` onto r; return r` |
| `MemberSegmentMiddleware` | List | `_memberSegments` | if any child has a segment → emit `BuildList<TNode>(…)` run; else `next(list)` |
| `InterpolationFoldMiddleware` | List | `_stringStagedRoots` + `InterpolationFold` | if `TNode == InterpolatedStringContentSyntax` and fold applies → return folded; else `next(list)`. The `TNode` guard moves **inside** this middleware. |

**Registration order** (the composition root passes this exact order; it encodes precedence):

Node seam: `Replacement`, `Trim`, `Decoration`.
- Replacement first (short-circuits before Trim and before the base quote+Decoration).
- Trim second (drops before the base quote).
- Decoration last (post-processes the base quote; a replaced/trimmed node never reaches it because
  those middleware short-circuit `next`). **This reproduces today's exact precedence**: in the
  current code the pre-checks (`_unquotedReplacements`, then `_trimNodes`) run before `base.Visit`,
  and the post-hook loop runs only on a non-null base result.

List seam order: `MemberSegment`, `InterpolationFold` (or the reverse — they target disjoint lists:
member lists vs. interpolated-string-content lists, so order is behaviorally irrelevant; pick the
order that matches today's check sequence in `Visit<TNode>(SyntaxList<TNode>)`, which tests the
interpolation fold first, then the member segment — so register `InterpolationFold` then
`MemberSegment` to mirror it exactly).

> Correction to capture in the plan: today's `Visit<TNode>(SyntaxList<TNode>)` checks
> **interpolation-fold first**, then member-segment. Mirror that order. The two are disjoint in
> practice, but mirror it so the refactor is provably behavior-preserving by inspection.

`InterpolationFold` itself is reused unchanged: `InterpolationFoldMiddleware` constructs/holds an
`InterpolationFold`, passing `ctx.Quote` and `ctx.ToArrayLiteral` as its two callbacks (today
`Visit` and `ToArrayLiteral`).

### Composition root (4 sites)

The 4 sites that today do `new TemplateSyntaxQuoter(…)` become the composition root: build the
ordered `IReadOnlyList<IQuoteMiddleware>` from the same channel data, then
`new MiddlewareSyntaxQuoter<QuoteContext>(middleware, includeTrivia)`:

1. `StagedRegionEmitter.cs:67` — replacements + stringStagedRoots (→ Replacement + InterpolationFold).
2. `StagedScaffoldBuilder.cs:165` — liftMap + stringStagedRoots.
3. `TemplateFactoryBuilder.cs:779` — UnquotedReplacements + StringStagedRoots.
4. `TemplateFactoryBuilder.cs:898` (`QuoteAndAssemble`) — the full set: replacements + prunableNodes
   (trim) + memberSegments + stringStagedRoots + decorationHooks (→ all 5 middleware).

A middleware whose channel is empty should simply not be added (or be added and hit its own internal
empty fast path — but **preferably not added**, so the engine's empty-partition guard gives the
zero-overhead base path, exactly reproducing today's behavior where empty channels are inert). The
plan settles whether the composition root omits empty-channel middleware or the engine's
empty-partition guard suffices; functionally either works, but omitting keeps partitions empty and
the fast path clean.

**Drop `_semanticModel`:** `TemplateSyntaxQuoter` field `_semanticModel` is assigned but never read
(dead). It disappears with the refactor — neither the engine nor any middleware needs it.

### What happens to `TemplateSyntaxQuoter`

It is **deleted**. Its two overrides become middleware; its construction becomes the composition
root. `TemplateSyntaxQuoterInvoker` (which holds a quoter + template and drives `TryQuote`) is
retargeted to accept the engine type (`MiddlewareSyntaxQuoter<QuoteContext>` or the base
`CSharpSyntaxQuoter`) — confirm its signature only needs the base quoting surface.

## Risks & mitigations

| Risk | Mitigation |
|---|---|
| Snapshot drift (behavior change) | Byte-identical snapshots are the oracle; run the full green gate. Mirror today's seam check-order exactly. |
| Precedence inversion (decorating a replaced node) | Registration order Replacement→Trim→Decoration; short-circuit `next`. Add/verify a test exercising replace+decorate on the same node if one doesn't exist. |
| Cacheability regression | No live symbols in middleware/context; both incremental guard tests stay green. |
| Self-host breakage | Never touch generated/bootstrap base; base stays `internal` and untouched; engine is a pure subclass. Bootstrap build must stay unaffected. |
| Public-API surface creep | N/A this plan — everything is `internal`. Keep `QuoteContext` primitives minimal anyway so the future GA flip exposes a clean surface. |
| Generic `TContext` viral cost | Cheap now (all `internal`, freely changeable). Carried purely to make the GA flip a pure accessibility change. |

## Verification (green gate)

Pure refactor → **257/257 byte-identical** `Synto.Test`, **10/10** ObjectReader, both incremental
guards green, self-host/bootstrap build unaffected, build 0 errors.

```
dotnet build Synto.slnx -c Debug
./test/Synto.Test/bin/Debug/net10.0/Synto.Test.exe                                            # expect 257
./examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Tests/bin/Debug/net10.0/Synto.Example.ObjectReader.Tests.exe  # expect 10
```

(MTP quirk — use the exe directly; `dotnet test` misreports under SDK 10 MTP.)

NO snapshot may move. If one moves, behavior changed — stop and reconcile.

## Open questions for the plan (not blockers)

1. `…Next` as named delegate types vs. `Func<…>` (lean: named delegates — clearer signatures, keeps
   the public-shaped surface clean for the future GA flip).
2. `TContext` acquisition: ctor `Func<TContext>` factory vs. single reused per-quoter instance
   (lean: single reused instance bound at ctor — one quoter == one transform).
3. Composition root: omit empty-channel middleware vs. rely on engine empty-partition guard
   (lean: omit, so partitions stay empty and the fast path is exact).
4. The 5 internal middleware exercise only the Node and List seams. The other 5 seams
   (SeparatedList/Token/TriviaList/TokenList/Kind) have no internal consumer, so their middleware
   plumbing would ship untested. Add at least one internal smoke-test middleware on a non-Node/List
   seam to prove the engine actually dispatches all 7 seams through the chain (covers the
   public-shaped contract before the GA flip relies on it).

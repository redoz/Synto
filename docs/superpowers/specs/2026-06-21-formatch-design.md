# Matching DSL — `ForMatch` incremental-provider helper (design)

- **Date:** 2026-06-21
- **Status:** Approved design (pre-plan)
- **Builds on:** Matching DSL v1 — `docs/superpowers/specs/2026-06-20-matching-dsl.md` (implemented on `experimental/matching`, Tasks 1–17)
- **Tracking:** follow-on to #92

## 1. Motivation

Roslyn gives generator authors `SyntaxValueProvider.ForAttributeWithMetadataName` — *"hook my
incremental generator to this attribute"* — backed by a fast attribute-usage index so the
predicate never re-scans every node on each keystroke.

Synto's Matching DSL is the **inverse of templating**: a `[Match<TMatcher>]` pattern compiles to a
bespoke `static TMatch? Name(SyntaxNode)` matcher. Its audience **is** Roslyn generator authors.
The missing ergonomic is the mirror image of FAWMN — *"hook my incremental generator to this
**match pattern**"* — a `ForMatch` helper that yields the matched nodes and their captures as an
`IncrementalValuesProvider`.

## 2. Background — the matcher today

A pattern `Sum` generates (into the consumer's generator project):

```csharp
public sealed record SumMatch(ExpressionSyntax A, ExpressionSyntax B);
public static SumMatch? Sum(SyntaxNode node) { /* root kind/shape gate, then captures */ }
```

Two facts constrain the design:

1. **Captures are raw syntax nodes** (`ExpressionSyntax`, …). Synto's pipeline discipline
   (matching spec §6 / C4) forbids `SyntaxNode` / `SemanticModel` / `ISymbol` from entering
   incremental pipeline state — only **equatable** values may flow.
2. **The matching DSL is self-contained** — generated + injected code, **no Synto runtime-package
   dependency** (the v1 C3 contract). Anything `ForMatch` needs must be self-contained too.

## 3. Design decisions

- **D1 — Layered API.** Provide **both** a *thin* form (yields the match; convenient, not
  cacheable) and a *projecting* form (consumer projects to equatable data up front; cacheable by
  construction). The consumer picks per use.
- **D2 — Injected, self-contained.** The helper surface is injected into the consumer's generator
  project via the existing post-init surface-injection mechanism (the same path that injects the
  marker types), as `internal` members. No runtime-package dependency.
- **D3 — Generated companion predicate.** There is no global index for "nodes matching a pattern."
  Per pattern, Synto emits a cheap `XCouldMatch(SyntaxNode) -> bool` (the matcher's top-level
  kind/shape gate) used as the incremental predicate; the full matcher runs in the transform.
  Mirrors FAWMN's cheap-predicate / heavier-transform split.
- **D4 — `Matched<T>` carries node + captures; `MatchPattern<T>` bundles predicate + matcher.**
  `Matched<TMatch>(SyntaxNode Node, TMatch Captures)` so consumers get the matched node (diagnostic
  location, replacement). `MatchPattern<TMatch>` pairs `CouldMatch` + `Match` so the consumer
  references a single symbol (`M.SumPattern`).

## 4. API surface

### 4.1 Generated per pattern (additive to today's matcher)

```csharp
partial class M
{
    public sealed record SumMatch(ExpressionSyntax A, ExpressionSyntax B);  // today
    public static SumMatch? Sum(SyntaxNode node) { … }                      // today
    public static bool SumCouldMatch(SyntaxNode node) { … }                 // NEW — cheap root gate
    public static MatchPattern<SumMatch> SumPattern { get; } = new(SumCouldMatch, Sum); // NEW
}
```

### 4.2 Injected once, self-contained `internal`

```csharp
internal readonly record struct Matched<TMatch>(SyntaxNode Node, TMatch Captures)
    where TMatch : class;

internal readonly struct MatchPattern<TMatch> where TMatch : class
{
    public MatchPattern(Func<SyntaxNode, bool> couldMatch, Func<SyntaxNode, TMatch?> match);
    internal Func<SyntaxNode, bool> CouldMatch { get; }
    internal Func<SyntaxNode, TMatch?> Match { get; }
}

internal static class SyntoMatchProviderExtensions
{
    // thin — convenient, NOT cacheable (yields syntax nodes)
    public static IncrementalValuesProvider<Matched<TMatch>> ForMatch<TMatch>(
        this SyntaxValueProvider syntax, MatchPattern<TMatch> pattern)
        where TMatch : class;

    // projecting — cacheable by construction
    public static IncrementalValuesProvider<TResult> ForMatch<TMatch, TResult>(
        this SyntaxValueProvider syntax, MatchPattern<TMatch> pattern,
        Func<Matched<TMatch>, CancellationToken, TResult> transform)
        where TMatch : class;
}
```

### 4.3 Consumer usage

```csharp
// thin — convenient; not cacheable (holds syntax nodes)
IncrementalValuesProvider<Matched<SumMatch>> sums =
    context.SyntaxProvider.ForMatch(M.SumPattern);

// projecting — cacheable; only the equatable TResult flows
IncrementalValuesProvider<(Location loc, string a)> lowered =
    context.SyntaxProvider.ForMatch(M.SumPattern,
        static (m, ct) => (m.Node.GetLocation(), m.Captures.A.ToString()));
```

## 5. Data flow & the cacheability rule (load-bearing)

Both forms wrap `SyntaxValueProvider.CreateSyntaxProvider(predicate, transform)`:

- `predicate = (node, ct) => pattern.CouldMatch(node)` — cheap syntactic gate (D3).
- `transform` runs `pattern.Match(node)`.

**The projecting form runs the matcher AND the consumer's `transform` inside the _same_
`CreateSyntaxProvider` transform**, emitting only the equatable `TResult`. The non-equatable
`Matched<TMatch>` is transform-local and never becomes a cached pipeline value. *This* is what
makes it cacheable.

The **thin form** emits `Matched<TMatch>` as the pipeline value — it holds syntax nodes, so it is
not cacheable and roots nodes in pipeline state. That is the documented tradeoff (D1).

**Anti-pattern (must avoid in implementation):** the projecting form must **not** be implemented as
`thinForm.Select(transform)`. That would cache the non-equatable `Matched<TMatch>` at the
intermediate step, defeating caching and rooting syntax nodes. The two overloads are **distinct
implementations**, not one expressed via the other.

## 6. Contracts / invariants

- **C-FM1 — Predicate is a superset.** `XCouldMatch(n)` MUST return `true` for every node where
  `X(n) != null`. It is the matcher's root gate: it may over-accept, never under-accept (else
  `ForMatch` silently drops real matches). Holds by construction (it is the matcher's own gate);
  pinned by test.
- **C-FM2 — Equatable boundary at the projecting transform.** The projecting form's only pipeline
  output is the consumer's `TResult`; no `SyntaxNode` / `SemanticModel` / `ISymbol` is cached by the
  helper. The API shape (transform takes `Matched`, returns `TResult`) makes the boundary explicit.
  The consumer is responsible for `TResult` being equatable; the helper itself is tested to leak
  nothing.
- **C-FM3 — Self-contained.** Injected helpers + generated members compile on `netstandard2.0` with
  no runtime-package dependency; records reuse the `IsExternalInit` polyfill the matching DSL
  already injects.
- **C-FM4 — One match per node.** `ForMatch` yields one `Matched<T>` per node where the matcher
  succeeds, preserving the matcher's existing first/leftmost semantics. No change to match meaning.

## 7. Edge cases

- **Pattern in a nested / generic containing type.** The generated `XCouldMatch` / `XPattern`
  members live in the same partial type as `X`; qualification mirrors the existing matcher emission.
- **Capture-less pattern.** `XMatch` may be a parameter-less record; `Matched<XMatch>` still carries
  the node. Works.
- **Predicate cost.** `CouldMatch` is the root kind/shape check only — O(1) per node; the full
  matcher runs only on the nodes that pass it.

## 8. Testing

- **Snapshots:** extend the match snapshot goldens to include the generated `XCouldMatch` +
  `XPattern`; extend the `SurfaceInjectionTest` goldens for the injected `Matched` / `MatchPattern`
  / `SyntoMatchProviderExtensions`.
- **C-FM1 superset property:** over representative trees, assert `Match(n) != null ⇒ CouldMatch(n)`
  for every node.
- **Incrementality:** a minimal in-test consumer generator using the **projecting** `ForMatch`;
  with generator-driver tracking, an unrelated edit leaves its outputs `Cached` / `Unchanged`
  (mirrors `Generator_IsIncremental_OnUnrelatedEdit`). A companion assertion/doc that the thin form
  does not cache (and why).
- **Self-containment (C-FM3):** the injected helper surface compiles under the `netstandard2.0` ref
  closure (reuse the v1 C3 harness).
- **Functional round-trip:** `ForMatch` over a known tree yields the expected matched nodes +
  captures.

## 9. Scope / non-goals

- **In scope:** the generated `CouldMatch` + `Pattern` per pattern; the injected `Matched` /
  `MatchPattern` / `ForMatch` surface; the two overloads; the tests above.
- **Non-goals (this increment):** semantic predicates (the matcher is pure-syntax); enumerating
  multiple matches per node; a runtime-package distribution; analyzer / code-fix integration.
- **Relationship to v1:** additive — its own implementation plan and increment on
  `experimental/matching` (or a sibling experimental branch). Does not alter the v1 matcher surface.

## 10. Open questions (resolve during planning)

- **Naming.** `ForMatch` (lean: yes); `Matched<T>` vs `MatchResult<T>` (lean: `Matched<T>`);
  `MatchPattern<T>` vs `Pattern<T>` (lean: `MatchPattern<T>`).
- **Imperative convenience.** Whether `MatchPattern<T>` should also expose `Match(node)` /
  `IsMatch(node)` for non-pipeline (imperative) callers — likely yes and cheap, but confirm during
  planning.

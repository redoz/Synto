# Matching DSL — `Pattern.Replace` match-driven rewrite helper (design)

- **Date:** 2026-06-22
- **Status:** Delivered — implemented on `experimental/matching` (plan `2026-06-22-match-replace.md`).
  Surface: `SyntoMatchReplaceExtensions.Replace<TRoot, TMatch>(this MatchPattern<TMatch>, TRoot, Func<Matched<TMatch>, SyntaxNode>, ReplaceOption = All)` + `ReplaceOption { All = 0, First = 1 }`, injected `internal` and self-contained on `netstandard2.0`. Resolved decisions: a root-self match returns the replacement (cast to `TRoot`); `First` rewrites the earliest match in document order and short-circuits (no match-all-then-trim).
- **Builds on:** Matching DSL v1 + `ForMatch` — `docs/superpowers/specs/2026-06-20-matching-dsl.md`,
  `docs/superpowers/specs/2026-06-21-formatch-design.md` (implemented on `experimental/matching`).
- **Tracking:** follow-on to the `ForMatch` increment.

## 1. Motivation

The Matching DSL today *detects and extracts*: `M.Sum(node)` / `pattern.Match(node)` /
`pattern.IsMatch(node)` (point checks), `pattern.Pattern` (the descriptor), and `ForMatch` (hook a
pattern into an incremental pipeline). The missing capstone is *transformation* — given a pattern,
**rewrite the nodes it matches** in a tree. This is the bridge that makes **Matching + Templating
compose**: match a sub-tree, swap it for a Template-built node (or any node).

`System.Text.RegularExpressions.Regex` is the model: alongside `Match` / `IsMatch` it offers
`Replace(input, MatchEvaluator)`. We add the syntax-tree analog: `pattern.Replace(root, m => newNode)`.

## 2. Background — the surface today

Per pattern `Sum`, Synto generates (into the consumer's generator project) `SumMatch? Sum(SyntaxNode)`,
`SumCouldMatch`, `SumPattern`. Injected once (`internal`, self-contained on `netstandard2.0`):

```csharp
internal readonly record struct Matched<TMatch>(SyntaxNode Node, TMatch Captures) where TMatch : class;

internal readonly struct MatchPattern<TMatch> where TMatch : class
{
    public bool IsMatch(SyntaxNode node);    // point check
    public TMatch? Match(SyntaxNode node);   // point check
    internal Func<SyntaxNode, bool> CouldMatch { get; }
    internal Func<SyntaxNode, TMatch?> MatchFn { get; }
}
```

`MatchOption` is a **plain (non-`[Flags]`) enum** — "mutually-exclusive cardinalities, not bit fields".

Two facts constrain the design (inherited from the matching spec / `ForMatch`):

1. **A replacement is a `SyntaxNode`** — non-equatable. It must **never** enter cached incremental
   pipeline state. `Replace` is therefore an **imperative** operation (transform-local, or at the
   `RegisterSourceOutput` stage), like `IsMatch` / `Match` — not a cached provider like `ForMatch`.
2. **Self-contained, no runtime dependency.** Injected helpers compile on `netstandard2.0` with no
   Synto runtime-package reference.

## 3. Design decisions

- **D1 — Pattern-subject, Regex-parallel.** `pattern.Replace(root, evaluator)` mirrors
  `Regex.Replace(input, MatchEvaluator)`, and is symmetric with `pattern.Match(node)`. Because the
  pattern is the subject (it already says "matches"), the verb is just **`Replace`** — no
  `ReplaceMatches`.
- **D2 — Open replacement; Template is free.** The replacement is `Func<Matched<TMatch>, SyntaxNode>`
  — any node. Template integration falls out for free (return `Factory.X(...)` from the lambda); no
  Template-specific API is added.
- **D3 — Fixed traversal; cardinality via an option enum.** Traversal is **outermost-match-wins, one
  top-down pass**. The only knob is *how many* matches to rewrite, via **`ReplaceOption { All, First }`**
  — a plain enum mirroring `MatchOption` (mutually-exclusive cardinality, **not** `[Flags]`). Anything
  fancier — bottom-up, fixpoint — is a **custom rewrite the consumer writes themselves** with
  `pattern.Match(node)` inside their own `CSharpSyntaxRewriter`. (Explicitly chosen over a regex-style
  `[Flags]` options bag: the library does not impose, nor over-configure, a traversal policy.)
- **D4 — Return type preserved.** `Replace` returns `TRoot` (the input's static type), like
  `SyntaxNode.ReplaceNodes<TRoot>`.
- **D5 — Extension placement.** `Replace` is an **extension method on `MatchPattern<TMatch>`** (not an
  instance method like `Match`/`IsMatch`), so the call-site is identical — `pattern.Replace(...)` — but
  the tree-rewriting machinery lives in the injected **extensions** file, keeping the netstandard2.0-pure
  **data surface** (`Matched<T>` / `MatchPattern<T>`) clean. `ReplaceOption` is a pure enum (no Roslyn),
  injected next to `MatchOption`.

## 4. API surface

### 4.1 `ReplaceOption` (pure enum, injected `internal` next to `MatchOption`)

```csharp
namespace Synto.Matching;

/// <summary>Selects how many of a pattern's matches <c>Replace</c> rewrites in a tree.</summary>
/// <remarks>
/// Like <see cref="MatchOption"/> (and unlike <c>Synto.Templating.TemplateOption</c>), this is
/// deliberately NOT a <c>[Flags]</c> enum — the values are mutually-exclusive cardinalities. Traversal
/// is fixed (outermost match wins, one top-down pass); anything else is a custom rewrite the consumer
/// writes with <c>pattern.Match(node)</c>.
/// </remarks>
public enum ReplaceOption
{
    /// <summary>Replace every match (outermost-wins, single pass). The default.</summary>
    All = 0,
    /// <summary>Replace only the first match in document order; leave the rest.</summary>
    First = 1,
}
```

### 4.2 `Replace` (extension on `MatchPattern<TMatch>`, injected with the ForMatch extensions)

```csharp
// reads: M.SumPattern.Replace(root, static m => Factory.Mul(m.Captures.A, m.Captures.B))
public static TRoot Replace<TRoot, TMatch>(
    this MatchPattern<TMatch> pattern,
    TRoot root,
    Func<Matched<TMatch>, SyntaxNode> replacement,
    ReplaceOption option = ReplaceOption.All)
    where TRoot : SyntaxNode
    where TMatch : class;
```

### 4.3 Consumer usage

```csharp
// rewrite every Sum into a Mul (Template-built replacement)
CompilationUnitSyntax rewritten =
    M.SumPattern.Replace(root, static m => Factory.Mul(m.Captures.A, m.Captures.B));

// rewrite only the first match (≈ Regex.Replace count: 1)
var firstOnly = M.SumPattern.Replace(root, m => m.Captures.A, ReplaceOption.First);
```

## 5. Semantics (load-bearing)

- **Traversal.** A single top-down walk of `root`, **descendants-and-self** (implemented with a private
  `CSharpSyntaxRewriter` internal to the injected file). At each visited node, `pattern.Match(node)`
  runs; on a match the node is replaced by `replacement(matched)` and the matched subtree is **not**
  descended into — the replacement is final for that subtree (**outermost-wins, no re-scan / no
  fixpoint**). Matches strictly *inside* a replaced subtree are therefore not applied. `root` itself is a
  candidate: if it matches, it is replaced and the replacement is returned (see C-R2).
- **Cardinality.** `All` rewrites every match; `First` rewrites the earliest match in **document order**
  and stops — the remainder of the tree is returned unchanged.
- **Return.** The same `TRoot` instance when nothing matches; otherwise the rewritten `TRoot`.
- **Trivia / formatting** are the consumer's concern (call `NormalizeWhitespace()` on the result if
  desired) — `Replace` uses the replacement node verbatim, matching Roslyn's `ReplaceNode` behavior.
- **Match scope nuance (documented).** `IsMatch` / `Match` are *point checks* on the single node handed
  to them; `Replace` *searches the whole `root` subtree* (like `Regex.Replace` / `string.Replace`). Same
  subject, wider scope — intentional, called out in the doc-comment so no one expects a one-node op.

## 6. Contracts / invariants

- **C-R1 — Outermost-wins, single pass.** A matched subtree is replaced wholesale; matches inside a
  replaced subtree are not applied; replacements are not re-scanned. No fixpoint, no infinite loop.
- **C-R2 — Return type preserved; root-match returns the replacement.** `Replace` returns `TRoot` (the
  input's static type). When `root` itself matches, its replacement is returned (cast to `TRoot`); the
  replacement must therefore be assignable to `TRoot` — replacing the root with a non-assignable node is
  a consumer type error (`InvalidCastException`). In the common case (the root is a container that does
  not itself match) `TRoot` is preserved exactly, like `SyntaxNode.ReplaceNodes<TRoot>`.
- **C-R3 — Imperative, never cached.** `Replace` yields a `SyntaxNode`; it is transform-local /
  output-stage and introduces no cached pipeline value. (The consumer must not push the rewritten tree
  into cached incremental state — same discipline as `Matched<T>`.)
- **C-R4 — Self-contained.** `ReplaceOption` + `Replace` compile on the faithful `netstandard2.0`
  closure with no Synto runtime-package dependency (same proof family as the `ForMatch` surface).
- **C-R5 — `First` is document order.** `First` rewrites the earliest match in source order.

## 7. Edge cases

- **Capture-less pattern.** Works; `Matched<TMatch>` still carries the node.
- **No match.** Returns `root` unchanged (same instance).
- **Overlapping / nested matches.** Resolved by C-R1 (outermost-wins).
- **The `root` node itself matches.** It is replaced like any other node and the replacement is returned
  (as `TRoot`, per C-R2) — no special-casing; the root is just another candidate.

## 8. Testing

- **Functional round-trip — `All`:** a tree with N matches → all rewritten; assert output text.
- **Functional round-trip — `First`:** same tree → only the first match rewritten.
- **Template-as-replacement:** the evaluator returns a `Factory.X(...)` node; assert the composed output
  — the Matching + Templating capstone.
- **No-match:** returns `root` unchanged.
- **Captures:** the evaluator sees strongly-typed `m.Captures.*`.
- **Self-containment (C-R4):** extend the `ForMatch` netstandard2.0 self-containment proof to compile
  `ReplaceOption` + `Replace` on the faithful closure with zero errors.
- **Snapshots:** new injected golden for `ReplaceOption` (next to `MatchOption`); `Replace` appears in
  the injected extensions golden. **No per-pattern generated members change** — `Replace` is pure
  injected surface, not emitted per pattern — so the matcher goldens are untouched (minimal churn).

## 9. Scope / non-goals

- **In scope:** `ReplaceOption { All, First }`; the `pattern.Replace` extension; outermost-wins single
  pass; return-type preservation; Template-as-replacement (free).
- **Non-goals (this increment):** traversal options (bottom-up / fixpoint — roll-your-own with
  `pattern.Match`); code-fix / `Document` / Workspaces integration; a pipeline/incremental `Replace`
  variant; multi-pattern replace; trivia/formatting normalization (the consumer's concern).
- **Relationship to `ForMatch`:** independent and complementary — `ForMatch` flows equatable
  projections through a cached pipeline; `Replace` is an imperative rewrite over a concrete `root` (e.g.
  inside `RegisterSourceOutput`, a build-time rewriter, or tests).

## 10. Resolved decisions

- **Root-node-self match (resolved).** `Replace` walks **descendants-and-self**; if `root` itself matches
  it is replaced and the replacement is returned (cast to `TRoot`, per C-R2). No special-casing — the
  root is just another node.
- **`First` short-circuits (resolved).** `First` stops the walk after the first replacement; it does not
  rewrite-all-then-take-first.

# Spec: `quote` / `unquote` / `splice` — Lisp-aligned staging vocabulary

> Status: **draft / proposal** (2026-06-27). Companion to
> `2026-06-27-live-staged-templates-design.md`. Renames the binding-time surface from the
> invented `Live`/`Inline`/`AsSyntax` vocabulary to the Lisp quasiquotation vocabulary Synto
> already half-speaks.

## 1. Motivation

Synto's core verb is already **quote**: `CSharpSyntaxQuoter`, `TemplateSyntaxQuoter`, and the
whole `[Template]` body — which is a *quoted* C# fragment turned into syntax-building code. A
quotation system's natural escape hatches are **unquote** (drop out of the quote, evaluate at
the meta level, insert the result) and **unquote-splicing** (splice a pre-built fragment /
sequence in). Lisp spells these `` ` `` (quasiquote), `,` (unquote), `,@` (unquote-splicing).

The current surface invented its own words — `Live`, `[Inline]`, `AsSyntax` — that don't
connect to `quote`, and worse, **conflate two distinct operations behind one boolean flag**
(see §3). This spec replaces them with the quote/unquote/splice vocabulary.

## 2. The model

| Lisp | Synto | Meaning |
|---|---|---|
| `` `(…) `` quasiquote | the `[Template]` body | a quoted fragment with holes |
| `,x` unquote | **Unquote** | escape the quote: evaluate `x` at factory time, **lift the value** back into the quote (as a literal / via `ToSyntax`) |
| `,@x` unquote-splicing | **Splice** | insert a **pre-built `ExpressionSyntax`** (or sequence) **verbatim** — no evaluation, no lift |

The distinction C# forces (and Lisp hides via homoiconicity): **Unquote takes a *value* and
must lift it to syntax; Splice takes *syntax* and inserts it as-is.** Two different input
types ⇒ two named operations.

## 3. The key insight: `AsSyntax` was an Unquote-vs-Splice switch

Reading the emitter (`TemplateFactorySourceGenerator`, the `InlinedParameterFinder` branch):

- `[Inline] T x` **without** `AsSyntax` → factory param keeps type `T`; every use of `x` is
  replaced by `x.ToSyntax()` (or a `[Runtime]` converter, or the generic `ToSyntax<T>`
  fallback). **This is a value lift = Unquote.**
- `[Inline] ExpressionSyntax x` **with** `AsSyntax = true` → factory param is typed
  `ExpressionSyntax`; uses of `x` are replaced by `x` verbatim. **This is a splice = Splice.**

So today's three "lift a value to syntax" surfaces are **the same operation**:

- `Parameter<T>()` — the external-argument flavor (becomes a factory parameter).
- `Live<T>(expr)` — the internal factory-time-computation flavor (a real runtime local).
- `[Inline]` *without* `AsSyntax` — the attribute-on-parameter flavor of the same external lift.

…and the one genuinely different surface is `[Inline]` **with** `AsSyntax = true`.

**Consequence:** renaming `[Inline]` → `[Splice]` wholesale would be *wrong* — only the
`AsSyntax = true` half is a splice. The boolean dissolves: choosing **Unquote** vs **Splice**
*is* the choice `AsSyntax` used to encode.

## 4. Proposed surface

### Unquote (value → lift)

| Form | Spelling | Replaces |
|---|---|---|
| internal factory-time value | `Unquote<T>(T value)` → `var n = Unquote(2 + 3);` | `Live<T>()` |
| external factory argument | `Parameter<T>()` *(unchanged)* | — |
| value-lift parameter (attr) | folds into `Parameter` / `Unquote`; the old `[Inline]`-without-`AsSyntax` ceases to be its own thing | `[Inline]` (default) |

`Unquote` and `Parameter` stay distinct because they differ in *binding origin* (external arg
vs. internal computation), not in operation — both are unquote. (Open question §7: is the
attribute-on-method-parameter lift worth keeping as `[Unquote]`, or is `Parameter<T>()` enough?)

### Splice (syntax → splice verbatim)

| Form | Spelling | Replaces |
|---|---|---|
| splice a pre-built `ExpressionSyntax` | `[Splice] ExpressionSyntax x` (and/or `Splice(x)` body form) | `[Inline]` with `AsSyntax = true` |

This is the role the repo's `unquote-experiment` branch already prototyped as `[Unquote]` —
which was a **mis-name** under this model (it splices syntax, so it is **Splice**, not Unquote).
Adopting this spec renames that experimental `[Unquote]` → `[Splice]`, making it *more* correct.

### Quote (the dual, for completeness)

The `[Template]` body is the implicit quasiquote. No user-facing `quote(...)` is proposed here;
named only so the trio reads coherently.

## 5. Worked examples

```csharp
using Synto.Templating;
using static Synto.Templating.Template;

// Unquote: evaluate now, lift the value
[Template(typeof(Factory))]
void Build() {
    var n = Unquote(2 + 3);          // runs at factory time -> literal 5 in the OUTPUT
    System.Console.WriteLine(n);
}

// Splice: insert a pre-built ExpressionSyntax verbatim
[Template(typeof(Factory))]
void Wrap([Splice] ExpressionSyntax body) {
    System.Console.WriteLine(body);  // `body` spliced in as-is (no ToSyntax)
}
```

## 6. Impact

- **Identifiers / internal types.** `LiveAttribute` → (parameter escape-hatch keeps a marker;
  rename per §7); `Live<T>()` carrier → `Unquote<T>()`. `InlineAttribute`/`InlinedParameterFinder`
  /`InlinedTypeParameterFinder` → `Splice*` for the `AsSyntax=true` behavior; the `AsSyntax`
  boolean is removed. `LiveParameterFinder`/`LiveRootFinder` naming follows.
- **Generic-parameter inlining.** `[Inline]` also applies to **generic parameters**
  (`InlinedTypeParameterFinder`, `AttributeTargets.GenericParameter`) with its own `AsSyntax`.
  The same Unquote/Splice split applies on the type axis — **flagged as an open question** (§7),
  not decided here.
- **Diagnostics.** `SY####` messages and snapshot names that mention `Live`/`Inline`/`AsSyntax`
  re-baseline to the new words. (Snapshots pin spellings — do the rename **before** the relevant
  tasks freeze them.)
- **`using static`.** `Unquote`/`Parameter`/`Member`/`TypeOf` all hang off `Template`, so
  `using static Synto.Templating.Template;` exposes them together (unchanged pattern).

## 7. Open questions

1. **Keep a parameter-position lift marker?** The old `[Inline]`-without-`AsSyntax` (external
   value param, lifted) overlaps `Parameter<T>()`. Drop it in favor of `Parameter<T>()`, or
   keep an `[Unquote]` parameter attribute as sugar?
2. **Type-axis split.** Does generic-parameter inlining get the same `Unquote`/`Splice` split,
   or stay a single `[Inline]`-on-type for now?
3. **`Splice` body form.** Ship `[Splice]` (attribute) only, or also a `Splice(x)` body call so
   a spliced fragment can appear mid-expression like `Unquote`?
4. **`,@` sequence splicing.** The live-`foreach` unroller already splices *sequences* of
   statements — that is the literal `,@`. Is that purely internal, or is there a user `Splice`
   over a collection?

## 8. Out of scope

- No change to `[Template]`, `[Match]`, `[Runtime]`, or the cacheability model.
- No change to *what* the generator emits — this is a **naming/surface** refactor; emitted
  output and snapshots change only where they literally embed the old words.

## 9. Timing

`Live`/`Parameter` are mid-implementation on `experimental`. `Parameter<T>()` has **landed**
(commit `0d7d6fc8`); `Unquote` (née `Live`, Task 2) has **not** yet frozen snapshots. Renaming
`Live → Unquote` now is a cheap §1 re-baseline (same move already used once); after Task 2 lands
it costs a snapshot re-baseline. `Inline → Splice` is independent of the in-flight plan (Inline
is untouched by it) and can be a separate follow-up refactor.

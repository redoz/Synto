# Design: `[Quote]` — value-into-syntax without staging

**Date:** 2026-06-27
**Status:** APPROVED (brainstormed, owner-approved 2026-06-27; §6 resolved)
**Builds on:** `2026-06-27-quote-unquote-splice-naming.md` (the quote/unquote/splice rename, landed
on `experimental` as Phase 1 + Phase 2). This adds the **fourth** surface word that the rename
left implicit.

## 1. Problem

After the full fold (Phase 2), the only value-lifting parameter surface is `[Unquote]`
(plus its `Parameter<T>()` / `Unquote<T>(expr)` siblings). `[Unquote]` is a **staging root**:
its value is live at factory time, so any control construct it drives runs at factory time and
**unrolls** (`StagedFor_Unrolls`, `StagedWhile_Unrolls`, `RoundTripTests.Test5`).

That is correct for `[Unquote]` — but it is now the *only* behavior available. There is no way to
say "take this factory-time value, drop it into the emitted tree as a literal, and **leave my loop
as a runtime loop**." Concretely, `Loop(1000)` is forced to emit 1000 statements; there is no
compact-loop escape. The fold deleted the old value-substitution path (`[Inline]`) without leaving a
principled replacement for the *non-staging* half of it.

## 2. The conceptual model (why this is `Quote`, not a third thing)

In Lisp, `,n` (unquote) does two things at once: **evaluate** `n` at expansion time **and** drop the
result back into the template as a datum — free, because in a homoiconic language a value already *is*
syntax. C# has no homoiconicity, so that single `,` splits into two nameable operations:

- **unquote** — escape to stage-0 (factory time); the value stays *live* and can drive factory-time
  control flow (unroll / specialize). This is today's `[Unquote]`.
- **quote** — lift a stage-0 value *back into* stage-1 syntax (a literal for primitives; a constructed
  expression for `[Runtime]`-converted types, e.g. `Rgb` → `new Rgb(255)`; `typeof(T).ToTypeSyntax()`
  for a type). The surrounding structure stays quoted output.

Injecting a literal into the tree **is** quoting the value (`value.ToSyntax()` is a quote). So the
"keep the loop, put `4` in" behavior is named `[Quote]`, completing the `[Quote]` / `[Unquote]`
duality a Lisp reader recognizes: *quote = into the tree as data; unquote = escape the tree, stay
live.* Lisp's `,n`-that-keeps-the-loop is exactly our `[Quote]`; the unroll is the staging surplus
C# adds.

`[Quote]` over `[Literal]`: `[Literal]` is honest for `int`/`string` but a **misnomer** for the
`[Runtime]`-converter case (`new Rgb(255)` is not a literal). "Quote the value into its syntax form"
is accurate across primitives, converted types, and types; "Literal" is not.

## 3. Contract for `[Quote]`

`[Quote]` marks a `[Template]` value parameter (and its local/inline siblings). Its supplied value is
**quoted into the produced syntax** at every use site — identical lift to `[Unquote]` in *value*
position — but it is **never a staging root**:

- It does **not** seed liveness in the binding-time classifier.
- A control construct (`for` / `while` / `foreach` / `if`) whose driver references **only** quoted
  values stays **Quoted** (emitted as a real runtime control construct), never `StagedControl`.
- The value-lift itself is unchanged: primitive → literal; `[Runtime]`-converted type →
  constructor/converter call; (type axis — see §6).

**Behavioral pivot (the whole point):** `[Quote]` and `[Unquote]` are *observably identical in value
position* (both emit the literal `4`); they diverge *only* in a control driver:

| body | `[Quote] int n` | `[Unquote] int n` |
|---|---|---|
| `return n;` | `return 4;` | `return 4;` |
| `for (k=0; k<n; k++) body;` | `for (k=0; k<4; k++) body;` (loop kept) | unrolled (4× `body`) |

## 4. Surface forms (mirror the unquote trio)

The unquote surface is a trio: `[Unquote]` (attribute) · `Parameter<T>()` (caller-supplied
local/inline) · `Unquote<T>(expr)` (computed local/inline). `[Quote]` adds exactly **two** forms — the
value-parameter attribute and the inline boundary — and deliberately **no** caller-supplied declaration
form:

- **`[Quote]` attribute** on a **value** parameter (value axis only — see §6.1). Every use of the
  parameter is quoted (non-live), everywhere. Covers the common case:
  `static void Loop([Quote] int count) { for (int i = 0; i < count; i++) … }` → loop kept,
  `count` lifted to a literal bound.
- **`Quote(value)` inline form** mirroring `Unquote(value)` / `Splice(node)`: the explicit
  stage-0 → stage-1 boundary. `Quote(x)` emits the quoted syntax of factory-time value `x` *at this
  site* and **stops liveness there**, so the enclosing construct stays output. This is the only way to
  keep a loop whose bound is a **computed** factory-time value (no parameter to attribute):
  `var bound = Unquote(columns.Count); for (int i = 0; i < Quote(bound); i++) …` → runtime loop, not
  unrolled.

**No `Quote<T>()`.** A caller-supplied non-staging declaration form would only differ from
`Parameter<T>()` when used as a loop driver from a body position — and in *value* position the two emit
the identical literal. That narrow case is already expressible as `Parameter<T>()` + `Quote(…)` at the
site, so `Quote<T>()` is a near-synonym of `Parameter<T>()` and is dropped (YAGNI). `Parameter<T>()`
stays the lone caller-supplied-local form (live); re-quote it per-site with `Quote(…)` for a literal.

The two `[Quote]`/`Quote(value)` forms are non-overlapping: `[Quote]` quotes a *parameter* everywhere;
`Quote(value)` quotes *any factory-time value* (incl. a computed `Unquote` local) at a point. Pin the
contracts; let the implementer choose exact signatures.

## 5. What does NOT change (the commit stands)

This is **purely additive**. `[Unquote]` keeps its current live/unroll semantics; `Test5`,
`StagedFor_Unrolls`, `StagedWhile_Unrolls`, and the entire ObjectReader dog-food are all *genuinely*
live and stay exactly as committed (`experimental` @ `27c7b1d0`). Nothing is reverted. We are adding
the missing non-staging word, not changing the staging one.

## 6. Resolved design decisions (owner, 2026-06-27)

1. **Type axis → value parameters only.** `[Quote]` targets `AttributeTargets.Parameter` only, **not**
   `GenericParameter`. On the type axis `[Quote]` and `[Unquote]` would coincide observably (a type
   never drives a loop), so a `GenericParameter` target would be a pure synonym. `[Unquote]`/`[Splice]`
   already cover the type axis; `[Quote]` exists solely to express the value/control (literal-vs-unroll)
   distinction, which is value-axis-only.
2. **No `Quote<T>()`; ship `[Quote]` + `Quote(value)`** — see §4. The caller-supplied non-staging
   declaration form is redundant with `Parameter<T>()` + `Quote(…)` and is dropped.
3. **No diagnostics.** A `[Quote]` value used where the author might have expected an unroll just stays a
   runtime loop, silently. Intent is unknowable — quoting a loop bound is a legitimate, common intent,
   so there is nothing to warn about.

## 7. Testing (contracts to pin)

- A `[Quote] int n` in a `for`/`while` condition emits a **runtime loop** with `n` lifted to a literal
  bound — the compact-loop counterpart to `StagedFor_Unrolls` (compile-asserting, not snapshot-only).
- A `[Quote]` value in pure value position emits the **same** literal as `[Unquote]` (equivalence pin).
- A `[Quote]` of a `[Runtime]`-converted type (e.g. `Rgb`) emits the **constructor/converter call**,
  not a literal — guards the `[Literal]`-misnomer rationale.
- `[Quote]` is **not** a staging root: a construct driven solely by quoted values stays `Quoted` in the
  `BindingTimeClassifier` (mirror of the existing classifier tests).
- **Cacheability** holds: the new path captures no
  `Compilation`/`ISymbol`/`SemanticModel`/`SyntaxNode` into pipeline state (every tracked step
  `Cached`/`Unchanged` on an unrelated edit) — sacred per the naming spec §8.

## 8. Non-goals

- No change to `[Unquote]` / `[Splice]` semantics or names.
- `,@` collection-splicing (open question 4 of the naming spec) remains future scope; the unroll is its
  current implicit form.
- No re-fold or revival of `[Inline]`; `[Quote]` is the principled replacement for the non-staging
  half, not a rename of the old attribute.

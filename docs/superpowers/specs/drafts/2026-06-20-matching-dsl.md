# Matching — structural pattern-matching for C# syntax trees

> **Tracking issue:** #92

The second capability of the Synto umbrella generator (`Synto.Matching`), and the
**inverse of Templating**. Where Templating takes a phantom C# method body and emits a
factory that **builds** that tree, Matching takes a phantom C# method body and emits a
matcher that **recognizes** that tree on a `SyntaxNode` and hands the captured pieces
back, typed. A `[Match]` pattern recognizes exactly the trees the equivalent `[Template]`
could build — and recovers the holes.

This spec fixes the **authoring surface (the DSL)** and the **generation model**, and
draws the **v1 line** with everything beyond it explicitly designed-for.

---

## 1. Motivation & fit

`Matching` is the marquee next feature the architecture has reserved a sibling
namespace/folder for (`Templating/` today, `Matching/` next) inside the single umbrella
generator `src/Synto.SourceGenerator` (ships as `Synto`). It is not a new package: a
consumer adds one package and gets the toolkit.

The audience is Roslyn generator/analyzer authors who today hand-write
`node is IfStatementSyntax ifs && ifs.Condition is ...` cascades to recognize a shape and
pull pieces out. Matching lets them instead **write the shape as ordinary C#** with holes,
and get a typed matcher generated for them.

Why C#-as-pattern and not a string DSL (Semgrep/Comby style): keeping the pattern as real,
compilable C# preserves **compile-time checking of the pattern, IntelliSense, autocomplete,
and rename-refactoring** — all of which a string throws away. The cost (no `...`-style free
notation) is paid by the DSL design below.

## 2. Goals / non-goals

**Goals**
- A readable, compile-checked, IntelliSense-friendly authoring surface that *reads like the
  C# it matches*.
- Typed captures: a successful match yields a generated record with statically-typed
  members, not `SyntaxNode`s to cast.
- Purely **syntactic, structural** matching — no semantic/binding analysis in v1.
- Generation that fits the existing discipline: bespoke per-pattern expansion,
  self-contained output (no runtime dependency), equatable incremental pipeline, failures
  as diagnostics.

**Non-goals (v1)**
- Semantic matching (match by *type/symbol* rather than *shape*). Designed-for as an opt-in
  (§9), built later.
- A rewrite/replace API. Matching returns captures; rewriting is the consumer's job with the
  captured nodes (a future `Rewrite` feature could pair with it).
- Backtracking-heavy features (variable-length sequence matching, deep search) in the first
  cut — designed-for (§8), deferred.

## 3. The authoring surface (the DSL)

### 3.1 The load-bearing invariant

> **A hole is *only ever* a reference to a `[Capture]` parameter, or a call to a known
> `Synto.Matching` marker. Everything else in the body is literal syntax to match exactly.**

The generator recognizes a hole by **binding** — does this identifier resolve to a
`[Capture]` parameter? — never by overloading a type or a construct. This is what keeps the
DSL from **leaking**: because the audience writes code full of `StatementSyntax` locals,
`foreach`, `default`, etc., any "this type/shape means hole" rule would collide with literal
matched code. Binding never does.

This mirrors Templating exactly: there, non-`[Inline]` code is literal tree-to-build and
`[Inline]` params are the substitution points; here, non-`[Capture]` code is literal
shape-to-match and `[Capture]` params are the blanks.

### 3.2 The attribute and match shape

```csharp
[Match<TMatcher>(MatchOption.Bare)]   // generic attribute (C# 11)
static void PatternName(/* [Capture] holes */) { /* literal shape with holes */ }
```

`MatchOption` mirrors `TemplateOption` — it selects *which slice of the pattern method is
the shape*, hence *what the matcher's root accepts* and *how it relates to its scope*:

| Option | Matches | Scope relationship |
|---|---|---|
| `None` (default) | a method / local-function **declaration** | the whole declaration, fully bounded by its own `{ }` — nothing hidden |
| `Bare` | a run of statements **contained in a block** | **"contains": matches the run wherever it sits in the block** unless anchored |
| `Single` | a single `StatementSyntax` / expression | one node *somewhere* in its block |

The pattern method's **own name** names the generated matcher; it is never matched
literally. In `None`, the method body's braces fully bound the match; in `Bare`/`Single`
the surrounding scope is *not* part of the pattern, which is what makes anchors (§3.9)
meaningful there.

### 3.3 Expression captures

A `[Capture]` parameter is an expression hole; it appears as a **bare identifier** in the
body (no parens). Its declared CLR type is **compile-glue + IntelliSense** — it makes the
body compile and lights up autocomplete — and the captured result is an `ExpressionSyntax`.

```csharp
[Capture] Dictionary<object, object> dict   // dict.ContainsKey(...) autocompletes; capture = ExpressionSyntax
[Capture] object key                        // when you don't constrain the position's type
[Capture] dynamic anything                  // when you want member access without naming a type (no IntelliSense)
```

Do **not** put a `SyntaxNode` type on the param (`[Capture] BinaryExpressionSyntax x`) — it
won't compile where the matched code expects a `bool`/`int`/etc. To **narrow** the matched
kind and type the captured member, use the generic-attribute form:

```csharp
[Capture<BinaryExpressionSyntax>] object sum   // only matches a binary expr; result member typed BinaryExpressionSyntax
```

### 3.4 Statement captures and the quantifier vocabulary

A statement-shaped hole is a `[Capture]` parameter referenced at a statement position via a
**fluent quantifier**. The quantifier (a) compiles in statement position, (b) reads as a
deliberate cardinality (not empty `()` noise), and (c) determines the result-member type:

| placement | matches | result member |
|---|---|---|
| `then.One()` | exactly one statement | `StatementSyntax` |
| `tail.Opt()` | zero-or-one | `StatementSyntax?` |
| `body.Some()` | one-or-more | `SyntaxList<StatementSyntax>` |
| `rest.All()` | zero-or-more | `SyntaxList<StatementSyntax>` |
| `mid.Exactly(n)` | exactly n | `SyntaxList<StatementSyntax>` |

```csharp
[Match<M>(MatchOption.Bare)]
static void Guarded([Capture] object cond, [Capture] Stmt guard, [Capture] Stmt rest)
{
    if (cond)
        guard.One();     // legal as an un-braced embedded statement (it's an invocation)
    rest.All();          // the remaining run
}
```

Because the quantifier is an *invocation*, it is legal anywhere a statement is — including
un-braced `if`/`else`/`while` bodies (where a local-declaration form would error CS1023).
Subtlety accepted: **the quantifier method determines the result type** (`.One()`→single,
`.All()`→list, `.Opt()`→nullable). Mildly magic, the price of one hole type instead of
several. `AtLeast(n)` / `Between(a,b)` are obvious extensions; not built in v1.

### 3.5 Wildcards (match, don't capture)

The **static** form of the same quantifier vocabulary matches but doesn't surface:

```csharp
Statement.All();      // a run of statements I don't care about
Statement.One();      // exactly one, ignored
Expr.Any<bool>();     // a wildcard expression (or `default` in expression position)
```

So the verb reads the same everywhere; *static = wildcard, instance-on-a-`[Capture]` =
capture*. This replaces any discard-named-capture hack. (Mechanical note for
implementation: C# forbids a static and an instance method of the same name on one type, so
capture and wildcard live on two parallel marker surfaces sharing the verbs — names
bikesheddable.)

### 3.6 Non-linear / equality (free)

Reuse a `[Capture]` more than once and the two sites must be **syntactically equal**; the
generator emits one `IsEquivalentTo` check. No special notation — C#'s variable identity
expresses it:

```csharp
static void SelfCompare([Capture] object x) { _ = x == x; }   // matches <e> == <e>, both sides equal
```

### 3.7 Repetition / nested ellipsis (`foreach`)

A phantom `foreach` over a sequence capture means "a run of this sub-pattern, capturing each
iteration's holes as parallel lists" — Shinn's `(append x) ...`, written in native C#:

```csharp
[Match<M>(MatchOption.Bare)]
static void Concat([Capture] StringBuilder sb, [Capture] Many<object> parts)
{
    foreach (var part in parts)     // zero-or-more of…
        sb.Append(part);            // …this shape, capturing each `part`
}
// → result has IReadOnlyList<ExpressionSyntax> Parts
```

This is leak-free by §3.1: a `foreach` is "repetition" *only* when it iterates a `[Capture]`
param; a literal `foreach` over a normal collection is matched literally. (The backtracking
lowering this requires is **deferred** — see §8.)

### 3.8 Optional

`Opt()` (statement) and nullable holes express "may be absent"; the capture comes back
nullable.

### 3.9 Anchors — `Block.Start()` / `Block.End()`

In `Bare`/`Single`, the surrounding scope is not written, so position relative to it is
expressed with marker calls (same family as `Statement.All()` / `Deep()`):

```csharp
[Match<M>(MatchOption.Single)]
static object TrailingReturn([Capture] object result)
{
    return result;
    Block.End();      // this return is the LAST statement of its block
}
```

`Block.Start()` before the first matched statement = "…is first in its block"; both =
"exactly this block." Anchors are how a `Bare` "contains" match is pinned to a block's
start/end.

- In **`None`** the body's braces already bound the match completely, so anchors are a
  usage error → **diagnostic `SY1008`** (§9). "No hidden before/after" is precisely the
  `None` invariant.
- One accepted wrinkle: `Block.End()` after a `return`/`throw` is unreachable code
  (CS0162). The pattern body is phantom (never runs), so it is a suppressed warning, the
  same bucket as the unused-local/unused-function suppressions Templating bodies already
  carry.
- `Block` is the statement-scope flavor; the same idea generalizes to "first/last argument"
  etc. if needed.

### 3.10 Directives

`Deep(() => …)` — sub-pattern at any depth (Shinn's `***`). `Either(() => A(), () => B())`
— alternation. These look like calls *on purpose*: they are directives *about* the pattern,
not part of the matched C#.

### 3.11 The generated result and consumer usage

Each pattern generates a bespoke nullable result record; non-null only on a successful
match, members typed per the captures:

```csharp
public sealed record GuardedMatch(ExpressionSyntax Cond, StatementSyntax Guard,
                                  SyntaxList<StatementSyntax> Rest);

partial class M { public static GuardedMatch? Guarded(SyntaxNode node) { /* … */ } }
```

```csharp
foreach (var node in root.DescendantNodes())
    if (M.Guarded(node) is { } m)
        Use(m.Cond, m.Guard, m.Rest);   // typed; can't read captures without proving the match
```

Value guards/predicates are deliberately **out of the DSL** — you get a typed record, so
`M.Foo(node) is { X: var x } && Good(x)` covers them in plain C#. (record `class` vs
`readonly record struct` is an implementation detail, §10.)

## 4. Matching semantics

- **Purely syntactic, structural** — kind-by-kind, child-by-child. No semantic equivalence
  (`i++` does not match `i = i + 1`). Mirrors the quoter.
- **Trivia-insensitive** — whitespace/comments ignored.
- **Rooted at the node you hand it** — `M.Foo(node)` asks "does *this* node match?"; it does
  not search the subtree (that's the consumer's `DescendantNodes()` loop, or `Deep`).
- **`None`** is exact and fully bounded; **`Bare`** is *contains* (a run anywhere in a
  block) unless anchored; **`Single`** is one node anywhere in its block unless anchored.

## 5. Generation model — bespoke expansion

Each `[Match]` pattern is **compiled away into straight-line Roslyn**, exactly as Shinn's
macro expands a pattern into specialized code, and exactly as Templating already emits
bespoke `SyntaxFactory` code. There is **no generic runtime engine** and no descriptor
indirection — Synto patterns are statically authored (known at generation time), so a
runtime interpreter would buy nothing.

```csharp
public static GuardedMatch? Guarded(SyntaxNode node)
{
    if (node is not BlockSyntax { Statements: { Count: >= 1 } stmts }) return null;
    if (stmts[0] is not IfStatementSyntax ifs) return null;
    // … straight-line structural tests; non-linear → IsEquivalentTo; sequence → list slice …
    return new GuardedMatch(/* captured nodes */);
}
```

Consequences:
- **Self-contained output** — like Templating, generated matchers reference only Roslyn
  (already present in the consumer's generator), no Synto runtime package. The consumer
  surface (`[Match]`, `[Capture]`, markers) is **injected as `internal` source** via
  `RegisterPostInitializationOutput`, under `namespace Synto.Matching`, the same mechanism
  as the Templating surface.
- **Reads like hand-written Roslyn** — steppable, debuggable.
- **Hard logic lives in the emitter** (the pattern→code lowering), tested via round-trip +
  golden snapshots over generated matchers — the same test model Templating already uses.

## 6. Incremental-pipeline discipline

Matching obeys the same cacheability contract as Templating: all semantic work happens
inside the `ForAttributeWithMetadataName(typeof(MatchAttribute).FullName, …)` transform,
which flows out an **equatable** result (generated text + `EquatableArray<DiagnosticInfo>`).
No `Compilation` / `ISymbol` / `SemanticModel` / `SyntaxNode` is captured into pipeline
state.

## 7. Layering & packaging

- Lands as `src/Synto.SourceGenerator/Matching/` (namespace `Synto.Matching.*`), a sibling
  of `Templating/` inside the one umbrella generator. **No new package.**
- The consumer markers (`MatchAttribute`/`Match<T>`, `CaptureAttribute`/`Capture<T>`,
  `Stmt`/`Many`, the `Statement`/`Expr`/`Block` static marker holders, `MatchOption`,
  `Deep`/`Either`) are authored once in `src/Synto` under `namespace Synto.Matching`,
  embedded as resources, and injected `internal`/file-scoped — mirroring the Templating
  surface single-source-of-truth.
- No forced runtime dependency; generated output stays dependency-free.

## 8. v1 scope (the line)

The lowering splits cleanly by difficulty. **v1 = the straight-line half (no backtracking):**

- `MatchOption.None | Bare | Single` with the §4 semantics.
- Expression captures (`[Capture] T x`, `[Capture<TNode>]` narrowing) and expression
  wildcards.
- Statement captures via the flat quantifiers `One/Opt/Some/All/Exactly`, and their static
  wildcard forms.
- **Non-linear** equality (one `IsEquivalentTo`).
- **Anchors** `Block.Start()`/`Block.End()` + the `SY1008` validation.
- The generated nullable result record and the bespoke straight-line matcher.

## 9. Designed-for growth (deferred, but the surface must not preclude)

- **`foreach` repetition / nested ellipsis** (§3.7) — needs emitted backtracking; the
  notation is fixed now so v2 only adds the lowering.
- **Deep / descendant** `Deep(…)` — subtree search.
- **`Either` / structural optional** — alternation and optional clauses.
- **Token / identifier capture** — capturing a *name* (a reused `var x` for non-linear
  declared locals, or a matched method's name). The one genuinely-separate capability; the
  rest of the model already accommodates it.
- **Negation** — the real need behind a `None`-quantifier ("a block where *none* of the
  statements is a `throw`", "an `if` with no `await` inside"); folds in with guards as
  `not`.
- **Semantic-constraint opt-in** — `[Capture(MatchType = true)]`-style, threading a
  `SemanticModel`, to match by type/symbol rather than shape.
- **Comma-list ellipsis** and **bounded counts** (`AtLeast`/`Between`).

## 10. Open questions (bikeshed, not blockers)

- Final names: `Stmt`/`Many` vs `Statement`/`Statements`; the capture-vs-wildcard parallel
  marker surfaces; `Block.Start/End`.
- Result type: `record class` vs `readonly record struct`.
- Statement-hole reference form is settled as the fluent quantifier (`rest.All()`); the
  paren-free `_ = rest;` alternative was rejected (reads as a throwaway assignment).

## 11. Diagnostics

New `SY1xxx` usage diagnostics, in the hand-written `Diagnostics` family (or via dogfooded
`Synto.Diagnostics` later). At least:

- **`SY1008` — anchor not allowed.** `Block.Start()`/`Block.End()` used in a `None` pattern
  (already fully bounded). Located on the marker call.
- Further arms as the implementation surfaces them (e.g. capture referenced but never placed;
  conflicting quantifiers on one hole; `Bare` body empty).

All generation failures are converted to a `DiagnosticInfo` (`SY0000` internal-error
catch-all), never thrown.

## 12. Testing strategy

- **Golden snapshots** of generated matchers (`*.verified.cs`) per pattern shape, the
  executable spec of output — same harness as Templating.
- **Round-trip / behavioral**: build a known tree, assert the generated matcher matches it
  and returns the expected captures; assert near-miss trees *don't* match.
- **Cacheability**: a driver re-run on an unrelated edit yields cached steps
  (equatability of the pipeline model).
- **Diagnostics**: a driver test per `SY1xxx` arm (esp. `SY1008`).

## 13. Risks & consequences

- **Emitter complexity migrates the hard logic into codegen.** Mitigated by shipping the
  straight-line v1 first and by the established snapshot/round-trip test model; Shinn proves
  bespoke ellipsis-from-a-macro is tractable.
- **Surface is a one-way door once consumers compile against it.** The marker shape,
  generated record shape, and `Synto.Matching` namespace are compatibility boundaries — pinned
  by snapshots, changed freely only pre-consumer. This spec fixes them deliberately.
- **`Bare` = contains** is a deliberate, regex-like default; anchors are the pins. Chosen so
  "a block containing this" needs no boilerplate.

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
- Backtracking-heavy sequence matching — a statement run with **more than one**
  variable-length quantifier (`Some`/`All`/`Opt`), or a variable-length quantifier abutting
  content it could also consume — and deep search, in the first cut: designed-for (§8/§9),
  deferred. v1 *does* support a run with **at most one** variable-length quantifier (the
  single greedy split is straight-line; §3.4, §8).

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

`MatchOption` is a **plain (non-`[Flags]`) enum** that mirrors the *concept* of
`TemplateOption`, not its bit layout. Templating's `TemplateOption` is `[Flags]` with
`Single = 2 | Bare` (Single *implies* Bare); Matching's `None`/`Bare`/`Single` are
**mutually-exclusive cardinalities** (one node vs a run vs a whole declaration), so a flags
shape would be semantically wrong here. It selects *which slice of the pattern method is the
shape*, hence *what the matcher's root accepts* and *how it relates to its scope*:

| Option | Matches | Scope relationship |
|---|---|---|
| `None` (default) | a method / local-function **declaration** | the whole declaration, fully bounded by its own `{ }` — nothing hidden |
| `Bare` | a run of statements **contained in a block** | **"contains": matches the run wherever it sits in the block** unless anchored |
| `Single` | a single `StatementSyntax` / expression | a statement in its block, or the handed expression node itself (§4) |

The pattern method's **own name** names the generated matcher; it is never matched
literally. In `None`, the method body's braces fully bound the match; in `Bare`/`Single`
the surrounding scope is *not* part of the pattern, which is what makes anchors (§3.9)
meaningful there.

> **Open question — generic-attribute discovery on the Roslyn 5.0 floor (unverified).**
> `[Match<TMatcher>]` is the generic attribute `MatchAttribute<T>` (metadata name
> `` MatchAttribute`1 ``), and the pipeline reads `TMatcher` from its type argument. Whether
> `ForAttributeWithMetadataName` reliably matches a *generic* attribute and surfaces the type
> argument **on the 5.0 floor** is not yet confirmed (§10). If it doesn't, the fallback is the
> **non-generic** `[Match(typeof(TMatcher))]` form — exactly how Templating's
> `[Template(typeof(Factory))]` already passes its target through a ctor `typeof` arg —
> costing only a slightly less elegant surface, no model change. (`[Capture<TNode>]`'s type
> arg, §3.3, is read from the parameter's `AttributeData` via the symbol API — a different,
> safer path not gated by this risk.)

### 3.3 Expression captures

A `[Capture]` parameter is an expression hole; it appears as a **bare identifier** in the
body (no parens). Its declared CLR type is **compile-glue + IntelliSense** — it makes the
body compile and lights up autocomplete — and the captured result is an `ExpressionSyntax`.

```csharp
[Capture] Dictionary<object, object> dict   // dict.ContainsKey(...) autocompletes; capture = ExpressionSyntax
[Capture] object key                        // when you don't constrain the position's type
[Capture] dynamic anything                  // when you want member access without naming a type (no IntelliSense)
```

> **Slot-typing rule.** A capture's declared (compile-glue) type must be **valid in the
> syntactic slot it occupies** — `[Capture] bool cond` for an `if`/`while` condition,
> `[Capture] int i` for an `int` position, etc. — precisely because the body must compile
> (§1/§2). `object` works only where `object` is assignable (a bare argument, an `==` operand,
> a `return` of an `object`-typed method), **not** in a `bool` slot (`if (cond)` over an
> `object` is CS0266). This is the same constraint that forbids a `SyntaxNode`-typed param
> below; it does not change the capture's *result* type, which is always `ExpressionSyntax`
> (or the narrowed node type below).

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
static void Guarded([Capture] bool cond, [Capture] Stmt guard, [Capture] Stmt rest)
{
    if (cond)            // [Capture] bool — the hole sits in a bool slot (slot-typing rule, §3.3)
        guard.One();     // legal as an un-braced embedded statement (it's an invocation)
    rest.All();          // the remaining run
}
```

Because the quantifier is an *invocation*, it is legal anywhere a statement is — including
un-braced `if`/`else`/`while` bodies (where a local-declaration form would error CS1023).
Subtlety accepted: **the quantifier method determines the result type** (`.One()`→single,
`.All()`→list, `.Opt()`→nullable). Mildly magic, the price of one hole type instead of
several. `AtLeast(n)` / `Between(a,b)` are obvious extensions; not built in v1.

**v1 straight-line line (no backtracking).** A matched run may contain **at most one
variable-length quantifier** — `Some` = 1+, `All` = 0+, **and `Opt` = 0–1** (an `Opt`
contributes 0 *or* 1 to the run length, so it is **variable-length, not fixed-arity**). The
surrounding **fixed-arity** elements (`One`, `Exactly(n)`, and literal statements) consume
known counts, pinning the one variable-length hole's boundaries, so a single greedy forward
pass splits the run **deterministically** — no backtracking. **Two** variable-length
quantifiers in one run (`guard.Opt(); body.All();`, two `Opt`s, `Some` + `All`, …), or a
variable-length quantifier abutting content it could also match (an ambiguous split), leaves
the run length under-determined — one equation, two unknowns — so it needs the deferred
backtracking lowering and is rejected in v1 with **`SY1204`** (§8, §11), the same "reachable
construct, deferred lowering, clean diagnostic" treatment as the phantom `foreach` (§3.7).
This is also what keeps §4's "leftmost" position rule unambiguous: with one variable-length
hole the split point is unique; with two it isn't, which is exactly why that shape is rejected
rather than matched.

### 3.5 Wildcards (match, don't capture)

The **static** form of the same quantifier vocabulary matches but doesn't surface:

```csharp
Statement.All();      // a run of statements I don't care about
Statement.One();      // exactly one, ignored
Expr.Any<bool>();     // the wildcard expression — a marker call (recognized by binding, §3.1)
```

So the verb reads the same everywhere; *static = wildcard, instance-on-a-`[Capture]` =
capture*. This replaces any discard-named-capture hack. `Expr.Any<T>()` is the **sole**
expression-wildcard spelling — there is **no** bare-`default`-means-wildcard rule, so a literal
`default` (e.g. `return default;`, `x = default`) always stays literal, preserving the §3.1
invariant with no exception. (Mechanical note for
implementation: C# forbids a static and an instance method of the same name on one type, so
capture and wildcard live on two parallel marker surfaces sharing the verbs — names
bikesheddable.) Wildcard quantifiers obey the same v1 straight-line line as captures (§3.4):
at most one variable-length wildcard per run.

### 3.6 Non-linear / equality (free)

Reuse a `[Capture]` more than once and the two sites must be **syntactically equal**; the
generator emits one `IsEquivalentTo` check. No special notation — C#'s variable identity
expresses it:

```csharp
[Match<M>]   // None (default): matches a method declaration whose body is `_ = <e> == <e>;`
static void SelfCompare([Capture] object x) { _ = x == x; }   // the two <e> are one reused hole → equal
```

This is a `None` pattern (§3.2), so the matcher roots on the whole `SelfCompare` declaration;
the `_ = …;` is a **literal** discard-statement hosting the two equality holes, and the reuse
of `x` is what collapses both sides to a single `IsEquivalentTo`.

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
// → result captures each `part` as a run; the run collection type is settled with the v2
//   foreach lowering, aligned to the v1 statement-run abstraction (§10)
```

This is leak-free by §3.1: a `foreach` is "repetition" *only* when it iterates a `[Capture]`
param; a literal `foreach` over a normal collection is matched literally. (The backtracking
lowering this requires is **deferred to v2** — see §8/§9. `Many<T>` is not injected in v1, and
a phantom `foreach` over a `[Capture]` is one of the two deferred constructs reachable on a
native-C# path, so v1 rejects it with `SY1203` (§11) until the lowering ships.)

### 3.8 Optional

`Opt()` (statement) and nullable holes express "may be absent"; the capture comes back
nullable. `Opt` is **variable-length** (0 or 1) and so counts against the §3.4 one-per-run
straight-line line.

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
start/end. (The `TrailingReturn` body above is block-bodied, so it is a **statement**-`Single`
matching a `return` statement; the expression-`Single` form is the arrow body in §4.)

- In **`None`** the body's braces already bound the match completely, so anchors are a
  usage error → **diagnostic `SY1201`** (§11). "No hidden before/after" is precisely the
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
not part of the matched C#. Both are **deferred markers** — not injected in v1; each is
authored *with* its lowering (§7, §9).

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
- **A single local test on the handed node — never a subtree walk.** `M.Foo(node)` is one
  deterministic test on the node you pass; it never recurses into descendants hunting for a
  match. Searching a whole tree stays the consumer's `DescendantNodes()` loop (or, later,
  `Deep`). What counts as "the handed node" differs by option:
  - **`None`** — the node *is* the candidate declaration (method / local function); the
    matcher tests it **exactly and fully bounded**. One node in, match-or-null out.
  - **`Bare`** — the node *is* the candidate `BlockSyntax`; the matcher looks for the
    pattern's statement run **contained** in that block, scanning **offsets within that one
    block only** (it does not descend into nested blocks). Unanchored (§3.9), several offsets
    may satisfy — the matcher commits to the **leftmost** and returns **exactly one** result.
    This single deterministic position is load-bearing: an undefined choice would flap
    snapshots and break caching. The §3.4 single-variable-length-quantifier rule (enforced by
    `SY1204`) is what keeps "leftmost" itself well-defined — two variable-length holes would
    make the split point undefined, so that shape is rejected, not matched.
    `Block.Start()`/`Block.End()` pin the run to the block's first/last edge.
  - **`Single` (statement)** — rooted on the candidate **block**; matches one of the block's
    **direct** statements at the **leftmost** satisfying position (unless anchored). Block-
    rooting is what gives the `Block.Start()`/`Block.End()` anchors (§3.9) a scope to pin to.
  - **`Single` (expression)** — rooted on the **handed node itself**: `M.Foo(node)` tests
    whether `node` *is* the expression shape (e.g. `node is BinaryExpressionSyntax { Left: <a>,
    Right: <b> }` for `a + b`), returning the captures or null. It is **not** block-scoped — an
    expression sits arbitrarily deep inside a statement, so the consumer drives it with a
    `DescendantNodes()` loop (§3.11), handing each candidate node to the matcher (the
    expressions analyzer authors recognize — binary/conditional/member-access — are reached
    exactly this way). Anchors are statement-scope (§3.9), so they do not apply to an
    expression `Single`. The matcher's input contract — *hand it the expression node* — is part
    of the frozen surface (§13).

**Authoring an expression-`Single`.** Use an **expression-bodied** pattern method — the arrow
body *is* the matched expression:

```csharp
[Match<M>(MatchOption.Single)]
static object Sum([Capture] object a, [Capture] object b) => a + b;   // matches `<a> + <b>`
```

The arrow form (`=> shape`) is what tells an expression-`Single` apart from a
statement-`Single` (a block body with one statement, §3.9) and from a literal expression
statement: the body is an *expression*, not a statement, so there is no `_ = expr;` discard
collision (§10 rejected `_ =` only as a *statement-hole* spelling). Each `[Capture] object`
here sits in an operand slot (slot-typing rule, §3.3) and is captured as `ExpressionSyntax`.
The generated `Sum(SyntaxNode node)` then roots on the handed node (the bullet above) —
returning non-null exactly when `node` is the `<a> + <b>` shape — so the consumer feeds it from
a `DescendantNodes()` loop, never from a block.

This reconciles the §5 expansion (which roots on `node is BlockSyntax` and indexes its
statements) with "rooted at the node you hand it": for `Bare` and statement-`Single` the
matcher roots on, and scans within, *that one* block — it does not search the subtree below it.
An expression-`Single` instead roots on the handed expression node directly (the bullet above),
still a single local test, never a subtree walk.

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
inside the `ForAttributeWithMetadataName` transform (keyed on the `[Match]` attribute's
metadata name — generic-attribute discovery on the 5.0 floor is the open question in
§3.2/§10), which flows out an **equatable** result (generated text +
`EquatableArray<DiagnosticInfo>`). No `Compilation` / `ISymbol` / `SemanticModel` /
`SyntaxNode` is captured into pipeline state.

## 7. Layering & packaging

- Lands as `src/Synto.SourceGenerator/Matching/` (namespace `Synto.Matching.*`), a sibling
  of `Templating/` inside the one umbrella generator. **No new package.**
- **v1's injected consumer surface** — authored once in `src/Synto` under
  `namespace Synto.Matching`, embedded as resources, and injected `internal`/file-scoped
  (mirroring the Templating surface single-source-of-truth) — is exactly the markers v1
  lowers: `MatchAttribute`/`Match<T>`, `CaptureAttribute`/`Capture<T>`, the `Stmt` statement
  quantifier holder, the `Statement`/`Expr`/`Block` static marker holders, and `MatchOption`.
- The **deferred** markers (`Deep`, `Either`, the `Many<T>` sequence-capture type) are
  **not** injected in v1 — each is authored and injected *with* its lowering (§8, §9), so the
  v1 surface never carries an inert marker on a reachable misuse path. The notation is
  designed now; only the *source* is withheld until the lowering ships.
- No forced runtime dependency; generated output stays dependency-free.

## 8. v1 scope (the line)

The lowering splits cleanly by difficulty. **v1 = the straight-line half (no backtracking):**

- `MatchOption.None | Bare | Single` with the §4 semantics.
- Expression captures (`[Capture] T x`, `[Capture<TNode>]` narrowing) and expression
  wildcards.
- Statement captures via the flat quantifiers `One/Opt/Some/All/Exactly`, and their static
  wildcard forms — **with at most one variable-length quantifier (`Some`/`All`/`Opt`) per
  matched run** (the single-greedy-split straight-line subset; §3.4). A run with two
  variable-length quantifiers, or a variable-length quantifier abutting content it could also
  consume, needs backtracking and is rejected with `SY1204`.
- **Non-linear** equality (one `IsEquivalentTo`).
- **Anchors** `Block.Start()`/`Block.End()` + the `SY1201` validation.
- **Diagnostics**: `SY1201` (anchor misuse), the *provable-contradiction* subset of `SY1202`
  (unsatisfiable pattern) the straight-line lowering can detect for free, `SY1203` (a phantom
  `foreach` over a `[Capture]` — repetition not yet lowered), and `SY1204` (a statement run
  whose quantifier placement needs backtracking). The two `SY120x` not-yet-supported arms
  cover the deferred constructs still reachable on a native path.
- The generated nullable result record and the bespoke straight-line matcher.

**v1's injected surface is exactly what v1 supports.** The deferred markers (`Deep`,
`Either`, the `Many<T>` sequence-capture type) are **not** authored into the injected surface
until their lowering lands (§7, §9) — so a v1 consumer simply cannot name them (they don't
compile), and there is no inert-marker-on-a-reachable-path to guard. The exceptions are the
constructs built from **always-available** pieces — native `foreach`, and the v1 quantifiers
composed past the straight-line line: a consumer *can* write a phantom `foreach` over a
`[Capture]` (§3.7), or a run with two variable-length quantifiers (§3.4), before their
backtracking lowering exists. Each such reachable deferred path degrades to a clean
diagnostic — **`SY1203`** / **`SY1204`** (§11) — never a literal mis-match or an
unimplemented-arm throw.

## 9. Designed-for growth (deferred, but the surface must not preclude)

The notation for each deferred capability is **designed** now — so v2 only adds the lowering,
not a surface redesign — but its markers are **injected only when that lowering lands** (§7).
"Designed-for" means the *shape is reserved and proven to fit the model*, not *shipped inert*;
v1 does not author `Deep`/`Either`/`Many<T>` into the consumer surface.

- **`foreach` repetition / nested ellipsis** (§3.7) — needs emitted backtracking; the
  notation is fixed now so v2 only adds the lowering. One of the two deferred constructs
  reachable in v1 (native `foreach`), so v1 rejects it with `SY1203` (§11) until then.
- **Multi-variable-length / ambiguous-split statement runs** (§3.4) — two variable-length
  quantifiers in a run (`Some`/`All`/`Opt`), or one abutting content it could also match; the
  other reachable deferred construct, rejected with `SY1204` until the backtracking lowering
  ships.
- **Deep / descendant** `Deep(…)` — subtree search. Marker injected with its lowering.
- **`Either` / structural optional** — alternation and optional clauses. Markers injected with
  their lowering.
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

- **Generic-attribute discovery on Roslyn 5.0 — must verify before planning commits to the
  generic form.** Does `ForAttributeWithMetadataName("Synto.Matching.MatchAttribute`1", …)`
  match `[Match<M>]` and expose `M` as the attribute's type argument on the 5.0 floor? If not,
  fall back to the non-generic `[Match(typeof(M))]` (§3.2), as Templating already does for its
  target. (`[Capture<TNode>]` reads its type arg from the parameter `AttributeData`, a
  different and safer path.) The model is unchanged either way; only the surface spelling
  differs.
- **Captured-run collection type.** v1 statement quantifiers (`Some`/`All`/`Exactly`) yield
  `SyntaxList<StatementSyntax>`; the deferred `foreach` repetition (§3.7) must align to **one**
  captured-run abstraction when its lowering lands, **not** freeze a second one
  (`IReadOnlyList<T>` vs `SyntaxList<T>`) inconsistently across the two halves. Settling the
  unified type is deliberately deferred to the v2 `foreach` lowering, with `SyntaxList<T>` the
  v1 anchor.
- Final names: `Stmt`/`Many` vs `Statement`/`Statements`; the capture-vs-wildcard parallel
  marker surfaces; `Block.Start/End`.
- Result type: `record class` vs `readonly record struct`.
- Statement-hole reference form is settled as the fluent quantifier (`rest.All()`); the
  paren-free `_ = rest;` alternative was rejected (reads as a throwaway assignment). That
  rejection is about the *statement-hole* spelling only — an expression-`Single` is authored
  with an expression body (`=> shape`, §4), never `_ = expr;`.

## 11. Diagnostics

New usage diagnostics in a **per-feature `SY12xx` range reserved for Matching**, kept clear of
Templating's `SY0000`/`SY1001`–`SY1009` (the `[Template]`/`[Runtime]` family). Note
`SY1008`/`SY1009` are **already taken** by the `[Runtime]` converter checks
(`Diagnostics.cs`, registered in `AnalyzerReleases.Unshipped.md`), so Matching must **not**
reuse them; carving its own `SY12xx` range keeps the two features' IDs from ever colliding as
both grow. They live in the hand-written `Diagnostics` family (or via dogfooded
`Synto.Diagnostics` later) and are likewise registered in `AnalyzerReleases.Unshipped.md`. At
least:

- **`SY1201` — anchor not allowed.** `Block.Start()`/`Block.End()` used in a `None` pattern
  (already fully bounded). Located on the marker call.
- **`SY1202` — pattern can never match (unsatisfiable).** Emitted *only* when the generator
  can **prove** no syntax tree satisfies the pattern's combined constraints. The
  conservatism rule is load-bearing: a false positive would reject a valid matcher, so we
  stay silent on anything merely suspicious and flag only the provably-dead. Cheap, provable
  triggers detected during lowering:
  - **anchor contradiction** — content positioned before `Block.Start()` or after
    `Block.End()` (e.g. `Statement.Some(); Block.Start();` — statements required before the
    block begins);
  - **narrowing incompatible with the slot** — `[Capture<TNode>]` where `TNode` can never
    appear at that position (a statement-kind narrow in an expression slot; a kind the parent
    never permits);
  - **conflicting cardinality** — the same hole placed twice with incompatible quantifiers,
    or `Exactly(0)` on an otherwise-required hole.

  Full unsatisfiability is not decidable in general, so this is deliberately a *partial,
  provable-only* check — it never claims a pattern is dead unless it is. (Analogous to the C#
  compiler's "this pattern can never match" / unreachable-case diagnostics.)

The two **`SY120x` "not yet supported in v1"** arms cover the deferred constructs a consumer
can still *reach* with always-available C#, so each degrades to a clean diagnostic rather than
a literal mis-match or an unimplemented-arm throw. (The deferred *markers* — `Deep`/`Either`/
`Many<T>` — need no diagnostic: they aren't injected in v1, so a consumer can't name them.)

- **`SY1203` — `foreach`-repetition not yet supported in v1.** A phantom `foreach` iterating a
  `[Capture]` param (the §3.7 repetition notation, whose backtracking lowering is deferred to
  v2 — §8/§9). Located on the `foreach`.
- **`SY1204` — quantifier placement needs backtracking (not yet supported in v1).** A matched
  statement run with **more than one** variable-length quantifier (`Some` = 1+, `All` = 0+,
  `Opt` = 0–1), or a variable-length quantifier abutting content it could also consume,
  requires the deferred backtracking lowering (§2, §3.4, §8). v1 supports **at most one**
  variable-length quantifier per run (the single greedy split is straight-line). The pattern
  is *satisfiable* — this is a "can't lower it yet" signal, distinct from `SY1202`'s "provably
  no tree matches." Located on the offending second quantifier (or the ambiguous run).
- Further arms as the implementation surfaces them (e.g. capture referenced but never placed;
  `Bare` body empty).

All generation failures are converted to a `DiagnosticInfo` (`SY0000` internal-error
catch-all), never thrown.

## 12. Testing strategy

- **Golden snapshots** of generated matchers (`*.verified.cs`) per pattern shape, the
  executable spec of output — same harness as Templating.
- **Round-trip / behavioral**: build a known tree, assert the generated matcher matches it
  and returns the expected captures; assert near-miss trees *don't* match.
- **Cacheability**: a driver re-run on an unrelated edit yields cached steps
  (equatability of the pipeline model).
- **Diagnostics**: a driver test per `SY12xx` arm — esp. `SY1201` (anchor misuse), `SY1203`
  (deferred-`foreach`), and `SY1204` (over-the-line quantifier placement, incl. an
  `Opt`+`All` run).

## 13. Risks & consequences

- **Emitter complexity migrates the hard logic into codegen.** Mitigated by shipping the
  straight-line v1 first and by the established snapshot/round-trip test model; Shinn proves
  bespoke ellipsis-from-a-macro is tractable.
- **Surface is a one-way door once consumers compile against it.** The marker shape,
  generated record shape, `Synto.Matching` namespace, and the **per-option matcher input
  contract** (what node you hand each matcher — the declaration for `None`, the block for
  `Bare`/statement-`Single`, the expression node itself for expression-`Single`; §4) are
  compatibility boundaries — pinned by snapshots, changed freely only pre-consumer. This spec
  fixes them deliberately. (The generic-vs-`typeof` attribute form, §3.2/§10, and the
  captured-run collection type, §10, are part of that surface and must be settled before they
  freeze.)
- **`Bare` = contains** is a deliberate, regex-like default; anchors are the pins, and the
  **leftmost-single-result** position rule (§4) — kept unambiguous by the
  single-variable-length-quantifier line (§3.4) — keeps it deterministic. Chosen so "a block
  containing this" needs no boilerplate.

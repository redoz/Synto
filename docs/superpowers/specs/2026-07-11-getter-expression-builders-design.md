# Getter expression builders — `Coalesce` + ordinal ergonomics (retire the raw-syntax towers)

**Status:** Design (brainstormed 2026-07-11, accepted shape). Path 2 of a two-part plan; the imperative
counterpart is `2026-07-11-staged-if-prune-design.md` (Path 1, deferred). This spec ships now.

**Origin:** the ObjectReader dog-food's typed getters (`ReaderTemplate.cs`, `TypedGetter<TRet>`). The
ADO.NET-correct null-guarded arm for a nullable-reference column currently drops out of the builder
surface into hand-assembled Roslyn factory calls (`SwitchExpressionArm(ConstantPattern(LiteralExpression(
NumericLiteralExpression, Literal(...))), BinaryExpression(CoalesceExpression, MemberAccessExpression(...),
Throw<ExpressionSyntax>(...)))`). That tower is the least readable code in the example, and it exists only
because two small builders are missing — not because the shape needs raw syntax.

## 1. Problem

A nullable-reference typed getter arm must emit `N => _e.Current.<Name> ?? throw new InvalidCastException(...)`.
Today the author cannot express the `??` through the builder surface, so the whole arm collapses to raw
`SyntaxFactory` calls:

- There is **no `??` builder**, so the author hand-writes `BinaryExpression(CoalesceExpression, left, right)`.
- Because `BinaryExpression`'s operands are raw `ExpressionSyntax`, the member access must also be hand-built
  (`MemberAccessExpression(SimpleMemberAccessExpression, MemberAccessExpression(...,"_e","Current"),
  IdentifierName(c.Name))`) — the existing `Member` builder can't feed a raw `BinaryExpression` cleanly.
- The arm's constant pattern is spelled `ConstantPattern(LiteralExpression(NumericLiteralExpression,
  Literal(c.Ordinal)))` because `Arm`'s pattern parameter is a staged `ExpressionSyntax`, not a factory-time
  ordinal.

The result: the guarded branch shares no code path with the plain branch, and the reader sees a wall of
`SyntaxFactory` nesting for what is conceptually `member ?? throw`.

**A note on why this is not solved imperatively.** The obvious "just write control flow" rewrite — a `foreach`
over the columns with a nested `if (c.IsNullableReference)` picking the guarded vs. plain return — does **not**
work in the current staging model (verified by spike, 2026-07-11): a factory-time `if` nested inside an
unrolled `foreach` region does not prune; its purely-staged condition folds to a `bool` literal and **both**
branches are emitted as `if (true/false) …; return …;` — dead code that trips CS0162 under global TWAE. Making
that imperative shape prune is real generator work, carved out as Path 1. This spec stays in the **functional**
arm-projection shape (the shape that works today) and removes the raw towers within it.

## 2. Design — two builders, no generator surgery

The nullable choice stays a **factory-time `?:` inside the `.Select` projection** (value position, choosing
which arm builder to call — this already works; it is the current mechanism). Two additions let both branches
ride the builder surface:

### 2.1 `Coalesce` — a `??` expression builder

A new `[SyntaxBuilder]` that builds a null-coalescing `BinaryExpression` from two quoted operand islands:

```csharp
[SyntaxBuilder]
public static ExpressionSyntax Coalesce([Quoted] ExpressionSyntax left, [Quoted] ExpressionSyntax right) =>
    BinaryExpression(CoalesceExpression, left, right);
```

Both operands are `[Quoted]` islands, so nested builder calls — `Member(_e.Current, c.Name)` and
`Throw(new InvalidCastException(...))` — **expand via re-entry**, exactly as the doc-comment on
`Arm`/`Return` already describes for their quoted value islands. The author never touches raw
`BinaryExpression` or raw `MemberAccessExpression` again.

**The one constraint — no type argument at the call site.** Any `ExpressionSyntax`-returning builder earns a
fresh-`TResult` facade today (`FacadeShape.Derive`: `returnsExpression → freshReturnTypeParam`), so a bare
`Coalesce(...)` would be `Coalesce<TResult>(...)` and fail to infer `TResult` (C# has no return-type
inference), forcing an ugly explicit `Coalesce<ExpressionSyntax>(...)` at every site. `Coalesce` must instead
get a **fixed `ExpressionSyntax` facade** so a bare, type-argument-free call composes inside an island. This is
the existing operator-facade mechanism (`SyntaxBuilder(Returns=, Facade=)`, `2026-07-06-operator-facades-design.md`)
— `Coalesce` is, in effect, the `??` operator joining that family. The exact host/return wiring is left to the
implementer; the **contract** is: `Coalesce(a, b)` is callable with no type argument in any `[Quoted]` island
position.

### 2.2 Ordinal ergonomics — `Arm` takes a factory-time ordinal

The author should write `Arm(c.Ordinal, value)` — passing the factory-time `int` ordinal directly — rather
than `Arm(LiteralExpression(NumericLiteralExpression, Literal(c.Ordinal)), value)` +
an implicit `ConstantPattern`. `Case`'s label already does this via `[Constant]` (a factory-time value lifted
to a constant `ExpressionSyntax` at the binding seam). `Arm`'s pattern is currently an **unmarked staged**
`ExpressionSyntax`, so it does not accept a bare `int`.

The **contract**: an arm's constant pattern is authored from a factory-time ordinal value, not a hand-built
`ConstantPattern(LiteralExpression(...))`. The implementer resolves the shape — the leading candidates:

- A `[Constant]`-marked pattern **overload** of `Arm` (`Arm([Constant] object ordinal, [Quoted] ExpressionSyntax value)`)
  alongside the existing staged-pattern `Arm`, so hand-built patterns still bind the old overload. Preferred.
- A standalone `Constant(value)` / literal facade producing the pattern expression, if one already exists on
  the constant-literal surface (`2026-07-05-constant-literal-facade-design.md`) — then `Arm(Constant(c.Ordinal), value)`.

Changing the **existing** `Arm` pattern parameter in place to `[Constant]` is rejected: it would break every
current `Arm` caller that passes a pre-built `ExpressionSyntax` pattern (the `[Constant]` lift would run that
node through `ToSyntax` and throw). An overload or a separate literal builder preserves both spellings.

### 2.3 Optional polish — shed the `Throw`/`Member` type arguments

`Throw` and `Member` also earn fresh-`TResult` facades, so their call sites carry an explicit
`Throw<TRet>(...)` / `Member<TRet>(...)`. Inside the guarded arm these type arguments are pure ceremony (the
value flows into a `[Quoted]` `object` parameter). Giving `Throw` (and, if cheap, `Member`) the same **fixed
`ExpressionSyntax` facade** as `Coalesce` lets them compose bare: `Throw(new …)`, `Member(_e.Current, c.Name)`.

This is **secondary**: the raw-tower removal (2.1) and ordinal ergonomics (2.2) stand on their own; the
type-argument shedding is a readability polish that rides the same fixed-facade mechanism. If the mechanism
proves heavier than expected, the getter still reads cleanly with `<TRet>` retained on the operands.

## 3. Target authoring surface

With 2.1 + 2.2 (+ 2.3 for the bare operands), `TypedGetter` collapses to:

```csharp
return Switch<TRet>(i, columns.Where(c => c.ColumnTypeName == clrTypeName).Select(c =>
        c.IsNullableReference
            ? Arm(c.Ordinal, Coalesce(Member(_e.Current, c.Name),
                  Throw(new InvalidCastException($"Field {i} is null; call IsDBNull first."))))
            : Arm(c.Ordinal, Member(_e.Current, c.Name)))
    .Append(DefaultArm(Throw(new InvalidCastException($"Field {i} is not {typeLabel} column.")))));
```

No raw `SwitchExpressionArm`, `BinaryExpression`, `MemberAccessExpression`, `ConstantPattern`, or
`LiteralExpression`. Both branches ride `Arm`; the guarded branch differs only by wrapping its member in
`Coalesce(…, Throw(…))`. The exception message keeps `{i}` as an **output-world** interpolation (the message
island is quoted; `i` is the getter's runtime parameter) — the same behavior as today.

The `TypedGetters()` `[Splice]` list is unchanged.

## 4. Contracts & invariants (binding on the plan)

- **`Coalesce` correctness:** `Coalesce(a, b)` emits `a ?? b`; nested `Member`/`Throw` builders inside its
  operands expand via re-entry. A test pins `member ?? throw` output.
- **No call-site type argument:** `Coalesce(...)` (and, under 2.3, `Throw(...)`/`Member(...)`) compile and
  compose in `[Quoted]` island positions with **no** explicit type argument. A template-compilation test pins
  this (the failure mode is a `TResult`-inference error).
- **Ordinal ergonomics without regressions:** `Arm(c.Ordinal, value)` builds `N => value`; existing
  `Arm(<built pattern>, value)` callers/tests still bind and stay green (overload, not in-place change).
- **Output stays semantically correct, not byte-identical:** the ObjectReader getters produce ADO.NET-correct
  arms (nullable-reference → `?? throw InvalidCastException`; others → plain access). Snapshot re-accepts are
  expected and benign (the arm is assembled by builders now, not raw factory calls); behavior tests
  (`ObjectReaderBehaviorTests`) stay green.
- **Cacheability preserved:** no `ISymbol`/`SemanticModel`/`SyntaxNode` enters cached pipeline state; the new
  builder rides the existing structural `[SyntaxBuilder]` recognition. Existing cacheability asserts stay green.
- **Injected-surface snapshots change:** `SyntoBuilders.g` / the operator-facade hosts gain `Coalesce` (+ any
  `Arm` overload); re-accept.
- **Dog-food validates by building the example:** `Synto.Example.ObjectReader.Generator` builds green with the
  rewritten `TypedGetter`; the example project (not just `Synto.Test`) compiles.

## 5. Alternatives considered

- **Fresh-`TResult` `Coalesce` with explicit `Coalesce<ExpressionSyntax>(...)`.** Works with no facade-mechanism
  change, but reintroduces call-site type-argument ceremony — the exact wart being removed. Rejected.
- **Operands typed `TResult` (`Coalesce<T>(T left, T right)`) to infer from `Member<TRet>`/`Throw<TRet>`.**
  Makes `Coalesce`'s type arg inferrable but forces type arguments back onto both operands. Strictly uglier.
  Rejected.
- **Imperative control-flow rewrite (Path 1).** The readability ideal, but requires a generator feature
  (factory-time `if` prune nested in a `foreach` region). Deferred to `2026-07-11-staged-if-prune-design.md`.
- **A general `Binary(kind, left, right)` builder instead of a named `Coalesce`.** More general, but the only
  demand here is `??`; a named `Coalesce` reads better and matches the operator-facade family. A general binary
  builder can be added later if demand appears. Out of scope.

## 6. Scope

Ship `Coalesce` + the ordinal ergonomics (2.1 + 2.2) and rewrite `TypedGetter` onto them in one plan. The
`Throw`/`Member` type-argument shedding (2.3) is in-scope-if-cheap on the same fixed-facade mechanism; drop it
to a follow-up if it balloons the plan. Path 1 (imperative prune) is explicitly **not** in this plan.

## 7. Open questions (carried to the plan)

- **Fixed-facade host for `Coalesce`:** does it join the existing `Operators`/`OperatorBuilders` family, or get
  its own host? `??` is an operator, so `Operators` is the natural home; confirm against the reorg's role-folder
  conventions.
- **`Arm` ordinal shape:** `[Constant]` overload vs. a `Constant(...)`/literal facade — pick during planning by
  checking what the constant-literal surface already exposes.
- **`Returns = typeof(ExpressionSyntax)` legitimacy:** the operator-facade `Returns` has so far carried
  non-syntax value types (`System.Type`/`string`/`int`); confirm a fixed `ExpressionSyntax` facade return is
  accepted by `FacadeShape`/`SyntaxBuilderFacadeGenerator` (or add that mode) before relying on 2.1/2.3.

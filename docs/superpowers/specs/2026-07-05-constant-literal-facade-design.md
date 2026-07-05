# `Constant` facade — value → `ExpressionSyntax` literal in builder-argument position

**Date:** 2026-07-05
**Status:** ⛔ Superseded by `2026-07-05-authoring-surface-taxonomy.md` §3.2 — the standalone
`Constant<T>() → ExpressionSyntax` facade is **dropped** in favor of a `[Constant]`
builder-argument binding-time kind (no `Quote` twin). The value-lift semantics, supported types,
and `SY1011`/`SY1012` diagnostics below still hold; they now attach to the `[Constant]` builder
parameter rather than a facade. A free-standing `Constant` facade is deferred until a consumer
needs a lifted node outside a `[SyntaxBuilder]` argument. Retained for that rationale.
**First consumer:** ObjectReader `TypedGetter` switch labels

## Problem

Inside a `[Template]` body, building a constant `ExpressionSyntax` from a factory-time
scalar value is verbose. In the ObjectReader dog-food's `TypedGetter` sub-template, each
switch-case label is written by hand:

```csharp
SyntoBuilders.Case(
    LiteralExpression(NumericLiteralExpression, Literal(c.Ordinal)),
    SyntoBuilders.Return(Member<TRet>(_e.Current, c.Name)))
```

`SyntoBuilders.Case(ExpressionSyntax label, …)` (`SyntoBuilders.cs:46`) deliberately takes an
**un-`[Quoted]`** label — a factory-time-built node — because marking it `[Quoted]` would quote
the live projection variable `c` out of output scope, emitting the member-access `c.Ordinal`
instead of the literal `5`. So the author must construct the literal node explicitly, and the
terse `SyntaxFactory.Literal(c.Ordinal)` is only a `SyntaxToken`, forcing the
`LiteralExpression(NumericLiteralExpression, …)` wrapper.

We want a one-call facade that turns a factory-time value into its literal `ExpressionSyntax`.

## Why not existing surface

- **`Quote<T>(T value)`** (`Template.cs:48`) performs exactly the right value-lift (int → the
  numeric literal), but returns `T`. In `Case(ExpressionSyntax label, …)` a `Quote<int>(…)` is
  carrier-typed `int` and will not type-check. `Quote` is a *value-position* lift, not a way to
  produce a standalone `ExpressionSyntax` node for a builder argument.
- **Naming.** `Quote(c.Ordinal)` also reads as "quote the expression `c.Ordinal`" (→ member
  access), the opposite of Synto's value-lift meaning. A distinct name avoids the ambiguity.
- **`Literal` (rejected name).** Collides with `SyntaxFactory.Literal`, which these templates
  import via `using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory`. Written bare as
  `Literal(c.Ordinal)`, C# overload resolution prefers the non-generic `SyntaxFactory.Literal(int)`
  (returns `SyntaxToken`) over a generic `Template.Literal<T>`, silently shadowing the facade.

## Design

### Surface contract

Add one inert facade to the injected `Template` surface
(`src/Synto.Core/Templating/Staging/Surface/Template.cs`):

```csharp
/// <summary>
/// Builds the constant <c>ExpressionSyntax</c> literal for a factory-time <paramref name="value"/>:
/// the node-typed sibling of <c>Quote(value)</c>. Where <c>Quote&lt;T&gt;</c> returns <c>T</c> (value
/// position), <c>Constant&lt;T&gt;</c> returns <c>ExpressionSyntax</c>, so it slots into positions that
/// expect a built node — e.g. a <c>[SyntaxBuilder]</c> argument such as a switch <c>Case</c> label or
/// <c>Arm</c> pattern. Inert (<c>=&gt; default!</c>) so the template body type-checks; the generator
/// recognizes the call by binding and lifts the value at factory-build time.
/// </summary>
public static Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax Constant<T>(T value) => default!;
```

The facade is `Constant`, chosen to avoid the `SyntaxFactory.Literal` collision and to read as
"the constant expression for this value." It is recognized by binding (like `Member` / `TypeOf` /
`Quote`), never called at runtime.

### Semantics

At factory-build time the generator replaces a recognized `Template.Constant<T>(value)` call with
the **same value-lift `Quote` and `[Unquote]`/`[Quote]` value parameters use** — the output of
`ValueLift.TryEmitValueLift` (`ValueLift.cs:70`): `value.ToSyntax()` for a built-in literal type or
an inlined generic type parameter, or a fully-qualified `[Runtime]` converter call for a concrete
non-built-in type. `ToSyntax` binds to the file-local `LiteralSyntaxExtensions`
(`src/Synto.Core/Syntax/EmitHelpers/LiteralSyntaxExtensions.cs`), which already returns
`ExpressionSyntax` and handles every numeric width, `null`, and `NaN`/`Infinity`. That helper is
injected into the generated document by the existing referenced-helper scan — no new injection
plumbing.

`Constant` is therefore **`Quote` with an `ExpressionSyntax` return type** — the carrier return
type is the *only* difference. Both:

- reuse the identical literal-building single source (`LiteralSyntaxExtensions`);
- are a **liveness boundary** — the argument is evaluated at factory time and lifted; a live
  projection variable referenced inside it (`c.Ordinal`) does not propagate liveness to the
  enclosing construct;
- share the value-lift's supported-type policy and diagnostics.

### Supported types and diagnostics

Supported value types are exactly those the shared value-lift supports (no new special-casing):

- built-in literal types — `ValueLift.IsBuiltInLiteralType` (`ValueLift.cs:40`): `string`, `bool`,
  `char`, all integer widths, `decimal`, `float`, `double`;
- an inlined generic type parameter (the `value.ToSyntax()` generic fallback);
- a concrete non-built-in type carrying exactly one `[Runtime]` `ToSyntax(this T)` converter.

A concrete non-built-in type with **no** converter reuses **SY1011**; with **more than one**,
**SY1012** — identical locations and messages to `Quote`/`[Quote]`. No `Constant`-specific
diagnostic is introduced.

### First consumer

`examples/Synto.Example.ObjectReader/.../ReaderTemplate.cs`, `TypedGetter`:

```csharp
// before
SyntoBuilders.Case(
    LiteralExpression(NumericLiteralExpression, Literal(c.Ordinal)),
    SyntoBuilders.Return(Member<TRet>(_e.Current, c.Name)))

// after
SyntoBuilders.Case(
    Constant(c.Ordinal),
    SyntoBuilders.Return(Member<TRet>(_e.Current, c.Name)))
```

Generated output is byte-identical for the case labels (both produce
`LiteralExpression(NumericLiteralExpression, Literal(<ordinal>))` at factory runtime), so the
ObjectReader snapshot is unchanged by this substitution.

## Implementation notes and the one open seam (for planning)

The rewrite must fire **inside `[SyntaxBuilder]` staged-argument positions**, because the first
consumer uses `Constant` as the `label` argument of the `Case` builder call nested in `Switch`.
Two existing recognition paths are candidates; the plan must choose and justify one:

1. **Built-in facade in `FacadeCallFinder`** (like `Member` / `TypeOf`, `FacadeCallFinder.cs:133`).
   Proven to recognize facades in builder-argument positions, but its `BuilderCall` rewrite emits a
   `SyntoBuilders.X(args)` static call — the wrong shape for `Constant`, whose target is the
   extension-call `value.ToSyntax()`. Would need either a new rewrite shape or a
   `SyntoBuilders.Constant` that duplicates the literal switch (rejected: violates single-source).
2. **Value-lift / `QuoteCall` path** (like `Quote`, `QuoteCallFinder.cs`). Emits `value.ToSyntax()`
   directly, reusing `LiteralSyntaxExtensions` (single source). The plan must confirm this path's
   recognition + replacement reaches into builder-staged argument sub-expressions (the walker
   visits them; the open question is whether the replacement is *applied* there, given `Member`
   inside a `[Quoted]` `Return` island already works).

**Recommended direction:** path 2 (reuse the value-lift, single-source literal building), pending
the plan verifying replacement application inside builder-staged arguments. Path 1 is the fallback
only if replacement in that position proves infeasible without new plumbing.

Surface injection: `Constant` is a new method on the already-injected `Template` class, so
`SurfaceInjectionGenerator` picks it up with no registry change. Confirm the injected-surface
completeness test (`test/Synto.Test/.../InjectedSurfaceCompletenessTest.cs`) and the
`SurfaceInjectionTest` snapshot are updated.

## Testing

1. **Unit / snapshot (generator):** a `[Template]` using `Constant(<int>)` (and at least one
   `string` and one `bool`) as a `Case` label; assert the generated factory emits
   `<value>.ToSyntax()` and the `LiteralSyntaxExtensions` helper is injected. Cover a `Constant`
   over an inlined generic type parameter.
2. **Diagnostics:** a `Constant` over a concrete non-built-in type with no `[Runtime]` converter →
   SY1011; with two → SY1012 (mirror the existing `Quote` diagnostic tests).
3. **Injected-surface completeness:** `Template.Constant` present in the injected surface snapshot.
4. **ObjectReader dog-food:** switch `TypedGetter`'s case labels to `Constant(c.Ordinal)`; the
   ObjectReader snapshot stays byte-identical; all ObjectReader tests green.

## Out of scope

- The `[Unquote]` value-parameter region-consumed lift-skip gap (the separate friction that blocked
  moving `columns` to an `[Unquote]` parameter). Captured elsewhere; not part of this feature.
- Generalizing `Constant` beyond the value-lift's current type coverage.
- Any change to `Quote` / `Member` / `TypeOf` semantics.

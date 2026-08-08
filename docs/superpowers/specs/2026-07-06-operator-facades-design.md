# Operator facades — retire the hardwired `TypeOf`, generalize to `typeof`/`nameof`/`sizeof`

**Status:** Design (brainstormed 2026-07-06, accepted shape). Supersedes the "keep `TypeOf` hardwired
as deliberate debt" ruling recorded in `FacadeCallFinder`/`SyntoBuilders`/`Template` doc comments and in
authoring-surface-taxonomy §3.3.

**Origin:** carved out of the `member-builtin-to-syntaxbuilder` plan (`Member` already migrated; `TypeOf`
was the sole remaining hardwired built-in). Rather than bless `TypeOf` as permanent debt, this fixes the
design choice that forced the hardwiring.

## 1. Problem

`Template.TypeOf(string) : System.Type` is the one facade still recognized **by name** in
`FacadeCallFinder` (`TryBuiltIn` / `IsBuiltInFacadeName`), rather than riding the structural
`[SyntaxBuilder]` path every other builder uses. The recorded rationale framed this as irreducible, but
it rests on a single root cause with a fixable shape.

### Root cause

There are two `TypeOf(string)` methods that differ **only by return type**:

- `Template.TypeOf(string) : System.Type` — the author-facing value view. Must be a *bound, value-typed*
  method so a template body like `System.Type GetFieldType(int i) => TypeOf(c.ColumnTypeName);`
  type-checks as ordinary C#.
- `SyntoBuilders.TypeOf(string) : ExpressionSyntax` — the real emit builder,
  `=> TypeOfExpression(ParseTypeName(name))`, called by the generated factory at the consumer's
  generation time (the type name is a *staged* value, unknown in Synto's own process).

That single fact throws two different compiler errors, which is why it looked irreducible:

- **CS0111** if both live on the same type (C# forbids return-type-only overloads) → why the value view
  is exiled onto `Template` and the builder onto `SyntoBuilders`.
- **CS0121 (ambiguous call)** because both are `using static`-imported into the template file
  (`ReaderTemplate.cs:9` imports `using static Synto.Templating.SyntoBuilders;`) → why authors must
  write the qualified `Template.TypeOf(...)` instead of bare `TypeOf(...)`.

`Member` avoids all of this because its value view is *generic* — the synthesized facade is
`Member<TRet>(instance, name) : TRet`, distinguished from the non-generic `SyntoBuilders.Member` by
generic arity and shielded by its `[Quoted] ExpressionSyntax` first parameter, so a real author argument
never binds to the builder. `TypeOf`'s value type is **fixed** (`System.Type`) and callers write **no**
type argument, so the generic-disambiguation trick cannot apply.

## 2. The category

This is not a `TypeOf` singleton. It is a **closed family**: fixed-return facades that mirror C#'s
value-producing keyword operators — take a factory-time name, are consumed as a fixed CLR value in the
template body, and lower to the operator expression in output.

| Operator | Emits | Fixed facade return | Demand |
| --- | --- | --- | --- |
| `TypeOf` | `typeof(Foo)` | `System.Type` | existing |
| `NameOf` | `nameof(Foo.Bar)` | `string` | high — generated code uses `nameof` constantly |
| `SizeOf` | `sizeof(Foo)` | `int` | real (interop / P/Invoke / marshalling) |

The set is closed to C#'s value operators; `default(T)` is **not** a member (generic return — already
rides the `Member<TRet>` path with zero special-casing). Delivering all three proves the mechanism is
genuinely general rather than accidentally `typeof`-shaped, and `nameof`/`sizeof` are one builder method
each once the mechanism exists.

## 3. Design — operators are ordinary `[SyntaxBuilder]`s

The generalization reuses the existing `[SyntaxBuilder]` facade-synthesis path rather than adding a
parallel framework. The emit logic lives in the builder method body (no generator switch); recognition
and rewrite are the existing structural SyntaxBuilder path (the by-name arm is deleted).

### 3.1 Facade-synthesis already produces value-position facades

`FacadeShape.Derive` already computes a facade's value-position return two ways:

- **Fresh generic `TResult`** — an `ExpressionSyntax` builder gets a `<TResult>` return (this is
  `Member<TRet>`).
- **`[ReturnType]` parameter** — a `[TypeArg]` parameter supplies the facade's return type.

Emit for any SyntaxBuilder is **uniform**: the recognized facade call is rewritten to a fully-qualified
static call of the real builder. The generator never encodes what a builder *does* — the builder body
holds the logic. Operators need one more facade-return mode: a **fixed** value type.

### 3.2 The one placement constraint (CS0111)

`SyntaxBuilderFacadeGenerator` emits each synthesized facade into a **`partial` reopening of the
builder's own containing type** (`SyntaxBuilderFacadeGenerator.cs` — the containing-type chain, each
reopened `partial`). `Member`'s facade coexists with its builder there because it differs by generic
arity. A **fixed-return** operator facade shares the builder's exact signature except return type, so
placing it in the builder's type would be **CS0111**. Therefore the operator facade must be synthesized
into a **different** type than its builder.

This is what produces the clean split, and it is the second (and final) piece the design needs.

### 3.3 Two optional `[SyntaxBuilder]` properties

`SyntaxBuilderAttribute` (today empty) gains two optional properties:

- `Returns` — a `System.Type`; the fixed value-position return type of the synthesized facade. Unset →
  today's behavior (fresh `TResult` / concrete node).
- `Facade` — a `System.Type`; the author-facing type the synthesized facade is emitted into. Unset →
  today's behavior (the builder's own containing type).

`Returns` set to a non-syntax type without `Facade` is a declaration-site error (the facade would CS0111
against its builder); the generator diagnoses it rather than emitting broken source.

### 3.4 Shape

Emit builders — real code, internal, factory-called, **not** `using static`-imported by authors:

```csharp
// injected surface; internal host type authors never import
internal static partial class OperatorBuilders
{
    [SyntaxBuilder(Returns = typeof(System.Type), Facade = typeof(Operators))]
    internal static ExpressionSyntax TypeOf(string name) => TypeOfExpression(ParseTypeName(name));

    [SyntaxBuilder(Returns = typeof(string), Facade = typeof(Operators))]
    internal static ExpressionSyntax NameOf(string name) =>
        InvocationExpression(IdentifierName("nameof"), ArgumentList(SingletonSeparatedList(Argument(ParseExpression(name)))));

    [SyntaxBuilder(Returns = typeof(int), Facade = typeof(Operators))]
    internal static ExpressionSyntax SizeOf(string name) => SizeOfExpression(ParseTypeName(name));
}
```

Author-facing value view — **synthesized** into `Operators`, inert:

```csharp
public static class Operators
{
    public static System.Type TypeOf(string name) => default!;   // synthesized, inert
    public static string      NameOf(string name) => default!;
    public static int         SizeOf(string name) => default!;
}
```

Author writes `using static …Operators;` and bare `TypeOf(c.ColumnTypeName)`; the generated factory calls
`global::….OperatorBuilders.TypeOf(c.ColumnTypeName)` at factory-build time, emitting `typeof(<that type>)`.

### 3.5 Recognition and emit

- **Recognition** is the existing structural SyntaxBuilder path: the operator builders register their
  names via the SyntaxBuilder registry; an author's bare `TypeOf(name)` call is recognized the same way
  `Member` is. The hardwired `TryBuiltIn` / `IsBuiltInFacadeName` arm in `FacadeCallFinder` is **deleted**.
- **Emit** is the uniform facade→builder rewrite; the per-operator logic is the builder body. There is
  **no** `OperatorKind` enum and **no** `switch (kind)` in the generator.

## 4. Surface change

- **Deleted:** `Template.TypeOf`; `SyntoBuilders.TypeOf`; the by-name arm in `FacadeCallFinder`.
- **Added:** `Operators` (author-facing, synthesized facades); internal `OperatorBuilders` (emit
  builders); `SyntaxBuilder(Returns=, Facade=)` properties; `NameOf`, `SizeOf`.
- **Net author experience:** bare, unqualified `TypeOf(...)` / `NameOf(...)` / `SizeOf(...)` via
  `using static …Operators`.

## 5. Contracts & invariants (binding on the plan)

- **Output stays semantically identical** for `typeof`: `GetFieldType` still emits `typeof(<col type>)`.
  Factory-body snapshots re-accept (the emit-builder call moves from `SyntoBuilders.TypeOf` to
  `OperatorBuilders.TypeOf`); generated *output* is unchanged. Benign snapshot re-accepts are expected.
- **Injected-surface snapshots change:** `Template.g` loses `TypeOf`; `SyntoBuilders.g` loses `TypeOf`;
  new `Operators.g` + `OperatorBuilders.g` + `SyntaxBuilderAttribute.g` (two new properties).
- **Cacheability preserved:** no `ISymbol` / `SemanticModel` / `SyntaxNode` enters cached pipeline state;
  recognition stays inside the `ForAttributeWithMetadataName` transform. Existing cacheability asserts
  stay green.
- **`nameof` / `sizeof` correctness:** new tests prove `NameOf("Foo.Bar")` → `nameof(Foo.Bar)` and
  `SizeOf("int")` → `sizeof(int)` in output.
- **Recognition-by-attribute, not by name:** a test pins that no facade is recognized by a hardcoded name
  (the `TryBuiltIn` arm is gone).
- **ObjectReader dog-food:** `ReaderTemplate.cs` switches to `using static …Operators` and bare
  `TypeOf(...)`; the example stays green with semantically-identical generated output.

## 6. Alternatives considered

- **Option A — bespoke `[OperatorFacade(OperatorKind)]` + enum + emit `switch (kind)`.** Rejected: it is
  the by-name special case relocated into the generator (a closed switch is still hardwiring), and it
  builds a parallel recognition framework for three built-ins. The chosen design reuses the SyntaxBuilder
  path and keeps emit as ordinary builder-body code.
- **Generic-arity trick (`SyntoBuilders.TypeOf<T>`).** Making the builder generic fixes only the CS0121
  call-site ambiguity (C# prefers the non-generic overload, so bare `TypeOf("x")` binds to the value
  view). It leaves the value view bound and hardwired, does not retire the special case, and must be
  repeated per operator. Useful only as a smaller stepping stone; not chosen.
- **Keep it hardwired (bless as debt).** The prior ruling. Rejected now that `NameOf`/`SizeOf` give the
  category real members.

## 7. Scope

Ship all three operators (`TypeOf` migrated onto the new mechanism + `NameOf` + `SizeOf`) in one plan.
`SizeOf` could be deferred if the plan needs trimming, but it is one builder method + one facade once the
mechanism exists.

## 8. Open questions (carried to the plan)

- **`Facade` defaulting:** whether `Returns`-set-to-a-non-syntax-type could *infer* a conventional facade
  type instead of requiring explicit `Facade =`. Default: explicit for now.
- **Host-type naming:** `Operators` (author-facing) and `OperatorBuilders` (internal) are proposed names;
  confirm against the reorg's role-folder conventions during planning.
- **Exact recognition wiring** (structural unbound-name match vs. bound-symbol match) is left to the TDD
  implementer to match however `Member` is recognized today; this spec pins the behavior, not the code.

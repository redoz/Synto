# Child templates — a template invoking a template (user-facing design)

**Date:** 2026-06-28
**Status:** Design (user-facing surface). Narrow member-level case scoped to ship first;
inline-fragment case designed but deferred.

## Context

The ObjectReader example hand-authors 13 typed getters (`GetBoolean`, `GetInt32`,
`GetString`, …). Every body is the same shape — loop the column list filtered by a CLR type,
read the matching member, throw otherwise — differing only by **return type**, **method name**,
and a **type-name filter**. This is the friction-log's finding #1 ("data-driven list expansion")
in its purest form, and it's the motivating case for letting one `[Template]` be composed from
another: a single *child* template, specialized per type, invoked once per getter.

The headline finding of this brainstorm: **most of "a template invoking a template" already
exists.** Child-templates is a small completion of the shipped surface, not a new subsystem.

## Mental model (what the pieces already mean)

- **`TemplateOption` is a reduction ladder.** It controls how much of the carrier survives
  quoting:
  - `None` (default) → the **whole declaration** as written. A *method* carrier →
    `MethodDeclarationSyntax`; a *class* carrier → `ClassDeclarationSyntax`; a *local function*
    → `LocalFunctionStatementSyntax`.
  - `Bare` → drop the declaration shell, keep the **body block** → `BlockSyntax`.
  - `Single` (`= 2 | Bare`) → also unwrap the block, keep the **single inner statement** →
    `StatementSyntax` (with `.Expression` for the inner `ExpressionSyntax`).
  - `PreserveTrivia` is an orthogonal flag.
- **Every `Factory.X(...)` invocation returns a Syntax node**, whose kind tracks the carrier
  shape + option above.
- **Composition already works.** `Synto.Examples/Program.cs` `Test2` is one template fed
  another's output: `Factory.Outer(Factory.Inner().Expression)`. Passing a child's node into a
  parent's `[Splice]` parameter is a shipped capability.
- **A `[Splice]` type parameter is a real type in the carrier but a `TypeSyntax` *value* on the
  generated factory.** ObjectReader authors `class ObjectReaderTemplate<[Splice] T>` with a real
  `T`, and the factory is `Factory.ObjectReaderTemplate(TypeSyntax T, …)`. Because the factory
  takes a `TypeSyntax` **value**, callers can vary it in a loop.

That last point dissolves an apparent tension: a target type can be authored as a *real* type
(so `Member<TRet>` and the return position compile) **and** be supplied in a loop (because the
factory receives it as a `TypeSyntax` value). There is no "generic = can't loop" trade-off.

## What a child template is

A child template is an ordinary `[Template]` whose distinguishing feature is an **explicit
target type** — a `[Splice]` type parameter naming the type it is specialized *for*. A standalone
template emits fixed syntax onto its Factory; a child template says "give me a target type, I'll
hand back syntax specialized to it," and a parent supplies that target per invocation.

```csharp
// child: explicit target type TRet; None option → returns the whole method
[Template]
static TRet TypedGetter<[Splice] TRet>(int i)
{
    var columns     = Parameter<EquatableArray<ColumnInfo>>();
    var clrTypeName = Parameter<string>();
    foreach (var c in columns.Where(c => c.ColumnTypeName == clrTypeName))
        if (i == c.Ordinal)
            return Member<TRet>(_e.Current, c.Name);   // real TRet, compiles
    throw new global::System.InvalidCastException($"Field {i} is not a {clrTypeName} column.");
}

// generated factory:
//   MethodDeclarationSyntax TypedGetter(TypeSyntax TRet, EquatableArray<ColumnInfo> columns, string clrTypeName)
```

## User-facing surface

### A. Member-level composition — the narrow case (ships first)

A `[Splice]` member-generator method calls the child's factory per item and yields the members.
Because the target type is a `TypeSyntax` value, the call site loops:

```csharp
[Splice]
static IEnumerable<MemberDeclarationSyntax> TypedGetters()
{
    foreach (var col in GetterColumns)              // data-driven set
        yield return Factory.TypedGetter(col.ClrTypeSyntax, columns, col.ClrTypeName)
                            .WithIdentifier(Identifier(col.GetterName));   // name variance
}
```

Net effect on ObjectReader: 13 hand-written getter bodies collapse to **one** child template +
the per-getter table.

### B. Inline-fragment composition — the general case (designed, deferred)

Inside a *quoted* body you graft a child's node with `Splice(...)`:

```csharp
public bool GetBoolean(int i)
{
    Splice(Factory.TypedGetterBody(/* … */));   // build-time evaluate child, graft its node here
}
```

`Splice` is the verb that bridges a build-time Syntax value into a quoted code position. Without
it, `Factory.TypedGetterBody(...)` would be quoted verbatim as a method-call expression in the
output. This is the reuse path for *statement/expression* fragments shared across bodies; it is
**not required for the narrow member case** (section A uses a `[Splice]` generator + `yield`).

## What is genuinely new vs. already shipped

Already shipped: the `TemplateOption` ladder; `Factory.X(...)` returning syntax; fragment
composition by passing one template's output into another's `[Splice]` parameter (`Test2`);
`[Splice]` type params surfacing as `TypeSyntax` factory values.

New for child-templates:

1. **Calling one template's factory from inside another's `[Splice]`/quoted body** as the
   blessed composition pattern (largely works today; this makes it intentional and documented).
2. **`Splice(Factory.X(...))`** as the inline graft verb in a quoted context (section B).
3. **Member-name variance** — the getter's *name* varies, which types do not cover. Resolved by
   either a post-quote `.WithIdentifier(...)` rename (works today, mirrors ObjectReader's
   class/ctor rename) or a future identifier hole.
4. **Position-aware `Splice` validation** — a `Splice`'d node must be legal where it is grafted
   (a `MemberDeclarationSyntax` only where members are legal, a `StatementSyntax` only in a
   statement position, etc.). Mismatches should surface as a diagnostic rather than emit
   malformed code.

## What it can NOT do (limits)

- **Names are not types.** A `[Splice]` target type covers the return type and `Member<…>`, but
  the member's *identifier* is not splice-able; name variance needs `.WithIdentifier(...)` or a
  dedicated identifier hole.
- **Syntax-only, cacheable inputs.** Child arguments must be equatable data (e.g. `TypeSyntax`,
  `EquatableArray<…>`, primitives) — no Roslyn `ISymbol`/`SemanticModel` captured — so child
  invocation stays inside the incremental-generator cacheability rules. Child bodies do no
  semantic queries, like every template.
- **The carrier must compile as real C#.** A child that references parent state (e.g. the
  reader's `_e` field, or `int i`) needs that state visible where the carrier is authored — the
  same field-visibility wrinkle as the ObjectReader partial split. Resolved by authoring the
  child within the parent carrier's scope or by lifting the state as a `Parameter<>()`/hole.
- **No looping a C# generic argument.** The loopability comes from the *factory* taking a
  `TypeSyntax` value; it is not from instantiating a generic at the call site. (Stated to avoid
  the misconception — there is no enumerated-only mode.)

## Scope & phasing

- **Ship first (narrow):** member-level composition (section A) — a `[Splice]` generator
  invoking a child `[Template]` with a `[Splice]` target type, names supplied via
  `.WithIdentifier(...)`. Demonstrated by migrating ObjectReader's 13 typed getters to one child
  template. This needs items #1 and #3 above; #3 uses the already-working post-quote rename.
- **Deferred (general):** the inline `Splice(Factory.X(...))` graft (section B, item #2) and the
  position-aware validation (item #4). Designed here so the narrow case does not paint us into a
  corner, but not built until a fragment-reuse case earns it.

## Open decisions (for the implementation plan to resolve)

- Member-name variance: ship with post-quote `.WithIdentifier(...)` now, or pull the identifier
  hole forward? (Recommendation: `.WithIdentifier(...)` now; identifier hole later.)
- `Splice` position-awareness: diagnostic-only, or also infer the graft position? (Recommendation:
  diagnostic-only for the first cut.)
- Child-carrier field visibility: author the child inside the parent carrier vs. lift parent
  state as parameters. (Recommendation: decide per the ObjectReader getter migration, which is
  the first real consumer.)

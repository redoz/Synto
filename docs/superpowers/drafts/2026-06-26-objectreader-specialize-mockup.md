# ObjectReader, mocked up under the proposed feature spine

**Date:** 2026-06-26
**Status:** brainstorming mockup — PAUSED pending a re-examination of the templating foundation (see *Discussion state* below)
**Driving question:** what is the *smallest cohesive* feature set that lets the entire `ReaderTemplate.cs` dog-food be authored as faithful, quoted C# — with **zero** raw `SyntaxFactory` fix-up?

---

## Discussion state (2026-06-27)

Where we landed before pausing:

- **"Both" binding styles reconciled.** The attribute form *and* a value-passed form survive as two front-ends to one `ISpecializer` contract — **attribute** (Synto news it) and **parameter** (you pass a configured instance into the `Factory` call, parallel to how `[Repeat]` columns flow in). The literal `Template.For<>().Specialize(...)` fluent-registration spelling was **retired**: it implies a runtime registration step that has no home in a quote-time template. (Open decision #2.)
- **Specializer = router, not a syntax factory.** Blessed path is "filter columns by `ctx.Member`, then reuse a normal `[Template]` for the body" — `ISpecializer` should not push authors back into hand-built `SyntaxFactory`. (Open decision #1.)
- **Not yet convinced.** The cohesive *story* holds, but rungs 2–3 (`[Repeat]`, `[Specialize]`) are being layered on a templating foundation we have not re-examined. Before committing to this surface we are **going back to look at the foundation of templating** — the existing primitives (`[Template]`, `[Inline]`, `Syntax`/`Syntax<T>`, `[Runtime]`, the quote/factory model) — to check whether `[Specialize]`/`[Repeat]` *extend* the foundation cleanly or *fight* it, and whether a more fundamental primitive subsumes them. The foundation review feeds back into the open decisions below.

The bar you set: *flexible but simple and cohesive*, not a grab-bag. So everything below hangs off **one spine** and an **escalation ladder** — you only reach for the next rung when the previous one genuinely can't express the member.

---

## The spine: one model, three rungs

Everything fans out over a single quote-time **model** the generator already computes — the resolved columns:

```csharp
// Authored by the generator (not by Synto). Equatable, flows in once.
public readonly record struct ColumnInfo(int Ordinal, string Name, System.Type ClrType);
```

Three rungs, increasing power. Each rung is "use the previous one unless you can't":

| Rung | Primitive | Splices… | Reach for it when… |
|------|-----------|----------|--------------------|
| **1** | `[Inline]` *(exists today)* | one value or type | a single hole — e.g. the element type `T` |
| **2** | `[Repeat]` + phantom `foreach` | a *collection* into uniform arms | every arm has the **same shape**, one per item |
| **3** | `[Specialize<T>]` / `ISpecializer` | a member whose body depends on **the member itself** | the shape varies *per member* (return type, name, …) |

Two **holes** support rung 2 — the only places where "splice as *code*" is ambiguous with "splice the *value*":

| Hole | Emits | Without it you'd get |
|------|-------|----------------------|
| `Member(x, c.Name)` | `x.Name` (identifier as code) | `x."Name"` (a string — wrong) |
| `Type(c.ClrType)` | `typeof(global::System.Int32)` | a `System.Type` *value* — wrong in a `typeof` slot |

Plain `c.Ordinal` / `c.Name` in *value* position need no hole — they become literals automatically.

**Total new surface: 2 structural attributes (`[Repeat]`, `[Specialize]`), 1 interface (`ISpecializer`), 2 holes (`Member`, `Type`).** That's it.

---

## The mockup — `ReaderTemplate.cs`, rewritten

```csharp
using Synto.Templating;
using static Synto.Templating.Hole;          // Member(), Type()

namespace Synto.Example.ObjectReader.Generator;

[Template(typeof(Factory))]
internal sealed class ObjectReaderTemplate<[Inline(AsSyntax = true)] T>
{
    // RUNG 2 — the fan-out source. [Repeat] lifts it to a Factory parameter; never instantiated.
    [Repeat] private static readonly Repeat<ColumnInfo> Columns = default;

    private readonly System.Collections.Generic.IEnumerator<T> _e;
    private bool _closed, _onRow;
    public ObjectReaderTemplate(System.Collections.Generic.IEnumerable<T> source)
        => _e = source.GetEnumerator();

    // ---- list-driven members: now DECLARATIVE (was raw SyntaxFactory) ----

    public int FieldCount => Columns.Count;                      // .Count over the source → int literal

    public string GetName(int i)
    {
        foreach (var c in Columns)
            if (i == c.Ordinal) return c.Name;                  // c.Ordinal/c.Name → literals
        throw OutOfRange(i);
    }

    public int GetOrdinal(string name)
    {
        foreach (var c in Columns)
            if (name == c.Name) return c.Ordinal;
        throw NoColumn(name);
    }

    public System.Type GetFieldType(int i)
    {
        foreach (var c in Columns)
            if (i == c.Ordinal) return Type(c.ClrType);         // type-as-code → typeof(...)
        throw OutOfRange(i);
    }

    public object GetValue(int i)
    {
        if (!_onRow)
            throw new System.InvalidOperationException("No current row. Call Read() first.");
        foreach (var c in Columns)
            if (i == c.Ordinal)
                return (object?)Member(_e.Current, c.Name) ?? System.DBNull.Value;  // _e.Current.Name
        throw OutOfRange(i);
    }

    // ---- typed getters: RUNG 3, ONE specializer bound to all twelve ----

    [Specialize<TypedGetter>] public bool             GetBoolean(int i)  => default;
    [Specialize<TypedGetter>] public byte             GetByte(int i)     => default;
    [Specialize<TypedGetter>] public char             GetChar(int i)     => default;
    [Specialize<TypedGetter>] public System.DateTime  GetDateTime(int i) => default;
    [Specialize<TypedGetter>] public decimal          GetDecimal(int i)  => default;
    [Specialize<TypedGetter>] public double           GetDouble(int i)   => default;
    [Specialize<TypedGetter>] public float            GetFloat(int i)    => default;
    [Specialize<TypedGetter>] public System.Guid      GetGuid(int i)     => default;
    [Specialize<TypedGetter>] public short            GetInt16(int i)    => default;
    [Specialize<TypedGetter>] public int              GetInt32(int i)    => default;
    [Specialize<TypedGetter>] public long             GetInt64(int i)    => default;
    [Specialize<TypedGetter>] public string           GetString(int i)   => default;

    // ---- invariant members: unchanged plain quoted C# ----
    // GetValues, IsDBNull, this[int], this[string], GetDataTypeName, Depth, IsClosed,
    // RecordsAffected, Read, NextResult, Close, Dispose, GetSchemaTable, GetBytes,
    // GetChars, GetData, OutOfRange, NoColumn — IDENTICAL to today. (elided)
}
```

### The specializer — a thin *router*, not a syntax factory

This is the cohesion move. `ISpecializer` does **not** drop you back into hand-built `SyntaxFactory` — that would re-introduce exactly the friction Synto exists to delete. Instead the specializer does the *one* imperative thing the declarative rung can't (a member-aware filter), then **reuses a normal `[Template]`** for the body:

```csharp
sealed class TypedGetter : ISpecializer
{
    public MemberDeclarationSyntax Specialize(SpecializeContext ctx)
    {
        // the ONE line declarative holes can't express:
        // pick the columns whose CLR type matches THIS member's return type.
        var mine = ctx.Columns.Where(c => c.ClrType == ctx.Member.ReturnType);

        // no SyntaxFactory — keep the member's own signature, fill the body from a template.
        return ctx.Member.WithBody(Factory.TypedGetterBody(ctx.Member.ReturnType, mine));
    }
}

[Template(typeof(Factory), Options = TemplateOption.Bare)]
private static TRet TypedGetterBody<[Inline(AsSyntax = true)] TRet>(
    [Repeat] Repeat<ColumnInfo> mine, int i)
{
    foreach (var c in mine)
        if (i == c.Ordinal) return (TRet)GetValue(i);
    throw OutOfRange(i);
}
```

`SpecializeContext` exposes only equatable, no-`Compilation`-rooting data — same cacheability rule as everything else in the pipeline:

```csharp
readonly struct SpecializeContext
{
    public Repeat<ColumnInfo> Columns   { get; }   // the model
    public MemberView         Member    { get; }   // .Name, .ReturnType (a type token, NOT ISymbol), .Parameters
    public System.Type        ElementType { get; } // T
}
```

### What rung 3 actually buys you

For `Person { Name: string, Age: int }`, the *single* `TypedGetter` produces a correct, **member-aware** body for each of the twelve — something a uniform repeater fundamentally cannot, because each switches over a *different* subset of columns:

```csharp
public int      GetInt32(int i)  { if (i == 1) return (int)GetValue(i);    throw OutOfRange(i); } // Age
public string   GetString(int i) { if (i == 0) return (string)GetValue(i); throw OutOfRange(i); } // Name
public DateTime GetDateTime(int i) => throw OutOfRange(i);                                          // no match → empty
```

Twelve members, **one** logic unit, **zero** SyntaxFactory.

---

## Binding — the "both" you liked, reconciled with the quote model

You liked the attribute form *and* the value-passed form. Grounded in Synto's model (templates are **quoted, never run**, so there is no runtime to register against), the two cohesive front-ends to the *same* `ISpecializer` contract are:

**A. Attribute binding** — Synto news up the specializer. Static, local, the 99% case:
```csharp
[Specialize<TypedGetter>] public int GetInt32(int i) => default;   // C# 11 generic attribute
[Specialize(typeof(TypedGetter))] public int GetInt32(int i) => default;   // pre-C# 11, identical semantics
```

**B. Parameter binding** — *you* construct it (configured / stateful), and it flows in as a `Factory` argument, exactly parallel to how the `[Repeat]` columns already flow in:
```csharp
// in ObjectReaderGenerator — pass a configured instance instead of attributing the member:
var decl = Factory.ObjectReaderTemplate(elementType, columns, typedGetter: new TypedGetter(opts));
```

> **Note on the fluent `Template.For<>().Specialize(t => t.GetInt32, …)` spelling:** it reads beautifully, but it implies a *runtime registration step* that has no home in a quote-time template — there's no instance to call it on. **Parameter binding gives you the same win** (specializer chosen by value, fully type-checked, supports state/config) without inventing a registration DSL or a place to run it. That's the cohesive version of your option 3.

Same contract, two providers: **Synto news it (attribute)** or **you news it (parameter)**. Nothing else added.

---

## The complete primitive set (the whole ask, on one screen)

```
MODEL    record struct ColumnInfo(...)         ← generator-authored, equatable

RUNG 1   [Inline] value/type hole              ← exists
RUNG 2   [Repeat] source + phantom foreach     ← uniform-per-item members
           ├─ Member(x, name)  → x.name        (identifier-as-code hole)
           └─ Type(t)          → typeof(t)     (type-as-code hole)
RUNG 3   [Specialize<T>] / ISpecializer        ← member-aware members
           └─ router pattern: filter + reuse a [Template] (no SyntaxFactory)

BINDING  attribute  (Synto news it)  |  parameter (you news it, configured)
```

Five new names total: `[Repeat]`, `Member`, `Type`, `[Specialize]`, `ISpecializer`. Everything composes through the one `ColumnInfo` model.

---

## Open decisions (where I want your reaction)

1. **Specializer return shape.** Recommend the **router pattern** (filter + reuse a `[Template]`) as the *blessed* path, with raw `MemberDeclarationSyntax` allowed as an escape-within-the-escape. Or do you want `ISpecializer` to *only* ever return a template-built member (no raw-syntax door at all)?
2. **Binding surface.** Recommend **attribute + parameter**, and *dropping* the runtime fluent-registration DSL (no home in the quote model). OK to lose the literal `Template.For<>()` spelling?
3. **List-driven members (the 5).** Recommend keeping them **declarative (rung 2)** — they're genuinely uniform-per-column, so a specializer would be overkill. Agree, or do you want to see them as specializers too for consistency?
4. **Hole spelling.** `using static Synto.Templating.Hole;` → `Member(...)` / `Type(...)`. Better names? (`Ident`/`TypeOf`? `Splice.Member`?)
```

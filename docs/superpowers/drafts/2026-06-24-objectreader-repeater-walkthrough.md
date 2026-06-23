# ObjectReader under the lexical-projection repeater — a concrete walkthrough

> **STATUS: DRAFT — code-first illustration for owner review; NOT an approved plan, no source touched.**
> Companion to `docs/superpowers/drafts/2026-06-24-synto-real-world-roadmap.md` (idea B / P2, plus
> P0 the identifier hole). Where the roadmap argues in prose, this shows the *actual* before/after C#.
>
> - **Date:** 2026-06-24
> - **Baseline:** assumes `generator-utilities` landed (the example already uses
>   `Synto.Generators.EquatableArray<T>` / `LocationInfo` — roadmap §1.3).
> - **Every hypothetical Synto surface element below is marked `ILLUSTRATIVE` — spelling not pinned.**
>   The C# is written to actually compile, because the point is concreteness. Real files referenced
>   by absolute path; nothing here is edited.
>
> **Files referenced (real, current working copy):**
> - `examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Generator/ReaderTemplate.cs`
> - `examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Generator/ObjectReaderGenerator.cs`
> - `examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Generator/Model.cs`
> - `examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Demo/Program.cs` (`Create(people, "Name", "Age")`)
> - `examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Tests/snapshots/ObjectReaderSnapshotTests.Generates_Specialized_Reader_For_Person#ObjectReader.g.verified.cs` (today's output)

---

## 1. The mechanism in one paragraph

A **lexical-projection repeater** lets a `[Template]` carrier member's **body** contain a real
`foreach` over a quote-time, value-equatable collection (`EquatableArray<ColumnInfo>`). At
**generation time** the `[Template]` factory unrolls that loop, emitting one chunk of syntax per
item. Each repeater site is identified **by where it is written** — its body *is* the per-item
projection, and the loop variable `c` *is* the current item — exactly like the existing
`Syntax statement; statement();` repeater identifies a splice by where `statement()` is called
(`SyntaxParameterFinder.cs`), and exactly like the Matching DSL's §3.7 phantom-`foreach` recognizes
a repeat by **binding** to its sequence capture. There is **no global "which hole am I filling?"
callback** and **no name-dispatch** — that was the owner's old blocker, and it is gone: many small
lexically-sited projections, each implicitly handed the current `c`, never one switchboard that must
ask which member it is building. The collection flows in once; all expansion happens at emit time;
no non-equatable type ever enters pipeline state.

This is the resolution that makes the four `switch` members **and** the twelve cast-less typed getters
all express as ordinary, compile-checked C# instead of `StringBuilder` + `ParseMemberDeclaration`.

**One spelling, committed to below; alternatives considered are listed once in §7.**

---

## 2. BEFORE — what gets deleted (owner knows this code; refs only)

All line numbers are the **current working copy** (post `generator-utilities`).

**`ObjectReaderGenerator.cs:328-383` — `BuildVariableMembers`** builds the five list-driven members as
**text**: a `StringBuilder` per switch, string-interpolated arms, then `ParseMemberDeclaration`:

```csharp
// :334-341 — arms concatenated as strings
nameArms.Append($"            {i} => \"{column.Name}\",\n");
ordinalArms.Append($"            \"{column.Name}\" => {i},\n");
fieldTypeArms.Append($"            {i} => typeof({column.ColumnTypeName}),\n");
valueArms.Append($"            {i} => (object?)_e.Current.{column.Name} ?? global::System.DBNull.Value,\n");
// …then Parse($$"""public string GetName(int i) => i switch { {{nameArms}} … };""")
```

**`ObjectReaderGenerator.cs:310-326` — `SpecializeMember`** is the swap pass: rename the ctor, and
overwrite each compiling placeholder member by name:

```csharp
case PropertyDeclarationSyntax property when variableMembers.TryGetValue(property.Identifier.Text, out var r): return r;
case MethodDeclarationSyntax method   when variableMembers.TryGetValue(method.Identifier.Text,   out var r): return r;
```

**`ObjectReaderGenerator.cs:293-296` — `BuildReader`'s swap wiring** (`variableMembers` dict + the
`Select(SpecializeMember)`), and **`:385` — `Parse`** (`ParseMemberDeclaration(...)!`, re-parsing the
strings the generator just glued).

**`ReaderTemplate.cs:27-45` — the five placeholder bodies** that exist only to be overwritten, plus
**`:15` — `#pragma warning disable CA1822`** that exists only because those placeholders look static:

```csharp
public int FieldCount => 0;
public string GetName(int i) => throw OutOfRange(i);
public int GetOrdinal(string name) => throw NoColumn(name);
public global::System.Type GetFieldType(int i) => throw OutOfRange(i);
public object GetValue(int i) { if (!_onRow) { throw new …; } throw OutOfRange(i); }
```

Out of scope here (roadmap P3/P4): the shell `.With*` fix-ups (`:298-305`) and the interceptor text
(`BuildInterceptorMethod` `:387-402`, holder `:250-255`) stay raw `SyntaxFactory`/string for now.

---

## 3. AFTER — the carrier (`ReaderTemplate.cs`)

### 3.0 ILLUSTRATIVE injected surface (the holes this uses)

Authored once in `src/Synto` under `namespace Synto.Templating`, injected `internal` via the existing
surface-injection path — **same mechanism as the `Syntax` delegate** that already powers the
fixed-count repeater. Spellings not pinned.

```csharp
namespace Synto.Templating; // ILLUSTRATIVE — not pinned

// Marks the quote-time collection a repeater iterates. The [Template] factory lifts the marked
// field to a factory parameter instead of treating it as runtime state.
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class RepeatAttribute : System.Attribute { }

public static class Template
{
    // P0 identifier hole, packaged as a member-access wrapper. Emits `instance.<name>` with <name>
    // spliced as an IdentifierName (NOT a string literal). The wrapper is needed because `_e.Current`
    // is typed `T` (unconstrained) — a bare `_e.Current.<hole>` cannot bind/compile in the carrier.
    public static TValue Member<TValue>(object instance, string name) => default!;

    // Sibling of the identifier hole for type position: emits `typeof(<name-as-type>)`.
    public static System.Type TypeOf(string name) => default!;
}
```

`c.Ordinal`, `c.Name`, `c.ColumnTypeName` need **no** marker — they are member-accesses on the
repeater's loop variable and are recognized **by binding** (the §3.1 leak-free rule), converted via the
existing `[Inline]`/`ToSyntax` machinery (`int` → literal, `string` → literal).

> **One ILLUSTRATIVE model tweak:** `ColumnInfo` gains its `Ordinal` (its position) so the projection
> can read `c.Ordinal` uniformly — today the ordinal is the loop index in `BuildVariableMembers`. It is
> a trivially value-equatable `int`, so cacheability is unaffected:
> `internal readonly record struct ColumnInfo(int Ordinal, string Name, string ColumnTypeName);`

### 3.1 The carrier's data-driven section — FULL

`using Synto.Generators;` (for `EquatableArray<ColumnInfo>`) and `using static Synto.Templating.Template;`
(for `Member` / `TypeOf`) at the top. The carrier is still real source in the netstandard2.0 generator
assembly — **every member below compiles** (see §3.3), so there are **no dummy placeholder bodies** and
the `CA1822` pragma is gone.

```csharp
[Template(typeof(Factory))]
internal sealed class ObjectReaderTemplate<[Inline(AsSyntax = true)] T>
{
    private readonly global::System.Collections.Generic.IEnumerator<T> _e;
    private bool _closed;
    private bool _onRow;

    public ObjectReaderTemplate(global::System.Collections.Generic.IEnumerable<T> source) => _e = source.GetEnumerator();

    // ---- THE quote-time input: the resolved column list -------------------------------------------
    // [Repeat] makes the [Template] factory lift this to a parameter (EquatableArray<ColumnInfo> columns).
    // The empty initializer exists ONLY so the carrier compiles; it is never read (the carrier is quoted,
    // never instantiated). Every repeater below iterates `Columns`.
    [Repeat] private static readonly EquatableArray<ColumnInfo> Columns = EquatableArray<ColumnInfo>.Empty;

    // ---- data-driven members: each body is a phantom foreach over Columns --------------------------

    // Degenerate value hole: count of the repeat source → an int literal.
    public int FieldCount => Columns.Count;

    public string GetName(int i)
    {
        foreach (var c in Columns)            // phantom repeat — legal statement position, compiles
            if (i == c.Ordinal)               // c.Ordinal → int literal (per-item projection)
                return c.Name;                // c.Name → string literal
        throw OutOfRange(i);
    }

    public int GetOrdinal(string name)
    {
        foreach (var c in Columns)
            if (name == c.Name)               // c.Name → string literal
                return c.Ordinal;             // c.Ordinal → int literal
        throw NoColumn(name);
    }

    public global::System.Type GetFieldType(int i)
    {
        foreach (var c in Columns)
            if (i == c.Ordinal)
                return TypeOf(c.ColumnTypeName);   // type-name hole → typeof(<that type>)
        throw OutOfRange(i);
    }

    public object GetValue(int i)
    {
        if (!_onRow)                          // invariant guard — literal, precedes the repeater
            throw new global::System.InvalidOperationException("No current row. Call Read() and ensure it returned true before reading values.");
        foreach (var c in Columns)
            if (i == c.Ordinal)
                // Member<object>(_e.Current, c.Name) → `_e.Current.<Name>` (identifier hole), then box + DBNull
                return (object?)Member<object>(_e.Current, c.Name) ?? global::System.DBNull.Value;
        throw OutOfRange(i);
    }

    // ---- cast-less typed getters: data-driven, filtered by the column's CLR type -------------------
    // Each getter repeats over ONLY its own-typed columns (the `where` filter) and returns the member
    // DIRECTLY via the identifier hole — no (T)GetValue(i), no boxing. Non-matching ordinals throw.

    public int GetInt32(int i)
    {
        foreach (var c in Columns.Where(c => c.ColumnTypeName == "global::System.Int32"))   // where-filter
            if (i == c.Ordinal)
                return Member<int>(_e.Current, c.Name);     // → return _e.Current.<Name>;  (no cast, no box)
        throw new global::System.InvalidCastException($"Field {i} is not an Int32 column.");
    }

    public string GetString(int i)
    {
        foreach (var c in Columns.Where(c => c.ColumnTypeName == "global::System.String"))
            if (i == c.Ordinal)
                return Member<string>(_e.Current, c.Name);
        throw new global::System.InvalidCastException($"Field {i} is not a String column.");
    }

    public global::System.DateTime GetDateTime(int i)
    {
        foreach (var c in Columns.Where(c => c.ColumnTypeName == "global::System.DateTime"))
            if (i == c.Ordinal)
                return Member<global::System.DateTime>(_e.Current, c.Name);
        throw new global::System.InvalidCastException($"Field {i} is not a DateTime column.");
    }

    // The other NINE cast-less getters are byte-identical in shape, differing only in return type +
    // the `where` filter string + the throw message:
    //   GetBoolean→bool, GetByte→byte, GetChar→char, GetDecimal→decimal, GetDouble→double,
    //   GetFloat→float, GetGuid→System.Guid, GetInt16→short, GetInt64→long.
    // GetDataTypeName ALONE stays invariant — it reads `GetFieldType(i).Name`, so it rides the
    // now-data-driven GetFieldType without itself being a repeater.

    // ---- invariant members UNCHANGED (the [Template] payload) ---------------------------------------
    // GetValues, IsDBNull, this[int], this[string], GetDataTypeName, Depth/IsClosed/RecordsAffected,
    // Read/NextResult/Close/Dispose, GetSchemaTable/GetBytes/GetChars/GetData, OutOfRange/NoColumn —
    // exactly as in ReaderTemplate.cs:49-129 today. They still call the data-driven members
    // (GetValues→GetValue, this[string]→GetOrdinal, GetDataTypeName→GetFieldType) and bind fine.
}

internal static partial class Factory { }
```

**Data-driven site count:** `FieldCount` (1) + the four reshaped members (4) + the twelve cast-less
getters (12) = **17** (the roadmap's "~18"), all authored as compiling C#, zero string concatenation.

### 3.2 The three holes made explicit (the crux)

| In the projection | Emits as | Hole flavor |
|---|---|---|
| `c.Ordinal` | `0`, `1`, … | value hole → **int literal** (existing `[Inline]`/`ToSyntax`) |
| `c.Name` (in `GetName`/`GetOrdinal`/`GetValue` comparisons/returns) | `"Name"` | value hole → **string literal** |
| `Member<T>(_e.Current, c.Name)` | `_e.Current.Name` | **P0 identifier hole** — `c.Name` spliced as an `IdentifierName`, NOT `"Name"` |
| `TypeOf(c.ColumnTypeName)` | `typeof(global::System.Int32)` | type-name hole (identifier-hole sibling) |

The identifier hole is the load-bearing one: `GetValue` and every cast-less getter read
`_e.Current.<Member>`, where the column's member name must land as **code** (`.Age`), not data
(`"Age"`). A bare `_e.Current.<hole>` cannot compile because `_e.Current` is an unconstrained `T`, so
the hole is wrapped: `Member<TValue>(_e.Current, c.Name)`.

### 3.3 How it compiles, and how it unrolls (design point #5)

- **It compiles** because every construct is real: `Columns` is an `EquatableArray<ColumnInfo>`
  (implements `IReadOnlyList<T>` → `Count`, `foreach`, and LINQ `.Where` all bind);
  `ColumnInfo` is a `record struct` (satisfies `EquatableArray`'s `T : IEquatable<T>`); `c.Ordinal`/
  `c.Name`/`c.ColumnTypeName` are ordinary member-accesses; `Member<int>(…)` returns `int`,
  `TypeOf(…)` returns `System.Type`, so every `return` type-checks. The carrier never *runs* — it is
  quoted — so the empty `Columns` and the `default!` marker bodies are inert.
- **It unrolls** by the **same mechanism as the existing `Syntax` repeater**, generalized from
  fixed-count to collection-driven. Today `TemplateFactorySourceGenerator` turns each `statement()`
  call into one splice (`SyntaxParameterFinder.cs`). Here, when the quoter meets `foreach (var c in
  Columns)` whose source is the `[Repeat]` field, it does **not** quote the loop literally — it emits a
  **real `foreach (var c in columns)` into the generated factory**, wrapping the quoted body, so the
  factory accumulates one unrolled chunk per item **at generation time**. Inside: `c.Ordinal` quotes to
  `c.Ordinal.ToSyntax()`, the identifier hole quotes to
  `MemberAccessExpression(_e.Current, IdentifierName(c.Name))`, `.Where(pred)` becomes `if (!(pred))
  continue;` at the loop head, and the literal scaffold (`if (i == …) return …;`) quotes to fixed
  `SyntaxFactory` calls. No `StringBuilder`, no re-parse — typed nodes throughout.

---

## 4. AFTER — the generator (`ObjectReaderGenerator.cs`)

### 4.1 `Transform` / `TransformCore` — UNCHANGED

The whole semantic front half is untouched: it still resolves the target type and each constant member
name, still flows out the equatable `ObjectReaderModel` carrying `EquatableArray<ColumnInfo>`
(`Model.cs:39-45`), still converts failures to `PendingDiagnostic` (C-5). The pipeline value is
identical, so cacheability is unchanged. (`ColumnInfo` gains `Ordinal` per §3.0 — set it to the loop
index `i` already used in `TransformCore`'s column loop, `ObjectReaderGenerator.cs:131-153`.)

### 4.2 `BuildReader` — collapses to one factory call + shell

```csharp
// AFTER — no variableMembers dict, no SpecializeMember, no StringBuilder. One factory call now carries
// BOTH holes: the element-type syntax hole (T) AND the quote-time column list, which the factory unrolls
// into all 17 data-driven members at generation time.
private static MemberDeclarationSyntax BuildReader(ObjectReaderModel model, int index)
{
    string name = $"ObjectReader_{model.TargetTypeShortName}_{index}";
    TypeSyntax elementType = SyntaxFactory.ParseTypeName(model.TargetTypeQualifiedName);

    ClassDeclarationSyntax skeleton = Factory.ObjectReaderTemplate(elementType, model.Columns);

    // The ONLY per-member fix-up left is the constructor rename (a ctor name must match its type).
    // This — plus the modifiers/base-list below — is the residual shell work (finding #3 / roadmap P3),
    // NOT part of the repeater.
    var members = SyntaxFactory.List(skeleton.Members.Select(m =>
        m is ConstructorDeclarationSyntax ctor ? ctor.WithIdentifier(SyntaxFactory.Identifier(name)) : m));

    return skeleton
        .WithIdentifier(SyntaxFactory.Identifier(name))
        .WithModifiers(SyntaxFactory.TokenList(
            SyntaxFactory.Token(SyntaxKind.FileKeyword),
            SyntaxFactory.Token(SyntaxKind.SealedKeyword)))
        .WithBaseList(SyntaxFactory.BaseList(SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(
            SyntaxFactory.SimpleBaseType(SyntaxFactory.ParseTypeName("global::System.Data.IDataReader")))))
        .WithMembers(members);
}
```

The factory contract widens by exactly one argument (the columns):

```csharp
// generated into the Factory partial — shape ILLUSTRATIVE:
ClassDeclarationSyntax Factory.ObjectReaderTemplate(ExpressionSyntax T, EquatableArray<ColumnInfo> columns);
```

### 4.3 `Emit` — reader-member text plumbing gone

`Emit` keeps its overall shape (replay diagnostics → collect intercepting models → per model
`BuildReader` + build the interceptor holder → `AddSource`), but the reader half no longer touches any
text:

```csharp
for (int index = 0; index < intercepting.Count; index++)
{
    ObjectReaderModel model = intercepting[index];
    members.Add(BuildReader(model, index));               // ← was: + BuildVariableMembers + SpecializeMember
    interceptors.Append(BuildInterceptorMethod(model, index));  // ← UNCHANGED (interceptor body is roadmap P4)
}
```

**`BuildVariableMembers`, `SpecializeMember`, and `Parse` are deleted outright.** The interceptor
`StringBuilder` (`interceptors`) survives only because the interceptor *body* is roadmap P4, explicitly
out of scope here — it is the last text in the file after this change.

---

## 5. The GENERATED output — `Create(people, "Name", "Age")`

The Demo (`Program.cs:23`) calls `ObjectReader.Create(people, "Name", "Age")` on a `Person` with
`Name : string` and `Age : int`. Columns resolve to `[ (0, "Name", global::System.String),
(1, "Age", global::System.Int32) ]`.

### 5.1 Today (switch-EXPRESSIONS — the verified snapshot)

```csharp
public int FieldCount => 2;
public string GetName(int i) => i switch { 0 => "Name", 1 => "Age", _ => throw OutOfRange(i), };
public int GetOrdinal(string name) => name switch { "Name" => 0, "Age" => 1, _ => throw NoColumn(name), };
public global::System.Type GetFieldType(int i) => i switch
    { 0 => typeof(global::System.String), 1 => typeof(global::System.Int32), _ => throw OutOfRange(i), };
public object GetValue(int i)
{
    if (!_onRow) { throw new global::System.InvalidOperationException("No current row. …"); }
    return i switch
    {
        0 => (object? )_e.Current.Name ?? global::System.DBNull.Value,
        1 => (object? )_e.Current.Age ?? global::System.DBNull.Value,
        _ => throw OutOfRange(i),
    };
}
public int GetInt32(int i) => (int)GetValue(i);     // boxes via GetValue, then unboxes
public string GetString(int i) => (string)GetValue(i);
public global::System.DateTime GetDateTime(int i) => (global::System.DateTime)GetValue(i);
```

### 5.2 After the repeater (if-CHAINS + cast-less getters — re-baselined)

```csharp
public int FieldCount => 2;

public string GetName(int i)
{
    if (i == 0) return "Name";
    if (i == 1) return "Age";
    throw OutOfRange(i);
}

public int GetOrdinal(string name)
{
    if (name == "Name") return 0;
    if (name == "Age") return 1;
    throw NoColumn(name);
}

public global::System.Type GetFieldType(int i)
{
    if (i == 0) return typeof(global::System.String);
    if (i == 1) return typeof(global::System.Int32);
    throw OutOfRange(i);
}

public object GetValue(int i)
{
    if (!_onRow)
        throw new global::System.InvalidOperationException("No current row. Call Read() and ensure it returned true before reading values.");
    if (i == 0) return (object?)_e.Current.Name ?? global::System.DBNull.Value;
    if (i == 1) return (object?)_e.Current.Age ?? global::System.DBNull.Value;
    throw OutOfRange(i);
}

// cast-less — the new behavior the repeater unlocks:
public int GetInt32(int i)
{
    if (i == 1) return _e.Current.Age;        // Age is the ONLY int column → one arm, no cast, no box
    throw new global::System.InvalidCastException($"Field {i} is not an Int32 column.");
}

public string GetString(int i)
{
    if (i == 0) return _e.Current.Name;       // Name is the ONLY string column
    throw new global::System.InvalidCastException($"Field {i} is not a String column.");
}

public global::System.DateTime GetDateTime(int i)
{
    // Person has NO DateTime column → the where-filter yields zero items → ZERO arms emitted
    throw new global::System.InvalidCastException($"Field {i} is not a DateTime column.");
}
```

**Snapshot re-baseline — call it out.** The generated *shape* legitimately changes (switch-expression →
if-chain; typed getters stop delegating to `GetValue`), so
`…snapshots/…#ObjectReader.g.verified.cs` **WILL change** and must be re-verified. This is the correct
signal that construction changed — unlike the byte-identical `generator-utilities` refactor. The behavior
of `GetName`/`GetOrdinal`/`GetFieldType`/`GetValue` is preserved; `GetInt32`/`GetString`/… now read the
member **directly** (`_e.Current.Age`, no boxing), and a wrong-type ordinal throws `InvalidCastException`
with zero arms when no column of that type exists (e.g. `GetDateTime` on `Person`).

---

## 6. Deletion summary

| Removed | Where (current working copy) | ≈ lines | Replaced by |
|---|---|---:|---|
| `BuildVariableMembers` (4 `StringBuilder`s + interpolated arms + `Parse`) | `ObjectReaderGenerator.cs:328-383` | 56 | repeater bodies in `ReaderTemplate.cs`, unrolled in the factory at emit time |
| `SpecializeMember` (placeholder swap) | `ObjectReaderGenerator.cs:310-326` | 17 | nothing — factory emits final members; only the ctor rename remains (→ P3) |
| `Parse` helper (`ParseMemberDeclaration(...)!`) | `ObjectReaderGenerator.cs:385` | 1 | gone — no strings to re-parse |
| `variableMembers` dict + `Select(SpecializeMember)` in `BuildReader` | `ObjectReaderGenerator.cs:293-296` | 4 | one factory arg: `model.Columns` |
| 5 placeholder bodies | `ReaderTemplate.cs:27-45` | 19 | 17 real, compiling data-driven members |
| `#pragma warning disable/ restore CA1822` | `ReaderTemplate.cs:15,133` | 2 | gone — members are real instance bodies |
| **Generator subtotal removed** | | **≈ 78** | ≈ 8 added (ctor-rename `Select` + the `columns` factory arg) → **net generator ≈ −74** |
| Carrier data-driven section | `ReaderTemplate.cs` | +19 → ≈ +95 | grows: 12 cast-less getters become multi-line data-driven (was one-line `(T)GetValue` casts) + new `Columns` field |

**Net line delta ≈ flat** (generator −74, carrier +75). That is *not* the win — the win is qualitative:
**`StringBuilder`/`ParseMemberDeclaration` count → 0**, every emitted member is compile-checked
syntax, and a genuinely new capability (cast-less, allocation-free typed access) lands inside the same
change. Fewer lines was never the goal; *text-free* was.

---

## 7. The real decisions this concrete code forces

These are the choices the owner actually has to make — each is shown above in exactly one spelling, with
the alternatives I weighed:

1. **Repeater spelling — phantom-`foreach` (shown) vs method-call `Repeat(columns, c => …)` vs a LINQ
   `columns.Select(c => …).Splice()`.** I committed to **phantom-`foreach`** because (a) `return`/`throw`
   in the body flow to the *enclosing* method, so the carrier reads identically to the unrolled output,
   whereas a `c => …` lambda's `return` would return from the lambda; and (b) it is the direct sibling
   of both the existing `Syntax` repeater and the Matching §3.7 phantom-`foreach` (recognize-by-binding,
   no new construct). A method-call `Repeat` reads better only when the projection is a single
   *expression* (an argument or a switch arm), which §3 deliberately avoids.

2. **How the projection references item fields — bare binding (`c.Ordinal`, `c.Name`) for values, a
   wrapper marker `Member<T>(_e.Current, c.Name)` for the identifier hole.** The wrapper is *forced*:
   `_e.Current` is an unconstrained `T`, so a bare `_e.Current.<hole>` will not bind/compile. Alternatives
   rejected: an `[Inline(AsIdentifier=true)] string` spliced bare (won't compile against `T`), and
   `_e.Current.Get<T>(c.Name)` extension-style (drags an extension into the carrier). **`TypeOf(...)` is
   its type-position sibling** for `GetFieldType`'s `typeof(...)`. → This is roadmap **P0**, and it is the
   crux: ship it first or `GetValue`/the cast-less getters can't template.

3. **Statement-`switch` vs if-chain (shown) — and why switch is NOT an option.** A statement
   `switch (i) { case c.Ordinal: … }` hits the **exact same CS0150 wall** as the switch-*expression*:
   a `case` label requires a *constant*, and `c.Ordinal` is per-item, not constant. The **if-chain** is
   the only reshape that hosts the hole in a legal position — `if (i == c.Ordinal)` is an ordinary `bool`
   expression with no constant requirement. So the reshape is not a style preference; it is what makes the
   hole legal. (This re-baselines the snapshot — §5.2.)

4. **How the `where` filter is expressed — `.Where(c => predicate)` on the `[Repeat]` source (shown).**
   The factory transcribes the predicate verbatim into an `if (!(predicate)) continue;` at the head of
   the generated loop (it is *copied*, not interpreted), so a column-type filter is just real C# the
   factory runs at generation time. The open sub-decision is **what the predicate compares**: I used the
   display string `c.ColumnTypeName == "global::System.Int32"`, which is concrete but couples the filter
   to the `SymbolDisplayFormat`. The cleaner alternative is to give `ColumnInfo` a `SpecialType`/kind
   enum and filter on that — a small model change worth deciding before this freezes.

5. **`[Repeat]` field vs `[Repeat]` constructor-parameter for the quote-time collection.** I used a
   `[Repeat]` *field* (`Columns`) so the loop variable is in scope for every member uniformly; a ctor
   parameter would also work and reads as "an input," but then every member would have to reach a captured
   field anyway. Bikeshed, but pin it: it determines the generated `Factory.ObjectReaderTemplate(...,
   EquatableArray<ColumnInfo> columns)` signature, which is a consumer-facing contract once snapshotted.

> Dependency note (roadmap §6): this walkthrough is **P0 + P2**. P0 (identifier hole, decision 2) is the
> prerequisite. P1 (`[TemplateHole]` reserved-member carrier) is *orthogonal* here — because the repeater
> bodies compile as real C#, the carrier needs no placeholder bodies and no `[TemplateHole]` at all for
> these members; P1 remains useful for the ctor/shell story but is not on this change's critical path.

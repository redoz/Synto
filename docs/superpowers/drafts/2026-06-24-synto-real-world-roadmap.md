# Synto real-world roadmap — killing the residual `SyntaxFactory`/string-concat

> **STATUS: DRAFT — design exploration for owner review; NOT an approved plan.**
> Written overnight against the repo as it stands (working copy pinned pre-Task-3 of
> `generator-utilities`). It explores design alternatives and sequences the next features; each
> section is promotable to a real `docs/superpowers/plans/` file after the owner picks the
> open decisions in §7. Nothing here is implemented and no source was touched.
>
> - **Date:** 2026-06-24
> - **Reads on:** the ObjectReader friction log (`docs/superpowers/notes/2026-06-22-objectreader-synto-friction.md`),
>   the in-flight `generator-utilities` plan/spec (`docs/superpowers/plans/2026-06-22-generator-utilities.md`,
>   `docs/superpowers/specs/2026-06-22-generator-utilities-design.md`), and the shipped Templating +
>   Matching surfaces.
> - **Baseline assumption:** `generator-utilities` Tasks 1–3 are **done** (see §1.3). All before/after
>   snippets below start from that post-refactor state, not from the current working copy.

---

## 1. Framing

### 1.1 Synto's promise is *text-free* syntax authoring

Synto exists to let a generator author emit code **as syntax** — type-checked, refactorable,
IntelliSense-lit — instead of as concatenated strings. Templating delivers this for *invariant*
shapes: you write the shape once as ordinary compiling C# under `[Template(typeof(Factory))]`, and
Synto quotes it into a `Factory.X(...)` that rebuilds it as a `SyntaxNode`
(`src/Synto.SourceGenerator/Templating/TemplateFactorySourceGenerator.cs`). Holes thread the
variable parts through:

- `[Inline] T x` — a **value** hole: the factory takes a `T` and converts it to a literal via
  `x.ToSyntax()` (built-in types) or a discovered `[Runtime]` converter (custom types)
  (`TemplateFactorySourceGenerator.cs:358-468`).
- `[Inline(AsSyntax = true)] T x` — a **syntax** hole: the factory parameter is `ExpressionSyntax`
  and the caller passes a node (`InlineAttribute.cs`, `Program.cs` Test2).
- `[Inline] T` on a **type** parameter — inlines a type (as a real generic param, or `AsSyntax` to
  splice `T` wherever it appears as a type) (`Program.cs` Test6/Test7).
- `Syntax` / `Syntax<T>` delegate params — the **statement/expression repeater**: each invocation
  of the delegate in the body is replaced by the passed node
  (`src/Synto/Templating/Syntax.cs`, `SyntaxParameterFinder.cs`, `Program.cs` Test3).

The whole point: an author never builds C# by gluing strings. **Where the ObjectReader generator
still glues strings, Synto has a hole in its hole vocabulary.** That gap is this roadmap.

### 1.2 The dog-food-as-probe method

`examples/Synto.Example.ObjectReader/` is a deliberate friction probe (per
`docs/superpowers/specs/2026-06-22-objectreader-example-design.md` §11 and MEMORY:
*ObjectReader dog-food drives features*). The rule: build a real, semi-complex generator against
Synto as-is, and **every place it falls back to raw `SyntaxFactory` or string concat is a logged
finding**, not a failure. The friction log ranks five findings; this roadmap is the work *after*
`generator-utilities` clears the first two plumbing findings.

### 1.3 Exactly what `generator-utilities` just removed (the new baseline)

`generator-utilities` (landing now on `experimental/object-reader`) injects a `Synto.Generators`
namespace and refactors the example onto it. After its Task 3, relative to today's working copy:

| Removed from the example | Replaced by | Finding |
|---|---|---|
| hand-rolled `EquatableArray<T>` (`Model.cs:74-133`) | injected `Synto.Generators.EquatableArray<T>` | #2 (cacheability toolkit) — **SOLVED** |
| hand-rolled `LocationInfo` (`Model.cs:31-44`) | injected `Synto.Generators.LocationInfo` | #2 — **SOLVED** |
| `InterceptsLocationAttributeSource` const (`ObjectReaderGenerator.cs:280-295`) | `Synto.Generators.Interceptors.AddDefinition(spc)` (`src/Synto/Generators/Interceptors.cs`) | #5 attribute-definition tail — **SOLVED** |
| the `IsExternalInit` polyfill (already deleted earlier) | injected unconditionally | doc-only — SOLVED |

What it explicitly **did not** touch (spec §8 non-goals): the list-driven switch members, the
interceptor method body's `(object)` cast, the carrier's reserved-member dummy bodies, the
emitted-shell `.With*` fix-ups, and `Synto.Diagnostics` cacheable support. **Those are the residual
problem this roadmap attacks.** The example's `PendingDiagnostic` enum carrier (renamed from
`DiagnosticInfo`) stays as a stopgap and is out of scope here too.

---

## 2. The residual problem, shown in code

After §1.3, `ObjectReaderGenerator.cs` still builds code as **text**, which is precisely "the
opposite of what we want to enable." Three clusters remain.

### 2.1 The five list-driven members — `BuildVariableMembers` (`ObjectReaderGenerator.cs:346-401`)

This is finding #1, the single biggest wall. `FieldCount` + four `switch` members have a **variable
arm count** (one arm per resolved column), so they fall back to `StringBuilder` + string
interpolation + `SyntaxFactory.ParseMemberDeclaration`:

```csharp
// ObjectReaderGenerator.cs:348-359 — arms built as TEXT
for (int i = 0; i < model.Columns.Count; i++)
{
    ColumnInfo column = model.Columns[i];
    nameArms.Append($"            {i} => \"{column.Name}\",\n");
    ordinalArms.Append($"            \"{column.Name}\" => {i},\n");
    fieldTypeArms.Append($"            {i} => typeof({column.ColumnTypeName}),\n");
    valueArms.Append($"            {i} => (object?)_e.Current.{column.Name} ?? global::System.DBNull.Value,\n");
}
// …then each member is Parse($$"""public string GetName(int i) => i switch { {{nameArms}} … };""")  (361-401)
```

`Parse` (`:403`) is `ParseMemberDeclaration(...)!` — re-parsing strings the generator just
concatenated. **Why it exists:** a `[Template]` is a fixed syntax shape and a Synto hole splices a
*single* node; neither expresses a data-driven *list expansion*. The existing `Syntax` repeater is
**fixed-count** — `statement(); statement();` (`Program.cs:124-127`) splices the *same* node a
*hard-coded* number of times (`SyntaxParameterFinder.cs` + `TemplateFactorySourceGenerator.cs:476-490`
turn each `statement()` reference into one splice). There is no collection-driven cousin. So these
five members stay text. **Target: §4 (idea B).**

### 2.2 The carrier dummy bodies + the swap loop — `ReaderTemplate.cs` + `SpecializeMember`

Because the invariant members *call* the variable ones (`GetValues`→`GetValue`,
every typed getter→`GetValue`, `this[string]`→`GetOrdinal`), the `[Template]` carrier can't omit
them — each must exist as a **compiling placeholder body** the generator later overwrites:

```csharp
// ReaderTemplate.cs:27-45 — placeholders that exist only to be overwritten
public int FieldCount => 0;
public string GetName(int i) => throw OutOfRange(i);
public int GetOrdinal(string name) => throw NoColumn(name);
public global::System.Type GetFieldType(int i) => throw OutOfRange(i);
public object GetValue(int i) { if (!_onRow) { throw new …; } throw OutOfRange(i); }
```

(note the `#pragma warning disable CA1822` at `:15` exists *only* because these placeholders look
static.) The generator then **swaps** them out node-by-node:

```csharp
// ObjectReaderGenerator.cs:328-344 — SpecializeMember: rename ctor + overwrite placeholders
case PropertyDeclarationSyntax property when variableMembers.TryGetValue(property.Identifier.Text, out var replacement):
    return replacement;
case MethodDeclarationSyntax method when variableMembers.TryGetValue(method.Identifier.Text, out var replacement):
    return replacement;
```

**Why it exists:** there is no way to *reserve* a member to be supplied at quote time. This is
finding #4 + the owner's "declare it abstract" idea. **Target: §3 (idea A).**

### 2.3 The shell fix-ups + the interceptor body — raw `SyntaxFactory` / interpolation

```csharp
// ObjectReaderGenerator.cs:316-323 — the emitted SHELL, hand-fixed after the factory call
return skeleton
    .WithIdentifier(SyntaxFactory.Identifier(reader))                 // per-call-site name
    .WithModifiers(SyntaxFactory.TokenList(FileKeyword, SealedKeyword)) // `file sealed`
    .WithBaseList(BaseList(SimpleBaseType(ParseTypeName("global::System.Data.IDataReader"))));
```
**Why:** a `[Template]` can't carry a `file` modifier (file types are top-level only) or a
per-call-site type name. Finding #3. **Target: §5.1.**

```csharp
// ObjectReaderGenerator.cs:239,245-246,248-253 + 405-420 — interceptor holder built as TEXT
var interceptors = new StringBuilder();
…
interceptors.Append(BuildInterceptorMethod(model, index));            // interpolated string body
…
members.Add(SyntaxFactory.ParseMemberDeclaration($$"""file static class ObjectReaderInterceptors {{{interceptors}}} """)!);
// BuildInterceptorMethod (405-420): => new {{reader}}((IEnumerable<{{t}}>)(object)source)  — arity + double-cast
```
**Why:** the intercepted `Create<T>` is generic, the reader is concrete, so the body needs the
generic-arity shell + an `(object)` bridge cast. `generator-utilities` solved only the *attribute
definition*; the *body* is the residual of finding #5. **Target: §5.2.**

---

## 3. Owner idea A — the abstract / reserved-member carrier (finding #4)

**Problem restated:** the carrier must compile, so the members the generator will overwrite need
dummy bodies (`ReaderTemplate.cs:27-45`), and the generator needs a swap pass
(`SpecializeMember`, `:328-344`). The owner's instinct: *"declare ObjectReader abstract — then we
don't have to declare all the things we're going to add in the source-gen anyway."*

### 3.1 Alternatives

**A1 — `abstract` carrier (the owner's literal idea).** Make the carrier an `abstract class` and the
five members `abstract`:

```csharp
[Template(typeof(Factory))]
internal abstract class ObjectReaderTemplate<[Inline(AsSyntax = true)] T>
{
    public abstract int FieldCount { get; }
    public abstract string GetName(int i);
    // … invariant members below call these — legal: a concrete instance method may call an abstract member
}
```

- *Recognition:* "abstract member ⇒ hole" is mechanical and needs no new attribute.
- *Compiles?* Yes — the prompt's worry is answered: **an abstract class's concrete members may call
  its abstract members and it compiles** (the calls bind to the abstract slot; virtual dispatch is a
  runtime concern the carrier never reaches). `GetValues`→`GetValue` etc. all bind fine.
- *Downsides:* (1) It **overloads** the C# `abstract` keyword to mean "Synto hole" — exactly the
  leak the Matching DSL forbids in its §3.1 "recognize by binding, never by overloading a
  construct" invariant. An author who legitimately wants to **quote a genuinely abstract base
  class** (a real use case for a generator) now finds every abstract member silently treated as a
  hole. (2) `abstract` forbids `static` and `file`, and forces the carrier non-`sealed` — fine
  *here*, but it bakes shape constraints into a mechanism that should be shape-agnostic. (3) It
  conflates "no body" with "is a hole": you can't have an abstract member that is *not* a hole.

**A2 — a `[TemplateHole]` / `[ReservedMember]` marker (recommended).** Mark the member explicitly;
optionally make it abstract to avoid the dummy body. Recognition is by the **marker**, not by
`abstract`-ness:

```csharp
[Template(typeof(Factory))]
internal abstract class ObjectReaderTemplate<[Inline(AsSyntax = true)] T>
{
    [TemplateHole] public abstract int FieldCount { get; }          // abstract ⇒ no dummy body
    [TemplateHole] public abstract string GetName(int i);
    [TemplateHole] public abstract int GetOrdinal(string name);
    [TemplateHole] public abstract global::System.Type GetFieldType(int i);
    [TemplateHole] public abstract object GetValue(int i);
    // invariant members unchanged, still call the reserved members
}
```

The factory then exposes **one `MemberDeclarationSyntax` parameter per reserved member**, and the
generator **splices the supplied member in place of the reserved one** — no swap pass, no dummy
body:

```csharp
// Generated factory gains member-holes (one per [TemplateHole]); shape illustrative, not pinned:
ClassDeclarationSyntax Factory.ObjectReaderTemplate(
    ExpressionSyntax T,
    MemberDeclarationSyntax fieldCount, MemberDeclarationSyntax getName,
    MemberDeclarationSyntax getOrdinal, MemberDeclarationSyntax getFieldType,
    MemberDeclarationSyntax getValue);
```

This is the **member-level sibling** of the existing hole vocabulary: `[Inline(AsSyntax)]` is an
*expression* hole, `Syntax` is a *statement/expression* hole, `[TemplateHole]` is a *member* hole.
The marker keys recognition (leak-free, §3.1-style), while `abstract` is an *optional convenience*
to drop the dummy body. An author quoting a real abstract base is unaffected (no marker, no hole).

**A3 — partial members.** `public partial int FieldCount { get; }`. **Rejected:** a non-void
partial member *requires* an implementing declaration in the same compilation; the carrier has
none (the generator emits into the *consumer's* compilation, not the carrier's), so the carrier
fails to compile. Legacy void partial methods don't apply (the holes return values). Also partial
properties are very new (C# 13) and raise the carrier's required `LangVersion`.

### 3.2 Recommendation — A2, abstract-friendly

Ship `[TemplateHole]` as the recognized signal, **usable on an `abstract` member** (carrier becomes
an abstract class, no dummy body — the owner's exact ask) **or** on a concrete member (its body is
ignored). Rationale: gives the owner the abstract ergonomics without overloading `abstract`'s
meaning; unifies cleanly with the existing holes; and the factory member-hole parameter deletes
`SpecializeMember` outright.

### 3.3 Contract (pin-level)

- **Surface:** a `Synto.Templating.TemplateHoleAttribute` (`[AttributeUsage(Method | Property)]`),
  injected `internal` via the existing surface-injection path (no new mechanism).
- **Recognition:** a member carrying `[TemplateHole]` inside a `[Template]` type is a member hole.
  Its body (if any) is dropped; if `abstract`, the `abstract` modifier is dropped on emit.
- **Factory shape:** each reserved member becomes a factory parameter of the matching member-kind
  syntax type (`MemberDeclarationSyntax`, or narrower — `MethodDeclarationSyntax` /
  `PropertyDeclarationSyntax` — keyed off the reserved member's kind). The supplied node replaces
  the reserved member positionally in the emitted type.
- **Constructor identity:** the ctor-rename friction (`SpecializeMember` `:335-336`) is folded into
  §5.1's shell parameterization (the ctor identifier tracks the emitted type name), so A doesn't
  need a special case for it.
- **Validation (designed-for, not v1):** Synto *may* later emit a diagnostic when the supplied
  member's signature is incompatible with the reserved one (the invariant members bind against the
  reserved signature). v1 leaves signature-correctness to the author.
- **Cacheability:** unchanged — member holes are an emit-time concern; nothing new enters pipeline
  state.

### 3.4 What the example sheds with A

- `ReaderTemplate.cs:27-45` placeholder *bodies* → abstract signatures (no bodies);
  `#pragma warning disable CA1822` (`:15`) deleted.
- `ObjectReaderGenerator.cs:328-344` (`SpecializeMember`) **deleted** (~17 lines).
- `ObjectReaderGenerator.cs:311-314` (`variableMembers` dict build + `SpecializeMember` `Select`)
  collapses to passing the five built members as factory args.

---

## 4. Owner idea B — the list/repeater hole vs the `Pattern.Replace` splice (finding #1)

**Problem restated:** the four `switch` members have one arm per column; the existing repeater is
fixed-count; so `BuildVariableMembers` (`:346-401`) glues strings. We need **data-driven**
repetition: map a value-equatable collection to a list of nodes.

### 4.1 The hard syntactic constraint (must be stated up front)

A switch-**expression** arm is `pattern => value`, and the **pattern** position requires a
constant/type/var pattern — it **cannot host a non-constant marker or a hole**. `case ordinal:`
where `ordinal` is a parameter is `CS0150` ("a constant value is expected"). A `foreach` between
arms / between `case` labels is not valid C# either. **Therefore no in-body hole can live in a
switch-expression arm, and the carrier with such a hole would not compile** — which is the whole
premise of Templating. This is a C# constraint, not a Synto one, and it shapes every option below.

What *can* host a data-driven hole: **statement runs** (a `foreach`/marker invocation is legal in
statement position — this is why the existing repeater works), **member lists** (the factory
returns a type; members are appendable), and **comma-lists** of expressions/arguments (an
invocation marker can stand in an argument position).

### 4.2 Alternatives

**B1 — a data-driven repeater hole (the collection cousin of `Syntax`; recommended).** Generalize
the fixed `statement(); statement();` repeater to a **phantom `foreach` over a value-equatable
collection parameter**, with per-item `[Inline]` projections threading item fields into the
repeated shape. This is the **symmetric inverse of the Matching DSL's §3.7 phantom `foreach`**
(Matching *recognizes* a repeated run; Templating *builds* one) — a pleasing, already-precedented
shape:

```csharp
// Author writes (illustrative): one templated member whose body is a data-driven run.
[Template(typeof(Factory), Options = TemplateOption.Bare)]
static void GetNameBody([Repeat] EquatableArray<ColumnInfo> columns)
{
    foreach (var col in columns)                 // phantom repeat — legal in a block, compiles
        if (Index == col.Ordinal)                // per-item projections via [Inline]-style holes
            return col.Name;                     // string projection → literal
    throw OutOfRange(Index);
}
// → expands to: if (Index == 0) return "Name"; if (Index == 1) return "Age"; throw OutOfRange(Index);
```

- *Data flow:* the factory takes the collection (`EquatableArray<ColumnInfo>` — already value
  equatable, already what the pipeline carries) and, per item, evaluates the projections
  (`col.Ordinal` → `int.ToSyntax()`, `col.Name` → `string.ToSyntax()`) reusing the **existing**
  `[Inline]`/`ToSyntax`/`[Runtime]` machinery (`TemplateFactorySourceGenerator.cs:358-468`). No new
  converter concept.
- *Node kinds:* covers statement runs and (the same mechanism over a member-position foreach)
  member lists. It does **not** cover switch-expression arm *patterns* (§4.1).
- *The switch members:* reshape them from switch-**expression** form to a **statement-bodied
  if-chain** (as above) so the per-item unit is a statement the repeater can expand. This is a
  behavior-preserving reshape; the generated *shape* legitimately changes (switch → if-chain), so
  **the example's `ObjectReader.g.cs` snapshot re-baselines** — expected and correct for a feature
  that changes construction, unlike the byte-identical `generator-utilities` refactor.

**B1′ — a lighter "splice" sibling.** Where the author already has the per-item nodes (e.g. built by
a per-item `[Template]`), a list hole that splices a pre-built `IEnumerable<TNode>` into a marked
statement position (`items.Splice();`) is the minimal version — more composable, less magic, but
requires a separate per-item node source. Useful, smaller; can ship as a v1 subset of B1 or be
folded in. Recommended to design the surface so B1 and B1′ are the *same* hole viewed two ways
(declarative foreach vs. pre-built splice).

**B2 — a `Pattern.Replace`-style splice keyed on a placeholder arm (the friction log's alternative).**
Reuse `MatchPattern<T>.Replace(root, evaluator, ReplaceOption)`
(`docs/superpowers/specs/2026-06-22-match-replace-design.md`). Author writes the switch with one
literal placeholder arm, builds the skeleton, then `Replace`s the placeholder with the real arms.

- **Honest verdict — wrong tool for list expansion.** `Replace` is **1→1 by contract** (C-R2:
  "return type preserved; replacement is *a* `SyntaxNode`"). To go 1→N you must match the *arm list*
  (or the containing switch) and **rebuild the whole list in the evaluator** — which is exactly the
  raw `SyntaxFactory`/`SeparatedList` construction we are trying to delete. So `Pattern.Replace`
  *relocates* the string-concat into a lambda; it does not erase it. It also drags the Matching
  feature into a pure-Templating example for no expressiveness gain.
- **Keep `Pattern.Replace` for what it is:** genuine 1→1 rewrites (swap a matched sub-tree for a
  Template-built node — its actual design intent). It is not the list hole.

### 4.3 Recommendation — B1 (data-driven repeater), v1 scoped like the Matching DSL drew its line

Ship the phantom-`foreach` repeater for **statement runs and member lists** (the straight-line
subset). Explicitly **out of v1** (designed-for, deferred with a clean `SY*` diagnostic if reached):
switch-expression arm patterns (the §4.1 wall — reshape to statement form instead), and
multi-collection / zipped repeats. This mirrors how `matching-dsl` shipped the no-backtracking half
first and reserved the rest (`specs/2026-06-20-matching-dsl.md` §8).

**Companion gap — the member-name hole.** `GetValue`'s arm reads `_e.Current.{column.Name}`, where
the projected string must become an **identifier**, not a string literal. Today `[Inline] string`
yields a literal (`ToSyntax`). B needs an "inline as identifier/member-name" projection (a small,
separable hole flavor; the Matching DSL lists the symmetric "token/identifier capture" as
designed-for in its §9). Pin it as a sub-contract of B (or a tiny predecessor plan — see §6).

### 4.4 Contract (pin-level)

- **Surface:** a `[Repeat]` parameter marker (collection-typed) + the phantom `foreach (var item in
  repeatParam)` recognized in the body; per-item projections are member-accesses on the loop
  variable, converted via the existing `[Inline]` value/syntax machinery. (Marker spelling
  bikeshed-deferred — `[Repeat]` vs reusing `[Inline]` on a collection.)
- **Cacheability (sacred):** the repeated collection and every per-item projection source must be
  **value-equatable** (`EquatableArray<ColumnInfo>` already is). The expansion runs at **emit time**
  in the factory; no new non-equatable type enters pipeline state. This is the load-bearing
  constraint and the reason the items, not nodes, flow through the pipeline.
- **Node kinds (v1):** `SyntaxList<StatementSyntax>` runs and `SyntaxList<MemberDeclarationSyntax>`
  lists. Comma-lists (`SeparatedList` of arguments/parameters) and switch-expression arms are
  designed-for, not v1.
- **Member-name hole:** an inline mode that emits the projected `string` as an `IdentifierName`
  (member/identifier position) rather than a literal.
- **Diagnostics:** a reachable-but-unsupported shape (e.g. a phantom foreach in switch-arm position)
  degrades to a clean `SY*` diagnostic, never a mis-expansion — same discipline as `SY1203/SY1204`.

### 4.5 What the example sheds with B (+ the member-name hole)

- `ObjectReaderGenerator.cs:346-401` (`BuildVariableMembers`, the four switches' `StringBuilder` +
  interpolation + `Parse`) **deleted** (~55 lines) — the headline win.
- `ObjectReaderGenerator.cs:403` (`Parse` helper) deleted.
- `FieldCount` needs **no** new feature — it is `=> N`, already expressible as a plain `[Inline] int`
  (or `columns.Count`); only the four switches need B.

---

## 5. The other open friction findings

### 5.1 Finding #3 — template parameters for the emitted shell (name / modifiers / base-list)

**Residual:** `ObjectReaderGenerator.cs:316-323` hand-applies `.WithIdentifier` (class **and**, via
`SpecializeMember:335-336`, the ctor), `.WithModifiers(file sealed)`, `.WithBaseList(: IDataReader)`.

**Contract proposal — parameterize the *shell* distinctly from the *body*.** The factory should
accept the emitted type's **identifier**, **modifier list**, and **base list** as parameters (or a
single shell descriptor), so one shaped call replaces the three post-hoc `.With*` mutations and the
ctor rename. Two viable shapes (pin one in the plan):

- **(a) `[Template]` shell options** — declare which shell facets are per-call-site
  (`[Template(typeof(Factory), Shell = ShellFacet.Name | ShellFacet.Modifiers | ShellFacet.BaseList)]`),
  surfacing them as factory params; the carrier keeps a placeholder name/modifiers the factory
  overrides. The ctor identifier auto-tracks the supplied name (folds in the `SpecializeMember`
  ctor case).
- **(b) a typed shell-builder extension** — `factoryResult.WithShell(name, modifiers, baseList)` in
  `Synto.Generators` (or extend the existing `ClassDeclarationSyntaxExtensions`,
  `src/Synto/ClassDeclarationSyntaxExtensions.cs`), keeping the ctor-rename inside the helper. Less
  machinery, but it is sugar over `SyntaxFactory` rather than a true hole.

Recommendation: **(a)** for the type *name* (it must reach the ctor too, which a post-hoc helper
handles clumsily) + **(b)**-style sugar for `file sealed` / base-list which are pure shell mutations.
Lower priority than A/B (it is raw `SyntaxFactory`, not string-concat) but it is the last thing
keeping `BuildReader` from being a single factory call. **Sheds:** `ObjectReaderGenerator.cs:316-323`
and the ctor case of `SpecializeMember` (`:335-336`).

### 5.2 Finding #5 residual — the generic-arity / `(object)` interceptor cast

**Residual:** `BuildInterceptorMethod` (`:405-420`) + the `ObjectReaderInterceptors` holder
(`:248-253`) build the interceptor as text; the body is
`=> new Reader((IEnumerable<target>)(object)source)` — the generic-arity shell + `(object)` bridge
cast. `generator-utilities` solved the *attribute definition* (`Interceptors.AddDefinition`); this
is the *body/shell*.

**Contract proposal — an interceptor-stub emitter in `Synto.Generators.Interceptors`.** Given (the
interceptable location attribute syntax, the intercepted method's signature/arity, the target
concrete type, and the constructed-instance expression), emit the interceptor method with the
matching generic arity and the bridge cast, plus the `[InterceptsLocation]` usage — so the author
calls one helper instead of interpolating a method:

```csharp
// illustrative — the author supplies the "what to construct", Synto supplies the arity/cast/usage:
MethodDeclarationSyntax stub = Interceptors.Stub(
    location: model.InterceptsLocationAttribute,
    interceptedSignature: …,          // arity + param shape of Create<T>
    constructedInstance: /* new Reader(...) */);
```

This is the smallest of the four and depends on `generator-utilities` `Interceptors` already
existing. It is genuinely bespoke (it constructs the author's type), so the *body* of "what to
construct" stays the author's — Synto owns only the arity/cast/usage ceremony. **Sheds:**
`ObjectReaderGenerator.cs:405-420` and most of `:239,245-253`. (Could also be expressed as a
`[Template]` interceptor pattern once B's holes exist; note as an alternative.)

---

## 6. Recommended roadmap

Ordered by **dependency then payoff**. Each row is promotable to a `docs/superpowers/plans/*.md`
(contracts-not-syntax house style, per MEMORY *plans pin contracts not syntax*). All land on
`experimental/object-reader` (or a sibling `experimental/*`), never main (MEMORY *experimental
features on own branch*), each closed by the dog-food shedding concrete lines.

| # | Plan (one-line goal) | Removes (friction) | Contract it adds | Depends on | Size | ObjectReader before→after |
|---|---|---|---|---|---|---|
| **P0** | **Member-name (identifier) inline hole** — emit an `[Inline] string` projection as an `IdentifierName`, not a literal | the `_e.Current.{name}` blocker inside #1 | an inline mode / marker for identifier-position strings | none (extends `[Inline]`) | **S** | unblocks `GetValue`'s `_e.Current.{col.Name}` so B can template it |
| **P1** | **`[TemplateHole]` reserved-member carrier** (idea A) | #4 (carrier dummy bodies + `SpecializeMember` swap) | member-level hole; abstract-friendly; factory member-params | none | **M** | `ReaderTemplate.cs:27-45` → abstract sigs; `SpecializeMember` (`:328-344`) deleted |
| **P2** | **Data-driven repeater hole** (idea B, v1: statement runs + member lists) | #1 (the `StringBuilder` switches — the headline) | phantom-`foreach` `[Repeat]` collection hole; equatable item flow | P0 (name hole), lands cleanly on P1 | **L** | `BuildVariableMembers` (`:346-401`) + `Parse` (`:403`) deleted (~56 lines); snapshot re-baselines |
| **P3** | **Emitted-shell parameters** (name / modifiers / base-list) (finding #3) | #3 (`.WithIdentifier/.WithModifiers/.WithBaseList`) | shell facets distinct from body; ctor-name tracking | P1 (ctor case) | **S–M** | `BuildReader` (`:316-323`) collapses to one factory call |
| **P4** | **Interceptor-stub emitter** (finding #5 tail) | #5 (generic-arity + `(object)` cast body) | `Interceptors.Stub(...)` over the gen-utils attribute helper | `generator-utilities` (`Interceptors`) | **S–M** | `BuildInterceptorMethod` (`:405-420`) + holder (`:248-253`) deleted |

**Ordering rationale.**
- **P0 first** (tiny, unblocking): B can't template `GetValue` without an identifier hole; ship it
  as a self-contained extension to `[Inline]` so P2 has it.
- **P1 before P2** (dependency over raw payoff): P2 is the *bigger* win (it deletes the literal
  string-concat the owner objected to — the friction log's #1), but its data-driven members must
  *land* in the carrier. Today that landing is `SpecializeMember` over dummy bodies. If P2 ships
  first, its beautiful data-driven members still get jammed in via the ugly swap — a half-win and an
  awkward intermediate. P1 makes the carrier clean (member-holes, no swap) so P2's members flow
  straight in. Each step is independently green and the example improves monotonically.
  *(Alternative ordering P2→P1 is defensible if the owner wants the headline win prototyped first —
  flagged in §7.)*
- **P3, P4 last** (smallest, independent, raw-`SyntaxFactory`-not-string-concat): cleanup that turns
  `BuildReader`/the interceptor into single calls. P4 sits naturally on `generator-utilities`'
  `Interceptors`.

**End state of the dog-food:** `ObjectReaderGenerator.cs` loses `BuildVariableMembers`, `Parse`,
`SpecializeMember`, `BuildInterceptorMethod`, and the shell `.With*` calls (~130+ lines of
text/raw-factory); `ReaderTemplate.cs` loses its placeholder bodies. What remains is the equatable
transform (`Transform`/`TransformCore`/`ResolveMemberType`) + a handful of `Factory.*(...)` /
`Interceptors.*(...)` calls — i.e. **text-free syntax authoring**, which is the whole promise.

---

## 7. Open questions / decisions for the owner

1. **Build order P1→P2 vs P2→P1.** Recommendation is P1 (reserved-member) first so P2's data-driven
   members land without the swap. The friction log ranks the *list hole* as the #1 *payoff* — if you
   want that prototyped first (to validate the repeater shape before investing in P1), say so; the
   intermediate is uglier but the headline lands sooner. **Your call.**
2. **`[TemplateHole]` + `abstract`, or `abstract`-as-the-signal?** Recommendation: the **marker** is
   the signal, `abstract` an optional convenience (no leak; quoting a genuine abstract base stays
   possible). Confirm you don't prefer the barer "abstract member ⇒ hole" rule (less to type, but
   overloads `abstract`).
3. **Generalize the repeater beyond the example, or scope to v1?** Recommendation: v1 = statement
   runs + member lists only, with switch-expression arms / comma-lists / multi-collection zips
   *designed-for but deferred* (mirroring the Matching DSL's v1 line). Confirm you don't want
   comma-list (`SeparatedList`) support pulled into v1 — it's the next most-asked shape and would let
   the switches stay switches via a `SeparatedList<SwitchExpressionArmSyntax>` factory parameter
   (though the arm *pattern* hole is still impossible — see §4.1).
4. **Is re-baselining the ObjectReader snapshot acceptable for P2?** P2 changes the *generated shape*
   (switch → if-chain) — unlike `generator-utilities`, the snapshot **will** change. Confirm that is
   fine for the example (it is the correct signal that construction changed), or whether you want P2
   to preserve the exact switch output (which forces the harder `SeparatedList`-arm path of Q3).
5. **Marker spellings / namespace.** `[TemplateHole]` vs `[ReservedMember]`; `[Repeat]` vs reusing
   `[Inline]` on a collection; identifier-hole spelling. All bikeshed, all live in
   `Synto.Templating` (injected `internal`). Defer to plan time, but flag any you feel strongly about.
6. **`Synto.Diagnostics` cacheable support** (the `PendingDiagnostic` stopgap) is explicitly *not* in
   this roadmap (it's the deferred D4 of `generator-utilities`). Should it be sequenced alongside, or
   stay a separate track? Recommendation: separate track — it's orthogonal to the text-free goal.

# Spec: post-quote declaration decorations (`[Identifier]` / `[Visibility]` / `[Sealed]` / `[Implements<T>]` / `[Inherits<T>]`)

> Status: **draft / proposal** (2026-06-28). Drives the ObjectReader "shell parameters"
> friction (`docs/superpowers/notes/2026-06-22-objectreader-synto-friction.md`, finding #3:
> *"template parameters for the emitted shell — name / modifiers / base-list"*). First feature of
> the ObjectReader full-showcase sweep. Builds on the `TemplateScope` ownership boundary
> (`child-templates-design-inflight`) landed earlier the same day.

## 1. Motivation

A `[Template]` quotes a fixed C# shape. But the *shell* of the emitted type — its **name**,
**modifiers**, and **base list** — is not fixed: it varies per call site or it simply can't be
authored on the carrier. So today the ObjectReader generator hand-fixes the quoted result with
raw `SyntaxFactory`:

```csharp
ClassDeclarationSyntax skeleton = Factory.ObjectReaderTemplate(elementType, model.Columns);
var specialized = SyntaxFactory.List(skeleton.Members.Select(m => RenameConstructor(m, reader)));
return skeleton
    .WithIdentifier(SyntaxFactory.Identifier(reader))                       // per-call-site name
    .WithModifiers(file sealed)                                             // file: top-level only
    .WithBaseList(: global::System.Data.IDataReader)                        // carrier can't declare it
    .WithMembers(specialized);                                              // rename ctor too
```

None of these can move onto the carrier as-authored:

- **Name** is per-call-site dynamic (`ObjectReader_Person_0`).
- **`file`** is illegal as the carrier's own modifier at the scope it lives in.
- **Base list `: IDataReader`** can't be authored: the carrier does **not** satisfy `IDataReader`
  at carrier-compile time — the 12 typed getters (`GetBoolean`…`GetString`) are produced by the
  `[Splice] TypedGetters()` member-generator at *factory* time, so declaring the base would be
  12 × CS0535.

These three are the same operation mechanically: **a transformation applied to one declaration's
quoted output at factory-build time**. This spec introduces them as small, orthogonal *decoration
markers* over **one** shared post-quote-hook mechanism — deliberately **not** as options on
`[Template]` (which would pollute a single-concern marker with shell-only knobs).

## 2. The model: `Apply…` extension-method hooks

A decoration's behavior lives in an **extension method called in the generated factory**, not in
the quoter and not executed by Synto. For a decoration attribute `XxxAttribute`, the hook is the
extension method **`ApplyXxxAttribute`** (convention: `"Apply"` + the attribute's *type name*).
Where the quoter today emits `<quote of N>`, a decorated node emits
`<quote of N>.ApplyXxxAttribute(<args>)`, **composing** one fluent call per decoration:

```csharp
quotedReader.ApplyIdentifierAttribute(identifier)
            .ApplyVisibilityAttribute(Access.File)
            .ApplySealedAttribute()
            .ApplyImplementsAttribute(ifaceType)
```

Why this shape (vs. inlining `.WithIdentifier(…)` in the quoter):

- **No Roslyn wall.** The `Apply…` method runs at the *factory's* runtime (the consumer's
  generator execution), which is ordinary code. Synto only *emits the call* — it never executes a
  hook. So a user-defined `[Foo]` + `ApplyFooAttribute` extension works **by construction**.
- **Per-marker logic is a named, directly unit-testable method**, not branches in the generator.
- **`this`-parameter type IS the target contract.** Synto resolves `ApplyXxxAttribute` by name,
  reads its `this` type (e.g. `TypeDeclarationSyntax` for `ApplyImplementsAttribute`); if the
  decorated node is not assignable to it → mechanical SY-D1, not a downstream compile error.
- **Type-preserving (returns the `this` type).** A hook is a `T → T` transform: its return type
  equals its first-parameter type, so decorations **compose** (each call hands back a node of the
  same type for the next). The cleanest form is generic — `static T ApplyXxxAttribute<T>(this T
  node, …) where T : <target-base> → T` — where the `where` is the target gate and `T` preserves
  the concrete type through the chain regardless of decoration order. Synto verifies return == `this`
  (→ SY-D10).

The quoter side is one new channel parallel to `_unquotedReplacements` (node→replacement) and
`_memberSegments` (member-list splice): a `node → ordered list of Apply… calls` map.

Properties:

- **Attachable anywhere.** A hook keys on a *declaration node* — root emitted type, nested type,
  method, or constructor, not only the top-level shell.
- **Extensible by construction, curated in v1.** Because association is the `Apply…` naming
  convention validated by signature lookup, a user's own `[Foo]` + `ApplyFooAttribute` just works.
  v1 ships, documents, and tests only the **five built-in** decorations (KISS); user-defined
  decorations are an open door, not an advertised feature, and get no extra docs/tests this round.
  No lift-and-splice framework, no `IDecorationHook` interface.
- **Constructor-args only.** A decoration attribute declares its inputs as **constructor
  parameters** — no settable properties/fields (so there is never a `[Foo(Bar = "x")]` named-arg to
  map). Enforced by SY-D8.
- **Scoped.** Decoration discovery runs through the `TemplateScope` boundary, so a nested child
  `[Template]`'s decorations belong to the child's own factory and never leak to the parent.
- **Cacheable.** All discovery and symbol resolution happen inside the `GenerateTemplate`
  transform; no `ISymbol`/`SemanticModel`/`SyntaxNode` enters cached pipeline state.

## 3. Consumer surface (the five markers)

Authored once in `src/Synto` under `namespace Synto.Templating`, embedded as a `Synto.Runtime.*`
resource, and injected `public→internal` like every other marker (netstandard2.0; inert at
carrier-compile time). All five are `[AttributeUsage]`-restricted to the declarations they make
sense on.

### 3.1 `[Identifier]` — the emitted name (a hole)

The only marker that introduces a *value* hole; like every other hole (`[Splice]`,
`Parameter<T>()`), it injects a factory parameter. **KISS v1: no-argument form only.**

- **`[Identifier]`** (no argument) — injects one `string` factory parameter; the emitted
  identifier is that value. Applied to a **type**, it also renames every **constructor** of that
  type (a constructor's name must equal the type's by C# rule), removing the hand-written
  `RenameConstructor`. The injected parameter is named `identifier` (uniquified on collision).

The caller computes whatever name it needs and passes the string (the ObjectReader generator passes
`$"ObjectReader_{shortName}_{index}"`). A format-string / reuse / `Inject` form is deferred (§8).

### 3.2 `[Visibility(Access access)]` — the emitted access modifier

`access` is a non-`[Flags]` enum `Access { Public, Internal, Private, Protected, ProtectedInternal,
PrivateProtected, File }`. The hook **replaces the access tokens** of the emitted declaration's
modifier list (leaving non-access modifiers — `sealed`, `static`, `abstract`, `partial`, … —
carried through from the carrier as authored). `Access.File` is valid **only** on a top-level
declaration (see SY-D5).

### 3.3 `[Sealed]` — the emitted `sealed` modifier

Adds `sealed` to the emitted type's modifier list. Its own marker (a distinct axis from access).
Idempotent if the carrier is already `sealed` (carried through).

### 3.4 `[Implements<TInterface>]` (repeatable) / `[Inherits<TBase>]` (single)

Generic attributes (mirrors `[Match<M>]`), **not** `typeof`. The hook resolves the type-argument's
symbol to its fully-qualified name and adds it to the emitted type's base list:
`[Inherits<TBase>]` first (single), then each `[Implements<TInterface>]` in source order. Inert
metadata on the carrier — neither forces the carrier to actually derive/implement the type.

### 3.5 Carry-through rule

Any shell aspect **not** governed by a marker is emitted as authored on the carrier. So a carrier
authored `internal sealed class Foo : IFoo` with no decorations emits exactly that; decorations
override only the axis they name.

## 4. Mechanism (generator internals)

1. **`DecorationFinder`** (`src/Synto.SourceGenerator/Templating/`, a `TemplateScopedWalker`)
   walks the carrier; for each declaration carrying ≥1 decoration attribute it resolves the
   associated hook by convention — `ApplyXxxAttribute` for attribute `XxxAttribute` — looks the
   method up as an extension method visible from the carrier, validates it (see §5: exists,
   `this`-type assignable from the decorated node, return == `this`, arity matches the attribute's
   constructor parameters), and builds the `node → ordered Apply… calls` map plus, for
   `[Identifier]`, the injected `string` parameter. Returns the map, params to inject, diagnostics.

2. **Parameter assembly.** `[Identifier]`-injected `string` params join the factory parameter list
   alongside the other holes (after the existing `[Splice]`/staged/`Parameter<T>()` params).

3. **Argument binding.** Each `Apply…` call's arguments come from the attribute usage: a generic
   type-arg → a `TypeSyntax` expression (FQ-resolved at generation), a constructor arg → its
   constant value rendered as a literal, and a per-call-site hole (`[Identifier]`) → the injected
   factory parameter. (No named-property args exist — SY-D8.)

4. **Quoter channel.** `TemplateSyntaxQuoter` gains `_postQuoteHooks`
   (`IReadOnlyDictionary<SyntaxNode, IReadOnlyList<ExpressionSyntax→ExpressionSyntax>>`). After a
   node's base quote `q` is produced, each hook folds `q = q.ApplyXxxAttribute(<args>)`, composing
   in source order of the decorations (the calls are type-preserving, so order is observable only
   for same-axis conflicts, which SY-D7 forbids).

5. **Trim.** The decoration attribute syntax is added to `trimNodes` so it never appears in quoted
   output (like `[Template]`/`[Splice]`).

6. **Built-in `Apply…` helpers** (`ApplyIdentifierAttribute`, `ApplyVisibilityAttribute`,
   `ApplySealedAttribute`, `ApplyImplementsAttribute`, `ApplyInheritsAttribute`) ship as
   **emitted file-local helpers** — like `ToSyntax`/`ToTypeSyntax`, auto-injected into a factory
   file when that factory references them (the existing `FindReferencedHelpers` scan). A
   user-defined decoration supplies its own `ApplyFooAttribute` extension in scope instead; the
   resolution and validation path is identical.

## 5. Diagnostics (all mechanical — symbol/syntax facts only)

| ID (proposed) | Trigger |
|---|---|
| SY-D1 | `[Identifier]`/`[Visibility]`/`[Sealed]`/`[Implements]`/`[Inherits]` on a declaration kind it does not support (e.g. `[Implements<T>]` on a method; `[Identifier]` on a node with no renameable identifier). |
| SY-D5 | `[Visibility(Access.File)]` on a non-top-level declaration (`file` is top-level only). |
| SY-D6 | `[Implements<T>]` where `T` is not an interface; `[Inherits<T>]` where `T` is not a non-sealed class. |
| SY-D7 | Conflicting/duplicate decorations on one node: >1 `[Visibility]`, >1 `[Inherits]`, `[Sealed]` on a non-type. |
| SY-D8 | A decoration attribute declares a settable property/field (must use constructor parameters only — no `[Foo(Bar = "x")]` to map). Reported on the attribute *usage* (the carrier author's signal), keyed off the attribute type. |
| SY-D9 | No `ApplyXxxAttribute` extension method visible from the carrier for decoration `[Xxx]`, or its constructor-arg arity doesn't match the attribute's constructor parameters. |
| SY-D10 | `ApplyXxxAttribute`'s return type does not equal its `this`-parameter type (a hook must be type-preserving so decorations compose). |

Each diagnostic carries the offending decoration's location and bails that template (returns no
factory) rather than emit a mis-shaped type — consistent with Synto's "never silently mis-expand."
Descriptor IDs finalized during implementation against the existing `SY1xxx` range.

## 6. ObjectReader after

Carrier (`ReaderTemplate.cs`):

```csharp
[Template(typeof(Factory))]
[Identifier]
[Visibility(Access.File)]
[Sealed]
[Implements<global::System.Data.IDataReader>]
internal class ObjectReaderTemplate<[Splice] T>
{
    public ObjectReaderTemplate(global::System.Collections.Generic.IEnumerable<T> source) => …;
    …
    [Template(typeof(Factory))]
    public TRet TypedGetter<[Splice] TRet>(int i) { … }   // unchanged; getter renames stay as today
}
```

Generator (`ObjectReaderGenerator.BuildReader`) collapses to:

```csharp
return Factory.ObjectReaderTemplate(
    elementType, model.Columns, $"ObjectReader_{model.TargetTypeShortName}_{index}");
```

`RenameConstructor` and the three `.WithIdentifier/.WithModifiers/.WithBaseList` fixups on the
reader shell are deleted. The `TypedGetters()` member-generator is **unchanged** — its 12
`.WithIdentifier(Identifier("GetXxx"))` calls stay (collapsing those needs the deferred format form
of `[Identifier]`).

**Behavior-preserving:** the generated reader stays *semantically the same* — only the *authoring*
moves from generator C# into carrier markers. Byte-for-byte identity is **not** required: the
emitted shape may differ in benign ways (modifier/base-list token order, trivia, an injected
`Apply…` helper) as long as it still compiles and `ObjectReader.Tests` stays green. A changed
snapshot is fine when the diff is reviewed and benign — re-accept it; only an unreviewed
behavioral change is a red flag.

## 7. Testing

- Per-marker round-trip unit tests in `Synto.Test/Templating/`: a carrier with each decoration →
  assert the factory output's name / access / `sealed` / base-list, and that the emitted
  `Apply…` file-local helper is injected.
- One negative case per diagnostic SY-D1 / SY-D5 / SY-D6 / SY-D7 / SY-D8 / SY-D9 / SY-D10.
- Composed-on-one-node test (all applicable markers chained on one type).
- "decoration on a nested child template does not leak to the parent factory" (exercises
  `TemplateScope`).
- Extensibility smoke test: a *user-defined* `[Foo]` + `ApplyFooAttribute` extension flows through
  the same path (proves the door is open — the one test we keep despite not advertising it).
- Cacheability: a staged decoration template stays `Cached/Unchanged` on an unrelated edit.
- ObjectReader **semantically equivalent** (compiles + `ObjectReader.Tests` 10/10 green); the
  snapshot may be re-accepted if its diff is reviewed and benign (token order / trivia / an injected
  `Apply…` helper) — it need not be byte-identical. Gates: `Synto.Test`, ObjectReader 10/10,
  Diagnostics 8/8, `dotnet format whitespace` clean.

## 8. Out of scope (v1 — KISS)

- **The pluggable hook framework** (`IDecorationHook` + lift-and-splice + user-defined decoration
  attributes). Explored and deferred: v1 hard-wires the five built-in markers over one quoter
  channel. The lift-the-hook-syntax-as-a-bespoke-template idea is recorded for a future extensibility
  pass, but KISS for now.
- **`[Identifier]` format / reuse / `Inject` form** (`[Identifier("Get{name}")]`). v1 is the
  no-argument form only; the caller computes the string. Revisit when collapsing the typed-getters'
  `.WithIdentifier` calls is worth it.
- Type-hole composition in `[Implements<T>]`/`[Inherits<T>]` (a `[Splice]`-staged base type).
- Modifiers beyond access + `sealed` as markers (`static`/`abstract`/`partial` carry through; add
  markers only when a real need appears).

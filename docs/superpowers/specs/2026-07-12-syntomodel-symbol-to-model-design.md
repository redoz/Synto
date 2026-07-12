# `[SyntoModel]` — symbol-to-model generator

**Status:** design (approved for planning)
**Date:** 2026-07-12
**Category:** Synto authoring surface (input side)

## Problem

Synto is an output-side toolkit: it quotes example code, templates holes, splices live data, and
polices the cacheability of its *own* emitted steps. But every incremental generator has a *front
half* Synto gives no help with: turning an `ISymbol` (plus its attributes/members) into a
**value-equatable data model** that the incremental pipeline can cache.

Hand-authoring that model is the single most common footgun in generator authoring:

- The model must be structurally equatable. Get `IEquatable`/`GetHashCode` wrong and the pipeline
  cache never hits, so the generator re-runs on every keystroke.
- The model must **never** hold an `ISymbol`, `Compilation`, `Location`, or any Roslyn object with
  reference identity — doing so silently defeats incrementality (and roots the whole compilation in
  memory).
- Collections must be wrapped (`EquatableArray<T>`) or equality degrades to reference equality.

Synto already ships the output-side symmetric primitive: the `[Runtime]` / `ToSyntax(this T)`
converter registry (diagnostic SY1008) that turns a runtime value into `ExpressionSyntax`. This
feature is the mirror image — **symbol → value** — reusing the same "inert authored surface, rewritten
by the generator" idiom that the entire Synto template surface is built on (`Template.cs`).

## Authoring surface

You write a **spec class** whose expression-bodied getters are pure projections over the source
symbol. Synto reads it and emits a sibling cacheable `record` plus a `From` factory.

```csharp
[SyntoModel]
class TypeModelShape : SyntoModel<INamedTypeSymbol>
{
    // Each instance expression-getter = one projected field.
    // "How much of the symbol" is just what you type — no options, no DSL.
    string        Namespace => Symbol.ContainingNamespace.ToDisplayString();
    string        Name      => Symbol.Name;
    Accessibility Access    => Symbol.DeclaredAccessibility;   // enum: already equatable

    // Nested model — composes via the Project shim (no forward reference in your code).
    EquatableArray<PropModelShape> Properties =>
        Project.Each<PropModelShape>(Symbol.GetMembers().OfType<IPropertySymbol>());

    // Escape hatch is just a method call. Zero new API.
    string DocId => DocumentationIds.Create(Symbol);
}

[SyntoModel]
class PropModelShape : SyntoModel<IPropertySymbol>
{
    string Name     => Symbol.Name;
    string TypeName => Symbol.Type.ToDisplayString();
    bool   Required => Symbol.IsRequired;
}
```

### Surface elements

- **`SyntoModel<T>`** — an injected, inert base (via `SurfaceInjectionGenerator`, like `Template`).
  Exposes `protected T Symbol => default!;` so getters type-check at author time. Never executed:
  the generator discards the spec class body and re-hosts each getter's RHS in the generated `From`.

- **`[SyntoModel]`** — marks a spec class. The generated record's name defaults to the authored name
  minus a trailing `Shape` suffix; override with `[SyntoModel(Name = "TypeModel")]`. (If the authored
  name has no `Shape` suffix and no explicit `Name`, that is an error — see diagnostics.)

- **Instance expression-getters** — one per projected field. The getter's declared type is the record
  field's type; its name is the field's name; its RHS is the projection. Only expression-bodied
  (`=>`) instance getters participate. Static members, fields, methods, and block-bodied members are
  ignored (available for local helpers).

- **`Project.Each<TModel>(IEnumerable<TSymbol>)` / `Project.One<TModel>(TSymbol)`** — inert shims
  (`=> default!`) for composing nested models, mirroring `Template.Unquote`. The generator recognizes
  them by bound symbol and rewrites:
  - `Project.Each<PropModelShape>(seq)` → `seq.Select(PropModel.From).ToEquatableArray()`
  - `Project.One<PropModelShape>(sym)` → `PropModel.From(sym)` (nullable-aware for optional symbols)

  Using the shim keeps the *authored* code free of a forward reference to the generated `From`; the
  generated `From` may reference a sibling `From` because the carrier-binding invariant (consumer code
  binding generated members in the same compilation) already holds across the Synto codebase.

### Generated output (sibling)

```csharp
// generated beside TypeModelShape
record TypeModel(
    string Namespace,
    string Name,
    Accessibility Access,
    EquatableArray<PropModel> Properties,
    string DocId)
{
    public static TypeModel From(INamedTypeSymbol symbol) => new(
        Namespace:  symbol.ContainingNamespace.ToDisplayString(),
        Name:       symbol.Name,
        Access:     symbol.DeclaredAccessibility,
        Properties: symbol.GetMembers().OfType<IPropertySymbol>()
                          .Select(PropModel.From).ToEquatableArray(),
        DocId:      DocumentationIds.Create(symbol));
}
```

The record's structural equality plus `EquatableArray`'s value equality make the model cache-safe **by
construction** — the consumer cannot get equality wrong because they never write it.

The spec class (`TypeModelShape`) remains in the compilation, inert and unused at runtime, exactly as a
`[Template]` method coexists with its generated factory. Consumers reference only the generated record.

### Pipeline wiring is the consumer's job

This feature owns `symbol → model`. It deliberately does **not** generate the
`ForAttributeWithMetadataName` registration, predicate, or `RegisterSourceOutput` wiring (that was the
rejected "full pipeline" scope). The consumer writes their pipeline and calls `TypeModel.From(symbol)`
at the transform step. This keeps the feature small, unopinionated, and composable with any generator
structure.

## Cache-safety — mechanical diagnostics

All checks are symbol/syntax facts only (no LLM/heuristic judgement), matching Synto's existing
generator-diagnostic discipline. New IDs continue the `SY10xx` sequence (final numbers assigned at
implementation time).

- **Non-equatable field type** — a getter whose declared type is not one of: a C# primitive, `string`,
  an `enum`, `EquatableArray<…>`, or another `[SyntoModel]` record. Message names the offending type
  and, when the type is `ISymbol`/`Compilation`/`Location`-derived, adds "would leak a symbol into the
  cached model; project it to a value (e.g. `.Name`, `.ToDisplayString()`)."
- **Symbol leak in RHS** — the getter's *result* is symbol-typed even if the declared field type is
  (say) `object`. Caught by the same equatable-result check on the lifted expression.
- **`Project.Each/One` element type not a `[SyntoModel]`** — the type argument must resolve to a spec
  class in the same compilation.
- **Name resolution** — spec class name lacks a `Shape` suffix and no `[SyntoModel(Name=…)]` override.

Diagnostics accumulate and continue (drop the offending getter, keep emitting the rest), matching the
`ValidateTemplate` / `DecorationFinder` pattern.

## Delivery machinery (reuse map)

| Concern | Existing mechanism reused |
| --- | --- |
| Inject `SyntoModel<T>`, `Project`, consumer-facing `EquatableArray` | `SurfaceInjectionGenerator` |
| Self-compiling inert surface recognized by binding | `Template` idiom (`Template.cs`) |
| Lift getter RHS into `From`, rebind `Symbol` → parameter | `StagedRootLifter` / `ValueLift` |
| Consumer/sibling code binding generated `From` in same compilation | carrier-binding invariant |
| Accumulate-and-continue diagnostics | `ValidateTemplate` / `DecorationFinder` |

`EquatableArray<T>` exists today in `Synto.Diagnostics` and `Synto.Generators.Incremental`; a
consumer-facing copy is injected into the consumer compilation (or the `Synto.Diagnostics` one is made
consumer-visible). `.ToEquatableArray()` is added as an injected extension.

### Reuse discipline

Each reused mechanism above was built for the template/output path. Where the model path needs
something the current shape doesn't cleanly provide (e.g. `StagedRootLifter` assuming a factory target,
or `SurfaceInjectionGenerator` assuming template-only surface), the plan **refactors the shared piece so
it genuinely serves both use-cases** — extracting the common seam behind a fitting abstraction — rather
than threading a model-specific special case through template code or forcing the model path into a
template-shaped hole. A reuse that requires a flag like `isModel` branching through the lifter is a
signal the seam is wrong and needs factoring, not a parameter. This keeps both callers legible and
avoids coupling the two features through incidental shared internals.

## Phasing

- **Phase 1 — the generator.** Spec class → sibling record + `From`; `SyntoModel<T>` / `Project` /
  `EquatableArray` surface injection; cache-safety diagnostics; `Project.Each/One` rewrite. Dog-food
  against the ObjectReader example's own symbol-extraction if one exists, else a focused test model.
- **Phase 2 — the analyzer completion provider.** With the cursor in a spec class body, offer
  "add `string Name => Symbol.Name;`"-style completions for members of `T` that have a known equatable
  projection (name/display-string/enum/bool/nested-model). Built on the SY1035 live-highlight analyzer
  precedent. This is the delight feature: the editor teaches the surface.

## Non-goals

- Generating the incremental pipeline registration (FAWMN, predicates, `RegisterSourceOutput`).
- A binding DSL or per-field option vocabulary — dissolved by writing projections directly.
- Symbol-shape *inference* (Synto never guesses which facts you want; you type them).
- Round-tripping model → symbol (models are one-way projections).

## Open questions for the plan

1. Exact `SY10xx` diagnostic IDs and category (`Synto.Usage`).
2. Whether the consumer-facing `EquatableArray` is a new injected type or the existing
   `Synto.Diagnostics` one promoted to consumer visibility.
3. `Project.One` semantics for a `null` source symbol (emit `null` field vs. diagnostic).
4. Whether `[SyntoModel]` records should be `sealed` and/or carry `[Synto]`-generated `ToString`
   suppression to keep output tidy.

# Cacheable diagnostics for consumers

**Status:** design (approved for planning)
**Date:** 2026-07-12
**Category:** Synto authoring surface (diagnostics)

## Problem

Synto already ships `Synto.Diagnostics` — the `[Diagnostic]` generator that turns a partial method
into a `DiagnosticDescriptor` field plus a typed factory:

```csharp
[Diagnostic("MYG001", "Title", "Message {0}", "MyGen.Usage", DiagnosticSeverity.Error, true)]
public static partial Diagnostic MyDiag(Location location, string arg0);
```

But the generated factory returns a **live `Diagnostic`**. A `Diagnostic` holds a `Location`, which
roots the `SyntaxTree`/`Compilation`; carry one through an incremental pipeline and caching is
defeated. Tellingly, Synto's *own* generators never use `[Diagnostic]` — they hand-write the cacheable
trio `DiagnosticInfo` / `LocationInfo` / `EquatableArray<string>` (see `DiagnosticInfo.cs`,
`TemplateDiagnostics.cs`). That trio is `internal`, so a consumer who wants to collect diagnostics the
cache-safe way re-writes the same boilerplate by hand — the exact footgun the `[SyntoModel]` feature
kills for data models.

This feature makes the cache-safe path a first-class consumer surface, and is the natural twin of
`[SyntoModel]`: a model's extraction (`From(symbol)`) can collect diagnostics that ride through the
cache alongside the model and are materialized to live `Diagnostic`s only at the source-output stage.

## Design

### Return-type-directed `[Diagnostic]`

`[Diagnostic]` gains a cacheable form selected by the factory's **declared return type** — no new
attribute, fully backward compatible:

- return `Diagnostic` → today's behavior (live factory), unchanged.
- return `DiagnosticInfo` → cacheable factory.

```csharp
[Diagnostic("MYG001", "Unsupported property type",
            "Property '{0}' has unsupported type '{1}'",
            "MyGen.Usage", DiagnosticSeverity.Warning, true)]
public static partial DiagnosticInfo UnsupportedProp(LocationInfo? location, string name, string type);
```

Generated:

```csharp
static readonly DiagnosticDescriptor _UnsupportedProp = new("MYG001", /*…*/);
public static partial DiagnosticInfo UnsupportedProp(LocationInfo? location, string name, string type)
    => new(_UnsupportedProp, location, new EquatableArray<string>(ImmutableArray.Create(name, type)));
```

Parameter mapping mirrors the existing generator: a `LocationInfo?` parameter fills the location slot,
a `DiagnosticSeverity` parameter fills severity, everything else is a message argument (in order).
(The live form keeps matching `Location`; the cacheable form matches `LocationInfo?`.)

### Shipped consumer surface

The cacheable trio, currently `internal` to `Synto.Diagnostics`, is made a public consumer surface
(injected into the consumer compilation, same mechanism as the `[SyntoModel]` surface — see the reuse
discipline note):

- **`DiagnosticInfo`** — value-equatable `(DiagnosticDescriptor, LocationInfo?, EquatableArray<string>)`
  with a public `ToDiagnostic()` that materializes the live `Diagnostic` (the only place a `Location`
  is created).
- **`LocationInfo`** — value-equatable location, with provenance helpers:
  - `LocationInfo.From(ISymbol)` — first declaring-syntax location, cache-safe.
  - `LocationInfo.From(SyntaxNode)` — the node's location.
  - `LocationInfo.CreateFrom(Location?)` — existing, kept.
- **`EquatableArray<T>`** — the consumer-facing copy (shared with `[SyntoModel]`; a single injected
  type, not one per feature).

### Collect + drain helper

A small `DiagnosticBag` collector plus a `Drain` extension remove the collect/report boilerplate and
give the "carry diagnostics as one value" ergonomic without touching the model:

```csharp
// during extraction
var bag = new DiagnosticBag();
foreach (var p in symbol.GetMembers().OfType<IPropertySymbol>())
    if (!IsSupported(p.Type))
        bag.Report(Diagnostics.UnsupportedProp(LocationInfo.From(p), p.Name, p.Type.ToDisplayString()));

return new ModelResult(TypeModel.From(symbol), bag.ToEquatableArray());
```

```csharp
// at source output — the only place live Locations are materialized
context.RegisterSourceOutput(results, static (spc, r) =>
{
    spc.Drain(r.Diagnostics);                 // reports every DiagnosticInfo via ToDiagnostic()
    if (r.Model is { } m) spc.AddSource(/*…*/);
});
```

`DiagnosticBag` is a thin, value-equatable wrapper over `ImmutableArray<DiagnosticInfo>.Builder` that
yields an `EquatableArray<DiagnosticInfo>` (so a bag or its drained array can itself sit in a cached
pipeline value). `Drain` is `SourceProductionContext.Drain(EquatableArray<DiagnosticInfo>)` — and an
overload taking `DiagnosticBag` — that reports each entry. No `IncrementalGenerator`-shaped state; pure
data in, `ReportDiagnostic` out.

## Reuse discipline

Making the `Synto.Diagnostics` trio public must not fork it: today those types are duplicated per
generator project (`Synto.Diagnostics`, `Synto.Generators.Incremental` each carry their own
`EquatableArray`/`DiagnosticInfo`). The plan **consolidates onto one shipped definition** injected into
consumers, and has Synto's own generators consume that same seam where practical, rather than adding a
second public copy beside the internal ones. If the internal callers need something the public shape
doesn't provide, refactor the shared type to serve both — do not shoehorn a consumer-only variant next
to an internal one (this is the same reuse discipline the `[SyntoModel]` spec sets).

The `return-type-directed` branch reuses the existing `ProcessTarget` parameter-classification loop in
`DiagnosticsGenerator.cs`; the cacheable branch differs only in the emitted body shape (construct
`DiagnosticInfo` vs. call `Diagnostic.Create`). Extract the body-emission behind a small seam so the
two return shapes are siblings, not an `if (isCacheable)` thread through one method.

## Relationship to `[SyntoModel]`

Decoupled by design. A `SyntoModel` getter is a pure projection and cannot conditionally report, so
diagnostics remain the transform's concern (where the `ISymbol` and its `Location`s are in hand). The
model and the diagnostic bag ride together as a plain result tuple, exactly as mocked. The rejected
"model carries a `Diagnostics` field" sugar (option 3) is a **non-goal**: populating it still requires
the transform, which would either thread diagnostics into `From` (breaking the pure-projection surface)
or add a mutable seam to an immutable model — muddying `[SyntoModel]` to save one tuple.

## Non-goals

- A model-carries-diagnostics field on `[SyntoModel]` records (rejected option 3).
- Analyzer/live reporting of these diagnostics (this feature is generator-side reporting only).
- Changing the live `Diagnostic`-returning form's behavior (kept for back-compat).
- `additionalLocations` / `properties` bag support beyond what `DiagnosticInfo` already models (can be
  a later extension; out of scope here).

## Open questions for the plan

1. Whether the public trio lives in a new consumer-facing namespace (e.g. `Synto.Diagnostics`) injected
   via post-init, or is promoted from the existing internal types with visibility widened.
2. `DiagnosticBag` mutability/threading: builder-backed value vs. immutable-with-`Add`-returning-new.
   (Builder-backed is simpler for the transform; confirm it never needs to sit *inside* another cached
   value mid-build.)
3. Exact `LocationInfo.From(ISymbol)` semantics for multi-declaration symbols (first vs. all locations).
4. Whether `Drain` should de-duplicate identical `DiagnosticInfo`s (same descriptor + location + args).

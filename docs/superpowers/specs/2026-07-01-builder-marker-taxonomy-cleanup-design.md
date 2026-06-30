# Builder marker-taxonomy cleanup — design

**Date:** 2026-07-01
**Status:** Design approved; plan authored (`docs/superpowers/plans/2026-07-01-builder-marker-taxonomy-cleanup.md`). `[TypeArg].Constraint` (generic base-type constraint) folded in per user decision 2026-07-01.
**Scope:** The `[SyntaxBuilder]` **parameter marker** surface only — `[Quoted]`, `[ReturnType]`,
and the doomed `QuotedAttribute.AsTypeArg`. The other 13 Synto attributes (template control,
decorations, matching) are unrelated subsystems and are out of scope.

This is a **prerequisite cleanup**. It lands **before** the switch-builders work (a separate,
later plan) so that work builds on a clean marker model. See *Relationship to switch-builders*.

## Context

Synto's `[SyntaxBuilder]` mechanism **reverse-engineers a facade from the builder
implementation**: `FacadeShape.Derive` reads the impl's parameter markers and projects an inert
facade the consumer calls. The impl is the thing you write by hand; the facade is a projection
of it. That projection is the design's load-bearing principle.

One marker breaks that principle. `[Quoted(AsTypeArg = true)]` is a **bool bolted onto
`[Quoted]`** that silently reinterprets a `TypeSyntax` *value parameter* as a *generic type
parameter* on the facade — a parameter that exists nowhere in the impl signature. Concretely
(the only usage in the whole repo, the test-only `Cast` builder):

```csharp
// impl (NOT generic):
static ExpressionSyntax Cast([Quoted(AsTypeArg = true), ReturnType] TypeSyntax t, [Quoted] ExpressionSyntax x)
    => CastExpression(t, x);
// synthesized facade (generic):  T Cast<T>(object x)
// consumer writes:               Cast<int>(x)
```

This encoding creates illegal states that need *runtime* validation to police:

- `[ReturnType]` is only meaningful when `AsTypeArg = true`, enforced by a SY1015 check.
- `As` is documented as "ignored when `AsTypeArg` is true" — two mutually-exclusive properties
  on one attribute.

Meanwhile, `[Template]` methods already express staged/quoted **types** the honest way — as
real generic type parameters (`Build<[Splice] T>`), lifted by `StagedTypeParameterFinder`. The
builder-facade path is the odd one out.

## Goal

Replace the `AsTypeArg` encoding with a dedicated, single-purpose `[TypeArg]` attribute, making
the builder-param marker taxonomy a faithful, minimal projection of the impl. The change is
**behavior-preserving**: the generic materialization (`Cast<int>(x)`) is unchanged; this is a
surface/encoding cleanup, not a capability change.

## The complete builder-param marker taxonomy (target)

A `[SyntaxBuilder]` parameter falls into exactly these kinds. Two underlying axes: **how the
argument is captured** (evaluate vs quote-syntax) and, when it is a quoted *type*, the type is
surfaced to the consumer as a generic type argument.

| Marker | Consumer writes | Builder body receives | Facade parameter | Produces |
|---|---|---|---|---|
| *(unmarked)* | an expression/value evaluated at build time | the value verbatim (incl. pre-built nodes) | same type | staged value |
| `[Quoted]` | any expression | quoted `ExpressionSyntax` | `object` (or `As`) | expression island |
| `[TypeArg]` | `Foo<int>(…)` | quoted `TypeSyntax` | generic `<T>` (opt. `where T : Constraint`) | type island |
| `[ReturnType]` | — | — | makes facade return = that `<T>` | (modifier) |

**Attribute properties**
- `QuotedAttribute.As` (`Type?`, default `null`) — narrows the synthesized facade value-param
  type from `object`. Orthogonal; **kept** (see *Open / to-confirm*).
- `TypeArgAttribute.Name` (`string?`, default `null`) — **new.** Names the synthesized facade
  type parameter; default is `Pascal(paramName)`. Decouples the facade type-param name from the
  impl parameter name (fixes the `t` → `T` ugliness — name the impl param readably, e.g.
  `targetType`, and set `Name = "TResult"`).
- `TypeArgAttribute.Constraint` (`Type?`, default `null`) — **new.** When set, constrains the
  synthesized facade type parameter to this base class or interface, emitting a `where T : Constraint`
  clause so the consumer's supplied type argument must satisfy it. Default is an unconstrained `<T>`.
  A single base/interface type for now (not `class`/`struct`/`new()` or multiple constraints).

### Two design rules that fix the wart

1. **`[TypeArg]` is a *sibling* of `[Quoted]`, not a modifier of it.** Both mean "capture the
   consumer's input as syntax"; they differ only in *what* is captured — an expression
   (`ExpressionSyntax`) vs a type argument (`TypeSyntax`). A builder type-arg is **inherently
   quoted** (there is no "staged type-arg"; the only other way a `TypeSyntax` reaches a builder
   is an *unmarked* staged value parameter). So `[TypeArg]` **subsumes** the quoting — you never
   write `[Quoted, TypeArg]`, and an orphan `[TypeArg]` (no `[Quoted]`) is the normal form. This
   is precisely the decoupling that the `AsTypeArg`-bool-on-`[Quoted]` lacked.

2. **`[ReturnType]` requires `[TypeArg]` (decision: "Option X").** A facade return type must be
   **statically known at the consumer's call site** (`Cast<int>(x)` type-checks as `int`). Only
   a `[TypeArg]` param provides that — it surfaces as a generic `<T>`. An unmarked (staged)
   `TypeSyntax` param is a *runtime value*; there is no static type for `[ReturnType]` to
   designate, so `[ReturnType]` is meaningless there. `[TypeArg]` is therefore the **single
   home** for both "surface as generic" and `Name`; `[ReturnType]` stays a one-job modifier.
   The return type-arg is written `[TypeArg(Name = "TResult"), ReturnType] TypeSyntax t`.

   *(Rejected alternative "Option Y": `[ReturnType]` implies `[TypeArg]` and stands alone. More
   concise for the common single-type-arg case, but it fragments the `Name` property and the
   "surface-as-generic" trigger across two attributes — the same single-purpose erosion that
   `AsTypeArg` represented.)*

## Changes vs. current

- **Delete** `QuotedAttribute.AsTypeArg`.
- **Add** `TypeArgAttribute` (`[AttributeUsage(AttributeTargets.Parameter)]`) with two properties,
  `string? Name` and `Type? Constraint`.
- **`FacadeShape.Derive`**: discriminate the type-island by `[TypeArg]` presence instead of the
  `AsTypeArg` bool; the facade generic name becomes `Name ?? Pascal(paramName)`.
- **`FacadeShape` generic-param model + `SyntaxBuilderFacadeGenerator`**: the facade's generic
  parameters carry an optional base-type constraint (from `Constraint`), and the emitter appends a
  `where T : Constraint` clause. Unconstrained builders emit byte-identical facades as today.
- **`[ReturnType]`** validation: "requires `[TypeArg]`" (was "requires
  `[Quoted(AsTypeArg=true)]`"); the SY1015 diagnostic message updates accordingly.
  `ReturnTypeAttribute` stays a pure marker (doc-comment refreshed).
- **Keep** `[Quoted].As` — orthogonal to all of this.
- **Internals unchanged otherwise**: the `QuotedTypeArg` `BuilderArgKind`, the facade `<T>`
  emission in `SyntaxBuilderFacadeGenerator`, the type-arg quoting branch in
  `TemplateFactoryBuilder`, and `FacadeArgumentBinder`'s type-arg binding all stay — **only the
  trigger marker changes**.

## Blast radius

- `src/Synto/Templating/Builders/QuotedAttribute.cs` — remove `AsTypeArg`.
- `src/Synto/Templating/Builders/TypeArgAttribute.cs` — **new** file.
- `src/Synto/Templating/Builders/ReturnTypeAttribute.cs` — doc-comment tweak only.
- `src/Synto.SourceGenerator/Templating/Builders/FacadeShape.cs` — trigger + `Name`.
- `src/Synto.SourceGenerator/Templating/Diagnostics/TemplateDiagnostics.cs` — SY1015 message.
- `src/Synto.SourceGenerator/SurfaceInjectionGenerator.cs` (+ csproj embedded resources) — inject
  `TypeArgAttribute`.
- Snapshots: `…#QuotedAttribute.g.verified.cs` (loses `AsTypeArg`), **new**
  `…#TypeArgAttribute.g.verified.cs`, `…#ReturnTypeAttribute.g.verified.cs` (doc).
- Tests (`test/Synto.Test/Templating/SyntaxBuilderTest.cs`): migrate `Cast` to
  `[TypeArg, ReturnType]`; the SY1015 negative test becomes "`[ReturnType]` without `[TypeArg]`";
  add a `Name` test (facade type-param named via `[TypeArg(Name=…)]`).

## Verification (behavior preservation)

- `Cast<int>(x)` consumer call site is unchanged; the synthesized facade is still
  `T Cast<T>(object x)` (or a `Name`d type param).
- All existing `[SyntaxBuilder]`/facade tests stay green; only `Cast` migrates.
- Incremental cacheability guards stay green — no `ISymbol`/`SemanticModel` enters pipeline
  *state*; marker reading happens inside the `ForAttributeWithMetadataName` transform (nodes in,
  nodes out), exactly as today.
- `dotnet format whitespace --verify-no-changes` clean; snapshots re-accepted only where the
  injected surface intentionally changes (`QuotedAttribute` shrinks, `TypeArgAttribute` added).

## Deferred (out of scope; recorded as the future direction)

The earlier brainstorm surfaced a second materialization of a type-island: surfacing it as a
`System.Type` **value** parameter (`Cast(typeof(int), x)`) rather than a generic `<T>`, and the
data-driven form where the type comes from a per-item runtime `System.Type` value
(`cols.Select(c => …c.ClrType…)`, lifted via the existing `Type.ToTypeSyntax()`). The generic
`<T>` form only works for *literal* types in the template body; the `typeof`/`Type`-value form
also composes inside a live projection — which is exactly what **switch type/property patterns**
will eventually need. These become **materialization options of `[TypeArg]`**, designed against
real usage when type-patterns are built. **No attribute is introduced for them now.**

## Relationship to switch-builders

This cleanup exists to unblock the switch-builders feature (data-driven `switch`
statement/expression as runtime built-in builders), which was re-scoped onto a **symbol-binding
recognition model**: built-in builders recognized by binding to the `Template` surface (like
`Member`/`TypeOf`), with a generalized `TryBuiltIn` that reads arg-shape from `[Quoted]`/staged
markers instead of hardcoding a case per builder. Switch v1 needs no type-islands, but it reads
the same builder-param marker model this cleanup defines, so the clean taxonomy is its
foundation. The switch-builders design doc
(`docs/superpowers/specs/2026-06-28-switch-builders-design.md`) still describes the **old
by-name** recognition model and must be corrected separately when that work is picked up.

## Decisions / rejected

- **`AsTypeArg` bool — rejected.** Creates illegal states (mutually-exclusive props, `[ReturnType]`
  precondition) and breaks the facade-is-a-projection-of-the-impl principle.
- **Replacement name `[TypeArg]`** — chosen over `[QuotedType]` and `[TypeParam]`; it names what
  the *consumer supplies* (a type argument), which is the surface that matters.
- **`[TypeArg]` as a sibling of `[Quoted]`** (not co-occurring `[Quoted, TypeArg]`) — chosen; a
  type-arg is inherently quoted.
- **`[ReturnType]` requires `[TypeArg]` (Option X)** — chosen over Option Y (imply `[TypeArg]`);
  single home for surfacing + `Name`, single-purpose `[ReturnType]`.
- **Generic-only materialization for now**; `typeof`/`Type`-value and data-driven deferred.
- **`Name` added to `[TypeArg]`** — decouples facade type-param name from impl param name.
- **`Constraint` added to `[TypeArg]`** — a distinct `Type?` property (never a reused `As`), emitting
  `where T : Constraint`. A single base/interface constraint for now; `class`/`struct`/`new()` and
  multiple constraints are future work if a real caller needs them.

## Open / to-confirm (non-blocking)

- **`As` is kept** (recommended): it is harmless and orthogonal, and keeping it preserves
  behavior. It currently has **zero production usage**, so it remains lightly open — if the user
  prefers, it can be dropped as YAGNI in the same plan.

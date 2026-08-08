# `[Unquote]` value-params lift on splice — one meaning across both spellings

**Date:** 2026-07-05
**Status:** Design ruling (approved in brainstorming)
**Branch:** `experimental/carrier-scoped-live-root`
**Relates:**
- `2026-07-05-authoring-surface-taxonomy.md` — the `Unquote` = one-role-many-spellings taxonomy this makes true in behavior.
- `2026-07-05-carrier-scoped-live-root-design.md` — the effort this unblocks; its Task 2 (TypedGetter → `[Unquote]` params) tripped SY1008 on this exact gap, and its §2.1 primary-ctor `[Unquote]` param spelling depends on this fix.

## 1. Problem

`Unquote` is supposed to mean **one** thing: a caller-supplied live value that may drive factory-time
control flow (loops / filters) *and/or* be spliced into produced syntax. The value is lifted to
output syntax **only where it is actually spliced**. Where it merely drives build-time control, no
lift happens and no value→syntax converter is required.

The **in-body** spellings honor this. `var columns = Unquote<T>();` (and, via Task 1, the carrier
field `private readonly T columns = Unquote<T>();`) lift **only depth-zero references** — references
not consumed by a live control region, a `[Splice]` generator, a builder-staged spine, or a scalar
fold. A control-only `Unquote<T>()` of a converterless type (e.g. `EquatableArray<ColumnInfo>`)
compiles cleanly: it drives `foreach` unrolling and never asks for a converter.

The **param** spelling does not. `[Unquote] T x` **eager-lifts**: `LiftStagedRootParameters`
unconditionally calls `ValueLift.TryEmitValueLift` for every marked parameter, regardless of whether
any reference is actually spliced. A converterless, control-only `[Unquote] EquatableArray<ColumnInfo> columns`
therefore throws **SY1008 (no value→syntax converter)** — even though the value is never spliced.

The two spellings mean different things. That is the bug. It surfaced concretely: the
carrier-scoped-live-root plan's Task 2 tried to convert `TypedGetter`'s body locals to `[Unquote]`
parameters and hit SY1008, and the carrier spec's §2.1 primary-ctor `[Unquote]` param spelling is
blocked for the same reason.

## 2. Fix — lift on splice for `[Unquote]` value-params

Teach the param path the depth-zero analysis the in-body path already performs. A `[Unquote]`
value-parameter lifts **only** its depth-zero (spliced) references; when every reference is
control-consumed, it skips the lift and the converter requirement entirely.

### 2.1 Semantics (target behavior)

For a `[Unquote] T x` parameter, let `depthZero(x)` = references minus
(`RegionConsumedNodes` ∪ `SpliceGeneratorNodes` ∪ `BuilderStagedConsumedNodes` ∪ `FoldClaimedReferences`) —
the identical partition the in-body path computes.

| Case | `depthZero(x)` | Behavior |
|---|---|---|
| **Spliced** (≥1 depth-zero ref) | non-empty | Lift via `ValueLift.TryEmitValueLift`. Converter still required for a concrete non-built-in `T` → **SY1008 when genuinely missing** (unchanged), **SY1009** when ambiguous (unchanged). |
| **Control-only** (0 depth-zero refs) | empty | **No lift, no converter, no preamble local.** The value drives factory-time control; its references rewrite to the raw factory parameter. Converterless `T` compiles. **(new)** |
| **Mixed** | non-empty | Depth-zero refs lift; control refs rewrite via `RootNames`. Mirrors in-body. |

`Unquote` now means one thing across the field, local, and param spellings: lift-on-splice,
control-without-lift.

### 2.2 Structure — extract a shared `StagedRootLifter`

The param path (`LiftStagedRootParameters`) and the in-body path (`LiftStagedParameters`) will share
their staged-root lift logic. Extract the common core into one helper both call. The helper owns:

1. **Reference partition** — compute `depthZeroReferences` from the four consumed-node sets above.
2. **Gated lift** — emit the `syntaxForParam_<name>` preamble local and replace references **only if**
   `depthZeroReferences.Count > 0`.
3. **Control-region wiring** — `RootNames[symbol] = paramName` for every root symbol, so unrolled
   control-region references rewrite to the raw factory parameter.
4. **String-roots** — map string-typed references to the raw factory parameter (interpolation fold).
5. **Trim** the carrier node(s) from quoted output; add the factory `Parameter` with its
   fully-qualified declared type.

The two paths differ on a small set of inputs, supplied by an adapter over the two staged-root
shapes (`StagedParameterRoot` vs `StagedParameter`):

- **name / declared type / references / root-symbols / trim-node(s)** — differing property shapes.
- **lift strategy** — a delegate producing the depth-zero lift expression:
  - **param path** → `ValueLift.TryEmitValueLift` (keeps `[Runtime]` converter discovery, SY1008 /
    SY1009, and the fully-qualified declared parameter type).
  - **in-body path** → direct `value.ToSyntax()` (**unchanged** — preserves byte-identical existing
    snapshots; a built-in / generic-fallback lift is textually identical to what `ValueLift`
    would emit anyway, so the seam changes nothing for existing output).

Param-path specifics that stay in `LiftStagedRootParameters`, outside the shared helper: the generic
type-parameter declaration block (a `[Unquote] T` whose `T` is an open type parameter declares a
type parameter on the factory method) runs before the shared call, and the `ctx.ConverterError`
bail stays with the param path.

### 2.3 Ordering

`InitValueLift` currently runs after `LiftStagedParameters`. The in-body path keeps its direct
`.ToSyntax()` lift strategy and does **not** consume `ctx.ValueLift`, so no reordering is required.
The param path continues to run after `InitValueLift`, exactly as today.

### 2.4 Non-goals

- **No lift-mechanism unification.** The in-body path is not migrated onto `ValueLift`. Doing so
  could flip an existing depth-zero custom-type reference from a compiling `custom.ToSyntax()` to an
  SY1008 diagnostic, perturbing snapshots. The lift-strategy seam exists precisely to avoid that.
- **No new diagnostic** for a control-only `[Unquote]` param. Silence matches the in-body path — a
  never-spliced live root is a legitimate, common shape (every `foreach`-driver column list).
- Cacheability is preserved: all work stays semantic, inside the transform; every touched pipeline
  step keeps its tracking name; the all-steps-cached guard stays green.

## 3. Dog-food — `columns` declared once (uses shipped Task 1; no generator change)

Independent of §2, and the concrete authoring win the user asked for: stop re-declaring the
`columns` live root in every ObjectReader member. This rides the **already-shipped** carrier
field-initializer live root (carrier plan Task 1) and needs **no** generator change.

### 3.1 Change

In `ReaderTemplate.cs`, the parent carrier `ObjectReaderTemplate<T>`:

```csharp
[Template<Factory>]
internal class ObjectReaderTemplate<[Splice] T>
{
    private static readonly EquatableArray<ColumnInfo> columns = Unquote<EquatableArray<ColumnInfo>>();  // ONCE (static — see below)
    // ...
    public string GetName(int i)
    {
        // deleted: var columns = Unquote<EquatableArray<ColumnInfo>>();
        foreach (var c in columns) ...
    }
}
```

Delete the per-member `var columns = Unquote<EquatableArray<ColumnInfo>>();` from the six
parent-scope sites: `FieldCount`, `GetName`, `GetOrdinal`, `GetFieldType`, `GetValue`, and the
`[Splice] TypedGetters()` generator. Task 1's `(name, T)` dedupe folds the field and any leftover
site into the same single factory parameter.

The field is **`static`**: `[Splice] TypedGetters()` is a `static` method, so once its local is
deleted its `columns` reference must bind to a static field (an instance field would be CS0120 in the
real-compiling carrier). The staged-root finder is modifier-agnostic and the field is trimmed from
quoted output, so `static` is safe and carries no output cost.

### 3.2 Scope boundary — `TypedGetter` keeps its local

`TypedGetter` is a **nested `[Template]`** — its own factory scope. `TemplateScopedWalker` skips
foreign child subtrees, so the parent's carrier field is not seeded into it. Its
`var columns = Unquote<EquatableArray<ColumnInfo>>();` (and `clrTypeName` / `typeLabel`) **stay** as
body locals — already correct via the in-body path. This supersedes the carrier plan's Task 2, which
wrongly tried to convert these to `[Unquote]` params.

Every `columns` reference in the ObjectReader carrier is control-only or fold-claimed (foreach
drivers, `columns.Count` scalar fold, a `.Where(...).Select(...)` builder spine, a build-time
factory argument). None is spliced — so the carrier-field refactor requires no converter and
produces **byte-identical** output.

## 4. Verification

**Generator fix (§2) — dedicated unit tests in `Synto.Test`:**
- A converterless, control-only `[Unquote] T` param (e.g. `IReadOnlyList<int>` / an `EquatableArray<…>`
  driving a `foreach`) **compiles with no error diagnostics**, drives unrolling, and never appears in
  produced output. (Was SY1008.)
- A **spliced** `[Unquote] T` param still lifts; a genuinely missing converter still yields SY1008,
  an ambiguous one still yields SY1009.
- A **mixed** param (spliced + control) lifts depth-zero refs and rewrites control refs.
- Full `Synto.Test` suite green; cacheability all-steps-cached guard green.

**Dog-food (§3):**
- ObjectReader **10/10 byte-identical** — zero `.received.cs`
  (`Synto.Example.ObjectReader.Tests`).

## 5. Out of scope / follow-ups

- The carrier spec's §2.1 primary-ctor `[Unquote]` param spelling for `columns` becomes viable once
  §2 lands, but is **not** adopted here — the field spelling is what ObjectReader uses. Revisit if a
  primary-ctor form is wanted later.
- Converting `TypedGetter`'s locals to `[Unquote]` params is explicitly dropped (§3.2).

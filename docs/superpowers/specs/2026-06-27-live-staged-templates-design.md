# Live (staged) templates — binding-time analysis for Synto templates

> **STATUS: DELIVERED (2026-06-27).** Implemented by plan `docs/superpowers/plans/2026-06-27-live-staged-templates.md`
> (Tasks 0–10). The surface spellings here were frozen in the plan's "Locked Names (resolved Task 0)" section —
> consult that table for the as-shipped names; any "ILLUSTRATIVE" spelling below is superseded by it. The
> ObjectReader dog-food (`examples/Synto.Example.ObjectReader/`) rides the shipped surface, and cacheability +
> injected-surface guards for a staged template are pinned in `test/Synto.Test/Templating/`.
>
> - **Date:** 2026-06-27
> - **Reads on:** the shipped Templating surface (`src/Synto/Templating/`,
>   `src/Synto.SourceGenerator/Templating/TemplateFactorySourceGenerator.cs`,
>   `src/Synto/CSharpSyntaxQuoter.cs`) and the drafted roadmap/walkthrough
>   (`docs/superpowers/drafts/2026-06-24-synto-real-world-roadmap.md`,
>   `docs/superpowers/drafts/2026-06-24-objectreader-repeater-walkthrough.md`).
> - **Relationship to the roadmap:** this **supersedes** roadmap idea B / P2 (the special-purpose
>   `[Repeat]` repeater). Data-driven expansion stops being a bespoke hole and becomes one case of a
>   general staging mechanism. P0 (identifier/member-name lift) is absorbed as the live→quoted lift at
>   identifier position; P1/P3/P4 are orthogonal and unaffected.
> - **House style:** pins **contracts and behaviors**, not exact code. Every concrete surface spelling
>   below is marked **ILLUSTRATIVE** and is an owner decision at §10.

---

## 1. Summary

A `[Template]` body is **quoted** today: the Synto generator turns it into `SyntaxFactory.X(...)`
calls that rebuild it, and holes (`[Inline]`, the `Syntax` repeater) splice single nodes into leaf
positions. This feature adds a **binding-time split** to the body:

- Some data is marked **live**. Code in the live data's dataflow trace is **not quoted** — it is
  emitted **verbatim as executing C#** into the generated template-invocation method (the "factory"),
  so it **runs when the template is invoked** (i.e. at the consuming generator's generation time).
- Everything else is quoted into output syntax, exactly as today.
- At the boundary, a live **value** used in quoted code is **lifted** to syntax (a literal /
  `ToSyntax()` / identifier), and quoted syntax nested inside live **control flow** is **collected**
  as that control flow runs — which is loop unrolling / branch specialization.

This is multi-stage programming / binding-time analysis (à la LMS, MetaML): `[Live]` = stage-0 "runs
now, in the factory"; the rest of the template = stage-1 "becomes generated code"; the dataflow infers
the cut. `[Inline]` is recognizably the depth-0 special case (trace = the parameter itself, lifted at
the leaf).

**Why:** it is the principled foundation under the data-driven expansion the ObjectReader dog-food
needs (the four `switch` members built today as `StringBuilder` + `ParseMemberDeclaration`). Instead of
a special-purpose `[Repeat]` + phantom-`foreach` recognizer, the author writes an ordinary `foreach`
over live data and it unrolls — and because the live scaffold runs verbatim, **arbitrary imperative
live code works** (`for`, `while`, mutable accumulation), not just the one repeater shape.

---

## 2. Core concept — two binding times, one invocation

Today every node in a template carries one binding time (quoted → output). This feature introduces a
second: **live** (executes at template-invocation time, inside the factory). The generated factory
becomes a small **staged program** — partly executing code, partly node-construction.

- **Template-invocation time** is when the consuming generator calls `Factory.X(...)`. (We adopt
  *template invocation* over *factory invocation* as the name for this moment.)
- **Live** code is transcribed into the factory body essentially **verbatim** and runs at that moment.
- **Quoted** code is rebuilt into output syntax, as today.

Cacheability is unaffected (§8): all staging is **emit-time inside the factory**. The Synto pipeline
value remains the equatable generated-text result; no `SemanticModel`/symbol/node enters cached state.

---

## 3. The surface — live roots

A **live root** is the seed of liveness. Liveness propagates from roots by dataflow (§4). There are
two independent axes:

- **Capability** — *splice* (value lifted to a literal at the leaf; today's `[Inline]`) vs *live*
  (drives staging: control flow over it executes, computations on it run).
- **Origin** — *parameter* (the caller supplies the value; lifted to a factory parameter) vs *bound*
  (the value is computed in the template body).

The 2×2, every cell reachable:

| | **parameter** (caller supplies) | **bound** (computed in-body) |
|---|---|---|
| **splice** (leaf → literal) | `[Inline] T x` *(shipped)* | a plain literal — no marker needed |
| **live** (drives staging) | `var x = Parameter<T>()` *(new)* | `[Live] var x = <expr>` *(new)* |

### 3.1 `Parameter<T>(...)` — the live parameter marker (ILLUSTRATIVE)

A construction marker that makes its binding **live** *and* **lifts it to an invocation-time
parameter** of the generated factory. A **static method on `Template`** — so a consumer chooses the
ergonomics: qualified `Template.Parameter<T>()`, or `using static Synto.Templating.Template;` for bare
`Parameter<T>()` (the same `using static` pattern the walkthrough used for `Member`/`TypeOf`).
Function form (chosen over a generic *type* so `var x = Parameter<T>()` infers `x : T` and the carrier
compiles with no `.Value` gymnastics):

```csharp
namespace Synto.Templating; // ILLUSTRATIVE — spelling/namespace per §10
public static class Template
{
    /// Inert marker. The carrier is quoted, never run, so the body is never reached.
    /// Recognized BY BINDING (leak-free, same family as `Syntax`): the transform lifts the
    /// initialized binding to a factory parameter and treats it as a live root.
    public static T Parameter<T>(string? parameterName = null) => default!;
}
```

**Two positions:**

- **Declaration initializer** — `var columns = Parameter<EquatableArray<ColumnInfo>>();` — the
  binding's identifier names the parameter; the optional `parameterName` argument overrides it.
- **Inline (no binding to name it)** — `=> Parameter<int>("fieldCount")` — `parameterName` is
  **required**; omitting it is a diagnostic (§7). Inline is an invocation-time parameter introduced
  and used in one place, and composes with the lift boundary for free: inline in a quoted expression it
  lifts to a literal; inline in live control flow it is a verbatim parameter reference.

**Identity — `(parameterName, T)`:**

- The same `(name, T)` used at multiple sites — and the declaration form `var name =
  Parameter<T>()` together with an inline `Parameter<T>("name")` — **collapse to one** factory
  parameter referenced at each site.
- The same `name` with a **different `T`** is a contradiction in the signature → diagnostic (§7).

**Naming rules:** an **inferred** name (from the binding) may auto-dedup against collisions (the
generator's existing `while (!names.Add(n)) n += '_'`). An **explicit** `parameterName` that collides
with another distinct parameter is a **diagnostic**, never silently mangled — it is intentional and
lands in the snapshotted factory signature, so renaming it would betray the author.

### 3.2 `[Live]` — the live capability marker (ILLUSTRATIVE)

`[Live]` on a **declaration** marks it live with a **bound** (in-body) value, and does **not** force a
parameter:

```csharp
[Live] var ops = new[] { "Add", "Sub", "Mul" };   // staged constant, no caller input
foreach (var op in ops) { /* live region driven by `ops` */ }
```

This is "staging by intent": data with no live *input* that the author nonetheless wants executed at
invocation time. `[Live]` also applies to a **method parameter** to give it live capability (vs
`[Inline]`'s splice). Decoupling `[Live]` from "parameter" is deliberate: the **marker** carries the
parameter origin; the **attribute** carries only the live capability.

### 3.3 Relationship to `[Inline]` — coexist for v1

`Parameter<T>()` and `[Inline]` both lift a value to a factory parameter. v1 keeps them as **two named
surfaces**: `[Inline]` is the splice-parameter (value → literal), `Parameter<T>()` is the
live-parameter (drives staging). `[Inline]` is shipped, consumer-visible contract, and Synto treats
that as a **one-way door** (`principles.md`); unifying it into a single "parameter, with a splice-vs-live
flag" concept now would reopen a shipped marker for tidiness — the speculative move the principles
caution against. The unification is recorded as a **designed-for** north star (§9), cheap to honor
later, costly to rush.

---

## 4. Propagation — from roots to a binding-time partition

Inside the transform (where all semantic work already happens — no symbols escape to pipeline state),
compute, via the semantic model's def-use / dataflow:

1. **Live set** — the set of expressions/statements whose value or execution depends on a live root,
   transitively. Each node is then classified:
   - **quoted** — independent of every live root (the default; unchanged behavior).
   - **live value** — an expression whose value depends on a live root, consumed where a *value* is
     consumed.
   - **live control** — a control-flow construct (`foreach`/`for`/`while`/`if`/…) whose **driving
     expression** (iteration source / condition) is live. It **runs** at invocation time (unrolls /
     specializes). Control flow whose driver is *quoted* stays quoted (emitted as real output control
     flow), as today.
2. **Lift points** — a *live value* that is a child of a *quoted* node. Lifted to syntax at that node.
3. **Collect points** — a *quoted* node that is a child of a *live control* region. Collected as the
   region runs and incorporated at the enclosing **container** (§5.3).

Conservatism is expected: static dataflow under-approximates through opaque boundaries (containers,
virtual dispatch, helpers the analysis won't follow). The `[Live]` manual root (§3.2) is the
escape hatch to re-assert liveness where the trail was lost — guarded by the impossible-cut diagnostic
(§7).

---

## 5. Mechanism — the quoter stays untouched

The base `CSharpSyntaxQuoter` (a `CSharpSyntaxVisitor<ExpressionSyntax>`, bootstrap-generated, sync-bound
to `Synto.Bootstrap`) is **not modified**. All new behavior rides the existing seam in
`TemplateSyntaxQuoter`: `unquotedReplacements : Dictionary<SyntaxNode, ExpressionSyntax>` — "emit this
expression instead of quoting this node." Every hole today is already an entry here; staging adds richer
entries.

### 5.1 The three per-node behaviors

- **Quote** — unchanged; `SyntaxFactory.X(...)`.
- **Lift** (live value in quoted position) — replace the node with `value.ToSyntax()` (built-in /
  `[Runtime]` converter), a literal, or an identifier/`typeof` form when the value lands in
  identifier/type position (this absorbs roadmap P0). Mechanically identical to today's `[Inline]`
  replacement, just keyed by dataflow rather than by a parameter marker.
- **Run + collect** (live control region) — see §5.2/§5.3.

### 5.2 Run-verbatim with island collection

A live control region is **not transcribed** (no rewrite into LINQ). It is emitted **verbatim** and
executes. The **only** rewrites inside a live region are:

1. live root references → factory parameter names (`columns` stays `columns`), and
2. each **quoted island** (a quoted node sitting inside the live region) → a **collector call**
   wrapping the island's quote, with live values lifted.

The live island cannot run verbatim because it references **generated-world** names (e.g. a generated
method's parameter `i`, the output `if`/`return`) that do not exist at invocation time; replacing it
with `collect(quote(island))` is unavoidable and sufficient. The scaffold around it (`foreach`/`while`
headers, live conditions, live locals) stays verbatim — which is precisely why **arbitrary imperative
live code works**.

The replacement expression for the region is **precomputed** by the transform: it recursively runs the
**same quoter** on each island (with the live-value lifts in its map), then wraps those island-quotes
in the verbatim live scaffold + a collection helper. The result is registered in `unquotedReplacements`
at the container node; when the top-level quote runs it hits the container first, emits the precomputed
expression, and never descends into the live region. The quoter is invoked recursively on islands but
its source is unchanged.

### 5.3 The boundary node — where each replacement is keyed

- **Value lift (1→1, cardinality preserved)** — keyed **at the node** (the live value). This is the
  `[Inline]` depth-0 case.
- **Run expansion (1→N, cardinality changes)** — keyed at the **owning list/container, one level
  above** the live region, because the fixed-size `new[]{…}` the quoter emits for a list cannot host a
  one-to-many. The container's replacement concatenates fixed quoted siblings with the live region's
  collected run (e.g. a method block whose statements are `[<foreach's collected run>, <trailing quoted
  throw>]`).

### 5.4 Plumbing reuse

The live-parameter marker is **found** like `InlinedParameterFinder` finds `[Inline]`: it **adds a
factory parameter** and **trims** the marker's declaration from the quoted body — the same trim+add path
already in `CreateSyntaxFactoryMethod`. Fully imperative live regions, if ever needed as *statements*
rather than a single container expression, hoist into the factory body via the existing `preamble`
list — but the run-verbatim container form in §5.2 covers the in-scope cases without it.

---

## 6. Collection helpers

The incorporation logic (gather a run of produced nodes; splice it into the enclosing list/block/
argument-list at the right slot) lives in **runtime helper extension methods**, authored once in
`src/Synto`, emitted **file-local** into the one generated file that uses them, and selected by the
existing *scan-the-emitted-factory-for-calls* mechanism (`FindReferencedHelpers`) — exactly the model
behind `ToSyntax`/`ToTypeSyntax`/`OrNullLiteralExpression`. They carry **no runtime package
dependency** into consumer output. The concrete helper set (build/append a `SyntaxList<TNode>` and a
`SeparatedSyntaxList<TNode>` from mixed fixed nodes + node runs) is an implementation detail of the
plan, not pinned here.

---

## 7. Diagnostics

New `SY####` descriptors (numbers assigned at plan time), in the catch-convert-report discipline:

- **Missing parameter name** — inline `Parameter<T>()` with no binding to name it and no
  `parameterName` argument.
- **Explicit name collision** — an explicit `parameterName` equal to another distinct parameter's
  name.
- **Conflicting parameter type** — same `parameterName`, different `T`.
- **Impossible cut** — a node forced live (a manual `[Live]` root, or its dataflow) that transitively
  depends on a **quoted / generated-world** value, i.e. a live fragment that cannot run because an input
  exists only in the output. Reported, never miscompiled.
- **Unsupported live shape** (catch-all) — a live construct the v1 transform does not handle degrades
  to a clean diagnostic, never a mis-expansion.

---

## 8. Cacheability (sacred)

Unchanged and explicitly preserved:

- All staging is **emit-time inside the factory**. The Synto pipeline value remains the equatable
  `TemplateGenerationResult` (generated text + `EquatableArray<DiagnosticInfo>`); no `Compilation`,
  `ISymbol`, `SemanticModel`, or `SyntaxNode` enters cached state.
- A live value the **consumer** passes into the factory must be value-equatable for the consumer's *own*
  pipeline — their responsibility, identical to passing any value into a generator today, and aided by
  the fact it is now compile-checked rather than string-glued.

---

## 9. Scope

**In scope (v1):**

- `[Live]` (capability; declaration + method parameter) and `Parameter<T>(...)` (live parameter;
  both positions, identity/dedup/diagnostic rules of §3.1).
- Dataflow propagation, the quote/lift/run+collect partition, run-verbatim islands, value lift at
  identifier/type/literal positions (absorbs roadmap P0).
- **Statement-level live sections in method bodies**, including arbitrary imperative live control flow
  (`for`/`foreach`/`while`/`if`, live locals, mutable accumulation).
- **Type-level live roots** shared across a type template's method bodies (the ObjectReader shape:
  fixed member set, expansion only *within* bodies).
- File-local collection helpers (§6).

**Out of scope (this feature, explicitly):**

- **Member-list expansion** — generating *N members* from a live loop. C# has no control-flow position
  between member declarations, so it would need a new host construct; that is dropped entirely from
  this feature, not deferred-with-a-stub.
- **`[Inline]` unification** — recorded as a designed-for north star (§3.3), not built.

---

## 10. Open decisions for the owner

1. **Marker namespace / spelling.** Leaning `Synto.Templating.Template.Parameter<T>(...)` as a
   **static method** so consumers can `using static` it for bare `Parameter<T>()` or keep it qualified
   ("best of both worlds"). Remaining bikeshed: the `[Live]` attribute name and whether other future
   markers share the `Template` static class. All injected `internal` via the existing surface-injection
   path. Flag any strong preference.
2. **`Parameter<T>()` return-type ergonomics.** Confirm the `T`-returning inert function (vs a generic
   type) is the form you want, given it must compile in the carrier under `netstandard2.0`.
3. **Anything in §9's in-scope list to trim further for a first cut** (e.g. ship value-lift +
   `foreach` first, add `while`/mutable-accumulation second) — or hold the full statement-level scope.

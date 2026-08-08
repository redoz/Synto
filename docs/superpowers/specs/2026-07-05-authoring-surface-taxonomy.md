# Authoring-surface taxonomy — the injected staging/lift surface

**Date:** 2026-07-05
**Status:** Design ruling (audit before shipping the value→node lift)
**Supersedes:** `2026-07-05-constant-literal-facade-design.md` (the standalone `Constant` facade — dropped; see §3.2)
**Branch:** `experimental/constant-facade`

## Why this document

The injected authoring surface (the `Template.*` facades + the staging attributes +
`SyntoBuilders`) grew feature-by-feature. Before adding the next member (a value→
`ExpressionSyntax` lift for builder-argument slots) we inventoried the whole surface, found
its underlying taxonomy, and ruled on the redundancies. This doc is the cohesive map plus the
keep/merge/rename verdicts. It is the *why/shape* record; `docs/kb/subsystems/templating.md`
remains the *where* map and is updated to match once these land.

## 1. The taxonomy — three axes

Every member of the staging/lift surface is one cell of **operand** × **spelling** ×
**staging behavior**:

- **operand** — what enters: a **value** (computed/supplied at factory time), a **type**, or a
  pre-built **node**.
- **spelling** — how it is written: a **parameter attribute**, an **inline call**, or an
  **in-body declaration**.
- **staging behavior** — **drives-control** (a live root that may unroll loops / specialize
  conditions), **liveness-shielded** (lifted as a value but never drives control), or
  **verbatim** (a pre-built node spliced with no evaluation).

The lift itself (value → literal syntax) is a *single* operation (`ValueLift`, backed by
`LiteralSyntaxExtensions.ToSyntax`); the surface members differ only by which cell they occupy.

### Group 1 — Live roots (a value is lifted into output syntax)

| Surface | Spelling | Operand | Value source | Behavior | Result type |
|---|---|---|---|---|---|
| `[Unquote]` param | parameter-attr | value / type | caller | **drives control** | factory parameter |
| `Unquote<T>()` *(renamed from `Parameter<T>()`)* | in-body decl | value | caller | **drives control** | `T` (value position) |
| `Unquote<T>(value)` | inline call | value | in-body expr | **drives control** | `T` (value position) |
| `[Quote]` param | parameter-attr | value | caller | liveness-shielded | factory parameter |
| `Quote<T>(value)` | inline call | value | in-body expr | liveness-shielded | `T` (value position) |

### Group 2 — Verbatim splice (a pre-built node enters, no eval, no lift)

| Surface | Spelling | Operand | Behavior | Result type |
|---|---|---|---|---|
| `[Splice]` param | parameter-attr | node / type | verbatim | factory parameter |
| `[Splice]` method | method-attr | members | verbatim (member generator) | spliced members |
| `Splice(node)` | inline call | node | verbatim | `ExpressionSyntax` (node position) |

### Group 3 — Node builders (construct a node from parts)

| Surface | Spelling | Mechanism | Result type |
|---|---|---|---|
| `Member<T>(inst, name)` | inline call | **hardwired** → `SyntoBuilders.Member` | `T` (value position) |
| `TypeOf(name)` | inline call | **hardwired** → `SyntoBuilders.TypeOf` | `Type` (value position) |
| `[SyntaxBuilder]` method | method-attr + `[Quoted]`/`[TypeArg]`/`[ReturnType]` params | **synthesized** facade | builder-decided |

### Builder-argument binding-time kinds (the sub-taxonomy inside Group 3)

A `[SyntaxBuilder]` parameter declares how its call argument is bound. Today three kinds
exist; §3.2 adds the fourth:

| Annotation | Argument treatment | Result type |
|---|---|---|
| *(unmarked)* | pass the live/computed value verbatim | parameter type |
| `[Quoted]` | quote the argument as an output-world island | `ExpressionSyntax` |
| `[TypeArg]` | quote the type island (+ surface a facade generic param) | `TypeSyntax` |
| **`[Constant]`** *(new — §3.2)* | evaluate the value at factory time, **lift** to a literal | `ExpressionSyntax` |

### Orthogonal (mapped for completeness, not part of the lift overlap)

- **Definition** — `[Template<T>]` (top-level, pins target), `[Template]` (nested sub-template
  only), `TemplateOption` (`Bare`/`Single`/`PreserveTrivia`).
- **Lift support** — `[Runtime]` (marks a `ToSyntax(this T)` converter provider; inferred, not a
  root).
- **Decorations** — `[Identifier]`, `[Visibility]`, `[Sealed]`, `[Implements]`, `[Inherits]`,
  `Access`. Post-quote type-shape markers; internally cohesive; not value/type/node lifts.

## 2. The pattern the map reveals

Two structural facts fall out and drive every verdict below:

1. **Live-value roots vary on two independent axes:** *drives-control?* (`Unquote` = yes,
   `Quote` = no) and *value source?* (a factory **parameter** vs an in-body **expression**). The
   naming honors this everywhere except one member (see §3.1).

2. **A value→node lift is a builder-argument binding-time kind, not a free-standing facade.**
   `[Quoted]` (island→`ExpressionSyntax`) and `[TypeArg]` (type→`TypeSyntax`) already live as
   per-parameter binding-time declarations on `[SyntaxBuilder]` methods. The value-lift is the
   missing fourth member of that exact set (see §3.2).

## 3. Verdicts

### 3.1 Redundancy — `Parameter<T>()` is `[Unquote]` under a positional name → **rename**

`Parameter<T>()` is a **caller-supplied live root declared in the body** — semantically identical
to an `[Unquote]` value parameter (its own docstring: "lifts exactly like an `[Unquote]` value").
It is the *only* root named by position (`Parameter`) rather than by binding-time role, while the
`Unquote` name is spent on the *in-body-expression* local (`Unquote<T>(value)`). The `[Quote]` /
`Quote<T>()` pair already shows the clean shape: one name, argument presence distinguishing
parameter-source from expression-source.

**Ruling:** rename `Parameter<T>()` → **`Unquote<T>()`**. The `Unquote` name then covers one
concept across all three spellings:

| Contract | Spelling | Meaning |
|---|---|---|
| `[Unquote] T x` | parameter-attr | caller-supplied live root (signature form) |
| `Unquote<T>()` | in-body decl, **no value arg** | caller-supplied live root (body form); parameter name = bound variable name |
| `Unquote<T>(parameterName: "…")` | in-body decl, **name arg** | same, with an explicit parameter name for inline (non-`var`) position |
| `Unquote<T>(value: expr)` | inline call, **value arg** | in-body-expression local, lifted on consumption |

**Disambiguation contract (load-bearing):** the parameter-name overload keeps a `string`
argument, so for `T = string` it is signature-adjacent to the value overload. Param-vs-local is
therefore resolved by an **explicitly named argument** — `Unquote<string>(parameterName: "x")`
declares a parameter; `Unquote<string>(value: expr)` lifts an expression. The generator
recognizes the call by the bound symbol; the named argument makes the author's intent
unambiguous rather than leaning on overload resolution (which would silently prefer the
`string`-typed name overload for a bare positional `string`). This asymmetry is the reason the
two live behind one name but require naming at the `string` boundary.

This is a breaking rename of an inert facade, acceptable pre-GA. `Unquote<T>(value)` (the
existing local form) is unchanged.

### 3.2 The trigger — `Constant` is a builder-arg kind, not a facade → **`[Constant]`, drop the facade**

The standalone `Constant<T>() → ExpressionSyntax` facade (prior spec) is the node-position twin of
`Quote<T>() → T`: **the same value-lift, split only by return type** because C# forces the return
shape to match the *consumption position* (an output-world value slot needs `T` so the body
type-checks; a builder-argument slot needs `ExpressionSyntax`). That twinning is the smell that
triggered this audit — and the map shows the cohesive home for the value-lift is the
builder-argument binding-time sub-taxonomy (§1), where `[Quoted]` and `[TypeArg]` already turn
call arguments into nodes.

**Ruling:** add **`[Constant]`** as the fourth builder-argument binding-time kind. Marking a
`[SyntaxBuilder]` parameter `[Constant]` means: evaluate the call argument at factory-build time
and lift it to its literal `ExpressionSyntax` via the shared `ValueLift` (`value.ToSyntax()` for a
built-in literal type or an inlined generic type parameter; a `[Runtime]` converter for a concrete
custom type — identical policy, diagnostics `SY1011`/`SY1012`, and single-source literal building
to `[Quote]`/`Quote`). No standalone facade, **no `Quote` twin**.

First consumer — the switch `Case` label:

```csharp
// builder (SyntoBuilders.cs)
[SyntaxBuilder]
public static SwitchSectionSyntax Case([Constant] ExpressionSyntax label, params IEnumerable<StatementSyntax> body) => …;

// call site (ObjectReader TypedGetter)
SyntoBuilders.Case(c.Ordinal, SyntoBuilders.Return(Member<TRet>(_e.Current, c.Name)))
```

The label argument is a factory-time value (`c.Ordinal`) lifted to `LiteralExpression(…)`; a live
projection variable inside it does **not** propagate liveness to the enclosing `Switch`
(liveness-shielded, exactly as the un-`[Quoted]` label is today). Generated output is
byte-identical to the current hand-form, so the ObjectReader snapshot is unchanged.

**Deferred, not dropped:** a general `Constant<T>() → ExpressionSyntax` facade is warranted only
if a **free-standing** lifted node is ever needed *outside* a builder-argument position (none
today). If that consumer appears, `Constant` is the correct name/shape for it (the node-position
sibling of `Quote`); until then it is YAGNI. This doc supersedes the standalone-facade spec on
that basis.

**Binding-time note (per param, by design):** `[Constant]` fixes the label's binding-time to
value-lift, changing `Case`'s contract from "caller passes a pre-built `ExpressionSyntax`" to
"caller passes a value to lift" — the builder author's choice, consistent with how `[Quoted]`
already fixes binding-time per parameter. A builder wanting both forms would expose two
parameters/overloads.

### 3.3 Redundancy — `Member`/`TypeOf` are a hardwired facade→builder pair → **keep, record debt**

Two mechanisms map a facade to a factory-time builder: `Member`/`TypeOf` are **hardwired**
(the binder special-cases the `Template.Member`/`Template.TypeOf` call and rewrites to the
matching `SyntoBuilders` method), while Switch/Case/Arm/… are **synthesized** from
`[SyntaxBuilder]` + parameter annotations. `Member<T>` is expressible under the general
mechanism (`[Quoted] instance` + `[TypeArg][ReturnType] T` + `string name`); `TypeOf` likewise.

**Ruling:** keep as-is for now — the `Template.Member` / `Template.TypeOf` spelling is
discoverable on the `Template` facade, whereas a synthesized facade surfaces under its builder
method name. Record the debt: they could migrate to `[SyntaxBuilder]` synthesis and delete the
hardwired recognition path. Not blocking, not urgent; revisit if the hardwired path costs a
change elsewhere.

## 4. Net surface after these rulings

- **Live roots:** one name per binding-time role — `Unquote` (drives control) and `Quote` (shielded),
  each with parameter, body-declaration, and inline-expression spellings. `Parameter` is gone.
- **Value→node lift:** a builder-argument binding-time kind `[Constant]`, completing
  `[Quoted]`/`[TypeArg]`/unmarked. No facade, no `Quote` twin.
- **Splice / node builders / decorations / definition:** unchanged.

## 5. Follow-up work (separate plans; contracts only)

1. **Rename `Parameter<T>()` → `Unquote<T>()`** — the argumentless + `parameterName` overloads,
   the named-argument disambiguation contract (§3.1), the injected-surface completeness test, and
   the `SurfaceInjectionTest` snapshot. Update `docs/kb/subsystems/templating.md` live-roots list.
2. **Add `[Constant]` builder-arg kind** — attribute in `Builders/Surface/`; facade synthesis +
   argument binding lift the value via `ValueLift` (single-source, `SY1011`/`SY1012`); switch
   `Case`'s `label` (and any other value-labelled builder param) adopts it; ObjectReader
   `TypedGetter` labels become `Case(c.Ordinal, …)` with a byte-identical snapshot.
3. **(Deferred debt)** migrate `Member`/`TypeOf` to `[SyntaxBuilder]` synthesis; drop the
   hardwired path.

Out of scope here: the `[Unquote]` value-parameter region-consumed lift-skip asymmetry (separate
friction), and any change to `Quote`/`Splice`/decoration semantics.

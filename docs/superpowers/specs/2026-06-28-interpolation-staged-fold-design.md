# Interpolation staged-fold — bake build-time staged strings into literal text (design)

**Date:** 2026-06-28
**Status:** Shipped (quoter correctness fix; plan `2026-06-28-interpolation-staged-fold.md`, Tasks 1–4).
Default/automatic; scoped to bare staged-**string** holes for v1. Format/alignment holes, non-string
staged values, and verbatim/raw interpolated strings explicitly deferred.

## Context

Synto's staging contract is "build-time/staged values are *evaluated and baked* into the output;
only genuinely-runtime things stay quoted." Interpolated strings are the one place that contract is
not honored. When a staged value (`Parameter<>()` / `Unquote` root) sits in an interpolation hole,
the quoter lifts it as an *expression inside the hole* rather than baking its value into the text:

```csharp
// template body — i is a runtime method parameter, typeLabel is a staged Parameter<string>
throw new InvalidCastException($"Field {i} is not {typeLabel} column.");

// today's generated output (wrong):
$"Field {i} is not {"a Boolean"} column."
// desired output (build-time value baked, runtime hole preserved):
$"Field {i} is not a Boolean column."
```

This was surfaced by the ObjectReader child-templates collapse: the result was byte-identical to the
verified snapshot **except** the 12 exception messages, purely because of this gap. It is best read
as a **correctness bug** against the staging contract, not a missing feature — so the fix is default
behavior, not an opt-in knob.

Confirmed against the quoter (no interpolation handling exists today): the runtime quoter is a
generated base + hand-written override partial (`src/Synto/CSharpSyntaxQuoter.cs`,
`CSharpSyntaxQuoterBase` + a partial that already overrides `VisitIdentifierName` with the comment
"by specifying this here we prevent the generator from generating this method"). The staging-aware
layer is `TemplateSyntaxQuoter` (`src/Synto.SourceGenerator/Templating/TemplateSyntaxQuoter.cs`),
whose `Visit` swaps a staged-root node for its lifted expression via the `_unquotedReplacements` map
(`SyntaxNode → ExpressionSyntax`). That map carries lifted expressions only — **not types**.

## Behavior

When the quoter processes an `InterpolationSyntax` whose `Expression` is a **build-time staged
string root** and which has **no alignment clause and no format clause**, it bakes the value into the
surrounding literal text — producing one contiguous run of interpolated-string text — instead of
emitting a hole. Everything else is unchanged.

This is automatic and default. There is no marker and no opt-out: opting out of baking a known
build-time value has no use case.

## Non-goals (explicitly unchanged)

- **Alignment/format holes** (`{x,5}`, `{x:N2}`) — left as today's hole form. Folding these would
  require applying alignment/format at build time (culture, padding), deferred.
- **Non-string staged values** (`int`/`bool`/`enum`/`float`/…) — left as holes. Build-time `ToString`
  raises culture/format correctness concerns; deferred. Only `string` (identity, always-correct) folds.
- **Genuinely-runtime holes** (`{i}` where `i` is a real runtime value, not a staged root) — always
  remain holes. Unaffected.
- **No new public surface.** This is internal quoter behavior; no builder, attribute, or API is added.

## Mechanism

1. **Override site (shipped: list-level, not per-hole).** The fold lives in the hand-written
   list-level `Visit<TNode>(SyntaxList<TNode>)` override in `TemplateSyntaxQuoter` (a plain virtual
   override of the generated base list-quoting, the same mechanism as the `[Splice]` member
   `BuildList` path — not the `VisitInterpolation` per-hole override the design first sketched). The
   contents list is the correct level because fusing a foldable hole with its **flanking**
   `InterpolatedStringText` runs requires seeing the sibling text nodes, which a per-hole override
   never receives. Every non-foldable list (and every interpolated string with no foldable hole)
   defers to the base behavior.

2. **Type-metadata channel (shipped: `Dictionary<SyntaxNode, ExpressionSyntax>`).** The fold fires
   only when the hole's expression is a *string* staged root, AND it needs the hole's factory-time
   raw value accessor to emit `accessor.ToInterpolatedText()`. Recovering that accessor from
   `_unquotedReplacements` (which holds the `accessor.ToSyntax()` form) proved unworkable, so the
   channel is upgraded from the design's default `HashSet<SyntaxNode>` to a
   `Dictionary<SyntaxNode, ExpressionSyntax>` mapping each string staged-root reference node to its
   raw accessor — a strictly-internal change carrying no type symbols. It covers **all three** string
   staged-root kinds: `StagedParameterRoot` (`[Unquote]` parameter), `StagedLocal` (`Unquote<T>()`
   local), and `StagedParameter` (`Parameter<T>()` local — the kind the motivating ObjectReader
   `var typeLabel = Parameter<string>()` hole uses). The channel is built at emission as a plain local
   adjacent to where staged roots are already consumed, then passed into the quoter's constructor;
   nothing enters cached pipeline state.

3. **Build-time fold + fuse.** The actual characters are only known when the factory runs, so the
   override emits *factory code* that, at build time, fuses the hole with its adjacent literal-text
   runs into a single `InterpolatedStringText` token — e.g. `" is not " + escape(typeLabel) +
   " column."` — via a small injected helper (a `.ToInterpolatedText()`-style escaper, fitting the
   existing `FileLocalHelpers` injection mechanism alongside `LiteralSyntaxExtensions`/`.ToSyntax()`).
   Escaping handles interpolation-text context: `{` → `{{`, `}` → `}}`, plus quote/backslash handling
   for the string-literal token. Fusing adjacent text into one token (rather than emitting adjacent
   text content nodes) is the robust path to byte-identical output.

4. **Adjacent-hole / boundary cases.** A foldable hole may be flanked by other holes (runtime or
   non-foldable) rather than text; the fold simply produces a text content node in that position
   (merging with whatever literal text actually adjoins it). Multiple foldable holes in one string
   each fold independently. A foldable hole at the very start/end folds against the start/end text.

## Cacheability

The "is this a string staged root" decision is made during **emission** (inside the generation
transform) and produces only equatable output — the generated factory source. No `ITypeSymbol` or
`SemanticModel` is stored in cached pipeline state. The incremental-generator tests stay green.

## Testing

Shipped focused snapshot/behavior tests (`test/Synto.Test/Templating/InterpolationFoldTest.cs`,
plus a direct escaper unit test in `HelperContractTests.cs`):
- A bare staged-string hole folds into contiguous literal text — both the `Parameter<string>()` and
  the `Unquote<string>()` channel-population paths (the core case).
- Escaping: a staged string whose flanking literal value contains `{`, `}`, `"`, `\` round-trips
  correctly when folded.
- A runtime hole (`{i}`) is left untouched.
- A **non-string** staged value (`Parameter<int>()` / `Unquote<bool>`) in a bare hole is left untouched.
- A staged-string hole **with** a format (`{label:N2}`) or alignment (`{label,5}`) clause is left
  untouched (non-goal guard).
- A bare staged-string hole inside a verbatim (`$@"…"`) or raw (`$"""…"""`) string is left untouched.
- Mixed: `$"a {runtime} b {stagedString} c"` folds only the staged part.
- Boundary/adjacency: holes at string start/end, adjacent foldable holes, and a foldable hole flanked
  by a runtime hole each fold independently into the adjoining (possibly empty) literal text.

Toolkit-wide snapshot churn: any existing template that interpolates a staged string will change
output (correctly) and its snapshot is re-accepted.

## Relationship to the other in-flight work

- **Child-templates Task 3 (the escalation):** this fold is the root-cause fix. Once it lands, the
  ObjectReader collapse comes out **byte-identical** with clean output — no gate relaxation, no
  `{"a Boolean"}` blemish. Sequence: land this fold → re-run the child-templates `implement-plan`
  (resumes from its `Plan-Tasks` trailers and finishes Tasks 3–4 byte-identically).
- **Switch builders / list-shaped-builder pattern:** the spec for switch records the interpolated
  string as another list-shaped construct. This fold is the *quoter-side* correctness fix for the
  common bare-string case; it does not preclude a future `Interp(...)` builder for the richer
  (format/alignment, computed-part) cases, should those ever be needed.

## Scope & phasing

- **Ship:** default folding of bare staged-string holes (no alignment/format), with escaping, fusing
  to contiguous text, the type-metadata channel, and the test suite above.
- **Deferred:** format/alignment-clause folding; non-string staged folding; any `Interp(...)` builder.

## Resolved decisions (at implementation)

- **Form of the type-metadata channel** — resolved to `Dictionary<SyntaxNode, ExpressionSyntax>`
  (string staged-root reference node → raw value accessor), not the `HashSet<SyntaxNode>` default,
  because the fold emits `accessor.ToInterpolatedText()` and must recover the raw accessor. See
  Mechanism §2.
- **Escaper as new helper vs. extension of an existing file** — resolved to a **new** helper file
  `src/Synto/InterpolationSyntaxExtensions.cs` (`ToInterpolatedText(this string)`), injected file-local
  via the existing `FileLocalHelpers` scan, alongside `LiteralSyntaxExtensions.ToSyntax`.
- **Verbatim/raw interpolated strings** — deferred for v1: the fold only fires on regular `$"…"`
  strings (gated on `InterpolatedStringStartToken`); `$@"…"` and `$"""…"""` holes stay as runtime
  holes. Boundary tests pin this.

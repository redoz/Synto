# Interpolation staged-fold — bake build-time staged strings into literal text (design)

**Date:** 2026-06-28
**Status:** Design (quoter correctness fix). Default/automatic; scoped to bare staged-**string** holes
for v1. Format/alignment holes and non-string staged values explicitly deferred.

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

1. **Override site.** A hand-written `VisitInterpolation` override in `TemplateSyntaxQuoter` (the
   blessed "override suppresses the generated visitor method" pattern already used for
   `VisitIdentifierName`). The generated base keeps handling every other interpolation case.

2. **Type-metadata channel.** The override must fire only when the hole's expression is a *string*
   staged root. The string-typed staged roots are known upstream (`StagedParameterRoot.Type` /
   `StagedLocal`), but `_unquotedReplacements` carries no type. Pass a small additional input into
   `TemplateSyntaxQuoter` — the set (or predicate) identifying string-typed staged-root nodes — so the
   override can recognize a foldable hole. (Minimal: a `HashSet<SyntaxNode>` of string staged roots.)

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

New focused snapshot/behavior tests:
- A bare staged-string hole folds into contiguous literal text (the core case).
- Escaping: a staged string whose value contains `{`, `}`, `"`, `\` round-trips correctly when folded.
- A runtime hole (`{i}`) is left untouched.
- A staged-string hole **with** a format/alignment clause is left untouched (non-goal guard).
- Mixed: `$"a {runtime} b {stagedString} c"` folds only the staged part.

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

## Open decisions

- **Exact form of the type-metadata channel** (a `HashSet<SyntaxNode>` of string staged roots vs.
  threading the type through the replacements map) — an implementation detail, settled when wiring
  `TemplateSyntaxQuoter`'s constructor.
- **Whether the injected escaper is a new helper or an extension of an existing `*SyntaxExtensions`
  file** — a packaging detail decided at implementation, consistent with `FileLocalHelpers`.

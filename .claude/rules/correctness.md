# Correctness

Evaluate whether the generator does what it's supposed to do and handles failure.

## Checklist

### Logic
- Are there logic bugs? (wrong conditions, off-by-one, missing `SyntaxKind`/node-type
  cases in the quoter)
- Does the generator emit **correct, compilable C#**? The round-trip is the contract: a
  quoted template must re-render to source that parses and means the same thing — the
  `RoundTripTests` `AssertGenerated` cases and the golden `*.verified.cs` snapshots pin
  this.
- Are the marker → emission mappings correct? (`TemplateOption.None`/`Single`/`Bare`
  produce the full-method / single-statement / bare-block shapes; `[Inline]` parameters
  and `[Inline]` type parameters are substituted, not left as references;
  `Syntax`/`Syntax<T>` splice points expand correctly)
- Are inlined values rendered as **parseable syntax**, not runtime representations? (the
  `InlinedGenericTypeArgument` fix: a `List<int>` type argument must render as
  `System.Collections.Generic.List<System.Int32>`, never the unparseable CLR name
  ``List`1[[System.Int32, …]]``)
- Is **incremental caching correct**? The pipeline model must be **equatable value types**
  (`TemplateInfo`, `DiagnosticInfo`, `EquatableArray<T>`, `LocationInfo`) and must **never
  capture `Compilation`, `ISymbol`, or `SyntaxNode`** into pipeline state — doing so breaks
  structural equality and the generator re-runs on every keystroke. The "Refactor template
  generator to equatable pipeline model" change is exactly this discipline.
- Is the output **deterministic**? Stable ordering of members/usings, no timestamps,
  GUIDs, hash-set iteration order, or environment-dependent paths leaking into generated
  source — otherwise snapshots flap and caching is unsound.
- Does the **self-host bootstrap chain** stay consistent? `src/Synto` consumes
  `Synto.Bootstrap` as an analyzer to build itself; the generated `CSharpSyntaxQuoter`
  must agree with the checked-in `CSharpSyntaxQuoter.Generated.cs` (the bootstrap snapshot
  guards this).

### Error Handling
- Is failure surfaced as a **diagnostic, never an exception**? A generator that throws
  crashes analysis in the IDE/compiler. Every unhandled exception must be caught and
  reported as `SY0000` (the internal-error catch-all), not allowed to escape into the host.
- Are **usage errors** reported precisely? The `SY1001`–`SY1007` family validates the
  target (`TargetNotPartial`, `TargetNotClass`, `TargetNotDeclaredInSource`,
  `TargetAncestorNotPartial`) and the source body (`BareSourceCannotBeEmpty`,
  `MultipleStatementsNotAllowed`, `MultipleMembersNotAllowed`). Is each reachable misuse
  mapped to the right descriptor with a useful `Location`, not silently dropped or
  mis-emitted?
- Are diagnostics carried **cacheably**? Findings flow as `DiagnosticInfo` with a
  serializable `LocationInfo` (not a live `Location`) and `EquatableArray<string>`
  arguments — the real `Diagnostic` is reconstructed only at the output stage. Capturing a
  raw `Location`/`ISymbol` here re-breaks caching.
- Are `.Result`, `!` null-suppression, and unchecked casts on reachable paths avoided
  where a malformed or partial compilation could hit them?

### Boundaries
- Is input validated at the **generator boundary**? The attribute target arrives as
  untrusted, possibly half-written user code: a missing `partial`, a non-class target, a
  target in a referenced assembly rather than source, an empty `Bare` body, multiple
  statements under `Single`. Each is a boundary the `SY1xxx` checks must hold.
- Could a **realistic editing state** (a partially-typed type, an unresolved symbol, a
  malformed attribute argument, a template body that doesn't parse) cause wrong output or a
  crash rather than a clean diagnostic and "no output"?
- Does the generator **degrade gracefully on partial input**? Mid-keystroke the model may
  be incomplete; the predicate (`ForAttributeWithMetadataName`) and transform must tolerate
  it and emit nothing-or-a-diagnostic, never a malformed partial type that cascades into
  hundreds of downstream errors.

## Scope Guidance

- **Full evaluation**: Review the quoter's emission across all handled node kinds, every
  marker/`TemplateOption` mapping, the inlining (value and type-parameter) paths, all
  `SY0000`/`SY1xxx` diagnostic paths, the equatability of every pipeline stage, output
  determinism, and the self-host bootstrap snapshot.
- **Change review**: Focus on new node-kind handling (is the emission round-trip-correct?),
  new diagnostic arms (are they exhaustive and precisely located?), new or modified
  pipeline stages (are they equatable value types that capture no
  `Compilation`/`ISymbol`/`SyntaxNode`?), and whether new failure modes emit a diagnostic
  instead of throwing.

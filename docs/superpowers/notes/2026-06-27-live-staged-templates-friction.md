# Live (staged) templates — friction log

Genuine findings as the live-staged-templates feature is implemented (seam strain, near-misses on the quoter,
builder/usings sharp edges, deliberately deferred scope). Empty findings are not manufactured.

## Deferred / out-of-cut (logged, not stubbed)

- **`SeparatedSyntaxList<TNode>` collection helper (Task 5 scope note).** Only the non-separated
  `SyntaxList<TNode>` builder (`CollectionSyntaxExtensions.BuildList`) ships in v1. The in-scope ObjectReader
  dog-food reshapes its `switch` into an `if`-chain, which stays in non-separated **statement** lists, so the
  separator-interleaving variant is never exercised. Deferred deliberately; revisit if a later live region needs
  to emit a comma/separator-delimited list (arguments, parameters, initializer elements) directly from an
  unrolled run.

## Run + collect — foreach unroll (Task 6)

- **Classifier liveness does NOT cover the loop variable.** `BindingTimeClassifier.PropagateLiveness` only
  propagates over `VariableDeclaratorSyntax`, so the `foreach` iteration variable `c` (declared by the
  `ForEachStatementSyntax`, not a declarator) is never marked live. The classifier still correctly flags the
  `foreach` itself as `LiveControl` (its driver `columns` is a root), but the per-island lift-point search needs
  its OWN augmented live set (roots ∪ loop variable). `LiveRegionEmitter.TryBuildScaffold` adds the loop var to a
  region-local live set; it does not reuse the classifier's private `_live`. Task 7 (live locals inside a region,
  mutable accumulation) will widen this region-local set further.
- **`IsLiveValue` is too coarse for lift points (handoff caution, confirmed).** The classifier marks `i ==
  c.Ordinal` `LiveValue` because it references live `c`, but only `c.Ordinal` is a valid lift point (`i` is
  output-world). The emitter therefore does NOT reuse `IsLiveValue`; it walks each island top-down for *maximal
  purely-live* subexpressions (every value-leaf live/const, no output-world local/param) and lifts those via
  `.ToSyntax()`. Member-name identifiers (`Ordinal` in `c.Ordinal`) are skipped so they don't disqualify purity.
- **Live parameter used ONLY in a region must suppress its depth-0 `.ToSyntax()` lift.** A `Parameter<T>()` whose
  references are all consumed by a live region (e.g. `columns` as the foreach source) still becomes a factory
  parameter, but emitting `columns.ToSyntax()` would bind to the generic `ToSyntax<T>` fallback and **throw
  `NotImplementedException` at factory runtime** (it's an `IReadOnlyList<Col>`, not a literal type). The generator
  now splits references into region-consumed vs depth-0 and only lifts the latter; region-consumed refs are
  handled by the verbatim scaffold referencing the factory parameter directly.
- **Container keying replaces the WHOLE owning block.** The replacement is keyed at the `foreach`'s parent
  `BlockSyntax` (the method body), so the quoter never descends into it — the trimmed `var columns = …`
  declaration, the region, and the fixed `throw` sibling are all re-assembled by `Block(BuildList(Run(__run_N),
  <quote of throw>))`. `BranchPruneIdentifier` leaves the block alone (not all children are prunable) and the
  `unquotedReplacements` hit wins over trim/prune in `TemplateSyntaxQuoter.Visit`.
- **Nested file-local helper must be qualified.** `ListSegment<T>` is a nested type of the injected `file static
  class CollectionSyntaxExtensions`, so the emitted scaffold calls `CollectionSyntaxExtensions.BuildList<…>` and
  `CollectionSyntaxExtensions.ListSegment<…>.Run(…)` (qualified). The `BuildList` member-access name still triggers
  the `FindReferencedHelpers` scan, so injection auto-fires. The helper's own `using System.Collections.Generic;`
  is merged into the factory file by the existing helper-usings path, so no F2 usings transplant was needed for the
  canonical foreach — `System.Linq`-style carrier transplant is deferred until a live scaffold actually needs it
  (Task 7/9).
- **Quoter NOT touched.** All staging rides `unquotedReplacements` (container key) + fresh per-island
  `TemplateSyntaxQuoter` instances; `CSharpSyntaxQuoter*`/bootstrap unchanged.

## Builder mechanism (Task 3)

- **`[Inline(AsSyntax = true)]` in PARAMETER-TYPE position** (`Build<[Inline(AsSyntax=true)] T>(T instance)`) emits
  `Parameter(..., <ExpressionSyntax>, ...)` into a `TypeSyntax` slot → CS1503. The plan's `Member` snapshot uses
  that form but only snapshots it (latent, never compiled); the zero-collision compile test uses the param form
  `[Inline(AsSyntax = true)] object instance` instead. Quoter/bootstrap untouched.

## Additional control shapes + accumulation (Task 7)

- **`IsPurelyLive` mis-classified output-world calls as liftable.** The depth-0 lift heuristic (lift the maximal
  subexpression that references a live symbol and no output-world local) silently broke on an island like
  `System.Console.WriteLine(k)` where `k` is the only value and it is live: with no output-world *local* to
  disqualify it, the whole invocation was lifted as `System.Console.WriteLine(k).ToSyntax()` (nonsense — a void
  call collapsed to a literal). Task 6's canonical island only worked because it carried an output-world `i`
  (`i == c.Ordinal`) that forced descent. Fix: an `InvocationExpression`/`BaseObjectCreationExpression` in the
  subtree is output-world code that must be QUOTED (its live operands lift), never collapsed — `IsPurelyLive`
  now returns false for those so the walker descends to the live operands. Conservative default: `c.Name.ToUpper()`
  stays output-world (wrap in `Live(...)` to force factory-time).
- **Loop vars + accumulators live outside the classifier.** The classifier's live set covers neither loop
  variables (no `VariableDeclarator` for a `foreach` var; a `for` declarator's `int k = 0` initializer isn't
  live) nor mutation-defined accumulators (`int sum = 0; sum += c.Ordinal;` — the init `0` isn't live). The
  emitter recovers both via `ComputeLiveSet`: seed from `partition.LiveSymbols`, add every region loop var, then
  fixpoint over the region bodies adding any local whose initializer OR an assignment's RHS references the
  current live set. An accumulator declared as a *container sibling* (before the loop) is then recognized live
  and hoisted verbatim into the factory body, not quoted.
- **Region-body statement partition.** Within a scaffold body a statement is kept VERBATIM (root-renamed runtime
  code) iff it is a live-local declaration or a mutation (`=`/`+=`/`++`/`--`) of a live local; otherwise it is a
  quoted island (`__run.Add(<quote>)`). `if` branch-specializes onto the SAME run (both branches append; exactly
  one runs at factory time); `else if` chains recurse.
- **`while` driver via `Live<T>()`.** A `while` needs a mutable live driver — `var k = Live(0); while (k < n) { …; k++; }`.
  The live-local hoist (`var k = 0;`) is the runtime driver; its region-consumed refs (`k < n`, `k++`, and the
  island `k`) skip the depth-0 `syntaxForLive_k` lift entirely (it would be dead), so the live-local loop now
  filters region-consumed refs exactly like the live-param loop.
- **Nested live regions guarded, not handled.** A live-control statement nested inside another region's body
  (only reachable when both drivers are roots — a loop-var-driven inner control is NOT classifier-live, so
  Task 6's inner `if (i == c.Ordinal)` stays a quoted island) would be mis-expanded; the emitter degrades it to
  `SY1014`. True nested-container recursion is deferred.

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

## ObjectReader dog-food migration (Task 9)

- **A single live `Parameter<T>()` identity shared across MANY member bodies needs ALL declaration-site symbols
  seeded as roots.** The ObjectReader template re-declares `var columns = Parameter<EquatableArray<ColumnInfo>>();`
  in ~16 members (one per data-driven member). `LiveParameterFinder` already deduped them to ONE factory parameter
  by `(name, T)`, but `LiveParameter.Symbol` exposed only the FIRST declaration's local — so only the first
  member's `foreach` was classified `LiveControl` and every other member's loop was quoted VERBATIM into the
  output (a literal `foreach` referencing a nonexistent `columns`). Fix: `LiveParameter.Symbols` now carries every
  declaration-site local; the generator seeds the classifier AND `rootNames` from all of them, so every member
  unrolls and each member's `columns` reference renames to the one factory parameter. (`SharedLiveParameterTest`
  pins this.)
- **F2 usings transplant landed here (it was deferred from Task 6).** The cast-less getters filter columns by type
  with `columns.Where(c => c.ColumnTypeName == "global::System.Int32")` — a LINQ call in the live driver that runs
  at factory time (so the type filter never reaches the output; non-matching columns simply emit zero arms). The
  verbatim scaffold therefore needs `System.Linq` in the factory file. The transplant merges the carrier's simple
  namespace usings into `additionalUsings` (deduped vs `RequiredUsings`), excluding `using static …` / alias usings
  (UsingDirectiveSet drops them anyway) and any `Synto.*` using (so the inert facade/marker surface is never pulled
  into factory scope). Only fires when the template has ≥1 live region. Side effect: two pre-existing staged
  snapshots (`RunCollectTest`, `MutableAccumulationAcrossIterations`) re-baselined for a benign using-ORDER change
  (`System.Collections.Generic` now transplanted early instead of arriving via the helper-usings merge).
- **`SyntoBuilders.TypeOf` had to emit a `typeof(...)` EXPRESSION, not a bare type reference.** The `TypeOf(string)
  : System.Type` facade is consumed in expression position (`GetFieldType` returns `typeof(global::System.Int32)`),
  so a bare `IdentifierName(name)` (a `TypeSyntax`) would emit `return global::System.Int32;` (CS0119). Changed the
  built-in builder to `TypeOfExpression(ParseTypeName(name))` (returns `ExpressionSyntax`). Task 3 only had a
  `Member` functional snapshot; `TypeOf` was first exercised end-to-end here.
- **FieldCount is a degenerate value hole → a separate live `int` parameter.** `columns.Count` cannot lift at
  depth-0 (the bare `columns` reference would bind to the throwing generic `ToSyntax<T>` fallback — an
  `EquatableArray` is not a literal type). Rather than add depth-0 live-VALUE-subexpression lifting, FieldCount
  reads `var fieldCount = Parameter<int>();` and the generator passes `model.Columns.Count` — an `int` lifts via the
  built-in `LiteralSyntaxExtensions`. (Logged as a candidate generalization: lift a maximal purely-live value
  subexpression like `columns.Count` directly, mirroring the island `CollectLiftPoints`.)
- **`Member` inside a live-region island works because builder calls are registered BEFORE region emit.** The
  builder scan rewrites every `Member<…>(_e.Current, c.Name)` facade call into `unquotedReplacements` (keyed at the
  invocation) before `LiveRegionEmitter.Emit` runs, so the per-island quoter (whose lift map is seeded from those
  replacements) returns the precomputed `SyntoBuilders.Member(<quote of _e.Current>, c.Name)` and never descends to
  re-lift `c.Name`. Output carries zero `Synto.*` and zero reflection.

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

## Deep end-review fixes (post-landing)

- **Island lift ignored factory-parameter renaming (CS0103).** `LiveRegionEmitter.CollectLiftPoints` wrapped the
  ORIGINAL purely-live subexpression in `.ToSyntax()` without running the `RootRenameRewriter` that every other
  staging path uses. So a live root whose factory-parameter name differs from the source local — an explicit
  `Parameter<T>("columns")` bound to a local `cols`, or a name uniquified after a collision — referenced BY VALUE
  inside a region island lifted the trimmed source identifier (`cols.Count.ToSyntax()`), and the generated factory
  failed to compile with `CS0103`. The dog-food dodged it (implicit, collision-free names; value refs were the loop
  var `c`, which is in scaffold scope). Fix: rename the lift VALUE (`renamer.Visit(expression)`) while keeping the
  map KEY the original node so the quoter still matches. Pinned by
  `InjectedSurfaceCompletenessTest.RenamedLiveRoot_ReferencedByValueInRegion_Compiles` (post-generation compile).

## Guards + completeness (Task 10)

- **A live *root* in a region's `if` condition trips the nested-region guard.** Writing the injected-surface
  rich template's loop body as `if (i == c.Ordinal + offset)` — where `offset` is a `Live<T>()` local (a real
  classifier root) — reclassifies the `if` from a quoted island into a *live-control* `if` nested inside the
  live `foreach`, which is exactly the deferred nested-region shape → `SY1014`. The fix in the guard test was
  not in the emitter: keep a region's inner `if` driven by a *quoted* value plus the *loop variable* (`i ==
  c.Ordinal`, the ObjectReader shape), and exercise the `Live`/`[Live]` root at depth-0 instead
  (`Console.WriteLine(offset)` before the loop, lifting `offset` to a literal). Worth a future ergonomics pass:
  the diagnostic message could hint that a live local used in a region condition is the trigger.
- **`[Inline(AsSyntax)]` on a generic type parameter does not survive a post-generation *compile*.** A carrier
  shaped `void M<[Inline(AsSyntax=true)] T>(T instance)` lifts `T` to an `ExpressionSyntax T` factory parameter,
  but the same `T` is then emitted as the *type* of the `instance` parameter — `Parameter(..., T, ...)` — where
  the slot wants a `TypeSyntax`, so the generated factory fails to compile (`CS1503: ExpressionSyntax ->
  TypeSyntax`). The existing `SyntaxBuilderTest` only *snapshots* that shape (never compiles it), so it was
  latent. For a body that must reach a value-position syntax splice (a `Member` instance), use a plain
  `[Inline(AsSyntax=true)] object instance` parameter (lifts to `ExpressionSyntax`, used only in value position,
  parameter type emitted as `object`) — the form the compile-asserting guard uses. Candidate generator
  hardening: when an `AsSyntax` generic-type-param symbol is referenced in *type position* (a parameter/local
  declared type), lift a `TypeSyntax` for that use rather than reusing the `ExpressionSyntax` binding (or emit a
  diagnostic), so the two carrier shapes are not silently different at compile time.
- **Caching holds for a staged template.** `StagedTemplate_IsIncrementalOnUnrelatedEdit` (mirror of the depth-0
  `GeneratorIsIncrementalOnUnrelatedEdit`) confirms the whole staging path — live-root discovery, the
  binding-time classifier, region unrolling, the `Member` builder rewrite — runs inside the
  `ForAttributeWithMetadataName` transform and captures no `Compilation`/`SemanticModel`/`SyntaxNode`: every
  tracked step stays `Cached`/`Unchanged` on an unrelated edit. No new seam strain surfaced here.

## `[Quote]` value-marker (quote-value-marker plan)

- **The custom-type FQ parameter-type resolution stayed at the call site, not in `TryEmitValueLift`.** The Task 2
  shared value->syntax helper returns only the lifted expression + an optional diagnostic; it does NOT resolve the
  factory-parameter's *fully-qualified type name*. So both the `[Unquote]` value-param loop and the new `[Quote]`
  param loop independently recompute the FQ type for a custom-type parameter before declaring the factory parameter.
  A minor DRY seam - the helper could grow an `out string parameterType` (or return a small struct) so the lift and
  the parameter-type rendering share one resolution. Left as-is: the value lift and the parameter declaration are
  two distinct concerns and folding them risked widening the behavior-preserving Task 2 refactor.
- **`[Quote]` needed NO `BindingTimeClassifier` change for the parameter form.** Because a `[Quote]` parameter is
  simply never seeded into the classifier's root set (it stays out of `StagedParameterFinder`), a `for`/`foreach`
  driven only by a quoted value classifies `Quoted` automatically - the loop survives with a literal bound. The only
  classifier touch the whole plan needed was the inline `Quote(...)` output-world boundary (Task 4), and even that
  is purely additive (shield a recognized `Quote(...)` invocation's argument from liveness propagation), confirming
  the spec §5 "purely additive, `[Unquote]` unchanged" contract held.

# Staged `if` prune — factory-time branch selection nested inside an unrolled region

**Status:** Design (brainstormed 2026-07-11, accepted shape). Path 1 of a two-part plan; **deferred** — Path 2
(`2026-07-11-getter-expression-builders-design.md`) ships first. This is a generator feature, planned after
Path 2 lands.

**Origin:** the ObjectReader typed getters. Path 2 removes the raw-syntax towers but keeps the getter as a
dense functional arm-projection (`Switch<TRet>(i, columns.Where(...).Select(c => c.IsNullableReference ? … : …))`).
The readable ideal is ordinary control flow — a `foreach` over the columns with the nullable decision as a
plain `if`. This spec is the generator feature that makes that shape compile to correct, prune-clean output.

## 1. Problem

The target authoring shape is imperative and reads like a hand-written getter:

```csharp
[Template]
public TRet TypedGetter<[Unquote] TRet>([Unquote] EquatableArray<ColumnInfo> columns,
    [Unquote] string clrTypeName, [Unquote] string typeLabel, int i)
{
    foreach (var c in columns.Where(c => c.ColumnTypeName == clrTypeName))
    {
        if (i == c.Ordinal)                       // MIXED: runtime `i` + factory-time `c.Ordinal`
        {
            if (c.IsNullableReference)            // PURELY factory-time — must PRUNE, emit nothing itself
                return Coalesce(Member(_e.Current, c.Name),
                    Throw(new InvalidCastException($"Field {i} is null; call IsDBNull first.")));
            return Member(_e.Current, c.Name);
        }
    }
    return Throw(new InvalidCastException($"Field {i} is not {typeLabel} column."));
}
```

**This does not work today (verified by spike, 2026-07-11).** The outer `foreach` unrolls correctly and the
mixed `if (i == c.Ordinal)` correctly stays an output-world `if` (with `c.Ordinal` folded to a literal). But
the nested **purely-staged** `if (c.IsNullableReference)` does **not** prune. It is treated as a quoted island,
its condition folds to a `bool` literal, and **both** branches are emitted:

```csharp
// generated, per nullable column                // per non-nullable column
if (true) return <coalesce…>; return <member…>;  // if (false) return <coalesce…>; return <member…>;
```

`if (true) …; return …;` / `if (false) …; return …;` each leave a dead branch → **CS0162 unreachable** →
build break under global TWAE. The clean shape must prune: emit exactly the taken branch's statements, nothing
else.

## 2. Why it doesn't prune — three findings

From reading `BindingTimeClassifier`, `StagedScaffoldBuilder`, and the spike output:

1. **The classifier never tracks a `foreach` loop variable as live.** `BindingTimeClassifier` seeds `_live`
   from the template roots and propagates only through `var` **initializers**; a `foreach` loop variable (`c`)
   never enters `_live`. So `ReferencesStaged(c.IsNullableReference)` is `false` and the inner `if` is **not**
   classified `StagedControl` — it falls through as a quoted island.

2. **This untracked-loop-var fact is load-bearing.** It is exactly why the outer mixed `if (i == c.Ordinal)`
   stays output-world: `c` is untracked, `i` is a runtime parameter, so the condition references nothing live
   and is emitted as real runtime control flow (with `c.Ordinal` folded at emit time, where the staged set
   *does* include loop vars). Naively adding loop-var tracking with today's `ReferencesStaged` predicate would
   **wrongly** stage the outer `if` too (it references `c`), breaking the canonical unroll.

3. **Nested staged control is hard-bailed regardless.** Even with classification fixed,
   `StagedScaffoldBuilder.TryBuildRegionBody` refuses any region-body statement that *is* or *contains* a
   `StagedControl` node — `SY1014` *"nested staged control region is not supported in v1"*. The prune machinery
   itself already exists (`TryBuildIf`: *"both branches append to the SAME run; exactly one runs at factory
   time"*) but only for a **top-level** staged `if`, never nested inside a `foreach` body.

## 3. Design

Three coordinated changes; the emit machinery is mostly present.

### 3.1 A sharper binding-time predicate — purely-staged vs. mixed conditions

Distinguish two kinds of live-referencing condition:

- **Purely staged** — references ≥1 staged value and **no** output-world local/parameter (e.g.
  `c.IsNullableReference`). Runs at factory time and **prunes**: exactly one branch is selected during the
  unroll.
- **Mixed** — references ≥1 staged value **and** ≥1 output-world value (e.g. `i == c.Ordinal`, mixing runtime
  `i` with factory-time `c.Ordinal`). Stays **output-world** control flow; its staged subexpressions fold at
  emit time (today's behavior, unchanged).

`ClassifyNodes` currently marks an `if` `StagedControl` when its condition `ReferencesStaged` (any staged ref).
Change the `if` arm to require a **purely-staged** condition (staged ∧ no output-world value). The `foreach`/
`for`/`while` driver arms are unaffected (their drivers — the iteration source / loop condition — are the
purely-root live expressions already). This is the predicate `IsPurelyStaged` already encodes for lift points
in `StagedScaffoldBuilder`; the classifier grows the same notion.

### 3.2 Classify-time loop-variable awareness

For the purely-staged determination the classifier must know a `foreach` loop variable is staged when its
iteration source is staged. Seed loop variables of staged `foreach` regions into the classifier's live set
(the emit-time `StagedLivenessAnalysis` already does this; the classifier needs the same knowledge one phase
earlier). Because the `if` predicate is now **purely-staged** (3.1), tracking `c` does **not** re-stage the
mixed outer `if (i == c.Ordinal)` — it still references output-world `i`, so it stays output-world. Finding #2's
hazard is neutralized precisely by the sharper predicate.

### 3.3 Support a nested staged `if` inside a region body

Lift the `SY1014` bail in `TryBuildRegionBody` for the specific, supported case of a nested **staged `if`**:
instead of refusing it, recurse through `TryBuildIf` so the nested `if` becomes part of the factory-time
scaffold — its condition (root-renamed) runs at factory time, and each branch's islands append to the **same**
run as the surrounding region. Exactly one branch executes per iteration, so only the taken branch's statements
are collected. Other nested staged-control shapes (a nested staged `foreach`/`while`) remain unsupported and
keep their diagnostic — this spec adds nested staged **`if`** only.

The resulting factory scaffold for the getter is roughly:

```csharp
var __run_0 = new List<StatementSyntax>();
foreach (var c in columns.Where(c => c.ColumnTypeName == clrTypeName))     // unrolled region
{
    // outer mixed `if (i == c.Ordinal)` is a QUOTED island collected into the run…
    // …and INSIDE that island, the purely-staged `if (c.IsNullableReference)` runs at factory time,
    //    selecting which return-island is quoted — no `if (true/false)` reaches the output.
    __run_0.Add(/* IfStatement(i == <ordinal literal>, <selected return island>) */);
}
```

Concretely: within the quoted island for `if (i == c.Ordinal) { … }`, the nested purely-staged `if` must be
executed during island construction (choosing the guarded vs. plain return quote) rather than quoted as an
output `if`. The implementer determines whether that is best expressed as a factory-time branch in the
island-building scaffold or by hoisting the nested `if` into the region scaffold with the mixed outer `if`
rebuilt around the selected island. The **contract** is: the taken branch's statements are the only ones
emitted; no constant-condition `if` and no dead branch appear in the output.

## 4. Contracts & invariants (binding on the plan)

- **Prune, don't fold:** a purely-staged `if` nested in an unrolled region emits **only** the taken branch's
  statements. No `if (true)`/`if (false)`, no dead branch, no CS0162 in generated output. A generator test
  pins the emitted factory and the final output for both branch values.
- **Mixed conditions unchanged:** `if (i == c.Ordinal)` (and any staged∧output-world condition) still emits as
  output-world control flow with staged subexpressions folded. The `RunCollectTest` canonical snapshot stays
  byte-identical.
- **No regression to top-level staged `if`:** the existing top-level `TryBuildIf` path and its tests are
  unchanged.
- **Unsupported shapes still diagnose:** a nested staged `foreach`/`while`/`for` inside a region body keeps its
  `SY1014` (only nested staged **`if`** is newly supported). A test pins the still-unsupported cases.
- **Cacheability preserved:** classification and staging stay inside the generator transform; no
  `ISymbol`/`SemanticModel`/`SyntaxNode` enters cached pipeline state. Existing cacheability asserts stay green.
- **Dog-food:** `TypedGetter` rewritten to the imperative shape (§1) builds green in
  `Synto.Example.ObjectReader.Generator`, with output semantically equal to the Path 2 functional form and
  `ObjectReaderBehaviorTests` green.

## 5. Alternatives considered

- **Track loop vars with today's `ReferencesStaged` predicate.** Rejected — finding #2: it re-stages the mixed
  outer `if (i == c.Ordinal)` and breaks the canonical unroll. The purely-staged predicate (3.1) is what makes
  loop-var tracking safe.
- **Constant-fold the emitted `if (true/false)` in a post-pass.** Emit the dead-branch shape then delete the
  false branch and unwrap. Rejected — it is a syntactic cleanup bolted onto the wrong representation; the
  branch was a factory-time decision and should never have reached output. Also fragile against nested
  interactions and trivia.
- **Stay functional (Path 2 only) forever.** Legitimate — Path 2 already removes the raw towers. This spec
  exists only if the imperative readability is judged worth a real generator feature; it is deferred, not
  mandatory.

## 6. Scope

One plan, after Path 2: the sharper predicate (3.1), classify-time loop-var awareness (3.2), and nested
staged-`if` support (3.3), plus the `TypedGetter` imperative rewrite as the dog-food. Nested staged
`foreach`/`while`/`for` stay out of scope (keep `SY1014`).

## 7. Open questions (carried to the plan)

- **Island-internal branch vs. scaffold hoist:** whether the nested purely-staged `if` is best executed inside
  the island-building code (selecting which return to quote) or hoisted into the region scaffold with the mixed
  outer `if` rebuilt around the selected island. Determined during TDD against the two-branch snapshot.
- **`else if` / `else` chains:** the getter uses `if … return; return;` (no `else`). Confirm the nested-`if`
  support composes with `TryBuildIf`'s existing `else if` recursion, or scope it to the no-`else` form first.
- **Depth:** only one level of nesting (staged `if` inside `foreach`) is exercised by the dog-food; deeper
  nesting (staged `if` inside staged `if` inside `foreach`) is untested and may warrant its own diagnostic
  until demanded.

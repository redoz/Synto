# Collapse the Middleware Engine into a Per-Node Pipeline Quoter — Implementation Plan

**For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Status:** Builds ON TOP of Scope C (`docs/superpowers/plans/completed/2026-07-03-quote-action-pipeline-scope-c.md`), which landed on `experimental/quote-action-pipeline` and is NOT on `main`. This plan's base MUST be the Scope-C tip — `PlanAssembler`, the step files, and the two pipeline adapters exist only there.

**Design decision (settled — do not re-open):** Scope C left the generic `MiddlewareSyntaxQuoter<TContext>` engine folding 7 seam chains, but its only consumer feeds it exactly two length-1 chains (the `NodePipelineMiddleware` / `ListPipelineMiddleware` adapters) and 5 permanently-empty seams. The engine's per-seam fold always folds a single element that then does the REAL per-node lookup + fold — two identical folds, one nested in the other; the engine is a pass-through. Collapse engine + both adapters into ONE quoter subclass that dispatches per-node natively.

**Goal:** Replace `MiddlewareSyntaxQuoter<TContext>` + `NodePipelineMiddleware` + `ListPipelineMiddleware` with a single `PerNodePipelineQuoter : CSharpSyntaxQuoter` constructed from the same `(nodeSteps, listSteps, includeTrivia)` that `PlanAssembler` already produces — byte-identical output, smaller runtime assembly, one dispatch layer instead of two.

**Architecture:** `PerNodePipelineQuoter` (generator-side, `src/Synto.SourceGenerator/Templating/Quoting/`) overrides exactly the two seams the feature model uses — `Visit(SyntaxNode?)` does the node-keyed step lookup + last-to-first compose with `base.Visit` as terminal; `Visit<TNode>(SyntaxList<TNode>)` does the owner-keyed (`list[0].Parent`) equivalent. The bodies are today's two adapter `Quote` methods, inlined, with `next` replaced by the base call. Step re-entry (`ctx.Quote` / `ctx.ToArrayLiteral`) survives unchanged via a slim nested `QuoteContext` that delegates to `this.Visit` — so the step interfaces, step files, and `PlanAssembler` need zero edits in Unit 1. The 5 dead seam interfaces and the engine then delete cleanly; the step-model types relocate from the runtime assembly to the generator in a final tidy.

**⚠ Constraint reversal (read if you know Scope C):** Scope C's plan said *"Never touch the runtime engine — `MiddlewareSyntaxQuoter.cs`, `QuoteContext.cs`, `QuoteMiddleware.cs` are read-only."* That was risk-avoidance fencing while the step model was being proven. The step model is now landed and green; the engine has no consumer beyond the pass-through adapters. This plan **deletes** the engine and **moves/trims** the other two files. That is the point of the plan, not a violation of it.

**Tech Stack:** C# / Roslyn incremental source generators, `Microsoft.CodeAnalysis.CSharp`. Tests: xUnit under MTP (`dotnet test --project …`), Verify snapshots.

## Global Constraints (apply to every task)

- **Byte-identical generated output at EVERY commit.** Pure internal refactor: `dotnet build Synto.slnx -c Debug` clean, then `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug` all-green with **zero snapshot diffs**. If an UNRELATED snapshot diffs at any gate, STOP and report — never re-accept a snapshot to make a gate pass.
- **Test gate counts:** Units 1 gate = **269/269** (verified current baseline). Units 2–3 gate = **267/267** (269 − 3 deleted engine tests + 1 ported parity test — see Task 3). If the baseline is not 269 when you start, STOP, recount, and adjust both gates by the same delta.
- **Test invocation (MTP/SDK 10):** raw `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug`. NOT via rtk. Filter (if ever needed) via `--filter-method` / `--filter-class`, NEVER `--filter`.
- **jj-native, off `main`.** Base = the `experimental/quote-action-pipeline` tip (Scope C). Work in `@` on an experimental bookmark — either continue on `experimental/quote-action-pipeline` or start `experimental/quote-pipeline-collapse` on top of it (implementer/user picks when parking `@`; the commit blocks below say `<bookmark>` — substitute the chosen name). Per unit: `jj describe -m "<msg>"` → `jj bookmark move <bookmark> --to @` → `jj new`. Never move `main`. No git hooks.
- **Preserve bug #1/#2 behavior** (Splice inside a `[Quoted]` builder arg; nested builder calls), pinned by `SyntaxBuilderTest.SpliceInsideQuotedBuilderArg_InjectsSplicedNode` and `SyntaxBuilderTest.NestedBuilderCalls_ExpandInnerBuilder`. The re-entry path (`ctx.Quote` routing back through full per-node dispatch) is exactly what keeps them fixed — see R1.
- **Contracts, not prescribed code.** Tasks pin type names, member signatures, precedence, behavior, and the delete list; implementers write the bodies (the current engine + adapter sources are the reference implementations — read them before deleting them).

---

## Orientation (read before starting)

Key paths (all verified against the Scope-C tip; `src/SG/` = `src/Synto.SourceGenerator/Templating/`):

**The engine layer being collapsed (runtime assembly, namespace `Synto`, consumed by the generator via IVT — `src/Synto/Synto.csproj:64`):**
- `src/Synto/Quoting/MiddlewareSyntaxQuoter.cs:19` — the engine: 7 seam middleware arrays partitioned by `OfType<>` in the ctor (lines 35–41), one chain-fold override per seam (lines 79–181), private nested `DefaultQuoteContext` (lines 188–200) whose `Quote`/`QuoteList`/`ToArrayLiteral` route back to the engine. **DELETE in Unit 2.**
- `src/Synto/Quoting/QuoteMiddleware.cs` — `IQuoteMiddleware` marker (line 14), Node seam (lines 18–24: `NodeQuoteNext`, `INodeQuoteMiddleware<TContext>`), List seam (lines 26–32: `ListQuoteNext<TNode>`, `IListQuoteMiddleware<TContext>`), then **5 dead seams** (lines 34–72: SeparatedList/Token/TriviaList/TokenList/Kind interfaces + delegates) whose only implementors are the engine's arrays and one engine test. Dead seams DELETE in Unit 2; the Node/List survivors RELOCATE in Unit 3.
- `src/Synto/Quoting/QuoteContext.cs:16` — abstract re-entry surface: `Quote(SyntaxNode?)`, `QuoteList<TNode>(SyntaxList<TNode>)`, `ToArrayLiteral(IEnumerable<ExpressionSyntax>, TypeSyntax)`. Grep-verified: steps call ONLY `ctx.Quote` (`BuilderStep.cs:34-35`, `MemberSegmentStep.cs:51`) and `ctx.ToArrayLiteral` (`InterpolationFoldStep.cs:30`); **`QuoteList` has zero callers** — it dies in Unit 3. RELOCATE + trim in Unit 3.
- `src/Synto/Quoting/CSharpSyntaxQuoter.cs` — the base quoter both old engine and new quoter subclass. `ToArrayLiteral` is `protected static` (line 36); `Visit<TNode>(SyntaxList<TNode>)` is `public virtual` (line 58); `Visit(SyntaxNode?)` comes from `CSharpSyntaxVisitor<ExpressionSyntax>`. NOT touched by this plan.

**The pass-through adapters (their bodies become the new quoter's overrides):**
- `src/SG/Quoting/NodePipeline/NodePipelineMiddleware.cs:22-38` — `Quote`: null/unkeyed → `next(node)`; keyed → compose steps last-to-first with `next` terminal (first step outermost), invoke. **DELETE in Unit 2** (body inlined into `Visit(SyntaxNode?)`).
- `src/SG/Quoting/ListPipeline/ListPipelineMiddleware.cs:33-52` — `Quote<TNode>`: owner = `list.Count > 0 ? list[0].Parent : null`; null/unkeyed → `next(list)`; keyed → compose per call (continuation is generic over `TNode`, so no pre-composition), last-to-first, `next` terminal. Owner-key collision residual is resolved by step self-checks (see its `<remarks>`), NOT by the dispatcher — preserve that division. **DELETE in Unit 2** (body inlined into `Visit<TNode>(SyntaxList<TNode>)`).

**The wiring (Unit 1 touches):**
- `src/SG/Quoting/TemplateQuoterFactory.cs:18` — `Create(nodeSteps, listSteps, includeTrivia)` wraps the dicts in the two adapters and news the engine. Becomes a 1-line factory returning the new quoter (KEPT as the single named wiring point — deletion considered, rejected: three call sites keep zero-edit `Create` calls and one grep-able composition root).
- `src/SG/Quoting/TemplateSyntaxQuoterInvoker.cs:11,14,20` — `_quoter` field, private ctor param, and `TryQuote` param are typed `MiddlewareSyntaxQuoter<QuoteContext>`; the invoker only calls `_quoter.Visit(node)` / `_quoter.Visit(list)` (lines 55, 61, 82, 92, 125, 135, 142). Retype to `CSharpSyntaxQuoter` — virtual dispatch reaches the new overrides.
- Three construction sites (all call `PlanAssembler` then `TemplateQuoterFactory.Create` — **no edits needed**, `Create`'s signature is unchanged): `src/SG/Pipeline/TemplateFactoryBuilder.cs:877` (`QuoteAndAssemble`, main site), `src/SG/Staging/StagedRegionEmitter.cs:68-70` (`fixedQuoter` island), `src/SG/Staging/StagedScaffoldBuilder.cs:165-167` (`islandQuoter` island).
- `src/SG/Pipeline/PlanAssembler.cs:21,65` — produces the two step maps (`Dictionary<SyntaxNode, ImmutableArray<INodeQuoteMiddleware<QuoteContext>>>` / `…IListQuoteMiddleware…`). NOT touched until the Unit-3 type relocation.

**Tests:**
- `test/Synto.Test/Quoting/MiddlewareSyntaxQuoterTest.cs` — 3 engine seam-dispatch tests: (a) `NoMiddleware_QuotesIdenticallyToBase`, (b) `TokenMiddleware_IsInvoked` (Token seam — dies with the seam), (c) `NodeMiddleware_ShortCircuits_LaterMiddlewareNotCalled` (global-chain ordering — superseded by per-node step tests). **DELETE in Unit 2; port (a) only** (empty step maps == plain base quoter is still a real property of the new quoter).
- `test/Synto.Test/Templating/QuoteMiddlewareTest.cs` — 5 per-step behavior tests currently routed `Engine(new NodePipelineMiddleware(map))` / `…ListPipelineMiddleware…`. **REPURPOSE in Unit 2**: construct `new PerNodePipelineQuoter(nodeMap, listMap, includeTrivia: false)` directly; all 5 assertions unchanged.
- Baseline suite: **269/269** (verified 2026-07-03 on the Scope-C tip).

**One genuine design decision — the re-entry surface (recommendation pinned, implementer confirms):**
`BuilderStep` re-enters full dispatch for `[Quoted]`/`[TypeArg]` islands via `ctx.Quote`; `InterpolationFoldStep` hands `ctx.Quote`/`ctx.ToArrayLiteral` as delegates into `InterpolationFold`; `MemberSegmentStep` quotes keyed children via `ctx.Quote`. Re-entry MUST route through the new quoter's overrides (`ctx.Quote(child)` ≡ `this.Visit(child)` on the SAME instance — virtual dispatch, never `base.Visit`), and `ToArrayLiteral` is `protected static` on the base, reachable only from a subclass. Two options:
- **Option A (RECOMMENDED — lowest churn, chosen by this plan):** keep `QuoteContext` and the step `Quote(payload, ctx, next)` signatures exactly as-is; `PerNodePipelineQuoter` holds one instance of a private nested `QuoteContext` implementation (mirror of the engine's `DefaultQuoteContext`, `MiddlewareSyntaxQuoter.cs:188-200`) delegating `Quote`→`this.Visit(node)`, `QuoteList`→`this.Visit(list)`, `ToArrayLiteral`→ the base's static. Zero step-file edits; byte-identical is a structural argument (below).
- Option B (rejected): change steps to receive the quoter directly — touches every step file + interface + test for no output change, and re-couples steps to a concrete quoter type.

**Equivalence argument (why byte-identical):** For every visited node today: `engine.Visit(node)` → single-element node chain → `NodePipelineMiddleware.Quote(node, ctx, BaseVisitNode)` → keyed ? compose(steps, terminal=`BaseVisitNode`) : `BaseVisitNode(node)`. New: `quoter.Visit(node)` → keyed ? compose(steps, terminal=`base.Visit`) : `base.Visit(node)`. The adapter's `next` WAS `BaseVisitNode` (= `base.Visit`) because the engine's chain had exactly one element — so terminal, composition order (last-to-first, first step outermost), and step bodies are all unchanged. Same for the list seam with owner keying. Re-entry: old `ctx.Quote(child)` = `engine.Visit(child)` (public, re-runs the chain); new = `this.Visit(child)` (public override, re-runs the lookup) — identical call graph. The 5 other seams: engine had empty arrays → immediate `base` call; new quoter doesn't override them → direct `base` call. No call sequence differs, so no emitted byte differs.

---

## Unit 1 — `PerNodePipelineQuoter` replaces the engine at all construction sites

End-state: production code no longer constructs or names `MiddlewareSyntaxQuoter<TContext>`; engine + adapters remain in-tree (still test-referenced) but production-dead. Byte-identical, 269/269.

### Task 1: Create `PerNodePipelineQuoter`

**Files:**
- Create: `src/Synto.SourceGenerator/Templating/Quoting/PerNodePipelineQuoter.cs`
- Reference implementations (read, do not modify): `src/Synto/Quoting/MiddlewareSyntaxQuoter.cs` (ctor/`DefaultQuoteContext`/base-call helpers), `src/Synto.SourceGenerator/Templating/Quoting/NodePipeline/NodePipelineMiddleware.cs`, `src/Synto.SourceGenerator/Templating/Quoting/ListPipeline/ListPipelineMiddleware.cs`

**Interfaces:**
- Consumes: `CSharpSyntaxQuoter(bool includeTrivia)` base (IVT from `Synto`), `INodeQuoteMiddleware<QuoteContext>` / `IListQuoteMiddleware<QuoteContext>` / `NodeQuoteNext` / `ListQuoteNext<TNode>` / `QuoteContext` (namespace `Synto`, resolved via outer-namespace lookup — no `using` change needed).
- Produces: `internal sealed class PerNodePipelineQuoter : CSharpSyntaxQuoter` in namespace `Synto.Generators.Templating`, ctor `(IReadOnlyDictionary<SyntaxNode, ImmutableArray<INodeQuoteMiddleware<QuoteContext>>> nodeSteps, IReadOnlyDictionary<SyntaxNode, ImmutableArray<IListQuoteMiddleware<QuoteContext>>> listSteps, bool includeTrivia)`.

**Contract:**
- `public override ExpressionSyntax? Visit(SyntaxNode? node)` — if `node` is non-null and keyed in `nodeSteps`, compose that node's step list **last-to-first** with a `base.Visit`-calling terminal (first step in the list outermost) and invoke on `node`; else `base.Visit(node)`. This is `NodePipelineMiddleware.Quote` (`NodePipelineMiddleware.cs:22-38`) inlined with `next` = base.
- `public override ExpressionSyntax Visit<TNode>(SyntaxList<TNode> list)` — owner = `list.Count > 0 ? list[0].Parent : null`; if owner non-null and keyed in `listSteps`, compose **per call** (the continuation is generic over `TNode`, so no ctor pre-composition — keep the adapter's remark) last-to-first with a `base.Visit`-calling terminal; else `base.Visit(list)`. This is `ListPipelineMiddleware.Quote` (`ListPipelineMiddleware.cs:33-52`) inlined. Carry over BOTH `<remarks>` (per-call composition; owner-key collision residual resolved by step self-checks).
- Re-entry per Option A: one private nested `sealed class` implementing `QuoteContext`, instantiated once (field), delegating `Quote(node)` → `this.Visit(node)`, `QuoteList(list)` → `this.Visit(list)`, `ToArrayLiteral(nodes, elementType)` → the base's `protected static ToArrayLiteral`. Passed as `ctx` to every step invocation. Model on `DefaultQuoteContext` (`MiddlewareSyntaxQuoter.cs:188-200`).
- Implementation gotcha (from the engine, keep it): `base.*` cannot be captured inside a lambda — use tiny private helper methods as the chain terminals, as the engine does (`MiddlewareSyntaxQuoter.cs:70-72`).
- Do NOT override the other 5 seams (`SeparatedSyntaxList`, `SyntaxToken`, `SyntaxTriviaList`, `SyntaxTokenList`, `SyntaxKind`) — base behavior is the pinned behavior.
- XML doc: this is the templating quoter — per-node/per-owner step dispatch over `CSharpSyntaxQuoter`, replacing the dissolved `MiddlewareSyntaxQuoter` engine + two pipeline adapters; feature precedence lives in `PlanAssembler`; steps re-enter via `QuoteContext`.

- [ ] **Step 1: Implement `PerNodePipelineQuoter`** per the contract above.
- [ ] **Step 2: Build** — `dotnet build Synto.slnx -c Debug`. Expected: clean (new type unreferenced yet).

### Task 2: Route the factory + invoker through it (three `Create` sites unchanged)

**Files:**
- Modify: `src/Synto.SourceGenerator/Templating/Quoting/TemplateQuoterFactory.cs`
- Modify: `src/Synto.SourceGenerator/Templating/Quoting/TemplateSyntaxQuoterInvoker.cs:11,14,20`
- No edit (verify only): `src/Synto.SourceGenerator/Templating/Pipeline/TemplateFactoryBuilder.cs:877`, `src/Synto.SourceGenerator/Templating/Staging/StagedRegionEmitter.cs:70`, `src/Synto.SourceGenerator/Templating/Staging/StagedScaffoldBuilder.cs:167`

**Interfaces:**
- Consumes: `PerNodePipelineQuoter` (Task 1).
- Produces: `public static PerNodePipelineQuoter Create(IReadOnlyDictionary<SyntaxNode, ImmutableArray<INodeQuoteMiddleware<QuoteContext>>> nodeSteps, IReadOnlyDictionary<SyntaxNode, ImmutableArray<IListQuoteMiddleware<QuoteContext>>> listSteps, bool includeTrivia)` — same parameters, new return type; `TemplateSyntaxQuoterInvoker.TryQuote(CSharpSyntaxQuoter quoter, TemplateInfo template, out …)`.

**Contract:**
- `Create` body becomes the 1-line wiring point: construct and return `PerNodePipelineQuoter` from the three arguments directly — no adapter wrapping, no `IQuoteMiddleware` list, no empty-count registration checks (the quoter's own unkeyed fast path replaces them). Rewrite the factory's XML doc: still names zero features; now the ONLY place the quoter type is chosen.
- `TemplateSyntaxQuoterInvoker`: `_quoter` field, private ctor param, and `TryQuote` param retype `MiddlewareSyntaxQuoter<QuoteContext>` → `CSharpSyntaxQuoter` (the invoker only needs the `Visit` surface; keeps it decoupled from the concrete quoter). All `_quoter.Visit(...)` calls stay untouched.
- Grep-verify `TemplateQuoterFactory.Create` still has exactly 3 call sites and none needed an edit.
- After this task, production references to `MiddlewareSyntaxQuoter` / the adapters are ZERO (grep `MiddlewareSyntaxQuoter|NodePipelineMiddleware|ListPipelineMiddleware` under `src/` → only their own definition files); tests still reference them — that is expected until Unit 2.

- [ ] **Step 1: Rewrite `Create` + retype the invoker** per the contract.
- [ ] **Step 2: Build** — `dotnet build Synto.slnx -c Debug`. Expected: clean.
- [ ] **Step 3: Test** — `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug`. Expected: **269/269, zero snapshot diffs** (bug #1/#2 `SyntaxBuilderTest` cases, `ChildTemplateTest`, interpolation-fold and decoration snapshots all byte-identical). Unrelated diff → STOP.
- [ ] **Step 4: Commit (Unit 1)**

```bash
jj describe -m "refactor(quoting): PerNodePipelineQuoter subsumes middleware engine at all sites

New CSharpSyntaxQuoter subclass dispatches nodeSteps/listSteps natively
(adapter bodies inlined as Visit overrides; re-entry via nested QuoteContext
delegating to this.Visit). Factory returns it directly; invoker retyped to
CSharpSyntaxQuoter. Engine + adapters now production-dead (tests still
reference them until the next unit). Byte-identical, 269/269."
jj bookmark move <bookmark> --to @
jj new
```

---

## Unit 2 — Delete the engine layer; migrate the tests; recount the gate

End-state: engine file, both adapter files, the 5 dead seam interfaces + delegates, and the engine test file are gone; `QuoteMiddlewareTest` exercises `PerNodePipelineQuoter` directly. Byte-identical, **267/267**.

### Task 3: Repurpose `QuoteMiddlewareTest`; delete `MiddlewareSyntaxQuoterTest`

**Files:**
- Modify: `test/Synto.Test/Templating/QuoteMiddlewareTest.cs`
- Delete: `test/Synto.Test/Quoting/MiddlewareSyntaxQuoterTest.cs`

**Interfaces:**
- Consumes: `PerNodePipelineQuoter(nodeSteps, listSteps, includeTrivia)` (Task 1; internal, visible via `Synto.SourceGenerator` → `Synto.Test` IVT, `Synto.SourceGenerator.csproj:26`).

**Contract:**
- Replace the `Engine(params IQuoteMiddleware[])` + adapter-wrapping plumbing with direct construction: node-map tests build `new PerNodePipelineQuoter(nodeMap, emptyListMap, includeTrivia: false)`, list-map tests the converse. Keep the `NodeMap`/`ListMap` helpers (retargeted to the new ctor's dictionary types).
- All **5 existing assertions unchanged**: `Replacement_ReturnsReplacement_ForKeyedNode`, `Trim_ReturnsNull_ForKeyedNode`, `Decoration_WrapsBaseQuote_InChainedInvocation`, `MemberSegment_EmitsBuildList_WhenChildHasSegment` (owner keying now exercised by the quoter's own `Visit<TNode>` override), `InterpolationFold_DefersToNext_ForNonInterpolatedList` (parentless list → null owner → base parity).
- Port engine test (a) as a 6th test, e.g. `EmptyStepMaps_QuotesIdenticallyToBase`: `PerNodePipelineQuoter` with two empty maps quotes a representative expression identically to `new Core.CSharpSyntaxQuoter(includeTrivia: false)` (keep the `SyntoCore` alias for the base quoter). Engine tests (b) Token-seam and (c) global-chain ordering die with their subjects — (c)'s short-circuit property is already pinned per-node by the Replacement test.
- Do NOT delete the engine/adapters yet (this task's tree must build with both old test file gone and new tests green); update the test class XML doc (now exercises `PerNodePipelineQuoter` dispatch + step behavior; no engine).
- Suite arithmetic: 269 − 3 (engine tests) + 1 (ported parity) = **267**.

- [ ] **Step 1: Rewrite `QuoteMiddlewareTest` (5 retargeted + 1 ported) and delete `MiddlewareSyntaxQuoterTest.cs`.**
- [ ] **Step 2: Test** — `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug`. Expected: **267/267, zero snapshot diffs**.

### Task 4: Delete the engine, the two adapters, and the 5 dead seams

**Files:**
- Delete: `src/Synto/Quoting/MiddlewareSyntaxQuoter.cs`
- Delete: `src/Synto.SourceGenerator/Templating/Quoting/NodePipeline/NodePipelineMiddleware.cs`
- Delete: `src/Synto.SourceGenerator/Templating/Quoting/ListPipeline/ListPipelineMiddleware.cs`
- Modify: `src/Synto/Quoting/QuoteMiddleware.cs` — delete lines 34–72 (SeparatedList/Token/TriviaList/TokenList/Kind interfaces + `…Next` delegates) and rewrite the file-header comment (no more "7 seams"; it now declares the two step seams of the per-node pipeline). Keep `IQuoteMiddleware`, `NodeQuoteNext`, `INodeQuoteMiddleware<TContext>`, `ListQuoteNext<TNode>`, `IListQuoteMiddleware<TContext>` — relocation is Unit 3, not here.
- Modify: `src/Synto/Quoting/QuoteContext.cs` — doc only: drop the `MiddlewareSyntaxQuoter{TContext}` `<see cref>` (it must not dangle); surface stays intact until Unit 3.

**Interfaces:**
- Consumes: nothing new. Produces: absence — grep contracts below.

**Contract:**
- Grep-verify BEFORE deleting: `MiddlewareSyntaxQuoter`, `NodePipelineMiddleware`, `ListPipelineMiddleware` each match only their own definition file (+ docs/plans, which don't compile) — Tasks 2–3 made this true.
- Grep-verify AFTER deleting: `ISeparatedListQuoteMiddleware|ITokenQuoteMiddleware|ITriviaListQuoteMiddleware|ITokenListQuoteMiddleware|IKindQuoteMiddleware|SeparatedListQuoteNext|TokenQuoteNext|TriviaListQuoteNext|TokenListQuoteNext|KindQuoteNext` → zero matches in `*.cs`.
- `DefaultQuoteContext` and the engine's `CreateContext` virtual die with the engine file (both private/engine-only — nothing else to update).

- [ ] **Step 1: Grep the three type names; delete the three files; trim `QuoteMiddleware.cs`; fix the `QuoteContext.cs` doc.**
- [ ] **Step 2: Build** — `dotnet build Synto.slnx -c Debug`. Expected: clean.
- [ ] **Step 3: Test** — `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug`. Expected: **267/267, zero snapshot diffs**.
- [ ] **Step 4: Commit (Unit 2)**

```bash
jj describe -m "refactor(quoting): delete middleware engine, adapters, 5 dead seams

MiddlewareSyntaxQuoter + NodePipelineMiddleware + ListPipelineMiddleware
deleted (subsumed by PerNodePipelineQuoter); SeparatedList/Token/TriviaList/
TokenList/Kind seam interfaces+delegates deleted (never had a consumer).
QuoteMiddlewareTest retargeted to the quoter (5 kept + 1 ported parity);
engine test file deleted. Byte-identical, 267/267."
jj bookmark move <bookmark> --to @
jj new
```

---

## Unit 3 — Relocate the step model into the generator; tidy stale docs

End-state: `src/Synto/Quoting/` carries no step-model types (runtime assembly ships none of this machinery); the step contracts live beside their implementations; dead surface (`QuoteList`, `IQuoteMiddleware` marker, `TContext` generic) is gone; stale docstrings refreshed. Byte-identical, **267/267**.

### Task 5: Move + simplify `QuoteContext` and the step interfaces

**Files:**
- Delete: `src/Synto/Quoting/QuoteMiddleware.cs`, `src/Synto/Quoting/QuoteContext.cs` (contents move)
- Create: `src/Synto.SourceGenerator/Templating/Quoting/QuoteStep.cs` (the two step interfaces + two delegates), `src/Synto.SourceGenerator/Templating/Quoting/QuoteContext.cs` — both namespace `Synto.Generators.Templating`
- Modify (mechanical, generic-arg removal only): `src/Synto.SourceGenerator/Templating/Quoting/PerNodePipelineQuoter.cs`, `TemplateQuoterFactory.cs`, `src/Synto.SourceGenerator/Templating/Pipeline/PlanAssembler.cs`, the 6 step files under `Quoting/NodePipeline/` + `Quoting/ListPipeline/`, `test/Synto.Test/Templating/QuoteMiddlewareTest.cs` (drop `Core.`-qualified step-model refs; `SyntoCore` alias stays for `CSharpSyntaxQuoter` parity tests)

**Interfaces:**
- Produces (the pinned end-state surface, all `internal`, namespace `Synto.Generators.Templating`):
  - `delegate ExpressionSyntax? NodeQuoteNext(SyntaxNode? node)`
  - `interface INodeQuoteMiddleware { ExpressionSyntax? Quote(SyntaxNode? node, QuoteContext ctx, NodeQuoteNext next); }`
  - `delegate ExpressionSyntax ListQuoteNext<TNode>(SyntaxList<TNode> list) where TNode : SyntaxNode`
  - `interface IListQuoteMiddleware { ExpressionSyntax Quote<TNode>(SyntaxList<TNode> list, QuoteContext ctx, ListQuoteNext<TNode> next) where TNode : SyntaxNode; }`
  - `abstract class QuoteContext { Quote(SyntaxNode?); ToArrayLiteral(IEnumerable<ExpressionSyntax>, TypeSyntax); }`

**Contract:**
- Relocation rationale (pin): the step model only serves the generator-internal pipeline now; the shipped runtime assembly should not carry it. The GA "public-shaped engine" framing died with the engine — these are internal step contracts, full stop. IVT direction is unaffected (`Synto.SourceGenerator` already has IVT to `Synto.Test`).
- Three simplifications ride the move (each grep-justified, none behavioral):
  1. **Delete `IQuoteMiddleware` marker** — its only purpose was the engine's `OfType<>` seam partitioning, which no longer exists. The two interfaces stand alone.
  2. **De-genericize `TContext`** — `INodeQuoteMiddleware<TContext> where TContext : QuoteContext` → `INodeQuoteMiddleware` taking `QuoteContext` directly (same for List). The generic existed for the public-engine "richer context" story; every declaration in the tree instantiates it at exactly `QuoteContext`. Purely mechanical: `INodeQuoteMiddleware<QuoteContext>` → `INodeQuoteMiddleware` everywhere (quoter fields, factory signature, `PlanAssembler` map types, step declarations, test helpers).
  3. **Delete `QuoteContext.QuoteList<TNode>`** — grep-verified zero callers (steps use only `Quote` and `ToArrayLiteral`); remove the abstract member and the nested implementation in `PerNodePipelineQuoter`. Re-grep `\.QuoteList\(` before deleting; if a caller appeared since, keep it and note why.
- Same-namespace resolution means the step files and `PlanAssembler` need NO `using` edits — only the generic-arg removals above. The old types resolved from namespace `Synto` via outer-namespace lookup; the new ones are in the files' own namespace.
- `src/Synto/Quoting/` afterward contains ONLY the base-quoter family and its helpers (`CSharpSyntaxQuoter*.cs`, `QuoteSyntaxExtensions.cs`, …) — nothing pipeline-shaped.

- [ ] **Step 1: Move the two files, apply the three simplifications, sweep the generic-arg removals.**
- [ ] **Step 2: Build** — `dotnet build Synto.slnx -c Debug`. Expected: clean.
- [ ] **Step 3: Test** — `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug`. Expected: **267/267, zero snapshot diffs**.

### Task 6: Stale-doc sweep

**Files:**
- Modify: `src/Synto.SourceGenerator/Templating/Pipeline/QuotePlan.cs:8-13` — class doc still claims each field is "1:1 the argument list of `TemplateQuoterFactory.Create`"; `Create` now takes step maps. Re-doc: QuotePlan fields are the plan-phase channels `PlanAssembler` folds into the step maps.
- Modify: `src/Synto.SourceGenerator/Templating/Staging/StagedRegionEmitter.cs:15` — `<see cref="MiddlewareSyntaxQuoter{TContext}"/>` dangles; repoint to `PerNodePipelineQuoter`.
- Modify: `test/Synto.Test/Templating/DecorationTests.cs:58` — comment names "the MiddlewareSyntaxQuoter engine"; reword to the per-node quoter.
- Verify (fix if stale, else leave): step-file docs that narrate "the engine's base quote via next" (`NodePipeline/*.cs`, `ListPipeline/*.cs`) still read correctly — "next" now terminates in the quoter's own base call; reword only where a sentence is now false.

**Interfaces:** none (comments only — MUST NOT change any code semantics; the diff for this task is comment-only).

- [ ] **Step 1: Apply the doc fixes; grep `MiddlewareSyntaxQuoter` under `src/` + `test/` → zero matches.**
- [ ] **Step 2: Build + test** — clean, **267/267, zero snapshot diffs**.
- [ ] **Step 3: Commit (Unit 3)**

```bash
jj describe -m "refactor(quoting): step model moves generator-side; drop dead surface

QuoteContext + INodeQuoteMiddleware/IListQuoteMiddleware relocate from the
Synto runtime to Synto.SourceGenerator (namespace Synto.Generators.Templating);
IQuoteMiddleware marker, TContext generic, and unreferenced QuoteContext.QuoteList
deleted. Stale engine references in docs/comments refreshed. Runtime Quoting/
now carries only the base quoter family. Byte-identical, 267/267."
jj bookmark move <bookmark> --to @
jj new
```

---

## Final verification (after Unit 3 commit)

- [ ] Grep `MiddlewareSyntaxQuoter|NodePipelineMiddleware|ListPipelineMiddleware|IQuoteMiddleware` over `src/` + `test/` → zero `*.cs` matches (docs/kb and archived plans/specs may still mention them — that is the refresh-kb note below, not a failure).
- [ ] Grep the 5 dead seam names (Task 4 pattern) → zero `*.cs` matches.
- [ ] `src/Synto/Quoting/` contains no `QuoteContext.cs` / `QuoteMiddleware.cs` / `MiddlewareSyntaxQuoter.cs`; `jj diff --from <base> -- src/Synto/Quoting/` shows only the three deletions/moves, nothing touching `CSharpSyntaxQuoter*`.
- [ ] `TemplateQuoterFactory.Create` still has exactly 3 call sites, each fed by `PlanAssembler`; the factory still names zero features.
- [ ] Precedence still appears exactly once, hand-written, in `PlanAssembler` (Node: Replace → Builder → Trim → Decorate; List: InterpolationFold → MemberSegment) — this plan never touched it.
- [ ] Bug #1/#2 tests green by name: `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug --filter-class Synto.Test.SyntaxBuilderTest` (adjust the class name to its namespace if needed).
- [ ] Spot-check one feature-dense generated snapshot (e.g. a `ChildTemplateTest` or ObjectReader snapshot) is byte-identical to the base revision's copy.
- [ ] `<bookmark>` sits on the Unit-3 commit; `main` unmoved.

## Risk register

- **R1 (highest): re-entry must stay virtual.** `ctx.Quote(child)` must reach the OVERRIDDEN `Visit` on the same quoter instance (`this.Visit`), never `base.Visit` — the nested context delegating to `base` would skip per-node dispatch for nested builder calls and splice-in-builder-arg islands, breaking bug #1/#2. Pinned by `SyntaxBuilderTest.SpliceInsideQuotedBuilderArg_InjectsSplicedNode` / `NestedBuilderCalls_ExpandInnerBuilder` at every gate.
- **R2: `base.*` in lambdas.** The chain terminals must be helper methods (engine pattern, `MiddlewareSyntaxQuoter.cs:70-72`); capturing `base.Visit` in a lambda does not compile. Cheap: build catches it.
- **R3: owner-keying drift.** None expected — the list override is the adapter body verbatim; step self-checks (not the dispatcher) still resolve the owner-key collision residual. If a snapshot diffs, the inlining was NOT verbatim — diff against the deleted adapter.
- **R4: gate-count drift.** If the pre-plan suite is not 269, or Task 3's arithmetic lands off 267, STOP and recount before commit — never bend a gate to the observed number without understanding the delta.
- **R5: unrelated snapshot diff at any gate** → STOP and report (possible latent pre-existing issue); never re-accept.
- **R6: Unit 3 mechanical sweep breaks a signature silently.** The de-genericize is a rename with no overload surface; the compiler is the guard. Keep Unit 3 in its own commit so it can be dropped/reworked without touching Units 1–2.

## After landing

- Run the `refresh-kb` skill: `docs/kb/subsystems/quoting.md:38-39` and `docs/kb/subsystems/templating.md:73` map the engine at `src/Synto/Quoting/MiddlewareSyntaxQuoter.cs`, which no longer exists; the step-model paths also moved.
- This work extends the not-yet-merged Scope-C stack — fold it into the existing review + merge decision for `experimental/quote-action-pipeline` rather than opening a separate one.

# Quote Action Pipeline (Scope C) — Step Pipelines Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **Status:** SUPERSEDES `docs/superpowers/plans/2026-07-02-quote-action-pipeline.md` for the remaining Scope-C work (that plan's Stage 1 + Stage 2 Task 6 are landed; its Stage 3 was reverted; its Tasks 7/9 are subsumed by the design below — see "Subsumed prior tasks" in the spec).

**Spec (the brief — read first):** `docs/superpowers/specs/2026-07-03-quote-action-pipeline-scope-c-design.md`

**Goal:** Make the Transform (quote phase) name zero features: the Plan attaches ordered *steps* to nodes/lists, two generic pipeline middleware run them, and `TemplateQuoterFactory.Create` shrinks to `Create(nodeSteps, listSteps, includeTrivia)`.

**Architecture:** Both quoter seams reduce to one discipline — *look the visited thing up in a plan map keyed by `SyntaxNode`, run its ordered step chain (terminal = base quote via `next`), else defer.* `NodePipelineMiddleware` (revived from git `361c9a6b`) dispatches per-node steps (`ReplaceStep → BuilderStep → TrimStep → DecorateStep`); a new mirror `ListPipelineMiddleware` dispatches owner-keyed list steps (`InterpolationFoldStep → MemberSegmentStep`). A plan-stage assembler owns the channel→steps fold with hand-written precedence and is shared by all three quoter `Create` sites. The runtime engine is untouched — steps compose *as* existing `INodeQuoteMiddleware<QuoteContext>` / `IListQuoteMiddleware<QuoteContext>`.

**Tech Stack:** C# / Roslyn incremental source generators, `Microsoft.CodeAnalysis.CSharp`. Tests: MTP (`dotnet test --project ...`), Verify snapshot suite, xunit.

## Global Constraints

- **Byte-identical output is the bar at EVERY commit.** Each unit ends: `dotnet build Synto.slnx -c Debug` clean, then `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug` = **269/269 pass, zero snapshot diffs**. If an UNRELATED snapshot diffs, **STOP and report** — do not re-accept. Watch `test/Synto.Test/Templating/ChildTemplateTest.cs` in particular (builder-call inside a live region — the ObjectReader child-template dog-food).
- **Never touch the runtime engine:** `src/Synto/Quoting/MiddlewareSyntaxQuoter.cs`, `src/Synto/Quoting/QuoteContext.cs`, `src/Synto/Quoting/QuoteMiddleware.cs` are read-only for this work.
- **Never rewrite the input tree.** All patches stay side-plan, keyed by original-node reference identity (that identity is what makes the step dictionaries valid).
- **Precedence is hand-written, never derived.** Node steps: Replace → Builder → Trim → Decorate. List steps: InterpolationFold → MemberSegment. No topo-sort, no ranks. Across different nodes order is irrelevant (keyed separately).
- **Out of scope:** fold-native pruning (keep `BranchPruneIdentifier` as-is), SeparatedList/Token/any other seam conversion, `NameAllocator` refactor (name minting is byte-sensitive), multi-pass tree rewrite.
- **Preserve bug #1/#2 behavior** (Splice inside a `[Quoted]` builder arg; nested builder calls) — pinned by `SyntaxBuilderTest.SpliceInsideQuotedBuilderArg_InjectsSplicedNode` and `SyntaxBuilderTest.NestedBuilderCalls_ExpandInnerBuilder`. Keep `StagedEmitContext.BuilderCalls` threading into the live-region island quoters.
- **Test invocation (MTP/SDK 10):** raw `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug`. No `-nologo`. NOT via rtk. Filter (if ever needed) via `--filter-method` / `--filter-class`, NOT `--filter`.
- **jj-native, off `main`.** All work in `@` on top of bookmark `experimental/quote-action-pipeline`; per unit: `jj describe -m "<msg>"` then `jj bookmark move experimental/quote-action-pipeline --to @` then `jj new`. Never move `main`. No git hooks.
- **Contracts, not prescribed code.** Tasks below pin type names, member signatures, precedence, and behavior; implementers write the bodies (the reverted artifact and the current middleware are the reference implementations).

---

## Orientation (read before starting)

Key paths (all under `src/Synto.SourceGenerator/Templating/` unless noted):

- `Quoting/TemplateQuoterFactory.cs` — the composition root that shrinks. Today it registers six feature middleware over seven optional channels; end-state it wires two step maps into the engine and names no feature.
- `Quoting/Middleware/` — the six feature middleware being dissolved into steps: `ReplacementMiddleware`, `BuilderCallMiddleware`, `TrimMiddleware`, `DecorationMiddleware` (Node seam), `InterpolationFoldMiddleware`, `MemberSegmentMiddleware` (List seam). They remain (unregistered but test-exercised) until Unit 3 retires them.
- `Quoting/InterpolationFold.cs` — the fold subsystem; NOT dissolved, the `InterpolationFoldStep` keeps delegating to it. Its `TryFoldInterpolatedContents` self-defers (returns false) when nothing folds — this self-check is what makes owner-key over-approximation safe.
- `Pipeline/QuotePlan.cs` — the six channels: `Replacements`, `Trim`, `BuilderCalls`, `StringRoots`, `MemberSegments`, `DecorationHooks`.
- `Pipeline/TemplateFactoryBuilder.cs` — `QuoteAndAssemble` (~line 875) is the main `Create` site (expands `Trim` via `BranchPruneIdentifier` first).
- `Staging/StagedRegionEmitter.cs:68` and `Staging/StagedScaffoldBuilder.cs:165` — the two live-region island `Create` sites (fixed siblings / lift islands). They build quoters from PARTIAL channels (replacements/liftMap + `stringStagedRoots` + `builderCalls`; empty trim; no decorations/segments) and must inherit the step model in Unit 3.
- Engine (READ-ONLY): `src/Synto/Quoting/QuoteMiddleware.cs` — `INodeQuoteMiddleware<TContext>.Quote(SyntaxNode?, TContext, NodeQuoteNext)`, `IListQuoteMiddleware<TContext>.Quote<TNode>(SyntaxList<TNode>, TContext, ListQuoteNext<TNode>)`. The engine composes each seam's registered middleware last-to-first with base as terminal — first registered is outermost. The pipeline middleware reproduce exactly that composition for their per-key step lists.
- Tests: `test/Synto.Test/Templating/QuoteMiddlewareTest.cs` — 5 per-middleware behavior tests (migrate in Unit 3, count must stay 5). Baseline suite: **269/269**.
- The reverted artifact: `git show 361c9a6b` — `NodePipelineMiddleware`, `ReplaceStep`, `TrimStep`, `DecorateStep` under `Quoting/NodePipeline/` + a `BuildNodeSteps` fold in the factory. Unit 1 revives the four step files **verbatim** and re-does the fold by hand (the commit predates the `BuilderCalls` channel).

**Byte-identical equivalence argument (why this is safe):** a node's (or owner's) step list is exactly the subsequence of today's global seam chain whose membership includes that key, in the same relative order. The pipeline middleware compose that subsequence with the identical last-to-first discipline the engine uses. Keys never collide across features into a different order because precedence is assembled in one hand-written fold.

---

## Unit 1 — Node seam as steps + Builder reconcile

End-state: factory registers ONE `NodePipelineMiddleware` instead of the four global Node middleware; list seam unchanged; `Create` signature unchanged; fold lives IN the factory for now. Byte-identical.

### Task 1: Revive the four NodePipeline files from git `361c9a6b`

**Files:**
- Create (restore): `src/Synto.SourceGenerator/Templating/Quoting/NodePipeline/NodePipelineMiddleware.cs`
- Create (restore): `src/Synto.SourceGenerator/Templating/Quoting/NodePipeline/ReplaceStep.cs`
- Create (restore): `src/Synto.SourceGenerator/Templating/Quoting/NodePipeline/TrimStep.cs`
- Create (restore): `src/Synto.SourceGenerator/Templating/Quoting/NodePipeline/DecorateStep.cs`

**Interfaces (as restored — verbatim from the commit, namespace `Synto.Generators.Templating`):**
- `internal sealed class NodePipelineMiddleware : INodeQuoteMiddleware<QuoteContext>` — ctor takes `IReadOnlyDictionary<SyntaxNode, ImmutableArray<INodeQuoteMiddleware<QuoteContext>>> steps`. `Quote`: unkeyed/null node → `next(node)`; keyed node → compose its step list last-to-first over `next` (first step outermost) and invoke.
- `internal sealed class ReplaceStep : INodeQuoteMiddleware<QuoteContext>` — ctor takes the single `ExpressionSyntax` replacement; `Quote` returns it unconditionally (terminal, never calls `next`).
- `internal sealed class TrimStep : INodeQuoteMiddleware<QuoteContext>` — stateless singleton `TrimStep.Instance`; `Quote` returns `null` (terminal drop).
- `internal sealed class DecorateStep : INodeQuoteMiddleware<QuoteContext>` — ctor takes `ImmutableArray<AppliedDecoration>`; `Quote` calls `next(node)` then folds each hook as a chained `result.<HelperMethodName>(args…)` invocation (post/wrap; null base result passes through as null).

- [ ] **Step 1: Restore the directory from the reverted commit** (do NOT restore that commit's `TemplateQuoterFactory.cs` — it predates the `BuilderCalls` channel):

```bash
git restore --source=361c9a6b -- src/Synto.SourceGenerator/Templating/Quoting/NodePipeline/
```

(If `git restore` misbehaves in the colocated repo, fall back to `git show 361c9a6b:<path> > <path>` per file after `mkdir`.)

- [ ] **Step 2: Verify the four files match the commit exactly** — `git diff 361c9a6b -- src/Synto.SourceGenerator/Templating/Quoting/NodePipeline/` shows no diff.
- [ ] **Step 3: Build** — `dotnet build Synto.slnx -c Debug`. Expected: clean (new types unreferenced yet).

### Task 2: Add `BuilderStep` (per-node re-cast of `BuilderCallMiddleware`)

**Files:**
- Create: `src/Synto.SourceGenerator/Templating/Quoting/NodePipeline/BuilderStep.cs`
- Reference implementation (read, do not modify yet): `src/Synto.SourceGenerator/Templating/Quoting/Middleware/BuilderCallMiddleware.cs`

**Interfaces:**
- Produces: `internal sealed class BuilderStep : INodeQuoteMiddleware<QuoteContext>` — ctor takes the single resolved `BuilderCall` for its node (model: `Templating/Builders/BuilderCallModel.cs`).

**Contract / behavior (copy the assembly semantics of `BuilderCallMiddleware.Quote`, minus the dictionary lookup):**
- Terminal: assembles and returns the fully-qualified `ParseName(call.BuilderFullyQualifiedTypeName).<BuilderMethodName>(args…)` invocation; **never calls `next`**.
- Per arg binding: `BuilderArgKind.Quoted` → `ctx.Quote(binding.ValueArgument!)` (full-pipeline **re-entry** — this is what makes bug #1/#2 stay fixed: nested builder calls expand, `Splice` inside a `[Quoted]` arg injects); `BuilderArgKind.QuotedTypeArg` → `ctx.Quote(binding.TypeArgument!)`; anything else → `binding.ValueArgument!.WithoutTrivia()` (verbatim).
- Re-entry is on arg subtrees, never the invocation node itself — no self-recursion.
- XML-doc it as the per-node counterpart of the dissolved `BuilderCallMiddleware`, noting the re-entrant island quoting.

- [ ] **Step 1: Implement `BuilderStep`** per the contract above.
- [ ] **Step 2: Build** — clean (still unreferenced).

### Task 3: Factory folds the four node channels into the node-step map

**Files:**
- Modify: `src/Synto.SourceGenerator/Templating/Quoting/TemplateQuoterFactory.cs`

**Interfaces:**
- Consumes: `NodePipelineMiddleware`, `ReplaceStep`, `BuilderStep`, `TrimStep.Instance`, `DecorateStep` (Tasks 1-2).
- Produces: `Create` — **signature unchanged** (all seven channel parameters stay) — plus a private static fold:
  `Dictionary<SyntaxNode, ImmutableArray<INodeQuoteMiddleware<QuoteContext>>> BuildNodeSteps(replacements, builderCalls, trimNodes, decorationHooks)`.

**Contract:**
- `BuildNodeSteps` accumulates one ordered step list per touched node in fixed precedence **Replace → Builder → Trim → Decorate**: each `replacements` entry appends `new ReplaceStep(value)`; each `builderCalls` entry appends `new BuilderStep(call)`; each `trimNodes` member appends `TrimStep.Instance`; each `decorationHooks` entry appends `new DecorateStep(hooks)`. Iterate the channels in that precedence order (append order = chain order; the fold in `git show 361c9a6b` is the reference shape — extend it with the Builder pass between Replace and Trim).
- `Create` replaces the four Node-seam registrations (`ReplacementMiddleware`, `BuilderCallMiddleware`, `TrimMiddleware`, `DecorationMiddleware`) with: build `nodeSteps`; if non-empty, register ONE `new NodePipelineMiddleware(nodeSteps)`. Empty map → no middleware (engine fast path intact).
- List seam registrations (`InterpolationFoldMiddleware`, `MemberSegmentMiddleware`) are **unchanged** this unit.
- The four old Node middleware classes REMAIN in the tree (unregistered; still exercised by `QuoteMiddlewareTest`) until Unit 3.
- Update the factory's XML doc: Node seam is now a per-node step pipeline (Replace → Builder → Trim → Decorate); List seam still two feature middleware.
- The two island `Create` sites (`StagedRegionEmitter.cs:68`, `StagedScaffoldBuilder.cs:165`) need NO edit — same signature, `builderCalls` threading via `StagedEmitContext.BuilderCalls` preserved automatically.

- [ ] **Step 1: Implement the fold + registration swap** in `TemplateQuoterFactory.Create`.
- [ ] **Step 2: Build** — `dotnet build Synto.slnx -c Debug`. Expected: clean.
- [ ] **Step 3: Test** — `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug`. Expected: **269/269 pass, zero snapshot diffs** (explicitly confirm `SyntaxBuilderTest.SpliceInsideQuotedBuilderArg_InjectsSplicedNode`, `SyntaxBuilderTest.NestedBuilderCalls_ExpandInnerBuilder`, and all of `ChildTemplateTest` pass). If any UNRELATED snapshot diffs: STOP, report, do not re-accept.
- [ ] **Step 4: Commit (Unit 1)**

```bash
jj describe -m "refactor(templating): node seam as per-node step pipeline (Scope C unit 1)

Revive NodePipelineMiddleware + Replace/Trim/Decorate steps (git 361c9a6b) and add
BuilderStep (re-entrant, from BuilderCallMiddleware, bug #1/#2 behavior preserved).
TemplateQuoterFactory folds the four node channels into one per-node step map,
precedence Replace -> Builder -> Trim -> Decorate. List seam + Create signature
unchanged; old middleware remain test-exercised. Byte-identical, 269/269."
jj bookmark move experimental/quote-action-pipeline --to @
jj new
```

---

## Unit 2 — List seam as steps

End-state: factory registers ONE `ListPipelineMiddleware` (owner-keyed) instead of the two global List middleware; `Create` signature still unchanged; fold still in the factory. Byte-identical.

### Task 4: Add `ListPipelineMiddleware` (owner-keyed mirror of `NodePipelineMiddleware`)

**Files:**
- Create: `src/Synto.SourceGenerator/Templating/Quoting/ListPipeline/ListPipelineMiddleware.cs`
- Mirror (read): `src/Synto.SourceGenerator/Templating/Quoting/NodePipeline/NodePipelineMiddleware.cs`

**Interfaces:**
- Produces: `internal sealed class ListPipelineMiddleware : IListQuoteMiddleware<QuoteContext>` — ctor takes `IReadOnlyDictionary<SyntaxNode, ImmutableArray<IListQuoteMiddleware<QuoteContext>>> steps`.

**Contract:**
- Lookup key = the visited list's **owner node**: `list.Count > 0 ? list[0].Parent : null`. Null owner or unkeyed owner → `next(list)`.
- Keyed owner → compose the owner's step chain last-to-first with `next` as terminal (first step in list outermost), same discipline as `NodePipelineMiddleware` — but composed **per call inside the generic `Quote<TNode>`** (the continuation is the generic `ListQuoteNext<TNode>`, so no pre-composition is possible; note this in the XML doc).
- XML-doc the **owner-key collision residual**: one owner (e.g. a `TypeDeclarationSyntax`) owns several lists (attributes, base types, members) that all share it as `.Parent`; steps found for a non-target list must self-check and defer — the steps already do (see Task 5), so the pipeline stays generic and feature-blind.

- [ ] **Step 1: Implement `ListPipelineMiddleware`** per the contract.
- [ ] **Step 2: Build** — clean (unreferenced).

### Task 5: Re-cast the two List middleware as steps

**Files:**
- Create: `src/Synto.SourceGenerator/Templating/Quoting/ListPipeline/InterpolationFoldStep.cs` (from `Quoting/Middleware/InterpolationFoldMiddleware.cs`)
- Create: `src/Synto.SourceGenerator/Templating/Quoting/ListPipeline/MemberSegmentStep.cs` (from `Quoting/Middleware/MemberSegmentMiddleware.cs`)

**Interfaces:**
- Produces: `internal sealed class InterpolationFoldStep : IListQuoteMiddleware<QuoteContext>` — ctor `(IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> stringStagedRoots)`; `internal sealed class MemberSegmentStep : IListQuoteMiddleware<QuoteContext>` — ctor `(IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> memberSegments)`.

**Contract:**
- **Structurally identical** to the middleware they re-cast (they already implement `IListQuoteMiddleware<QuoteContext>`): body copied, class renamed, XML doc updated to say "owner-keyed List-pipeline step; dispatched by `ListPipelineMiddleware`".
- Each step KEEPS its self-checks and carries the FULL channel map — this is load-bearing:
  - `InterpolationFoldStep` keeps the `typeof(TNode) == typeof(InterpolatedStringContentSyntax)` guard and delegates to `InterpolationFold.TryFoldInterpolatedContents` (which self-defers for verbatim/raw strings and no-foldable-hole lists). The generic-over-`TNode` cast stays sealed inside the step.
  - `MemberSegmentStep` keeps its scan-children-for-keys check (`hasSegment`) and defers via `next` when the visited list has no keyed generator child — this self-check IS the collision-residual resolution (a type decl's attribute/base lists reach the step and fall through).
- Old `InterpolationFoldMiddleware` / `MemberSegmentMiddleware` classes REMAIN (unregistered after Task 6; still test-exercised) until Unit 3.

- [ ] **Step 1: Create both steps** as renamed copies with updated docs.
- [ ] **Step 2: Build** — clean.

### Task 6: Factory folds the two list channels (re-keyed to owner nodes) into the list-step map

**Files:**
- Modify: `src/Synto.SourceGenerator/Templating/Quoting/TemplateQuoterFactory.cs`

**Interfaces:**
- Consumes: `ListPipelineMiddleware`, `InterpolationFoldStep`, `MemberSegmentStep` (Tasks 4-5).
- Produces: private static fold `Dictionary<SyntaxNode, ImmutableArray<IListQuoteMiddleware<QuoteContext>>> BuildListSteps(stringStagedRoots, memberSegments)`; `Create` signature still unchanged.

**Contract (owner re-keying — the only new logic this unit):**
- `StringRoots` channel: keys are staged-string REFERENCE nodes. Owner = `key.FirstAncestorOrSelf<InterpolatedStringExpressionSyntax>()`; keys with no such ancestor contribute nothing. Each distinct owner gets ONE `new InterpolationFoldStep(stringStagedRoots)` (the full map — the fold subsystem does per-hole lookup). This owner set is a safe **over-approximation**: any keyed owner where nothing actually folds self-defers inside the step (exactly what the global middleware did on every interpolated string), so output is unchanged.
- `MemberSegments` channel: keys are `[Splice]` generator method declaration nodes. Owner = `key.Parent` (the enclosing `TypeDeclarationSyntax` — non-null by construction; skip defensively if null). Each distinct owner gets ONE `new MemberSegmentStep(memberSegments)` (the full map — the step does per-child lookup). Multiple generators in one type → still one step (dedupe owners).
- Precedence within an owner: **InterpolationFold → MemberSegment**, hand-written in the fold's iteration order (no owner has both today — different owner node types — but encode the order anyway for uniformity).
- `Create` replaces the two List-seam registrations with: build `listSteps`; if non-empty, register ONE `new ListPipelineMiddleware(listSteps)`. Update the factory XML doc (both seams now step pipelines).
- Island `Create` sites: still NO edit (same signature).

- [ ] **Step 1: Implement `BuildListSteps` + the registration swap.**
- [ ] **Step 2: Build** — clean.
- [ ] **Step 3: Test** — `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug`. Expected: **269/269, zero snapshot diffs** (interpolation-fold snapshots — staged-string / member-access folds — and `ChildTemplateTest` are the sensitive ones). Unrelated diff → STOP.
- [ ] **Step 4: Commit (Unit 2)**

```bash
jj describe -m "refactor(templating): list seam as owner-keyed step pipeline (Scope C unit 2)

Add ListPipelineMiddleware (owner = list[0].Parent) mirroring the node pipeline;
re-cast InterpolationFold/MemberSegment middleware as steps (self-checks intact —
they resolve the owner-key collision residual). Factory folds StringRoots/
MemberSegments re-keyed to owner nodes. Create signature unchanged.
Byte-identical, 269/269."
jj bookmark move experimental/quote-action-pipeline --to @
jj new
```

---

## Unit 3 — Move the fold into the Plan; shrink the factory

End-state: a plan-stage assembler owns BOTH folds and serves all three `Create` sites; `TemplateQuoterFactory.Create(nodeSteps, listSteps, includeTrivia)` names zero features; the six old middleware are deleted; `QuoteMiddlewareTest` exercises step types. Byte-identical.

### Task 7: Extract the folds into a plan-stage `PlanAssembler`

**Files:**
- Create: `src/Synto.SourceGenerator/Templating/Pipeline/PlanAssembler.cs`
- Modify: `src/Synto.SourceGenerator/Templating/Quoting/TemplateQuoterFactory.cs` (delete `BuildNodeSteps`/`BuildListSteps`; temporarily call the assembler from inside `Create` so this task stays independently green)

**Interfaces:**
- Produces: `internal static class PlanAssembler` with exactly the two folds, moved **verbatim** from the factory (bodies unchanged — this task is a relocation, not a rewrite):
  - `public static Dictionary<SyntaxNode, ImmutableArray<INodeQuoteMiddleware<QuoteContext>>> BuildNodeSteps(IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> replacements, IReadOnlyDictionary<SyntaxNode, BuilderCall>? builderCalls, HashSet<SyntaxNode> trimNodes, IReadOnlyDictionary<SyntaxNode, ImmutableArray<AppliedDecoration>>? decorationHooks)`
  - `public static Dictionary<SyntaxNode, ImmutableArray<IListQuoteMiddleware<QuoteContext>>> BuildListSteps(IReadOnlyDictionary<SyntaxNode, ExpressionSyntax>? stringRoots, IReadOnlyDictionary<SyntaxNode, ExpressionSyntax>? memberSegments)`
- XML-doc: "Plan-stage assembler: folds `QuotePlan` channels into the per-node / per-owner step maps the quoter runs. Precedence is hand-written here (Node: Replace → Builder → Trim → Decorate; List: InterpolationFold → MemberSegment) — the ONLY place feature order is known. Callable from all three quoter construction sites, each with its own (partial) channels."

- [ ] **Step 1: Move the two folds** to `PlanAssembler`; factory calls them internally (signature still the old channel list for one more task).
- [ ] **Step 2: Build + test** — clean, **269/269, zero snapshot diffs**.

### Task 8: Shrink `Create(nodeSteps, listSteps, includeTrivia)`; repoint all three sites

**Files:**
- Modify: `src/Synto.SourceGenerator/Templating/Quoting/TemplateQuoterFactory.cs`
- Modify: `src/Synto.SourceGenerator/Templating/Pipeline/TemplateFactoryBuilder.cs` (`QuoteAndAssemble`, ~line 875)
- Modify: `src/Synto.SourceGenerator/Templating/Staging/StagedRegionEmitter.cs` (line ~68, the `fixedQuoter`)
- Modify: `src/Synto.SourceGenerator/Templating/Staging/StagedScaffoldBuilder.cs` (line ~165, the `islandQuoter`)

**Interfaces:**
- Consumes: `PlanAssembler.BuildNodeSteps` / `BuildListSteps` (Task 7).
- Produces: `public static MiddlewareSyntaxQuoter<QuoteContext> Create(IReadOnlyDictionary<SyntaxNode, ImmutableArray<INodeQuoteMiddleware<QuoteContext>>> nodeSteps, IReadOnlyDictionary<SyntaxNode, ImmutableArray<IListQuoteMiddleware<QuoteContext>>> listSteps, bool includeTrivia)` — the ONLY `Create`; all channel parameters deleted.

**Contract:**
- `Create` body: register `NodePipelineMiddleware(nodeSteps)` if `nodeSteps.Count > 0`, then `ListPipelineMiddleware(listSteps)` if `listSteps.Count > 0`; construct the engine. **It names zero features.** Rewrite its XML doc accordingly.
- Site 1 — `QuoteAndAssemble`: `nodeSteps = PlanAssembler.BuildNodeSteps(ctx.Plan.Replacements, ctx.Plan.BuilderCalls, prunableNodes, ctx.Plan.DecorationHooks)` (note: `prunableNodes` — the `BranchPruneIdentifier`-expanded trim set — NOT `ctx.Plan.Trim`); `listSteps = PlanAssembler.BuildListSteps(ctx.Plan.StringRoots, ctx.Plan.MemberSegments)`; then the shrunken `Create(nodeSteps, listSteps, includeTrivia: ctx.Options.HasFlag(TemplateOption.PreserveTrivia))`.
- Site 2 — `StagedRegionEmitter` fixed-sibling quoter: `BuildNodeSteps(baseReplacements, builderCalls, new HashSet<SyntaxNode>(), null)` + `BuildListSteps(stringStagedRoots, null)`; `includeTrivia: false`. (`StagedEmitContext.BuilderCalls` threading preserved — same channel, now folded here.)
- Site 3 — `StagedScaffoldBuilder` island quoter: `BuildNodeSteps(liftMap, context.BuilderCalls, new HashSet<SyntaxNode>(), null)` + `BuildListSteps(context.StringStagedRoots, null)`; `includeTrivia: false`.
- The island sites thereby inherit the step model automatically — no other behavior change.

- [ ] **Step 1: Shrink `Create` + repoint the three sites** per the mapping above.
- [ ] **Step 2: Build** — clean. (Compile errors at any missed `Create` caller = the worklist; there are exactly three.)
- [ ] **Step 3: Test** — **269/269, zero snapshot diffs** (`ChildTemplateTest` + staging snapshots are the sensitive ones — the island quoters changed construction path). Unrelated diff → STOP.

### Task 9: Retire the six old feature middleware; migrate `QuoteMiddlewareTest`

**Files:**
- Delete: `src/Synto.SourceGenerator/Templating/Quoting/Middleware/ReplacementMiddleware.cs`, `TrimMiddleware.cs`, `DecorationMiddleware.cs`, `BuilderCallMiddleware.cs`, `InterpolationFoldMiddleware.cs`, `MemberSegmentMiddleware.cs` (then remove the now-empty `Middleware/` directory)
- Modify: `test/Synto.Test/Templating/QuoteMiddlewareTest.cs`

**Interfaces:**
- Consumes: `NodePipelineMiddleware` + node steps (Unit 1), `ListPipelineMiddleware` + list steps (Unit 2). Steps ARE middleware, so the per-feature behavior assertions carry over.

**Contract — migrate the 5 existing tests 1:1 (count stays 5; suite stays 269), routing each through its pipeline middleware so per-key dispatch is covered for free:**
- `Replacement_ReturnsReplacement_ForKeyedNode` → build a node-step map `{ node: [new ReplaceStep(IdentifierName("REPLACED"))] }`, register `new NodePipelineMiddleware(map)`; same assertion.
- `Trim_ReturnsNull_ForKeyedNode` → map `{ node: [TrimStep.Instance] }` via `NodePipelineMiddleware`; same assertion.
- `Decoration_WrapsBaseQuote_InChainedInvocation` → map `{ node: [new DecorateStep(hooks)] }` via `NodePipelineMiddleware`; same assertion.
- `MemberSegment_EmitsBuildList_WhenChildHasSegment` → the list needs a real PARENT for owner keying: parse a member inside a type (e.g. `ParseMemberDeclaration("class C { void M() { } }")` and use the class's `.Members` list + a segment keyed on the method) so `list[0].Parent` is the class decl; step map `{ classDecl: [new MemberSegmentStep(segments)] }` via `ListPipelineMiddleware`; same `BuildList<` assertion.
- `InterpolationFold_DefersToNext_ForNonInterpolatedList` → keep the parentless statement list; an owner-keyed `ListPipelineMiddleware` (any non-matching map, e.g. keyed on an unrelated node, steps `[new InterpolationFoldStep(roots)]`) finds no owner → defers; same equals-base-quote assertion.
- Update the test class XML doc (now exercises the step pipeline: per-key dispatch + per-feature step behavior).
- After deletion, grep-verify zero remaining references to the six old type names.

- [ ] **Step 1: Migrate the 5 tests**, run only this class first: `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug --filter-class "*.QuoteMiddlewareTest"`. Expected: 5/5 pass.
- [ ] **Step 2: Delete the six middleware files** + empty directory; grep `Grep "(ReplacementMiddleware|TrimMiddleware|DecorationMiddleware|BuilderCallMiddleware|InterpolationFoldMiddleware|MemberSegmentMiddleware)"` over `src/` + `test/` → zero hits.
- [ ] **Step 3: Build** — clean.
- [ ] **Step 4: Full test** — **269/269, zero snapshot diffs.**
- [ ] **Step 5: Commit (Unit 3)**

```bash
jj describe -m "refactor(templating): plan-stage step assembler; factory names zero features (Scope C unit 3)

PlanAssembler owns both channel->steps folds (hand-written precedence), shared by
all three quoter construction sites (main body + the two live-region island
quoters). TemplateQuoterFactory.Create shrinks to (nodeSteps, listSteps,
includeTrivia). Six feature middleware retired; QuoteMiddlewareTest migrated to
the step pipeline. Byte-identical, 269/269. Scope C complete."
jj bookmark move experimental/quote-action-pipeline --to @
jj new
```

---

## Final verification (after Unit 3 commit)

- [ ] `TemplateQuoterFactory.cs` contains no feature name (no Replace/Builder/Trim/Decorate/Interpolation/MemberSegment identifier) — read it and confirm it only wires the two pipeline middleware.
- [ ] Grep confirms `src/Synto/Quoting/MiddlewareSyntaxQuoter.cs`, `QuoteContext.cs`, `QuoteMiddleware.cs` have zero diffs vs `main` (`jj diff --from main -- src/Synto/Quoting/` shows nothing for those three).
- [ ] Precedence appears exactly once, hand-written, in `PlanAssembler` (Node: Replace → Builder → Trim → Decorate; List: InterpolationFold → MemberSegment).
- [ ] All three `Create` sites call `PlanAssembler` (grep `TemplateQuoterFactory.Create` → exactly 3 call sites, each preceded by assembler calls).
- [ ] Spot-check one generated factory snapshot (e.g. an ObjectReader/`ChildTemplateTest` snapshot) is byte-identical to `main`'s copy.
- [ ] `experimental/quote-action-pipeline` bookmark is at the Unit-3 commit; `main` unmoved.

## Risk register

- **R1 (Unit 2, highest):** owner re-keying changes WHEN the list steps fire. Mitigated: owner sets are over-approximations and both steps self-defer exactly as the global middleware did (`TryFoldInterpolatedContents` returns false / `hasSegment` scan fails → `next`), so any reachable list produces identical output. If a snapshot diffs anyway, it is a real keying gap — STOP and report.
- **R2 (Units 1+3):** builder-in-live-region threading. The island quoters must keep receiving the `BuilderCalls` channel (Units 1-2: unchanged signature; Unit 3: folded at sites 2-3). Sentinel: `ChildTemplateTest` snapshots.
- **R3 (Unit 1):** per-node step order must equal the global-chain subsequence (Replace → Builder → Trim → Decorate = today's registration order). Encoded once in the fold; do not reorder.
- **R4 (Unit 3):** the two folds must move verbatim (Task 7 is a relocation). Any "improvement" to fold bodies risks byte drift — resist.
- **R5 (all):** step maps rely on reference-identity keys over the never-rewritten input tree (default `SyntaxNode` comparer). Do not introduce any tree rewrite or node re-creation before quoting.
- **R6 (all):** if an UNRELATED snapshot diffs at any gate, STOP and report (possible pre-existing latent case) — never re-accept to make a gate pass.

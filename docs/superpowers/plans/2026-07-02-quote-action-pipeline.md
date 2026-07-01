# Quote Action Pipeline (Scope C) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dissolve the ~30-field mutable `TemplateBuildContext` god-context and re-shape the templating factory pipeline so that the plan phase attaches per-node *actions* and the quote phase runs them as a pre-order-short-circuit / post-order-combine fold — the "quote action pipeline" end-state (Scope C).

**Architecture:** Two phases over the **original** (never-rewritten) syntax tree, so the semantic model stays valid. (1) **Plan** — cohesive per-feature planners (finder + planning step) write into three honestly-separate structures: `FactoryShape` (the factory method skeleton), `QuotePlan` (the patches the quoter applies), and `NameAllocator` (shared name minting). (2) **Quote+Assemble** — the terminal step derives `body = Quote(source, plan)` and `method = Assemble(shape, body)`. The quoter is the existing `MiddlewareSyntaxQuoter` engine; covering features (builder calls, live regions) compose via its `QuoteContext` **re-entry** instead of out-of-band partial-plan sub-quoters.

**Tech Stack:** C# / Roslyn incremental source generators, `Microsoft.CodeAnalysis.CSharp`. Tests: MTP (`dotnet test --project ...`), Verify snapshot suite.

## Implementation status — updated 2026-07-02 (overnight autonomous run)

Branch: `experimental/quote-action-pipeline` (off `main`, never merged). Baseline before work: 267/267 `Synto.Test` green.

- ✅ **Stage 1 (Tasks 1-5) — LANDED, byte-identical.** Commit `ykzozxtx` (163f0ff9). `TemplateBuildContext` dissolved into `FactoryShape` + `QuotePlan` + `NameAllocator` (thin coordinator retains constant inputs + transient analysis). 71 call sites repointed. **267/267 pass.**
- ❌ **Stage 3 (Node-seam per-node pipeline) — REVERTED (review decision 2026-07-02 AM).** Was landed overnight (commit `yzkumstq`), then abandoned per review: low behavior payoff (the old Node middleware were already node-keyed dicts), a "failed cleanup attempt." Branch reverted to Stage-1-only; `TemplateQuoterFactory` is back to the three global Node middleware. Per-node lives in git history if ever revived. **Tasks 10-12 CANCELLED.**
- 🟢 **Stage 2 (Tasks 6-9) — APPROVED to implement.** Review decision: bugs #1 (Splice inside a `[Quoted]` builder arg quoted raw) and #2 (nested builder calls not expanded) are unsupported-but-should-work — **fix them.** Re-entry is the fix; it is NOT byte-identical (it changes output for these two cases), so it needs new snapshot tests capturing the desired output. See the divergence investigation appendix in `docs/superpowers/handoffs/2026-07-02-quote-action-pipeline-overnight.md`.
- ✅ **Node-seam middleware — decision: ALL-IN on the global node-middleware model.** Keep `ReplacementMiddleware`/`TrimMiddleware`/`DecorationMiddleware` as the Node-seam mechanism (they are already `INodeQuoteMiddleware`). Stage 2 adds the re-homed covering features (builder calls, live regions) as **additional re-entrant node middleware** alongside them — NOT as per-node steps.

**Revised Stage 2 framing:** it is a *bug-fix* (fix #1 and #2), not a byte-identical refactor. Live-region re-homing carries the same partial-plan divergence risk as builder-call re-homing (its island sub-quoters at `StagedScaffoldBuilder.cs:165` also use replacements+folds only) — so it too needs the same treatment (tests first, then re-entry). Keep the covering-replace ⊥ Layer-B assertion (Task 9) as the backstop.

## Global Constraints

- **Byte-identical output is the bar.** The whole change is behavior-preserving. Verification = existing `Synto.Test` suite stays green (267 tests, incl. Templating Verify snapshots). No snapshot re-accepts except the ONE intentional new diagnostic in Task 9 (which does not fire for any existing valid input, so snapshots are still unchanged).
- **Never change public factory signatures.** Generated factory method parameter/type-parameter *order and names* are load-bearing for positional callers. Output must not move.
- **Never rewrite the input tree before/inside quoting.** All edits are expressed as a side plan keyed by original-node identity; the semantic model must stay queryable across the whole plan phase. (This is the root-cause constraint: multi-pass tree rewrite invalidates the semantic model.)
- **Order is hand-written, never derived.** Plan step order stays exactly as `TemplateFactoryBuilder.Build` invokes them; quoter seam precedence stays as `TemplateQuoterFactory`/the engine encode it. No topo-sort, no ranks.
- **Off `main`, jj-native.** All work on bookmark `experimental/quote-action-pipeline`. Never move `main`.
- **No git hooks.** Format via `dotnet format whitespace` if needed; CI is the gate.
- **Test invocation (MTP/SDK 10):** raw `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug`. No `-nologo`. Not via rtk. Filter (if ever needed) via `--filter-method` / `--filter-class`, NOT `--filter`.
- **Refactor-TDD:** the existing suite IS the red/green harness. Each task's gate is "build clean + 267 pass + zero snapshot diffs" (except Task 9's benign diagnostic). There is no new failing test to write first except where a task explicitly adds behavior (Task 9).

---

## Orientation (read before starting)

Key files (all under `src/Synto.SourceGenerator/Templating/` unless noted):

- `Pipeline/TemplateFactoryBuilder.cs` — `Build()` (lines ~24-79) calls ~22 ordered private steps over a shared `TemplateBuildContext`. `QuoteAndAssemble` (~891-933) is the terminal quote+assemble. This is the primary file.
- `Pipeline/TemplateBuildContext.cs` — the god-context (~30 fields). The thing being dissolved.
- `Quoting/TemplateQuoterFactory.cs` — `Create(...)` builds the `MiddlewareSyntaxQuoter<QuoteContext>` from the five channels; its parameter list IS the QuotePlan.
- `Quoting/Middleware/{Replacement,Trim,InterpolationFold,MemberSegment,Decoration}Middleware.cs` — the five feature middleware.
- `Analysis/BranchPruneIdentifier.cs` — the global trim-prune fixpoint (all-children-prunable → parent prunable; ParameterList never prunes).
- `Staging/StagedRegionEmitter.cs`, `Staging/StagedScaffoldBuilder.cs` — live-region covering feature + its partial-plan island sub-quoters.
- Engine (runtime side): `src/Synto/Quoting/MiddlewareSyntaxQuoter.cs`, `src/Synto/Quoting/QuoteMiddleware.cs` (7 seam interfaces + `(payload, context, next)` shape), `src/Synto/Quoting/QuoteContext.cs` (re-entry: `Quote(node)`, `QuoteList<TNode>(list)`, `ToArrayLiteral(...)`), `src/Synto/Quoting/CSharpSyntaxQuoter.cs` (base quoter; drop = return null; lists filter nulls; separated lists drop the paired separator too).

### Field → structure mapping (the dissolution target)

| Current `TemplateBuildContext` field | Goes to | New name |
|---|---|---|
| `UnquotedReplacements` | `QuotePlan` | `Replacements` |
| `TrimNodes` | `QuotePlan` | `Trim` |
| `StringStagedRoots` | `QuotePlan` | `StringRoots` |
| `SpliceMemberSegments` | `QuotePlan` | `MemberSegments` |
| `DecorationHooks` | `QuotePlan` | `DecorationHooks` |
| `Parameters` | `FactoryShape` | `Parameters` |
| `TypeParams` | `FactoryShape` | `TypeParams` |
| `Preamble` | `FactoryShape` | `Preamble` |
| `InlinedTypeParams` | `FactoryShape` | `InlinedTypeParams` |
| `ParamNames` | `NameAllocator` | `ParamNames` |
| `InlinedTypeParamNames` | `NameAllocator` | `InlinedTypeParamNames` |
| Constant inputs (`Diagnostics`, `SemanticModel`, `AdditionalUsings`, `TemplateInfo`, `Options`) | stay on `TemplateBuildContext` | — |
| Transient analysis (`Scope`, `StagedParameters`, `StagedLocals`, `StagedRootParameters`, `QuoteCalls`, `Partition`, `StagedRegions`, `RegionConsumedNodes`, `FoldClaimedReferences`, `FoldsByStagedParameter`, `ValidSpliceGenerators`, `SpliceGeneratorNodes`, `ValueLift`, `ConverterError`, `StagedRegionCounter`, `RootNames`) | stay on `TemplateBuildContext` | — |

### The three load-bearing facts (from the design conversation)

1. **Two layers of quoter behavior.** Input-side primitives — `replace` (terminal substitute), `trim`/`dontQuote` (terminal drop, with the global prune) — vs boundary-crossing capability transforms — `InterpolationFold`, `MemberSegment`, `Decoration` — which emit constructs that don't exist in the input tree (`.Apply`, `BuildList`, folded literal text) and therefore must run at the input→output boundary (the quoter), not as pre-passes.
2. **Cohesion is a plan-time property.** A feature = finder + planning step (+ the quote-time behavior it drives). Middleware stay kind-scoped consumers; the plan is where per-feature cohesion lives.
3. **Covering-replace ⊥ Layer-B (today, by construction only).** A covering replacement short-circuits, so a decoration/segment/fold *nested inside* it never fires. Today this is safe only because covering replacements wrap expression/statement contexts and Layer-B targets are declarations — nothing enforces it. Task 9 makes it explicit; Scope-C re-entry (Task 6-7) makes it compose so the hazard cannot arise.

### Known engine constraint (from mapping)

`InterpolationFoldMiddleware` is gated by `typeof(TNode) == InterpolatedStringContentSyntax` — a **list-element-type** trigger, not node identity. It therefore **cannot** be purely node-keyed. Consequence for Scope C: the **Node seam** becomes per-node pipelines; the **List seam** stays list-level (its two middleware keep their type/child dispatch) but is driven by the same `QuotePlan` data. "Per-node pipeline" is the Node-seam story, not a universal one. This is a deliberate, documented boundary — not a gap.

---

## Stage 1 — Dissolve the god-context (Tasks 1-5). Byte-identical by construction; engine untouched.

### Task 1: Introduce `FactoryShape`

**Files:**
- Create: `src/Synto.SourceGenerator/Templating/Pipeline/FactoryShape.cs`

**Interfaces:**
- Produces: `internal sealed class FactoryShape` with mutable collections:
  - `List<ParameterSyntax> Parameters { get; }` (init `new()`)
  - `List<TypeParameterSyntax> TypeParams { get; }` (init `new()`)
  - `List<StatementSyntax> Preamble { get; }` (init `new()`)
  - `HashSet<ITypeParameterSymbol> InlinedTypeParams { get; }` (init `new(SymbolEqualityComparer.Default)`)

- [ ] **Step 1: Create `FactoryShape`** with exactly the four members above, XML-doc it as "the factory method skeleton the plan phase accumulates: signature (Parameters/TypeParams) + hoisted Preamble statements." Match namespace `Synto.Generators.Templating`.
- [ ] **Step 2: Build** — `dotnet build Synto.slnx -c Debug`. Expected: clean (new type is unreferenced yet, still compiles).

### Task 2: Introduce `QuotePlan`

**Files:**
- Create: `src/Synto.SourceGenerator/Templating/Pipeline/QuotePlan.cs`

**Interfaces:**
- Produces: `internal sealed class QuotePlan` with:
  - `Dictionary<SyntaxNode, ExpressionSyntax> Replacements { get; }` (init `new()`)
  - `HashSet<SyntaxNode> Trim { get; }` (init `new()`)
  - `Dictionary<SyntaxNode, ExpressionSyntax> StringRoots { get; }` (init `new()`)
  - `Dictionary<SyntaxNode, ExpressionSyntax> MemberSegments { get; }` (init `new()`)
  - `IReadOnlyDictionary<SyntaxNode, ImmutableArray<AppliedDecoration>>? DecorationHooks { get; set; }` (nullable field/prop, mirrors current)

- [ ] **Step 1: Create `QuotePlan`** with the five members. XML-doc: "the patches the terminal quoter applies; this is 1:1 the argument list of `TemplateQuoterFactory.Create`." Preserve the exact comments currently on those fields in `TemplateBuildContext` (the interpolation-fold / member-segment / decoration explanations) — move them here.
- [ ] **Step 2: Build** — clean.

### Task 3: Introduce `NameAllocator`

**Files:**
- Create: `src/Synto.SourceGenerator/Templating/Pipeline/NameAllocator.cs`

**Interfaces:**
- Produces: `internal sealed class NameAllocator` with:
  - `HashSet<string> ParamNames { get; }` (init `new(StringComparer.Ordinal)`)
  - `HashSet<string> InlinedTypeParamNames { get; }` (init `new(StringComparer.Ordinal)`)

> NOTE: Do NOT extract the name-minting `while (!set.Add(name)) name += "_"` logic into methods in this task — that is behavior-sensitive and stays inline at the call sites for now to preserve byte-identical output. This task only relocates the dedup sets. (A later, separate cleanup may add mint helpers once byte-identical is proven.)

- [ ] **Step 1: Create `NameAllocator`** with the two sets. XML-doc: "shared uniqueness sets for minted factory parameter + inlined type-parameter names."
- [ ] **Step 2: Build** — clean.

### Task 4: Wire the three structures into `TemplateBuildContext`

**Files:**
- Modify: `src/Synto.SourceGenerator/Templating/Pipeline/TemplateBuildContext.cs`

**Interfaces:**
- Consumes: `FactoryShape`, `QuotePlan`, `NameAllocator` (Tasks 1-3).
- Produces: `TemplateBuildContext` gains `FactoryShape Shape { get; }`, `QuotePlan Plan { get; }`, `NameAllocator Names { get; }` (constructed in the ctor). The eleven migrated fields are **removed** from `TemplateBuildContext`. Constant inputs and transient-analysis fields stay.

- [ ] **Step 1:** Add `Shape`/`Plan`/`Names` auto-properties initialized to `new()` (or in ctor). Remove the eleven migrated field declarations (`UnquotedReplacements`, `TrimNodes`, `StringStagedRoots`, `SpliceMemberSegments`, `DecorationHooks`, `Parameters`, `TypeParams`, `Preamble`, `InlinedTypeParams`, `ParamNames`, `InlinedTypeParamNames`). Keep everything else verbatim.
- [ ] **Step 2: Build** — EXPECTED TO FAIL with many CS1061 ("'TemplateBuildContext' does not contain a definition for 'UnquotedReplacements'…") across `TemplateFactoryBuilder.cs` and any emitter call sites. This failure list is the exact worklist for Task 5.

### Task 5: Repoint all call sites to the new structures

**Files:**
- Modify: `src/Synto.SourceGenerator/Templating/Pipeline/TemplateFactoryBuilder.cs` (all `ctx.<migrated>` references)
- Modify (call sites only): any other file that reads a migrated field off a `TemplateBuildContext` instance (grep to confirm — likely just `TemplateFactoryBuilder.cs`; emitters receive fields as parameters, so their signatures are unchanged and only the call sites that pass `ctx.<field>` move to `ctx.Plan.<field>` etc.)

**Interfaces:**
- Consumes: the `Shape`/`Plan`/`Names` accessors from Task 4.
- Produces: a compiling, byte-identical pipeline.

Mechanical rename map (apply with editor replace-all, each string is unique in-file):
- `ctx.UnquotedReplacements` → `ctx.Plan.Replacements`
- `ctx.TrimNodes` → `ctx.Plan.Trim`
- `ctx.StringStagedRoots` → `ctx.Plan.StringRoots`
- `ctx.SpliceMemberSegments` → `ctx.Plan.MemberSegments`
- `ctx.DecorationHooks` → `ctx.Plan.DecorationHooks`
- `ctx.Parameters` → `ctx.Shape.Parameters`
- `ctx.TypeParams` → `ctx.Shape.TypeParams`
- `ctx.Preamble` → `ctx.Shape.Preamble`
- `ctx.InlinedTypeParams` → `ctx.Shape.InlinedTypeParams`
- `ctx.ParamNames` → `ctx.Names.ParamNames`
- `ctx.InlinedTypeParamNames` → `ctx.Names.InlinedTypeParamNames`

- [ ] **Step 1:** Grep `Grep "\.(UnquotedReplacements|TrimNodes|StringStagedRoots|SpliceMemberSegments|Parameters|TypeParams|Preamble|InlinedTypeParams|ParamNames|InlinedTypeParamNames|DecorationHooks)\b"` scoped to `src/Synto.SourceGenerator/Templating` to enumerate every call site and confirm which files are affected. Watch for false hits on unrelated `.Parameters`/`.TypeParams` (e.g. Roslyn symbol APIs) — only rewrite accesses whose receiver is the `TemplateBuildContext ctx`.
- [ ] **Step 2:** Apply the rename map in `TemplateFactoryBuilder.cs` and any confirmed call sites.
- [ ] **Step 3: Build** — `dotnet build Synto.slnx -c Debug`. Expected: clean.
- [ ] **Step 4: Test** — `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug`. Expected: **267 passed, 0 failed, zero snapshot diffs.**
- [ ] **Step 5: Commit** — `jj describe` + advance `experimental/quote-action-pipeline`. Message: `refactor(templating): dissolve TemplateBuildContext into FactoryShape/QuotePlan/NameAllocator`.

**Stage-1 done-check:** `TemplateBuildContext` is now a thin coordinator (constant inputs + transient analysis + `Shape`/`Plan`/`Names`); the five quoter channels are a cohesive `QuotePlan` whose shape mirrors `TemplateQuoterFactory.Create`. Byte-identical. This is a safe, independently-valuable landing point.

---

## Stage 2 — Re-home covering features onto `QuoteContext` re-entry (Tasks 6-9).

> Rationale: today builder-calls and live-regions pre-assemble output at PLAN time using out-of-band sub-quoters built from a PARTIAL plan (replacements + folds only; NOT decorations/segments), then install a TERMINAL replacement. This is why the covering-replace ⊥ Layer-B hazard exists and why the "container replacement added LAST" hack is needed. Re-entry moves assembly to QUOTE time via `QuoteContext.Quote(subtree)`, which runs the FULL pipeline over original subtrees (semantic model stays valid), so nested transforms compose and the sub-quoters + "added LAST" hack disappear.

### Task 6: Re-home builder-call assembly onto re-entry

**Files:**
- Modify: `Pipeline/TemplateFactoryBuilder.cs` (`ResolveBuilderCalls`, ~762-807)
- Create: `Quoting/Middleware/BuilderCallMiddleware.cs` (a Node-seam middleware) — OR extend the plan to register builder-call nodes as a re-entrant action consumed by a new middleware.

**Contract / behavior to preserve:**
- Discovery (`FacadeCallFinder`) stays at plan time; it identifies builder-call invocation nodes + their arg bindings (`Quoted` / `QuotedTypeArg` / verbatim). Diagnostics (SY1009-1011) still bail at plan time.
- Instead of installing a terminal `Replacements[call.Invocation] = prebuiltInvocation`, register a plan entry that, at quote time, a `BuilderCallMiddleware` recognizes at `call.Invocation` and assembles: for each `Quoted`/`QuotedTypeArg` arg → `ctx.Quote(originalArgNode)` (full pipeline re-entry); verbatim args → `arg.WithoutTrivia()`; then the fully-qualified `Builder.Method(args…)` invocation. It returns that expression WITHOUT calling `next` (terminal for the call node itself, but its arg islands went through full re-entry).
- **Self-recursion guard:** the middleware must not re-trigger itself on `call.Invocation` while assembling (it re-quotes the ARGS, not the call node). Since re-entry is on arg subtrees, not the invocation, this is naturally satisfied — assert/verify no arg equals the invocation.

**Byte-identical note:** the old island sub-quoter carried replacements + folds only; re-entry carries the full plan. For all existing valid inputs no decoration/segment nests inside a builder arg, so output is unchanged. Verify via snapshots.

- [ ] Step 1: Implement `BuilderCallMiddleware` consuming a `IReadOnlyDictionary<SyntaxNode, BuilderCallPlan>` (invocation node → resolved builder name + ordered arg bindings). Define `BuilderCallPlan` as a plan record.
- [ ] Step 2: Change `ResolveBuilderCalls` to populate that dictionary instead of building the island sub-quoter + terminal replacement. Delete the `islandQuoter` (line ~779).
- [ ] Step 3: Register the middleware in `TemplateQuoterFactory.Create` (new optional channel), Node seam, BEFORE Trim, alongside Replacement precedence — verify precedence against snapshots.
- [ ] Step 4: Build clean; **267 pass, zero snapshot diffs**. Commit.

### Task 7: Re-home live-region container assembly onto re-entry

**Files:**
- Modify: `Staging/StagedRegionEmitter.cs`, `Staging/StagedScaffoldBuilder.cs`, `Pipeline/TemplateFactoryBuilder.cs` (`EmitLiveRegions`)
- Create: `Quoting/Middleware/LiveRegionMiddleware.cs` (Node-seam, keyed at the region's container block)

**Contract / behavior to preserve:**
- The scaffold/preamble computation (control-statement hoisting, run declarations, root-renaming) stays at plan time — it produces `Preamble` statements and the per-region metadata. What moves to quote time is the **container block assembly**: instead of pre-building `Block(BuildList<StatementSyntax>(seg…))` with island sub-quoters and installing it as the "added LAST" terminal replacement, a `LiveRegionMiddleware` at the container block re-quotes the fixed-sibling statements + quoted islands via `ctx.Quote(...)` and assembles the block.
- **Self-recursion guard is essential here:** the middleware fires at the container block; it must re-quote the block's *constituent parts* (fixed siblings, island statements) via re-entry WITHOUT re-triggering itself on the same container block. The old "added LAST" ordering was the plan-time analog of this guard; reify it as: the middleware, when invoked on the container, marks/uses a guard so nested `ctx.Quote` on the same container node does not recurse (or, more simply, re-quotes only the children it assembles, never the container itself).
- The `CollectLiftPoints` maximal-live-subexpression lifting stays (those become `.ToSyntax()` lifts installed as replacements in the plan, consumed during re-entry).

- [ ] Step 1: Introduce `LiveRegionMiddleware` + a `LiveRegionPlan` (container node → ordered segment descriptors: run-name refs + quoted-island original nodes + fixed-sibling original nodes).
- [ ] Step 2: Change `StagedRegionEmitter` to emit that plan (Preamble + `LiveRegionPlan`) instead of `ContainerReplacements`; drop the island sub-quoters (StagedScaffoldBuilder ~165) in favor of deferred re-entry.
- [ ] Step 3: Register `LiveRegionMiddleware`; ensure it composes after Replacement (so lifted-value replacements inside the region still apply during re-entry). Verify precedence via snapshots.
- [ ] Step 4: Build clean; **267 pass, zero snapshot diffs**. Commit.

### Task 8: Retire splice-call + quote-call as ordinary terminal actions (no behavior change)

Splice-call (`ResolveSpliceCalls`, verbatim arg) and quote-call (`LiftQuoteCalls`, value-lift conversion) are already terminal replacements with no sub-quoter — they need no re-homing. Confirm they still flow through `QuotePlan.Replacements` unchanged. This task is a verification checkpoint only (no code), folded into the Task 7 commit if trivially confirmed.

### Task 9: Make the covering-replace ⊥ Layer-B invariant explicit (safety net)

**Files:**
- Modify: `Pipeline/TemplateFactoryBuilder.cs` (a new plan-time assertion step, or inside `QuoteAndAssemble` pre-quote)
- Diagnostic: add `SYxxxx` "decoration/segment/fold target nested inside a covering replacement" (choose next free id; register in the diagnostics table).

**Contract:**
- After the plan is assembled and before quoting, for each Layer-B target node (a `DecorationHooks` key, a `MemberSegments` key, a `StringRoots` key), verify no ancestor is a *covering* replacement/action node (builder-call, live-region container, splice-call). If one is, emit the diagnostic instead of silently dropping the transform.
- **Byte-identical:** this fires for ZERO existing valid inputs (the invariant holds by construction today), so snapshots are unchanged. It converts a latent silent-drop into a loud failure.

- [ ] Step 1: Write a focused unit/snapshot test that constructs a template with a decoration nested inside a covering replacement and asserts the new diagnostic (this is the one place a NEW test is written first). Expected: FAIL (no diagnostic yet).
- [ ] Step 2: Implement the ancestor-scan assertion + diagnostic. Test passes.
- [ ] Step 3: Full suite **267 (+1 new) pass, zero diffs on existing snapshots**. Commit.

---

## Stage 3 — Flip the Node seam to per-node override pipelines (Tasks 10-12). Highest risk; land last.

> This is the "per-node pipeline" end-state for the **Node seam** only (see Known engine constraint: the List seam stays list-level). Semantic payoff is modest (the current Node middleware are already node-keyed dicts, so this is largely an internal dispatch reorganization), so it is sequenced last and only attempted once Stages 1-2 are green and committed.

### Task 10: Add per-node dispatch to the engine (behind a construction flag), old path intact

**Files:**
- Modify: `src/Synto/Quoting/MiddlewareSyntaxQuoter.cs`

**Contract:**
- Add an alternate construction that accepts `IReadOnlyDictionary<SyntaxNode, IReadOnlyList<INodeQuoteMiddleware<TContext>>>` (per-node Node-seam steps) instead of a global Node-seam array. The `Visit(SyntaxNode?)` override: if the node has a per-node step list, build the CPS chain from THOSE steps (terminal `BaseVisitNode`) exactly as today's global chain does; else `base.Visit(node)`. List/SeparatedList/Token/etc. seams are UNCHANGED (still global — the List seam keeps InterpolationFold + MemberSegment).
- **Equivalence for byte-identical:** a node's per-node step list MUST equal the subsequence of today's global Node chain (`Replacement → Trim → Decoration`) whose membership includes that node, in that relative order. Assemble it in that fixed order.

- [ ] Step 1: Implement the alternate dispatch + construction path; keep the global path for all other seams and as the default. Build clean.
- [ ] Step 2: Add an engine-level unit test (in `Synto.Test`) that a per-node-configured quoter and an equivalent global-configured quoter produce identical output on a representative tree. Pass.

### Task 11: Have `TemplateQuoterFactory` build the per-node Node pipeline from `QuotePlan`

**Files:**
- Modify: `Quoting/TemplateQuoterFactory.cs`

**Contract:**
- Replace the three Node-seam global middleware (`Replacement`, `Trim`, `Decoration`) with a construction that, for each node touched by any of those channels (union of `Replacements.Keys`, prunable-`Trim`, `DecorationHooks.Keys`, plus re-entrant builder/live-region nodes from Stage 2), builds its ordered step list in the fixed precedence and hands the per-node map to the engine (Task 10 path). The List-seam middleware (`InterpolationFold`, `MemberSegment`) stay global exactly as today.
- Prune interaction: `Trim` still expands via `BranchPruneIdentifier` first; each resulting prunable node gets a `[drop]` step. (Fold-native pruning is explicitly OUT of scope for Scope C — keep the existing global prune pass to protect byte-identical; revisit separately.)

- [ ] Step 1: Implement. Build clean; **full suite pass, zero snapshot diffs**.
- [ ] Step 2: Commit.

### Task 12: Delete the now-dead global Node-seam path + stale sub-quoter scaffolding

**Files:**
- Modify: `src/Synto/Quoting/MiddlewareSyntaxQuoter.cs` (remove the global Node path if fully unused — CAUTION: only if no other quoter construction depends on it; the island sub-quoters removed in Stage 2 were the main other users), `Quoting/Middleware/*` (remove any middleware fully superseded).

- [ ] Step 1: Confirm via grep no remaining construction uses the global Node path. Remove dead code. Build clean; full suite pass, zero diffs.
- [ ] Step 2: Commit. Final: `refactor(templating): quote action pipeline (Scope C) — per-node Node-seam dispatch`.

---

## Self-review checklist (run after Stage completions)

1. **Byte-identical:** every stage ends with 267 (+Task-9's 1) passing and zero diffs on pre-existing snapshots.
2. **Signatures unmoved:** spot-check a generated factory snapshot (e.g. ObjectReader) for identical parameter order/names before/after each stage.
3. **Semantic model:** no code path constructs a rewritten input tree and re-queries the semantic model on it; all quoting is over original nodes.
4. **Order preserved:** `Build()` step order and quoter seam precedence are byte-for-byte the same ordering as before.
5. **Scope discipline:** fold-native pruning and List-seam per-node conversion are explicitly deferred (documented above), not silently attempted.

## Risk register

- **R1 (Stage 2, high):** re-entry changes WHICH channels nested islands see (full vs replacements+folds). Mitigated: no existing valid input nests Layer-B in a covering context (Task 9 enforces). If a snapshot diffs, it reveals a real pre-existing latent case — STOP and report, do not re-accept.
- **R2 (Stage 2, high):** self-recursion in re-entrant covering middleware. Mitigated: re-quote constituent parts, never the covering node itself; add an explicit guard/assert.
- **R3 (Stage 3, medium):** per-node step ordering must equal the global-chain subsequence. Mitigated: Task 10 equivalence test.
- **R4 (all):** name-minting logic is byte-sensitive; do NOT refactor it during Scope C (kept inline in `NameAllocator` task note).

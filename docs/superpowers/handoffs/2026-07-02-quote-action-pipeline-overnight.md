# Morning report — Quote Action Pipeline (Scope C), overnight autonomous run

**Date:** 2026-07-02 (overnight). **Branch:** `experimental/quote-action-pipeline` (off `main`, nothing pushed, `main` never moved). **Baseline & current:** 267/267 `Synto.Test` green throughout.

## TL;DR

You asked me to build Scope C (the per-node "quote action pipeline") overnight and report in the morning. I landed the two parts that are **byte-identical and therefore verifiable while you slept**, and **deliberately did NOT land the one part where green passing is not sufficient proof of correctness**. Everything is on the experimental branch as clean, separate commits — nothing merged, all reversible.

| What | Status | Commit | Verified |
|---|---|---|---|
| Stage 1 — dissolve `TemplateBuildContext` → `FactoryShape`/`QuotePlan`/`NameAllocator` | ✅ landed | `ykzozxtx` 163f0ff9 | 267/267, byte-identical |
| Stage 3 — Node seam as per-node action pipeline | ✅ landed | `yzkumstq` 361c9a6b | 267/267, byte-identical |
| plan status update | 📄 docs | `tlmmkltv` (tip) | n/a |
| Stage 2 — re-home covering features onto re-entry | ⏸️ deferred | — | needs your review (see below) |

Commit graph (top = newest):
```
tlmmkltv  docs(plans): status update            <- @ / bookmark tip
yzkumstq  Node seam per-node pipeline (Stage 3)
ykzozxtx  dissolve TemplateBuildContext (Stage 1)
sowtvwxx  docs(plans): the plan
zmvzmwrs  main  (untouched, f5d27bbc)
```

## What I built and why it's safe

### Stage 1 — god-context dissolved (your #1 "abomination")
`TemplateBuildContext`'s ~30 fields split into three cohesive carriers:
- `FactoryShape` — `Parameters`, `TypeParams`, `Preamble`, `InlinedTypeParams` (the factory skeleton).
- `QuotePlan` — `Replacements`, `Trim`, `StringRoots`, `MemberSegments`, `DecorationHooks` (1:1 with `TemplateQuoterFactory.Create`'s arg list).
- `NameAllocator` — `ParamNames`, `InlinedTypeParamNames`.

`TemplateBuildContext` is now a thin coordinator (constant inputs + transient analysis + `Shape`/`Plan`/`Names`). 71 call sites in `TemplateFactoryBuilder.cs` repointed mechanically. Pure data reorganization; the engine/middleware were untouched, so byte-identical is guaranteed by construction and confirmed by the suite. **This is the safe, high-value win — it stands on its own even if you reject everything else.**

### Stage 3 — Node seam is now a per-node action pipeline (the model you described)
The three global Node middleware (Replacement/Trim/Decoration) are superseded (for templating) by a single `NodePipelineMiddleware` that looks up the visited node and runs *its own* ordered step list — `ReplaceStep` (terminal) → `TrimStep` (terminal drop) → `DecorateStep` (post/wrap). `TemplateQuoterFactory` folds the three node-keyed channels into one `node → steps` map. New files under `Templating/Quoting/NodePipeline/`.

**Important deviation from the written plan (for the better):** the plan's Tasks 10-12 called for changing the shared runtime engine (`MiddlewareSyntaxQuoter`). I found that unnecessary and riskier than needed — the per-node dispatch lives entirely in a Node-seam middleware on the SourceGenerator side. The runtime engine and the List seam are completely untouched. Byte-identical, confirmed by the suite.

**Honest caveat on Stage 3's value:** the old Node middleware were already node-keyed dictionaries, so this is largely a *structural/clarity* change realizing the "per-node pipeline" model you liked, not a behavior change. It's a clean standalone commit — trivial to revert if you decide it doesn't earn its keep. I'd value your gut-check here.

## What I deliberately did NOT do, and why

### Stage 2 — re-home covering features (builder calls, live regions) onto `QuoteContext` re-entry
This is the semantically valuable part (it kills the out-of-band partial-plan sub-quoters, the "container replacement added LAST" hack, and the covering-replace ⊥ Layer-B hazard by construction). **But it is a behavior change, not a reorganization:** today the island sub-quoters quote with the *partial* plan accumulated so far (replacements + folds only, NOT decorations/segments); re-entry would quote with the *full* plan. For every input the snapshot suite covers, output is identical — but **green does not prove the uncovered nesting combinations are equivalent** (e.g. a builder-arg island containing a node targeted by a later-added replacement, or nested builder calls). Landing that blind risks a green-but-wrong regression I couldn't catch without you.

I ran a background investigation of the exact divergence risk (nested builder calls, builder-arg-with-later-replacement, live-region self-recursion, snapshot coverage). **Its findings are appended below** (or, if the section says "pending", the agent was still running at sign-off — check `docs/superpowers/plans/2026-07-02-quote-action-pipeline.md` Stage 2 for the plan and re-run the investigation).

## How to verify what I landed

```
dotnet build Synto.slnx -c Debug
dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug   # expect: 267 passed, 0 failed
```
(MTP/SDK-10: raw `dotnet test --project`, no `-nologo`, not via rtk.)

Inspect the two code commits:
```
jj diff -r ykzozxtx   # Stage 1
jj diff -r yzkumstq   # Stage 3
```

## Decisions I need from you (morning)

1. **Stage 3: keep or revert?** Low behavior payoff; realizes the model you asked for. Clean standalone commit.
2. **Old global Node middleware** (`ReplacementMiddleware`/`TrimMiddleware`/`DecorationMiddleware`): retire them (migrate `QuoteMiddlewareTest.cs` to the per-node steps) or keep them as general engine middleware? I kept them (green) rather than make a public-surface call for you.
3. **Stage 2: go or no-go**, given the re-entry investigation findings below. If go, I'd want targeted tests for the risky nesting combos before landing, so green means something.

## Next actions if you want to continue Stage 2
- Read the investigation findings (below / re-run).
- If the risky nesting combos are proven unreachable (e.g. guarded by diagnostics), Stage 2 becomes byte-identical-safe → implement Tasks 6-9.
- If reachable, add snapshot tests for those combos FIRST, decide intended semantics WITH me, then implement.

---

## Appendix: Stage 2 re-entry divergence investigation (COMPLETE)

**Verdict: re-homing builder-call assembly onto full-plan re-entry is NOT byte-identical.** Two constructible, currently-untested divergences exist. Live-region re-homing is safe.

| Case | Verdict | Today's output | Under re-entry | Constructible | Tested |
|---|---|---|---|---|---|
| 1. `Template.Splice(node)` inside a builder `[Quoted]` arg | **RISKY** | raw-quoted `Splice(...)` invocation | replaced with `node` | yes | no |
| 2. Nested builder calls `A.f(B.g(x))` | **RISKY** | inner `B.g` quoted raw | inner `B.g` expanded | yes | no |
| 3. Live-region container self-recursion | **SAFE** | — | no recursion (node-identity keys) | n/a | yes |

**Root cause of 1 & 2:** `ResolveBuilderCalls` builds ONE island sub-quoter at step 17 (`TemplateFactoryBuilder.cs:779`) from the plan accumulated *so far*. Splice-call replacements are added at step 18 (`:814-815`); sibling/nested builder replacements are added *during* the step-17 loop (`:802`) — after the island quoter was built. So the island quoter can't see them and quotes those nodes raw. Full re-entry uses the final quoter (all replacements present), so it applies them.

**Interpretation (important):** cases 1 & 2 are almost certainly **latent bugs** in today's behavior — a `Splice` inside a `[Quoted]` builder arg *should* inject, and a nested builder *should* expand. If so, re-entry is a **fix**, not a regression. But that makes Stage 2 a deliberate behavior change requiring: (a) your confirmation of intended semantics, and (b) new snapshot tests for both cases (the suite has zero coverage — `SyntaxBuilderTest` covers only flat, non-nested, non-splice builder args). It is NOT a silent byte-identical refactor.

**Recommended Stage 2 path (revised):**
1. Decide intended semantics for cases 1 & 2 (likely "should expand/inject" = re-entry is correct).
2. Write failing snapshot tests capturing the desired output for both.
3. Implement builder-call re-homing onto re-entry; the new snapshots become the record. Live-region re-homing can proceed in parallel (safe).
4. Keep the covering-replace ⊥ Layer-B assertion (plan Task 9) as the backstop.

If instead strict byte-identical is mandatory, re-homing must replicate the partial-plan behavior with an explicit guard — which defeats the purpose. So Stage 2's real question is "**do we fix these two latent bugs?**", not "how do we refactor invisibly."

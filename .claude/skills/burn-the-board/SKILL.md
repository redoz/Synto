---
name: burn-the-board
description: Burn the issue-flow board down by walking EVERY actionable issue to terminal (closed/done or blocked), closest-to-done first — a rolling worker pool of width N where each slot walks one issue all the way through its stages via the matching issue-* skill, refilling from a fresh proximity-sorted snapshot the instant any walk finishes, until nothing is actionable. ready→implement (default-ON) lands the plan and closes the issue. Same workflow as /walk-issue, scoped to the whole board instead of one issue. Runs continuously via `/loop /burn-the-board`. Part of the issue-planning flow.
user_invocable: true
---

# burn-the-board

Thin wrapper over the `walk-issue` workflow, scoped to the **whole board**. All orchestration lives in the workflow; this skill mints a fresh `runid`, invokes the workflow with `scope:'all'`, and relays the structured return.

**What it is.** Burning the board down = **walking every actionable issue to terminal**. A **rolling worker pool of width N** walks up to N issues concurrently; each slot drives **one issue all the way** through its stages (brainstorm → spec-review → plan → plan-review → implement → close) to **closed/done or blocked**, and the instant **any** walk finishes its slot refills from a **fresh proximity-sorted snapshot** (closest-to-done first) — so a long walk never idles the other slots. It stops at the **fixpoint**: nothing actionable and nothing in flight (**everything done or everything blocked**).

**Relationship to `/walk-issue`.** Identical machinery. `/walk-issue <n>` runs this same `walk-issue` workflow scoped to **one** target (or one epic's children); `/burn-the-board` runs it scoped to **all** actionable open issues. Because each stage is **delegated to its `issue-*` skill**, both inherit **adaptive rigor** (quick-brainstorm, skip-plan, the floor-protected dimension subset, the one-shot implement tag). It carries **no** snapshot-as-goal, **no** reconcile, and **no** intake — it walks what is **already actionable**, and keeps the "Safe" essentials (infra probe, ff-sync, a minimal crash-reaper, per-unit `issue-flow-bug` self-healing), relying on each skill's own **entry** unaddressed-comment guard.

**Read first:** `.claude/rules/github.md` — § The autonomous loops (the single-operator trust assumption), § Queues vs Doing (what is pulled), § The unaddressed-comment guard; and `.claude/rules/rigor.md` (the adaptive-rigor knobs each stage applies).

**On tooling failure** (the workflow throws / a `gh`/git call breaks — *not* a stage legitimately parking `blocked`): the workflow self-heals (files/recurs a de-duped `issue-flow-bug`). If invoking the workflow itself fails, print the error and stop — never report the report.

## Usage

- `/burn-the-board` — one burn to fixpoint (nothing actionable left), then exit.
- `/loop /burn-the-board` — continuous mode: self-paced, ticks serialized. The next tick re-checks the board, so anything a human un-blocks between ticks is picked up.

## Args

| Arg | Default | Meaning |
|---|---|---|
| `N` | `3` | Worker-pool width — max issues **walked concurrently** at any instant. The pool is rolling: as soon as one walk finishes, its slot is refilled from a fresh snapshot, so a slow stage never idles the others. Each runs in its own worktree, so concurrent stages never stomp the primary checkout. Raise to burn faster; lower to `1` for strictly sequential. Concurrency is bounded by the per-stage `MAX_ATTEMPTS` push-race cap — back off if it is contended. |
| `implement` | `true` | Include the `ready`→implement→**close** step (run TDD, land on `main`, close the issue) — the step that actually takes items off the board. **Default-ON — approved-plan TDD diffs land on `main` unseen** (deliberate; github.md § The autonomous loops — a `manual` label on an issue still excludes it). `false` = stop at the `ready` gate (authoring/review only; `ready` plans pile up for a human or `/issue-implement`). |
| `K` | `5` | Per-stage review round limit (github.md § The round limit K). A review cycle parks `blocked` (leaves the actionable set) on its Kth failure — the loop's termination guarantee for review cycles. |

## Steps

1. **Mint a fresh `runid`** (the workflow runtime forbids `Date.now()`/`Math.random()`, so the run id and any wall-clock value must be supplied by the caller). Run, via the Bash tool:
   `date -u +%Y%m%dT%H%M%SZ`
   and use its output as `runid`. (Each invocation — including each `/loop` tick — gets a fresh one; it scopes the run's worktree paths and run-log entries.)
2. **Invoke** the workflow scoped to the whole board, passing args through verbatim plus the minted `runid`:
   `Workflow({ name: 'walk-issue', args: { scope: 'all', N, implement, K, runid } })` (omitted user-facing args take the defaults above).
3. **Relay** the structured return: the heartbeat digest (idle-nothing-actionable / walked-N / parked-blocked-M / machinery-error / precondition-skipped), the dispatch count (`rounds`), the per-issue `{ issue, fromStage, toState, outcome }` rows (advanced and parked), what was implemented+closed, and anything left `blocked`.
4. **Do not** re-implement any workflow logic here and **do not** mutate GitHub directly — the workflow owns every label/comment/push/close.

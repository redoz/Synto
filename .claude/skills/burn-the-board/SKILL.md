---
name: burn-the-board
description: Burn the issue-flow board down by proximity-to-done — keep the N issues closest to done (ready > plan-review-queued > … > brainstorm-queued) advancing in a rolling worker pool, each by EXACTLY ONE stage via the matching issue-* skill, refilling every freed slot from a fresh re-sorted snapshot the instant any issue finishes, until nothing is actionable (everything done or everything blocked). ready→implement (default-ON) lands the plan and closes the issue, taking items off the board fastest. A simpler, priority-driven alternative to /issue-drive (no snapshot-as-goal, no reconcile, no intake). Wraps the burn-the-board workflow; runs continuously via `/loop /burn-the-board`. Part of the issue-planning flow.
user_invocable: true
---

# burn-the-board

Thin wrapper over the `burn-the-board` workflow. All orchestration lives in the workflow; this skill mints a fresh `runid`, invokes it, and relays the structured return.

**What it is.** A **priority loop** driven by a **rolling worker pool of width N**: snapshot the open issues, start advancing the **N closest-to-done** actionable ones (each by **exactly one stage**), and the instant **any** one finishes, refill its freed slot from a **fresh re-sorted snapshot** — so a slow stage (e.g. a long `implement`) never leaves the other slots idle. It prioritizes issues nearest the end of the pipeline so items leave the board fastest, and stops at the **fixpoint**: nothing actionable and nothing in flight (**everything is done or everything is blocked**).

**How it differs from `/issue-drive`.** Same stage skills, simpler scheduling. `/issue-drive` freezes a `ready` snapshot as its goal and drains every Phase-1 queue to a fixpoint over many passes, with reconcile (un-park blocked issues on a human reply), intake (stamp new issues), and exit-side commit-gates. `burn-the-board` carries **none** of that: it just burns down what is **already actionable** by proximity. It keeps the "Safe" essentials — infra probe, ff-sync, a minimal crash-reaper, per-unit self-healing (`issue-flow-bug`) — and relies on each skill's own **entry** unaddressed-comment guard rather than an exit-side gate. Reach for `/issue-drive` when you need reconcile/intake/the full commit-gate; reach for `/burn-the-board` to quickly clear the board.

**Read first:** `.claude/rules/github.md` — § The autonomous driver (the trust assumption — burn-the-board shares it), § Queues vs Doing (what is pulled), § The unaddressed-comment guard.

**On tooling failure** (the workflow throws / a `gh`/git call breaks — *not* a stage legitimately parking `blocked`): the workflow self-heals (files/recurs a de-duped `issue-flow-bug`). If invoking the workflow itself fails, print the error and stop — never report the report.

## Usage

- `/burn-the-board` — one burn to fixpoint (nothing actionable left), then exit.
- `/loop /burn-the-board` — continuous mode: self-paced, ticks serialized. The next tick re-checks the board, so anything a human un-blocks (or `/issue-drive`'s reconcile re-queues) between ticks is picked up.

## Args

| Arg | Default | Meaning |
|---|---|---|
| `N` | `3` | Worker-pool width — max issues advancing **concurrently** at any instant (the "N"). The pool is rolling: as soon as one issue finishes, its slot is refilled from a fresh snapshot, so a slow stage never idles the others. Each runs in its own worktree where it commits, so concurrent stages never stomp the primary checkout. Raise to burn faster; lower to `1` for strictly sequential. Concurrency is bounded by the per-stage `MAX_ATTEMPTS` push-race cap — back off if it is contended. |
| `implement` | `true` | Include the `ready`→implement→**close** step (run TDD, land on `main`, close the issue) — the step that actually takes items off the board. **Default-ON — approved-plan TDD diffs land on `main` unseen** (deliberate; github.md § The autonomous driver — a `manual` label on an issue still excludes it). `false` = stop at the `ready` gate (authoring/review only; `ready` plans pile up for a human or `/issue-implement`). |
| `dryRun` | `false` | Mutation-free preview across the whole loop: no authoring commit/push, no `set_status`, no promotion, no implement, no close — only `log()` of every would-be action. |
| `K` | `5` | Per-stage review round limit (github.md § The round limit K). A review cycle parks `blocked` (leaves the actionable set) on its Kth failure — this is the loop's termination guarantee for review cycles. |

## Steps

1. **Mint a fresh `runid`** (the workflow runtime forbids `Date.now()`/`Math.random()`, so the run id and any wall-clock value must be supplied by the caller). Run, via the Bash tool:
   `date -u +%Y%m%dT%H%M%SZ`
   and use its output as `runid`. (Each invocation — including each `/loop` tick — gets a fresh one; it scopes the run's worktree paths and run-log entries.)
2. **Invoke** the workflow, passing args through verbatim plus the minted `runid`:
   `Workflow({ name: 'burn-the-board', args: { N, implement, dryRun, K, runid } })` (omitted user-facing args take the defaults above).
3. **Relay** the structured return: the heartbeat digest (idle-nothing-actionable / burned-N / parked-blocked-M / machinery-error / precondition-skipped), the dispatch count (`rounds`), the per-issue `{ issue, fromStage, toState, outcome }` rows (advanced and parked), what was implemented+closed, and anything left `blocked`.
4. **Do not** re-implement any workflow logic here and **do not** mutate GitHub directly — the workflow owns every label/comment/push/close.

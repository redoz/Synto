---
name: issue-drive
description: Autonomously drain the issue-flow board — pull every available issue and push each as far as it can go in one background run (brainstorm/spec-review/plan/plan-review authoring + routing, reconcile, and by default implement approved ready plans), stopping only where a human is genuinely required. Wraps the issue-flow-drive workflow; runs continuously via `/loop /issue-drive`. Part of the issue-planning flow.
user_invocable: true
---

# issue-drive

Thin wrapper over the `issue-flow-drive` workflow — the "composing workflow" that drains the board to a fixpoint in one run. All orchestration logic lives in the workflow; this skill invokes it and relays the structured return.

**Read first:** `.claude/rules/github.md` — § The autonomous driver (semantics + the trust assumption), § Queues vs Doing (what is pulled), § The unaddressed-comment guard (the commit-gate).

**On tooling failure** (the workflow throws / a `gh`/git call breaks — *not* a stage legitimately parking `blocked`): the workflow self-heals (files/recurs a de-duped `issue-flow-bug`, github.md § Reporting a broken skill). If invoking the workflow itself fails, print the error and stop — never report the report.

## Usage

- `/issue-drive` — one drive to fixpoint, then exit.
- `/loop /issue-drive` — continuous mode: self-paced, ticks serialized (never overlapping). The terminal mode for unattended operation.

## Args

| Arg | Default | Meaning |
|---|---|---|
| `autoImplement` | `true` | Run Phase 2: implement human-approved `status:ready` plans and close their issues. **Default-ON — plan-approval is then the only human gate; the TDD diffs land on `main` unseen** (deliberate; github.md § The autonomous driver). `false` opts back to a Phase-1-only drive that stops at the `ready` gate. |
| `dryRun` | `false` | Mutation-free preview across **all** of Phase 1 and Phase 2: no authoring commit/push, no `set_status`, no promotion, no `/issue-respond`, no close — only `log()` of every would-be action. |
| `K` | `5` | Per-stage review round limit (github.md § The round limit K). |
| `maxParallel` | `1` | Width cap on **both** the Phase 1 stage fan-out and the Phase 2 implement fan-out — issues/plans worked concurrently. `1` = strictly sequential (one stage / one plan at a time). Raise (e.g. `2`) to drain faster; each concurrent plan runs in its own `implement-plan` worktree integrating onto `origin/main` via the per-green-commit rebase + ff-push loop. Concurrency is bounded by the per-plan `MAX_ATTEMPTS` push-race cap — back off to `1` if it is contended. |

## Steps

1. **Mint a fresh `runid`** (the workflow runtime forbids `Date.now()`/`Math.random()`, so the run id and any wall-clock value must be supplied by the caller). Run, via the Bash tool:
   `date -u +%Y%m%dT%H%M%SZ`
   and use its output as `runid`. (Each invocation — including each `/loop` tick — gets a fresh one; it scopes the run's worktree paths and run-log entries.)
2. **Invoke** the workflow, passing args through verbatim plus the minted `runid`:
   `Workflow({ name: 'issue-flow-drive', args: { autoImplement, dryRun, K, maxParallel, runid } })` (omitted user-facing args take the defaults above).
3. **Relay** the structured return: the per-tick heartbeat digest (idle-at-fixpoint / worked-N / parked-blocked-M / machinery-error / precondition-skipped), the per-issue `{ issue, fromStage, toState, outcome }` rows, what was implemented+closed, and anything left `blocked`.
4. **Do not** re-implement any driver logic here and **do not** mutate GitHub directly — the workflow owns every label/comment/push/close.

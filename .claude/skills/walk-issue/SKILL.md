---
name: walk-issue
description: Walk ONE issue (or one epic's children) through the issue-flow board to terminal — closed/done or blocked — by advancing it stage-by-stage via the matching issue-* skill (brainstorm → spec-review → plan → plan-review → implement → close), so it inherits adaptive rigor. Auto-detects an epic target and walks its open children, rolling up the epic when all close. The single-issue primitive of the issue flow; /burn-the-board is the same workflow over the whole board. Wraps the walk-issue workflow; runs continuously via `/loop /walk-issue <n>`. Part of the issue-planning flow.
user_invocable: true
---

# walk-issue

Thin wrapper over the `walk-issue` workflow — **the** issue-flow loop. The workflow owns all orchestration; this skill resolves the target, mints a fresh `runid`, invokes the workflow scoped to that target, and relays the structured return.

**What it is.** `walk(issue)` is the primitive: drive ONE issue from its current stage to **terminal** — **closed/done**, or **blocked** (needs-you / stuck-after-K / partial-land / posted-questions) — by running each stage via the matching `issue-*` skill and re-reading the issue after each, until it is no longer actionable. Because each stage is **delegated to its skill**, the walk inherits **adaptive rigor** (quick-brainstorm, skip-plan, the floor-protected dimension subset, the one-shot implement tag). `/burn-the-board` is this same workflow run over **every** actionable issue at once; `/walk-issue` scopes it to one target.

**Epic target.** If `<n>` carries the `epic` label, walk-issue resolves its **open children** and walks them (rolling pool, N at a time), then **rollup-closes** the epic if every child ended closed (else leaves it open). An epic carries no `status:*` itself, so it is never walked directly. (A split's children are out of a single-unit walk's scope — re-invoke on the new epic, or use `/burn-the-board`.)

**Read first:** `.claude/rules/github.md` — § The autonomous loops (the single-operator trust assumption walk-issue shares), § Queues vs Doing (what is pulled), § The unaddressed-comment guard; and `.claude/rules/rigor.md` (the adaptive-rigor knobs each stage applies).

**On tooling failure** (the workflow throws / a `gh`/git call breaks — *not* a stage legitimately parking `blocked`): the workflow self-heals (files/recurs a de-duped `issue-flow-bug`, github.md § Reporting a broken skill). If invoking the workflow itself fails, print the error and stop — never report the report.

## Usage

- `/walk-issue <n>` — walk issue (or epic) `<n>` to terminal, then exit.
- `/loop /walk-issue <n>` — continuous: re-checks `<n>` each tick (picks up anything you un-block between ticks). Stops being useful once `<n>` is closed.

## Args

| Arg | Default | Meaning |
|---|---|---|
| *target* `<n>` | — (required) | The issue (or epic) number to walk — the first positional argument. |
| `N` | `3` (epic) / `1` (unit) | Worker-pool width — issues walked **concurrently**. A unit has one in-scope issue (N is forced to `1`); for an epic this caps how many children walk at once. Each runs in its own worktree, so concurrent stages never stomp the primary checkout. Concurrency is bounded by the per-stage `MAX_ATTEMPTS` push-race cap — back off if contended. |
| `implement` | `true` | Include the `ready`→implement→**close** step (run TDD, land on `main`, close the issue). **Default-ON — approved-plan TDD diffs land on `main` unseen** (deliberate; github.md § The autonomous loops — a `manual` label on the issue still excludes it). `false` = stop at the `ready` gate. |
| `K` | `5` | Per-stage review round limit (github.md § The round limit K). A review cycle parks `blocked` (leaving the actionable set) on its Kth failure — the walk's termination guarantee for review cycles. |

## Steps

1. **Resolve the target** `<n>` from the command argument (the first integer). If none was given, ask which issue to walk (or suggest `/burn-the-board` for the whole board) — do **not** guess.
2. **Detect epic vs unit** (the workflow also auto-promotes, but choosing here lets us set N): run, via the Bash tool, `gh issue view <n> --json labels` and check whether the labels include `epic`.
3. **Mint a fresh `runid`** (the workflow runtime forbids `Date.now()`/`Math.random()`, so the run id must be supplied by the caller): run `date -u +%Y%m%dT%H%M%SZ` via the Bash tool and use its output.
4. **Invoke** the workflow, scoped to the target, passing args through verbatim plus the minted `runid`:
   - **unit:** `Workflow({ name: 'walk-issue', args: { only: [<n>], N: 1, implement, K, runid } })`
   - **epic:** `Workflow({ name: 'walk-issue', args: { epic: <n>, N, implement, K, runid } })`
   (omitted user-facing args take the defaults above).
5. **Relay** the structured return: the heartbeat digest (idle-nothing-actionable / walked-N / parked-blocked-M / machinery-error / precondition-skipped), the per-issue `{ issue, fromStage, toState, outcome }` rows (advanced and parked), what was implemented+closed, the epic rollup result (epic mode), and anything left `blocked`.
6. **Do not** re-implement any workflow logic here and **do not** mutate GitHub directly — the workflow owns every label/comment/push/close.

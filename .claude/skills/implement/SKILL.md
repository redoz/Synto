---
name: implement
description: Implement ONE approved Synto implementation plan end-to-end by driving the implement-plan workflow. Takes the plan's path (or slug) under docs/superpowers/plans/, runs preflight, hands it to the batched self-rotating engine (per-unit green-gate + bookmark-B integration + deep end-review), and reports what landed. Use when a written plan is ready to build.
user_invocable: true
---

# implement

The Synto-specific entry point for implementing **one** approved plan. It resolves the
plan path, then hands it to the **`implement-plan` workflow**
(`.claude/workflows/implement-plan.js`) — the agnostic batched engine that owns all the
orchestration (the self-rotating green-unit loop, `Plan-Tasks` commit-trailer progress, the
completeness gate, per-unit integration onto bookmark **B**, and the deep end-review).

**Read first (on demand):** `CLAUDE.md` (jj, the green gate, no AI footer, experimental
branches) and — only if you need the severity bar — `.claude/playbook/project-phase.md`. You
do **not** pre-read the plan; the workflow's setup step parses it.

## What to pass

The user gives you a plan reference. Resolve it to the **exact top-level path**
`docs/superpowers/plans/<file>.md` (top level **only** — never `drafts/`, never
`completed/`, never a subdirectory). If they gave a slug or bare filename, find the matching
top-level file. If no such top-level plan exists, **stop** and say so (an un-promoted draft is
not ready — it must be moved to the top level first).

## Run it

Call the **Workflow tool** with the resolved path:

```
Workflow({ name: 'implement-plan', args: { plan: 'docs/superpowers/plans/<file>.md' } })
```

- **Mode.** Default is `integrate` (per-unit CI that advances bookmark **B**). Pass
  `args.mode = 'dry-run'` to implement + gate per unit **without** advancing the bookmark or
  pushing — use this for a rehearsal.
- **Push gating is an environment knob, not an arg.** In the default LOCAL mode nothing is
  pushed; the work lands on **B** locally and the operator's working copy follows it via jj
  auto-rebase. Only `SYNTO_FLOW_INTEGRATE=push` in the environment makes the engine also
  `jj git push`. Per `CLAUDE.md`, experimental work stays on an `experimental/*` line — do
  **not** set push mode unless the operator explicitly asked for it.
- **Plan-review checklist (optional).** If a plan-review pass produced critical/high concerns,
  pass them deduped as `args.planReviewConcerns: ["[high] …", …]` — the deep end-review uses
  them as a focusing checklist. Omit for a standalone plan.

The engine is **plan-file-driven**: it reads no issue and no board. It runs preflight
(`base-branch.sh` resolves bookmark **B**; `probe.sh` checks the jj working copy), creates an
isolated jj workspace rooted at **B**, then a single batched self-rotating implementer burns
small green **units** (≈one plan task each): a fast commit-stage build → `jj commit` carrying
`Plan:`/`Plan-Tasks:` trailers (these are machine-readable progress markers, **not** an AI
footer — keep the message otherwise footer-clean) → `integrate.sh`, which rebases the stack
onto the latest **B**, runs the **full** green-gate (`dotnet build` · `dotnet test` ·
`dotnet format whitespace --verify-no-changes`) on the rebased tree, and advances **B**. It
self-rotates near its quality zone; a fresh generation resumes mechanically from the trailers
until every task has landed. Then one **deep end-review** of the cumulative diff fix-forwards
the critical/high/medium defects this change introduced, and the plan file is archived to
`completed/`.

## Present the result

The workflow returns a single `planResult`. Lead with the outcome:

- **`merged === true && reason === 'merged + archived'`** — success. Report the landed commit
  ids (`planResult.commits`) and `pushedTasks`. Surface `planResult.reviewWarnings` (unresolved
  introduced mediums the review proceeded past) and `planResult.reviewDeferred` (pre-existing /
  pre-release-deferred findings) as plain informational notes if non-empty — never auto-file them.
- **Anything else** — report `reason` + `problems` and what to do next. The engine resumes
  mechanically from the trailers, so for most parks a **bare retry is safe**:
  - **Resumable park** — `no progress (stuck)` / `generation cap reached`: some units may
    already be on **B**; a fresh run reads the trailers and continues the un-landed tasks (it
    will not redo landed work). Skim *why* it stuck before retrying.
  - **Escalation** — `reason` starts with `escalated:`: the engine judged the **plan
    wrong/infeasible**, or `integrate.sh` hit an unresolvable **rebase conflict** or a
    **push-rejected** remote. A human fixes the plan / resolves the conflict / reconciles the
    remote first; landed units stay on **B**.
  - **Transient/environment park** — `reason` mentions `transient/environment` (a NuGet restore
    blip or a build-host file lock under concurrent workspaces): not a code defect; retry once
    the environment settles.
  - **`{ aborted: true }`** — preflight failed (no bookmark **B**, or the jj working copy has
    conflicts). Fix the precondition (`probe.sh`'s `reason` says which) and re-run.

## Notes

- All code lands **inside the workflow's isolated jj workspace** and advances bookmark **B**;
  this skill makes **no** commit in the operator's default working copy and must leave it clean.
- One plan per run, by exact path — no other plan is ever swept.

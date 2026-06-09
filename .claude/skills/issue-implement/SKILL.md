---
name: issue-implement
description: Implement the one ready (promoted) plan for issue <n> via the implement-plan workflow (strict red-green TDD per task + green-gate + per-green-commit CI ff-push to main), then comment the merged commits and close <n> (and any fully-rolled-up parent). Operates on exactly <n>. Refuses non-ready issues. Part of the issue-planning flow.
user_invocable: true
---

# issue-implement

Closes the loop for **one** issue: implements issue `<n>`'s ready plan and reconciles `<n>` on GitHub. Like every other `issue-*` skill, it operates on **exactly the `<n>` you pass** — never the whole `status:ready` set. (The driver implements many ready issues by *looping* this one-issue stage, not by batching.)

**Read first:** `.claude/rules/github.md` (the Implement comment template, § work-graph linkage, § Working-tree hygiene).

**On tooling failure** (a `gh`/git/tool error, exception, or broken reference — *not* the work itself failing, which parks `blocked` per step 6): follow github.md § Reporting a broken skill (search-first; file/recur a de-duped `issue-flow-bug`).

## Steps

1. **Guard (fail closed).** `gh issue view <n> --json labels,comments`. If `status:ready` is **absent**, refuse: print "Issue #<n> is not status:ready — promote its plan first via /issue-respond." and stop.
   - **Unaddressed-comment guard** (github.md § The unaddressed-comment guard): if a human comment sits *after* the last `##` skill comment on `<n>` and is **not** a clear go-ahead/ack, do **not** implement — `set_status <n> implementing block`, post the `## 🛑 Needs you — unaddressed comment` note, and **stop**. A go-ahead/ack means the discussion is resolved; continue normally.
2. **Resolve `<n>`'s plan slug.** Find `<n>`'s **promoted** plan — the file directly under `docs/superpowers/plans/*.md` (top level only, **not** `drafts/`/`completed/`) whose header has `> **Tracking issue:** #<n>` — and take its slug (filename minus the `YYYY-MM-DD-` date prefix and `.md`). If `<n>` has **no** top-level plan it isn't actually promoted: refuse — print "Issue #<n> has no promoted top-level plan — promote it via /issue-respond." and stop (never implement an un-promoted issue).
3. **Claim →** `set_status <n> implementing` (single-select; clear `blocked`), marking `<n>` owned before any code lands.
4. **Implement (scoped to `<n>`'s one plan).** `Workflow({ name: 'implement-plan', args: { plan: <the resolved top-level plan path from step 2>, mode: 'merge' } })` — `implement-plan` takes exactly **one** plan by path, so no other plan (tracked or untracked) is ever swept. It enforces the implement guards: a fresh subagent per TASK doing **strict red-green TDD** (failing test first, then minimal code to green), a **green-gate** after each task, and **continuous integration** — after every GREEN COMMIT it rebases the worktree branch onto `origin/main`, re-runs the full green-gate, and **fast-forward-pushes the green commit straight to `main`**. It then archives the implemented plan to `completed/`. All code is committed **inside `implement-plan`'s isolated worktree** and ff-pushed; this skill makes **no** commit in the primary checkout and must leave its working tree clean (github.md § Working-tree hygiene).
5. **Reconcile `<n>`.** Read the single `planResult` the workflow returns. If it reports `<n>`'s plan implemented+archived (`merged === true && reason === 'merged + archived'`):
   - Post the **Implement comment** (github.md template) with the merged commit SHAs (auto-linking).
   - `gh issue close <n> && set-status <n> done` — close, then strip any `status:*`/`blocked` label and move the board card to Done (github.md § Setting state).
6. **Parent rollup (§ work-graph).** If `<n>` has a parent and **all** of that parent's sub-issues are now closed, post a brief rollup comment and `gh issue close <parent>`.
7. **On failure.** If the workflow throws/returns `{aborted:true}`, or `<n>`'s plan does not implement, leave `<n>` at `status:implementing` with `blocked` and a failure comment carrying the error — **never** close it, and never strand it un-`blocked` (an `implementing`+`blocked` issue is recoverable: a human go-ahead routes it back to retry). One caveat the driver handles and a manual run should know: if `implement-plan` reports it landed **some** task increments on `main` then stopped (a **partial land**), that is **terminal** — a bare retry would write a failing test for code that already exists. Recover by trimming the promoted plan to the un-landed tasks (or reverting the landed commits) before retrying.
8. **Report** whether `<n>` was closed or left `blocked`.

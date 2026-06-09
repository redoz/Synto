---
name: issue-plan-review
description: Run the full review team over a drafted plan via the issue-review workflow, post the consolidated verdict comment, and route the issue (LGTM→promote plan + advance to ready automatically [park for approval only if manual], NEEDS WORK→re-plan, RETHINK→re-brainstorm, round-limit→stuck). Part of the issue-planning flow.
user_invocable: true
---

# issue-plan-review

Thin wrapper over the `issue-review` workflow for the **plan** gate.

**Read first:** `.claude/rules/github.md` (the `set_status` procedure, the round limit `K`, the routing in § state machine) and `.claude/rules/standards.md` (verdict definitions).

**On tooling failure** (a `gh`/git/tool error, exception, or broken reference — *not* the work itself failing): follow github.md § Reporting a broken skill (search-first; file/recur a de-duped `issue-flow-bug`).

## Steps

1. **Read state & K.** `gh issue view <n> --json labels,comments`. Take the round limit `K` from github.md (default 5). Note whether `manual` is set.
   - **Unaddressed-comment guard** (github.md § The unaddressed-comment guard): if a human comment sits *after* the last `##` skill comment and is **not** a clear go-ahead/ack, do **not** review — `set_status <n> plan-reviewing block`, post the `## 🛑 Needs you — unaddressed comment` note, and **stop**. A go-ahead/ack means the discussion is resolved; continue normally.
2. **Set state →** `status:plan-reviewing` (single-select; clear `blocked`).
3. **Run the review.** `Workflow({ name: 'issue-review', args: { issue: <n>, kind: 'plan', roundLimit: K } })` → `{ verdict, comment, questions, counts, round }`.
4. **Post** the returned `comment` verbatim: `gh issue comment <n> --body "<comment>"`.
5. **Route** (github.md § Approval detection):
   - **`manual` set** → stay `status:plan-reviewing`, set `blocked` (the brake — the human steps via `/issue-respond` regardless of verdict). Stop. **This is the only human approval gate; everything below assumes non-`manual`.**
   - **LGTM** (non-`manual`) → **promote + advance automatically, no human gate**: promote the plan per github.md § Artifact storage & promotion (`git mv` the draft → top-level `docs/superpowers/plans/{slug}.md`, edit the `## 📋 Plan` comment link to the promoted path, then **commit only the renamed paths** — `git commit -m "docs(plan): promote {slug} (LGTM on #<n>)" -- docs/superpowers/plans/{slug}.md docs/superpowers/plans/drafts/{slug}.md` (the `git mv`-staged rename; **never** `git add -A`/`.`/`-u`/`commit -a` — github.md § Working-tree hygiene), then `git push`), then `set_status <n> ready` (no `blocked`). Promotion to top-level `plans/` *is* the ready signal `implement-plan` keys on. **Consequence:** with the driver's `autoImplement` on, a `ready` plan is then implemented to `main` with no further human step — set `manual` if you want eyes on the plan first.
   - **NEEDS WORK** and `round < K` → `set_status <n> plan-queued` (auto re-plan; no `blocked`).
   - **RETHINK** and `round < K` → `set_status <n> brainstorm-queued` (the spec is wrong — cross-stage escape; no `blocked`).
   - **`round ≥ K`** with any failing verdict → stay `status:plan-reviewing`, set `blocked`, and append a `🛑 stuck after K rounds — needs you` comment.
6. **Report** to the user: verdict, `round N/K`, the counts, and the routing taken.

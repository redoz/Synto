---
name: issue-plan-review
description: Run the review team over a drafted plan via the issue-review workflow (optionally a floor-protected dimension subset), post the consolidated verdict comment, and route the issue (LGTM→promote plan + tag its implement rigor [one-shot/tdd-per-task] + advance to ready automatically [park for approval only if manual], NEEDS WORK→re-plan, RETHINK→re-brainstorm, round-limit→stuck). Part of the issue-planning flow.
user_invocable: true
---

# issue-plan-review

Thin wrapper over the `issue-review` workflow for the **plan** gate.

**Read first:** `.claude/rules/github.md` (the `set_status` procedure, the round limit `K`, the routing in § state machine), `.claude/rules/standards.md` (verdict definitions), and `.claude/rules/rigor.md` (the plan-review **team** subset + **implement** tag knobs, the floor, the H2 recording format, the `> **Rigor:**` tag, and the `manual` ⇒ full rule).

**On tooling failure** (a `gh`/git/tool error, exception, or broken reference — *not* the work itself failing): follow github.md § Reporting a broken skill (search-first; file/recur a de-duped `issue-flow-bug`).

## Steps

1. **Read state & K.** `gh issue view <n> --json labels,comments`. Take the round limit `K` from github.md (default 5). Note whether `manual` is set.
   - **Unaddressed-comment guard** (github.md § The unaddressed-comment guard + § Trust boundary): run `bash .claude/scripts/trust.sh new-human <n>` — it returns the trusted, human, unaddressed comments (untrusted/skill comments filtered out mechanically). If it is **non-empty** and its latest comment is **not** a clear go-ahead/ack, do **not** review — `set_status <n> plan-reviewing block`, post the `## 🛑 Needs you — unaddressed comment` note, and **stop**. Empty (or a go-ahead/ack) means the discussion is resolved; continue normally.
2. **Set state →** `status:plan-reviewing` (single-select; clear `blocked`).
3. **Rigor — team subset** (rigor.md, the **review → subset** knob). Read the drafted plan (linked from its `## 📋 Plan` comment). If the plan touches a **narrow concern** and clearly cannot implicate some dimensions (e.g. an internal-helper rename plan → maintainability + principal-engineer, dropping performance + testability), pick the subset of the 5 reviewers — **always keeping correctness** (the floor; `issue-review.js` also force-includes it as a backstop). Here the subset genuinely **shrinks the 5-agent fan-out**. If `manual` is set, run the full team. When you subset, post one **`## ⚡ Rigor — plan-review: subset`** comment naming which ran and which were dropped + why.
4. **Run the review.** `Workflow({ name: 'issue-review', args: { issue: <n>, kind: 'plan', roundLimit: K, dimensions: <subset or omit for full> } })` → `{ verdict, comment, questions, counts, round }`. **Upshift:** if a kept reviewer's finding implicates a **dropped** dimension, re-run with that dimension added and record a `## ⚡ Rigor — plan-review: upshift` comment.
5. **Post** the returned `comment` verbatim: `gh issue comment <n> --body "<comment>"`.
6. **Route** (github.md § Approval detection):
   - **`manual` set** → stay `status:plan-reviewing`, set `blocked` (the brake — the human steps via `/issue-respond` regardless of verdict). Stop. **This is the only human approval gate; everything below assumes non-`manual`.**
   - **LGTM** (non-`manual`) → **promote + advance automatically, no human gate**. First make the **implement-rigor decision** (rigor.md, the **plan-review → implement** knob): is this change a single concern, small, and diff-reviewable with no new logic branches that want test-first design → **`one-shot`**, else **`tdd-per-task`** (confirm or correct any advisory `> **Rigor:**` line `issue-plan` left in the draft; if `manual`, force `tdd-per-task`). Then promote per github.md § Artifact storage & promotion: `git mv` the draft → top-level `docs/superpowers/plans/{slug}.md`; **set the authoritative tag** in the promoted file's header — for `one-shot` ensure the line `> **Rigor:** one-shot` is present (just under the `> **Tracking issue:**`/`> **Spec:**` lines), for `tdd-per-task` ensure **no** `> **Rigor:** one-shot` line remains (absent ⇒ `tdd-per-task`, the default `implement-plan` reads); edit the `## 📋 Plan` comment link to the promoted path; then **commit only the renamed/edited plan paths** — `git add docs/superpowers/plans/{slug}.md` (the content edit) and `git commit -m "docs(plan): promote {slug} (LGTM on #<n>)" -- docs/superpowers/plans/{slug}.md docs/superpowers/plans/drafts/{slug}.md` (the `git mv`-staged rename + tag edit; **never** `git add -A`/`.`/`-u`/`commit -a` — github.md § Working-tree hygiene), then `git push`. Finally `set_status <n> ready` (no `blocked`), and if `one-shot` post one **`## ⚡ Rigor — plan-review: one-shot`** comment with the rationale. Promotion to top-level `plans/` *is* the ready signal `implement-plan` keys on. **Consequence:** with the driver's `autoImplement` on, a `ready` plan is then implemented to `main` with no further human step (a `one-shot` plan implements in one pass behind the floor — green-gate + final diff-review) — set `manual` if you want eyes on the plan first.
   - **NEEDS WORK** and `round < K` → `set_status <n> plan-queued` (auto re-plan; no `blocked`).
   - **RETHINK** and `round < K` → `set_status <n> brainstorm-queued` (the spec is wrong — cross-stage escape; no `blocked`).
   - **`round ≥ K`** with any failing verdict → stay `status:plan-reviewing`, set `blocked`, and append a `🛑 stuck after K rounds — needs you` comment.
7. **Report** to the user: verdict, `round N/K`, the counts, the rigor decisions (subset / implement tag / full), and the routing taken.

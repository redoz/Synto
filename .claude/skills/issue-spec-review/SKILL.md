---
name: issue-spec-review
description: Run the full review team over a drafted spec via the issue-review workflow, post the consolidated verdict comment, and route the issue (LGTM→promote spec + advance to plan-queued automatically [park for approval only if manual], NEEDS WORK/RETHINK→re-brainstorm, round-limit→stuck). Part of the issue-planning flow.
user_invocable: true
---

# issue-spec-review

Thin wrapper over the `issue-review` workflow for the **spec** gate. The spec gate runs as a **single consolidated review agent** — one agent reads the spec + the touched code once and judges across all 4 dimensions + principal-engineer, rather than the 5-reviewer fan-out the plan gate uses. This is a deliberate POC token-budget choice; it applies the **POC-phase severity calibration** in `.claude/rules/project-phase.md` (be realistic about findings until we declare MVP).

**Read first:** `.claude/rules/github.md` (the `set_status` procedure, the round limit `K`, the routing in § state machine), `.claude/rules/standards.md` (verdict definitions), and `.claude/rules/project-phase.md` (the POC→MVP severity calibration the review applies).

**On tooling failure** (a `gh`/git/tool error, exception, or broken reference — *not* the work itself failing): follow github.md § Reporting a broken skill (search-first; file/recur a de-duped `issue-flow-bug`).

## Steps

1. **Read state & K.** `gh issue view <n> --json labels,comments`. Take the round limit `K` from github.md (default 5). Note whether `manual` is set.
   - **Unaddressed-comment guard** (github.md § The unaddressed-comment guard): if a human comment sits *after* the last `##` skill comment and is **not** a clear go-ahead/ack, do **not** review — `set_status <n> spec-reviewing block`, post the `## 🛑 Needs you — unaddressed comment` note, and **stop**. A go-ahead/ack means the discussion is resolved; continue normally.
2. **Set state →** `status:spec-reviewing` (single-select; clear `blocked`).
3. **Run the review.** `Workflow({ name: 'issue-review', args: { issue: <n>, kind: 'spec', roundLimit: K } })` → `{ verdict, comment, questions, counts, round }`.
4. **Post** the returned `comment` verbatim: `gh issue comment <n> --body "<comment>"`.
5. **Route** (github.md § Approval detection):
   - **`manual` set** → stay `status:spec-reviewing`, set `blocked` (the brake — the human steps via `/issue-respond` regardless of verdict). Stop. **This is the only human approval gate; everything below assumes non-`manual`.**
   - **LGTM** (non-`manual`) → **promote + advance automatically, no human gate**: promote the spec per github.md § Artifact storage & promotion (`git mv` the draft → top-level `docs/superpowers/specs/{slug}.md`, edit the `## 📐 Spec` comment link to the promoted path, then **commit only the renamed paths** — `git commit -m "docs(spec): promote {slug} (LGTM on #<n>)" -- docs/superpowers/specs/{slug}.md docs/superpowers/specs/drafts/{slug}.md` (the `git mv`-staged rename; **never** `git add -A`/`.`/`-u`/`commit -a` — github.md § Working-tree hygiene), then `git push`), then `set_status <n> plan-queued` (no `blocked`). The strict review team's LGTM **is** the gate.
   - **NEEDS WORK or RETHINK** and `round < K` → `set_status <n> brainstorm-queued` (refine the spec; no `blocked`). (Both failing verdicts route here — for a spec, a wrong design is fixed by re-brainstorming.)
   - **`round ≥ K`** with any failing verdict → stay `status:spec-reviewing`, set `blocked`, and append a `🛑 stuck after K rounds — needs you` comment.
6. **Report** to the user: verdict, `round N/K`, the counts, and the routing taken.

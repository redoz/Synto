---
name: issue-spec-review
description: Run the review team over a drafted spec via the issue-review workflow (optionally a floor-protected dimension subset), post the consolidated verdict comment, and route the issue (LGTM→promote spec + advance to plan-queued, or skip-plan straight to ready with a thin one-shot plan derived inline [park for approval only if manual], NEEDS WORK/RETHINK→re-brainstorm, round-limit→stuck). Part of the issue-planning flow.
user_invocable: true
---

# issue-spec-review

Thin wrapper over the `issue-review` workflow for the **spec** gate. The spec gate runs as a **single consolidated review agent** — one agent reads the spec + the touched code once and judges across all 4 dimensions + principal-engineer, rather than the 5-reviewer fan-out the plan gate uses. This is a deliberate POC token-budget choice; it applies the **POC-phase severity calibration** in `.claude/rules/project-phase.md` (be realistic about findings until we declare MVP).

**Read first:** `.claude/rules/github.md` (the `set_status` procedure, the round limit `K`, the routing in § state machine), `.claude/rules/standards.md` (verdict definitions), `.claude/rules/project-phase.md` (the POC→MVP severity calibration the review applies), and `.claude/rules/rigor.md` (the spec-review **team** subset + **skip-plan** knobs, the floor, the H2 recording format, § Skip-plan, and the `manual` ⇒ full rule).

**On tooling failure** (a `gh`/git/tool error, exception, or broken reference — *not* the work itself failing): follow github.md § Reporting a broken skill (search-first; file/recur a de-duped `issue-flow-bug`).

## Steps

1. **Read state & K.** `gh issue view <n> --json labels,comments`. Take the round limit `K` from github.md (default 5). Note whether `manual` is set.
   - **Unaddressed-comment guard** (github.md § The unaddressed-comment guard + § Trust boundary): run `bash .claude/scripts/trust.sh new-human <n>` — it returns the trusted, human, unaddressed comments (untrusted/skill comments filtered out mechanically). If it is **non-empty** and its latest comment is **not** a clear go-ahead/ack, do **not** review — `set_status <n> spec-reviewing block`, post the `## 🛑 Needs you — unaddressed comment` note, and **stop**. Empty (or a go-ahead/ack) means the discussion is resolved; continue normally.
2. **Set state →** `status:spec-reviewing` (single-select; clear `blocked`).
3. **Rigor — team subset** (rigor.md, the **review → subset** knob). Read the drafted spec (linked from its `## 📐 Spec` comment). If the spec **clearly cannot implicate** some dimensions (e.g. a doc-only change can't implicate performance / testability), pick the subset that *can* be implicated — **always keeping correctness** (the floor; `issue-review.js` also force-includes it as a backstop). If `manual` is set, run the full team (no subset). When you subset, post one **`## ⚡ Rigor — spec-review: subset`** comment naming which ran and which were dropped + why. (The spec gate is a single agent, so the subset narrows *what it judges*, not the agent count.)
4. **Run the review.** `Workflow({ name: 'issue-review', args: { issue: <n>, kind: 'spec', roundLimit: K, dimensions: <subset or omit for full> } })` → `{ verdict, comment, questions, counts, round }`. **Upshift:** if a kept reviewer's finding implicates a **dropped** dimension, re-run with that dimension added and record a `## ⚡ Rigor — spec-review: upshift` comment.
5. **Post** the returned `comment` verbatim: `gh issue comment <n> --body "<comment>"`.
6. **Route** (github.md § Approval detection):
   - **`manual` set** → stay `status:spec-reviewing`, set `blocked` (the brake — the human steps via `/issue-respond` regardless of verdict). Stop. **This is the only human approval gate; everything below assumes non-`manual`.**
   - **LGTM** (non-`manual`) → **promote the spec, then make the skip-plan decision** (rigor.md, the **spec-review → plan?** knob). First promote the spec per github.md § Artifact storage & promotion (`git mv` the draft → top-level `docs/superpowers/specs/{slug}.md`, edit the `## 📐 Spec` comment link to the promoted path, then **commit only the renamed paths** — `git commit -m "docs(spec): promote {slug} (LGTM on #<n>)" -- docs/superpowers/specs/{slug}.md docs/superpowers/specs/drafts/{slug}.md` (the `git mv`-staged rename; **never** `git add -A`/`.`/`-u`/`commit -a` — github.md § Working-tree hygiene), then `git push`). The strict review team's LGTM **is** the gate. Then:
     - **skip-plan** (rigor heuristic met, not `manual`: the spec is fully diff-reviewable — no new feature folder/namespace, no cross-cutting pipeline change, no multi-step sequencing, no non-obvious TDD structure) → **derive a thin plan inline** (rigor.md § Skip-plan) and jump straight to `ready`:
       1. Write the thin plan to **top-level** `docs/superpowers/plans/{YYYY-MM-DD-slug}.md` (born promoted — the spec LGTM is its gate; **same slug** as the spec). Immediately after the writing-plans header add the three header lines:
          ```
          > **Tracking issue:** #<n>
          > **Spec:** docs/superpowers/specs/{slug}.md
          > **Rigor:** one-shot
          ```
          Keep it thin — a short description of the change (it may have **no** `### Task` sections; `implement-plan`'s one-shot mode reads the whole plan as the change). Authoring is **read the code + write the one `.md`** (never edit source — github.md § Working-tree hygiene).
       2. Commit **only that one file** (verify `git status --porcelain` shows nothing but it): `git add docs/superpowers/plans/{file}; git commit -m "docs(plan): derive thin one-shot plan for #<n> (skip-plan)"; git push`.
       3. Edit the `## 📐 Spec` comment (or post a follow-up) to note the derived plan path, then `set_status <n> ready` (no `blocked`).
       4. Post one **`## ⚡ Rigor — spec-review: skip-plan`** comment with the one-line rationale. **Upshift:** if while deriving you find the change actually needs decomposition, a new feature area, or sequencing, do **not** write the thin plan — `set_status <n> plan-queued` and record `## ⚡ Rigor — spec-review: upshift` instead.
     - **else** (full path, or `manual`) → `set_status <n> plan-queued` (no `blocked`) as today.
   - **NEEDS WORK or RETHINK** and `round < K` → `set_status <n> brainstorm-queued` (refine the spec; no `blocked`). (Both failing verdicts route here — for a spec, a wrong design is fixed by re-brainstorming.)
   - **`round ≥ K`** with any failing verdict → stay `status:spec-reviewing`, set `blocked`, and append a `🛑 stuck after K rounds — needs you` comment.
7. **Report** to the user: verdict, `round N/K`, the counts, the rigor decisions (subset / skip-plan / full), and the routing taken.

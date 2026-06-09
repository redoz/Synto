---
name: idea
description: Live front door to the issue-planning flow — hash out a raw idea WITH you right now via superpowers:brainstorming, write the spec, file a fresh GitHub issue, and land it at status:spec-review-queued. Use when you have a new idea (no issue exists yet) and want to talk it through in-session rather than async-in-the-issue. Part of the issue-planning flow.
user_invocable: true
---

# idea

The **live, in-conversation** front door to the issue-planning flow. You have a raw idea
and no issue yet; `/idea` brainstorms it **with you right now** (not async-in-the-issue),
writes the resulting spec, files the GitHub issue, and advances it to
`status:spec-review-queued` — handing it straight to the async review team.

This is the bootstrap that `issue-brainstorm`'s "Live alternative" assumes but doesn't
provide: that path needs an issue to already exist; `/idea` starts from nothing.

- **`/idea` vs `issue-triage` / `issue-brainstorm`:** those carry the dialogue
  **asynchronously in issue comments** (for the driver and unattended `/loop` runs).
  `/idea` carries it **live in this session** — for when you're here and want to think it
  through together. All three end inside the same flow.

**Read first:** `.claude/rules/github.md` (the `set_status` procedure, the Spec comment
template, § Artifact storage & promotion, § Working-tree hygiene, § Comment templates
plain-telegraph style) and `.claude/rules/architecture.md` (domain context for the
brainstorm).

**On tooling failure** (a `gh`/git/tool error, exception, or broken reference — *not* the
brainstorm or the work itself failing): follow github.md § Reporting a broken skill
(search-first; file/recur a de-duped `issue-flow-bug`).

## Steps

1. **Read context.** `.claude/rules/architecture.md`, recent commits, and any source the
   idea touches — the standard brainstorming context-gathering, grounded in Synto's
   domain.
2. **Brainstorm live.** Invoke **superpowers:brainstorming** and run its full
   conversation: clarifying questions one at a time → 2–3 approaches with a recommendation
   → present the design → **your approval** → the spec self-review pass. **Override two of
   its defaults** to fit this flow:
   - its terminal step is **this skill's tail (steps 5–7), not `writing-plans`** — `/idea`
     stops at a reviewable spec and hands off to the issue flow;
   - the spec is written to the flow's **`drafts/`** location (step 6), **not** top-level
     `specs/` (top-level is the home of *approved* specs; the spec-review LGTM promotes it
     there later).
3. **Scope guard — one issue ↔ one spec.** If the idea turns out to be several independent
   plannable units, surface that during the brainstorm and **narrow to one cohesive
   unit** — recommend re-running `/idea` per unit (or `/issue-split` once it's in the
   flow). Do **not** auto-decompose here; decomposition has a dedicated home.
4. **User review gate.** Show the drafted spec and proceed only on your explicit OK
   (superpowers:brainstorming's review gate). If you request changes, revise and re-show.
5. **Create the issue.** `gh issue create` with a clear title and a tight body summary in
   **plain telegraph English** (github.md § Comment templates style — name the concrete
   thing, drop the throat-clearing). This yields the tracking number `#N`. (Created here,
   at the end — only after the brainstorm is done — so an abandoned conversation leaves no
   orphan stub.) A fresh issue carries no `status:*`; step 7 stamps it.
6. **Write & commit the spec.** Write `docs/superpowers/specs/drafts/{YYYY-MM-DD-slug}.md`
   using the superpowers:brainstorming spec conventions; immediately after the standard
   header add `> **Tracking issue:** #N`. Authoring is **read the code + write the one
   `.md`** — never edit source to "try it" (any tracked-file trial belongs in a throwaway
   `git worktree`, never the primary checkout — github.md § Working-tree hygiene). Stage
   **only that one file**, assert `git status --porcelain` shows nothing but it (stop and
   report if anything else appears — never `git add -A`/`.`/`-u`/`commit -a`), then commit
   + push to main (primary checkout):
   `git add docs/superpowers/specs/drafts/{file}; git commit -m "docs(spec): draft for #N"; git push`
7. **Comment + advance.** Post the **Spec comment** (github.md template — 3–6 bullet
   summary + the permalink to the committed draft) on `#N`, then advance via the
   set-status script (github.md § Setting state — does the label swap *and* the board card
   move in one command), no `blocked`:
   `bash .claude/scripts/set-status.sh N spec-review-queued`  (or the `.ps1` twin from PowerShell)
8. **Report** the issue URL, the spec path, and the final state (`spec-review-queued`).

## Notes

- **Terminal state is the flow, not `writing-plans`.** superpowers:brainstorming normally
  ends by invoking `writing-plans`; here the spec is instead handed to the async flow
  (`issue-spec-review` reviews it, and `issue-plan` writes the plan after the spec is
  approved). Do **not** invoke `writing-plans`.
- **Deliberately out of scope (YAGNI for the POC front door):** auto-decomposition into
  epics + children (use `/issue-split`), the `manual` brake (a brand-new issue has no
  labels), and async comment Q&A (that's `issue-brainstorm`). Keep `/idea` single-purpose.
- **Dialogue handler.** Like the other front-door skills, `/idea` *is* the conversation —
  the github.md unaddressed-comment guard does not apply (there is no prior issue thread to
  step past; the issue doesn't exist until step 5).

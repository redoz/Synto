---
name: issue-plan
description: Draft or revise the implementation plan for a GitHub issue from its approved spec, commit it to plans/drafts/, comment the summary+link, and advance the issue to plan-review-queued. Part of the issue-planning flow.
user_invocable: true
---

# issue-plan

Takes an issue number `<n>`. Turns the issue's **approved spec** into a plan draft and queues it for review — one stage transition of the issue-planning flow.

**Read first:** `.claude/rules/github.md` (labels + the `set_status` procedure, the Plan comment template, storage/promotion, the brake), `.claude/rules/principles.md`, `.claude/rules/architecture.md`.

**On tooling failure** (a `gh`/git/tool error, exception, or broken reference — *not* the work itself failing): follow github.md § Reporting a broken skill (search-first; file/recur a de-duped `issue-flow-bug`).

## Steps

1. **Read state.** `gh issue view <n> --json number,title,body,labels,comments`. Note the current `status:*` and whether `manual` is set. Locate the **approved** spec — the top-level `docs/superpowers/specs/{slug}.md` linked from the issue's `## 📐 Spec` comment. If you are on `plan-queued` looped back from a failed review, also read the latest `## 🔍 Plan review` comment — you are **revising** an existing `plans/drafts/{slug}.md`, not starting fresh.
   - **Unaddressed-comment guard** (github.md § The unaddressed-comment guard): if a human comment sits *after* the last `##` skill comment and is **not** a clear go-ahead/ack, do **not** draft — `set_status <n> planning block`, post the `## 🛑 Needs you — unaddressed comment` note, and **stop**. A go-ahead/ack means the discussion is resolved; continue normally.
2. **Set state →** `status:planning` (single-select per github.md; do not set `blocked`).
3. **Draft the plan** using the **superpowers:writing-plans** conventions (bite-sized TDD tasks, exact paths, complete code, no placeholders). Write `docs/superpowers/plans/drafts/{YYYY-MM-DD-slug}.md`; immediately after the writing-plans header add:
   ```
   > **Tracking issue:** #<n>
   > **Spec:** docs/superpowers/specs/{slug}.md
   ```
   On a revision, edit the existing draft to address each review finding rather than rewriting wholesale.
4. **Commit + push** the draft to main (primary checkout) so the linked file resolves on GitHub — staging **only that one file** (github.md § Working-tree hygiene: never `git add -A`/`.`/`-u`/`commit -a`; any compile-check or tracked-file trial goes in a throwaway `git worktree`, never the primary checkout; verify `git status --porcelain` shows nothing but this artifact before committing):
   `git add docs/superpowers/plans/drafts/{file}; git commit -m "docs(plan): draft for #<n>"; git push`
5. **Comment** the **Plan comment** (github.md template): a 3–6 bullet summary + the permalink to the committed draft.
6. **Advance.** If `manual` is set: leave `status:planning`, set `blocked`, and note "drafted — waiting for your go-ahead." Otherwise `set_status <n> plan-review-queued` (no `blocked`).

## Scope check (decomposition — github.md §work-graph)

If the spec turns out to cover multiple independent plannable units, do **not** force one plan: run **`/issue-split <n>`** (the single decomposition mechanism — spawns a fresh epic + a child per unit at `brainstorm-queued`, closes `<n>`). (Specs are normally pre-decomposed in brainstorm; this is the backstop.)

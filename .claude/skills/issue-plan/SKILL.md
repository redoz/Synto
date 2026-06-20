---
name: issue-plan
description: Draft or revise the implementation plan for a GitHub issue from its approved spec, commit it to plans/drafts/, comment the summary+link, and advance the issue to plan-review-queued. Part of the issue-planning flow.
user_invocable: true
---

# issue-plan

Takes an issue number `<n>`. Turns the issue's **approved spec** into a plan draft and queues it for review — one stage transition of the issue-planning flow.

**Read first:** `.claude/rules/github.md` (labels + the `set_status` procedure, the Plan comment template, storage/promotion, the brake), `.claude/rules/principles.md`, `.claude/rules/architecture.md`, `.claude/rules/rigor.md` (the **plan → depth** knob: concise plans + the advisory `one-shot` suggestion).

**On tooling failure** (a `gh`/git/tool error, exception, or broken reference — *not* the work itself failing): follow github.md § Reporting a broken skill (search-first; file/recur a de-duped `issue-flow-bug`).

## Steps

1. **Read state.** `gh issue view <n> --json number,title,body,labels,comments`. Note the current `status:*` and whether `manual` is set. Locate the **approved** spec — the top-level `docs/superpowers/specs/{slug}.md` linked from the issue's `## 📐 Spec` comment. If you are on `plan-queued` looped back from a failed review, also read the latest `## 🔍 Plan review` comment — you are **revising** an existing `plans/drafts/{slug}.md`, not starting fresh.
   - **Address the review (mandatory on a revision).** If revising after a NEEDS WORK / RETHINK, read the **latest** `## 🔍 Plan review` comment and treat its findings as a checklist: for **each** finding, either **fix it** in the revised plan **or** add a one-line justification (in the plan or the Plan comment) for why it does not apply. Never silently skip a review finding.
   - **Unaddressed-comment guard** (github.md § The unaddressed-comment guard + § Trust boundary): run `bash .claude/scripts/trust.sh new-human <n>` — it returns the trusted, human, unaddressed comments (untrusted/skill comments filtered out mechanically). If it is **non-empty** and its latest comment is **not** a clear go-ahead/ack, do **not** draft — `set_status <n> planning block`, post the `## 🛑 Needs you — unaddressed comment` note, and **stop**. Empty (or a go-ahead/ack) means the discussion is resolved; continue normally.
2. **Set state →** `status:planning` (single-select per github.md; do not set `blocked`).
3. **Draft the plan** using the **superpowers:writing-plans** conventions (bite-sized TDD tasks, exact paths, complete code, no placeholders). **Rigor — depth** (rigor.md, the **plan → depth** knob, light touch): scale the plan to the spec — a small single-concern spec gets a **concise** plan (fewer tasks, less ceremony), not padded boilerplate; a complex spec gets the full treatment. Write `docs/superpowers/plans/drafts/{YYYY-MM-DD-slug}.md`; immediately after the writing-plans header add:
   ```
   > **Tracking issue:** #<n>
   > **Spec:** docs/superpowers/specs/{slug}.md
   ```
   **Advisory rigor suggestion:** if the change is clearly a single concern, small, and diff-reviewable (no new logic branches that want test-first design), add a third header line `> **Rigor:** one-shot` as a *suggestion* — `issue-plan-review` makes the authoritative call on promotion (rigor.md, the **plan-review → implement** knob), and the default when the line is absent is `tdd-per-task`. Mention the suggestion in the Plan comment; no separate H2 is needed at this stage. On a revision, edit the existing draft to address each review finding rather than rewriting wholesale.
4. **Commit + push** the draft to main (primary checkout) so the linked file resolves on GitHub — staging **only that one file** (github.md § Working-tree hygiene: never `git add -A`/`.`/`-u`/`commit -a`; any compile-check or tracked-file trial goes in a throwaway `git worktree`, never the primary checkout; verify `git status --porcelain` shows nothing but this artifact before committing):
   `git add docs/superpowers/plans/drafts/{file}; git commit -m "docs(plan): draft for #<n>"; git push`
5. **Comment** the **Plan comment** (github.md template): a 3–6 bullet summary + the permalink to the committed draft.
6. **Advance.** If `manual` is set: leave `status:planning`, set `blocked`, and note "drafted — waiting for your go-ahead." Otherwise `set_status <n> plan-review-queued` (no `blocked`).

## Scope check (decomposition — github.md §work-graph)

If the spec turns out to cover multiple independent plannable units, do **not** force one plan: run **`/issue-split <n>`** (the single decomposition mechanism — spawns a fresh epic + a child per unit at `brainstorm-queued`, closes `<n>`). (Specs are normally pre-decomposed in brainstorm; this is the backstop.)

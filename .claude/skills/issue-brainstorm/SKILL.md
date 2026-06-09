---
name: issue-brainstorm
description: Brainstorm a GitHub issue into a spec via async Q&A in the issue itself — ask questions and park, or (when enough is known) draft the spec, commit it, comment summary+link, and advance to spec-review-queued. Part of the issue-planning flow.
user_invocable: true
---

# issue-brainstorm

Turns an idea (issue `<n>`) into a drafted spec. The brainstorm is an **async dialogue carried in the issue**: post questions as a comment, the human answers inline, the next run reads the answers and continues. Re-runnable until the spec is drafted.

**Read first:** `.claude/rules/github.md` (the `set_status` procedure, the Brainstorm + Spec comment templates, storage, the brake, § work-graph).

**On tooling failure** (a `gh`/git/tool error, exception, or broken reference — *not* the work itself failing): follow github.md § Reporting a broken skill (search-first; file/recur a de-duped `issue-flow-bug`).

## Steps

1. **Read state & thread.** `gh issue view <n> --json number,title,body,labels,comments`. Note the current `status:*` and whether `manual` is set. Read the FULL thread: your prior `## 💭 Brainstorm` questions **and the human's answers**, plus any `## 🔬 Spec review` findings if the flow looped back here. As a dialogue handler this skill is **exempt** from the github.md unaddressed-comment guard — it *consumes* comments — but it must explicitly **address any unanswered human question or concern** in the thread (answer inline, or fold into your next questions) rather than drafting over it; a clear go-ahead/ack resolves the discussion and you may draft.
2. **Claim the work →** `status:brainstorming` (single-select per github.md), clearing any stale `blocked`. This is the Doing state — the loop pulls from `brainstorm-queued`, so entering here marks the issue as actively owned (§ Queues vs Doing).
3. **Assess** whether purpose / constraints / success criteria are pinned down, using the **superpowers:brainstorming** judgment.
4. **Branch:**
   - **Not enough yet** → post the **Brainstorm comment** (github.md template) with the next 1–3 focused questions (multiple-choice where possible); set `blocked`; stay `status:brainstorming`. **Stop.** When the human answers inline, reconcile (github.md § Queues vs Doing, and reconcile) clears `blocked` and moves the issue to `brainstorm-queued` — automatically when the driver/`/loop` runs, otherwise on a manual re-run of this skill — to continue.
   - **Too big** (multiple independent plannable units) → run **`/issue-split <n>`** (github.md § work-graph — the single decomposition mechanism: spawns a fresh epic + children at `brainstorm-queued`, closes `<n>`). Stop.
   - **Enough** → continue to step 5.
5. **Draft the spec** using the **superpowers:brainstorming** design conventions. Authoring is **read the code + write the one `.md`** — never edit source to "try it" (any tracked-file trial/compile-check belongs in a throwaway `git worktree`, never the primary checkout — github.md § Working-tree hygiene). Write `docs/superpowers/specs/drafts/{YYYY-MM-DD-slug}.md`; immediately after the standard header add `> **Tracking issue:** #<n>`. Commit + push to main (primary checkout) — staging **only that one file** (github.md § Working-tree hygiene: never `git add -A`/`.`/`-u`/`commit -a`; verify `git status --porcelain` shows nothing but this artifact before committing):
   `git add docs/superpowers/specs/drafts/{file}; git commit -m "docs(spec): draft for #<n>"; git push`
6. **Comment** the **Spec comment** (github.md template): a 3–6 bullet summary + the permalink to the committed draft.
7. **Advance.** If `manual` is set: leave `status:brainstorming`, set `blocked`, note "spec drafted — waiting for your go-ahead." Otherwise `set_status <n> spec-review-queued` (no `blocked`).

## Live alternative

You may instead run **superpowers:brainstorming** directly in a session to produce the spec, then run this skill: it finds the committed `specs/drafts/{slug}.md`, posts the Spec comment, and advances to `spec-review-queued`. Only the async-in-issue path is this skill's own logic.

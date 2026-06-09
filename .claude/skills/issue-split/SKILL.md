---
name: issue-split
description: Split an over-large issue into a fresh epic + sub-issues at any stage — distill the context collected so far into a new clean epic and one child per plannable unit (each entering the flow at brainstorm-queued), then close the original as a linked archive. The single decomposition mechanism. Part of the issue-planning flow.
user_invocable: true
---

# issue-split

Decomposes an issue that turned out **too big for one spec** — at *any* state (inbox,
brainstorming, spec-reviewing, planning, …). Rather than mutate a comment-heavy story into
a tracking shell, it **spawns a fresh clean epic and fresh children** and **closes the
original as the linked archive**. This is the single decomposition path: the "too big"
branches in `issue-triage` / `issue-brainstorm` and the scope-check in `issue-plan`
delegate here.

**Read first:** `.claude/rules/github.md` (§ Sub-issues & work graph, § The board — epics
are swimlanes, the `set_status` procedure, the brake) and `.claude/rules/standards.md`.

**On tooling failure** (a `gh`/git/tool error, exception, or broken reference — *not* the work itself failing): follow github.md § Reporting a broken skill (search-first; file/recur a de-duped `issue-flow-bug`).

## Steps

1. **Read `<n>` + full thread.** `gh issue view <n> --json number,title,body,labels,comments`. Identify the independent **plannable units** — the brainstorm/triage scope-check judgment, made decisively (one issue ↔ one spec ↔ one plan ↔ one cohesive change). Refuse if there is nothing to split: if `<n>` is a **single** plannable unit ("Issue #<n> is one plannable unit — no split needed.") or already a parent/epic ("Issue #<n> is already an epic.") — print and stop.
2. **Distill, don't copy.** Synthesize the decisions collected so far (brainstorm answers, review findings, constraints) into salient context. The original keeps the raw thread; the new issues get the *signal*, not the transcript.
3. **Create the epic** — `gh issue create --label epic`, titled for the overall feature; body = goal + why-split + `Split from #<n>` + a checklist of the children (backfilled in step 5). The **`epic`** label with **no `status:*`** keeps it a swimlane, not a card, and makes intake skip it (§ Intake) — an epic must **never** be auto-stamped onto the board; carry the whole-feature labels (e.g. `templating`) only where they still apply.
4. **Create one child per unit** — `gh issue create --label status:brainstorm-queued` each (opening it **already in the queue** makes intake skip it as already-classified — no Inbox flicker), titled for the unit; body = that unit's scope + the distilled context relevant to it + links to the epic and `#<n>`. Make each a sub-issue of the epic (`gh sub-issue add` / GraphQL `addSubIssue` — verify support at use time), then run `bash .claude/scripts/set-status.sh <child> brainstorm-queued` for each child to add its board card (the label is already set at creation; set-status is idempotent). They **skip the Inbox worth-doing gate** (the split already decided they are worth doing) and flow straight to spec.
   - **Link inter-child dependencies.** Where one unit must land before another (e.g. the domain model before the API that consumes it), record it with GitHub's **native issue-dependency** primitive — *blocked by* / *blocks* (verify the exact `gh`/GraphQL/REST call at use time). This is the **structural** graph and is distinct from the `blocked` modifier label: dependent children still flow through brainstorm/spec/plan; the dependency governs **implementation order**, not their lifecycle state.
5. **Backfill the epic checklist** with the child issue numbers.
6. **Close the original.** Post the **Split comment** (`## 🪓 Split into epic #<E>`) listing the epic and children, then `gh issue close <n> && set-status <n> done` (close, then strip `<n>`'s `status:*`/`blocked` label and move its board card to Done).
7. **Report** the epic #, the child #s + titles, and that `<n>` is closed.

## Notes

- **The brake.** If `<n>` carries `manual`, do not execute: post the proposed split (epic title + child scopes) as a `## 🪓 Proposed split` comment and stop for the human to run it.
- **Parent linkage.** If `<n>` was itself a sub-issue of an existing epic, make the new epic a child of that grandparent so the work graph stays connected.
- **Superseded drafts.** Any spec/plan draft `<n>` accumulated is abandoned by the split (it tried to cover too much) — leave it in `drafts/`, unpromoted; the children draft fresh.
- **Not an auto-advancing skill.** `/issue-split` is invoked deliberately, so the unaddressed-comment guard does not apply; it acts on the operator's instruction.

---
name: issue-triage
description: Shape an Inbox-stage idea via async Q&A in the issue — ask clarifying questions or answer the human while the idea stays in Inbox, and on a clear human go-ahead promote it to brainstorming (or close it if dropped). The Inbox dialogue handler. Part of the issue-planning flow.
user_invocable: true
---

# issue-triage

Shapes a raw idea **while it sits in Inbox** — the pre-flow "is this worth doing, and what
is it really?" conversation. Asks clarifying questions or answers the human **without**
committing the idea to the spec flow; the issue stays `status:inbox` until a human promotes
or closes it. Re-runnable until then.

**Read first:** `.claude/rules/github.md` (the `set_status` procedure, the Triage comment
template, the approval-detection conservatism rule, the brake, § work-graph).

**On tooling failure** (a `gh`/git/tool error, exception, or broken reference — *not* the work itself failing): follow github.md § Reporting a broken skill (search-first; file/recur a de-duped `issue-flow-bug`).

## Steps

1. **Guard (fail closed) + self-intake.** `gh issue view <n> --json number,title,body,labels,comments`, then branch on its labels:
   - **`epic`** → refuse ("Issue #<n> is an epic — a swimlane, not a triageable card.") and stop. An epic must never carry a `status:*`.
   - **`status:inbox`** present → proceed.
   - **No `status:*` at all** (a freshly-filed issue no driver has stamped yet) → **self-intake**: `bash .claude/scripts/set-status.sh <n> inbox` (adds `status:inbox` + the board card in one command; github.md § Intake — the manual front-door path), and proceed.
   - **Any other `status:*`** → refuse ("Issue #<n> is {status} — triage only runs on Inbox ideas.") and stop.

   Then note whether `manual` is set, and read the FULL thread — your prior `## 💬 Triage` comments and the human's answers/comments.
2. **Assess** at triage altitude: is the idea's *intent* clear enough for a human to decide "worth doing?", and is it one cohesive unit or several? Use **superpowers:brainstorming** judgment, but stay shallow — you are clarifying the idea, **not** designing the spec (that is `/issue-brainstorm`'s job, after promotion).
3. **Classify the latest human input** (github.md approval-detection conservatism rule) and branch:
   - **Promote** — an unambiguous go-ahead ("let's spec this", "go ahead, brainstorm it", "yes, worth doing") with no open question → post `## 💬 Triage — promoted`, then `set_status <n> brainstorm-queued` (enter the flow; no `blocked`). Stop.
   - **Drop** — an unambiguous drop ("not worth it", "wontfix", "close it") → post `## 💬 Triage — dropped` with the reason, then `gh issue close <n> && set-status <n> done` (close, then strip any `status:*`/`blocked` label and move the board card to Done). (Reversible — reopen to resume.) Stop.
   - **Too big** (multiple independent plannable units) → run **`/issue-split <n>`** (github.md § work-graph — the single decomposition mechanism: spawns a fresh epic + children, closes `<n>`). Stop.
   - **Still shaping** — anything else (a question, partial info, any caveat) → post the **Triage comment** (github.md template) with your answer and/or the next 1–3 clarifying questions; stay `status:inbox` (**no** `blocked` — Inbox has no Doing pair). **Stop.** Re-run after the human replies.
4. **Report** the branch taken (promote / drop / decompose / still shaping) and the issue's resulting state.

## Notes

- **Exempt from the unaddressed-comment guard.** Like `issue-brainstorm` and `issue-respond`, this is a dialogue handler — it *consumes* comments, so the github.md unaddressed-comment guard does not apply.
- **The brake.** If `manual` is set, never auto-promote or auto-close: on a promote/drop signal, leave `status:inbox`, note the recommendation, and stop for the human to step it.
- **Manual only.** Inbox is not a queue — no `/loop` and no driver ever pulls it *into the flow* (the driver's reconcile only *intakes* new issues to Inbox, § Intake; it never advances them). Triage always runs by explicit invocation; after the human replies, re-run `/issue-triage #<n>`.

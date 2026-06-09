---
name: set-status
description: Move a GitHub issue's issue-flow lifecycle state — the single-select status:* label swap, the blocked flag, AND the board Status-column card move, atomically in one step, so the label and the board can never drift apart. The single way every issue-* skill and the autonomous driver change an issue's state. Part of the issue-planning flow.
user_invocable: true
---

# set-status

The one place that moves an issue's issue-flow lifecycle state. It does **both** halves together — the authoritative label change (single-select `status:*` + the `blocked` flag) **and** the best-effort board card move — in one atomic command, so the board can't silently drift from the labels.

This exists because the old two-step procedure (the label edit in one snippet, the board-move shell function in another) reliably dropped the board half: a fresh shell didn't have that function defined, so the label moved but the card never did, swallowed as "best-effort." One command that does both fixes that.

**Read first:** `.claude/rules/github.md` § Setting state and § The board.

## Usage

Run the script — it is callable from any shell, so the issue-* skills, the driver's agents, and you on the CLI all use the same one command. Two equivalent twins; use whichever matches your shell:

```bash
bash .claude/scripts/set-status.sh  <issue> <new-status> [block|unblock]   # from a bash shell (Git Bash)
pwsh .claude/scripts/set-status.ps1 <issue> <new-status> [block|unblock]   # from PowerShell (Windows-native)
```

- `<new-status>` (short form): `inbox` · `brainstorm-queued` · `brainstorming` · `spec-review-queued` · `spec-reviewing` · `plan-queued` · `planning` · `plan-review-queued` · `plan-reviewing` · `ready` · `implementing`.
- `block` / `unblock` — set/clear the `blocked` modifier (only on Doing states).
- `done` — the **close transition**: strip any `status:*`/`blocked` label (Done = closed, no label) **and** move the closed issue's card to the Done column. Call it right after `gh issue close`.

Examples:

```bash
bash .claude/scripts/set-status.sh 51 ready
bash .claude/scripts/set-status.sh 51 brainstorming block
bash .claude/scripts/set-status.sh 51 brainstorm-queued unblock
gh issue close 51 && bash .claude/scripts/set-status.sh 51 done
```

## Guarantees

- **Labels are authoritative** — a label failure exits non-zero; single-select is enforced (the prior `status:*` is removed before the new one is added); the label is created if missing (idempotent).
- **The board move is best-effort but LOGGED** — it prints `board #N -> Column` on success, or a one-line reason on stderr if it can't move the card (e.g. the `gh` token lacks `project` scope → `gh auth refresh -s project`). It never silently swallows the failure, and a board failure never blocks the transition.

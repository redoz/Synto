#!/usr/bin/env bash
# set-status <issue> <new-status> [block|unblock]
#
# THE single way to move an issue's issue-flow lifecycle state. It does BOTH halves together,
# in one always-callable command, so the board can never silently drift from the labels (the
# failure mode where the label updates but the Status column doesn't):
#   1. labels (AUTHORITATIVE): single-select status:* swap + the optional `blocked` flag.
#   2. board card (best-effort, but LOGGED — never silently swallowed): add-if-missing + set
#      the matching Status column on the "Synto — Issue Flow" project.
#
# Callable from any shell — the issue-* skills, the autonomous driver's agents, and you on the
# CLI all use this one command (github.md § Setting state). Labels are the source of truth; the
# board is a view, so a board failure prints WHY on stderr and still succeeds the transition.
#
# Usage:
#   bash .claude/scripts/set-status.sh 51 ready
#   bash .claude/scripts/set-status.sh 51 brainstorming block
#   bash .claude/scripts/set-status.sh 51 brainstorm-queued unblock
#   gh issue close 51 && bash .claude/scripts/set-status.sh 51 done   # strip status:*/blocked + move card to Done
#
# Special: <new-status> = "done" is the close transition — Done = closed, no label: it strips any
# status:* AND blocked label, then moves the closed card to the Done column. Run it after `gh issue close`.
set -uo pipefail

issue="${1:?usage: set-status <issue> <new-status> [block|unblock]}"
status="${2:?usage: set-status <issue> <new-status> [block|unblock]}"
flag="${3:-}"
owner="redoz"; repo="Synto"; proj=1

# ── Board IDs — PLACEHOLDERS until regenerated for redoz Project #1 "Synto — Issue Flow" ─────────
# The project node id, the Status field id, and each column's option id (folded into the case below)
# are baked in as CONSTANTS so a state transition makes NO `gh project view` / `gh project field-list`
# GraphQL lookups — those re-fetched a fixed mapping on every call and drained the 5000/hr GraphQL
# budget on a board-sized sweep. Only the actual card write (item-add + item-edit) still hits the API.
# The status→column map is fixed (github.md § The board), so these ids are stable constants.
#
# These are PLACEHOLDERS (`REPLACE_ME_*` sentinels — no real ids for Synto's board exist yet). Until they
# are regenerated the board move is a guarded no-op (it LOGS and exits 0) so the authoritative label swap still succeeds.
#
# REGENERATION RECIPE — create/locate the "Synto — Issue Flow" board under owner redoz, then refresh:
#   gh project view 1 --owner redoz --format json -q .id              # → PROJECT_NODE_ID
#   gh project field-list 1 --owner redoz --format json               # → STATUS_FIELD_ID +
#     find the field named "Status": record its .id here and each .options[] {name,id} in the case below.
PROJECT_NODE_ID="REPLACE_ME_PROJECT_NODE_ID"
STATUS_FIELD_ID="REPLACE_ME_STATUS_FIELD_ID"

# ── 1. labels (authoritative) ────────────────────────────────────────────────────────────────
# 'done' is the close transition: Done = closed, no label — strip any status:* AND blocked, then
# fall through to the board move (Done column). Every other state does the single-select status:* swap.
if [ "$status" = "done" ]; then
  for l in $(gh issue view "$issue" --json labels -q '.labels[].name' 2>/dev/null | grep -E '^(status:|blocked$)' || true); do
    gh issue edit "$issue" --remove-label "$l" >/dev/null 2>&1 || true
  done
  echo "set-status: #$issue -> done (stripped status:*/blocked)"
else
  # create-if-missing (idempotent), mirroring github.md § Label inventory
  gh label create "status:$status" --color BFD4F2 --description "Issue flow stage" 2>/dev/null || true
  # single-select: remove any existing status:* first
  for l in $(gh issue view "$issue" --json labels -q '.labels[].name' 2>/dev/null | grep '^status:' || true); do
    gh issue edit "$issue" --remove-label "$l" >/dev/null
  done
  gh issue edit "$issue" --add-label "status:$status" >/dev/null \
    || { echo "set-status: FAILED to add status:$status to #$issue (labels are authoritative)" >&2; exit 1; }
  case "$flag" in
    block)   gh issue edit "$issue" --add-label "blocked"    >/dev/null      || true ;;
    unblock) gh issue edit "$issue" --remove-label "blocked" >/dev/null 2>&1 || true ;;
  esac
  echo "set-status: #$issue -> status:$status${flag:+ ($flag)}"
fi

# ── 2. board card (best-effort, but LOGGED on failure so drift is visible) ──────────────────────
# Each arm sets the column NAME (kept for logging) AND its hardcoded option id (see recipe above).
# The option ids are PLACEHOLDERS too — they will NOT exist for Synto until regenerated.
case "$status" in
  inbox)              col="Inbox";               opt="REPLACE_ME_OPT_INBOX" ;;
  brainstorm-queued)  col="Brainstorm: Queued";  opt="REPLACE_ME_OPT_BRAINSTORM_QUEUED" ;;
  brainstorming)      col="Brainstorming";       opt="REPLACE_ME_OPT_BRAINSTORMING" ;;
  spec-review-queued) col="Spec Review: Queued"; opt="REPLACE_ME_OPT_SPEC_REVIEW_QUEUED" ;;
  spec-reviewing)     col="Spec Reviewing";      opt="REPLACE_ME_OPT_SPEC_REVIEWING" ;;
  plan-queued)        col="Plan: Queued";        opt="REPLACE_ME_OPT_PLAN_QUEUED" ;;
  planning)           col="Planning";            opt="REPLACE_ME_OPT_PLANNING" ;;
  plan-review-queued) col="Plan Review: Queued"; opt="REPLACE_ME_OPT_PLAN_REVIEW_QUEUED" ;;
  plan-reviewing)     col="Plan Reviewing";      opt="REPLACE_ME_OPT_PLAN_REVIEWING" ;;
  ready)              col="Ready";               opt="REPLACE_ME_OPT_READY" ;;
  implementing)       col="Implementing";        opt="REPLACE_ME_OPT_IMPLEMENTING" ;;
  done)               col="Done";                opt="REPLACE_ME_OPT_DONE" ;;
  *) echo "set-status: no board column maps to '$status' — board left unchanged" >&2; exit 0 ;;
esac

warn() { echo "set-status: board move skipped for #$issue (-> $col): $1" >&2; }

# Placeholder guard: until the board ids above are regenerated for redoz/Synto (see recipe at top),
# the labels are already authoritative and done — the board is just a view, so LOG and exit 0. A
# missing/placeholder board must NEVER fail the transition.
case "$PROJECT_NODE_ID$STATUS_FIELD_ID$opt" in
  *REPLACE_ME*) warn "board ids are placeholders — regenerate them for redoz/Synto (see recipe at top); board left unchanged"; exit 0 ;;
esac

# No GraphQL lookups here — project/field/option ids are the constants above. Only the card write hits the API.
# item-add is idempotent — returns the existing card id when already on the board
item=$(gh project item-add "$proj" --owner "$owner" \
         --url "https://github.com/$owner/$repo/issues/$issue" --format json -q .id 2>/dev/null) \
  || { warn "item-add failed (does the gh token have 'project' scope? run: gh auth refresh -s project)"; exit 0; }
if gh project item-edit --project-id "$PROJECT_NODE_ID" --field-id "$STATUS_FIELD_ID" --id "$item" \
     --single-select-option-id "$opt" >/dev/null 2>&1; then
  echo "set-status: board #$issue -> $col"
else
  warn "item-edit failed (if the board was reconfigured, regenerate the hardcoded ids — see recipe at top)"
fi

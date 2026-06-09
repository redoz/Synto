#!/usr/bin/env pwsh
# set-status.ps1 — PowerShell (Windows-native) twin of set-status.sh. Identical contract:
# moves an issue's issue-flow lifecycle state by doing BOTH halves in one command —
#   1. labels (AUTHORITATIVE): single-select status:* swap + the optional `blocked` flag.
#   2. board card (best-effort, LOGGED — never silently swallowed): add-if-missing + set the
#      matching Status column on the "Synto — Issue Flow" project.
# Labels are the source of truth; a board failure prints why (a warning) and still succeeds the
# transition. Use this from PowerShell; use set-status.sh from a bash shell — they are equivalent.
#
# Usage:
#   pwsh .claude/scripts/set-status.ps1 51 ready
#   pwsh .claude/scripts/set-status.ps1 51 brainstorming block
#   pwsh .claude/scripts/set-status.ps1 51 brainstorm-queued unblock
#   gh issue close 51; pwsh .claude/scripts/set-status.ps1 51 done   # strip status:*/blocked + move card to Done
[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$Issue,
  [Parameter(Mandatory)][string]$Status,
  [ValidateSet('', 'block', 'unblock')][string]$Flag = ''
)
$ErrorActionPreference = 'Continue'
$Owner = 'redoz'; $Repo = 'Synto'; $Proj = 1
function Warn($m) { [Console]::Error.WriteLine("set-status: $m") }

# ── Board IDs — PLACEHOLDERS until regenerated for redoz Project #1 "Synto — Issue Flow" ─────────
# The project node id, the Status field id, and each column's option id (folded into the $map below)
# are baked in as CONSTANTS so a state transition makes NO `gh project view` / `gh project field-list`
# GraphQL lookups — those re-fetched a fixed mapping on every call and drained the 5000/hr GraphQL
# budget on a board-sized sweep. Only the actual card write (item-add + item-edit) still hits the API.
# The status→column map is fixed (github.md § The board), so these ids are stable constants.
#
# These are PLACEHOLDERS (the OuroCore ids will NOT exist for Synto). Until they are regenerated the
# board move is a guarded no-op (it LOGS and exits 0) so the authoritative label swap still succeeds.
#
# REGENERATION RECIPE — create/locate the "Synto — Issue Flow" board under owner redoz, then refresh:
#   gh project view 1 --owner redoz --format json -q .id              # → $ProjectNodeId
#   gh project field-list 1 --owner redoz --format json               # → $StatusFieldId +
#     find the field named "Status": record its .id here and each .options[] {name,id} in the $map below.
$ProjectNodeId = 'REPLACE_ME_PROJECT_NODE_ID'
$StatusFieldId = 'REPLACE_ME_STATUS_FIELD_ID'

# ── 1. labels (authoritative) ────────────────────────────────────────────────────────────────
# 'done' is the close transition: Done = closed, no label — strip any status:* AND blocked, then
# fall through to the board move (Done column). Every other state does the single-select status:* swap.
if ($Status -eq 'done') {
  $raw = gh issue view $Issue --json labels 2>$null
  if ($LASTEXITCODE -eq 0 -and $raw) {
    foreach ($l in ($raw | ConvertFrom-Json).labels.name) {
      if ($l -like 'status:*' -or $l -eq 'blocked') { gh issue edit $Issue --remove-label $l 2>$null | Out-Null }
    }
  }
  Write-Host "set-status: #$Issue -> done (stripped status:*/blocked)"
}
else {
  gh label create "status:$Status" --color BFD4F2 --description "Issue flow stage" 2>$null | Out-Null
  $raw = gh issue view $Issue --json labels 2>$null
  if ($LASTEXITCODE -eq 0 -and $raw) {
    foreach ($l in ($raw | ConvertFrom-Json).labels.name) {
      if ($l -like 'status:*') { gh issue edit $Issue --remove-label $l | Out-Null }
    }
  }
  gh issue edit $Issue --add-label "status:$Status" | Out-Null
  if ($LASTEXITCODE -ne 0) { Warn "FAILED to add status:$Status to #$Issue (labels are authoritative)"; exit 1 }
  switch ($Flag) {
    'block'   { gh issue edit $Issue --add-label 'blocked' | Out-Null }
    'unblock' { gh issue edit $Issue --remove-label 'blocked' 2>$null | Out-Null }
  }
  $suffix = if ($Flag) { " ($Flag)" } else { '' }
  Write-Host "set-status: #$Issue -> status:$Status$suffix"
}

# ── 2. board card (best-effort, but LOGGED on failure so drift is visible) ──────────────────────
# Each entry maps status → @{ column NAME (kept for logging); hardcoded option id (see recipe above) }.
# The option ids are PLACEHOLDERS too — they will NOT exist for Synto until regenerated.
$map = @{
  'inbox'              = @{ col = 'Inbox';               opt = 'REPLACE_ME_OPT_INBOX' }
  'brainstorm-queued'  = @{ col = 'Brainstorm: Queued';  opt = 'REPLACE_ME_OPT_BRAINSTORM_QUEUED' }
  'brainstorming'      = @{ col = 'Brainstorming';       opt = 'REPLACE_ME_OPT_BRAINSTORMING' }
  'spec-review-queued' = @{ col = 'Spec Review: Queued'; opt = 'REPLACE_ME_OPT_SPEC_REVIEW_QUEUED' }
  'spec-reviewing'     = @{ col = 'Spec Reviewing';      opt = 'REPLACE_ME_OPT_SPEC_REVIEWING' }
  'plan-queued'        = @{ col = 'Plan: Queued';        opt = 'REPLACE_ME_OPT_PLAN_QUEUED' }
  'planning'           = @{ col = 'Planning';            opt = 'REPLACE_ME_OPT_PLANNING' }
  'plan-review-queued' = @{ col = 'Plan Review: Queued'; opt = 'REPLACE_ME_OPT_PLAN_REVIEW_QUEUED' }
  'plan-reviewing'     = @{ col = 'Plan Reviewing';      opt = 'REPLACE_ME_OPT_PLAN_REVIEWING' }
  'ready'              = @{ col = 'Ready';               opt = 'REPLACE_ME_OPT_READY' }
  'implementing'       = @{ col = 'Implementing';        opt = 'REPLACE_ME_OPT_IMPLEMENTING' }
  'done'               = @{ col = 'Done';                opt = 'REPLACE_ME_OPT_DONE' }
}
$entry = $map[$Status]
if (-not $entry) { Warn "no board column maps to '$Status' — board left unchanged"; exit 0 }
$col = $entry.col
$opt = $entry.opt
function BoardWarn($m) { Warn "board move skipped for #$Issue (-> $col): $m" }

# Placeholder guard: until the board ids above are regenerated for redoz/Synto (see recipe at top),
# the labels are already authoritative and done — the board is just a view, so LOG and exit 0. A
# missing/placeholder board must NEVER fail the transition.
if ("$ProjectNodeId$StatusFieldId$opt" -match 'REPLACE_ME') {
  BoardWarn 'board ids are placeholders — regenerate them for redoz/Synto (see recipe at top); board left unchanged'
  exit 0
}

# No GraphQL lookups here — project/field/option ids are the constants above. Only the card write hits the API.
$raw = gh project item-add $Proj --owner $Owner --url "https://github.com/$Owner/$Repo/issues/$Issue" --format json 2>$null
if ($LASTEXITCODE -ne 0 -or -not $raw) { BoardWarn "item-add failed (does the gh token have 'project' scope? run: gh auth refresh -s project)"; exit 0 }
$item = ($raw | ConvertFrom-Json).id

gh project item-edit --project-id $ProjectNodeId --field-id $StatusFieldId --id $item --single-select-option-id $opt 2>$null | Out-Null
if ($LASTEXITCODE -eq 0) { Write-Host "set-status: board #$Issue -> $col" } else { BoardWarn 'item-edit failed (if the board was reconfigured, regenerate the hardcoded ids — see recipe at top)' }

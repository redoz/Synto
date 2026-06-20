#!/usr/bin/env pwsh
# base-branch.ps1 — PowerShell (Windows-native) twin of base-branch.sh. Identical contract:
# prints the resolved integration branch name B to STDOUT and exits 0, or prints guidance to
# stderr and exits non-zero when B cannot be resolved.
# Callers capture it verbatim: $B = pwsh .claude/scripts/base-branch.ps1
#
# Resolution order (fail-closed, bookmark-first):
#   1. $env:SYNTO_FLOW_BASE — if set and non-empty, use it directly (operator override).
#   2. Nearest jj bookmark among ancestors of @ (PRIMARY auto-detect). Uses:
#        jj log --no-graph -r 'latest(::@ & bookmarks())' -T 'bookmarks.map(|b| b.name()).join(",")'
#      Takes the FIRST name (comma-split — deterministic tie-break when a commit carries several
#      bookmarks). Falls through if jj is absent, errors, or the revset is empty.
#   3. Current git branch via `git symbolic-ref --short -q HEAD` (plain-git fallback).
#      Under jj HEAD is usually detached, so this tier is normally empty.
#   4. Refuse: prints guidance to stderr and exits non-zero.
#
# B is consumed by the implement-plan harness (.claude/workflows/implement-plan.js) as the
# integration bookmark the per-task green stack is rebased onto and advanced.
$ErrorActionPreference = 'Continue'

function LogB($m) { [Console]::Error.WriteLine("base-branch: $m") }

# ── Step 1: env-var override ─────────────────────────────────────────────────
if ($env:SYNTO_FLOW_BASE) {
  LogB "resolved B=$($env:SYNTO_FLOW_BASE) via override"
  [Console]::Out.WriteLine($env:SYNTO_FLOW_BASE)
  exit 0
}

# ── Step 2: nearest jj bookmark among ancestors of @ ─────────────────────────
$jjCmd = Get-Command jj -ErrorAction SilentlyContinue
if ($jjCmd) {
  $jjOut = jj log --no-graph -r 'latest(::@ & bookmarks())' -T 'bookmarks.map(|b| b.name()).join(",")' 2>$null
  if ($LASTEXITCODE -eq 0 -and $jjOut) {
    # $jjOut is a string (single-line template output); take first comma-split token
    $B = (($jjOut | Out-String).Trim() -split ',')[0].Trim()
    if ($B) {
      LogB "resolved B=$B via bookmark"
      [Console]::Out.WriteLine($B)
      exit 0
    }
  }
}

# ── Step 3: git branch fallback ──────────────────────────────────────────────
# NOTE: under jj HEAD is typically detached, so this is usually empty.
$gitBranch = git symbolic-ref --short -q HEAD 2>$null
if ($LASTEXITCODE -eq 0 -and $gitBranch) {
  $gitBranch = ($gitBranch | Out-String).Trim()
  if ($gitBranch) {
    LogB "resolved B=$gitBranch via git-branch"
    [Console]::Out.WriteLine($gitBranch)
    exit 0
  }
}

# ── Step 4: refuse ───────────────────────────────────────────────────────────
[Console]::Error.WriteLine("base-branch: cannot resolve the integration branch B.")
[Console]::Error.WriteLine("  No SYNTO_FLOW_BASE set, no jj bookmark found on ancestors of @, and HEAD is detached.")
[Console]::Error.WriteLine("  Set the override: `$env:SYNTO_FLOW_BASE = '<branch>'")
exit 1

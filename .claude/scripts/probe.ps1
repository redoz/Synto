#!/usr/bin/env pwsh
# probe.ps1 — PowerShell (Windows-native) twin of probe.sh. Identical contract:
# verify the machine can safely start an issue-flow run, then write EXACTLY ONE compact JSON line to
# STDOUT ({"ok":true,"reason":"..."}); ALL progress goes to STDERR; the exit code mirrors ok (0/1).
# Use this from PowerShell; use probe.sh from a bash shell — they are equivalent.
#
# Synto has NO database, NO containers, NO network runtime (build-time library + source generators),
# so there is no infra to bring up. The jj guardrails below verify the working copy is in a safe,
# consistent state for an issue-flow run.
#
# Checks (jj-native — run in order; first fatal failure exits 1):
#   1. B resolves: pwsh .claude/scripts/base-branch.ps1 succeeds with non-empty output.
#   2. Bookmark exists: the resolved B appears in `jj bookmark list -T 'name ++ "\n"'`
#      (catches typo'd overrides and deleted branches before any commit lands on a phantom target).
#   3. Working copy conflict-free: @ has no conflicts (`jj log -r '@' -T conflict` == "false").
#   4. (WARN, non-fatal) Stray edits: if @ has tracked changes, warn on stderr. Downstream commits
#      are fileset-scoped, so stray edits (e.g. the operator's README) are NOT swept — advisory only.
#
# The -NoImplement switch (probe.sh takes --no-implement) is still ACCEPTED for caller compatibility,
# but it no longer gates on anything (there is no infra to check) — both modes run the same jj check.
#
# Usage (PowerShell idiom: a -NoImplement switch, where probe.sh takes --no-implement):
#   pwsh .claude/scripts/probe.ps1                # implement mode (default)
#   pwsh .claude/scripts/probe.ps1 -NoImplement   # Phase-1-only run (same jj check)
[CmdletBinding()]
param(
  [switch]$NoImplement
)
$ErrorActionPreference = 'Continue'
$Implement = -not $NoImplement

function LogP($m) { [Console]::Error.WriteLine("probe: $m") }

# Emit <bool ok> <reason> — write the single stdout JSON line and exit with the mirroring code.
function Emit($ok, $reason) {
  $json = [ordered]@{ ok = [bool]$ok; reason = "$reason" } | ConvertTo-Json -Compress
  [Console]::Out.WriteLine($json)
  if ($ok) { exit 0 } else { exit 1 }
}

# The implement flag no longer gates on any infrastructure (Synto has none) — it is accepted for
# caller compatibility and both modes run the same jj check below.
if ($Implement) {
  LogP "implement mode (default) — no infra to check; running the jj check only"
}
else {
  LogP "implement is OFF this run — no infra to check anyway; running the jj check only"
}

# ── Guardrail 1: B resolves ───────────────────────────────────────────────────────────────────────
LogP "resolving integration branch B …"
$B = (pwsh -NoProfile -File "$PSScriptRoot/base-branch.ps1" 2>$null)
$baseBranchOk = ($LASTEXITCODE -eq 0)
if (-not $baseBranchOk -or -not $B) {
  Emit $false "could not resolve integration branch B. Remediation: set ``$env:SYNTO_FLOW_BASE to the target branch, then re-run."
}
# Normalize to a single trimmed string (base-branch.ps1 outputs exactly one line)
$B = if ($B -is [array]) { "$($B[0])".Trim() } else { "$B".Trim() }
if (-not $B) {
  Emit $false "could not resolve integration branch B. Remediation: set ``$env:SYNTO_FLOW_BASE to the target branch, then re-run."
}
LogP "resolved B=$B"

# ── Guardrail 2: bookmark exists ──────────────────────────────────────────────────────────────────
LogP "checking bookmark '$B' exists in jj …"
$bookmarkLines = @(jj bookmark list -T 'name ++ "\n"' 2>$null) |
  ForEach-Object { $_.Trim() } |
  Where-Object { $_ -ne '' }
if ($bookmarkLines -notcontains $B) {
  Emit $false "bookmark '$B' not found in jj bookmark list. Remediation: create it ('jj bookmark create $B') or fix ``$env:SYNTO_FLOW_BASE, then re-run."
}
LogP "bookmark '$B' confirmed"

# ── Guardrail 3 + 4: working-copy state ───────────────────────────────────────────────────────────
LogP "checking working copy state …"
$jjStatusLines = @(jj status 2>$null)

# Guardrail 3 (FATAL): no conflicts in @
# Use `jj log -r '@' -T conflict` (outputs literal "true"/"false") rather than grepping jj status,
# which may contain the word "conflict" in commit messages and cause false positives.
$conflictVal = (jj log --no-graph -r '@' -T conflict 2>$null | Out-String).Trim()
if ($conflictVal -eq 'true') {
  Emit $false "working copy @ has conflicts — resolve them with 'jj resolve' before running the issue flow."
}
LogP "no conflicts in @"

# Guardrail 4 (WARN, non-fatal): stray tracked-file edits in @
# Downstream commits are fileset-scoped so stray edits (e.g. the operator's README.md) will NOT
# be swept — this warning is advisory only.
if ($jjStatusLines -match '^[MADR] ') {
  LogP "WARNING: @ has tracked changes ('jj status' shows edits). Downstream commits are fileset-scoped — stray edits (e.g. README.md) will NOT be swept, but verify your fileset after committing."
}

Emit $true "all jj guardrails passed (B=$B)"

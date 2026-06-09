#!/usr/bin/env pwsh
# probe.ps1 — PowerShell (Windows-native) twin of probe.sh. Identical contract:
# verify the machine can safely start an issue-flow run, then write EXACTLY ONE compact JSON line to
# STDOUT ({"ok":true,"reason":"..."}); ALL progress goes to STDERR; the exit code mirrors ok (0/1).
# Use this from PowerShell; use probe.sh from a bash shell — they are equivalent.
#
# Synto has NO database, NO containers, NO network runtime (build-time library + source generators),
# so there is no infra to bring up — the only precondition is the git one below.
#
# Checks (see probe.sh for the full rationale):
#   1. main fast-forwardable vs origin/main: equal / unpushed-local / behind→`git merge --ff-only` / diverged→fail.
#      The fast-forward is the ONE allowed mutation.
#
# The -NoImplement switch (probe.sh takes --no-implement) is still ACCEPTED for caller compatibility,
# but it no longer gates on anything (there is no DB to check) — both modes run the same git check.
#
# Usage (PowerShell idiom: a -NoImplement switch, where probe.sh takes --no-implement):
#   pwsh .claude/scripts/probe.ps1                # implement mode (default)
#   pwsh .claude/scripts/probe.ps1 -NoImplement   # Phase-1-only run (same git check)
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
# caller compatibility and both modes run the same git check below.
if ($Implement) {
  LogP "implement mode (default) — no infra to check; running the git check only"
}
else {
  LogP "implement is OFF this run — no infra to check anyway; running the git check only"
}

# ── Check: main fast-forwardable vs origin/main (the one allowed mutation) ───────────────────────
$branch = git symbolic-ref --short -q HEAD 2>$null
if ($LASTEXITCODE -ne 0 -or $branch -ne 'main') {
  $b = if ($branch) { $branch } else { 'detached' }
  Emit $false "primary checkout is not on main (HEAD=$b); the runners commit to main. Remediation: git switch main, then re-run."
}

LogP "git fetch origin …"
git fetch origin *> $null
if ($LASTEXITCODE -ne 0) {
  Emit $false "git fetch origin failed (network/auth?). Remediation: restore connectivity to origin, then re-run."
}

$localSha = (git rev-parse HEAD 2>$null).Trim()
$remoteSha = git rev-parse origin/main 2>$null
if ($LASTEXITCODE -ne 0 -or -not $remoteSha) {
  Emit $false "could not resolve origin/main after fetch. Remediation: check the origin remote, then re-run."
}
$remoteSha = $remoteSha.Trim()

if ($localSha -eq $remoteSha) {
  Emit $true "main == origin/main"
}
git merge-base --is-ancestor $remoteSha $localSha 2>$null
if ($LASTEXITCODE -eq 0) {
  Emit $true "main has unpushed local commits ahead of origin/main (left untouched)"
}
git merge-base --is-ancestor $localSha $remoteSha 2>$null
if ($LASTEXITCODE -eq 0) {
  LogP "main is behind origin/main — fast-forwarding …"
  git merge --ff-only origin/main *> $null
  if ($LASTEXITCODE -eq 0) {
    Emit $true "main fast-forwarded to origin/main"
  }
  else {
    Emit $false "main is behind origin/main but git merge --ff-only failed (dirty working tree?). Remediation: clean/stash the tree, then re-run."
  }
}
Emit $false "main diverged from origin/main (neither is an ancestor of the other). Remediation: reconcile main with origin manually, then re-run."

<#
integrate.ps1 — PowerShell mirror of integrate.sh (Synto pairs .sh/.ps1 for its harness scripts).

Mechanical, LLM-free integration of a jj workspace's committed task stack onto bookmark B (jj + dotnet). Same
flow and SAME EXIT-CODE CONTRACT as integrate.sh — the workflow's implementer/fix-forward act on the exit code:
  0  landed (or nothing to integrate)         20 workspace not in expected committed state (refused)
  21 rebase conflict (escalate)               22 acceptance gate code-red
  23 transient/environment (NuGet/file lock)  24 lost the bookmark-advance race after -Retries
  25 push rejected in push mode (park)          2 bad invocation

Usage: integrate.ps1 -Workspace PATH -Base B [-Remote origin] [-Retries 8]
Env:   GATE_CMD              FULL green-gate (default = Synto's gate, whitespace-scope format only)
       SYNTO_FLOW_INTEGRATE  'local' (default) advances the bookmark only; 'push' also runs `jj git push`.
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][string]$Workspace,
  [Parameter(Mandatory = $true)][string]$Base,
  [string]$Remote = 'origin',
  [int]$Retries = 8
)
$ErrorActionPreference = 'Continue'

$gateCmd = if ($env:GATE_CMD) { $env:GATE_CMD } else {
  'dotnet build --no-restore -c Debug && dotnet test --no-build -c Debug && dotnet format whitespace --verify-no-changes'
}
$push = ($env:SYNTO_FLOW_INTEGRATE -eq 'push')

if (-not (Test-Path $Workspace)) { Write-Error "{`"error`":`"cannot cd to workspace: $Workspace`"}"; exit 2 }
Set-Location $Workspace

function Emit([string]$json, [int]$code) { Write-Output $json; exit $code }
function Tip { (jj log --no-graph -r '@-' -T 'commit_id.short()' 2>$null) -join '' }
function Is-Transient([string]$text) {
  $text -imatch 'Unable to load the service index|NU1301|NU1101 .*(feed|source)|being used by another process|Access is denied|os error 5|MSB3027|MSB3021|connection (timed out|refused|reset)|timed out|temporarily unavailable'
}

# Anything to land? B..@- is empty when @- IS B (nothing committed this unit — a ride-along). That is SUCCESS.
$pending = (jj log --no-graph -r "$Base..@-" -T '"x"' 2>$null) -join ''
if ([string]::IsNullOrWhiteSpace($pending)) { Emit "{`"pushed`":false,`"nothingToIntegrate`":true,`"headSha`":`"$(Tip)`"}" 0 }

for ($attempt = 1; ; $attempt++) {
  # 1. Rebase the task stack onto the current tip of B.
  $rebase = (jj rebase -b '@' -o $Base 2>&1) -join "`n"
  if ($LASTEXITCODE -ne 0) {
    jj undo *> $null
    Emit "{`"pushed`":false,`"problems`":`"jj rebase -b @ -o $Base failed`"}" 20
  }

  # 2. Conflict check — fail closed on an in-tree conflict.
  $conflicted = (jj log --no-graph -r "$Base..@" -T 'if(conflict, "C", "")' 2>$null) -join ''
  if ($conflicted) { jj undo *> $null; Emit "{`"pushed`":false,`"conflict`":true,`"problems`":`"rebase onto $Base conflicted -- escalate`"}" 21 }

  # 3. FULL green-gate on the rebased stack (classify transient vs code).
  $gateOut = (bash -c $gateCmd 2>&1) -join "`n"
  if ($LASTEXITCODE -ne 0) {
    if (Is-Transient $gateOut) { Emit "{`"pushed`":false,`"transient`":true,`"problems`":`"gate parked on a transient/environment failure`"}" 23 }
    Emit "{`"pushed`":false,`"gateGreen`":false,`"problems`":`"gate code-red on the rebased stack`"}" 22
  }

  # 4. Advance bookmark B forward-only (NEVER --allow-backwards).
  $tip = Tip
  $bset = (jj bookmark set $Base -r '@-' 2>&1) -join "`n"
  if ($LASTEXITCODE -eq 0) {
    if ($push) {
      $p = (jj git push --remote $Remote --bookmark $Base 2>&1) -join "`n"
      if ($LASTEXITCODE -ne 0) {
        $p2 = (jj git push --remote $Remote --allow-new --bookmark $Base 2>&1) -join "`n"
        if ($LASTEXITCODE -ne 0) { Emit "{`"pushed`":true,`"headSha`":`"$tip`",`"remotePushed`":false,`"problems`":`"advanced $Base locally but jj git push was rejected -- reconcile the remote`"}" 25 }
      }
      Emit "{`"pushed`":true,`"headSha`":`"$tip`",`"gateGreen`":true,`"remotePushed`":true,`"attempts`":$attempt}" 0
    }
    Emit "{`"pushed`":true,`"headSha`":`"$tip`",`"gateGreen`":true,`"remotePushed`":false,`"attempts`":$attempt}" 0
  }

  # Advance refused — non-fast-forward means a co-running plan advanced B. Re-rebase + retry within budget.
  if ($bset -imatch 'backwards or sideways|not fast-forward|non-fast-forward|would move backward') {
    if ($attempt -ge $Retries) { Emit "{`"pushed`":false,`"problems`":`"lost the bookmark-advance race after $Retries attempt(s)`"}" 24 }
    Start-Sleep -Seconds ([Math]::Min($attempt * 2, 16))
    continue
  }
  Emit "{`"pushed`":false,`"problems`":`"jj bookmark set $Base failed`"}" 20
}

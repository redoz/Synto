#!/usr/bin/env pwsh
# trust.ps1 — PowerShell twin of trust.sh. See trust.sh for the full rationale.
#
# MECHANICAL author-identity gate for the issue-flow. Trust = the comment/issue author IS the
# account currently running the flow (the signed-in gh user, `gh api user`). Identity comes ONLY
# from GitHub metadata (author.login), NEVER from comment text, so it cannot be prompt-injected;
# no LLM ever decides trust. OPTIONAL: $env:SYNTO_TRUSTED_LOGINS (comma-separated) trusts extra
# accounts. FAIL CLOSED: any gh error throws (ErrorActionPreference Stop) — never read as trusted.
#
# Usage:
#   trust.ps1 new-human    <issue>                 # trusted, human (non-"##"), unaddressed comments (after last "##")
#   trust.ps1 comments     <issue> [<watermark>]   # all trusted human comments (optionally after an ISO watermark)
#   trust.ps1 issue-author <issue>                 # exit 0 "trusted" if the issue author is trusted, else exit 1 "untrusted"
[CmdletBinding()]
param(
  [Parameter(Mandatory)][ValidateSet('new-human','comments','issue-author')][string]$Cmd,
  [Parameter(Mandatory)][int]$Issue,
  [string]$Watermark = '1970-01-01T00:00:00Z'
)
$ErrorActionPreference = 'Stop'

$me = (gh api user --jq .login).Trim()
$allowList = @($me) + (($env:SYNTO_TRUSTED_LOGINS -split ',') | Where-Object { $_ })
# jq array literal: ["me"] or ["me","extra1",...]
$allow = '[' + (($allowList | ForEach-Object { '"' + $_ + '"' }) -join ',') + ']'

# jq fragments built by concatenation so jq vars ($l/$wm) and quotes stay literal (single-quoted PS strings).
$trusted = 'select(.author.login as $l | ' + $allow + ' | index($l))'
$human   = 'select((.body | test("^[[:space:]]*##")) | not)'

switch ($Cmd) {
  'new-human' {
    $wm = '( [ .comments[] | select(.body | test("^[[:space:]]*##")) | .createdAt ] | max // "1970-01-01T00:00:00Z" ) as $wm'
    $jq = $wm + ' | [ .comments[] | ' + $trusted + ' | ' + $human + ' | select(.createdAt > $wm) ]'
    gh issue view $Issue --json comments --jq $jq
  }
  'comments' {
    $jq = '[ .comments[] | ' + $trusted + ' | ' + $human + ' | select(.createdAt > "' + $Watermark + '") ]'
    gh issue view $Issue --json comments --jq $jq
  }
  'issue-author' {
    $author = (gh issue view $Issue --json author --jq .author.login).Trim()
    if ($allowList -contains $author) { 'trusted'; exit 0 } else { 'untrusted'; exit 1 }
  }
}

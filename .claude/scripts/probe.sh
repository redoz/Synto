#!/usr/bin/env bash
# probe.sh [--implement | --no-implement]
#
# THE precondition probe for the issue-flow runners (walk-issue / burn-the-board).
# Verifies the machine is in a state where a run can safely proceed, then prints EXACTLY ONE
# compact JSON line to STDOUT:
#   {"ok":true,"reason":"..."}   (ok=false carries the blocker + remediation in reason)
# ALL human-readable progress goes to STDERR, so STDOUT is a single clean JSON line an agent can
# capture verbatim. The exit code mirrors ok (0 = ok, 1 = not ok), so the operator can gate on it too.
#
# This is the deterministic replacement for the old natural-language "probe agent": the runner agent
# now just RUNS this script and returns its stdout, instead of re-deriving the checks from a prose spec.
#
# Synto has NO database, NO containers, NO network runtime — it is a build-time library + source
# generators — so there is no infra to bring up. The jj guardrails below verify the working copy
# is in a safe, consistent state for an issue-flow run.
#
# Checks (jj-native — run in order; first fatal failure exits 1):
#   1. B resolves: bash .claude/scripts/base-branch.sh succeeds with non-empty output.
#   2. Bookmark exists: the resolved B appears in `jj bookmark list -T 'name ++ "\n"'`
#      (catches typo'd overrides and deleted branches before any commit lands on a phantom target).
#   3. Working copy conflict-free: @ has no conflicts (jj status shows no "conflict").
#   4. (WARN, non-fatal) Stray edits: if @ has tracked changes, warn on stderr. Downstream commits
#      are fileset-scoped, so stray edits (e.g. the operator's README) are NOT swept — advisory only.
#
# The --implement / --no-implement flag is still ACCEPTED for caller compatibility, but it no longer
# gates on anything (there is no infra to check) — both modes run the same jj check.
#
# Usage:
#   bash .claude/scripts/probe.sh                 # implement mode (default)
#   bash .claude/scripts/probe.sh --no-implement  # Phase-1-only run (same jj check)
set -uo pipefail

implement=1
case "${1:-}" in
  --implement | '') implement=1 ;;
  --no-implement)   implement=0 ;;
  *) echo "probe: unknown arg '$1' (use --implement | --no-implement); assuming --implement" >&2 ;;
esac

log() { echo "probe: $*" >&2; }

# emit <0|1 ok> <reason…> — print the single stdout JSON line and exit with the mirroring code.
emit() {
  local ok="$1"; shift
  local reason="$*"
  local esc=${reason//\\/\\\\}; esc=${esc//\"/\\\"}   # JSON-escape backslash + double-quote
  if [ "$ok" = "1" ]; then
    printf '{"ok":true,"reason":"%s"}\n' "$esc"; exit 0
  else
    printf '{"ok":false,"reason":"%s"}\n' "$esc"; exit 1
  fi
}

# The implement flag no longer gates on any infrastructure (Synto has none) — it is accepted for
# caller compatibility and both modes run the same jj check below.
if [ "$implement" = "1" ]; then
  log "implement mode (default) — no infra to check; running the jj check only"
else
  log "implement is OFF this run — no infra to check anyway; running the jj check only"
fi

# ── Guardrail 1: B resolves ───────────────────────────────────────────────────────────────────────
log "resolving integration branch B …"
B="$(bash .claude/scripts/base-branch.sh 2>/dev/null)" || B=""
if [ -z "$B" ]; then
  emit 0 "could not resolve integration branch B. Remediation: set \$SYNTO_FLOW_BASE to the target branch, then re-run."
fi
log "resolved B=${B}"

# ── Guardrail 2: bookmark exists ──────────────────────────────────────────────────────────────────
log "checking bookmark '${B}' exists in jj …"
if ! jj bookmark list -T 'name ++ "\n"' 2>/dev/null | grep -qxF "$B"; then
  emit 0 "bookmark '${B}' not found in jj bookmark list. Remediation: create it ('jj bookmark create ${B}') or fix \$SYNTO_FLOW_BASE, then re-run."
fi
log "bookmark '${B}' confirmed"

# ── Guardrail 3 + 4: working-copy state ───────────────────────────────────────────────────────────
log "checking working copy state …"
_jj_status="$(jj status 2>/dev/null)" || _jj_status=""

# Guardrail 3 (FATAL): no conflicts in @
if echo "$_jj_status" | grep -qi "conflict"; then
  emit 0 "working copy @ has conflicts — resolve them with 'jj resolve' before running the issue flow."
fi
log "no conflicts in @"

# Guardrail 4 (WARN, non-fatal): stray tracked-file edits in @
# Downstream commits are fileset-scoped so stray edits (e.g. the operator's README.md) will NOT
# be swept — this warning is advisory only.
if echo "$_jj_status" | grep -qE '^[MADR] '; then
  log "WARNING: @ has tracked changes ('jj status' shows edits). Downstream commits are fileset-scoped — stray edits (e.g. README.md) will NOT be swept, but verify your fileset after committing."
fi

emit 1 "all jj guardrails passed (B=${B})"

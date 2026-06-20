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
# generators — so there is no infra to bring up. The only precondition is the git one below.
#
# Checks:
#   1. main is fast-forwardable vs origin/main (mirror implement-plan preflight):
#        - HEAD == origin/main: fine;
#        - origin/main is an ancestor of HEAD (unpushed local commits — operator commits to main): fine;
#        - HEAD is an ancestor of origin/main (behind): `git merge --ff-only origin/main` to catch up
#          (this is the ONE allowed mutation — nothing else is changed);
#        - otherwise DIVERGED: ok=false.
#
# The --implement / --no-implement flag is still ACCEPTED for caller compatibility, but it no longer
# gates on anything (there is no DB to check) — both modes run the same git check.
#
# Usage:
#   bash .claude/scripts/probe.sh                 # implement mode (default)
#   bash .claude/scripts/probe.sh --no-implement  # Phase-1-only run (same git check)
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
# caller compatibility and both modes run the same git check below.
if [ "$implement" = "1" ]; then
  log "implement mode (default) — no infra to check; running the git check only"
else
  log "implement is OFF this run — no infra to check anyway; running the git check only"
fi

# ── Check: main fast-forwardable vs origin/main (the one allowed mutation) ───────────────────────
branch=$(git symbolic-ref --short -q HEAD || echo "")
if [ "$branch" != "main" ]; then
  emit 0 "primary checkout is not on main (HEAD=${branch:-detached}); the runners commit to main. Remediation: git switch main, then re-run."
fi

log "git fetch origin …"
if ! git fetch origin >/dev/null 2>&1; then
  emit 0 "git fetch origin failed (network/auth?). Remediation: restore connectivity to origin, then re-run."
fi

local_sha=$(git rev-parse HEAD)
remote_sha=$(git rev-parse origin/main 2>/dev/null || echo "")
if [ -z "$remote_sha" ]; then
  emit 0 "could not resolve origin/main after fetch. Remediation: check the origin remote, then re-run."
fi

if [ "$local_sha" = "$remote_sha" ]; then
  emit 1 "main == origin/main"
elif git merge-base --is-ancestor "$remote_sha" "$local_sha"; then
  emit 1 "main has unpushed local commits ahead of origin/main (left untouched)"
elif git merge-base --is-ancestor "$local_sha" "$remote_sha"; then
  log "main is behind origin/main — fast-forwarding …"
  if git merge --ff-only origin/main >/dev/null 2>&1; then
    emit 1 "main fast-forwarded to origin/main"
  else
    emit 0 "main is behind origin/main but git merge --ff-only failed (dirty working tree?). Remediation: clean/stash the tree, then re-run."
  fi
else
  emit 0 "main diverged from origin/main (neither is an ancestor of the other). Remediation: reconcile main with origin manually, then re-run."
fi

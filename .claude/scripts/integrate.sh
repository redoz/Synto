#!/usr/bin/env bash
# integrate.sh — mechanical, LLM-free integration of a jj workspace's committed task stack onto bookmark B.
#
# jj+dotnet port of ourocore's git+cargo integrate.sh, adapted to Synto's bookmark-advance model (the same logic
# the old per-task engine folded inline in integrateIncrementPrompt). Every step is plain jj + dotnet, so it is
# deterministic and fast. Used per green UNIT by the batched implementer (and the deep-review fix-forward). The happy
# path has no LLM judgement; the ONE case a script must not handle (an unresolvable rebase conflict) fails closed so
# the orchestrator can escalate.
#
# Flow (run INSIDE the plan workspace): nothing to integrate? exit 0 -> rebase the task stack onto the latest tip of
#       B -> (conflict? undo + fail closed) -> FULL green-gate on the rebased stack -> advance bookmark B forward-only
#       -> (push mode only) jj git push -> done. B is SHARED, so a forward-only advance that is REFUSED as
#       non-fast-forward means a co-running plan advanced B under us; we re-rebase onto the new tip and retry, behind
#       a growing backoff, up to --retries times.
#
# Usage: integrate.sh --workspace PATH --base B [--remote origin] [--retries 8]
# Env:   GATE_CMD              the FULL green-gate, run in the workspace. Default = Synto's gate:
#                              dotnet build --no-restore -c Debug && dotnet test --no-build -c Debug
#                              && dotnet format whitespace --verify-no-changes
#                              (whitespace scope ONLY — full `dotnet format` applies analyzer code-fixes that
#                              regress the build; analyzer WARNINGS from the build are findings, not gate failures.)
#        SYNTO_FLOW_INTEGRATE  'local' (default) advances the bookmark only and pushes NOTHING;
#                              'push' ALSO runs `jj git push` after a successful advance.
#
# Output: one JSON object on stdout. Exit codes (the contract the workflow's implementer/fix-forward act on):
#   0  landed (or nothing to integrate)         20 workspace not in expected committed state (refused)
#   21 rebase conflict (escalate)               22 acceptance gate code-red
#   23 transient/environment (NuGet/file lock)  24 lost the bookmark-advance race after --retries
#   25 push rejected in push mode (park)         2 bad invocation
set -uo pipefail

REMOTE=origin
RETRIES=8
WORKSPACE=
BASE=
while [ $# -gt 0 ]; do
  case "$1" in
    --workspace) WORKSPACE=${2:-}; shift 2 ;;
    --base)      BASE=${2:-}; shift 2 ;;
    --remote)    REMOTE=${2:-}; shift 2 ;;
    --retries)   RETRIES=${2:-}; shift 2 ;;
    *) echo "{\"error\":\"unknown arg: $1\"}" >&2; exit 2 ;;
  esac
done
[ -n "$WORKSPACE" ] || { echo '{"error":"--workspace required"}' >&2; exit 2; }
[ -n "$BASE" ]      || { echo '{"error":"--base required"}'      >&2; exit 2; }
GATE_CMD=${GATE_CMD:-dotnet build --no-restore -c Debug && dotnet test --no-build -c Debug && dotnet format whitespace --verify-no-changes}

# Integration mode: ONLY the exact value "push" pushes; anything else (unset/empty/"local") is local-advance-only.
PUSH=0
[ "${SYNTO_FLOW_INTEGRATE:-local}" = "push" ] && PUSH=1

TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT

cd "$WORKSPACE" 2>/dev/null || { echo "{\"error\":\"cannot cd to workspace: $WORKSPACE\"}" >&2; exit 2; }

# A failing gate is TRANSIENT/ENVIRONMENT (NuGet restore blip / build-host file lock) vs a real CODE/TEST defect.
# Same signatures the implement-plan.js GATE_INFRA_GUARD classifies on; transient must NOT read as a broken build.
is_transient() {
  grep -Eiq 'Unable to load the service index|NU1301|NU1101 .*(feed|source)|unable to (load|resolve)|being used by another process|Access is denied|os error 5|MSB3027|MSB3021|could not (be )?(copied|access)|connection (timed out|refused|reset)|timed out|temporarily unavailable' "$1"
}

# short commit id of @- (the parent of the empty working-copy commit = the stack tip)
tip_id() { jj log --no-graph -r '@-' -T 'commit_id.short()' 2>/dev/null; }

# Is there a DESCRIBED task commit to land? B..@- is empty when @- IS B (nothing committed this unit — e.g. a
# multi-task compile unit whose code rides along on a later task). That is SUCCESS, not a failure.
pending=$(jj log --no-graph -r "${BASE}..@-" -T '"x\n"' 2>"$TMP/pending")
if [ -z "${pending//[$' \t\r\n']/}" ]; then
  echo "{\"pushed\":false,\"nothingToIntegrate\":true,\"headSha\":\"$(tip_id)\"}"; exit 0
fi

attempt=0
while : ; do
  attempt=$((attempt + 1))

  # 1. Rebase this plan's task stack onto the CURRENT tip of B. jj records conflicts IN-TREE rather than stopping.
  if ! jj rebase -b @ -o "$BASE" >"$TMP/rebase" 2>&1; then
    # A hard rebase error (not an in-tree conflict). Undo to restore state, then fail closed.
    jj undo >/dev/null 2>&1
    echo "{\"pushed\":false,\"problems\":\"jj rebase -b @ -o $BASE failed -- $(tr -d '\n' <"$TMP/rebase" | tail -c 200)\"}"; exit 20
  fi

  # 2. Conflict check — an unresolvable conflict is the one thing a script must not resolve. Undo + fail closed.
  conflicted=$(jj log --no-graph -r "${BASE}..@" -T 'if(conflict, "C", "")' 2>/dev/null)
  if [ -n "$conflicted" ]; then
    jj undo >/dev/null 2>&1
    echo "{\"pushed\":false,\"conflict\":true,\"problems\":\"rebase onto $BASE conflicted -- escalate\"}"; exit 21
  fi

  # 3. FULL green-gate on the rebased stack (classify transient vs code).
  if ! bash -c "$GATE_CMD" >"$TMP/gate" 2>&1; then
    if is_transient "$TMP/gate"; then
      echo "{\"pushed\":false,\"transient\":true,\"problems\":\"gate parked on a transient/environment failure\"}"; exit 23
    fi
    echo "{\"pushed\":false,\"gateGreen\":false,\"problems\":\"gate code-red on the rebased stack\"}"; exit 22
  fi

  # 4. Advance bookmark B to the rebased stack tip (@-), FORWARD-only. NEVER pass --allow-backwards.
  TIP=$(tip_id)
  if jj bookmark set "$BASE" -r '@-' >"$TMP/bset" 2>&1; then
    if [ "$PUSH" = "1" ]; then
      # 5. Push the advanced bookmark (push mode only). First push of a brand-new remote bookmark may need --allow-new.
      if ! jj git push --remote "$REMOTE" --bookmark "$BASE" >"$TMP/push" 2>&1; then
        if ! jj git push --remote "$REMOTE" --allow-new --bookmark "$BASE" >"$TMP/push2" 2>&1; then
          echo "{\"pushed\":true,\"headSha\":\"$TIP\",\"remotePushed\":false,\"problems\":\"advanced $BASE locally but jj git push was rejected -- reconcile the remote\"}"; exit 25
        fi
      fi
      echo "{\"pushed\":true,\"headSha\":\"$TIP\",\"gateGreen\":true,\"remotePushed\":true,\"attempts\":$attempt}"; exit 0
    fi
    echo "{\"pushed\":true,\"headSha\":\"$TIP\",\"gateGreen\":true,\"remotePushed\":false,\"attempts\":$attempt}"; exit 0
  fi

  # Advance refused. If it is the non-fast-forward case, a co-running plan advanced B -> re-rebase + retry within budget.
  if grep -Eiq 'backwards or sideways|not fast-forward|non-fast-forward|would move backward' "$TMP/bset"; then
    if [ "$attempt" -ge "$RETRIES" ]; then
      echo "{\"pushed\":false,\"problems\":\"lost the bookmark-advance race after $RETRIES attempt(s)\"}"; exit 24
    fi
    sleep $((attempt < 8 ? attempt * 2 : 16))   # growing backoff so the competing advance settles
    continue
  fi
  # Any other bookmark-set failure is an unexpected state -> refuse.
  echo "{\"pushed\":false,\"problems\":\"jj bookmark set $BASE failed -- $(tr -d '\n' <"$TMP/bset" | tail -c 200)\"}"; exit 20
done

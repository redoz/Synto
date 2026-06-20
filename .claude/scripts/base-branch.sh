#!/usr/bin/env bash
# base-branch.sh — single source of truth for the integration branch B.
#
# Prints the resolved integration branch name B to STDOUT and exits 0.
# ALL logs go to stderr. Callers capture it verbatim:
#   B=$(bash .claude/scripts/base-branch.sh)
#
# Resolution order (fail-closed, bookmark-first):
#   1. $SYNTO_FLOW_BASE env var — if set and non-empty, use it directly (operator override).
#   2. Nearest jj bookmark among ancestors of @ (PRIMARY auto-detect). Uses:
#        jj log --no-graph -r 'latest(::@ & bookmarks())' -T 'bookmarks.map(|b| b.name()).join(",")'
#      Takes the FIRST name (comma-split — deterministic tie-break when a commit carries several
#      bookmarks). Falls through if jj is absent, errors, or the revset is empty.
#   3. Current git branch via `git symbolic-ref --short -q HEAD` (plain-git fallback).
#      Under jj HEAD is usually detached, so this tier is normally empty — it exists for
#      environments where the repo is used without jj.
#   4. Refuse: prints guidance to stderr and exits non-zero.
#
# B is consumed by the implement-plan harness (.claude/workflows/implement-plan.js) as the
# integration bookmark the per-task green stack is rebased onto and advanced.
set -euo pipefail

# ── Step 1: env-var override ──────────────────────────────────────────────────
if [ -n "${SYNTO_FLOW_BASE:-}" ]; then
  echo "base-branch: resolved B=${SYNTO_FLOW_BASE} via override" >&2
  echo "${SYNTO_FLOW_BASE}"
  exit 0
fi

# ── Step 2: nearest jj bookmark among ancestors of @ ─────────────────────────
if command -v jj >/dev/null 2>&1; then
  _jj_out="$(jj log --no-graph -r 'latest(::@ & bookmarks())' -T 'bookmarks.map(|b| b.name()).join(",")' 2>/dev/null)" || _jj_out=""
  if [ -n "$_jj_out" ]; then
    # Take the first name (comma-split — deterministic tie-break when a commit carries several bookmarks)
    _B="${_jj_out%%,*}"
    echo "base-branch: resolved B=${_B} via bookmark" >&2
    echo "${_B}"
    exit 0
  fi
fi

# ── Step 3: git branch fallback ───────────────────────────────────────────────
# NOTE: under jj HEAD is typically detached, so this is usually empty — kept for
# environments where the repo is used without jj.
_git_branch="$(git symbolic-ref --short -q HEAD 2>/dev/null)" || _git_branch=""
if [ -n "$_git_branch" ]; then
  echo "base-branch: resolved B=${_git_branch} via git-branch" >&2
  echo "${_git_branch}"
  exit 0
fi

# ── Step 4: refuse ────────────────────────────────────────────────────────────
echo "base-branch: cannot resolve the integration branch B." >&2
echo "  No SYNTO_FLOW_BASE set, no jj bookmark found on ancestors of @, and HEAD is detached." >&2
echo "  Set the override: export SYNTO_FLOW_BASE=<branch>" >&2
exit 1

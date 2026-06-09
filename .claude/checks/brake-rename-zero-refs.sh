#!/usr/bin/env bash
# Asserts the brake rename (human-in-the-loop -> manual) is complete across the LIVE operational surface.
# Exits non-zero (printing the offending file:line) if any stray `human-in-the-loop` reference remains
# OUTSIDE a line explicitly marked `MIGRATION:` (the fail-closed dual-brake exclusion, dropped last).
#
# Uses portable `grep -rnI` on purpose: ripgrep is frequently absent from a plain shell (e.g. Git Bash
# on Windows), and a missing tool must NOT silently produce a false "zero references" pass on a gate that
# authorizes dropping the brake. Historical docs/ (specs, plans) are NOT scanned. This script lives under
# .claude/checks/, which is itself out of scope (so its own mention of the token does not trip it).
set -euo pipefail
needle='human-in-the-loop'
command -v grep >/dev/null 2>&1 || { echo "brake-rename check: grep not found — cannot verify" >&2; exit 2; }
hits=$(grep -rnI "$needle" .claude/rules/github.md .claude/skills .claude/workflows 2>/dev/null \
       | grep -v 'MIGRATION:' || true)
if [ -n "$hits" ]; then
  echo "brake-rename incomplete — stray ${needle} reference(s):" >&2
  echo "$hits" >&2
  exit 1
fi
echo "brake-rename: zero stray ${needle} references in the live surface."

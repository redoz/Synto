#!/usr/bin/env bash
# trust.sh — the MECHANICAL author-identity gate for the issue-flow.
#
# WHY THIS EXISTS: the issue-flow loops read GitHub issue/comment TEXT to make decisions
# (approve / go-ahead / answer-that-advances, intake). That text is UNTRUSTED — anyone who can
# comment on (or open) an issue could embed prompt-injection. This script is the fence: identity
# is decided ONLY from GitHub metadata (author.login), NEVER from the comment body — so it CANNOT
# be prompt-injected. No LLM ever decides trust; an LLM only ever classifies the CONTENT of input
# this script has already certified as trusted. (See .claude/rules/github.md § Trust boundary.)
#
# TRUST PREDICATE (mechanical, self-configuring): an author is TRUSTED iff their login IS the
# account currently running the flow — i.e. the signed-in gh user (`gh api user`). The flow runs
# pull-based under YOUR gh token, so "trusted" == "authored by you". If someone else runs it under
# their account, they act ONLY on their OWN issues/comments — which, since they need repo access to
# run it at all, is fine. No hardcoded login, no repo-ownership assumption.
# OPTIONAL: $SYNTO_TRUSTED_LOGINS (comma-separated) trusts additional accounts (e.g. a co-maintainer).
#
# SUBCOMMANDS:
#   trust.sh new-human <issue>
#       JSON array of TRUSTED, HUMAN (non-"##"), UNADDRESSED comments — trusted human comments
#       strictly AFTER the last skill ("##") comment. THE primitive for "did the operator say
#       something we must act on?" (the unaddressed-comment guard, reconcile, /issue-respond).
#       Empty array => nothing to act on.
#   trust.sh comments <issue> [<iso-watermark>]
#       JSON array of ALL TRUSTED, HUMAN comments (optionally only those after <iso-watermark>).
#       For dialogue handlers that read the whole trusted thread.
#   trust.sh issue-author <issue>
#       Exit 0 + "trusted" if the issue's AUTHOR is trusted; else exit 1 + "untrusted".
#       Use at intake BEFORE stamping status:inbox, so a stranger's issue never enters the flow.
#
# FAIL CLOSED: any gh error aborts non-zero and prints nothing — a failure can never be read as trusted.
set -euo pipefail

cmd="${1:-}"; issue="${2:-}"
[ -n "$cmd" ] && [ -n "$issue" ] || { echo "usage: trust.sh new-human|comments|issue-author <issue> [<iso-watermark>]" >&2; exit 2; }

me="$(gh api user --jq .login)"   # the signed-in gh account running the flow (fail-closed: set -e aborts on gh error)

# allowlist = [ me ] + $SYNTO_TRUSTED_LOGINS, rendered as a jq array literal: ["me"] or ["me","extra1",...]
allow="[\"$me\""
IFS=',' read -ra _extra <<< "${SYNTO_TRUSTED_LOGINS:-}"
for _l in "${_extra[@]:-}"; do [ -z "$_l" ] && continue; allow+=",\"$_l\""; done
allow+="]"

trusted_sel="select(.author.login as \$l | $allow | index(\$l))"          # keep only trusted authors
human_sel="select((.body | test(\"^[[:space:]]*##\")) | not)"             # drop skill (H2 "##") comments

case "$cmd" in
  new-human)
    gh issue view "$issue" --json comments --jq "
      ( [ .comments[] | select(.body | test(\"^[[:space:]]*##\")) | .createdAt ] | max // \"1970-01-01T00:00:00Z\" ) as \$wm
      | [ .comments[] | $trusted_sel | $human_sel | select(.createdAt > \$wm) ]"
    ;;
  comments)
    wm="${3:-1970-01-01T00:00:00Z}"
    gh issue view "$issue" --json comments --jq "
      [ .comments[] | $trusted_sel | $human_sel | select(.createdAt > \"$wm\") ]"
    ;;
  issue-author)
    author="$(gh issue view "$issue" --json author --jq .author.login)"
    if [ "$author" = "$me" ] || printf '%s' ",${SYNTO_TRUSTED_LOGINS:-}," | grep -q ",$author,"; then
      echo "trusted"; exit 0
    fi
    echo "untrusted"; exit 1
    ;;
  *)
    echo "trust.sh: unknown subcommand '$cmd' (expected new-human|comments|issue-author)" >&2; exit 2 ;;
esac

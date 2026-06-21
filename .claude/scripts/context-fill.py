#!/usr/bin/env python3
"""
context-fill.py — report a Claude Code (sub)agent's current context-window fill,
read MECHANICALLY from its on-disk transcript (.jsonl).

Why this exists: an agent has no in-conversation API to read its own context. But each
(sub)agent's transcript is a JSONL file whose latest `type:"assistant"` entry carries a
`message.usage` object. The LIVE context size is the SUM of:

    input_tokens + cache_read_input_tokens + cache_creation_input_tokens

Raw `input_tokens` alone is ~nothing (often single digits) — almost all of the live
context lives in the cache fields, so you MUST sum all three or you under-report ~1000x.
The number is a one-turn-stale lower bound (it reflects the last COMPLETED assistant
turn, not the in-flight one) — fine for a between-task rotation check; leave headroom.

Interim spike tooling for the implement-orchestrator; this logic is destined to move
into `cargo dev` (the orchestrator subcommand). Single Python file (not the repo's
.sh/.ps1 pair) so the JSONL parsing lives in exactly one place and is callable
identically from bash or pwsh.

Usage:
  # fast path — discover the transcript once, then reuse it each check:
  python context-fill.py --transcript PATH [--window N | --model NAME] [--threshold PCT] [--pct]

  # discover own transcript by a unique marker the caller echoed earlier:
  python context-fill.py --marker STR [--projects-dir DIR] [--window N | --model NAME] [--threshold PCT]

Output: a JSON object on stdout (or just the number with --pct).
Exit codes: 0 = under threshold (keep going) | 10 = at/over threshold (ROTATE)
            2 = bad args / transcript not found | 3 = no usage entry found
"""
import argparse
import json
import os
import sys

# model-name substring -> context window (tokens)
WINDOWS = {
    "haiku": 200_000,
    "sonnet": 1_000_000,
    "opus": 1_000_000,
    "fable": 1_000_000,
}
DEFAULT_WINDOW = 1_000_000


def window_for(model):
    if not model:
        return DEFAULT_WINDOW
    m = model.lower()
    for key, val in WINDOWS.items():
        if key in m:
            return val
    return DEFAULT_WINDOW


def default_projects_dir():
    base = os.environ.get("CLAUDE_CONFIG_DIR") or os.path.join(os.path.expanduser("~"), ".claude")
    return os.path.join(base, "projects")


def find_by_marker(marker, projects_dir):
    """The agent-*.jsonl containing `marker`, searched NEWEST-FIRST.

    The marker is unique to the caller, so the first match IS the caller's transcript;
    scanning by most-recent mtime reaches the actively-written file almost immediately
    instead of reading every (possibly hundreds of) old transcript. Filtering to the
    `agent-` prefix excludes the parent session transcript (which also mirrors the marker).
    """
    needle = marker.encode("utf-8", "ignore")
    candidates = []
    for root, _dirs, files in os.walk(projects_dir):
        for name in files:
            if name.startswith("agent-") and name.endswith(".jsonl"):
                path = os.path.join(root, name)
                try:
                    candidates.append((os.path.getmtime(path), path))
                except OSError:
                    continue
    for _mtime, path in sorted(candidates, reverse=True):
        try:
            with open(path, "rb") as fh:
                if needle in fh.read():
                    return path
        except OSError:
            continue
    return None


def latest_usage(path):
    """The last `usage` dict in the transcript, or None."""
    last = None
    try:
        with open(path, encoding="utf-8") as fh:
            for line in fh:
                line = line.strip()
                if not line:
                    continue
                try:
                    obj = json.loads(line)
                except ValueError:
                    continue
                msg = obj.get("message") if isinstance(obj.get("message"), dict) else {}
                usage = msg.get("usage") or obj.get("usage")
                if isinstance(usage, dict):
                    last = usage
    except OSError:
        return None
    return last


def main():
    ap = argparse.ArgumentParser(description="Report a (sub)agent's context-window fill from its transcript.")
    grp = ap.add_mutually_exclusive_group(required=True)
    grp.add_argument("--transcript", help="explicit path to the agent-*.jsonl transcript")
    grp.add_argument("--marker", help="unique string the caller echoed; finds the agent-*.jsonl containing it")
    ap.add_argument("--projects-dir", default=default_projects_dir(),
                    help="root to search for --marker (default: ~/.claude/projects)")
    ap.add_argument("--window", type=int, help="context window in tokens (overrides --model)")
    ap.add_argument("--model", help="model name; maps to a window (haiku=200k, sonnet/opus/fable=1M)")
    ap.add_argument("--threshold", type=float, help="fill%% at/above which to ROTATE (sets exit code 10)")
    ap.add_argument("--max-tokens", type=int,
                    help="absolute context_tokens at/above which to ROTATE (e.g. 175000). A quality-zone cap; "
                         "more robust than --threshold%% since the 'still-sharp' zone is ~absolute, not a window fraction")
    ap.add_argument("--pct", action="store_true", help="print only the fill percentage")
    args = ap.parse_args()

    path = args.transcript
    if args.marker:
        path = find_by_marker(args.marker, args.projects_dir)
        if not path:
            print(json.dumps({"error": "no agent-*.jsonl found containing marker", "marker": args.marker}),
                  file=sys.stderr)
            return 2
    if not path or not os.path.exists(path):
        print(json.dumps({"error": "transcript not found", "path": path}), file=sys.stderr)
        return 2

    usage = latest_usage(path)
    if not usage:
        print(json.dumps({"error": "no usage entry in transcript", "path": path}), file=sys.stderr)
        return 3

    inp = int(usage.get("input_tokens", 0) or 0)
    cache_read = int(usage.get("cache_read_input_tokens", 0) or 0)
    cache_creation = int(usage.get("cache_creation_input_tokens", 0) or 0)
    ctx = inp + cache_read + cache_creation
    window = args.window if args.window else window_for(args.model)
    pct = round(100.0 * ctx / window, 1) if window else 0.0
    over_pct = args.threshold is not None and pct >= args.threshold
    over_abs = args.max_tokens is not None and ctx >= args.max_tokens
    rotate = over_pct or over_abs

    out = {
        "transcript": path,
        "input_tokens": inp,
        "cache_read_input_tokens": cache_read,
        "cache_creation_input_tokens": cache_creation,
        "output_tokens": int(usage.get("output_tokens", 0) or 0),
        "context_tokens": ctx,
        "window": window,
        "pct": pct,
    }
    if args.threshold is not None:
        out["threshold_pct"] = args.threshold
    if args.max_tokens is not None:
        out["max_tokens"] = args.max_tokens
    if args.threshold is not None or args.max_tokens is not None:
        out["should_rotate"] = rotate

    print(pct if args.pct else json.dumps(out, indent=2))
    return 10 if rotate else 0


if __name__ == "__main__":
    sys.exit(main())

---
name: evaluate
description: Evaluate codebase quality across four dimensions (correctness, maintainability, performance, testability) plus the principal-engineer lens. Produces a scorecard usable as a quality gate.
user_invocable: true
---

# Evaluate

Comprehensive quality evaluation of the Synto codebase across four dimensions —
**Correctness, Maintainability, Performance, Testability** — plus the
principal-engineer lens.

The entire multi-round fan-out runs in the **`evaluate` workflow**
(`.claude/workflows/evaluate.js`). This skill invokes it and presents the result.
Moving the orchestration into a workflow flattens what used to be a deep team of
sub-agents into one deterministic script driving leaf agents — no nested-agent wall,
and the run is resumable.

## Run it

Call the **Workflow tool**:

```
Workflow({ name: 'evaluate' })
```

The GitHub issue reconciliation is **off by default** (`skipSync` defaults to `true`) — a
plain run is findings-only and never mutates the repo. Opt in with
`Workflow({ name: 'evaluate', args: { skipSync: false } })` once you actually want findings
synced to `evaluation`-labelled GitHub issues (the first such run creates the `evaluation`
label and the issues).

## What the workflow does

1. **Baseline** — an agent runs `dotnet build --no-restore -c Debug`,
   `dotnet test --no-build -c Debug`, and `dotnet format --verify-no-changes`; build
   failure is a critical correctness finding, failing tests critical/high. (Analyzer
   warnings surfaced by the build are *findings*, not hard green-gate failures —
   `TreatWarningsAsErrors` is not set in Synto.) Results feed every reviewer.
2. **Triage** — maps each project to its most-relevant dimensions (from each project's
   `README.md` and `.csproj`) so reviewers read high-relevance projects in depth and
   skim the rest.
3. **Round 1 (discovery)** — 5 reviewers in parallel (4 dimensions + the
   principal-engineer lens), each reading `.claude/playbook/architecture.md`,
   `principles.md`, `standards.md`, and their own checklist/focus areas. Evidence (a
   3–10 line snippet) is required for every critical/high.
4. **Round 2 (verification + cross-pollination)** — 5 fresh reviewers verify their own
   criticals/highs against the code, challenge false positives, reinforce agreements,
   discover cross-dimensional findings, and adjust severities. The principal-engineer also
   receives mediums so they can promote convergent risks and challenge workarounds.
5. **Synthesis** — the team-lead agent resolves conflicts, correlates root causes,
   builds the prioritized action plan, and writes the full report.
6. **Sync** (only when `skipSync: false`) — reconciles findings with `evaluation`-labelled GitHub
   issues: stale review (close fixed / keep valid / respect deferred), then create /
   update / re-confirm. Returns a sync summary.

## Scoring model

The reviewers and synthesis follow `.claude/playbook/standards.md` — severity definitions,
per-dimension scores (Excellent / Good enough / Needs improvement / Broken), and the
quality-gate tiers (Correctness is the hard gate; Maintainability/Performance must be
Good enough+; Testability must not be Broken), plus the principal-engineer (consequences)
cross-cutting lens.

## Present the result

The workflow returns `{ report, gate, syncSummary, counts }`. Print `report` verbatim
(it is already formatted as the Quality Scorecard → Findings → Domain Expert Assessment
→ Root Causes → Prioritized Action Plan → Strengths → Verification Notes), then append
the `syncSummary`. Lead with the **quality gate: PASS / FAIL**.

## Notes

- **Live cross-expert consultation** (reviewers messaging each other mid-round) is not
  part of the workflow model — leaf agents aren't addressable to one another. The
  two-round structure (Round 2 reviewers see Round 1's findings) preserves the
  cross-dimensional discovery that consultation provided.
- The principal-engineer's findings are attributed to the dimension they most affect and
  counted in that dimension's score (no separate scorecard column), per `standards.md`.

# Synto — agent guide

Synto ("**SYN**tax **TO**olkit") is a set of Roslyn incremental source generators that quote
and template C# syntax trees. For orientation read `README.md` (what it is + the quickest
example) and `.claude/playbook/architecture.md` (the domain context + packaging model).

**Navigation map — `docs/kb/`** (read on demand): an OKF knowledge bundle mapping Synto's
internals — where code lives + non-obvious invariants, each fact a `path:line` link. Start
at `docs/kb/index.md` to orient before exploring source. The `refresh-kb` skill keeps it in
sync with the code. (This is the *where/what* layer; the playbook below is the *why*.)

## Playbook — `.claude/playbook/` (read on demand, NOT auto-loaded)

Reference material for reviewing, evaluating, and reasoning about Synto. These are **not
always-on instructions** — open the relevant file only when the task calls for it. The
`evaluate` skill and the `implement-plan` workflow read them automatically; you generally
only open them by hand for a manual review or a design question.

**Domain context & standards**
- `architecture.md` — domain context, project layering, codebase navigation. Read this first
  before reviewing or changing Synto source.
- `principles.md` — stable design principles (runtime/generator layering, the consumer
  surface, incremental-generator cacheability, single-source-of-truth injection).
- `standards.md` — severity definitions, dimension scores, quality-gate tiers, review verdicts.
- `project-phase.md` — pre-release severity calibration (what to down-rate vs. keep at full
  severity while Synto is experimental).

**Per-dimension review checklists**
- `correctness.md`, `maintainability.md`, `performance.md`, `testability.md` — one per quality
  dimension.
- `consequences.md` — the principal-engineer lens (architectural second-order consequences).

## Tooling

- `/evaluate` — on-demand, multi-dimension quality review of the codebase.
- `/handoff` — generate a dense session handoff doc.
- `implement-plan` workflow — implement one approved plan via subagent-driven TDD (jj-native).

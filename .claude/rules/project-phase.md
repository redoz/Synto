# Project Phase — severity calibration (pre-release → 1.0)

Cross-cutting calibration for every reviewer and evaluator. Read it alongside
`standards.md`: where `standards.md` defines *what* each severity means, this file
defines *where the bar sits right now* — because the same finding is critical in a
shipped, depended-on library and noise in a pre-release experiment.

## Current phase: **pre-release**

Synto is a **pre-1.0, experimental library with no real consumers yet** (the README calls
it "experimental"; it is mid packaging-redesign — see `architecture.md`). We are proving the
shape works: quote and template C# syntax through incremental generators, with the
source-injection packaging model. **Nothing has shipped to real users and nothing depends on
the surface yet.** Review accordingly — rate a finding by whether it blocks Synto from doing
its job *today*, not by what a stable, widely-consumed 1.0 would need.

This is **not** a license to wave through bad code. It is a license to stop treating
*future hardening* as a *present defect*.

## How to calibrate

**Down-rate — 1.0-phase work, not pre-release blockers.** Surface these, but as **low/medium
tagged `pre-release`** so nothing is lost and the list is ready to tighten when we flip.
Don't spend a `high`/`critical` on them:

- Feature completeness the toolkit isn't building yet — syntax kinds the quoter doesn't yet
  handle, the not-yet-built `Matching` feature, template options that aren't implemented.
- Generator performance tuning where the current cost is fine at the current scale (an O(n²)
  over a handful of syntax nodes, an extra tree walk) — **as long as it doesn't break
  incremental caching**, which is not a tuning concern (see below).
- Robustness against inputs we don't yet face — exotic nested generics, pathological
  templates, malformed-but-rare syntax shapes.
- Polish: naming nits, the known **duplicated quoter/helper sources** linked between
  `src/Synto` and `src/Synto.Bootstrap` (a deferred cleanup), doc-comment gaps, "this could
  be cleaner."
- Packaging niceties that don't change the contract (a missing `README` snippet, an
  analyzer-warning nit — `TreatWarningsAsErrors` is not set, so these are findings, not gates).

**Keep full severity — as critical in pre-release as at 1.0:**

- **Correctness bugs that break generation now** — a quoter that emits syntax which doesn't
  round-trip, generated code that doesn't compile, a template that produces wrong output, a
  wrong diagnostic, or an exception escaping the generator instead of becoming a `DiagnosticInfo`.
  The whole point is trustworthy syntax generation; if the output is wrong, the experiment has
  already failed.
- **Incremental-caching breakage** — capturing `Compilation`, `ISymbol`, `SemanticModel`, or
  `SyntaxNode` into pipeline state, or flowing a non-equatable value through a provider. This
  re-runs the generator on every keystroke and is both a correctness and a performance defect;
  it is the core discipline, so it keeps full severity regardless of phase.
- **One-way doors** — decisions that are cheap to fix now and irreversible (or brutally
  expensive) once consumers exist. These get *more* urgent in pre-release, not less, because
  now is the cheap moment:
  - the **public API surface of `Synto.Core`** — once code references it, it's frozen;
  - the **injected-surface shape** consumers' generated code binds to — the five marker
    types, the `internal`-vs-`public` and `file`-scoped-helper decisions, the helper method
    names — change these freely now, never after consumers compile against them;
  - **package identity** — `Synto`, `Synto.Core`, `Synto.Diagnostics` — renaming or merging a
    package after publish strands every consumer;
  - the **generated-output shape** consumers depend on — the factory method's name,
    signature, and namespace that the snapshots pin; an unexplained change here is a contract
    break, not a refactor.

  This is exactly `consequences.md`'s "rate by where the code ends up": an irreversible wrong
  turn is critical *today* even though it works today. Pre-release realism narrows the
  *reversible* future-proofing; it does **not** excuse one-way doors.

## Flipping to 1.0

When we declare 1.0, change **Current phase** above to `1.0` and the bar tightens: the
down-rated feature-completeness / hardening / performance-tuning concerns become real
findings again, and the one-way doors above harden from "get it right now" into "you can no
longer change this." Until then, that work is deferred by design, not overlooked.

# Adaptive Rigor — right-sizing the issue flow

Shared policy for every issue-flow stage skill — `issue-brainstorm`, `issue-spec-review`,
`issue-plan`, `issue-plan-review`, and `issue-implement` / `implement-plan`. Each stage
references this file **by path** and applies the part for its stage — the pattern
`architecture.md` / `github.md` / `standards.md` already establish (referenced, never
duplicated). This is the single source of truth for *how much rigor a stage spends*.

Adaptive rigor is the **inverse of the `manual` brake**. Where `manual` adds friction at
every gate, adaptive rigor *removes* it where the work in front of the stage doesn't
warrant it — a quick brainstorm for a self-evident issue, a skipped plan for a small
spec, a relevant subset of reviewers for a narrow change, a one-shot implement for a
trivial diff. It is **autonomous** (the stage decides and acts — no propose-and-wait),
**recorded** (every relaxation is an auditable `## ⚡ Rigor` comment), and bounded by an
**immovable floor** (§ The floor) that quality can never drop below.

Full design + rationale: `docs/superpowers/specs/2026-06-19-adaptive-rigor-issue-flow-design.md`.

## § Authority — decide and act, floor-protected

A stage **decides its own rigor at the moment it has the most information** and acts
immediately — it does **not** propose and wait for a human. The emergent property is
load-bearing: *"small spec → skip the plan"* is knowable only **after** the spec exists;
*"this plan only touches doc-comments"* only **after** the plan exists. There is no up-front
tier — each stage sizes the work with what it now knows.

Quality is protected by the **floor (§ The floor)**, not by a per-stage human gate. A
human may **veto or upshift** at any time — a comment is picked up by reconcile / the
unaddressed-comment guard exactly as today (github.md). Under **`manual`** every knob
defaults to `full` (§ The brake).

This mirrors the flow's spine: the strict review team's LGTM auto-advances, and `manual`
is the only brake. Adaptive rigor is the accelerating lever on that same spine.

## § The knobs

Each stage has one or two knobs. `full` is always the default; relax **only** when the
stage's heuristic (§ Heuristics) is met **and** `manual` is not set.

| Stage (skill) | Knob | `full` (default) | relaxed | Relax when… |
|---|---|---|---|---|
| **brainstorm** (`issue-brainstorm`) | depth | async Q&A, park for answers | **quick**: one-pass short spec, no questions | issue body specifies the change unambiguously; no open design choice |
| **spec-review** (`issue-spec-review`) | team | all 4 dims + 1 expert | **subset** (+ floor) | spec clearly cannot implicate a dimension |
| **spec-review** (`issue-spec-review`) | plan? | LGTM → `plan-queued` | **skip-plan**: LGTM → `ready` (thin plan derived, tagged `one-shot`) | spec is fully diff-reviewable; no decomposition / new feature area / sequencing |
| **plan** (`issue-plan`) | depth | full authored plan | **concise** plan; suggests an implement rigor | only reached when the plan was *not* skipped |
| **plan-review** (`issue-plan-review`) | team | all 4 dims + 1 expert | **subset** (+ floor) | plan touches a narrow concern |
| **plan-review** (`issue-plan-review`) | implement | `tdd-per-task` | **one-shot** tag | single concern, small, diff-reviewable |
| **implement** (`implement-plan`) | rigor | subagent TDD per task | **one-shot**: change → green-gate → floor diff-review → ff-push | the plan carries the `one-shot` tag |

Two invariants survive every relaxation:

- **A spec always exists** — even a 5-line one. We relax its *depth*, never its
  *existence* (the review needs an artifact; file-is-canonical holds). Skipping the spec
  entirely is **out of scope**.
- **Skip-plan still produces a plan file** — a thin one, auto-derived (§ Skip-plan), not
  *no* plan. `implement-plan`'s contract (one real top-level plan, archived to
  `completed/`) is untouched.

## § Heuristics (per knob)

- **brainstorm → quick** — relax when the issue body already specifies the change
  unambiguously and there is no open design or behavioral choice (label rename, doc-comment
  fix, dependency bump, config-default flip). Stay `full` for any genuine design question,
  consumer-facing / behavioral choice, or scope ambiguity.
- **review → subset** — drop a dimension only if the artifact *clearly cannot implicate
  it* (a doc-comment-only change drops performance + testability, keeping maintainability +
  principal-engineer; a snapshot-golden update keeps correctness + testability +
  principal-engineer — it pins the output-shape contract). **Correctness is never dropped**
  (§ The floor). Record which ran and which were dropped + why.
- **spec-review → skip-plan** — skip when the spec is *fully reviewable from the final
  diff* and needs no decomposition: no new feature folder/namespace, no cross-cutting
  pipeline change, no multi-step sequencing, no non-obvious TDD structure. The plan would
  add nothing beyond the spec. Otherwise advance to `plan-queued` as today.
- **implement → one-shot** — tag when the change is a single concern, small, and
  diff-reviewable, with no new logic branches that want test-first design. Otherwise
  `tdd-per-task`.

When in doubt at any knob, **stay `full`** — the cheap path is for the clearly-trivial,
not the merely-probably-fine. The conservatism mirrors the gate classifier in github.md.

## § The floor (never relaxed, every path)

These hold on **every** path, relaxed or not — they are what make the cheap path safe:

1. **A code review of the final diff always runs before any merge to `main`.** In
   `tdd-per-task` mode this is the per-task spec + quality reviews plus the final
   whole-plan diff-review; in `one-shot` mode it is the green-gate + the final diff-review,
   which is there the *sole* review and runs **before** the ff-push. This is
   `implement-plan`'s built-in diff review against the plan's base commit — the same gate
   `/code-review` embodies for a manual diff. It is non-negotiable in both modes.
2. **Correctness is always in any review subset** — the one hard-gate dimension in
   `standards.md`. A subset drops from the *other* four (the three remaining dimensions and
   the principal-engineer expert); never this one. (`issue-review.js` force-includes it as a
   backstop even if a caller's subset omits it.)
3. **The green-gate always runs** — the full test suite passes before any ff-push to
   `main`, one-shot included.
4. **Upshift on surprise (§ Upshift)** — a relaxation is a hypothesis; contradicting
   evidence forces escalation back to the fuller path.

## § Upshift — the cheap path is self-correcting

Ride the cheap path only **while the evidence supports it**. The moment a stage uncovers
complexity beyond its assumed rigor, it climbs back and records why (§ Recording):

- quick-brainstorm hits a real design question → park `blocked` with the questions (the
  normal full-brainstorm park).
- a plan-skipped spec turns out to need decomposition (found while deriving the thin plan,
  or at implement) → route to `plan-queued` (a real plan).
- a subset reviewer flags something implicating a **dropped** dimension → re-run with that
  dimension added.
- one-shot implement finds the change is bigger / riskier than assumed → **stop and park**
  (`implement-plan` reports `NEEDS_CONTEXT` / not-merged; `issue-implement` leaves it
  `implementing` + `blocked`). A human clears the `one-shot` tag to retry as
  `tdd-per-task`, or routes it back to `plan-queued`.

Upshift is always allowed and is never penalized — escalating is correct, not failure.

## § Recording (auditable, preserves the H2 convention)

Every **relaxation** and every **upshift** posts **exactly one H2 comment**. Running
`full` rigor is the **silent default** — it needs no comment (nothing was relaxed, so
there is nothing to audit).

The comment **must** be an H2 (`## …`) so it stays a *skill* comment: reconcile and the
unaddressed-comment guard (github.md) treat any non-`##` comment as human input, and that
invariant must not break.

```markdown
## ⚡ Rigor — {stage}: {decision}

{one-line rationale tied to concrete evidence}
```

`{stage}` ∈ {brainstorm, spec-review, plan, plan-review, implement}; `{decision}` ∈
{quick, subset, skip-plan, concise, one-shot, upshift}. Examples:

- `## ⚡ Rigor — brainstorm: quick` — "Pure label rename, no design choice; one-pass spec."
- `## ⚡ Rigor — spec-review: skip-plan` — "Spec is 9 lines, doc-comment fix, no new logic, diff fully reviewable → ready, one-shot."
- `## ⚡ Rigor — plan-review: subset` — "Internal-helper rename plan; ran maintainability + principal-engineer + (floor) correctness; dropped the other two (performance, testability)."
- `## ⚡ Rigor — implement: upshift` — "One-shot aborted: change touches the equatable pipeline model / cacheability; needs a full plan — parked for re-route."

**No new label.** Relaxation is emergent and lives in the comment trail, not a global
`rigor:lite` tier.

## § The tag & the status jump — how relaxations reach the machinery

Three mechanisms carry a relaxation into the rest of the flow, with different reach:

- **One-shot is a plan-file tag** — transparent to **every** loop. The promoted top-level
  plan carries a header line:

  ```markdown
  > **Rigor:** one-shot
  ```

  `implement-plan` reads this line itself (default `tdd-per-task` when the line is absent),
  so `issue-implement` and any loop's implement step get one-shot for free — they pass the
  plan path exactly as today; no caller change.

- **Skip-plan is a status jump** — reaches a loop **only if that loop delegates the stage's
  routing to its skill.** `issue-spec-review` runs `set-status <n> ready` instead of
  `plan-queued` (§ Skip-plan derives the thin one-shot plan inline so the `ready` invariant
  holds). The loops (`/walk-issue` and `/burn-the-board`, the same `walk-issue` workflow)
  delegate routing to the skill — their route step *follows `issue-spec-review`'s **Route** step
  (its § Approval-detection routing, including the skip-plan branch)* — so the skip-plan status
  jump flows through them.

- **Subset is a `dimensions` arg to `issue-review`** — decided in the skill *before* the
  review runs. On a manual skill run the skill decides the subset. The `walk-issue` workflow
  (which owns the `issue-review` call for both `/walk-issue` and `/burn-the-board`) reads the
  artifact, picks the floor-protected subset per § Heuristics, and forwards `dimensions` to
  `issue-review` — so the loops get the subset too. `correctness` remains force-included by
  `issue-review` (the floor), whatever subset a caller passes.

Once an issue is at a given `status:*`, what a loop *does* with it is unchanged — loops
route by `status:*` and the unaddressed-comment guard keeps working, because rigor comments
are H2 and are never mistaken for human input.

## § Skip-plan — deriving the thin plan inline

`implement-plan` requires a real top-level plan path (it archives it to `completed/`), so
skip-plan cannot mean "no plan file." On the **spec-review → ready** transition,
`issue-spec-review`:

1. Promotes the spec as on any LGTM (move the draft file → top-level `specs/`).
2. **Derives a thin plan from the spec in one pass** (no `issue-plan` stage, no
   `issue-plan-review` team) and writes it to top-level
   `docs/superpowers/plans/{YYYY-MM-DD-slug}.md` directly — born promoted, because the
   spec LGTM is its gate. Its header carries `> **Tracking issue:** #<n>`,
   `> **Spec:** docs/superpowers/specs/{slug}.md`, and `> **Rigor:** one-shot`.
3. `set-status <n> ready` (no `blocked`), and records the `## ⚡ Rigor — spec-review:
   skip-plan` comment.

We skip the *expensive* parts (full plan authoring + the multi-agent plan-review), **not**
the plan *file*. The `ready` invariant and all downstream machinery (implement, archival)
stay intact. A thin plan is allowed to have **no** `### Task` sections — `implement-plan`'s
one-shot mode reads the whole plan as the change description rather than extracting tasks.

## § The brake — `manual` restores full rigor

When `manual` is set, **all knobs default to `full`**: no auto-relaxation, full review
team, plan authored, TDD implement, every review parks `blocked` for `/issue-respond` as
today. `manual` is the single "maximum rigor, hand-stepped" brake — the clean inverse of
the adaptive default. (Want relaxed *and* hand-stepped? Simply don't set `manual`.)

**The autonomous trade-off, honestly:** under any loop that auto-implements `ready` plans
(e.g. `/burn-the-board`), a one-shot change lands on `main` unseen. This is an *extension*
of the already-documented hands-free trade-off (an approved `ready` plan already implements
to `main` unseen), not a new surprise — the floor (green-gate + final diff-review) is what
makes it acceptable; `manual` remains the escape hatch (a `manual` issue is excluded from
auto-implement, so it lands nothing without `/issue-implement`).

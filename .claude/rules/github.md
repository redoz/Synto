# GitHub Conventions — the issue-planning flow

Single source of truth for how Synto uses GitHub to drive an issue from idea to
shipped change. Every `issue-*` skill and the `issue-review.js` workflow reference this
file **by path** in their prompts rather than duplicating it — the pattern
`architecture.md` already establishes. It documents the existing `evaluate` conventions
(so it captures reality) **and** the planning-flow conventions below.

The flow is a traceable, review-gated path: an idea is **brainstormed** into a spec, the
spec is **reviewed**, the approved spec is **planned**, the plan is **reviewed**, and the
approved plan is **implemented** and the issue closed. Both review-gated halves share one
shape — **draft → review → human verdict** — with the human discussing on the issue in
natural language. See `docs/superpowers/specs/2026-06-14-issue-planning-flow-design.md`
for the full design (this doc is the operational reference; the spec is the rationale).

**Labels are the source of truth.** The board (§ The board) is pure visualization — no
skill ever reads it, so column drift is harmless.

---

## § Label inventory

**Existing — unchanged** (describe *what an issue is*; created by `evaluate`):

- `evaluation`
- dimension: `maintainability`, `correctness`, `performance`, `testability`
- severity: `critical`, `high`, `medium`, `low`

**Lifecycle `status:*`** (describe *where an issue is*). **Exactly one** `status:*` per
in-flight issue — single-select: every skill removes the prior `status:*` and adds the
new one in the same step (§ Setting state). One per board column (§ The board). The
**11 lifecycle labels**, one per in-flight issue:

- `status:inbox`
- `status:brainstorm-queued`
- `status:brainstorming`
- `status:spec-review-queued`
- `status:spec-reviewing`
- `status:plan-queued`
- `status:planning`
- `status:plan-review-queued`
- `status:plan-reviewing`
- `status:ready` — the "ready for implementation" gate
- `status:implementing`

(Done = issue closed; no label.)

**Modifiers** (orthogonal to `status:*`, may co-exist with each other and any status):

- `blocked` — *in-flight work is stuck waiting on you* (§ Human-signal labels). Set only
  on a **Doing** column; set/cleared dynamically by the skills.
- `manual` — the **brake** (§ Human-signal labels). Default-**off**, opt-in.

**Structural / meta** (orthogonal to the lifecycle — they mark a *kind* of issue, not a stage):

- `epic` — a tracking **parent** (§ Sub-issues & work graph). Carries **no** `status:*`: an
  epic is a board **swimlane, never a card**, and intake (§ Intake) skips any `epic`-labelled
  issue so it is never auto-stamped onto the board.
- `issue-flow-bug` — a defect in the issue-flow **machinery itself** (a broken skill or
  workflow), filed by § Reporting a broken skill — searched-first and de-duped.

Labels are created **idempotently** — a skill creates any missing label on first use
(create-if-missing, mirroring how `evaluate` creates the `evaluation` label). The
`status:*` family shares one color; `blocked` and `manual` each get a distinct
color:

```bash
# idempotent — ignore "already exists"
for s in inbox brainstorm-queued brainstorming spec-review-queued spec-reviewing \
         plan-queued planning plan-review-queued plan-reviewing ready implementing; do
  gh label create "status:$s" --color BFD4F2 --description "Issue flow stage" 2>/dev/null || true
done
gh label create "blocked"           --color D93F0B --description "Needs your judgment/input" 2>/dev/null || true
# brake label renamed: on existing repos rename in place — `gh label edit "human in the loop" --name "manual"`; create-if-missing below covers fresh repos
gh label create "manual" --color B60205 --description "Brake: never auto-advance" 2>/dev/null || true
gh label create "issue-flow-bug"    --color E11D21 --description "Broken issue-flow skill/tooling" 2>/dev/null || true
gh label create "epic"              --color 5319E7 --description "Tracking parent — swimlane, never a board card" 2>/dev/null || true
```

## § Setting state (single-select + blocked)

The canonical procedure every skill (and the autonomous driver) follows to move an issue is a
**single command** — it does the single-select `status:*` swap, the `blocked` flag, **and** the
board card move *together*, so the board can never drift from the labels:

```bash
# the ONE way to move an issue's state — does the label change (authoritative) AND the board card
# move (best-effort, logged) in one always-callable command. See the /set-status skill for details.
# Two equivalent twins; use whichever matches your shell:
bash .claude/scripts/set-status.sh  <issue> <new-status> [block|unblock]   # from a bash shell (Git Bash)
pwsh .claude/scripts/set-status.ps1 <issue> <new-status> [block|unblock]   # from PowerShell (Windows-native)
```

`<new-status>` is the short form (`brainstorm-queued`, `spec-reviewing`, `ready`, …); `block` /
`unblock` sets/clears the `blocked` modifier; `done` (after `gh issue close`) is the close
transition — it **strips** any `status:*` and `blocked` label (Done = closed, no label) **and**
moves the *closed* issue's card to the Done column. **Any "`set_status`" / "`sync_board`" / "board
sync" mention in any skill or driver prompt means exactly this: run this one script.**

**Why one command, not two snippets.** The board move used to be a separate `sync_board` shell
*function* called from the label snippet — but a fresh shell didn't have it defined, so the label
moved while the card silently didn't (swallowed as "best-effort"). The script is one
always-callable command (works from a skill, the driver's agents, or the CLI) and **logs** a board
failure on stderr instead of hiding it. **Labels remain the source of truth; the board is a
best-effort view** — a board failure prints why and never blocks the transition.

The status → board-column map (looked up live by option name on the "Synto — Issue Flow" project,
never hardcoded):

| status | column | status | column |
|---|---|---|---|
| `inbox` | Inbox | `plan-queued` | Plan: Queued |
| `brainstorm-queued` | Brainstorm: Queued | `planning` | Planning |
| `brainstorming` | Brainstorming | `plan-review-queued` | Plan Review: Queued |
| `spec-review-queued` | Spec Review: Queued | `plan-reviewing` | Plan Reviewing |
| `spec-reviewing` | Spec Reviewing | `ready` | Ready |
| `implementing` | Implementing | `done` (closed) | Done |

**Invariant:** after any transition the issue carries **exactly one** `status:*` label
(the prior one removed), and `blocked` is present only on the Doing-column states
enumerated in § Human-signal labels. The board card move is downstream of (and never
gates) the label write.

**Canonical driver snippets.** These three are the single copy-paste source for the
autonomous driver's (§ The autonomous driver) commit-gate watermark, its mid-stage
human-comment check, and its primary-checkout write — kept here beside the set-status procedure so the
shared `gh`/git helpers live in one place (DRY).

```bash
# watermark <issue>  — the commit-gate baseline: max(createdAt) over the issue's comments, tie-broken by
# comment url; epoch sentinel when there are none. Captured at claim (review) or the run-start `ready`
# snapshot (Phase 2), then re-checked at stage exit.
gh issue view "$1" --json comments -q \
  '([.comments[] | {t:.createdAt, url:.url}] | max_by(.t)) // {t:"1970-01-01T00:00:00Z", url:""}'
```
```bash
# comments_after <issue> <iso-watermark>  — non-## (human) comments strictly after the watermark.
# Non-empty ⇒ a human spoke mid-stage; classify the latest (go-ahead/no-caveat = advance, else park).
gh issue view "$1" --json comments -q \
  "[.comments[] | select(.createdAt > \"$2\") | select((.body | test(\"^[[:space:]]*##\")) | not)]"
```
```bash
# ff_push  — the canonical primary-checkout write (reconcile's /issue-respond promotion; any close-comment
# commit). Rebase-retry fast-forward onto origin/main; on exhaustion reset the primary checkout to
# origin/main (so the next pass's ff-sync still passes) and surface a machinery-failure (§ Reporting a
# broken skill). Run from the primary checkout with HEAD already carrying the commit(s).
ff_push() {
  local n=0 max=3
  until [ "$n" -ge "$max" ]; do
    git fetch origin && git rebase origin/main && git push origin HEAD:main && return 0
    git rebase --abort 2>/dev/null || true
    n=$((n+1))
  done
  git reset --hard origin/main
  echo "ff_push: exhausted $max attempts — reset primary checkout to origin/main" >&2
  return 1
}
```

## § The board

The lifecycle is visualized as a **GitHub Project (v2) kanban** with one column per
Queue/Doing cell of the pipeline — **12 columns**, in order:

`Inbox · Brainstorm: Queued · Brainstorming · Spec Review: Queued · Spec Reviewing · Plan: Queued · Planning · Plan Review: Queued · Plan Reviewing · Ready · Implementing · Done`

Configuration is **one-time, manual, and lives here**. The board is a **view, not a
contract** — no skill reads it:

- **Auto-add by label** so any issue carrying a `status:*` label joins the board.
- Map each `status:*` value to the matching Status column.
- The `blocked` modifier is surfaced as a board **filter/swimlane** (not a column), so
  "what needs me" reads off any stage at a glance.

Because labels are the source of truth and no skill reads the board, column sync is
**best-effort**: the set-status procedure (§ Setting state) moves the card in the same command, adding
the card if needed and setting its Status option — but if that call fails, lags, or a card is
dragged by hand, nothing breaks. The Status single-select option IDs are looked up via
`gh project field-list <num> --owner <org> --format json` **at use time** — never
hardcoded (the option set changes when the board is reconfigured).

**Board writes run under your local `gh` token.** Everything in this flow — skills, the
driver, manual runs — is pull-based and runs on your machine, so the board move writes the
org Project with your own credentials. The one requirement is that the token carry
`project` scope: run `gh auth refresh -s project` once. There is **no repo PAT** — the
flow deliberately uses no GitHub Actions (which would have needed a `project`-scoped
secret the default `GITHUB_TOKEN` can't provide), so nothing here depends on a stored
token.

**Parents are swimlanes, not cards.** A parent/epic issue (§ Sub-issues & work graph)
carries the **`epic`** label and **no** `status:*` label, so it never becomes a board card
(and intake, § Intake, skips any `epic`-labelled issue). The board view groups
by the native **Parent issue** field, so an epic shows as a **swimlane** over its
children. Only the in-flight work units (the issues that pass through `set_status`) are
cards. **Done = the issue is closed** — the close transition runs `set-status <n> done`, which
strips any `status:*`/`blocked` label and moves the closed card to the Done column (§ Setting state);
the built-in "Item closed → Done" project workflow remains as a redundant backstop.

## § Intake (the front door)

A new issue is invisible to the flow until it carries a `status:*` label — that label is
what the board's *auto-add-by-label* picks up and what the queues key on. Nothing else in
the flow applies that *first* label, so a freshly-filed issue (human-filed or
`evaluate`-created) would sit off-board until labelled. **Intake** stamps **`status:inbox`**
on such an issue, making **Inbox the single front door** — where `/issue-triage` shapes it —
then best-effort adds the card to the board.

Intake is **pull-based** — no GitHub Action (see § The board for why) — and runs two ways:

- **The driver's reconcile sweep** (§ Queues vs Doing, and reconcile) — at the start of each
  pass the driver lists open issues and stamps `status:inbox` on every un-classified one.
  This is the automatic path: with the driver (or `/loop`) running, new issues land on the
  board on their own.
- **`/issue-triage <n>` self-stamp** — run by hand on an un-classified issue, triage stamps
  `status:inbox` itself before shaping it. This is the manual path when no driver is running.

Two carve-outs, both checked against the issue's labels at the time intake runs:

- **Already-classified** — if the issue already carries any `status:*`, skip (don't clobber
  a status the creator set deliberately, e.g. `/issue-split` opening its children directly
  at `brainstorm-queued`).
- **Epics** — if the issue carries the **`epic`** label, skip. An epic is a swimlane, not a
  card, and must **never** be auto-stamped onto the board. `/issue-split` creates its epics
  with `--label epic` atomically at `gh issue create`, so the label is present when intake
  runs and the epic is correctly skipped.

## § Comment templates

All comment types are defined here so the skills emit a consistent, greppable thread.
**Round N** in the review/brainstorm headers is computed from the count of prior
same-type comments on the issue + 1 (§ The round limit K). The skills emit these
**exactly**; `{…}` are fill-ins.

**Write the fill-ins brief, in telegraph English.** A professional reads these — assume
domain fluency, skip the throat-clearing, don't explain what they already know. Drop
articles, auxiliaries, and filler; keep the load-bearing nouns and verbs ("Adds equatable
wrapper to template pipeline", not "This change adds an equatable wrapper to the template generator pipeline model").
Every token costs — name the concrete thing and stop. These comments drive a quick
decision: short, dense, scannable. Avoid showy phrasing. Full prose and precise wording
belong in the spec or plan *file*, not the issue comment.

**Triage comment** (`/issue-triage`, while shaping an Inbox idea) — clarifying Q&A that
keeps the issue in Inbox:

```markdown
## 💬 Triage — round {N}

{answer the human's question, and/or 1–3 clarifying questions — multiple-choice where possible}

<sub>still shaping in Inbox — re-run `/issue-triage #{n}` after you reply, or say "go ahead" to brainstorm it</sub>
```

On a decision `/issue-triage` instead posts a one-line H2 note — `## 💬 Triage — promoted`
(queued for brainstorming) or `## 💬 Triage — dropped` (with the reason, before closing).

**Brainstorm comment** (`/issue-brainstorm`, when it parks for answers) — questions asked
*in the issue*:

```markdown
## 💭 Brainstorm — round {N}

{1–3 focused questions; multiple-choice where possible}

<sub>answer inline — the issue is picked back up automatically (or re-run `/issue-brainstorm #{n}`)</sub>
```

**Spec comment** (`/issue-brainstorm`, when the spec is drafted) — summary + link, never
the full spec:

```markdown
## 📐 Spec — {date}

{3–6 bullet summary of the design and its shape}

**Full spec:** `docs/superpowers/specs/drafts/{YYYY-MM-DD-slug}.md` ({permalink})

<sub>spec slug `{slug}` · tracking issue #{n}</sub>
```

**Plan comment** (`/issue-plan`) — summary + link, never the full plan:

```markdown
## 📋 Plan — {date}

{3–6 bullet summary of what the plan does and its shape}

**Full plan:** `docs/superpowers/plans/drafts/{YYYY-MM-DD-slug}.md` ({permalink})

<sub>plan slug `{slug}` · tracking issue #{n}</sub>
```

**Review comment** (`/issue-spec-review` and `/issue-plan-review`) — one consolidated
comment per round. The two differ only by header/emoji (`🔬 Spec review` / `🔍 Plan
review`) and which artifact they cite:

```markdown
## {🔬 Spec review | 🔍 Plan review} — round {N} — {RETHINK | NEEDS WORK | LGTM}

{one-paragraph synthesis}

### {dimension} — {severity}
{the concern, located by section or code path}
> {3–10 line evidence snippet}
**Suggestion:** {what to change}

### Questions for author
- {open question}

<sub>reviewed {spec|plan} slug `{slug}` · round {N}/{K} · {criticals/highs/mediums}</sub>
```

**Implement comment** (`/issue-implement`) — closes the loop:

```markdown
## ✅ Implemented

{commit links} — merged to main.

<sub>plan `{slug}` archived to completed/</sub>
```

Verdict vocabulary is `standards.md`'s **Review Verdicts** — **RETHINK** (wrong approach /
boundary violation / wrong layering — fix the direction first), **NEEDS WORK** (sound
approach, critical/high findings to address), **LGTM** (no critical/high). Evidence (a
3–10 line snippet) is required on **every** critical/high finding, per `architecture.md`.

## § Artifact storage & promotion

Both artifacts follow the same rule: the **file is canonical**, the issue comment
**links** it, an un-approved artifact lives under `drafts/` and is **promoted** on human
approval.

- **Spec.** While brainstorming / spec-reviewing / discussing:
  `docs/superpowers/specs/drafts/{YYYY-MM-DD-slug}.md`. On **spec approval**
  (`/issue-respond` on a reviewed spec): `git mv` to top-level
  `docs/superpowers/specs/{YYYY-MM-DD-slug}.md`, and update the spec comment's link to the
  promoted path. Top-level `specs/` is, by construction, the home of *approved* designs.
- **Plan.** While planning / plan-reviewing / discussing:
  `docs/superpowers/plans/drafts/{YYYY-MM-DD-slug}.md` — a draft is never handed to
  `implement-plan` (which is given the exact top-level plan path, not a directory it
  scans), so no change to that tool is required. On **plan approval**: `git mv` to top-level
  `docs/superpowers/plans/{YYYY-MM-DD-slug}.md`, making it eligible — promotion *is*
  the ready signal. Update the plan comment's link.
- **On implementation:** `implement-plan` archives the plan to
  `docs/superpowers/plans/completed/` as it already does.

Every artifact records its tracking issue (and, for a plan, the spec it derives from) in
the header, added immediately **after** the standard brainstorming / writing-plans header,
so later steps can find and close the issue:

```markdown
> **Tracking issue:** #{n}
```

```markdown
> **Tracking issue:** #{n}
> **Spec:** docs/superpowers/specs/{YYYY-MM-DD-slug}.md
```

## § Working-tree hygiene (no stray edits on `main`)

The authoring skills and the autonomous driver commit **directly to the primary checkout
on `main`** (the driver runs there unattended). One broad `git add`, or one "let me just
edit a source file to try it," sweeps stray/trial changes onto `main`. Two **hard rules**
keep `main` clean — they bind **every** skill and the driver:

- **Commit only the one intended artifact, path-scoped.** The *only* thing a skill may
  write-and-commit in the primary checkout is the **single intended draft/promoted
  artifact** — the one spec or plan `.md` (a promote is its `git mv` rename; a comment-link
  edit is a GitHub comment, not a file). The commit step MUST stage **that exact path and
  nothing else**:
  - draft: `git add docs/superpowers/<specs|plans>/drafts/<the-file>.md` — the explicit
    path only.
  - promote / demote: stage via `git mv <old> <new>` and commit **only those two paths**
    (`git commit -- <new> <old> …`, i.e. the `git mv`-staged rename).
  - **Never** `git add -A`, `git add .`, `git add -u`, or `git commit -a` — these sweep
    untracked notes, editor files, and build artifacts onto `main` (exactly how
    `HANDOFF.md`/`ISSUES.md` once landed on `main` and had to be reverted, `197d23e`→`56662b5`).
- **Trials go in a throwaway worktree, never the primary checkout.** **Any**
  verification / POC / trial that mutates *tracked* files — a compile-check, a "does this
  build", a scratch edit — MUST happen in an **isolated, disposable git worktree**
  (`superpowers:using-git-worktrees`, or `git worktree add` under a temp dir), then be
  discarded. **Never** edit a tracked file in the primary checkout "just to try it," even
  intending to revert — a failed revert strands a trial edit on `main`. Spec/plan authoring
  is **read the code + write the one `.md`**, not editing source to test an idea.

**Assert clean.** Before committing, a skill MUST verify `git status --porcelain` shows
**nothing but** the one intended artifact path — if anything else appears, **stop and fail
loud** (report it) rather than sweep it. After the commit + push the working tree must be
**empty** (`git status --porcelain` prints nothing). The assertion is the guard; the
path-scoped `git add` is the mechanism.

## § Human-signal labels

Two distinct, orthogonal signals ride on top of the lifecycle:

- **`blocked` (state).** Set **only on a Doing column**, the moment in-flight work parks
  because it **needs your judgment or input** to proceed. A *queue* is never blocked — it
  is a pull buffer of work ready to be worked, not stuck; and the pre-work human decision
  ("is this worth doing?") lives in the **`Inbox`** backlog, not a block — shaped in place
via `/issue-triage` (§ Queues vs Doing). Concretely
  `blocked` marks: `brainstorming` having **posted questions** and awaiting your answers;
  a review (`spec-reviewing` / `plan-reviewing`) parked for your approval **only under the
  `manual` brake** (in the default flow an **LGTM auto-advances** — § Approval detection —
  so it never parks), or any review having **exhausted its round limit K** and stalled; or
  `implementing` having **failed** and needing diagnosis. It is **cleared** when you supply the input it
  waits on — by hand via the skill that consumes that input (`/issue-respond` for a review
  gate, a retried `/issue-implement`), or **automatically by the driver's reconcile**
  (§ Queues vs Doing, and reconcile) the moment you comment: reconcile detects the human
  input and routes each blocked state to the right handler — brainstorm answers →
  re-queued to `brainstorm-queued`; a review verdict → `/issue-respond`; a go-ahead on a
  failed implement → retried. A failed review that routes back to
  `brainstorm-queued`/`plan-queued` lands there **un-blocked** — it is simply queued for
  re-work you (or a `/loop`) pull.
- **`manual` (policy / the brake).** When present, **no skill advances the
  issue's `status:*` automatically** and **all auto-routes are disabled**: every review
  parks at `blocked` regardless of verdict, and the human steps the flow by hand.
  Default-**off**, opt-in. Its larger purpose is the future composing workflow: a braked
  issue forces the workflow to stop at every boundary; default-off lets the workflow
  auto-chain the human-free spans of un-braked issues.

The two are orthogonal and may co-exist.

## § Queues vs Doing, and reconcile (intake + requeue)

**The loop pulls only from queues.** Every stage is a Queue→Doing pair. A `*-queued`
label means *available to be picked up*; the matching `*-ing` (Doing) label means
*actively owned* — a skill is mid-run, or it's your turn and the work is `blocked` on your
input. A `/loop` (and any skill claiming work) **only ever pulls from `*-queued`** and
never auto-claims a Doing card. This is the concurrency contract: Doing = claimed, Queue =
free. (The same rule already governs failed reviews, which route back to `*-queued`
un-blocked for re-work.) So handing work back = moving it from its Doing state to a queue,
un-blocked.

**Inbox is the exception** — it is a human-paced backlog, **not** a Queue→Doing pair, so
no `/loop` and no driver ever pulls it *into the flow*. Its in-place dialogue skill is `/issue-triage`,
which asks/answers (§ Comment templates — Triage comment) while the idea stays
`status:inbox` and uses no `blocked` (Inbox has no Doing column). An idea leaves Inbox only
on a human decision the skill executes on a clear signal: **promote** → `brainstorm-queued`
(enter the flow) or **drop** → close.

**Reconcile — un-parking on human input.** A `blocked` issue is parked on *you*; the moment
you comment, that input must be picked up and routed. This is **reconcile**: a pull-based
pre-pass (no GitHub Action — see § The board for why) the driver runs at the start of every
drain pass, mirrored by the manual dialogue skills when re-run by hand. Reconcile first does
the **intake** sweep (§ Intake), then, for every open `blocked` Doing issue that is **not**
`manual` and carries **unaddressed human input** (a non-`##` comment after the
last skill comment — the H2 convention below), routes it to the handler that consumes that
kind of input:

| Blocked Doing state | The human comment is… | Reconcile routes it to |
|---|---|---|
| `brainstorming` | an answer (content) | **mechanical requeue** — clear `blocked`, `status:brainstorming → status:brainstorm-queued`; the next pull (`/loop` or `/issue-brainstorm`) reads the answers |
| `spec-reviewing` | an approve-or-feedback **verdict** | **`/issue-respond`** — approve → promote spec + `plan-queued`; feedback → answer + route back |
| `plan-reviewing` | an approve-or-feedback **verdict** | **`/issue-respond`** — approve → promote plan + `ready`; feedback → answer + route back |
| `implementing` | a diagnosis / "retry" | classify (§ Approval detection conservatism rule): an **unambiguous go-ahead** → clear `blocked`, return to `status:ready` for the implement phase to retry; **any caveat/question** → stay parked |

Only `brainstorming` is a mechanical relabel; the rest need judgment. The review rows defer
to `/issue-respond` because flipping a `*-reviewing` issue to a `*-queued` label would re-run
the review and **bury your verdict** (lose an approval, or re-litigate a rejection); the
implement row demands an unambiguous go-ahead before anything re-pushes to `main`. Two
invariants keep this reliable:

- **H2 = skill, non-H2 = human.** The skills post via `gh` as the same account that
  answers, so the author can't disambiguate. Instead: **every skill-emitted comment is an
  H2 (`## …`)** — any non-`##` comment is human input. Preserve this invariant when adding
  or editing any comment template (§ Comment templates).
- **Labels are still the truth; the board follows.** Reconcile's label writes are
  authoritative; the board card move is best-effort under your local `gh` token
  (§ The board) — if it lags, nothing breaks.

## § The autonomous driver

`/issue-drive` (the `issue-flow-drive` workflow) is the **composing workflow** this flow anticipates:
one background run that drains the board to a **fixpoint**. It reads labels (the source of truth), pulls
only from **queues** (§ Queues vs Doing), and stops only where a human is genuinely required. Full design:
`docs/superpowers/specs/2026-06-14-issue-flow-driver-design.md`.

**What it does, in one run:**
- **Reconcile pre-pass** (§ Queues vs Doing, and reconcile) — the intake sweep + the default-routed
  blocked-issue input router, under the local `gh` token (no GitHub Action).
- **Phase 1 — drain authoring + review stages to fixpoint.** For each collectable queue issue it runs the
  matching stage (brainstorm / spec-review / plan / plan-review), routing per verdict and `K`, and parks
  exactly where the manual flow parks.
- **Phase 2 — implement (default-ON).** Implements human-approved `status:ready` plans (scoped by the
  brake, one `implement-plan {plan:<path>}` per plan) and closes their issues.

**The hands-free trade-off (stated plainly).** LGTM auto-advances (§ Approval detection), so in the default
(non-`manual`) flow **there is no human approval gate at all** — not on the spec, not on the plan; the strict
review team's LGTM is the only gate. With `autoImplement` default-ON, an approved `status:ready` plan is then
implemented and the **TDD diffs `implement-plan` generates land on `main` unseen**. This is the operator's
deliberate choice. Two ways to reinsert a human: set **`manual`** on an issue (every review then parks
`blocked` for `/issue-respond`), or pass **`args.autoImplement: false`** for a Phase-1-only drive that stops
at the `ready` gate (the plans still auto-promote to `ready`; they just aren't implemented).

**Trust assumption.** This is a **private repository with a single trusted operator**; every issue comment
originates from that operator or from skills running under their account. The driver therefore implements
**no author allowlist and no injection-defended classifier** — approve / go-ahead decisions reuse
`/issue-respond` and the § The unaddressed-comment guard classification **as-is**. **Reinstate a trust
boundary before running the driver if *any non-operator-authored text can reach a driver decision
point*** (untrusted collaborators, a public repo, or an intra-repo data vector such as a future automation
that files issues from machine-generated content — commit messages, branch/PR titles).

**The brake is `manual`** (§ Human-signal labels): a `manual` issue is skipped entirely (collector, reaper,
and router all exclude it); the human steps it by hand. `/loop /issue-drive` is the continuous mode —
self-paced, ticks serialized.

## § Approval detection (the `/issue-respond` convention)

**In the default (non-`manual`) flow there is no human approval gate: an LGTM auto-advances.**
`issue-spec-review` promotes the spec and moves to `plan-queued`; `issue-plan-review` promotes the
plan and moves to `ready` — both with no `blocked` park. The strict review team's LGTM **is** the gate.

`/issue-respond` is the human-gate interpreter for a review parked **`blocked`** — which now happens
only under the **`manual`** brake (every review parks regardless of verdict), at a **round-limit stuck**,
or when a **mid-review comment** parked the issue (the commit-gate). It runs on such a `blocked` review
(`spec-reviewing` or `plan-reviewing`) and classifies the latest human comment(s) since the last skill
action:

- **Unambiguous approval** — e.g. **"looks good, go ahead"**, **"ship it"**, **"lgtm,
  proceed"** → **advance**: promote the artifact (§ Artifact storage & promotion) and move
  to the next stage (`spec-reviewing → plan-queued`, or `plan-reviewing → ready`),
  clearing `blocked`.
- **Questions / feedback / change requests / any caveat** → **stay in the loop**: answer
  the questions, route back to the appropriate queue (`spec → brainstorm-queued`;
  `plan → plan-queued`, or `brainstorm-queued` for a spec-level rethink), and clear
  `blocked` so the queued re-work can proceed.

**Conservatism rule:** the classifier is deliberately conservative — anything carrying a
question or a caveat (**"looks good *but*…"**) does **not** advance. Blast radius: a mis-read
spec approval only starts planning; a mis-read plan approval sets `status:ready`, which under
the driver's `autoImplement` then implements to `main` — so the plan gate is real (a `manual`
issue is excluded from Phase 2, so there nothing implements without `/issue-implement`).

Note: the brainstorm Q&A is **not** routed through `/issue-respond` — answering brainstorm
questions is content, not an approve/reject verdict, so you simply re-run
`/issue-brainstorm` and it reads your answers from the thread.

## § The unaddressed-comment guard

A human can comment at **any** stage, not only at the two scripted moments (answering a
brainstorm question; giving a review verdict). An *advancing* skill must never silently
step **past** a comment it hasn't addressed — running a review, drafting a plan, or
implementing over the top of an open question buries it. This guard generalizes
reconcile's spirit (§ Queues vs Doing) to every stage: **detect** human input
everywhere, but keep **answering** it in the dialogue handlers.

**Detect (mechanical — reuses the H2 convention).** Every skill-emitted comment is an H2
(`## …`); any comment that does **not** start with `##` is human input. The issue has
**unaddressed human input** when a human comment sits *after* the last skill (`##`)
comment.

**Classify the latest human comment** — reuse § Approval detection's conservatism rule:

- **Resolved** → an unambiguous go-ahead / ack: **"ok"**, **"lgtm"**, **"go ahead"**,
  **"looks good"**, **"ship it"**, a bare 👍 / positive emoji (comment *or* reaction), with
  **no** question or caveat. The discussion is done → **proceed with this skill's normal
  step**. (This is *not* an artifact approval — it neither promotes nor skips a stage; it
  only says "stop waiting on me and carry on.")
- **Needs you** → anything else: a question, concern, change request, or any caveat
  ("looks good *but*…"). Same conservatism as the gate classifier — when in doubt, treat
  as *Needs you*, never Resolved.

**On *Needs you* the guard fires.** The skill runs **no** work and advances **no**
`status:*`. It claims its Doing state, parks `blocked` (§ Setting state), posts one H2
note, and stops:

```markdown
## 🛑 Needs you — unaddressed comment

@{author}: "{one-line gist}". Did nothing yet — handle it (answer inline, run the right
dialogue skill, or just reply "go ahead"). Your reply is picked up automatically, or clear
`blocked` to re-queue by hand.
```

The human resolves it — answer it, run `/issue-brainstorm` (spec-stage content) or
`/issue-respond` (a review gate), or simply reply with a go-ahead. Their reply is then
picked up the same way any `blocked` Doing state is — by **reconcile** (§ Queues vs Doing,
and reconcile), which routes it per state (brainstorm content re-queued, a review verdict to
`/issue-respond`, an implement go-ahead to a retry), or by a human stepping it by hand.
Blast radius is low: the guard only ever *delays* a step, it never corrupts state.

**The dialogue handlers are exempt.** `issue-brainstorm`, `issue-respond`, and
`issue-triage` *consume* human input rather than guard against it, so the guard does not
apply to them. `issue-brainstorm` must still explicitly address any unanswered human
question before it drafts (a go-ahead/ack resolves it and it may draft).

**Interaction with the brake.** When `manual` is set the issue already parks
`blocked` at every boundary, so the guard is subsumed.

### The commit-gate (exit-side, for the autonomous driver)

The guard above is the **entry** check (a skill must not *start* over an open comment). The autonomous
driver adds the symmetric **exit** check — a comment landing *while a stage runs* must not be **stepped
past**. Before any stage advances, promotes, or closes, the driver:

1. **captures a watermark** at claim (review stages) or at the run-start `ready` snapshot (Phase 2) —
   `max(createdAt)` over the issue's comments, tie-broken by comment url (the **watermark** snippet in
   § Setting state);
2. **re-checks at exit** for non-`##` (human) comments strictly after the watermark (the **comments_after**
   snippet in § Setting state);
3. **classifies** the latest with the same conservatism rule (§ Approval detection — go-ahead/no-caveat =
   *Resolved*; any question/caveat = *Needs you*); and
4. **acts:** *Resolved* (or no mid-run comment) → advance, leaving a durable `##` breadcrumb only when a real
   post-watermark comment was classified; *Needs you* → do **not** advance — dialogue stages re-queue, other
   stages park `blocked` + `## 🛑 Needs you — unaddressed comment`.

**Irreversible-push caveat.** `implement-plan` pushes each task inside one opaque batch, so a "stop" landing
**mid-batch** is best-effort only; the driver gates **before** each plan's call and **before** its close,
bounding the blast radius to one plan. **Reconcile is exempt** (it consumes parked input).

## § Sub-issues & work graph

Labels carry *state*; GitHub's native relationship primitives carry *structure*. We use
them to build as rich a work graph as the platform allows. The exact `gh`/GraphQL commands
below should have their **API support verified at use time**:

- **Sub-issues** — native parent→child hierarchy with completion rollup on the parent. The
  primary structural tool. Use `gh sub-issue` (the extension) or the GraphQL `addSubIssue`
  mutation.
- **Closing keywords** (`Closes #n` / `Fixes #n`) and **commit/PR cross-references** — link
  implementation back to the issue and populate the "Development" timeline.
- **Cross-references** (`#n` mentions) — relate findings, specs, plans, and follow-ups in
  bodies/comments.
- **Issue dependencies** ("blocked by" / "blocking"), now natively supported — to express
  ordering between sibling units (one `ready` plan waits on another). `/issue-split` records
  these between the children it creates wherever the units depend on each other. This is
  *structural* (implementation order) and distinct from the `blocked` *modifier label*
  (in-flight work waiting on you).

**The unit rule:** one issue ↔ one spec ↔ one plan ↔ one cohesive change. Sub-issues
represent **separately-plannable units**, *not* the TDD tasks inside a plan (those stay as
checkboxes in the plan file) — keeping the issue↔spec↔plan mapping 1:1 and avoiding issue
explosion. Two decomposition directions, both valid:

- **Top-down** — an issue too big for one spec (surfaced at any stage — triage, brainstorm,
  or plan's scope check) is decomposed by **`/issue-split <n>`**: it **spawns a fresh epic +
  one child per plannable unit** (each child entering the flow at `brainstorm-queued` with
  the distilled context), then **closes the original** as a linked archive. The epic is a
  tracking issue (no `status:*`, no spec/plan file of its own) that closes when its children
  roll up to done. We spawn-fresh rather than convert in place so the epic stays a clean
  shell and the original keeps its full discussion thread.
- **Bottom-up** — several existing findings addressed by one change are grouped as
  **sub-issues of the tracking issue** (or simply cross-referenced), so the rollup shows
  progress as each is implemented.

**Implementation linkage:** because this repo pushes straight to main (no PR),
`/issue-implement` references the merged commit SHAs in its close comment (auto-linking)
and closes via `gh issue close`; closing keywords become the native mechanism if a
PR-based flow is ever adopted. Either way the timeline link is populated. Pinned commands
(verify support at use time): `gh sub-issue` / GraphQL `addSubIssue`, `gh issue close`,
`Closes #n`.

## § The round limit K

The per-loop round limit **K** defaults to **5** (configurable here). It defines when a
failed-review loop stops auto-routing and parks `blocked` for the human. **Round N** = the
count of prior same-type review comments on the issue + 1. The only cycles in the flow are
brainstorm↔spec-review, plan↔plan-review, and the plan→brainstorm escape; each carries this
round counter, and on the **Kth** consecutive failure the loop parks at its Doing column
with `blocked` and a `## 🛑 Stuck — needs you (after K rounds)` comment instead of routing
again (an H2, per the skill-comment convention in § Queues vs Doing).

## § Reporting a broken skill (self-healing)

Two failures differ in kind. The **work** failing — tests won't pass, a review can't reach
a verdict — is normal: the work issue parks `blocked` on its Doing state with the error in a
comment, and a human (or a retry) resumes it. The **machinery** failing — a `gh`/git call
breaks, a workflow throws, a step references something that no longer exists — is a *defect
in the flow itself* and needs its own bug story so we fix the skill.

Every issue-* skill and the issue workflows follow this on an unexpected tooling/skill error
(*not* a normal work-failure). It is **best-effort** and **searched-first**, so one breakage
never spawns twenty duplicate stories:

1. **Fingerprint** the failure: the skill/workflow name + a **normalized** error signature —
   the failing command and error class with issue-specific numbers, paths, and ids stripped —
   so the *same* breakage always fingerprints the same.
2. **Search first:** `gh issue list --label issue-flow-bug --state open --json number,title,body`
   and judge whether any open bug is the **same** breakage (same skill + same signature).
3. **Match → recur, don't duplicate.** Append a `## 🔁 Recurred — {date}` comment with the
   new occurrence (which work-issue it hit, the sanitized error) and bump an occurrence count
   in the body. No new issue.
4. **No match → file one:** `gh issue create` titled `issue-flow bug: {skill} — {signature}`,
   labelled `issue-flow-bug` + a severity, body = skill, failing step/command, **sanitized**
   error output, the work-issue cross-ref, and the date. It is a normal new issue, so
   intake (§ Intake) stamps it `status:inbox` — it enters the flow and gets
   fixed like any work.
5. **Sanitize, always.** Never put secrets (`NUGET_API_KEY`, `GITHUB_TOKEN`, any
   `*_TOKEN` / `*_SECRET` / `*_KEY` or bearer token) or raw payloads in the bug title or body.
6. **No recursion.** If reporting *itself* fails, print the error for the operator and stop —
   never try to report the report.

`/issue-report-breakage <skill> "<what broke>"` is the manual entry point to the same
procedure, for when you spot a broken skill yourself.

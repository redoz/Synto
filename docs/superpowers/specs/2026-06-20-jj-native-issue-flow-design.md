# jj-native issue-flow — design

> **Status:** design (brainstormed 2026-06-20). Standalone tooling design — no tracking issue
> (this is meta-work on the `.claude/` issue-flow itself, not a Synto product feature).

## Context

The `.claude/` issue-flow tooling was ported from the git-based OuroCore upstream and is **git-native
throughout**: it uses git worktrees for per-task isolation, `git rebase origin/main` +
`git push origin HEAD:main` for continuous integration, `git symbolic-ref` for branch detection, and a
"path-scoped `git add`, never `-A`" working-tree hygiene. This repo is driven with **jj (Jujutsu)** over
git — the working copy is typically a detached git HEAD, branches are jj **bookmarks**, and jj
auto-snapshots the working copy. Several git idioms therefore fight jj (notably `git worktree`, the
ff-push CI model, and `git add`/`git commit` in the jj working copy), and the hardcoded `main` target is
wrong for an experimental-branch workflow.

This design rewrites the **git-mechanics layer** of the issue-flow to be **jj-native**. Everything
gh-based — labels, the H2 comment convention, comment templates, `trust.sh`, the project board, review
verdicts — has no git in it and is **unchanged**.

## Goals

- Run the issue-flow on **any branch** (the bookmark you are working on), not a hardcoded `main`.
- Use **jj idioms** for all repo mutation (workspaces, `jj commit`/`describe`/`squash`, `jj rebase`,
  `jj bookmark set`, `jj git push`).
- **Build locally, do not auto-push** for now — but keep integration as a *mode* so full CI (auto-push)
  can switch on later without a rewrite.
- Preserve the **concurrent rolling worker pool** (burn-the-board implementing several issues at once).

## Non-goals

- No change to gh/label/comment/board/trust semantics, to `evaluate.js`, or to `issue-review.js`
  (read-only).
- No enabling of auto-push CI now (the `push` mode is designed but defaults off).
- Re-syncability with the git-based OuroCore upstream is **explicitly deprioritized** — going jj-native
  diverges the files structurally, so future upstream pulls become manual. Accepted trade-off.

## Decisions

1. **jj-native (full)** — rewrite the git-mechanics into jj idioms, not a thin compatibility shim.
2. **Integration = local build now, CI-push later**, behind a mode flag.
3. **Isolation = one `jj workspace` per implement run** — preserves the concurrent pool, scales to CI.
4. **Base resolution = jj-aware, bookmark-first** — explicit `$SYNTO_FLOW_BASE` override → nearest jj
   bookmark (computed from `@`) → git branch (plain-git fallback) → fail-closed.

## Components

### A. Base-branch resolver — `.claude/scripts/base-branch.{sh,ps1}` (new, shared)

Single source of truth for the integration branch `B` (mirrors the `trust.{sh,ps1}` shared-primitive
pattern). Resolution order, fail-closed:

1. `$SYNTO_FLOW_BASE` if set → use it (explicit override).
2. else the nearest jj bookmark among ancestors of `@`:
   `jj log --no-graph -r 'latest(::@ & bookmarks())' -T 'bookmarks.map(|b| b.name()).join(",")'`
   (verified to yield `experimental/matching` on the current stack). **This is the primary auto-detect:**
   the bookmark is computed from `@` — where you actually are — so it is the truest reflection of the
   branch you are working on in jj terms (git's `HEAD` is detached under jj and does not track it).
3. else the current git branch: `git symbolic-ref --short -q HEAD` — the **plain-git fallback** for a
   non-jj checkout (where step 2 finds no bookmark, or `jj` is absent).
4. else **refuse** (print guidance to set `$SYNTO_FLOW_BASE`; non-zero exit).

**Contract:** every site that previously referenced `main`/`origin/main` resolves `B` through this
script. Behaviour, not exact text, is pinned.

### B. Working-copy commits — authoring & promotion (jj)

The brainstorm/plan **draft** writes and the spec/plan **promotions** mutate the primary jj working copy.
They commit via **`jj commit <fileset>`** (scope to the one artifact path) / file-move + `jj commit <new>
<old>`, then **advance the integration bookmark locally** — `jj bookmark set <B> -r @-` (**no push** in
local mode) — so `B` stays the always-current trunk and the committed artifact is reachable at `B`. This
replaces `git add <path>` + `git commit` + ff-push: in the git model every commit ff-pushed `origin/main`
forward; the faithful jj-local translation is **commit-then-advance-`B`** (push only under
`SYNTO_FLOW_INTEGRATE=push`). It is the *same* integration primitive `implement-plan` uses (§ C) — the
single canonical "land a commit on `B`" snippet lives in `github.md` § Setting state. The "never `git add
-A`" hygiene is superseded by jj's native model: jj snapshots everything, you **scope at commit time**
with a fileset (never a bare `jj commit`), and when the working copy holds unrelated edits you **`jj
split`** the artifact into its own commit (the exact discipline used to keep these changes off the
operator's `README` edit).

**Visibility contract (load-bearing):** because `ready→implement` plan-resolution (§ walk-issue) and
`implement-plan`'s workspace both read the promoted plan from `B`, a promotion that did *not* advance `B`
would be invisible to implement. Advancing `B` on every artifact commit is what closes that gap. (The
operator's `@` tracking `B` after a *separate* implement workspace advances it is a cross-workspace
concern validated in the dry-run, not assumed.)

### C. Implement — `implement-plan.js` (jj workspaces)

Per plan:

- Resolve `B`. Create an isolated **`jj workspace add <dir>`** whose working-copy commit starts at `B`'s
  tip (read the promoted plan from `B`, not a stale on-disk tree).
- Per task: **RED** (failing test) → **GREEN** (`dotnet build --no-restore` / `dotnet test --no-build` /
  `dotnet format --verify-no-changes`) → **`jj commit -m "<task>"`**. Build the task commit stack in the
  workspace.
- **Integrate (local mode):** `jj rebase` the task stack onto the latest `B`, re-run the green-gate, then
  **`jj bookmark set B -r <new-tip>`**. No push. The operator's main working copy follows the advanced
  bookmark via jj's auto-rebase.
- **`push` mode (future, off by default):** additionally `jj git push --bookmark B`.
- **Concurrency:** one workspace per pool slot; integrating onto the shared bookmark uses jj rebase
  (conflicts are first-class) in place of the git ff-push-retry loop. Contended bookmark advance retries
  on a fresh rebase.
- **Cleanup:** `jj workspace forget <name>` + remove the dir (even on failure).
- Archive the plan to `completed/` as a jj commit on `B`.

### D. Precondition probe — `probe.{sh,ps1}` (jj-native)

Drop "must be on `main`" and the `git merge --ff-only origin/main` mutation. In **local** mode the probe
runs these guardrails:

- **`B` resolves** via the resolver (§ A) — *fail-closed* on an unresolved base.
- **the resolved bookmark exists** as a jj bookmark (`jj bookmark list` contains `B`) — *fail-closed*,
  catching a typo'd `$SYNTO_FLOW_BASE` or a since-deleted branch *before* any commit lands on a phantom
  target.
- **the working copy is conflict-free** (`jj status` shows no conflicts) — *fail-closed*.
- **no unrelated working-copy edits before an implement run** — if `@` holds tracked changes outside the
  artifact/plan path, *warn* (non-fatal), so a stray edit (e.g. the operator's `README`) is surfaced
  before it could be swept into an implement commit rather than silently riding along.

The `--implement/--no-implement` flag is kept for caller compatibility. The future `push` mode adds an
"ahead-of / ff vs `origin/B`" check on top of these.

### E. `github.md` + authoring/promotion skills (jj rewrite)

Rewrite the git-mechanics prose to jj: § Working-tree hygiene (jj snapshot + fileset-scoped commit +
`jj split`, replacing the git-add rules), the implement/CI model (workspaces, bookmark advance,
local-vs-push mode), the canonical integration snippet (replacing `ff_push`'s `git push origin HEAD:main`),
artifact promotion (jj file-move + commit), and the base-resolution + config docs. The issue-* skills'
**commit steps** (`issue-brainstorm`, `issue-plan`, and the promotions in `issue-spec-review`,
`issue-plan-review`, `issue-respond`) switch from git to jj.

### F. Config

- `SYNTO_FLOW_BASE` — explicit integration branch override (else auto-resolved, § A).
- `SYNTO_FLOW_INTEGRATE` — `local` (default) | `push`. `local` advances the bookmark only; `push` also
  `jj git push`. The single switch that turns on full CI later.

Both documented in `github.md`.

### G. Unchanged surface

`set-status.{sh,ps1}`, `trust.{sh,ps1}`, `evaluate.js`, `issue-review.js`, and all label / comment / H2 /
board / trust / verdict / round-limit semantics. The issue-flow's *behavior* (stages, guards, routing)
is identical; only the repo-mutation primitives change.

## Error handling

- Resolver and probe **fail closed** (refuse on ambiguity / unresolved conflict rather than guessing a
  target).
- Workspace cleanup runs on every exit path (success, green-gate failure, agent error) so abandoned
  workspaces don't accumulate.
- A contended bookmark advance retries on a fresh `jj rebase`; persistent conflict parks the issue
  `blocked` with the error (the existing failure-surfacing path), never a forced/destructive write.
- No `jj git push` in local mode under any branch — the operator-push invariant holds mechanically.

## Testing / verification

- `base-branch.sh` exercised on: a normal on-branch checkout, the detached jj stack (resolves the
  bookmark), and `$SYNTO_FLOW_BASE` override — mirroring how `trust.sh` was proven before wiring.
- `node --check` on the rewritten workflows; `bash -n` on the scripts.
- A dry-run implement against a throwaway bookmark in a scratch workspace (never the operator's stack),
  asserting: commits land on the bookmark locally, **no** `origin` ref moves in local mode, and the
  workspace is cleaned up.
- Grep guard: zero `origin/main` / `git push .* HEAD:` / `git worktree` left in the jj-native files.

## Staged implementation

1. **Foundation:** `base-branch.{sh,ps1}` + jj-native probe; thread the resolver everywhere `main` was
   hardcoded.
2. **Local-commit model:** authoring/promotion commit steps → jj (working-copy, scoped, no push);
   rewrite `github.md` § Working-tree hygiene + artifact promotion.
3. **Workspace-based implement:** `implement-plan.js` → jj workspaces + bookmark-advance integration +
   the `SYNTO_FLOW_INTEGRATE` mode; `walk-issue.js` per-stage sync + plan resolution → jj.
4. **Docs/skills sweep:** finish the `github.md` CI-model rewrite + the issue-* skill commit steps;
   verification grep.

Each stage is independently verifiable and committable. The whole jj-native change folds into the
re-port commit (`4acad516`) per the agreed commit plan.

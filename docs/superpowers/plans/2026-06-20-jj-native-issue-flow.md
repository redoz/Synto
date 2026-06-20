# jj-native issue-flow — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **Spec:** docs/superpowers/specs/2026-06-20-jj-native-issue-flow-design.md
> **No tracking issue** — this is meta-work on the `.claude/` issue-flow itself, driven through the superpowers spec→plan→implement pipeline, not the issue-flow.

**Goal:** Rewrite the git-mechanics layer of the `.claude/` issue-flow to be jj-native — runs on any bookmark (not hardcoded `main`), builds locally with no auto-push (behind a mode flag for future CI), and preserves the concurrent rolling worker pool.

**Architecture:** A new shared `base-branch.{sh,ps1}` resolver becomes the single source of truth for the integration branch `B`. Every site that hardcoded `main`/`origin/main` resolves `B` through it. Per-run isolation moves from `git worktree` to `jj workspace`; continuous integration moves from `git rebase origin/main` + `git push origin HEAD:main` to `jj rebase` onto `B` + `jj bookmark set B` (no push in `local` mode). Authoring/promotion commits move from path-scoped `git add` + `git commit` + push to fileset-scoped `jj commit` / file-move + `jj describe`, no push. All gh/label/comment/board/trust/verdict semantics are untouched.

**Tech Stack:** Bash + PowerShell shared-primitive scripts; Node workflow scripts (`implement-plan.js`, `walk-issue.js`) whose git/jj mechanics live in agent prompt strings; Markdown rules/skills; jj (Jujutsu) over a colocated git repo; `gh` CLI (embedded jq).

## Plan altitude (read before implementing)

Per the operator's standing preference, this plan **pins contracts, interfaces, and behaviors — not exact prose/code**. Each task gives the exact files, the load-bearing specifics that are contracts (env-var names, the resolution order, jj command *shapes*, the JSON/exit contract, the grep-guard regex), and concrete verification. Where a task rewrites natural-language prompt strings or rules prose, reproduce the *behavior* faithfully; you do not need to match wording verbatim. Snippets shown are **contract anchors**, not literal text to paste.

## Global Constraints

Every task's requirements implicitly include this section. Values are copied verbatim from the spec.

- **jj-native, full.** After this change, **zero** `origin/main`, `git push … HEAD:`, `git worktree`, `git symbolic-ref`, or `git merge --ff-only` remain in the rewritten files (`base-branch.*`, `probe.*`, `implement-plan.js`, `walk-issue.js`, the 6 skills, `github.md`, `rigor.md`). This is the final grep guard (Task 8).
- **Build local, no auto-push now.** `SYNTO_FLOW_INTEGRATE` = `local` (default) | `push`. `local` advances the bookmark only; `push` *additionally* runs `jj git push`. The single switch that turns on CI later; default off.
- **Base resolution, bookmark-first, fail-closed.** Order: `$SYNTO_FLOW_BASE` override → nearest jj bookmark (computed from `@`) → current git branch (plain-git fallback) → **refuse** (non-zero exit + guidance).
- **Preserve the concurrent rolling worker pool** — burn-the-board implements several issues at once; isolation is one `jj workspace` per pool slot.
- **Unchanged surface (do NOT touch):** `set-status.{sh,ps1}`, `trust.{sh,ps1}`, `evaluate.js`, `issue-review.js`, and all label / comment / H2 / board / trust / verdict / round-limit semantics. The issue-flow's *behavior* (stages, guards, routing) is identical; only the repo-mutation primitives change.
- **`.sh` and `.ps1` twins kept at parity** — semantically equivalent, idiomatic to each shell (the trust/probe/set-status precedent). No code generator; hand-maintained in lockstep.
- **Platform:** Windows; jj over a colocated git repo; HEAD is typically **detached**. The operator's uncommitted `README.md` edit lives in `@` and must **never** be swept into any commit — every commit is fileset-scoped.
- **This plan's OWN implementation is special:** local-only, path-scoped jj commits into the `.claude/` stack, **folded into `4acad516`** per the agreed commit plan, **never pushed**, and **NOT** executed through the (auto-pushing, git-native) `implement-plan.js` workflow. See § Execution Handoff.

---

## File Structure

| File | Responsibility | Action |
|---|---|---|
| `.claude/scripts/base-branch.sh` | Resolve integration branch `B`, fail-closed (bash) | **Create** |
| `.claude/scripts/base-branch.ps1` | Same, PowerShell twin | **Create** |
| `.claude/scripts/probe.sh` | jj-native precondition probe (bash) | **Rewrite** |
| `.claude/scripts/probe.ps1` | jj-native precondition probe (PowerShell) | **Rewrite** |
| `.claude/rules/github.md` | § Working-tree hygiene, § Artifact storage & promotion, § Setting state (`ff_push`→jj), § Config | **Rewrite (sections)** |
| `.claude/rules/rigor.md` | Skip-plan promotion mechanic (`git mv`→jj) at line ~185 | **Edit (one line)** |
| `.claude/skills/issue-brainstorm/SKILL.md` | Spec-draft commit step | **Edit** |
| `.claude/skills/issue-plan/SKILL.md` | Plan-draft commit step | **Edit** |
| `.claude/skills/issue-spec-review/SKILL.md` | Spec promotion + skip-plan thin-plan commit | **Edit** |
| `.claude/skills/issue-plan-review/SKILL.md` | Plan promotion commit | **Edit** |
| `.claude/skills/issue-respond/SKILL.md` | Spec/plan promotion-on-approval commits | **Edit** |
| `.claude/skills/idea/SKILL.md` | Spec-draft commit step | **Edit** |
| `.claude/workflows/implement-plan.js` | jj workspaces + bookmark-advance integration + `SYNTO_FLOW_INTEGRATE` | **Rewrite (git-native prompt strings)** |
| `.claude/workflows/walk-issue.js` | Probe arg, authoring isolation, promotion, plan-resolution from `B`, workspace sweep | **Rewrite (git-native prompt strings)** |

---

## Task 1: Base-branch resolver — `base-branch.{sh,ps1}`

**Files:**
- Create: `.claude/scripts/base-branch.sh`
- Create: `.claude/scripts/base-branch.ps1`

**Interfaces:**
- Produces (consumed by Tasks 2, 5, 6 and the github.md docs): a CLI primitive that **prints the resolved integration branch name `B` to stdout and exits 0**, or **prints guidance to stderr and exits non-zero** when `B` cannot be resolved. Invocation: `bash .claude/scripts/base-branch.sh` / `pwsh .claude/scripts/base-branch.ps1` (no subcommand — single purpose, like a function). No stdout noise other than the branch name (all logs to stderr), so callers capture it verbatim: `B=$(bash .claude/scripts/base-branch.sh)`.

**Contract (the resolution order — load-bearing, fail-closed):**
1. If `$SYNTO_FLOW_BASE` is set and non-empty → print it, exit 0.
2. Else the **nearest jj bookmark among ancestors of `@`** (primary auto-detect):
   `jj log --no-graph -r 'latest(::@ & bookmarks())' -T 'bookmarks.map(|b| b.name()).join(",")'`
   — if this yields a non-empty result, take the **first** name (comma-split; deterministic tie-break when a commit carries several bookmarks), print it, exit 0. If `jj` is absent/errors or the result is empty, fall through (do not fail yet).
3. Else the current git branch: `git symbolic-ref --short -q HEAD` (plain-git fallback). If non-empty, print it, exit 0.
4. Else **refuse**: print to stderr a message instructing the operator to `export SYNTO_FLOW_BASE=<branch>`; exit non-zero.

**Pattern to mirror** (from `trust.sh`/`set-status.sh`): `#!/usr/bin/env bash` + `set -euo pipefail`; fail-closed (any tooling error aborts non-zero); use `gh`'s embedded jq only where gh is involved (not needed here); the `.ps1` twin uses `$ErrorActionPreference`, `[Console]::Error.WriteLine` for stderr logs, `[Console]::Out.WriteLine` for the single stdout line, and `$LASTEXITCODE` guards after each native git/jj call.

- [ ] **Step 1: Write `base-branch.sh`** implementing the 4-step order above. Log each resolution decision to **stderr** (`base-branch: resolved B=<name> via <override|bookmark|git-branch>`), print only `<name>` to stdout. On refuse, stderr guidance + `exit 1`.

- [ ] **Step 2: Write `base-branch.ps1`** as the semantic twin (PowerShell idioms; `-NoLogo`-style: stderr logs, single stdout line, exit code mirrors success/refuse).

- [ ] **Step 3: Verify syntax.** Run `bash -n .claude/scripts/base-branch.sh`. Expected: no output, exit 0. (PowerShell parse check optional: `pwsh -NoProfile -Command "[void][System.Management.Automation.PSParser]::Tokenize((Get-Content -Raw .claude/scripts/base-branch.ps1),[ref]$null)"`.)

- [ ] **Step 4: Verify the three resolution scenarios** (mirrors how `trust.sh` was proven before wiring):
  - **jj bookmark (current detached stack):** `bash .claude/scripts/base-branch.sh` → prints `experimental/matching`, exit 0.
  - **override:** `SYNTO_FLOW_BASE=foo bash .claude/scripts/base-branch.sh` → prints `foo`, exit 0.
  - **refuse:** simulate no override + no bookmark + no git branch (e.g. `SYNTO_FLOW_BASE= ` in a context with neither) → non-zero exit + stderr guidance. (If hard to simulate locally, assert the refuse branch by reading the code path; the dry-run in Task 8 covers the live path.)
  Run each and confirm the printed value + exit code.

- [ ] **Step 5: Commit** (jj, fileset-scoped — see § Execution Handoff for the fold mechanics):
  `jj commit .claude/scripts/base-branch.sh .claude/scripts/base-branch.ps1 -m "feat(scripts): jj-aware base-branch resolver (bookmark-first, fail-closed)"`

---

## Task 2: jj-native precondition probe — `probe.{sh,ps1}`

**Files:**
- Modify: `.claude/scripts/probe.sh`
- Modify: `.claude/scripts/probe.ps1`

**Interfaces:**
- Consumes: `base-branch.{sh,ps1}` (Task 1) to resolve `B`.
- Produces (consumed by `walk-issue.js` Task 6, unchanged call site): the existing contract — exactly **one compact JSON line to stdout** `{"ok":true|false,"reason":"…"}`, all progress to stderr, **exit code mirrors `ok`** (0/1). The `--implement` / `--no-implement` (sh) and `-NoImplement` (ps1) flag is **kept** for caller compatibility (still a no-op gate).

**Contract — replace the git-native check with jj-native local-mode guardrails (all fail-closed unless noted):**
- **Drop** the "must be on `main`" precondition (`git symbolic-ref` == main), the `git fetch origin`, the `git rev-parse origin/main`, the `merge-base` ancestry checks, and the `git merge --ff-only origin/main` mutation. (None remain — grep-guarded in Task 8.)
- **Guardrail 1 — `B` resolves:** `B=$(bash .claude/scripts/base-branch.sh)` succeeds (exit 0, non-empty). On failure → `ok=false`, reason carries the resolver's guidance.
- **Guardrail 2 — bookmark exists:** the resolved `B` appears in `jj bookmark list` (catch a typo'd `$SYNTO_FLOW_BASE` or a deleted branch *before* any commit lands on a phantom target). On miss → `ok=false`.
- **Guardrail 3 — working copy conflict-free:** `jj status` shows no conflicts. On conflict → `ok=false` with remediation.
- **Guardrail 4 — no unrelated working-copy edits before an implement run (WARN, non-fatal):** if `@` holds tracked changes (jj status non-empty), emit a stderr **warning** that a stray edit (e.g. the operator's `README`) must not be swept — but do **not** set `ok=false` (commits are fileset-scoped downstream, so this is advisory). Keep `ok=true` if guardrails 1–3 pass.
- Keep the `--implement/--no-implement` flag parsing and the "no infra to check" framing; both modes run the same jj check.

- [ ] **Step 1: Rewrite `probe.sh`** — replace the git block (current lines ~62–91) with guardrails 1–4. Preserve `emit`/`log` helpers and the JSON/exit contract verbatim.

- [ ] **Step 2: Rewrite `probe.ps1`** as the semantic twin (mirror the same four guardrails; preserve `Emit`/`LogP` and the JSON/exit contract).

- [ ] **Step 3: Verify syntax.** `bash -n .claude/scripts/probe.sh`. Expected: exit 0.

- [ ] **Step 4: Run it on the current stack.** `bash .claude/scripts/probe.sh --implement` → expect a single stdout line `{"ok":true,"reason":"…"}` (B resolves to `experimental/matching`, bookmark exists, no conflicts), exit 0. Because the operator's `README` edit is in `@`, confirm guardrail 4 prints a **warning to stderr** but `ok` stays `true`. Run `bash .claude/scripts/probe.sh --no-implement` and confirm identical check.

- [ ] **Step 5: Commit:**
  `jj commit .claude/scripts/probe.sh .claude/scripts/probe.ps1 -m "feat(scripts): jj-native probe (resolver + bookmark-exists + conflict-free + stray-edit warn)"`

---

## Task 3: `github.md` — § Working-tree hygiene + § Artifact storage & promotion → jj

**Files:**
- Modify: `.claude/rules/github.md` (§ Working-tree hygiene, lines ~349–381; § Artifact storage & promotion, lines ~318–345)

**Interfaces:**
- Produces (referenced by Tasks 4, 7 and every authoring skill): the canonical jj working-tree-hygiene + promotion prose the skills point at by section name.

**Contract — translate the git discipline to its jj equivalent, preserving the *intent* (clean stack, never sweep stray edits):**
- **§ Working-tree hygiene:** replace the "path-scoped `git add`, never `git add -A`/`.`/`-u`/`commit -a`" rules with the jj model: **jj auto-snapshots the whole working copy; you scope at *commit* time with a fileset** — `jj commit <exact-path>` (or `jj describe`/`jj squash` with a fileset), **never** a bare `jj commit`/`jj describe @` with no fileset (that sweeps every working-copy change, e.g. the operator's `README`, into the commit). When the working copy holds unrelated edits, **`jj split`** the artifact into its own commit (the exact discipline that kept this session's commits off the operator's `README`). Replace "trials go in a throwaway `git worktree`" with "trials go in a throwaway **`jj workspace`** (`jj workspace add` under a temp dir), then `jj workspace forget` it." Keep the **Assert clean** rule, re-expressed: before committing, verify `jj status` shows nothing but the intended artifact path(s) in the fileset; fail loud otherwise. (The operator's pre-existing `README` change is the standing example of "must never be swept.")
- **§ Artifact storage & promotion:** replace `git mv <draft> <top-level>` + `git commit -- <paths>` with the jj file-move + fileset-scoped commit (`jj` rename = move the file on disk, then `jj commit <new> <old> -m …`; jj records the rename automatically). No push in `local` mode. Keep the drafts/→top-level promotion rule and the "file is canonical, comment links it" model verbatim — only the mechanic changes.

- [ ] **Step 1: Rewrite § Working-tree hygiene** to the jj model above (snapshot + fileset-scoped commit + `jj split` + jj-workspace trials + Assert-clean via `jj status`). Remove the `197d23e`→`56662b5` git-add anecdote or re-cast it as the jj "never bare-commit" lesson.

- [ ] **Step 2: Rewrite § Artifact storage & promotion** mechanics (jj file-move + fileset commit, no push), keeping the drafts/promote semantics.

- [ ] **Step 3: Verify** no git-native mechanics remain in these two sections: `grep -nE "git add|git commit|git mv|git worktree" .claude/rules/github.md` should show **no hits inside these two sections** (the § Setting state `ff_push` block is Task 7). Read the two sections back to confirm coherence.

- [ ] **Step 4: Commit:**
  `jj commit .claude/rules/github.md -m "docs(github): jj-native working-tree hygiene + artifact promotion"`

---

## Task 4: The 6 skill commit steps + `rigor.md` → jj

**Files:**
- Modify: `.claude/skills/issue-brainstorm/SKILL.md` (line ~27)
- Modify: `.claude/skills/issue-plan/SKILL.md` (line ~28)
- Modify: `.claude/skills/issue-spec-review/SKILL.md` (lines ~25, ~34)
- Modify: `.claude/skills/issue-plan-review/SKILL.md` (line ~25)
- Modify: `.claude/skills/issue-respond/SKILL.md` (lines ~20–21)
- Modify: `.claude/skills/idea/SKILL.md` (line ~114)
- Modify: `.claude/rules/rigor.md` (line ~185)

**Interfaces:**
- Consumes: the jj hygiene/promotion prose from Task 3 (these steps reference `github.md § Working-tree hygiene` by name — keep the reference).

**Contract — per-site translation (preserve the commit *message* text and the fileset-scoping intent; drop `; git push`):**
| Site | Current (git) | jj contract |
|---|---|---|
| issue-brainstorm | `git add <draft>; git commit -m "docs(spec): draft for #<n>"; git push` | `jj commit docs/superpowers/specs/drafts/{file} -m "docs(spec): draft for #<n>"` (fileset-scoped; no push) |
| issue-plan | `git add <draft>; git commit -m "docs(plan): draft for #<n>"; git push` | `jj commit docs/superpowers/plans/drafts/{file} -m "docs(plan): draft for #<n>"` |
| issue-spec-review (promote) | `git mv … ; git commit -m "docs(spec): promote {slug} (LGTM on #<n>)" -- <new> <old>; git push` | move file + `jj commit docs/superpowers/specs/{slug}.md docs/superpowers/specs/drafts/{slug}.md -m "docs(spec): promote {slug} (LGTM on #<n>)"` |
| issue-spec-review (skip-plan thin plan) | `git add <plan>; git commit -m "docs(plan): derive thin one-shot plan for #<n> (skip-plan)"; git push` | `jj commit docs/superpowers/plans/{file} -m "docs(plan): derive thin one-shot plan for #<n> (skip-plan)"` |
| issue-plan-review (promote) | `git add <plan>; git commit -m "docs(plan): promote {slug} (LGTM on #<n>)" -- <new> <old>; git push` | move file + `jj commit docs/superpowers/plans/{slug}.md docs/superpowers/plans/drafts/{slug}.md -m "docs(plan): promote {slug} (LGTM on #<n>)"` |
| issue-respond (spec gate) | `git mv … ; git commit … ; git push` | move file + `jj commit <new> <old> -m "docs(spec): promote {slug} (approved on #<n>)"` |
| issue-respond (plan gate) | `git mv … ; git commit … ; git push` | move file + `jj commit <new> <old> -m "docs(plan): promote {slug} (approved on #<n>)"` |
| idea | `git add <draft>; git commit -m "docs(spec): draft for #N"; git push` | `jj commit docs/superpowers/specs/drafts/{file} -m "docs(spec): draft for #N"` |
| rigor.md:185 | "Promotes the spec as on any LGTM (`git mv` the draft → top-level `specs/`)." | "(jj file-move the draft → top-level `specs/`)" |

- Each skill's surrounding hygiene note ("never `git add -A`/`.`/`-u`/`commit -a`; verify `git status --porcelain`…") becomes the jj equivalent: "fileset-scoped `jj commit`, never a bare commit; verify `jj status` shows nothing but this artifact" — point to `github.md § Working-tree hygiene` (Task 3) rather than re-explaining.
- `idea` and the skills that mention "throwaway `git worktree`" for trials → "throwaway `jj workspace`."

- [ ] **Step 1: Edit each of the 6 SKILL.md files** per the table. Keep step numbers/headings; replace only the command + the inline hygiene phrasing.

- [ ] **Step 2: Edit `rigor.md:185`** (and scan `rigor.md` for any other `git mv`/`git add`/`git push` — replace in kind).

- [ ] **Step 3: Verify** no git-native commit mechanics remain in the skills: `grep -rnE "git add|git commit|git mv|git push|git worktree" .claude/skills/ .claude/rules/rigor.md` → **no hits** (any remaining hit is a missed site). 

- [ ] **Step 4: Commit:**
  `jj commit .claude/skills/issue-brainstorm/SKILL.md .claude/skills/issue-plan/SKILL.md .claude/skills/issue-spec-review/SKILL.md .claude/skills/issue-plan-review/SKILL.md .claude/skills/issue-respond/SKILL.md .claude/skills/idea/SKILL.md .claude/rules/rigor.md -m "feat(skills): jj-native authoring + promotion commit steps"`

---

## Task 5: `implement-plan.js` → jj workspaces + bookmark-advance integration

**Files:**
- Modify: `.claude/workflows/implement-plan.js` (987 lines; all git mechanics live in agent prompt strings)

**Interfaces:**
- Consumes: `base-branch.{sh,ps1}` (Task 1) for `B`; the `SYNTO_FLOW_INTEGRATE` env var.
- Produces (consumed by `walk-issue.js` Task 6, unchanged call shape): `workflow('implement-plan', { plan, mode })` still returns the same result schema (`merged`, `pushedTasks`/integrated-count, `reason`, `archived`). The `mode` arg keeps `'merge'` | `'dry-run'`.

**Contract — keep the per-task RED→GREEN→review→integrate control flow and the green-gate (`dotnet build --no-restore -c Debug` / `dotnet test --no-build -c Debug` / `dotnet format --verify-no-changes`) UNCHANGED; swap only isolation + integration + base detection:**

- **Base detection (replaces preflight `git rev-parse --abbrev-ref HEAD` / `git fetch` / `git merge --ff-only origin/main`, lines ~237–247):** resolve `B=$(bash .claude/scripts/base-branch.sh)`; run the jj-native probe (Task 2) as the preflight. No `origin/main`, no on-main requirement.
- **Isolation (replaces `git worktree add … -b plan/{slug} origin/main`, lines ~266–278):** `jj workspace add <dir> --revision <B>` — the workspace's working-copy commit starts at **`B`'s tip** (so the promoted plan is read from `B`, current, not a stale tree). Capture the base revision (jj change-id/commit-id) as `baseSha`'s replacement for review-scoping. Stale-workspace cleanup before retry: `jj workspace forget <name>` + remove dir.
- **Plan read:** unchanged in mechanism (read the markdown from disk *inside the workspace*) — but the workspace now guarantees it reflects `B`.
- **Per-task commit (replaces the in-worktree `git commit`):** `jj commit -m "<task message>"` (or `jj describe` + `jj new`) in the workspace, building the task-commit stack on `B`. Fileset-scoping is implicit per task (each task changes its own files); never a bare sweep.
- **Integration — `local` mode (replaces the rebase→gate→`git push origin HEAD:main` loop, lines ~499–530):** per green task: `jj rebase` the task stack onto the **latest `B`** (re-read `B`'s current tip; conflicts are first-class — resolve keeping both intents), re-run the full green-gate, then **`jj bookmark set <B> -r <new-tip>`**. **No push.** Contended bookmark advance (a concurrent workspace moved `B`) **retries on a fresh `jj rebase`** onto the new `B` tip (replaces the 8-attempt ff-push backoff loop; same generous retry budget, now local).
- **Integration — `push` mode (future, `SYNTO_FLOW_INTEGRATE=push`):** after the bookmark advance, **additionally** `jj git push --bookmark <B>` (with `--allow-new` if first push). Default off; `local` is the default.
- **Archive (replaces `git mv … completed/` + `git commit` + ff-push, lines ~541–564):** jj file-move the plan into `docs/superpowers/plans/completed/`, `jj commit <new> <old> -m "chore(plans): archive {slug} (implemented)"` on `B`, advance the bookmark (same integration path); cleanup `jj workspace forget` + remove dir.
- **Cleanup on EVERY exit path (success, gate failure, agent error, exception):** `jj workspace forget <name>` + remove the workspace dir, so abandoned workspaces never accumulate (replaces `git worktree remove --force` + `git branch -D`). The existing "keep worktree on failure for inspection" behavior may be preserved as "keep the workspace dir but `jj workspace forget` it" — decide per the spec's "cleanup runs on every exit path"; default to forgetting + removing.
- **`dry-run` mode (lines ~769–771, ~842–864):** unchanged intent — implement + gate per task, **no bookmark advance, no push**, no archive. Just skip the integration step.
- **REPO_DIR / log lines:** replace `origin/main` references in log strings (e.g. `'… -> origin/main …'`, the final "your local main was left untouched; origin/main has the merged work" note) with bookmark-relative wording (`'… -> <B>'`; "the work is on bookmark `<B>` locally; nothing was pushed").
- **Review scoping (lines ~474–475, `git diff baseSha HEAD`, `git log baseSha..HEAD`):** swap to jj: `jj diff --from <base> --to @` and `jj log -r '<base>..@'` within the workspace. (These are read-only; not in the grep guard, but should be jj for consistency.)

- [ ] **Step 1: Rewrite the preflight + setup prompt strings** — base via `base-branch.sh` + jj probe; `jj workspace add --revision B`; capture base revision; stale-workspace cleanup via `jj workspace forget`.

- [ ] **Step 2: Rewrite the per-task integration prompt string** — `jj rebase` onto latest `B` + green-gate + `jj bookmark set B`; contended-advance retry; gate the `jj git push` behind `SYNTO_FLOW_INTEGRATE=push`.

- [ ] **Step 3: Rewrite the archive + cleanup prompt strings** — jj file-move + commit + bookmark advance; `jj workspace forget` on every exit path.

- [ ] **Step 4: Update `MODE`/dry-run handling and all `origin/main` log/wording strings** to bookmark-relative jj phrasing. Update `REPO_DIR` usage only where it referenced git worktree removal.

- [ ] **Step 5: Verify it still parses.** `node --check .claude/workflows/implement-plan.js`. Expected: no output, exit 0.

- [ ] **Step 6: Verify no git-native mechanics remain.** `grep -nE "origin/main|git worktree|git push .*HEAD:|git symbolic-ref|merge --ff-only" .claude/workflows/implement-plan.js` → **no hits**. (The end-to-end dry-run is Task 8.)

- [ ] **Step 7: Commit:**
  `jj commit .claude/workflows/implement-plan.js -m "feat(implement-plan): jj workspaces + bookmark-advance integration + SYNTO_FLOW_INTEGRATE"`

---

## Task 6: `walk-issue.js` → jj (isolation, promotion, plan-resolution from `B`, sweep)

**Files:**
- Modify: `.claude/workflows/walk-issue.js` (1164 lines; git mechanics in agent prompt strings)

**Interfaces:**
- Consumes: `base-branch.{sh,ps1}` (Task 1); `probe.sh` (Task 2, call site unchanged: `bash .claude/scripts/probe.sh --implement|--no-implement`); `implement-plan` workflow (Task 5, unchanged call shape).

**Contract — translate every git-native touchpoint; preserve concurrency:**
- **Probe invocation (line ~524):** unchanged command (`bash .claude/scripts/probe.sh <flag>`) — the probe itself went jj-native in Task 2.
- **Authoring-stage isolation (lines ~753–765):** replace the per-issue `git worktree add … -b <br> origin/main` + `git add <draft>` + `git commit` + ff-push loop with a jj equivalent. **Per the spec (Component B), authoring/promotion commit to the primary jj working copy via fileset-scoped `jj commit`, no push.** Encode the concurrency contract explicitly: each authoring commit names **only its own artifact path** (`jj commit docs/superpowers/<specs|plans>/drafts/{file}`), so concurrent walks authoring *different* issues' artifacts never sweep each other's drafts or the operator's `README` (jj's op-log serializes the commits; each fileset-scoped commit leaves other changes in the working copy). **Design note (validate in Task 8):** this is the one residual concurrency seam — shared working copy instead of per-issue isolation. If the dry-run shows interleaving problems under width-N, the upshift is to give authoring its own `jj workspace` (symmetric with implement); record that as a follow-up rather than blocking.
- **Review-stage spec/plan promotion (lines ~915–933):** replace the worktree `git mv` + ff-push with jj file-move + fileset-scoped `jj commit` on the working copy (per Task 3's promotion mechanic). The skip-plan thin-plan derivation (same slug, header `> **Rigor:** one-shot`) writes the top-level plan and `jj commit`s only that path. Demote-on-re-entry (top-level → drafts/) is the same jj file-move + commit.
- **Plan-path resolution for ready→implement (lines ~1042–1051) — the real contract change:** replace reading from `origin/main` (`git fetch origin`, `git ls-tree -r --name-only origin/main -- docs/superpowers/plans/`, `git show origin/main:<path>`) with reading from the **resolved bookmark `B`**: resolve `B=$(bash .claude/scripts/base-branch.sh)`, enumerate promoted candidates at `B` via `jj file list -r <B> docs/superpowers/plans/` filtered to top-level `docs/superpowers/plans/[^/]+\.md`, and read each candidate's header at `B` via `jj file show -r <B> <path>` — pick the one whose header carries `> **Tracking issue:** #<issue>` (newest by `YYYY-MM-DD-` prefix on ties). Delete the "primary checkout may be STALE behind origin/main" rationale — under jj local mode `B` is the source of truth and the working copy follows it; the false-negative bug that guarded against doesn't exist. If no candidate at `B` matches → the existing `parkBlocked(issue,'implementing', …)` path (reword "on origin/main" → "on bookmark `<B>`").
- **Stale-worktree sweep (lines ~561–573):** replace `git worktree prune` + `git worktree list --porcelain` + `git worktree remove --force` + `git branch -D` with the jj-workspace sweep: enumerate `jj workspace list`, and for any stale walk/burn/drive/smoke workspace from a crashed prior run, `jj workspace forget <name>` + remove its dir; **leave any live implement-plan workspace untouched** (a concurrent implement-plan owns it). Adjust `WORKTREES_DIR` naming/prefix logic to the jj-workspace dirs.
- **Comment/log prose (lines ~72, ~94, ~407–409, ~628):** reword the `origin/main`/ff-push/worktree mentions to the jj model (bookmark advance, workspace, local).

- [ ] **Step 1: Rewrite the authoring-stage isolation prompt string** to jj fileset-scoped working-copy commit (no push), with the concurrency note.

- [ ] **Step 2: Rewrite the review-stage promotion + skip-plan prompt strings** to jj file-move + fileset commit.

- [ ] **Step 3: Rewrite the plan-path resolution prompt string** to read from bookmark `B` via `jj file list`/`jj file show`; delete the origin/main staleness rationale; reword the park-blocked message.

- [ ] **Step 4: Rewrite the workspace sweep prompt string** to `jj workspace list`/`jj workspace forget`; update prefix/dir logic.

- [ ] **Step 5: Reword remaining `origin/main`/worktree/ff-push prose** in comments and log strings to the jj model.

- [ ] **Step 6: Verify parse.** `node --check .claude/workflows/walk-issue.js`. Expected: exit 0.

- [ ] **Step 7: Verify no git-native mechanics remain.** `grep -nE "origin/main|git worktree|git push .*HEAD:|git symbolic-ref|merge --ff-only|git ls-tree|git show origin" .claude/workflows/walk-issue.js` → **no hits**.

- [ ] **Step 8: Commit:**
  `jj commit .claude/workflows/walk-issue.js -m "feat(walk-issue): jj-native isolation, promotion, plan-resolution from bookmark, workspace sweep"`

---

## Task 7: `github.md` — § Setting state (`ff_push`→jj integration) + CI model + § Config

**Files:**
- Modify: `.claude/rules/github.md` (§ Setting state / the `ff_push` block, lines ~124–142; the CI-model prose; add § Config)

**Interfaces:**
- Consumes: `base-branch.*` and `SYNTO_FLOW_BASE`/`SYNTO_FLOW_INTEGRATE` (Tasks 1, 5).

**Contract:**
- **Replace the `ff_push()` canonical snippet** (rebase-retry `git push origin HEAD:main`, `git reset --hard origin/main` on exhaustion) with the **canonical jj integration snippet**: resolve `B`, `jj rebase` onto the latest `B`, `jj bookmark set <B>` (local mode); under `SYNTO_FLOW_INTEGRATE=push` additionally `jj git push --bookmark <B>`; on contention retry on a fresh rebase; on persistent conflict park `blocked` (no forced/destructive write — drop the `git reset --hard`). Keep it as the single copy-paste source the promotion/close-comment steps reference.
- **Reword the CI-model prose** (any "ff-push to origin/main", "primary checkout", "CI" mentions) to: local bookmark-advance now; `push` mode is the future CI switch; the operator's working copy follows the advanced bookmark.
- **Add § Config** documenting both env vars: `SYNTO_FLOW_BASE` (explicit base override; else auto-resolved bookmark-first per `base-branch.*`) and `SYNTO_FLOW_INTEGRATE` (`local` default = advance bookmark only; `push` = also `jj git push`).

- [ ] **Step 1: Replace the `ff_push` block** with the jj integration snippet (rename it accordingly, e.g. `integrate`/`ff_advance`).

- [ ] **Step 2: Reword CI-model prose** across § Setting state / § The board where `origin/main`/ff-push/"primary checkout commits to main" appears, to the local-bookmark-advance model.

- [ ] **Step 3: Add § Config** with the two env vars and their semantics.

- [ ] **Step 4: Verify** no git-native mechanics remain in github.md: `grep -nE "origin/main|git push|git worktree|git symbolic-ref|merge --ff-only|git add|git commit|git mv" .claude/rules/github.md` → **no hits**. Read the changed sections back for coherence.

- [ ] **Step 5: Commit:**
  `jj commit .claude/rules/github.md -m "docs(github): jj-native integration snippet, CI model, SYNTO_FLOW_* config"`

---

## Task 8: Verification sweep + end-to-end dry-run

**Files:** none modified (acceptance task). Any fix needed loops back to the owning task.

**Contract — the spec's verification section, run as the final gate:**

- [ ] **Step 1: Cross-cutting grep guard (the Global-Constraint invariant).** Run, expecting **zero** matches across the jj-native files:
  ```bash
  grep -rnE "origin/main|git push .*HEAD:|git worktree|git symbolic-ref|merge --ff-only" \
    .claude/scripts/base-branch.sh .claude/scripts/base-branch.ps1 \
    .claude/scripts/probe.sh .claude/scripts/probe.ps1 \
    .claude/workflows/implement-plan.js .claude/workflows/walk-issue.js \
    .claude/rules/github.md .claude/rules/rigor.md \
    .claude/skills/issue-brainstorm/SKILL.md .claude/skills/issue-plan/SKILL.md \
    .claude/skills/issue-spec-review/SKILL.md .claude/skills/issue-plan-review/SKILL.md \
    .claude/skills/issue-respond/SKILL.md .claude/skills/idea/SKILL.md
  ```
  Expected: no output. Any hit → fix in the owning task and re-run.

- [ ] **Step 2: Parse/syntax gates.** `node --check .claude/workflows/implement-plan.js` && `node --check .claude/workflows/walk-issue.js` && `bash -n .claude/scripts/base-branch.sh` && `bash -n .claude/scripts/probe.sh`. All exit 0.

- [ ] **Step 3: Resolver + probe live checks** (re-confirm post-integration): `bash .claude/scripts/base-branch.sh` → `experimental/matching`; `bash .claude/scripts/probe.sh --implement` → `{"ok":true,…}` exit 0.

- [ ] **Step 4: End-to-end dry-run implement against a THROWAWAY bookmark in a scratch workspace** (never the operator's stack). Set `SYNTO_FLOW_BASE=<throwaway-bookmark>` pointing at a scratch jj bookmark; run a minimal plan through `implement-plan` in `mode:'dry-run'` (and, separately, a `local`-mode micro-run if feasible). **Assert:**
  - task commit(s) land on the throwaway bookmark **locally**;
  - **no `origin` ref moves** in `local` mode (`SYNTO_FLOW_INTEGRATE` unset/`local`) — verify `jj git push` was never invoked (e.g. origin bookmark unchanged);
  - the scratch `jj workspace` is **cleaned up** (`jj workspace list` no longer shows it);
  - **validate the bookmark-advance / working-copy-follow behavior** the spec assumes (Task 5 design note) — confirm the operator's working copy correctly tracks the advanced bookmark, OR document the actual jj behavior and upshift if it contradicts the spec.
  - Tear down the throwaway bookmark + scratch workspace afterward.

- [ ] **Step 5: Confirm the unchanged surface is untouched.** `jj diff --stat` (across the stack's jj-native commits) shows **no** changes to `set-status.{sh,ps1}`, `trust.{sh,ps1}`, `evaluate.js`, `issue-review.js`.

- [ ] **Step 6: No separate commit** — Task 8 is verification. If everything passes, proceed to the fold (§ Execution Handoff). If a fix was needed, it was committed under its owning task.

---

## Self-Review (completed during authoring)

- **Spec coverage:** Component A → Task 1; B → Tasks 3,4,6 (authoring/promotion); C → Task 5; D → Task 2; E → Tasks 3,4,6,7 (github.md + skills); F → Task 7 (config); G (unchanged surface) → Task 8 Step 5 guard. Error handling (fail-closed resolver/probe, workspace cleanup every exit, contended-advance retry, no push in local) → Tasks 1,2,5. Testing/verification → Task 8. All covered.
- **Placeholder scan:** no "TBD"/"handle edge cases" — each task carries its concrete contract + commands. (Prose-rewrite tasks intentionally pin behavior, not verbatim wording — see § Plan altitude.)
- **Type/interface consistency:** `base-branch.{sh,ps1}` stdout-name/exit contract is consumed identically by probe (Task 2), implement-plan (Task 5), walk-issue (Task 6), github.md (Task 7). `SYNTO_FLOW_BASE`/`SYNTO_FLOW_INTEGRATE` names are identical across Tasks 1,5,7. The `implement-plan` call shape (`{plan, mode}`) and return schema are preserved (Task 5) so walk-issue's caller (Task 6) is unaffected.
- **Surfaced risk (not a gap):** the authoring-stage shared-working-copy concurrency seam (Task 6 design note) and the bookmark-advance/working-copy-follow assumption (Task 5) are both explicitly validated in Task 8 Step 4, with a defined upshift path — encoded per the spec rather than re-opened.

---

## Execution Handoff

**This plan is implemented LOCAL-ONLY and folded into commit `4acad516`** — it is **not** run through the `implement-plan.js` workflow (that workflow auto-pushes to `origin/main`, which this work must never do, and it is itself one of the files being rewritten). Drive it task-by-task; for each task: make the edits, run the task's verification, then `jj commit <exact-fileset> -m "…"` as listed. Keep the operator's `README` edit isolated in `@` throughout (fileset-scoped commits only; `jj split` if needed; never a bare `jj commit`/`jj describe @`). After Task 8 passes, fold the 8 task commits into `4acad516` (e.g. `jj squash`/`jj rebase` the task commits into the re-port commit per the agreed commit plan) — local, no push.

Two execution options:

**1. Subagent-Driven (recommended)** — dispatch a fresh subagent per task, review between tasks, fast iteration. (Matches the operator's standing preference for subagent execution on substantial Synto work.)

**2. Inline Execution** — execute tasks in this session via superpowers:executing-plans, batch with checkpoints.

Which approach?

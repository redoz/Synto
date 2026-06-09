export const meta = {
  name: 'implement-plan',
  description:
    'Implement ONE approved implementation plan (a single top-level docs/superpowers/plans/*.md, handed by exact path) in its own isolated worktree the subagent-driven-development way: a fresh subagent per TASK doing strict red-green TDD, each followed by a spec-compliance review and a code-quality review (with bounded fix loops). Continuous integration: after every GREEN COMMIT the worktree branch rebases onto origin/main, re-runs the full green-gate, and fast-forward-pushes HEAD straight to main. A final whole-plan review runs at the end, then the plan file is archived to completed/. The local main checkout is left untouched.',
  whenToUse:
    'Run when ONE written implementation plan is ready to implement. Pass {plan:"docs/superpowers/plans/<file>.md"} — the exact top-level plan path (NOT drafts/, NOT completed/, NOT a subdirectory). Pass {mode:"dry-run"} to implement+gate per task without pushing. /issue-implement and the issue-flow driver resolve the ready issue\'s promoted top-level plan and pass its path here, so no other plan is ever touched.',
  phases: [
    { title: 'Preflight', detail: 'verify clean main synced to origin; validate the plan path' },
    {
      title: 'Implement',
      detail:
        'one worktree; setup+extract tasks, then per task a TDD implementer + spec & code-quality review (fix loops)',
    },
    {
      title: 'Integrate',
      detail:
        'after each green task: rebase onto origin/main -> full gate -> ff-push HEAD:main; then final review + archive',
    },
  ],
}

// ---------------------------------------------------------------------------
// Config
// ---------------------------------------------------------------------------
// Tool-level args may arrive as a JSON string; a nested workflow() call delivers an object.
let A = args || {}
if (typeof A === 'string') { try { A = JSON.parse(A) } catch { A = {} } }

const PLANS_DIR = 'docs/superpowers/plans'
const COMPLETED_DIR = 'docs/superpowers/plans/completed'
const WORKTREES_DIR = '.claude/worktrees'
const REPO_DIR = 'C:/dev/Synto' // main repo root (for worktree cleanup; never checked out off main)
const MODE = A.mode || 'merge' // 'merge' (per-commit CI push to origin/main) | 'dry-run'
const PLAN = A.plan // the ONE plan to implement: a repo-relative top-level plan path.

// Normalize any plan reference (slug, filename, or repo-relative path) to a bare slug:
// strip the directory, the .md extension, and a leading YYYY-MM-DD- date prefix.
function toSlug(s) {
  return String(s).replace(/\\/g, '/').split('/').pop().replace(/\.md$/i, '').replace(/^\d{4}-\d{2}-\d{2}-/, '')
}

// Structural validation (fail-closed): the input must be an existing markdown file DIRECTLY under
// docs/superpowers/plans/ — not drafts/, not completed/, not a subdirectory. The string shape is
// checked here; the file's actual existence is confirmed by the Setup+Extract agent (setupOk=false).
function planPathError(p) {
  if (!p || typeof p !== 'string') return 'no plan path provided (pass {plan:"' + PLANS_DIR + '/<file>.md"})'
  const norm = p.replace(/\\/g, '/')
  if (!/^docs\/superpowers\/plans\/[^/]+\.md$/.test(norm))
    return 'plan must be a markdown file directly under ' + PLANS_DIR + '/ (NOT drafts/, NOT completed/, NOT a subdirectory): ' + p
  return null
}

const MAX_ATTEMPTS = 3 // gate fix-and-retry / rebase / push-race attempts
const MAX_REVIEW_ROUNDS = 2 // per-stage review->fix->review iterations

const GREEN_GATE =
  'dotnet build --no-restore -c Debug  (0 errors; analyzer warnings are findings, not gate failures) ; dotnet test --no-build -c Debug  (MTP v2 runner, all green) ; dotnet format --verify-no-changes  (no diffs)'

// ---------------------------------------------------------------------------
// Schemas
// ---------------------------------------------------------------------------
const PREFLIGHT_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  properties: { ok: { type: 'boolean' }, reason: { type: 'string' } },
  required: ['ok', 'reason'],
}

// Setup + task extraction for one plan. Also folds in the old Discover step: the plan's touched
// `files` and one-line `summary` (used by the final whole-plan review).
const TASKS_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  properties: {
    setupOk: { type: 'boolean' },
    worktreePath: { type: 'string' },
    branch: { type: 'string' },
    baseSha: { type: 'string' },
    tasks: {
      type: 'array',
      items: {
        type: 'object',
        additionalProperties: false,
        properties: {
          id: { type: 'string' },
          title: { type: 'string' },
          heading: { type: 'string' },
          fullText: { type: 'string' },
          commitMessage: { type: 'string' },
        },
        required: ['id', 'title', 'heading', 'fullText'],
      },
    },
    files: { type: 'array', items: { type: 'string' } },
    summary: { type: 'string' },
    notes: { type: 'string' },
  },
  required: ['setupOk', 'worktreePath', 'branch', 'tasks', 'files', 'summary'],
}

// One task implementer / fixer report.
const TASK_IMPL_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  properties: {
    status: { type: 'string', enum: ['DONE', 'DONE_WITH_CONCERNS', 'BLOCKED', 'NEEDS_CONTEXT'] },
    redObserved: { type: 'boolean' },
    greenObserved: { type: 'boolean' },
    commits: { type: 'array', items: { type: 'string' } },
    filesChanged: { type: 'array', items: { type: 'string' } },
    summary: { type: 'string' },
    concerns: { type: 'string' },
  },
  required: ['status', 'redObserved', 'greenObserved', 'summary'],
}

const SPEC_REVIEW_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  properties: {
    compliant: { type: 'boolean' },
    issues: { type: 'array', items: { type: 'string' } },
    notes: { type: 'string' },
  },
  required: ['compliant', 'issues'],
}

const QUALITY_REVIEW_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  properties: {
    approved: { type: 'boolean' },
    critical: { type: 'array', items: { type: 'string' } },
    important: { type: 'array', items: { type: 'string' } },
    minor: { type: 'array', items: { type: 'string' } },
    assessment: { type: 'string' },
  },
  required: ['approved', 'critical'],
}

const GATE_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  properties: {
    green: { type: 'boolean' },
    attempts: { type: 'integer' },
    commits: { type: 'array', items: { type: 'string' } },
    problems: { type: 'string' },
  },
  required: ['green', 'attempts'],
}

// One per-commit "land on origin/main" report (rebase -> gate -> ff-push HEAD:main).
const INTEGRATE_INCREMENT_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  properties: {
    pushed: { type: 'boolean' },
    nothingToIntegrate: { type: 'boolean' },
    gateGreen: { type: 'boolean' },
    headShaAfter: { type: 'string' },
    attempts: { type: 'integer' },
    conflictsResolved: { type: 'array', items: { type: 'string' } },
    problems: { type: 'string' },
  },
  required: ['pushed', 'problems'],
}

// Plan-file archive + worktree cleanup report.
const ARCHIVE_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  properties: {
    pushed: { type: 'boolean' },
    headShaAfter: { type: 'string' },
    problems: { type: 'string' },
  },
  required: ['pushed', 'problems'],
}

// ---------------------------------------------------------------------------
// Prompt builders (no backticks inside; use plain quotes for code/commands)
// ---------------------------------------------------------------------------
function preflightPrompt() {
  return [
    'You are the preflight check for an autonomous plan-implementation run on the Synto repo.',
    'Project: Synto, a C#/.NET Roslyn source-generator toolkit (netstandard2.0, .NET 10, jj over git).',
    'Host: Windows, PowerShell (a Bash tool is also available).',
    'Working directory is the repo root (C:/dev/Synto). Verify ALL of the following and report:',
    '1. The trunk is "main"  (git rev-parse --abbrev-ref HEAD). NOTE: the repo is managed with jj over git, so the',
    '   working copy may sit on a DETACHED HEAD. If so, that is fine — verify the local "main" ref instead and treat',
    '   it as the trunk pointer for the checks below.',
    '2. Working tree is clean  (git status --porcelain prints nothing).',
    '3. Local main has not DIVERGED from origin. Run "git fetch origin", then:',
    '   - if main == origin/main: fine;',
    '   - if origin/main is an ancestor of main (unpushed local commits exist, e.g. your own WIP): fine, leave them',
    '     UNTOUCHED — plans branch off and integrate onto origin/main, so your local-only commits are never moved or pushed;',
    '   - if main is an ancestor of origin/main (local is behind): run "git merge --ff-only origin/main" to catch up;',
    '   - otherwise the branch has diverged: set ok=false, reason "main diverged from origin".',
    '   (Test ancestry with: git merge-base --is-ancestor <maybe-ancestor> <descendant> ; exit 0 means yes.)',
    'Synto is a build-time library + source generators: there is NO database, NO containers, and NO network runtime',
    'to bring up — there is nothing else to check.',
    'Other than an allowed fast-forward of main in step 3, do NOT change any tracked files.',
    'Return ok=true only if the trunk is main, the tree is clean, and main has not diverged;',
    'otherwise ok=false with a precise reason.',
  ].join('\n')
}

// --- Implement phase: one plan, broken into per-task subagents ---

function setupExtractPrompt(plan) {
  return [
    'You are the SETUP+EXTRACT step for ONE approved implementation plan in the Synto repo.',
    'Project: Synto, a C#/.NET Roslyn source-generator toolkit (netstandard2.0, .NET 10, jj over git).',
    'Host: Windows, PowerShell (Bash tool also available). You run in the MAIN repository working directory (C:/dev/Synto).',
    '',
    'Plan file: ' + plan.path + '   (slug: ' + plan.slug + ')',
    '',
    'STEP A — validate the path, then create an isolated worktree branched off the current trunk (origin/main):',
    '  FIRST confirm the plan file exists DIRECTLY under ' + PLANS_DIR + '/ (top level ONLY — NOT ' + PLANS_DIR + '/drafts/,',
    '  NOT ' + COMPLETED_DIR + '/, NOT any other subdirectory). If it is missing or not a top-level plan, set setupOk=false',
    '  with a notes reason and STOP (return empty tasks[], empty files[], and an empty summary).',
    '  Run:  git fetch origin',
    '  Run:  git worktree add ' + WORKTREES_DIR + '/plan-' + plan.slug + ' -b plan/' + plan.slug + ' origin/main',
    '  - If branch plan/' + plan.slug + ' already exists from a stale run, remove the stale worktree',
    '    ("git worktree remove --force ..." then "git branch -D plan/' + plan.slug + '") and retry.',
    '  - If "git worktree add" fails because of a lock (another plan is being set up concurrently), wait a moment',
    '    and retry, up to 3 times.',
    '  Then capture the absolute worktree path:  git -C ' + WORKTREES_DIR + '/plan-' + plan.slug + ' rev-parse --show-toplevel',
    '  And capture baseSha — the trunk commit this branch starts from (for the final cumulative review):',
    '    git -C ' + WORKTREES_DIR + '/plan-' + plan.slug + ' rev-parse HEAD',
    '  Do NOT switch the MAIN worktree off main, and do NOT modify the local main branch — another process uses it.',
    '',
    'STEP B — read the plan file and split it into its ordered tasks:',
    '  Tasks are the sections whose heading line starts with "### Task " (e.g. "### Task 1: ..."). For EACH task return:',
    '    - id:       the task id exactly as written, e.g. "Task 1" (no title).',
    '    - title:    the heading text after the id.',
    '    - heading:  the FULL verbatim heading line, so an implementer can locate this exact section later.',
    '    - fullText: the COMPLETE VERBATIM markdown of that task section — from its "### Task" heading up to (but not',
    '                including) the next "### " heading, or the next "## " section, or end of file. Copy it EXACTLY:',
    '                every code block, the "**Files:**" list, every "- [ ]" step, and the commit command. Do NOT',
    '                summarize, reword, truncate, or drop any code. This is the authoritative instruction text.',
    '    - commitMessage: the commit message from the task\'s commit step if present (else empty string).',
    '  Preserve task order. The "## File Structure" and "## Notes for the implementer" sections are NOT tasks.',
    '',
    'STEP C — also summarize the plan as a whole (this folds in what the old discovery step computed):',
    '  - files: every repo-relative file path the plan will create or modify, collected from the "**Files:**" blocks',
    '    (lines like "- Create: path", "- Modify: path", "- Test: path"). Strip surrounding backticks and any trailing',
    '    parenthetical note; keep only the bare path. De-duplicate.',
    '  - summary: one or two sentences on what the plan delivers.',
    '',
    'Do NOT implement anything and do NOT modify tracked files — only create the worktree and read the plan.',
    'Return setupOk (true only if the plan is a valid top-level file AND the worktree exists on branch plan/' + plan.slug + '),',
    'worktreePath (the absolute path), baseSha (the trunk commit captured above), branch (plan/' + plan.slug + '), tasks[],',
    'files[] (the plan\'s touched repo-relative paths), summary (what the plan delivers), and notes.',
  ].join('\n')
}

function taskImplementerPrompt(plan, task, worktreePath, index, total) {
  return [
    'You are implementing ONE task of an approved plan using strict Test-Driven Development.',
    'Project: Synto, a C#/.NET Roslyn source-generator toolkit (netstandard2.0, .NET 10, jj over git).',
    'Host: Windows, PowerShell (Bash tool also available). Conventions you MUST follow: Conventional Commits; NEVER add any',
    'Claude/AI attribution footer to commit messages.',
    '',
    'If you have a "superpowers:test-driven-development" skill available, invoke it and follow it. Either way, the',
    'red-green discipline below is mandatory.',
    '',
    'Work ENTIRELY inside this git worktree — cd into it and run all git/dotnet commands there:',
    '  ' + worktreePath,
    'You are on branch plan/' + plan.slug + '. This is ' + task.id + ' (' + (index + 1) + ' of ' + total + ').',
    'Earlier tasks of this plan are ALREADY committed on this branch. Later tasks are NOT done yet — do NOT touch them.',
    '',
    '=== YOUR TASK (verbatim from the plan) ===',
    task.fullText,
    '=== END TASK ===',
    '',
    'The authoritative copy lives in ' + plan.path + ' under the heading:',
    '  ' + task.heading,
    'If the verbatim text above looks truncated, you MAY open that file and read ONLY that one task section for the',
    'exact code. You MUST NOT read or implement any OTHER task section.',
    '',
    'RED-GREEN-REFACTOR — honor the letter of it (in C#/xUnit terms):',
    '  1. RED:   write the failing test(s) the task specifies (use the task\'s exact test code if it provides any).',
    '            RUN them and CONFIRM they FAIL for the RIGHT reason (feature missing — not a typo/compile error in',
    '            the test itself). If you did not watch it fail, you do not know it tests anything.',
    '  2. GREEN: write the MINIMAL production code to make them pass. Run the tests and CONFIRM they PASS with no new',
    '            warnings. Do NOT add behavior the task did not ask for (YAGNI).',
    '  3. REFACTOR: only once green, tidy names/duplication while staying green.',
    '  Some tasks are pure docs or verification with no new test. For those, do EXACTLY what the steps say and run the',
    '  commands they list. When a step itself says "run the tests and watch them fail", you MUST actually run it and',
    '  observe the result — never assume.',
    '',
    'The suite is Verify-snapshot tests (golden *.verified.cs files) plus CSharpGeneratorDriver harness tests, run',
    'under the Microsoft Testing Platform (MTP v2) runner. They touch NO shared state (no DB, no network), so it is',
    'safe to run the suite while other worktrees run theirs in parallel.',
    '',
    'Commit when the task says to, staging only the files this task changed, using the EXACT commit message the task',
    'specifies (Conventional Commit; no AI footer).',
    '',
    'If you get genuinely stuck, do NOT fake it: report status BLOCKED or NEEDS_CONTEXT with specifics. Bad work is',
    'worse than no work — you will not be penalized for escalating. Report DONE only if the task is fully implemented',
    'and its tests are green.',
    '',
    'Report: status (DONE | DONE_WITH_CONCERNS | BLOCKED | NEEDS_CONTEXT), redObserved (did you run a test and watch it',
    'fail for the right reason — false for no-test doc/verification tasks), greenObserved (did you run and watch the',
    'tests/commands pass), commits (short SHAs you created), filesChanged, a summary, and concerns (any deviation/doubt).',
  ].join('\n')
}

function specReviewPrompt(plan, task, worktreePath, implReport) {
  return [
    'You are reviewing whether ONE task implementation matches its specification. Do NOT trust the implementer report —',
    'verify by READING THE ACTUAL CODE in the worktree.',
    'cd into this worktree:  ' + worktreePath,
    'Branch: plan/' + plan.slug + '. Inspect the commit(s) this task just added (e.g. "git log --oneline -5",',
    '"git show HEAD", or diff against the previous task commit). Read the real code, not just the diff summary.',
    '',
    '=== WHAT WAS REQUESTED (' + task.id + ', verbatim) ===',
    task.fullText,
    '=== END REQUEST ===',
    '',
    '=== WHAT THE IMPLEMENTER CLAIMS THEY DID ===',
    JSON.stringify(implReport, null, 2),
    '=== END CLAIM ===',
    '',
    'Check for: MISSING requirements (skipped, or claimed-but-not-actually-present); EXTRA/unrequested work',
    '(over-engineering, features or flags the task did not ask for); and MISUNDERSTANDINGS (right feature, wrong way).',
    'Following the plan exactly is the goal; deviations the plan did not call for are issues. Do NOT modify anything.',
    '',
    'Return compliant (true ONLY if the code matches the task spec with nothing missing and nothing extra), issues',
    '(specific, each with a file:line reference), and notes.',
  ].join('\n')
}

function qualityReviewPrompt(plan, task, worktreePath) {
  return [
    'You are a CODE-QUALITY reviewer for ONE task implementation (spec compliance has already passed).',
    'cd into this worktree:  ' + worktreePath,
    'Branch: plan/' + plan.slug + '. Review the diff THIS task introduced — inspect its commit(s) ("git show HEAD",',
    'or diff against the previous task commit) and read the actual code.',
    '',
    'BEFORE you judge anything: read .claude/rules/project-phase.md and let it set your severity bar — Synto is a',
    'POC, so calibrate what counts as CRITICAL per that file (critical is the gate that blocks this task).',
    '',
    'Task under review: ' + task.id + ' — ' + task.title,
    '',
    'Judge quality as a senior engineer would: correctness, clear naming (names match behavior, not mechanism), no',
    'needless complexity, tests that verify REAL behavior (not mocks testing themselves), idiomatic C# that matches',
    'the surrounding code, and one clear responsibility per file/unit. Only judge what THIS task changed — do not flag',
    'pre-existing file sizes or unrelated code. Do NOT modify anything.',
    '',
    'Return approved (true ONLY if there are NO critical issues), and issues split into critical (must-fix: correctness,',
    'security, broken or fake tests, clear bugs), important (should-fix), and minor (nice-to-have) — each with a',
    'file:line reference — plus a one-line assessment.',
  ].join('\n')
}

function fixPrompt(plan, worktreePath, kind, issues, scopeLabel, contextText) {
  return [
    'A reviewer found issues in an implementation. Fix them in the worktree, then commit.',
    'Project: Synto (C#/.NET Roslyn source generators). Host: Windows/PowerShell. Conventions: Conventional Commits, NO AI footer.',
    'cd into this worktree:  ' + worktreePath,
    'Branch: plan/' + plan.slug + '.   Scope: ' + scopeLabel,
    '',
    'Resolve these ' + kind + ' issues:',
    JSON.stringify(issues, null, 2),
    '',
    '=== CONTEXT (the spec/intent for this scope) ===',
    contextText,
    '=== END CONTEXT ===',
    '',
    'Fix ONLY these issues (plus anything strictly required to make them correct). Keep changes minimal and inside this',
    'scope — do NOT start unrelated tasks. Preserve TDD: a behavior change needs a test that fails first. Re-run the',
    'relevant tests and CONFIRM they pass (and that you broke nothing else). Then commit with a Conventional Commit',
    'message (no AI footer).',
    '',
    'Report: status (DONE if fixed and green, else BLOCKED/NEEDS_CONTEXT), redObserved, greenObserved, commits,',
    'filesChanged, summary, and concerns.',
  ].join('\n')
}

function greenGatePrompt(plan, worktreePath) {
  return [
    'Run the GREEN-GATE for an implemented plan branch and make it pass. Project: Synto (C#/.NET Roslyn source generators).',
    'Host: Windows/PowerShell. Conventions: Conventional Commits, NO AI footer.',
    'cd into this worktree:  ' + worktreePath,
    'Branch: plan/' + plan.slug + '. All tasks are implemented and committed.',
    '',
    'From the worktree root, run the full gate:',
    '    ' + GREEN_GATE,
    'If anything fails: DEBUG the root cause, FIX it (test-first if it is a behavior change), commit the fix',
    '(Conventional Commit, no AI footer), and re-run the gate. Repeat up to ' + MAX_ATTEMPTS + ' total attempts.',
    'The suite is Verify-snapshot + CSharpGeneratorDriver tests under the MTP v2 runner; they touch no shared state',
    '(no DB, no network), so it is safe to run alongside other worktrees.',
    'Do NOT rebase, merge, push, or touch main.',
    '',
    'Return green (true ONLY if the FULL gate passed: build with 0 errors, all tests green, and no format diffs —',
    'analyzer warnings are findings, not gate failures), attempts (how many gate runs), commits (any fix SHAs you',
    'added), and problems (precise reason it is not green, if so).',
  ].join('\n')
}

function finalReviewPrompt(plan, worktreePath, baseSha) {
  const fileScope =
    plan.files && plan.files.length ? plan.files.map((f) => '"' + f + '"').join(' ') : '.'
  return [
    'You are the FINAL reviewer for a completed plan, reviewing its WHOLE cumulative implementation against the plan.',
    'cd into this worktree:  ' + worktreePath,
    'Branch: plan/' + plan.slug + '. This plan was integrated to the trunk incrementally (each task already',
    'fast-forwarded onto origin/main), so review the cumulative contribution since the trunk point it started from',
    '(commit ' + baseSha + '), scoped to the files this plan owns:',
    '    git diff ' + baseSha + ' HEAD -- ' + fileScope,
    'Also skim the commit messages:  git log --oneline ' + baseSha + '..HEAD  — then OPEN and read the changed code.',
    'Plan file (the requirements): ' + plan.path + '. What it delivers: ' + plan.summary,
    '',
    'BEFORE you judge anything: read .claude/rules/project-phase.md and let it set your severity bar — Synto is a',
    'POC, so calibrate what counts as CRITICAL per that file (critical here triggers a fix pass).',
    '',
    'Confirm the plan as a whole is delivered, the per-task pieces fit together coherently, and no',
    'correctness/security defects were introduced ACROSS tasks (the kind a single-task review would miss). Do NOT',
    'modify anything.',
    '',
    'Return approved (true ONLY if NO critical issues), critical / important / minor issue lists (file:line refs), and',
    'a one-line assessment.',
  ].join('\n')
}

function integrateIncrementPrompt(plan, worktreePath) {
  return [
    'You are LANDING the latest committed work of a plan branch onto the shared trunk (origin/main),',
    'continuous-integration style. You run INSIDE this plan worktree and MUST NOT check out, modify, reset, or',
    'fast-forward the LOCAL "main" branch or the main working directory — another process is actively using it.',
    'Host: Windows/PowerShell. Conventions: Conventional Commits, NO AI attribution footer.',
    'cd into this worktree:  ' + worktreePath,
    'Branch: plan/' + plan.slug + '.',
    '',
    'STEP 0 — is there anything to land?',
    '  git fetch origin',
    '  git rev-list --count origin/main..HEAD',
    '  If that count is 0, an earlier task in a multi-task compile unit committed nothing yet — there is nothing to',
    '  integrate. Report pushed=false, nothingToIntegrate=true, problems="" and STOP. THIS IS SUCCESS, not a failure.',
    '',
    'Otherwise land the work with LINEAR history. NEVER force-push; NEVER "git reset --hard"; never discard commits',
    'you did not create. Repeat the following up to ' + MAX_ATTEMPTS + ' times:',
    '  1. git fetch origin',
    '  2. Rebase this branch onto the current trunk:  git rebase origin/main',
    '     - On conflict: resolve keeping BOTH intents — this plan change AND whatever is already on origin/main',
    '       (e.g. union both sets of additions in a shared file like Directory.Packages.props or Synto.slnx). "git add"',
    '       the resolved files, then "git rebase --continue". Repeat until the rebase completes.',
    '     - If you cannot resolve after real effort: "git rebase --abort" and report pushed=false with the reason.',
    '  3. Run the FULL green-gate on the rebased branch (a rebase can introduce a semantic conflict the textual',
    '     merge missed):',
    '         ' + GREEN_GATE,
    '     If it fails: DEBUG the root cause, FIX it (test-first for a behavior change), commit (Conventional Commit,',
    '     no AI footer), and re-run until green. If it cannot go green, report pushed=false, gateGreen=false + reason.',
    '  4. Fast-forward the trunk by pushing THIS branch HEAD straight to main on the remote (this does NOT touch the',
    '     local main checkout):',
    '         git push origin HEAD:main',
    '     - SUCCESS: report pushed=true, gateGreen=true, headShaAfter=(git rev-parse --short HEAD), attempts, and any',
    '       files in conflictsResolved. STOP.',
    '     - REJECTED as non-fast-forward (another plan pushed to main first): loop again from step 1 to rebase onto',
    '       the new trunk and re-gate. Do NOT force-push.',
    'If you exhaust ' + MAX_ATTEMPTS + ' attempts still losing the push race, report pushed=false, reason "lost push race repeatedly".',
    '',
    'Do NOT archive the plan file and do NOT remove the worktree here — that happens once, at the very end.',
    'Report: pushed, nothingToIntegrate, gateGreen, headShaAfter, attempts, conflictsResolved (files), problems.',
  ].join('\n')
}

function archiveAndPushPrompt(plan, worktreePath, planPath) {
  return [
    'Final step for a fully-implemented, fully-integrated plan: archive its plan file on the trunk and clean up.',
    'You run INSIDE this worktree. Do NOT touch the LOCAL main branch or the main working directory.',
    'Host: Windows/PowerShell. Conventions: Conventional Commits, NO AI attribution footer.',
    'cd into this worktree:  ' + worktreePath,
    'Branch: plan/' + plan.slug + '.',
    '',
    '1. Move the plan file into the completed/ archive and commit (a pure file move — no gate needed):',
    '     git mv "' + planPath + '" ' + COMPLETED_DIR + '/',
    '     git commit -m "chore(plans): archive ' + plan.slug + ' (implemented)"',
    '2. Land it on the trunk (a fast-forward; on non-ff rejection re-fetch/re-rebase/re-push; resolve any conflict',
    '   keeping both intents). Retry up to ' + MAX_ATTEMPTS + ':',
    '     git fetch origin ; git rebase origin/main ; git push origin HEAD:main',
    '3. Clean up — run these from the MAIN repo dir, NOT from inside the worktree:',
    '     cd ' + REPO_DIR,
    '     git worktree remove --force "' + worktreePath + '"',
    '     git branch -D plan/' + plan.slug,
    '',
    'Report: pushed (did the archive commit reach origin/main), headShaAfter (git rev-parse --short of the pushed',
    'commit), and problems (empty if clean).',
  ].join('\n')
}

// ---------------------------------------------------------------------------
// Per-task review gate: spec-compliance then code-quality, each with a bounded
// review -> fix -> review loop. Returns { accepted, blocked, openIssues, commits }.
// A task is "accepted" only when it is spec-compliant AND has no critical quality
// issues. Important/minor quality notes are surfaced but do NOT block.
// ---------------------------------------------------------------------------
async function reviewAndFix(plan, task, worktreePath, implReport) {
  const extraCommits = []
  const scope = task.id + ' — ' + task.title
  let report = implReport

  // --- Stage 1: spec compliance ---
  let specCompliant = false
  for (let round = 0; round <= MAX_REVIEW_ROUNDS; round++) {
    const spec = await agent(specReviewPrompt(plan, task, worktreePath, report), {
      schema: SPEC_REVIEW_SCHEMA,
      label: 'spec:' + plan.slug + ':' + task.id,
      phase: 'Implement',
    })
    if (spec && spec.compliant) {
      specCompliant = true
      break
    }
    const issues = (spec && spec.issues && spec.issues.length ? spec.issues : ['spec reviewer reported non-compliant'])
    if (round === MAX_REVIEW_ROUNDS) {
      return { accepted: false, blocked: false, openIssues: issues.map((i) => 'spec: ' + i), commits: extraCommits }
    }
    const fix = await agent(fixPrompt(plan, worktreePath, 'SPEC-COMPLIANCE', issues, scope, task.fullText), {
      schema: TASK_IMPL_SCHEMA,
      agentType: 'general-purpose',
      label: 'fix-spec:' + plan.slug + ':' + task.id,
      phase: 'Implement',
    })
    if (fix && fix.commits) extraCommits.push(...fix.commits)
    report = fix || report
    if (fix && (fix.status === 'BLOCKED' || fix.status === 'NEEDS_CONTEXT')) {
      return { accepted: false, blocked: true, openIssues: issues.map((i) => 'spec: ' + i), commits: extraCommits }
    }
  }
  if (!specCompliant) {
    return { accepted: false, blocked: false, openIssues: ['spec not compliant'], commits: extraCommits }
  }

  // --- Stage 2: code quality (only CRITICAL issues block) ---
  for (let round = 0; round <= MAX_REVIEW_ROUNDS; round++) {
    const q = await agent(qualityReviewPrompt(plan, task, worktreePath), {
      schema: QUALITY_REVIEW_SCHEMA,
      label: 'quality:' + plan.slug + ':' + task.id,
      phase: 'Implement',
    })
    if (!q) {
      // Reviewer infra failure: do not block the whole plan on it; note and accept.
      return { accepted: true, blocked: false, openIssues: ['quality reviewer unavailable'], commits: extraCommits }
    }
    const critical = q.critical || []
    if (critical.length === 0) {
      const soft = (q.important || []).map((i) => 'important: ' + i)
      return { accepted: true, blocked: false, openIssues: soft, commits: extraCommits }
    }
    if (round === MAX_REVIEW_ROUNDS) {
      return { accepted: false, blocked: false, openIssues: critical.map((i) => 'quality-critical: ' + i), commits: extraCommits }
    }
    const fix = await agent(fixPrompt(plan, worktreePath, 'CRITICAL CODE-QUALITY', critical, scope, task.fullText), {
      schema: TASK_IMPL_SCHEMA,
      agentType: 'general-purpose',
      label: 'fix-quality:' + plan.slug + ':' + task.id,
      phase: 'Implement',
    })
    if (fix && fix.commits) extraCommits.push(...fix.commits)
    if (fix && (fix.status === 'BLOCKED' || fix.status === 'NEEDS_CONTEXT')) {
      return { accepted: false, blocked: true, openIssues: critical.map((i) => 'quality-critical: ' + i), commits: extraCommits }
    }
  }
  return { accepted: false, blocked: false, openIssues: ['quality critical unresolved'], commits: extraCommits }
}

// Uniform result shape for one plan. `merged` = at least fully integrated through
// its last task; `pushedTasks` = how many increments reached origin/main (CI may
// leave earlier tasks landed even when a later task stops the plan).
function planResult(plan, branch, worktreePath, fields) {
  const problems = fields.problems
  return {
    slug: plan.slug,
    branch,
    worktreePath,
    green: !!fields.green,
    merged: !!fields.merged,
    pushedTasks: fields.pushedTasks || 0,
    commits: fields.commits || [],
    summary: fields.summary || '',
    problems: Array.isArray(problems) ? problems.join(' | ') : problems || '',
    reason: fields.reason || (fields.merged ? 'merged' : fields.green ? 'green (not merged)' : 'not green'),
  }
}

// Implement ONE plan end-to-end in its own worktree, integrating each green commit
// onto origin/main as it lands (continuous integration). The local main checkout is
// never touched. Always returns a planResult.
async function implementPlan(plan) {
  const branchGuess = 'plan/' + plan.slug
  try {
    // 1. Setup worktree (off origin/main) + extract tasks + capture baseSha + fold in files/summary.
    const setup = await agent(setupExtractPrompt(plan), {
      schema: TASKS_SCHEMA,
      agentType: 'general-purpose',
      label: 'setup:' + plan.slug,
      phase: 'Implement',
    })
    if (!setup || !setup.setupOk || !setup.worktreePath || !(setup.tasks && setup.tasks.length)) {
      return planResult(plan, (setup && setup.branch) || branchGuess, (setup && setup.worktreePath) || '', {
        summary: 'setup/extract failed',
        problems: setup ? 'no tasks extracted or worktree not ready; notes: ' + (setup.notes || '') : 'setup agent returned null',
        reason: 'setup failed',
      })
    }
    const worktreePath = setup.worktreePath
    const branch = setup.branch || branchGuess
    const tasks = setup.tasks
    const baseSha = setup.baseSha || 'origin/main'
    plan.files = setup.files || [] // fold-in: the plan's touched files (was the Discover step)
    plan.summary = setup.summary || '' // fold-in: what the plan delivers
    log(plan.slug + ': worktree ready (' + worktreePath + '); ' + tasks.length + ' task(s); base ' + baseSha + '.')

    const allCommits = []
    const problems = []
    let pushedTasks = 0

    // 2. Implement each task sequentially in the shared worktree, with review gates,
    //    then land its commit(s) on origin/main before moving to the next task.
    for (let t = 0; t < tasks.length; t++) {
      const task = tasks[t]
      const impl = await agent(taskImplementerPrompt(plan, task, worktreePath, t, tasks.length), {
        schema: TASK_IMPL_SCHEMA,
        agentType: 'general-purpose',
        label: 'impl:' + plan.slug + ':' + task.id,
        phase: 'Implement',
      })
      if (impl && impl.commits) allCommits.push(...impl.commits)
      if (!impl || impl.status === 'BLOCKED' || impl.status === 'NEEDS_CONTEXT') {
        problems.push(task.id + ' implementer ' + (impl ? impl.status : 'returned null') + ': ' + (impl ? impl.concerns || '' : ''))
        log('STOP ' + plan.slug + ' at ' + task.id + ': implementer ' + (impl ? impl.status : 'null'))
        return planResult(plan, branch, worktreePath, {
          pushedTasks,
          commits: allCommits,
          summary: 'stopped at ' + task.id + ' (implementer)',
          problems,
          reason: 'stopped at ' + task.id + ' (implementer)',
        })
      }

      const rev = await reviewAndFix(plan, task, worktreePath, impl)
      if (rev.commits) allCommits.push(...rev.commits)
      if (!rev.accepted) {
        problems.push(task.id + ' review unresolved: ' + (rev.openIssues || []).join('; '))
        log('STOP ' + plan.slug + ' at ' + task.id + ': review gate not satisfied' + (rev.blocked ? ' (fix blocked)' : ''))
        return planResult(plan, branch, worktreePath, {
          pushedTasks,
          commits: allCommits,
          summary: 'stopped at ' + task.id + ' (review)',
          problems,
          reason: 'stopped at ' + task.id + ' (review)',
        })
      }
      if (rev.openIssues && rev.openIssues.length) problems.push(task.id + ' notes: ' + rev.openIssues.join('; '))
      log('OK ' + plan.slug + ' ' + task.id + ' implemented + reviewed (' + (t + 1) + '/' + tasks.length + ').')

      // --- Continuous integration: land this task's commit(s) on origin/main now. ---
      const taskCommits = [...(impl.commits || []), ...(rev.commits || [])]
      if (MODE === 'dry-run') {
        if (taskCommits.length) log('  (dry-run) ' + task.id + ' committed; not integrating.')
        continue
      }
      if (taskCommits.length === 0) {
        log('  ' + task.id + ' produced no commit (mid compile-unit); rides along with the next green task.')
        continue
      }
      const integ = await agent(integrateIncrementPrompt(plan, worktreePath), {
        schema: INTEGRATE_INCREMENT_SCHEMA,
        agentType: 'general-purpose',
        label: 'integrate:' + plan.slug + ':' + task.id,
        phase: 'Integrate',
      })
      if (!integ || (!integ.pushed && !integ.nothingToIntegrate)) {
        problems.push(task.id + ' integrate failed: ' + (integ ? integ.problems : 'agent returned null'))
        log('STOP ' + plan.slug + ' at ' + task.id + ': integration failed — ' + (integ ? integ.problems : 'null'))
        return planResult(plan, branch, worktreePath, {
          pushedTasks,
          commits: allCommits,
          summary: 'stopped at ' + task.id + ' (integration); ' + pushedTasks + ' earlier increment(s) already on main',
          problems,
          reason: 'stopped at ' + task.id + ' (integration)',
        })
      }
      if (integ.pushed) {
        pushedTasks++
        if (integ.conflictsResolved && integ.conflictsResolved.length)
          problems.push(task.id + ' rebase resolved: ' + integ.conflictsResolved.join(', '))
        log('  LANDED ' + plan.slug + ' ' + task.id + ' -> origin/main ' + (integ.headShaAfter || ''))
      }
    }

    // 3a. dry-run: one end-to-end green-gate + informational final review, no push/archive.
    if (MODE === 'dry-run') {
      const gate = await agent(greenGatePrompt(plan, worktreePath), {
        schema: GATE_SCHEMA,
        agentType: 'general-purpose',
        label: 'gate:' + plan.slug,
        phase: 'Implement',
      })
      if (gate && gate.commits) allCommits.push(...gate.commits)
      const final = await agent(finalReviewPrompt(plan, worktreePath, baseSha), {
        schema: QUALITY_REVIEW_SCHEMA,
        label: 'final-review:' + plan.slug,
        phase: 'Integrate',
      })
      const fc = (final && final.critical) || []
      if (gate && !gate.green) problems.push('dry-run green-gate: ' + (gate.problems || 'failed'))
      if (fc.length) problems.push('dry-run final review critical: ' + fc.join('; '))
      return planResult(plan, branch, worktreePath, {
        green: !!(gate && gate.green) && !fc.length,
        commits: allCommits,
        summary: (final && final.assessment) || 'dry-run: implemented ' + tasks.length + ' task(s)',
        problems,
        reason: 'dry-run',
      })
    }

    // 3b. Final whole-plan cross-task review against the trunk point; fix-forward + land any critical.
    const final = await agent(finalReviewPrompt(plan, worktreePath, baseSha), {
      schema: QUALITY_REVIEW_SCHEMA,
      label: 'final-review:' + plan.slug,
      phase: 'Integrate',
    })
    const finalCritical = (final && final.critical) || []
    if (finalCritical.length) {
      log('Final review of ' + plan.slug + ' raised ' + finalCritical.length + ' critical issue(s); one bounded fix pass.')
      const fix = await agent(
        fixPrompt(plan, worktreePath, 'FINAL-REVIEW CRITICAL', finalCritical, 'whole plan', 'Plan: ' + plan.summary),
        { schema: TASK_IMPL_SCHEMA, agentType: 'general-purpose', label: 'fix-final:' + plan.slug, phase: 'Integrate' },
      )
      if (fix && fix.commits) allCommits.push(...fix.commits)
      const integ = await agent(integrateIncrementPrompt(plan, worktreePath), {
        schema: INTEGRATE_INCREMENT_SCHEMA,
        agentType: 'general-purpose',
        label: 'integrate-final:' + plan.slug,
        phase: 'Integrate',
      })
      if (integ && integ.pushed) pushedTasks++
      const final2 = await agent(finalReviewPrompt(plan, worktreePath, baseSha), {
        schema: QUALITY_REVIEW_SCHEMA,
        label: 'final-review2:' + plan.slug,
        phase: 'Integrate',
      })
      const stillCritical = (final2 && final2.critical) || []
      if (stillCritical.length || !integ || (!integ.pushed && !integ.nothingToIntegrate)) {
        problems.push('final review critical unresolved: ' + stillCritical.join('; '))
        return planResult(plan, branch, worktreePath, {
          merged: true, // earlier increments are already on main; the fix-forward did not land
          pushedTasks,
          commits: allCommits,
          summary: 'final review found unresolved critical issues (earlier tasks already on main)',
          problems,
          reason: 'final review unresolved',
        })
      }
    }

    // 4. Archive the plan file on the trunk + clean up the worktree/branch.
    const arch = await agent(archiveAndPushPrompt(plan, worktreePath, plan.path), {
      schema: ARCHIVE_SCHEMA,
      agentType: 'general-purpose',
      label: 'archive:' + plan.slug,
      phase: 'Integrate',
    })
    const archived = !!(arch && arch.pushed)
    if (!archived) problems.push('archive: ' + (arch ? arch.problems : 'agent returned null'))

    return planResult(plan, branch, worktreePath, {
      green: true,
      merged: true,
      pushedTasks,
      commits: allCommits,
      summary: (final && final.assessment) || 'implemented + integrated ' + tasks.length + ' task(s)',
      problems,
      reason: archived ? 'merged + archived' : 'merged; archive failed',
    })
  } catch (e) {
    return planResult(plan, branchGuess, WORKTREES_DIR + '/plan-' + plan.slug, {
      summary: 'exception during implementation',
      problems: 'exception: ' + ((e && e.message) || String(e)),
      reason: 'exception',
    })
  }
}

// ---------------------------------------------------------------------------
// Orchestration — single plan, no waves, no parallelism.
// ---------------------------------------------------------------------------
phase('Preflight')
const pf = await agent(preflightPrompt(), { schema: PREFLIGHT_SCHEMA, label: 'preflight' })
if (!pf || !pf.ok) {
  log('Preflight FAILED, aborting: ' + (pf ? pf.reason : 'preflight agent returned null'))
  return { aborted: true, reason: pf ? pf.reason : 'preflight null' }
}
log('Preflight OK — clean main in sync with origin.')

// Validate the single plan input (fail-closed: must be a top-level plan path).
const pathErr = planPathError(PLAN)
if (pathErr) {
  log('Plan validation FAILED, aborting: ' + pathErr)
  return { aborted: true, reason: pathErr }
}

const plan = { slug: toSlug(PLAN), path: PLAN.replace(/\\/g, '/') }
if (MODE === 'dry-run') log('MODE = dry-run: the plan will be implemented and gated but NOT merged or pushed.')
log('Implementing plan ' + plan.slug + ' (' + plan.path + ').')

phase('Implement')
const result = await implementPlan(plan)

if (result.merged) {
  log('DONE  ' + result.slug + ': ' + result.pushedTasks + ' increment(s) on origin/main — ' + result.reason)
} else if (MODE === 'dry-run' && result.green) {
  log('OK    ' + result.slug + ' green (dry-run, not pushed). worktree=' + (result.worktreePath || '?'))
} else {
  log(
    'FAIL  ' + result.slug + ': ' + result.reason + '. ' + result.pushedTasks + ' increment(s) landed before stopping. ' +
      'worktree=' + (result.worktreePath || '?') + ' kept for inspection. ' + (result.problems || ''),
  )
}
if (MODE !== 'dry-run' && result.merged) {
  log(
    'NOTE: your local main was left untouched; origin/main has the merged work. Run "git pull --ff-only" ' +
      '(or "git pull --rebase" if you have local-only commits) when ready.',
  )
}

// Return the single planResult directly (no { results: [...] } wrapper).
return result

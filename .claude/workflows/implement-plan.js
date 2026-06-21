export const meta = {
  name: 'implement-plan',
  description:
    'Implement ONE approved implementation plan (a single top-level docs/superpowers/plans/*.md, handed by exact path) in its own isolated jj workspace using a single BATCHED, self-rotating implementer. Each generation reads progress MECHANICALLY from "Plan-Tasks" commit trailers (read jj-native from the workspace history), burns small committable green UNITS (fast commit-stage build -> jj commit with a Plan-Tasks trailer -> integrate.sh, whose acceptance stage runs the full green-gate on the rebased-onto-B tree, then advances the integration bookmark B; push only under SYNTO_FLOW_INTEGRATE=push), and self-rotates near its quality zone (~175k tokens); a fresh generation then resumes from the trailers, until every plan task has landed. Then ONE deep end-review of the cumulative diff across Synto\'s quality lenses fix-forwards the critical/high/medium defects THIS change introduced, and the plan file is archived to completed/. In the default LOCAL mode nothing is pushed; the operator working copy follows the advanced bookmark via jj auto-rebase. Adaptive rigor: the plan header Rigor tag at most tunes in-prompt TDD strictness — it no longer selects an engine.',
  whenToUse:
    'Run when ONE written implementation plan is ready to implement. Pass {plan:"docs/superpowers/plans/<file>.md"} — the exact top-level plan path (NOT drafts/, NOT completed/, NOT a subdirectory). Pass {mode:"dry-run"} to implement + gate + commit per unit WITHOUT advancing the bookmark or pushing. Optionally pass {planReviewConcerns:["..."]} — the consumer\'s deduped plan-review critical/high concerns, used purely as the deep end-review\'s focusing checklist; implement-plan itself never reads an issue or board.',
  phases: [
    { title: 'Preflight', detail: 'resolve the integration bookmark B (base-branch.sh) + run the jj-native probe (probe.sh); validate the plan path' },
    {
      title: 'Implement',
      detail:
        'one jj workspace rooted at B; batched self-rotating generations — each burns small green units (commit-stage build -> jj commit Plan-Tasks -> integrate.sh runs the full acceptance gate then advances bookmark B) and self-rotates near the quality zone; fresh generations resume from the commit trailers',
    },
    {
      title: 'Review',
      detail:
        'one deep end-review of the cumulative diff vs the base across Synto\'s quality lenses; fix-forward the critical/high/medium defects this change introduced via the same per-unit mechanic; then archive the plan and forget the workspace',
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
const WORKSPACES_DIR = '.claude/workspaces' // one isolated jj workspace per plan run (rooted at bookmark B's tip)
const REPO_DIR = 'C:/dev/Synto' // main repo root (for jj workspace forget + dir cleanup; never touches the default workspace)
const MODE = A.mode || 'integrate' // 'integrate' (per-unit CI: advance bookmark B) | 'dry-run' (commit+gate per unit, no advance)
// SYNTO_FLOW_INTEGRATE (env) = 'local' (default) | 'push' gates whether the advanced bookmark is ALSO pushed. It is
// read inside the integrate/archive AGENT shells (the sandboxed JS runtime exposes no env); local mode pushes NOTHING.
const PLAN = A.plan // the ONE plan to implement: a repo-relative top-level plan path.
// Adaptive rigor: the plan file's "> **Rigor:**" header NO LONGER selects an engine — the batched engine is the sole
// path. The tag (one-shot | tdd-per-task) at most tunes in-prompt TDD strictness in the implementer generation. The
// DEFAULT SOURCE is the plan header (read by setup); A.rigor is an ad-hoc override. (Synto has no rigor.md; the tag
// defaults to tdd-per-task when absent.)
const RIGOR_OVERRIDE = A.rigor === 'one-shot' || A.rigor === 'tdd-per-task' ? A.rigor : null

// Optional plan-review focusing checklist (the issue-agnostic input). A consumer that ran a plan-review pass may
// collect that team's critical/high concerns, dedupe them, and pass them in here as plain strings. implement-plan
// treats them as an OPAQUE checklist for the deep end-review — it NEVER reads an issue, parses a tracking header, or
// calls any forge CLI. Empty/absent for a direct /implement run or a standalone plan (the checklist is then skipped).
const PLAN_REVIEW_CONCERNS = Array.isArray(A.planReviewConcerns)
  ? A.planReviewConcerns.filter((s) => typeof s === 'string' && s.trim()).map((s) => s.trim())
  : []

// Per-agent model tiers (cost calibration). The session model (opus, inherited when no `model` is set) is RESERVED
// for the agents that exercise real engineering JUDGEMENT — the implementer generations, the deep end-review, and
// the fix-forward. The mechanical agents drop a tier:
//   FAST (haiku) — the progress probe: pure commit-trailer reading, "NO judgement" by construction.
//   MID  (sonnet) — preflight, setup, archive: rule-following shell (bookmark/probe checks; jj workspace add + plan
//                   parse; plan-file move + bookmark advance + workspace forget). Consequential, but not judgement-heavy.
const FAST = 'haiku'
const MID = 'sonnet'

// Absolute paths to the two mechanical helper scripts. Each plan runs in a jj WORKSPACE rooted at bookmark B's tip;
// to be robust regardless of whether that checkout already carries these (unpushed) scripts, the agents (which run
// them via Bash) call them by ABSOLUTE path, never a workspace-relative one. Interim/sandbox form for now.
const SCRIPTS_DIR = REPO_DIR + '/.claude/scripts'
const INTEGRATE_SH = SCRIPTS_DIR + '/integrate.sh' // mechanical, LLM-free per-unit integration onto bookmark B
const CONTEXT_FILL = SCRIPTS_DIR + '/context-fill.py' // reads the agent's own transcript -> rotation verdict (exit 10)

// Rotation cap: an ABSOLUTE context-token quality-zone cap (~17% of a 1M window), not a window fraction — model
// sharpness degrades long before the window fills, and the still-sharp zone is ~absolute, not a percentage.
const ROTATE_MAX_TOKENS = 175000

// Backstops on the generation loop. The no-progress guard (a generation that lands no NEW Plan-Tasks trailer parks)
// already bounds it to <= one generation per unit; the cap is a defensive net against a pathological loop.
const MAX_GENERATIONS = 25
const MAX_REVIEW_FIX_ROUNDS = 3 // deep-review -> fix-forward -> re-review iterations (crit/high hard-gate the close; medium soft-gate: warn + proceed once it's all that's left after the cap)

// Normalize any plan reference (slug, filename, or repo-relative path) to a bare slug:
// strip the directory, the .md extension, and a leading YYYY-MM-DD- date prefix.
function toSlug(s) {
  return String(s).replace(/\\/g, '/').split('/').pop().replace(/\.md$/i, '').replace(/^\d{4}-\d{2}-\d{2}-/, '')
}

// Structural validation (fail-closed): the input must be an existing markdown file DIRECTLY under
// docs/superpowers/plans/ — not drafts/, not completed/, not a subdirectory. The string shape is
// checked here; the file's actual existence is confirmed by the Setup agent (setupOk=false).
function planPathError(p) {
  if (!p || typeof p !== 'string') return 'no plan path provided (pass {plan:"' + PLANS_DIR + '/<file>.md"})'
  const norm = p.replace(/\\/g, '/')
  if (!/^docs\/superpowers\/plans\/[^/]+\.md$/.test(norm))
    return 'plan must be a markdown file directly under ' + PLANS_DIR + '/ (NOT drafts/, NOT completed/, NOT a subdirectory): ' + p
  return null
}

const MAX_ATTEMPTS = 3 // bounded local green-gate fix-and-retry attempts inside a generation (NOT the bookmark race)
// Bookmark-advance retry budget — deliberately higher than MAX_ATTEMPTS because the advance lands on the SHARED
// integration bookmark B: another concurrently-running implement-plan can keep advancing B, so a small budget gets
// STARVED before it can win the race. Each attempt re-rebases onto the latest B tip, re-gates, and re-advances
// behind an incremental backoff so the competing advance can settle. Tunable — raise if contention grows.
const FF_PUSH_MAX_ATTEMPTS = 8

// The single canonical FULL green-gate (Synto's exact gate). One string so the gate can never drift across its call
// sites (integrate.sh's acceptance re-gate and the dry-run per-unit gate). The format gate is scoped to
// `dotnet format whitespace` (line-endings + layout) ONLY — full `dotnet format` also applies analyzer code-fixes
// (CA1515 strips `public` off xUnit test classes -> xUnit1000 build errors; CA1815 injects throwing operators) that
// rewrite code and regress the build, so a full-format gate can never be clean here. Determinism across Windows/Linux
// comes from the root .editorconfig (end_of_line=lf).
//
// Deployment-pipeline stages (the Farley shape). The implementer does NOT run this whole gate per unit. Per unit it
// runs the fast COMMIT STAGE (`dotnet build` — compile, incremental; analyzer warnings are findings, not failures,
// since there is no clippy equivalent and TreatWarningsAsErrors is not set) for fast feedback, plus the SPECIFIC
// tests for that unit (watch them go red then green). The FULL suite is the ACCEPTANCE STAGE, run by integrate.sh on
// the REBASED-onto-B tree BEFORE bookmark B advances — so nothing red lands on B, and tests run against what actually
// integrates. Dry-run has no integrate stage, so there the per-unit gate IS the full gate.
const GREEN_GATE =
  'dotnet build --no-restore -c Debug  (0 errors; analyzer warnings are findings, not gate failures) ; dotnet test --no-build -c Debug  (MTP v2 runner, all green) ; dotnet format whitespace --verify-no-changes  (no diffs)'

// ---------------------------------------------------------------------------
// Green-gate transient-flake guard (mirrors the FF_PUSH bounded+backoff pattern). Synto's green-gate (dotnet build +
// test + format) is deterministic and fully LOCAL — there is no DB, no container, and no network runtime, so almost
// every failure is a real CODE/TEST defect in THIS diff. But a few ENVIRONMENT faults are NOT code defects and cannot
// be fixed by editing code: a transient NuGet RESTORE failure / network blip, or a transient file-lock / build-host
// fault when several workspaces build at once. Telling an agent to "DEBUG + FIX + re-run until green" on one of those
// just burns gate attempts. The guard makes the gate/integrate agents CLASSIFY the failure, do a SHORT bounded
// backoff-retry (a transient blip usually clears), and otherwise PARK cleanly with transient=true. integrate.sh
// classifies the same signatures itself.
// ---------------------------------------------------------------------------
const GATE_INFRA_MAX_RECHECKS = 3 // bounded backoff-retries before parking on a transient environment failure
const GATE_INFRA_GUARD = [
  'GATE FAILURE POLICY -- CLASSIFY BEFORE YOU FIX. A failing gate is one of two kinds:',
  '  - TRANSIENT/ENVIRONMENT: not a defect in your diff and not fixable by editing code. Signatures: a NuGet',
  '    RESTORE failure or network blip (e.g. "Unable to load the service index", an "NU1301"/feed error, a',
  '    connection or timeout pulling a package); a transient FILE LOCK or build-host fault when another workspace',
  '    is building at the same time (e.g. "being used by another process", a locked obj/bin output, an MSBuild',
  '    worker that died). These come and go on their own under concurrency; you CANNOT fix them by editing code.',
  '  - CODE/TEST: a C# COMPILE error, a failing xUnit ASSERTION, a Verify SNAPSHOT mismatch (a *.received.cs vs',
  '    *.verified.cs diff), or a "dotnet format" finding tied to THIS diff. THIS is what you DEBUG and FIX',
  '    (test-first for a behavior change), commit, and re-run, per the steps above.',
  'On a TRANSIENT/ENVIRONMENT failure do NOT edit code and do NOT churn the gate. Wait a SHORT GROWING backoff',
  '(~2s, 4s, 6s; PowerShell Start-Sleep -Seconds <n>) and re-run the gate (for a restore blip, run "dotnet',
  'restore" first) up to ' + GATE_INFRA_MAX_RECHECKS + ' times -- the blip usually clears. If it is STILL failing',
  'transiently after ' + GATE_INFRA_MAX_RECHECKS + ' retries, STOP: report not-green (and, where the schema asks',
  'for it, transient=true) with problems naming the exact signature (for example "NuGet restore failed: connection',
  'timeout" or "build output locked by a concurrent workspace"). Parking cleanly on a transient failure is CORRECT,',
  'not a failure -- a human (or a later walk) retries once the environment settles.',
].join('\n')

// ---------------------------------------------------------------------------
// Schemas
// ---------------------------------------------------------------------------
const PREFLIGHT_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  // base = the resolved integration bookmark name B (from base-branch.sh) the run advances onto.
  properties: { ok: { type: 'boolean' }, reason: { type: 'string' }, base: { type: 'string' } },
  required: ['ok', 'reason'],
}

// Setup for one plan: create the jj workspace (rooted at bookmark B), capture baseSha, and read the plan's task-ids
// (NOT the full task bodies — the batched implementer reads the plan file itself), touched files + summary (for the
// deep review), and the rigor tag (an in-prompt TDD-strictness hint only).
const SETUP_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  properties: {
    setupOk: { type: 'boolean' },
    worktreePath: { type: 'string' },
    branch: { type: 'string' },
    baseSha: { type: 'string' },
    // The plan's ordered task-ids exactly as written, e.g. ["Task 1", "Task 2"]. Empty for a thin/one-shot plan
    // (no "### Task" sections) — the implementer then treats the whole plan as a single unit.
    taskIds: { type: 'array', items: { type: 'string' } },
    files: { type: 'array', items: { type: 'string' } },
    summary: { type: 'string' },
    rigor: { type: 'string', enum: ['one-shot', 'tdd-per-task'] },
    notes: { type: 'string' },
  },
  required: ['setupOk', 'worktreePath', 'branch', 'taskIds', 'files', 'summary'],
}

// One batched implementer generation's report. allDone is a HINT (the orchestrator verifies doneness mechanically
// from the commit trailers via the probe). escalate stops the run (plan wrong/infeasible OR a fatal integrate park).
const GEN_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  properties: {
    done: { type: 'boolean' }, // returned gracefully (rotated, finished, or escalated) rather than dying mid-run
    allDone: { type: 'boolean' }, // believes every plan task-id is now landed — a HINT, verified by the probe
    rotated: { type: 'boolean' }, // stopped because context-fill said rotate (exit 10), not because allDone
    escalate: { type: 'boolean' }, // plan wrong/infeasible OR a fatal integrate park (21/22/24/20) — STOP + park
    infra: { type: 'boolean' }, // an escalation that is a TRANSIENT/ENVIRONMENT park (retry once the environment settles)
    unitsLanded: { type: 'integer' }, // units integrated (integrate) or committed (dry-run) this generation, for logging
    handoffNote: { type: 'string' }, // SOFT context for the next generation only — never progress
    problems: { type: 'string' }, // escalation/park reason (exit code + signature), else empty
  },
  required: ['done', 'allDone', 'escalate', 'handoffNote'],
}

// Mechanical progress probe: numbers read straight from the commit trailers, no judgement. The orchestrator computes
// completeness from these against the plan's task-ids (the trailer-based successor to reconcileDispositions).
const PROBE_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  properties: {
    // THIS plan's task-ids found across landed "Plan-Tasks" trailers, intersected with the plan task-ids.
    coveredIds: { type: 'array', items: { type: 'string' } },
    // Count of commits carrying a "Plan: <slug>" trailer since baseSha (the progress signal for the no-progress guard).
    landedCommits: { type: 'integer' },
    // Short commit ids of those commits (for the Implement comment + logging).
    landedShas: { type: 'array', items: { type: 'string' } },
    headSha: { type: 'string' },
    notes: { type: 'string' },
  },
  required: ['coveredIds', 'landedCommits', 'landedShas'],
}

// Deep end-review (and dry-run informational review) findings, partitioned for the fix-forward gate.
// critical/high/medium hold ONLY findings THIS change INTRODUCED (POC-calibrated per project-phase.md) — they drive
// the fix-forward pass. low = introduced nits (informational). deferred = NOT fixed here: pre-existing issues in
// surrounding code, OR project-phase pre-release future-hardening (informational). Severity vocab matches
// standards.md (critical/high/medium/low).
const QUALITY_REVIEW_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  properties: {
    critical: { type: 'array', items: { type: 'string' } },
    high: { type: 'array', items: { type: 'string' } },
    medium: { type: 'array', items: { type: 'string' } },
    low: { type: 'array', items: { type: 'string' } },
    deferred: { type: 'array', items: { type: 'string' } },
    assessment: { type: 'string' },
  },
  required: ['critical', 'high', 'medium', 'low', 'assessment'],
}

// Deep-review fix-forward report (criticals fixed + landed via the same per-unit integrate mechanic).
const FIX_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  properties: {
    fixed: { type: 'boolean' }, // all criticals resolved AND landed green on bookmark B
    commits: { type: 'array', items: { type: 'string' } },
    infra: { type: 'boolean' }, // stopped on an unrecoverable TRANSIENT/ENVIRONMENT park
    problems: { type: 'string' },
  },
  required: ['fixed', 'problems'],
}

// Plan-file archive + jj workspace cleanup report.
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
    'Project: Synto, a C#/.NET Roslyn source-generator toolkit (netstandard2.0, .NET 10, jj over a colocated git repo).',
    'Host: Windows, Git Bash (a PowerShell tool is also available). Work from the repo root (' + REPO_DIR + ').',
    'jj manages the repo; HEAD is typically DETACHED and the integration target is a jj BOOKMARK, not a checked-out branch.',
    'Do EXACTLY two things — run the two project SCRIPTS, do NOT re-derive any check yourself, and do NOT modify any',
    'tracked file:',
    '1. Resolve the integration bookmark B. Run EXACTLY:',
    '     bash .claude/scripts/base-branch.sh',
    '   It prints the resolved bookmark NAME on STDOUT (all logs go to STDERR). Capture that one stdout line as "base".',
    '2. Run the jj-native precondition probe. Run EXACTLY:',
    '     bash .claude/scripts/probe.sh --implement',
    '   It prints human progress to STDERR and EXACTLY ONE compact JSON line to STDOUT: {"ok":true|false,"reason":"…"}.',
    '   The script owns every check (B resolves, the B bookmark exists, the working copy @ is conflict-free). Capture',
    '   that one stdout JSON line verbatim.',
    'Synto is a build-time library + source generators: there is NO database, NO containers, and NO network runtime to',
    'bring up — there is nothing else to check.',
    'Return ok (the probe JSON\'s ok verbatim — set ok=false if base-branch.sh produced no bookmark name), reason (the',
    'probe JSON\'s reason, or the failure reason), and base (the bookmark name from step 1).',
  ].join('\n')
}

function setupPrompt(plan, B) {
  const wsName = 'plan-' + plan.slug
  const wsDir = WORKSPACES_DIR + '/' + wsName
  return [
    'You are the SETUP step for ONE approved implementation plan in the Synto repo (batched implementer).',
    'Project: Synto, a C#/.NET Roslyn source-generator toolkit (netstandard2.0, .NET 10, jj over a colocated git repo).',
    'Host: Windows, Git Bash (PowerShell tool also available). You run in the MAIN repository working directory (' + REPO_DIR + ').',
    '',
    'Plan file: ' + plan.path + '   (slug: ' + plan.slug + ')',
    'Integration bookmark B: ' + B,
    '',
    'STEP A — validate the path, then create an isolated jj WORKSPACE rooted at the current tip of bookmark ' + B + ':',
    '  FIRST confirm the plan file exists DIRECTLY under ' + PLANS_DIR + '/ (top level ONLY — NOT ' + PLANS_DIR + '/drafts/,',
    '  NOT ' + COMPLETED_DIR + '/, NOT any subdirectory). If it is missing or not a top-level plan, set setupOk=false with a',
    '  notes reason and STOP (return empty taskIds[], empty files[], and an empty summary).',
    '  Create the workspace whose working-copy commit @ starts ON TOP of ' + B + ' (so the plan is read CURRENT from ' + B + '):',
    '  FIRST ensure the parent directory ' + WORKSPACES_DIR + ' exists — jj does NOT create intermediate directories, so',
    '  "jj workspace add" fails with "cannot find the path" (os error 3) on a fresh checkout where it is missing:',
    '    PowerShell:  New-Item -ItemType Directory -Force ' + WORKSPACES_DIR + '   (bash:  mkdir -p ' + WORKSPACES_DIR + ' )',
    '    jj workspace add ' + wsDir + ' --name ' + wsName + ' --revision ' + B,
    '  - If a workspace named ' + wsName + ' already exists in jj, OR the directory ' + wsDir + ' already exists on disk',
    '    from a stale/failed run, clear BOTH first: run "jj workspace forget ' + wsName + '" (ignore an "unknown',
    '    workspace" error) then REMOVE the directory ' + wsDir + ', and retry "jj workspace add".',
    '  - If "jj workspace add" fails because of a lock (another plan is being set up concurrently), wait a moment',
    '    and retry, up to 3 times.',
    '  Then capture the ABSOLUTE workspace path: cd into ' + wsDir + ' and run  jj workspace root  (print its output).',
    '  And capture baseSha — the jj commit id of ' + B + '\'s tip this workspace starts from (the base revision for the',
    '  deep end-review). From INSIDE the workspace run:',
    '    jj log --no-graph -r ' + B + ' -T \'commit_id.short()\'',
    '  Do NOT advance or move bookmark ' + B + ', and do NOT touch the DEFAULT workspace (its @) — another process uses it.',
    '',
    'STEP B — read the plan and return its ordered TASK-IDS ONLY (not the full bodies — the batched implementer reads the',
    '  plan file itself). Task sections are headings starting with "### Task " (e.g. "### Task 1: ..."). Return taskIds as',
    '  the bare id of each, in plan order, e.g. ["Task 1", "Task 2"] (the text up to the colon, no title). A one-shot/thin',
    '  plan may have NO "### Task" sections — return an empty taskIds[] and still set setupOk=true (the implementer treats',
    '  the whole plan as a single unit). The "## File Structure" and "## Notes for the implementer" sections are NOT tasks.',
    '',
    'STEP C — summarize the plan as a whole (for the deep end-review):',
    '  - files: every repo-relative file path the plan will create or modify, collected from the "**Files:**" blocks',
    '    (lines like "- Create: path", "- Modify: path", "- Test: path"). Strip surrounding backticks and any trailing',
    '    parenthetical note; keep only the bare path. De-duplicate.',
    '  - summary: one or two sentences on what the plan delivers.',
    '',
    'STEP D — read the plan\'s RIGOR tag. In the plan header, look for a line exactly like:',
    '    > **Rigor:** one-shot',
    '  If present, return rigor="one-shot"; otherwise return rigor="tdd-per-task" (the default — Synto has no rigor.md,',
    '  so absence means tdd-per-task). This tag NO LONGER selects an engine — it only tunes how strict the implementer',
    '  is about test-first (the batched engine is the sole path either way). A one-shot plan is ALLOWED to have NO',
    '  "### Task" sections (empty taskIds[]).',
    '',
    'Do NOT implement anything and do NOT modify tracked files — only create the workspace and read the plan.',
    'Return setupOk (true only if the plan is a valid top-level file AND the workspace ' + wsName + ' exists rooted at ' + B + '),',
    'worktreePath (the ABSOLUTE workspace path), baseSha (the base revision captured above), branch (the workspace name ' + wsName + '), taskIds[],',
    'files[] (the plan\'s touched repo-relative paths), summary (what the plan delivers), rigor ("one-shot"|"tdd-per-task"),',
    'and notes.',
  ].join('\n')
}

// The core: ONE batched implementer generation. Burns committable green units, integrating each, then returns
// gracefully when context-fill says it is near the quality zone — a fresh generation resumes from the commit trailers.
function implementerGenerationPrompt(plan, worktreePath, baseSha, gen, rigor, taskIds, handoffNote, marker, B) {
  const tddHint = rigor === 'one-shot'
    ? 'RIGOR HINT (one-shot): this plan was tagged small / single-concern. You do NOT need strict test-first-per-task — make the change directly and ensure the suite is green; add or adjust a test for any BEHAVIOR change. (The tag tunes strictness only; the mechanic below is unchanged.)'
    : 'RIGOR HINT (tdd-per-task): use Test-Driven Development for each behavioral slice — write the failing test FIRST, RUN it and watch it fail for the RIGHT reason, then minimal code to green. Pure docs/verification steps need no new test — run exactly what the step says and observe the result.'
  const hasTasks = taskIds && taskIds.length
  const idsLine = hasTasks ? taskIds.join(', ') : '(none — thin/one-shot plan; treat the whole plan as a single unit)'
  const trailerTasksHint = hasTasks
    ? '<the task-ids this unit completed, e.g. "Task 1, Task 2">'
    : '<the plan slug "' + plan.slug + '", since this thin plan has no numbered tasks>'
  const integrateStep = MODE === 'dry-run'
    ? [
        '  d. DRY-RUN MODE: do NOT integrate, advance any bookmark, or push. The committed unit stays local in the',
        '     workspace. Go straight to step e.',
      ].join('\n')
    : [
        '  d. Integrate the unit onto bookmark ' + B + ' by ABSOLUTE path (call it from INSIDE this workspace; the script',
        '     reads SYNTO_FLOW_INTEGRATE itself to decide local-advance vs also-push):',
        '         bash ' + INTEGRATE_SH + ' --workspace "' + worktreePath + '" --base ' + B,
        '     It is mechanical (rebase the task stack onto the latest tip of ' + B + ' -> re-run the FULL acceptance gate ->',
        '     advance bookmark ' + B + ' forward-only -> push only under SYNTO_FLOW_INTEGRATE=push), prints one JSON object,',
        '     and sets an EXIT CODE. Act on the code (check $? / $LASTEXITCODE):',
        '         0  = landed (or nothing to integrate) -> continue to step e.',
        '         20 = workspace not in the expected committed state (refused) -> STOP: return escalate=true with the detail in problems.',
        '         21 = rebase CONFLICT the script could not auto-resolve -> STOP: return escalate=true (the orchestrator escalates).',
        '         22 = the ACCEPTANCE gate (full green-gate) failed on the rebased stack -- a test THIS unit broke, or a',
        '              semantic conflict the textual merge missed. DEBUG + FIX it (test-first for a behavior change), commit',
        '              the fix, and re-run integrate.sh, BOUNDED to ' + MAX_ATTEMPTS + ' attempts. If it still cannot go green, STOP: escalate=true.',
        '         23 = TRANSIENT/ENVIRONMENT (NuGet restore blip / build-host file lock) -> do a SHORT bounded backoff-retry',
        '              (' + GATE_INFRA_MAX_RECHECKS + 'x per the policy below); if it recovers, re-run integrate.sh; if it does NOT, STOP: escalate=true AND infra=true.',
        '         24 = lost the bookmark-advance race after the script\'s internal retries -> re-run integrate.sh ONCE more; if it still loses, STOP: escalate=true.',
        '         25 = PUSH-REJECTED (push mode only: the remote refused the advanced bookmark) -> STOP: return escalate=true with the reason (a human reconciles the remote).',
      ].join('\n')
  // The per-unit gate is the fast COMMIT STAGE (`dotnet build`) in integrate mode -- the FULL gate is the ACCEPTANCE
  // stage run by integrate.sh on the rebased-onto-B tree (step d). In dry-run there is no integrate stage, so the
  // per-unit gate is the full GREEN_GATE.
  const gateStep = MODE === 'dry-run'
    ? [
        '  b. GATE (dry-run) — run the FULL green-gate in the workspace and make it pass:',
        '         ' + GREEN_GATE,
        '     In dry-run there is NO integrate ACCEPTANCE stage, so build + the whole test suite + the whitespace check must',
        '     run here. A CODE/TEST failure: DEBUG + FIX (test-first for a behavior change), re-run, BOUNDED to ' + MAX_ATTEMPTS + ' attempts.',
        '     A TRANSIENT/ENVIRONMENT failure: apply the GATE FAILURE POLICY at the bottom. If a CODE failure cannot go green, return escalate=true.',
      ].join('\n')
    : [
        '  b. COMMIT STAGE (fast feedback) — build the workspace and make it compile clean:',
        '         dotnet build --no-restore -c Debug',
        '     0 errors is the bar (analyzer warnings are FINDINGS, not gate failures — TreatWarningsAsErrors is not set,',
        '     and there is no clippy equivalent). This compiles fast and incrementally after the first unit. The FULL test',
        '     suite + the whitespace-format check are the ACCEPTANCE STAGE — integrate.sh (step d) runs them on the',
        '     REBASED tree before advancing ' + B + ', the right place to test what actually lands. So do NOT run the whole',
        '     suite here. You DO, per TDD, run the SPECIFIC tests for THIS unit (watch them go red then green) before',
        '     committing. A build failure: FIX it, BOUNDED to ' + MAX_ATTEMPTS + ' attempts; if it cannot compile, return escalate=true.',
      ].join('\n')
  return [
    'You are ONE generation of a BATCHED, self-rotating plan implementer for the Synto repo. You burn through as many',
    'committable green UNITS of an approved plan as you can — integrating each as it lands — then RETURN gracefully when a',
    'context check says you are near your quality zone. A FRESH generation then resumes exactly where you left off,',
    'reading progress from the commit trailers. You are generation #' + gen + '.',
    'Project: Synto, a C#/.NET Roslyn source-generator toolkit (netstandard2.0, .NET 10, jj over a colocated git repo).',
    'Host: Windows, PowerShell (a Bash tool is also available). Conventions you MUST follow: Conventional Commits; NEVER',
    'add any Claude/AI attribution footer to commit messages (the Plan:/Plan-Tasks: trailers below are NOT a footer —',
    'they are required machine-readable progress markers; keep the message otherwise footer-clean).',
    '',
    'Work ENTIRELY inside this jj workspace — cd into it and run all jj/dotnet commands there:',
    '  ' + worktreePath,
    'You are in the jj workspace plan-' + plan.slug + ', whose stack sits on bookmark ' + B + '. The plan file is your',
    'COMPLETE instruction set — READ IT:',
    '  ' + plan.path,
    'What it delivers: ' + plan.summary,
    'Its task-ids, in order: ' + idsLine,
    '',
    '=== STEP 0: DISCOVER YOUR OWN TRANSCRIPT (for self-rotation) ===',
    'Your unique context marker is:  ' + marker,
    'Run this ONCE to locate your own transcript and CACHE the path it prints (the "transcript" field of the JSON):',
    '    python ' + CONTEXT_FILL + ' --marker "' + marker + '" --max-tokens ' + ROTATE_MAX_TOKENS,
    'Use that cached path as --transcript on every later check (instant, no re-scan). If the exit code is already 10',
    'this early (unlikely), do ONE unit then rotate.',
    '',
    '=== STEP 1: ORIENT MECHANICALLY (never trust prose for what is done) ===',
    'The single source of truth for progress is the commit trailers. Every landed unit of THIS plan carries TWO trailers',
    'in its commit message body:',
    '    Plan: ' + plan.slug,
    '    Plan-Tasks: <comma-separated plan task-ids this unit completed>',
    'Find what is already done by reading THIS workspace\'s history between the base revision and @. Print each commit\'s',
    'id + full description, then collect, from EVERY commit whose description carries a "Plan: ' + plan.slug + '" trailer',
    'line, the union of its "Plan-Tasks" ids. For example:',
    '    jj log --no-graph -r \'' + baseSha + '..@\' -T \'commit_id.short() ++ "\\x1f" ++ description ++ "\\x1e\\n"\'',
    '  (Filtering by the "Plan: ' + plan.slug + '" trailer is REQUIRED — integrate.sh rebases this stack onto the shared',
    '  bookmark ' + B + ', so its history can also contain OTHER plans\' commits whose Plan-Tasks ids like "Task 1" would',
    '  otherwise COLLIDE with yours.)',
    'DONE = that union. The NEXT unit starts at the LOWEST plan task-id NOT in DONE. If EVERY plan task-id is already in',
    'DONE (or, for a thin plan with no task-ids, at least one unit has already landed), you are finished — make no new',
    'commit and return done=true, allDone=true.',
    '',
    '=== STEP 2: DRIFT-CHECK (only when RESUMING — i.e. DONE is non-empty) ===',
    'If some units are already landed, a prior generation did work. Skim recent commits (jj log -r \'' + baseSha + '..@\')',
    'and the handoff note below. If the landed work has DRIFTED from the plan, correct it before continuing. The PLAN IS',
    'LEADING — deviations from it are defects, not creativity. BUT if the PLAN ITSELF is wrong or infeasible (it asks for',
    'something that cannot be built as written), do NOT silently rewrite it: STOP and return escalate=true with the',
    'reason in problems. (On generation #1 with nothing landed, SKIP this step.)',
    'CONTINUOUS REVIEW (cheap — you are already reading the increment): while here, also eyeball the just-landed commits',
    'for any OBVIOUS correctness defect (wrong generated code, broken incremental caching — capturing Compilation /',
    'ISymbol / SemanticModel / SyntaxNode into pipeline state). The automated gate already proved they compile + pass',
    'tests; you are checking they do the RIGHT thing. Fix-forward anything clearly wrong NOW (small batch = caught',
    'early), landing the fix as its own unit (steps b–d). The deep end-review is the final whole-plan backstop, not the only review.',
    'Handoff note from the previous generation (SOFT context only — gotchas/deferrals; progress is NEVER here):',
    '  ' + (handoffNote ? handoffNote : '(none — this is the first generation)'),
    '',
    '=== STEP 3: BURN UNITS ===',
    tddHint,
    'A UNIT is the SMALLEST coherent, committable, GREEN slice of the plan — DEFAULT to ONE plan task per unit. Small',
    'batches are a CI discipline, not an inconvenience: they give fast, precisely-localized feedback and a small blast',
    'radius when something fails. Bundle tasks into one unit ONLY when they genuinely cannot stand alone (e.g. they share',
    'a single compile unit and neither compiles without the other) — NEVER merely to run the gate fewer times (the commit',
    'stage is fast, and the full suite runs once, at integrate). Working from the NEXT not-done task, for EACH unit:',
    '  a. Implement it (per the rigor hint above). The suite is Verify-snapshot tests (golden *.verified.cs files) plus',
    '     CSharpGeneratorDriver harness tests under the Microsoft Testing Platform (MTP v2) runner; they touch NO shared',
    '     state (no DB, no network), so it is safe to run while other workspaces run theirs in parallel.',
    gateStep,
    '  c. Commit the unit with jj (Conventional Commit; NO AI footer), fileset-scoped to ONLY the files this unit changed,',
    '     with BOTH trailers in the commit message body (after a blank line, one trailer per line). For example:',
    '         jj commit <file1> <file2> -m "<conventional subject>',
    '',
    '         Plan: ' + plan.slug,
    '         Plan-Tasks: ' + trailerTasksHint + '"',
    '     jj commits the working-copy changes and starts a fresh empty working-copy commit on top. These trailers are how',
    '     the next generation and the orchestrator know this unit landed — they are MANDATORY and must be ACCURATE. List',
    '     exactly the plan task-ids this commit completes.',
    integrateStep,
    '  e. SELF-ROTATION CHECK — run this after EACH unit (committed in dry-run, integrated otherwise):',
    '         python ' + CONTEXT_FILL + ' --transcript "<your cached path>" --max-tokens ' + ROTATE_MAX_TOKENS,
    '     Exit code 10 => you are at the quality-zone cap: STOP NOW (do NOT start another unit) and return gracefully with',
    '     done=true, rotated=true, allDone=false, and a short handoffNote. Exit 0 => continue to the next unit.',
    '',
    '=== WHEN TO RETURN ===',
    '  - Every plan task-id is now landed (DONE covers them all): return done=true, allDone=true.',
    '  - The rotation check said rotate (exit 10): return done=true, rotated=true, allDone=false, with a handoffNote.',
    '  - You hit an escalation/park condition above (plan wrong/infeasible, or an integrate park — exit 20/21/25',
    '    immediately, or 22/23/24 only after the bounded fix/retry fails): return done=true, escalate=true',
    '    (+ infra=true if it was a TRANSIENT/ENVIRONMENT park) with problems set.',
    'NEVER fabricate progress. Your allDone is a HINT — the orchestrator verifies it against the plan task-list from the trailers.',
    '',
    'handoffNote: a few bullets of SOFT context for the next generation ONLY — gotchas, deferrals, patterns to watch.',
    'NEVER put progress or "what is done" in it (that is read from the commit trailers). Keep it short. Omit it (empty',
    'string) if you have nothing useful to pass on.',
    '',
    GATE_INFRA_GUARD,
    '',
    'Report: done (you returned gracefully rather than dying), allDone (you believe every plan task-id is landed — a',
    'HINT), rotated (you stopped because of the rotation check), escalate (plan wrong/infeasible OR a fatal integrate',
    'park — the orchestrator stops the run), infra (true ONLY if an escalation was a TRANSIENT/ENVIRONMENT park),',
    'unitsLanded (how many units you integrated/committed this generation), handoffNote, and problems (the escalation/park reason, else empty).',
  ].join('\n')
}

// Mechanical progress + completeness probe — numbers read straight from the commit trailers, no judgement, no code changes.
function progressProbePrompt(plan, worktreePath, baseSha, taskIds) {
  const idsLine = taskIds && taskIds.length ? taskIds.join(', ') : '(none — thin/one-shot plan, no numbered tasks)'
  return [
    'You are a MECHANICAL progress probe for a batched plan implementation. You make NO code changes and exercise NO',
    'judgement — you only READ the jj history and report numbers. Project: Synto (C#/.NET Roslyn source generators; jj',
    'over a colocated git repo). Host: Windows/PowerShell (Bash also available).',
    'cd into this jj workspace:  ' + worktreePath,
    'Workspace: plan-' + plan.slug + '.',
    '',
    'Every landed unit of THIS plan carries two commit trailers:  "Plan: ' + plan.slug + '"  and  "Plan-Tasks: <ids>".',
    'Read this workspace\'s history between the base revision and @, printing each commit\'s id + full description, and',
    'consider ONLY commits whose description carries a "Plan: ' + plan.slug + '" trailer line (this filter is REQUIRED —',
    'the stack was rebased onto the shared bookmark, so its history can also hold OTHER plans\' commits whose Plan-Tasks',
    'ids would collide). For example:',
    '    jj log --no-graph -r \'' + baseSha + '..@\' -T \'commit_id.short() ++ "\\x1f" ++ description ++ "\\x1e\\n"\'',
    '',
    'This plan\'s task-ids are: ' + idsLine,
    '',
    'Report (numbers ONLY, read straight from the history — do NOT guess or infer):',
    '  - coveredIds: the union of "Plan-Tasks" ids found across THIS plan\'s commits, INTERSECTED with the plan task-ids',
    '    above (so only real plan task-ids appear; empty for a thin plan).',
    '  - landedCommits: how many commits carry the "Plan: ' + plan.slug + '" trailer.',
    '  - landedShas: the short commit ids of those commits (for the integration comment).',
    '  - headSha: the short commit id of @-  (jj log --no-graph -r @- -T \'commit_id.short()\').',
    '  - notes: anything odd (e.g. a "Plan-Tasks" id that is not a known plan task-id).',
  ].join('\n')
}

// The deep END-REVIEW: one whole-plan review of the cumulative diff against the base revision, across Synto's quality
// lenses (the 4 dimension files + the principal-engineer domain expert — Synto's review surface per its evaluate
// skill / CLAUDE.md), pre-release-calibrated. Unlike a post-hoc-feedback pass this is a real QUALITY GATE on what THIS
// change INTRODUCED: it fix-forwards introduced critical/high/medium defects via the per-unit integrate mechanic, and
// unresolved INTRODUCED critical/high BLOCK the close (medium is pursued across the bounded rounds too, then downgraded
// to a warning if it is all that is left — see the gate logic below). The per-unit automated pipeline (commit-stage
// build -> integrate.sh acceptance full-gate) still gates releasability of each unit; this catches the cross-unit and
// design/intent defects the automated gate cannot. The cheap CONTINUOUS review (implementerGenerationPrompt STEP 2) is
// the per-generation drift + correctness check; this is the final whole-plan backstop.
function deepReviewPrompt(plan, worktreePath, baseSha, concerns) {
  const fileScope =
    plan.files && plan.files.length ? plan.files.map((f) => '"' + f + '"').join(' ') : '.'
  // The plan-review focusing checklist arrives as plain data from the consumer (implement-plan never reads an issue).
  const hasConcerns = Array.isArray(concerns) && concerns.length
  const concernsBlock = hasConcerns
    ? [
        'This plan was gated by a plan-review pass; its critical/high concerns — collected across ALL rounds and',
        'deduped by the consumer — named the known-risky parts and are supplied to you below. Use them as a checklist',
        'that the implementation actually handles each one: for each concern, verify the code CONVINCINGLY addresses it;',
        'if it does NOT, that is a finding here (at a severity matching the concern). This is a FOCUSING checklist, not a',
        're-litigation of concerns already resolved. The concerns:',
        JSON.stringify(concerns, null, 2),
      ].join('\n')
    : '(No prior plan-review concerns were supplied for this run — SKIP this step. Normal for a standalone or one-shot ' +
      'plan, or a direct /implement run.)'
  return [
    'You are the DEEP END-REVIEWER for a completed plan. You review its WHOLE cumulative implementation across Synto\'s',
    'quality lenses and decide what THIS change must fix before it can close. Project: Synto, a C#/.NET Roslyn',
    'source-generator toolkit (netstandard2.0, .NET 10, jj over a colocated git repo). Host: Windows/PowerShell (a Bash',
    'tool is also available).',
    'cd into this jj workspace:  ' + worktreePath,
    'Workspace: plan-' + plan.slug + '.',
    '',
    '=== STEP 1: LOAD YOUR LENSES FIRST (read these BEFORE you look at any code, so the checklists are in your head) ===',
    '  - .claude/playbook/project-phase.md  — sets your severity bar (Synto is pre-release/experimental; calibrate',
    '                                          accordingly — and keep one-way doors + incremental-caching breakage at full severity).',
    '  - .claude/playbook/standards.md      — Severity Definitions + Review Verdicts. The "for full evaluations" scoring',
    '                                          tables do NOT apply here: this is a CHANGE review, not a full evaluation.',
    '  - .claude/playbook/architecture.md and principles.md — the domain context + stable design principles',
    '    (runtime/generator layering, the consumer surface, incremental-generator cacheability, single-source injection).',
    '  - The four dimension files: correctness.md, maintainability.md, performance.md, testability.md.',
    '  - The domain expert: consequences.md (principal-engineer — architectural second-order consequences).',
    '  (All under .claude/playbook/. Synto\'s review surface is deliberately these four dimensions + the principal-engineer',
    '  lens; there is no security/resilience/observability/delivery lens — Synto is a build-time library with no runtime',
    '  service, network, DB, or deployment surface.) The SAME invariant recurring across several files — review it ONCE;',
    '  do not re-report it per dimension.',
    '',
    '=== STEP 2: READ THE CHANGE (now, through all the lenses) ===',
    'This plan was integrated incrementally (each unit already advanced bookmark B), so review the cumulative',
    'contribution since the base revision it started from (commit ' + baseSha + '), scoped to the files this plan owns:',
    '    jj diff --from ' + baseSha + ' --to @ -- ' + fileScope,
    'Skim the commit messages:  jj log -r \'' + baseSha + '..@\'  — then OPEN and read the changed code. Confirm the plan',
    'is delivered as a whole, the per-unit pieces fit together coherently, and no defect was introduced ACROSS units',
    '(the kind a single-unit review would miss).',
    'Plan file (the requirements): ' + plan.path + '. What it delivers: ' + plan.summary,
    '',
    '=== STEP 3: PLAN-REVIEW MEMORY (additive focusing — do this IN ADDITION, never instead of, the review above) ===',
    concernsBlock,
    '',
    '=== STEP 4: CLASSIFY EVERY FINDING — INTRODUCED vs DEFERRED (this gate is load-bearing) ===',
    'We fix what THIS change introduced; we do NOT sign this plan up to fix the whole codebase. "Finding a medium" and',
    '"introducing a medium" are different. For each issue you spot, decide:',
    '  - INTRODUCED: the defect is in code this plan ADDED or MODIFIED — this change caused it. Put it in critical /',
    '    high / medium by its pre-release-calibrated severity. These DRIVE the fix-forward pass.',
    '  - low: an INTRODUCED nit (style/naming/minor inconsistency). Informational — logged, never fixed here.',
    '  - deferred: NOT fixed here — an issue in surrounding/untouched code (pre-existing), OR future-hardening that',
    '    project-phase.md defers (pre-release: feature completeness, performance tuning that does not break incremental',
    '    caching, robustness against inputs not yet faced), EVEN IF this change technically introduced it. Logged as context.',
    'Do NOT modify anything — you only review and classify.',
    '',
    'Return the INTRODUCED findings in critical / high / medium (each a file:line + one-line — these get fix-forwarded),',
    'low (introduced nits, informational), deferred (pre-existing or pre-release-deferred, informational), and a one-line assessment.',
  ].join('\n')
}

// Fix-forward the deep review's INTRODUCED findings (critical/high/medium, each severity-tagged), landing each via the
// same per-unit integrate mechanic (integrate mode).
function fixForwardPrompt(plan, worktreePath, actionable, B) {
  return [
    'A deep end-review of an already-integrated plan found defects THIS change INTRODUCED (critical/high/medium). Fix',
    'them and LAND the fixes on bookmark ' + B + ' using the same per-unit mechanic the implementer uses. Project: Synto',
    '(C#/.NET Roslyn source generators; jj over a colocated git repo). Host: Windows/PowerShell. Conventions:',
    'Conventional Commits, NO AI attribution footer.',
    'cd into this jj workspace:  ' + worktreePath,
    'Workspace: plan-' + plan.slug + '. The plan is fully implemented and landed on ' + B + '; you are fixing findings post-hoc.',
    '',
    'Resolve these findings (and ONLY these, plus what is strictly required to make them correct). Each is prefixed with',
    'its severity — fix all of them:',
    JSON.stringify(actionable, null, 2),
    '',
    'Plan (intent/spec): ' + plan.path + ' — ' + plan.summary,
    '',
    'For each fix: keep it minimal and scoped; a behavior change needs a test that FAILS first. Then LAND it:',
    '  1. Build the workspace and make it compile clean:  dotnet build --no-restore -c Debug  (0 errors).',
    '  2. Commit with jj (Conventional Commit, no AI footer), fileset-scoped to the files you changed ("jj commit <files>',
    '     -m ..."). A review-fix completes no NEW plan task, so it needs no Plan-Tasks trailer.',
    '  3. Integrate it onto ' + B + ' by ABSOLUTE path (call from INSIDE this workspace; the script reads SYNTO_FLOW_INTEGRATE):',
    '         bash ' + INTEGRATE_SH + ' --workspace "' + worktreePath + '" --base ' + B,
    '     It runs the FULL acceptance gate on the rebased stack before advancing ' + B + '. Act on its exit code:',
    '     0 = landed (or nothing to integrate); 20 = workspace not in expected state -> STOP; 21 = rebase conflict -> STOP;',
    '     22 = acceptance gate code-red -> STOP; 23 = transient/env -> short bounded backoff-retry (' + GATE_INFRA_MAX_RECHECKS + 'x) then retry,',
    '     else STOP with infra=true; 24 = lost the bookmark-advance race -> retry once; 25 = push-rejected -> STOP with the reason.',
    '',
    GATE_INFRA_GUARD,
    '',
    'Report: fixed (true ONLY if ALL listed findings are resolved AND landed green on ' + B + '), commits (short commit',
    'ids you created), infra (true ONLY if you stopped on an unrecoverable transient/environment park), and problems',
    '(precise reason if not fixed).',
  ].join('\n')
}

function archiveAndPushPrompt(plan, worktreePath, planPath, B) {
  const planFile = String(planPath).replace(/\\/g, '/').split('/').pop() // bare filename, e.g. 2026-06-22-foo.md
  const workspaceName = 'plan-' + plan.slug
  return [
    'Final step for a fully-implemented, fully-integrated plan: archive its plan file on bookmark ' + B + ', then forget',
    'the workspace. You run INSIDE this jj workspace for steps 1-2. Do NOT touch the DEFAULT workspace (its @) or any other.',
    'Host: Windows/PowerShell (Git Bash also available). Conventions: Conventional Commits, NO AI attribution footer.',
    'cd into this jj workspace:  ' + worktreePath,
    '',
    'INTEGRATION MODE — read the environment variable SYNTO_FLOW_INTEGRATE (bash: "$SYNTO_FLOW_INTEGRATE"; PowerShell:',
    '$env:SYNTO_FLOW_INTEGRATE). UNSET or "local" => LOCAL mode: advance the bookmark only, push NOTHING. ONLY the exact',
    'value "push" => PUSH mode: after the advance, ALSO run "jj git push". Treat any other/empty value as LOCAL.',
    '',
    '1. Move the plan file into the completed/ archive (a pure file move — no gate needed). jj auto-tracks the move; move',
    '   the file on disk (create ' + COMPLETED_DIR + '/ if missing), then commit EXACTLY those two paths:',
    '     jj commit ' + COMPLETED_DIR + '/' + planFile + ' ' + planPath + ' -m "chore(plans): archive ' + plan.slug + ' (implemented)"',
    '2. Land it on ' + B + ' using the SAME local-integration path as the per-task integrate: rebase the archive commit',
    '   onto the current tip of ' + B + ', then advance the bookmark FORWARD-only (NEVER --allow-backwards). Retry up to ' + FF_PUSH_MAX_ATTEMPTS + ',',
    '   waiting a short GROWING backoff between attempts (~2s,4s,6s,... capped ~16s; PowerShell Start-Sleep) so a',
    '   concurrent plan\'s advance of the shared bookmark settles first:',
    '     jj rebase -b @ -o ' + B + '   (resolve any conflict keeping both intents)',
    '     jj bookmark set ' + B + ' -r @-',
    '   THEN in PUSH mode ONLY also run:  jj git push --bookmark ' + B + '   . In LOCAL mode push NOTHING.',
    '3. Forget this jj workspace so jj never tracks a dead one (this does NOT delete the landed commits). Run from the',
    '   MAIN repo dir (' + REPO_DIR + '), NOT from inside the workspace:',
    '     cd ' + REPO_DIR,
    '     jj workspace forget ' + workspaceName + '   (if jj reports it already gone, that is fine)',
    '     Then remove the workspace directory:  ' + worktreePath + '  (PowerShell: Remove-Item -Recurse -Force; bash: rm -rf).',
    '',
    'Report: pushed (true = the archive commit is LANDED on ' + B + ' — advanced locally, or also pushed in push mode),',
    'headShaAfter (jj log --no-graph -r @- -T \'commit_id.short()\' of the landed commit), and problems (empty if clean).',
  ].join('\n')
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

// Uniform result shape for one plan. `merged` = at least fully integrated through its last unit; `pushedTasks` = how
// many unit increments landed on bookmark B (CI may leave earlier units landed even when a later generation parks).
function planResult(plan, branch, worktreePath, fields) {
  const problems = fields.problems
  return {
    slug: plan.slug,
    branch,
    worktreePath,
    green: !!fields.green,
    merged: !!fields.merged,
    pushedTasks: fields.pushedTasks || 0,
    // Completeness attestation (close-gate). null = not computed (a NOT-merged early-return); a merged+archived
    // result MUST set both, equal, or the walk close-gate refuses to close (defense-in-depth in walk-issue.js).
    tasksTotal: fields.tasksTotal === undefined ? null : fields.tasksTotal,
    tasksAccounted: fields.tasksAccounted === undefined ? null : fields.tasksAccounted,
    commits: fields.commits || [],
    // Unresolved INTRODUCED medium findings the deep end-review proceeded past (soft gate) — informational, for the
    // close comment. Empty on every path except a clean close that warned about mediums.
    reviewWarnings: fields.reviewWarnings || [],
    // Findings the deep end-review explicitly did NOT fix — pre-existing issues in surrounding code, OR project-phase
    // pre-release future-hardening. Informational context for the close comment (the consumer surfaces, never auto-files).
    // Empty on every path except a clean close.
    reviewDeferred: fields.reviewDeferred || [],
    summary: fields.summary || '',
    problems: Array.isArray(problems) ? problems.join(' | ') : problems || '',
    reason: fields.reason || (fields.merged ? 'merged' : fields.green ? 'green (not merged)' : 'not green'),
  }
}

// Completeness gate — the trailer-based successor to reconcileDispositions. Doneness is MECHANICAL: every plan
// task-id must appear in a landed "Plan-Tasks" trailer (the probe reads these from the history). A thin plan (no
// task-ids) is "done" once at least one unit has landed. Returns { complete, tasksTotal, tasksAccounted, missing }.
function completenessFromTrailers(taskIds, coveredIds, landedCommits) {
  const ids = Array.isArray(taskIds) ? taskIds : []
  const covered = new Set(Array.isArray(coveredIds) ? coveredIds : [])
  if (ids.length === 0) {
    const done = (landedCommits || 0) > 0
    return { complete: done, tasksTotal: 1, tasksAccounted: done ? 1 : 0, missing: [] }
  }
  const missing = ids.filter((id) => !covered.has(id))
  return { complete: missing.length === 0, tasksTotal: ids.length, tasksAccounted: ids.length - missing.length, missing }
}

// Implement ONE plan end-to-end in its own jj workspace via the batched, self-rotating engine, advancing bookmark B
// with each green unit as it lands (continuous integration). The DEFAULT workspace is never touched directly; it
// follows the advanced bookmark via jj auto-rebase. Always returns a planResult.
async function implementPlan(plan, B) {
  const branchGuess = 'plan-' + plan.slug
  try {
    // 1. Setup jj workspace (rooted at B's tip) + capture baseSha + task-ids + files/summary/rigor.
    const setup = await agent(setupPrompt(plan, B), {
      schema: SETUP_SCHEMA,
      agentType: 'general-purpose',
      label: 'setup:' + plan.slug,
      phase: 'Implement',
      model: MID,
    })
    if (!setup || !setup.setupOk || !setup.worktreePath) {
      return planResult(plan, (setup && setup.branch) || branchGuess, (setup && setup.worktreePath) || '', {
        summary: 'setup failed',
        problems: setup ? 'workspace not ready; notes: ' + (setup.notes || '') : 'setup agent returned null',
        reason: 'setup failed',
      })
    }
    const worktreePath = setup.worktreePath
    const branch = setup.branch || branchGuess
    const baseSha = setup.baseSha || B // base revision for review/probe scoping; falls back to the bookmark name
    plan.files = setup.files || [] // for the deep end-review's file scope
    plan.summary = setup.summary || '' // what the plan delivers
    const taskIds = Array.isArray(setup.taskIds) ? setup.taskIds : []
    // Rigor is now an in-prompt TDD-strictness HINT only (the batched engine is the sole path); A.rigor overrides.
    const rigor = RIGOR_OVERRIDE || setup.rigor || 'tdd-per-task'
    log(plan.slug + ': workspace ready (' + worktreePath + '); ' + (taskIds.length || 'no numbered') + ' task(s); base ' + baseSha + ' on ' + B + '; rigor ' + rigor + '.')

    const problems = []
    let handoffNote = ''
    let lastProbe = null
    let prevLanded = 0 // landedCommits at the end of the previous generation (for the no-progress guard)
    let complete = false
    let coverage = completenessFromTrailers(taskIds, [], 0)

    // 2. Batched generation loop: spawn a generation, then probe the history mechanically for progress + doneness.
    let gen = 0
    while (gen < MAX_GENERATIONS) {
      gen++
      if (handoffNote) log('gen ' + gen + ' resumes with handoff: ' + handoffNote)
      // Marker is unique per generation within this run (Date.now/Math.random are unavailable in the sandbox);
      // context-fill's newest-first scan resolves any cross-run collision to this agent's live transcript.
      const marker = 'CTXFILL::' + plan.slug + '::GEN' + gen
      const g = await agent(
        implementerGenerationPrompt(plan, worktreePath, baseSha, gen, rigor, taskIds, handoffNote, marker, B),
        { schema: GEN_SCHEMA, agentType: 'general-purpose', label: 'impl-gen' + gen + ':' + plan.slug, phase: 'Implement' },
      )
      if (g && g.handoffNote) handoffNote = g.handoffNote

      // A returning generation that escalated (plan wrong/infeasible OR a fatal integrate park) stops the run.
      if (g && g.escalate) {
        const infra = !!g.infra
        problems.push('gen ' + gen + ' escalated: ' + (g.problems || 'plan wrong/infeasible or integrate parked'))
        const probe = await agent(progressProbePrompt(plan, worktreePath, baseSha, taskIds), {
          schema: PROBE_SCHEMA, agentType: 'general-purpose', label: 'probe' + gen + ':' + plan.slug, phase: 'Implement', model: FAST,
        })
        if (probe) lastProbe = probe
        const cov = completenessFromTrailers(taskIds, lastProbe && lastProbe.coveredIds, lastProbe && lastProbe.landedCommits)
        log('ESCALATE ' + plan.slug + ' (gen ' + gen + '): ' + (g.problems || '') + (infra ? ' [infra]' : ''))
        return planResult(plan, branch, worktreePath, {
          merged: false,
          pushedTasks: lastProbe ? lastProbe.landedCommits : 0,
          commits: (lastProbe && lastProbe.landedShas) || [],
          tasksTotal: cov.tasksTotal,
          tasksAccounted: cov.tasksAccounted,
          summary: 'escalated at gen ' + gen + (infra ? ' (integration transient/environment failure)' : ''),
          problems,
          reason: infra
            ? 'escalated: integration transient/environment failure -- not a code defect; retry once the environment settles'
            : 'escalated: plan wrong/infeasible or fatal integrate park (' + (g.problems || '') + ')',
        })
      }

      // Mechanical progress + completeness probe (authoritative — the generation's allDone is only a hint).
      const probe = await agent(progressProbePrompt(plan, worktreePath, baseSha, taskIds), {
        schema: PROBE_SCHEMA, agentType: 'general-purpose', label: 'probe' + gen + ':' + plan.slug, phase: 'Implement', model: FAST,
      })
      if (probe) lastProbe = probe
      const landed = lastProbe ? lastProbe.landedCommits : prevLanded
      coverage = completenessFromTrailers(taskIds, lastProbe && lastProbe.coveredIds, lastProbe && lastProbe.landedCommits)
      complete = coverage.complete
      if (g && g.allDone && !complete)
        log('NOTE ' + plan.slug + ' gen ' + gen + ': generation claims allDone but trailers show ' + coverage.tasksAccounted + '/' + coverage.tasksTotal + ' — continuing.')
      if (complete) {
        log('completeness gate PASSED for ' + plan.slug + ': ' + coverage.tasksAccounted + '/' + coverage.tasksTotal + ' task(s) landed.')
        break
      }

      // No-progress backstop: a whole generation that landed NO new Plan-Tasks trailer is stuck -> park (do not loop).
      if (landed <= prevLanded) {
        problems.push('gen ' + gen + ' landed no new Plan-Tasks trailer (no progress)' + (g ? '' : ' [generation agent died]'))
        log('NO-PROGRESS ' + plan.slug + ' at gen ' + gen + ' (' + coverage.tasksAccounted + '/' + coverage.tasksTotal + ' landed) -> parking.')
        return planResult(plan, branch, worktreePath, {
          merged: false,
          pushedTasks: landed,
          commits: (lastProbe && lastProbe.landedShas) || [],
          tasksTotal: coverage.tasksTotal,
          tasksAccounted: coverage.tasksAccounted,
          summary: 'no progress in generation ' + gen + ' (' + coverage.tasksAccounted + '/' + coverage.tasksTotal + ' landed)',
          problems,
          reason: 'no progress (stuck) at generation ' + gen,
        })
      }
      prevLanded = landed
      // else: spawn the next generation — it resumes mechanically from the trailers.
    }

    if (!complete) {
      problems.push('generation cap (' + MAX_GENERATIONS + ') reached without completeness')
      log('GEN-CAP ' + plan.slug + ': ' + MAX_GENERATIONS + ' generations without completeness -> parking.')
      return planResult(plan, branch, worktreePath, {
        merged: false,
        pushedTasks: lastProbe ? lastProbe.landedCommits : 0,
        commits: (lastProbe && lastProbe.landedShas) || [],
        tasksTotal: coverage.tasksTotal,
        tasksAccounted: coverage.tasksAccounted,
        summary: 'generation cap reached (' + coverage.tasksAccounted + '/' + coverage.tasksTotal + ' landed)',
        problems,
        reason: 'generation cap reached without completeness',
      })
    }

    const landedShas = (lastProbe && lastProbe.landedShas) || []
    const landedCommits = lastProbe ? lastProbe.landedCommits : 0

    // 3a. dry-run: units committed locally with trailers + the completeness gate passed, but nothing pushed. Run one
    //     INFORMATIONAL deep review (no fix-forward push) and report green from the per-unit gates + completeness.
    if (MODE === 'dry-run') {
      const review = await agent(deepReviewPrompt(plan, worktreePath, baseSha, PLAN_REVIEW_CONCERNS), {
        schema: QUALITY_REVIEW_SCHEMA, label: 'deep-review:' + plan.slug, phase: 'Review',
      })
      const crit = (review && review.critical) || []
      const high = (review && review.high) || []
      const med = (review && review.medium) || []
      const low = (review && review.low) || []
      const actionable = crit.length + high.length + med.length
      if (actionable)
        problems.push(
          'dry-run deep review (INTRODUCED, would fix-forward): ' +
            [...crit.map((c) => 'CRIT ' + c), ...high.map((h) => 'HIGH ' + h), ...med.map((m) => 'MED ' + m)].join('; '),
        )
      if (low.length) log('dry-run deep review LOWS (informational) for ' + plan.slug + ': ' + low.join('; '))
      return planResult(plan, branch, worktreePath, {
        green: !actionable,
        commits: landedShas,
        tasksTotal: coverage.tasksTotal,
        tasksAccounted: coverage.tasksAccounted,
        summary: (review && review.assessment) || 'dry-run: ' + coverage.tasksAccounted + '/' + coverage.tasksTotal + ' task(s) committed (not pushed)',
        problems,
        reason: 'dry-run',
      })
    }

    // 3b. Deep end-review across Synto's lenses; fix-forward the defects THIS change INTRODUCED (critical/high/medium)
    //     via the same per-unit integrate mechanic. low + deferred findings are informational (logged, never fixed
    //     here — "finding a medium" != "introducing a medium"). Gate: unresolved INTRODUCED critical/high BLOCK the
    //     close; medium is pursued across the bounded rounds too, then if only medium/low remain (no crit/high) we
    //     PROCEED and warn about the mediums + inform about the lows.
    let review = await agent(deepReviewPrompt(plan, worktreePath, baseSha, PLAN_REVIEW_CONCERNS), {
      schema: QUALITY_REVIEW_SCHEMA, label: 'deep-review:' + plan.slug, phase: 'Review',
    })
    const reviewLists = (r) => ({
      critical: (r && r.critical) || [],
      high: (r && r.high) || [],
      medium: (r && r.medium) || [],
      low: (r && r.low) || [],
      deferred: (r && r.deferred) || [],
    })
    // The fix-forward set = INTRODUCED critical+high+medium, each prefixed with its severity for the fixer.
    const actionableOf = (f) => [
      ...f.critical.map((c) => '[critical] ' + c),
      ...f.high.map((h) => '[high] ' + h),
      ...f.medium.map((m) => '[medium] ' + m),
    ]
    let f = reviewLists(review)
    for (let round = 0; round < MAX_REVIEW_FIX_ROUNDS && actionableOf(f).length; round++) {
      log(
        'Deep review of ' + plan.slug + ' raised ' + f.critical.length + ' critical / ' + f.high.length + ' high / ' +
          f.medium.length + ' medium introduced finding(s); fix-forward round ' + (round + 1) + '.',
      )
      const fix = await agent(fixForwardPrompt(plan, worktreePath, actionableOf(f), B), {
        schema: FIX_SCHEMA, agentType: 'general-purpose', label: 'fix-fwd' + (round + 1) + ':' + plan.slug, phase: 'Review',
      })
      if (fix && fix.infra) {
        problems.push('deep-review fix-forward parked on TRANSIENT/ENVIRONMENT: ' + (fix.problems || ''))
        return planResult(plan, branch, worktreePath, {
          merged: true, // every plan task is already on B; only the post-hoc fix did not land
          pushedTasks: landedCommits,
          commits: landedShas,
          tasksTotal: coverage.tasksTotal,
          tasksAccounted: coverage.tasksAccounted,
          summary: 'deep-review fix-forward parked on a transient/environment failure; plan tasks already on ' + B,
          problems,
          reason: 'deep-review fix parked on transient/environment failure (retry once the environment settles)',
        })
      }
      review = await agent(deepReviewPrompt(plan, worktreePath, baseSha, PLAN_REVIEW_CONCERNS), {
        schema: QUALITY_REVIEW_SCHEMA, label: 'deep-review' + (round + 2) + ':' + plan.slug, phase: 'Review',
      })
      f = reviewLists(review)
    }
    // Informational findings — logged then dropped, regardless of the gate outcome.
    if (f.low.length) log('Deep review LOWS (informational, dropped) for ' + plan.slug + ': ' + f.low.join('; '))
    if (f.deferred.length)
      log('Deep review DEFERRED (pre-existing or pre-release-deferred, not introduced by this change, not fixed) for ' + plan.slug + ': ' + f.deferred.join('; '))
    // HARD gate: unresolved INTRODUCED critical/high after the bounded rounds block the close (work stays on B; the
    // plan stays open for a human). reason != 'merged + archived' => a close-gate excludes it.
    if (f.critical.length || f.high.length) {
      problems.push(
        'deep review unresolved INTRODUCED critical/high: ' +
          [...f.critical.map((c) => 'CRIT ' + c), ...f.high.map((h) => 'HIGH ' + h)].join('; '),
      )
      return planResult(plan, branch, worktreePath, {
        merged: true, // earlier units are already on B; the fix-forward did not clear the crit/high
        pushedTasks: landedCommits,
        commits: landedShas,
        tasksTotal: coverage.tasksTotal,
        tasksAccounted: coverage.tasksAccounted,
        summary: 'deep review found unresolved critical/high issues introduced by this change (plan tasks already on ' + B + ')',
        problems,
        reason: 'deep review unresolved', // not 'merged + archived' => a close-gate excludes it
      })
    }
    // SOFT gate: only medium/low remain (crit/high cleared). PROCEED to close, but warn about the unresolved INTRODUCED
    // mediums (carried on the result for the close comment; the lows were already logged above).
    const mediumWarnings = f.medium
    if (mediumWarnings.length)
      log(
        'WARN ' + plan.slug + ': proceeding to close with ' + mediumWarnings.length +
          ' unresolved INTRODUCED medium(s) after ' + MAX_REVIEW_FIX_ROUNDS + ' fix round(s): ' + mediumWarnings.join('; '),
      )

    // 4. Archive the plan file on bookmark B + forget the workspace.
    const arch = await agent(archiveAndPushPrompt(plan, worktreePath, plan.path, B), {
      schema: ARCHIVE_SCHEMA, agentType: 'general-purpose', label: 'archive:' + plan.slug, phase: 'Review', model: MID,
    })
    const archived = !!(arch && arch.pushed)
    if (!archived) problems.push('archive: ' + (arch ? arch.problems : 'agent returned null'))

    return planResult(plan, branch, worktreePath, {
      green: true,
      merged: true,
      pushedTasks: landedCommits,
      commits: landedShas,
      // All tasks landed (the completeness gate passed) — attest it so a close-gate is satisfied.
      tasksTotal: coverage.tasksTotal,
      tasksAccounted: coverage.tasksAccounted,
      reviewWarnings: mediumWarnings,
      reviewDeferred: f.deferred,
      summary:
        ((review && review.assessment) || 'implemented + integrated ' + coverage.tasksAccounted + '/' + coverage.tasksTotal + ' task(s)') +
        (mediumWarnings.length ? ' [proceeded past ' + mediumWarnings.length + ' unresolved medium warning(s)]' : ''),
      problems,
      reason: archived ? 'merged + archived' : 'merged; archive failed',
    })
  } catch (e) {
    return planResult(plan, branchGuess, WORKSPACES_DIR + '/plan-' + plan.slug, {
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
const pf = await agent(preflightPrompt(), { schema: PREFLIGHT_SCHEMA, label: 'preflight', model: MID })
if (!pf || !pf.ok || !pf.base) {
  const reason = pf ? (pf.ok && !pf.base ? 'preflight resolved no integration bookmark B' : pf.reason) : 'preflight agent returned null'
  log('Preflight FAILED, aborting: ' + reason)
  return { aborted: true, reason }
}
const B = String(pf.base).trim() // the integration bookmark this run advances; resolved by base-branch.sh
log('Preflight OK — integration bookmark B=' + B + ' resolved; jj probe passed.')

// Validate the single plan input (fail-closed: must be a top-level plan path).
const pathErr = planPathError(PLAN)
if (pathErr) {
  log('Plan validation FAILED, aborting: ' + pathErr)
  return { aborted: true, reason: pathErr }
}

const plan = { slug: toSlug(PLAN), path: PLAN.replace(/\\/g, '/') }
if (MODE === 'dry-run') log('MODE = dry-run: the plan will be implemented and gated per unit but the bookmark is NOT advanced and nothing is pushed.')
log('Implementing plan ' + plan.slug + ' (' + plan.path + ') onto bookmark ' + B + ' via the batched self-rotating engine.')

phase('Implement')
const result = await implementPlan(plan, B)

if (result.merged && result.reason === 'merged + archived') {
  log('DONE  ' + result.slug + ': ' + result.pushedTasks + ' unit(s) landed on ' + B + ' — ' + result.reason)
} else if (result.merged) {
  log('PARTIAL ' + result.slug + ': plan tasks on ' + B + ' but ' + result.reason + '. ' + (result.problems || ''))
} else if (MODE === 'dry-run' && result.green) {
  log('OK    ' + result.slug + ' green (dry-run, bookmark not advanced). workspace=' + (result.worktreePath || '?'))
} else {
  log(
    'FAIL  ' + result.slug + ': ' + result.reason + '. ' + result.pushedTasks + ' unit(s) landed before stopping. ' +
      'workspace=' + (result.worktreePath || '?') + ' kept for inspection. ' + (result.problems || ''),
  )
}
if (MODE !== 'dry-run' && result.merged) {
  log(
    'NOTE: the work is on bookmark ' + B + ' locally; in local mode (SYNTO_FLOW_INTEGRATE unset/local, the default) ' +
      'nothing was pushed. Your default working copy follows ' + B + ' via jj auto-rebase. ' +
      '(Under SYNTO_FLOW_INTEGRATE=push it was also pushed to the remote.)',
  )
}

// Return the single planResult directly (no { results: [...] } wrapper).
return result

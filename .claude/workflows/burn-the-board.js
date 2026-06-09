export const meta = {
  name: 'burn-the-board',
  description:
    'Burn the issue-flow board down by proximity-to-done. A simple priority loop driven by a rolling worker pool of width N: snapshot the open issues, pick the N closest-to-done actionable ones (status:ready > plan-review-queued > plan-queued > spec-review-queued > brainstorm-queued, excluding manual/blocked/epic), advance each EXACTLY ONE stage via the matching issue-* skill, and the instant ANY issue finishes refill its freed slot from a fresh re-sorted snapshot (so a slow stage never idles the other slots) — until nothing is actionable (everything done or everything blocked). ready->implement (default-ON) lands the plan and closes the issue, taking items off the board fastest. Unlike issue-flow-drive this carries NO snapshot-as-goal, NO reconcile, and NO intake — it just burns down what is already actionable. Continuous via /loop /burn-the-board.',
  phases: [
    { title: 'RunStart', detail: 'infra probe + ff-sync primary + stale-worktree sweep + minimal crash-reaper' },
    { title: 'Burn', detail: 'rolling worker pool of width N: keep the N closest-to-done advancing, refill each freed slot from a fresh snapshot, to fixpoint' },
    { title: 'Report', detail: 'heartbeat digest + durable run-log + structured return' },
  ],
}

// ─────────────────────────────────────────────────────────────────────────────────────────────────
// INLINED pure helpers — the Workflow runtime has NO module system (static import = syntax error;
// import() throws), so the small decision/scrub helpers are inlined here. The BRAKE_LABELS / sanitize /
// fingerprint definitions are kept BYTE-IDENTICAL to lib/drive-fns.js (the canonical reference copy) and
// the issue-flow-drive.js inlined block (github.md § Reporting a broken skill / spec §13).
// burn-the-board deliberately needs FAR less of that block than the driver: no route(), no commit-gate
// watermark (it relies on each skill's own entry unaddressed-comment guard, not an exit-side gate).
// ─────────────────────────────────────────────────────────────────────────────────────────────────
const BRAKE_LABELS = ['manual']
const PHASE1_QUEUES = [
  'status:brainstorm-queued',
  'status:spec-review-queued',
  'status:plan-queued',
  'status:plan-review-queued',
]
// Proximity-to-done rank (higher = closer to leaving the board → burned first). ready is one implement
// step from done, so it tops the order; brainstorm-queued is furthest. This ordering IS the whole policy.
const PROXIMITY = {
  'status:ready': 5,
  'status:plan-review-queued': 4,
  'status:plan-queued': 3,
  'status:spec-review-queued': 2,
  'status:brainstorm-queued': 1,
}
const SCRUB = [
  [/\b(webhook_secret|OUROCORE_INGEST_TOKEN|DATABASE_URL)\b\s*[=:]\s*\S+/gi, '$1=<redacted>'],
  [/https:\/\/[^@\s/]+@/gi, 'https://<redacted>@'],
  [/x-access-token:[^@\s]+/gi, 'x-access-token:<redacted>'],
  [/Authorization:\s*\S+/gi, 'Authorization: <redacted>'],
]
function sanitize(text) {
  let s = String(text == null ? '' : text)
  for (const [re, to] of SCRUB) s = s.replace(re, to)
  return s
}
function fingerprint(skill, errorText) {
  const sig = sanitize(errorText)
    .replace(/#?\b\d{1,7}\b/g, '#N')
    .replace(/\b[0-9a-f]{7,40}\b/gi, '<sha>')
    .replace(/[A-Za-z]:[\\/][^\s'"]+/g, '<path>')
    .replace(/(^|\s)\/[^\s'"]+/g, '$1<path>')
    .replace(/\s+/g, ' ').trim().slice(0, 200)
  return `${skill}: ${sig}`
}
// ───────────────────────────────────────── end inlined block ────────────────────────────────────────

// ---------------------------------------------------------------------------
// args shim + config (mirror issue-flow-drive.js). The runtime forbids Date.now()/Math.random()/new Date()
// (they throw, to keep resume deterministic), so RUNID is passed in by the skill and every timestamp is
// generated inside an agent's Git Bash (date -u), never in JS.
// ---------------------------------------------------------------------------
let A = args || {}
if (typeof A === 'string') { try { A = JSON.parse(A) } catch { A = {} } }

const IMPLEMENT = A.implement !== false        // default true: ready->implement+close (takes items off the board)
const DRY_RUN = !!A.dryRun                      // mutation-free preview — log every would-be action, change nothing
const K = A.K || 5                              // per-stage review round limit (github.md § The round limit K)
const N = Math.max(1, A.N || 3)                 // max issues advanced CONCURRENTLY per round (the "N")
const MAX_ROUNDS = A.maxRounds || 200           // defensive bound on TOTAL dispatches (a parked/closed issue leaves the
                                                //   actionable set, so this normally terminates well before the cap)
const MAX_ATTEMPTS = 3                          // ff-push rebase-retry attempts
const GRACE_MS = 15 * 60 * 1000                 // reaper recency guard: an issue updated < 15 min ago is a live run, not a crash
const WORKTREES_DIR = '.claude/worktrees'
const REPO_DIR = 'C:/dev/OuroCore'
const RUNID = A.runid || 'burn'                 // fresh per tick from the skill; deterministic fallback
const RUNLOG = '.claude/logs/burn-the-board.jsonl'

// Per-agent model tiers (cost calibration). Agents that omit `model` inherit the session model (Opus 4.8),
// which we keep for the genuinely cognitive / irreversible-write stages: authoring (writes the spec/plan),
// the review+implement CLAIM guards (the unaddressed-comment go-ahead-vs-caveat classification), and the
// review ROUTE (verbatim verdict + promote/demote git mv + ff-push to main). The mechanical agents are
// dropped: pure shell/gh reads -> Haiku (FAST); light-judgment but git/GitHub-mutating -> Sonnet (MID).
const FAST = 'haiku'      // pure-mechanical, read-only shell/gh: snapshot, ff-sync, runlog, worktree sweep
const MID = 'sonnet'      // specified-but-consequential: infra probe, reaper, self-heal fileBug, park, close

// Structured-return accumulator. Per-issue rows are { issue, fromStage, toState, outcome }.
const report = {
  runid: RUNID,
  dryRun: DRY_RUN,
  implement: IMPLEMENT,
  preconditionSkipped: false,
  fixpoint: false,
  rounds: 0,
  advanced: [],
  parked: [],
  implemented: [],
  closed: [],
  machineryErrors: [],
}

// ---------------------------------------------------------------------------
// Schemas — every agent returns validated JSON, never prose (runtime constraint).
// ---------------------------------------------------------------------------
const OPEN_SNAPSHOT_SCHEMA = {
  type: 'object', additionalProperties: false,
  properties: {
    open: {
      type: 'array', items: {
        type: 'object', additionalProperties: false,
        properties: { number: { type: 'integer' }, labels: { type: 'array', items: { type: 'string' } }, updatedAt: { type: 'string' } },
        required: ['number', 'labels', 'updatedAt'],
      },
    },
  },
  required: ['open'],
}
const PROBE_SCHEMA = {
  type: 'object', additionalProperties: false,
  properties: { ok: { type: 'boolean' }, reason: { type: 'string' } },
  required: ['ok', 'reason'],
}
const FFSYNC_SCHEMA = {
  type: 'object', additionalProperties: false,
  properties: { ok: { type: 'boolean' }, problems: { type: 'string' } },
  required: ['ok', 'problems'],
}
const SWEEP_SCHEMA = {
  type: 'object', additionalProperties: false,
  properties: { removed: { type: 'array', items: { type: 'string' } }, problems: { type: 'string' } },
  required: ['removed', 'problems'],
}
const REAP_SCHEMA = {
  type: 'object', additionalProperties: false,
  properties: {
    reaped: { type: 'array', items: { type: 'object', additionalProperties: false, properties: { issue: { type: 'integer' }, from: { type: 'string' }, to: { type: 'string' } }, required: ['issue', 'from', 'to'] } },
    skippedRecent: { type: 'array', items: { type: 'integer' } },
    problems: { type: 'string' },
  },
  required: ['reaped', 'skippedRecent', 'problems'],
}
const AUTHORING_SCHEMA = {
  type: 'object', additionalProperties: false,
  properties: {
    outcome: { type: 'string', enum: ['advanced', 'parked', 'split', 'error'] },
    toState: { type: 'string' },
    draftPath: { type: 'string' },
    problems: { type: 'string' },
  },
  required: ['outcome', 'toState', 'problems'],
}
const CLAIM_REVIEW_SCHEMA = {
  type: 'object', additionalProperties: false,
  properties: { guarded: { type: 'boolean' }, claimed: { type: 'boolean' }, problems: { type: 'string' } },
  required: ['guarded', 'claimed', 'problems'],
}
const ROUTE_ACTION_SCHEMA = {
  type: 'object', additionalProperties: false,
  properties: { toState: { type: 'string' }, promoted: { type: 'boolean' }, demoted: { type: 'boolean' }, problems: { type: 'string' } },
  required: ['toState', 'promoted', 'demoted', 'problems'],
}
const IMPL_CLAIM_SCHEMA = {
  type: 'object', additionalProperties: false,
  properties: { guarded: { type: 'boolean' }, planPath: { type: 'string' }, claimed: { type: 'boolean' }, problems: { type: 'string' } },
  required: ['guarded', 'planPath', 'claimed', 'problems'],
}
const IMPL_RECON_SCHEMA = {
  type: 'object', additionalProperties: false,
  properties: { closed: { type: 'boolean' }, parentClosed: { type: 'boolean' }, problems: { type: 'string' } },
  required: ['closed', 'problems'],
}
const PARK_SCHEMA = {
  type: 'object', additionalProperties: false,
  properties: { parked: { type: 'boolean' }, problems: { type: 'string' } },
  required: ['parked', 'problems'],
}
const FILEBUG_SCHEMA = {
  type: 'object', additionalProperties: false,
  properties: { action: { type: 'string' }, number: { type: 'integer' }, problems: { type: 'string' } },
  required: ['action', 'problems'],
}
const RUNLOG_SCHEMA = {
  type: 'object', additionalProperties: false,
  properties: { appended: { type: 'boolean' } },
  required: ['appended'],
}

// ---------------------------------------------------------------------------
// Pure label helpers (no I/O).
// ---------------------------------------------------------------------------
const DOING_STATES = ['brainstorming', 'planning', 'spec-reviewing', 'plan-reviewing', 'implementing']
function isBraked(labels) { return BRAKE_LABELS.some((b) => (labels || []).includes(b)) }
function doingState(labels) {
  for (const s of DOING_STATES) if ((labels || []).includes('status:' + s)) return s
  return null
}
// proximityOf(labels) — the burn priority of an issue (its highest-ranked status label); 0 if none.
function proximityOf(labels) {
  let best = 0
  for (const l of labels || []) if ((PROXIMITY[l] || 0) > best) best = PROXIMITY[l]
  return best
}
// burnActionable(labels) — is this issue something the loop can autonomously advance one step? A FREE queue
// (or ready, when IMPLEMENT is on), not braked, not blocked, not an epic. blocked/manual/done/inbox are NOT
// actionable — that is exactly the "everything done or everything blocked" stop condition.
function burnActionable(labels) {
  const L = labels || []
  if (isBraked(L)) return false
  if (L.includes('blocked')) return false
  if (L.includes('epic')) return false
  if (L.includes('status:ready')) return IMPLEMENT
  return PHASE1_QUEUES.some((q) => L.includes(q))
}
// topStatus(labels) — short form of the highest-proximity status label, for logging.
function topStatus(labels) {
  let best = 0, name = '?'
  for (const l of labels || []) if ((PROXIMITY[l] || 0) > best) { best = PROXIMITY[l]; name = l.replace('status:', '') }
  return name
}
// doingForLabels(labels) — the Doing state to PARK an actionable issue in on a machinery failure, so it
// leaves the actionable set (and the loop stops re-hammering a broken stage). Derived from the queue label
// the issue was picked at (or its current Doing state if a stage already claimed it).
function doingForLabels(labels) {
  const L = labels || []
  if (L.includes('status:ready')) return 'implementing'
  if (L.includes('status:plan-review-queued')) return 'plan-reviewing'
  if (L.includes('status:plan-queued')) return 'planning'
  if (L.includes('status:spec-review-queued')) return 'spec-reviewing'
  if (L.includes('status:brainstorm-queued')) return 'brainstorming'
  return doingState(L) || 'implementing'
}
function chunk(arr, n) { const out = []; for (let i = 0; i < arr.length; i += n) out.push(arr.slice(i, i + n)); return out }
function errText(e) { return (e && e.message) || String(e) }

// ---------------------------------------------------------------------------
// Observability helpers.
// ---------------------------------------------------------------------------
function hb(line) { log('[burn ' + RUNID + '] ' + line) }              // one-line liveness digest
function narrate(issue, line) { log('#' + issue + ' ' + line) }         // issue#-prefixed narrator

// appendRunLog(obj) — append one JSON line (with a bash-generated UTC timestamp) to the durable run-log.
// Observation, not a GitHub mutation, so it runs even under DRY_RUN. Best-effort: never aborts the burn.
async function appendRunLog(obj) {
  const payload = JSON.stringify({ runid: RUNID, ...obj })
  try {
    await agent(
      [
        'Append EXACTLY one line to a durable JSONL run-log. Host: Windows, Git Bash. Work from the repo root (' + REPO_DIR + ').',
        'Steps:',
        '  1. Ensure the log directory exists:  mkdir -p .claude/logs',
        '  2. Generate a UTC timestamp in bash (NOT any other way):  ts=$(date -u +%Y-%m-%dT%H:%M:%SZ)',
        '  3. The payload below is already a single-line compact JSON object. Produce ONE line equal to that',
        '     payload but with an additional leading field  "t":"<ts>"  inserted as the FIRST key, then append',
        '     that one line plus a trailing newline to  ' + RUNLOG + '  (>> append; never overwrite).',
        '  4. Touch NO other file. One line only; do not pretty-print.',
        'PAYLOAD:',
        payload,
        'Return { appended: true } once the line is appended.',
      ].join('\n'),
      { label: 'runlog:' + RUNID, phase: 'Report', schema: RUNLOG_SCHEMA, model: FAST },
    )
  } catch (e) {
    log('[runlog ' + RUNID + '] append failed (non-fatal): ' + sanitize(errText(e)))
  }
}

// ---------------------------------------------------------------------------
// Self-healing (github.md § Reporting a broken skill): best-effort, SEARCHED-FIRST, de-duped, SANITIZED,
// NO recursion. Under DRY_RUN, logs the would-be bug and creates nothing.
// ---------------------------------------------------------------------------
async function fileBug(skill, error) {
  const fp = fingerprint(skill, errText(error))
  const safe = sanitize(errText(error))
  if (DRY_RUN) { log('(dryRun) would file/recur issue-flow-bug: ' + fp); return }
  try {
    const r = await agent(
      [
        'You are the self-healing reporter for burn-the-board (github.md § Reporting a broken skill).',
        'A MACHINERY failure occurred in skill/stage: ' + skill,
        'Normalized fingerprint (the de-dupe key): ' + fp,
        'Sanitized error (already scrubbed of secrets — do NOT add raw payloads or secrets):',
        safe,
        '',
        'Procedure (best-effort, searched-FIRST so one breakage never spawns duplicates):',
        '  1. gh issue list --label issue-flow-bug --state open --json number,title,body',
        '  2. Judge whether any open bug is the SAME breakage (same skill + same fingerprint signature).',
        '  3. MATCH -> recur, do NOT duplicate: append a  "## 🔁 Recurred — <date>"  comment (date via',
        '     bash: date -u +%Y-%m-%d) noting this occurrence (runid ' + RUNID + ', the sanitized error),',
        '     and bump an occurrence count in the body. Report action="recurred", number=<that issue>.',
        '  4. NO MATCH -> file one: gh issue create  --title  "issue-flow bug: ' + skill + ' — <short signature>"',
        '     --label issue-flow-bug  (also add a severity label, e.g. high), body = the skill, the failing',
        '     step/command, the SANITIZED error above, a runid cross-ref, and the date. Intake will',
        '     stamp it status:inbox. Report action="filed", number=<new issue>.',
        '  5. Sanitize ALWAYS — never put webhook_secret / OUROCORE_INGEST_TOKEN / DATABASE_URL or raw payloads',
        '     in the title or body.',
        'Return { action: "filed"|"recurred"|"noop", number, problems }.',
      ].join('\n'),
      { label: 'filebug:' + RUNID, phase: 'Report', schema: FILEBUG_SCHEMA, model: MID },
    )
    log('[filebug ' + RUNID + '] ' + JSON.stringify({ skill, action: r ? r.action : 'null', number: r ? r.number : 0 }))
  } catch (e2) {
    // NO recursion: print for the operator and stop — never report the report.
    log('[filebug ' + RUNID + '] fileBug itself failed (no recursion): ' + sanitize(errText(e2)))
  }
}

// parkBlocked(issue, status, message) — leave an issue at status:<status> + blocked with a 🛑 note so it
// leaves the actionable set (a human go-ahead, or /issue-drive's reconcile, recovers it later). Best-effort.
async function parkBlocked(issue, status, message) {
  if (DRY_RUN) { narrate(issue, '(dryRun) would park status:' + status + ' + blocked: ' + message); return }
  try {
    await agent(
      [
        'Park issue #' + issue + ' at status:' + status + ' + blocked — it is recoverable. Host: Windows, Git Bash.',
        'Run `bash .claude/scripts/set-status.sh ' + issue + ' ' + status + ' block` (single-select label + board card, sets blocked;',
        'do NOT close, do NOT strand un-blocked), then post a "## 🛑" note with this exact gist (telegraph English):',
        sanitize(message),
        'Return { parked, problems }.',
      ].join('\n'),
      { label: 'park:' + issue, phase: 'Burn', schema: PARK_SCHEMA, model: MID },
    )
  } catch (e) { log('parkBlocked #' + issue + ' failed (non-fatal): ' + sanitize(errText(e))) }
}

// handleMachineryFailure — file a de-duped bug AND park the issue blocked (so the loop converges).
async function handleMachineryFailure(issue, stage, skill, err, parkStatus) {
  const fp = fingerprint(skill, errText(err))
  report.machineryErrors.push({ issue, stage, fingerprint: fp })
  log('[classify ' + RUNID + '] ' + JSON.stringify({ skill, classification: 'machinery', fingerprint: fp }))
  if (issue && parkStatus) {
    try { await parkBlocked(issue, parkStatus, 'burn-the-board ' + stage + ' machinery error: ' + sanitize(errText(err))) }
    catch (e2) { log('park after machinery failed: ' + sanitize(errText(e2))) }
  }
  await fileBug(skill, err)
}

// ---------------------------------------------------------------------------
// Shared reads.
// ---------------------------------------------------------------------------
// snapshotOpen() — the per-round open-issue snapshot (number, labels-as-names, updatedAt). Read-only.
async function snapshotOpen() {
  const res = await agent(
    [
      'List the OPEN issues in this repo as structured data. Host: Windows, Git Bash. Read-only.',
      'Run:  gh issue list --state open --json number,labels,updatedAt --limit 300',
      'Return open[] where each item is { number, labels (an array of label NAME strings, e.g. "status:ready"), updatedAt }.',
      'Flatten the labels objects to their .name strings. Do not modify anything.',
    ].join('\n'),
    { label: 'snapshot:' + RUNID, phase: 'Burn', schema: OPEN_SNAPSHOT_SCHEMA, model: FAST },
  )
  return (res && res.open) || []
}

// ffSyncPrimary() — re-fast-forward-sync the primary checkout before each round, so a promotion/draft push
// never starts from a stale tree (mirror issue-flow-drive).
async function ffSyncPrimary() {
  const res = await agent(
    [
      'Fast-forward-sync the PRIMARY checkout before a burn round. Host: Windows, Git Bash. Run from ' + REPO_DIR + '.',
      '  git fetch origin',
      '  - HEAD == origin/main: fine.',
      '  - origin/main ancestor of HEAD (unpushed local commits): fine, leave them untouched.',
      '  - HEAD ancestor of origin/main (behind): git merge --ff-only origin/main.',
      '  - diverged: do NOT force anything; report ok=false, problems="primary checkout diverged from origin".',
      'Change no tracked files beyond the allowed fast-forward. Return { ok, problems }.',
    ].join('\n'),
    { label: 'ffsync:' + RUNID, phase: 'Burn', schema: FFSYNC_SCHEMA, model: FAST },
  )
  if (!res || !res.ok) hb('ff-sync warning: ' + (res ? res.problems : 'agent returned null'))
  return res
}

// ---------------------------------------------------------------------------
// Run-start (the "Safe" machinery): infra probe -> ff-sync -> stale-worktree sweep -> minimal crash-reaper.
// NO reconcile and NO intake — burn-the-board burns down what is already actionable; un-parking blocked
// issues and stamping new ones are /issue-drive's job.
// ---------------------------------------------------------------------------
async function runStart() {
  // Step 1: infra precondition probe — now a deterministic SCRIPT (.claude/scripts/probe.sh), not a prose
  // spec the agent re-derives. The agent just RUNS it and returns its single stdout JSON line; the script
  // owns the checks (dev Postgres when --implement; main fast-forward vs origin/main — the one allowed
  // mutation). A failure SKIPS the whole run (no issue touched). Pure-mechanical now → FAST model.
  const probeArg = IMPLEMENT ? '--implement' : '--no-implement'
  const probe = await agent(
    [
      'Run the infra precondition probe SCRIPT for burn-the-board and return its result. Host: Windows, Git Bash.',
      'Work from the repo root (' + REPO_DIR + '). Run EXACTLY this one command:',
      '  bash .claude/scripts/probe.sh ' + probeArg,
      'The script prints human-readable progress to STDERR and EXACTLY ONE compact JSON line to STDOUT:',
      '  {"ok":true|false,"reason":"…"}',
      'It performs every check itself (and the single allowed fast-forward of main). Do NOT perform any check',
      'yourself, do NOT start the DB, do NOT change any other file. Capture that one stdout JSON line and',
      'return it verbatim as { ok, reason }.',
    ].join('\n'),
    { label: 'probe:' + RUNID, phase: 'RunStart', schema: PROBE_SCHEMA, model: FAST },
  )
  if (!probe || !probe.ok) {
    const reason = probe ? probe.reason : 'probe agent returned null'
    hb('PRECONDITION FAILED — skipping run, NO issue touched. Blocker: ' + reason + (IMPLEMENT ? '. Remediation: bring up infra-postgres-1 (podman compose -f infra/docker-compose.yml up -d) and/or reconcile main with origin, then re-run.' : '. Remediation: reconcile main with origin, then re-run.'))
    return { skipped: true, reason }
  }

  // Step 2: ff-sync the primary checkout (also done per-round; here it catches up before the sweep/reaper).
  if (DRY_RUN) log('(dryRun) would ff-sync the primary checkout at run-start')
  else await ffSyncPrimary()

  // Step 3: stale-worktree sweep — remove every .claude/worktrees/ tree that is not a live implement-plan
  // plan-<slug> tree (burn-* / drive-* / smoke-* leaks from a crashed prior run). Safe under single-operator.
  if (DRY_RUN) {
    log('(dryRun) would sweep stale worktrees under ' + WORKTREES_DIR + ' (git worktree prune + remove non plan-<slug> trees)')
  } else {
    const sweep = await agent(
      [
        'Sweep stale git worktrees for burn-the-board. Host: Windows, Git Bash. Run from ' + REPO_DIR + '.',
        '  1. git worktree prune',
        '  2. git worktree list --porcelain  to enumerate worktrees.',
        '  3. For every worktree under ' + WORKTREES_DIR + '/ whose directory name does NOT start with "plan-"',
        '     (i.e. it is a burn-*/drive-*/smoke-* leak from a crashed prior run, NEVER a live implement-plan plan-<slug> tree):',
        '       git worktree remove --force <path>   and, if a matching local branch exists (burn/* or drive/* or smoke/*), git branch -D it.',
        '     Leave any plan-<slug> worktree untouched (a concurrent implement-plan may own it).',
        'Return { removed: [paths removed], problems }.',
      ].join('\n'),
      { label: 'sweep:' + RUNID, phase: 'RunStart', schema: SWEEP_SCHEMA, model: FAST },
    )
    if (sweep && sweep.removed && sweep.removed.length) hb('swept ' + sweep.removed.length + ' stale worktree(s): ' + sweep.removed.join(', '))
  }

  // Step 4: minimal crash-reaper — recover issues orphaned in a Doing state (not blocked, not braked) by a
  // crashed prior run, with the recency guard. JS pre-filters candidates (pure label logic); the agent
  // applies the time-based guard (bash date) and the relabels (no Date.now() in JS).
  const open = await snapshotOpen()
  const reapCandidates = open
    .filter((i) => doingState(i.labels) !== null && !i.labels.includes('blocked') && !isBraked(i.labels))
    .map((i) => ({ issue: i.number, state: doingState(i.labels), updatedAt: i.updatedAt }))
  if (reapCandidates.length === 0) {
    log('reaper: no stale Doing-not-blocked candidates')
  } else if (DRY_RUN) {
    log('(dryRun) reaper candidates (recency guard applied live): ' + JSON.stringify(reapCandidates))
  } else {
    const reap = await agent(
      [
        'You are the run-start crash-reaper for burn-the-board (github.md § Queues vs Doing). Host: Windows, Git Bash.',
        'Below are stale-LOOKING Doing (not-blocked, not-braked) issues. Apply the RECENCY GUARD, then reap the rest.',
        'Candidates: ' + JSON.stringify(reapCandidates),
        '',
        'RECENCY GUARD: an issue whose updatedAt is within ' + GRACE_MS + ' ms of NOW is a LIVE run (a hand-run skill or a',
        '  concurrent driver), NOT a crash — SKIP it. Compute in bash:  now=$(date -u +%s) ; t=$(date -u -d "<updatedAt>" +%s) ;',
        '  if (( (now - t) * 1000 < ' + GRACE_MS + ' )) then it is recent -> skip.',
        'REAP the demonstrably-stale remainder by running, per issue, `bash .claude/scripts/set-status.sh <issue>',
        '  <new-status> [block|unblock]` (single-select label swap + the board card together, in one command):',
        '  - any NON-implementing *-ing  ->  its matching *-queued, UN-blocked',
        '      (brainstorming->brainstorm-queued, planning->plan-queued, spec-reviewing->spec-review-queued, plan-reviewing->plan-review-queued).',
        '  - implementing  ->  STAY status:implementing and ADD blocked (NEVER status:ready — a crashed implement may have',
        '      partially landed; only the operator may set ready after trimming the plan). Post a brief "## 🛑" note that it',
        '      was reaped after a crash and needs the operator to inspect what landed before retrying.',
        'Return { reaped: [{issue, from, to}], skippedRecent: [issue numbers skipped by the guard], problems }.',
      ].join('\n'),
      { label: 'reaper:' + RUNID, phase: 'RunStart', schema: REAP_SCHEMA, model: MID },
    )
    const reaped = (reap && reap.reaped) || []
    if (reaped.length) hb('reaped ' + reaped.length + ' stale Doing issue(s): ' + reaped.map((r) => '#' + r.issue + ' ' + r.from + '->' + r.to).join(', '))
    if (reap && reap.skippedRecent && reap.skippedRecent.length) log('reaper recency guard skipped (live): ' + reap.skippedRecent.join(', '))
  }

  return { skipped: false }
}

// ---------------------------------------------------------------------------
// The burn loop — a ROLLING worker pool of width N. Keep up to N issues advancing
// concurrently; the instant ANY one finishes, refill the freed slot from a FRESH
// proximity-sorted snapshot — rather than waiting for the whole batch to drain (a slow
// implement would otherwise leave the other N-1 slots idle until it finished). The
// just-completed advance is reflected in the refill snapshot, so an issue that moved to
// a nearer-to-done stage is re-prioritized and re-dispatched immediately. Terminates at
// the fixpoint: nothing actionable AND nothing in flight.
// ---------------------------------------------------------------------------
async function burn() {
  const inFlight = new Map()   // issue number -> Promise<number> (settles to the number, success OR failure)

  // nextActionable(slots) — ff-sync, re-snapshot, and return up to `slots` highest-priority actionable
  // issues NOT already in flight, closest-to-done first (oldest-updated tie-break). The fresh snapshot is
  // what makes the pool dynamic: it observes stages that just advanced / parked / closed an issue.
  async function nextActionable(slots) {
    if (slots <= 0) return []
    if (DRY_RUN) log('(dryRun) would ff-sync the primary checkout before refilling ' + slots + ' slot(s)')
    else await ffSyncPrimary()
    const open = await snapshotOpen()
    return open
      .filter((i) => burnActionable(i.labels) && !inFlight.has(i.number))
      .sort((a, b) => {
        const d = proximityOf(b.labels) - proximityOf(a.labels)   // closest-to-done first
        if (d !== 0) return d
        return a.updatedAt < b.updatedAt ? -1 : a.updatedAt > b.updatedAt ? 1 : 0  // tie-break: oldest first
      })
      .slice(0, slots)
  }

  // dispatch(issue) — start advancing one issue and register its slot. advanceOneStep isolates its own
  // failures (try/catch -> park + file bug), so the tracked promise NEVER rejects; it settles to the issue
  // number either way, so Promise.race below can name the finisher and free exactly its slot.
  function dispatch(issue) {
    report.rounds++   // now counts dispatches (one per advance attempt), not batch rounds
    hb('dispatch ' + report.rounds + ': #' + issue.number + '(' + topStatus(issue.labels) + ') [now ' + (inFlight.size + 1) + '/' + N + ' in flight]')
    inFlight.set(issue.number, advanceOneStep(issue).then(() => issue.number, () => issue.number))
  }

  // Initial fill — up to N closest-to-done issues.
  for (const issue of await nextActionable(N)) dispatch(issue)
  if (inFlight.size === 0) {
    report.fixpoint = true
    hb('nothing actionable — board already burned down (everything done or blocked)')
    return
  }

  // Rolling refill — every time ANY worker finishes, free its slot and top the pool back up to N from a
  // fresh snapshot. A finisher that produced new actionable work (advanced an issue a stage) is picked up
  // here; when no worker produces more work and the pool drains, inFlight empties and the loop exits.
  while (inFlight.size > 0 && report.rounds < MAX_ROUNDS) {
    const finished = await Promise.race(inFlight.values())
    inFlight.delete(finished)
    for (const issue of await nextActionable(N - inFlight.size)) dispatch(issue)
  }

  if (report.rounds >= MAX_ROUNDS && inFlight.size > 0) {
    hb('hit MAX_ROUNDS=' + MAX_ROUNDS + ' dispatches without burning down — stopping (investigate a non-advancing stage); draining ' + inFlight.size + ' in flight')
    await Promise.all(inFlight.values())   // never abandon stages mid-push
  } else {
    report.fixpoint = inFlight.size === 0
    hb('nothing actionable — board burned down (everything done or blocked) after ' + report.rounds + ' dispatch(es)')
  }
}

// advanceOneStep(issue) — dispatch on the highest-proximity *-queued / ready label to the matching skill,
// advancing the issue EXACTLY ONE stage. Per-unit try/catch isolation: a machinery exception files a bug
// AND parks the issue blocked (so the loop converges and never re-hammers a broken stage).
async function advanceOneStep(issue) {
  const n = issue.number
  const labels = issue.labels || []
  let stage = '?'; let skill = '?'
  try {
    if (labels.includes('status:ready')) { stage = 'implement'; skill = 'issue-implement'; return await runImplementStage(n) }
    if (labels.includes('status:plan-review-queued')) { stage = 'plan-review'; skill = 'issue-plan-review'; return await runReviewStage(n, 'plan') }
    if (labels.includes('status:plan-queued')) { stage = 'plan'; skill = 'issue-plan'; return await runAuthoringStage(n, 'plan') }
    if (labels.includes('status:spec-review-queued')) { stage = 'spec-review'; skill = 'issue-spec-review'; return await runReviewStage(n, 'spec') }
    if (labels.includes('status:brainstorm-queued')) { stage = 'brainstorm'; skill = 'issue-brainstorm'; return await runAuthoringStage(n, 'brainstorm') }
    log('advanceOneStep: #' + n + ' has no recognized actionable label, skipping')
  } catch (e) {
    narrate(n, stage + ' machinery failure: ' + sanitize(errText(e)))
    report.parked.push({ issue: n, fromStage: stage, toState: 'status:' + doingForLabels(labels) + '+blocked', outcome: 'machinery' })
    await handleMachineryFailure(n, stage, skill, e, doingForLabels(labels))
  }
}

// ---------------------------------------------------------------------------
// Authoring stages (brainstorm, plan) — ONE agent that reads+follows the skill + github.md, with the
// named deviation: the persist-draft step (commit+push to the primary checkout) is replaced by a per-issue
// worktree + canonical rebase-retry ff-push (so N concurrent stages never stomp the primary checkout); the
// brake branch is suppressed (the actionable filter already excludes braked issues).
// ---------------------------------------------------------------------------
async function runAuthoringStage(issue, kind) {
  const next = kind === 'brainstorm' ? 'spec-review-queued' : 'plan-review-queued'
  if (DRY_RUN) {
    narrate(issue, '(dryRun) would run ' + kind + ' authoring: worktree ' + WORKTREES_DIR + '/burn-' + RUNID + '-' + issue + ', draft under ' + (kind === 'brainstorm' ? 'specs' : 'plans') + '/drafts/{date-slug}.md, ff-push, advance -> ' + next + ' (or park blocked if questions)')
    report.advanced.push({ issue, fromStage: kind, toState: '(dryRun) ' + next, outcome: 'dryRun' })
    return
  }
  const skillFile = kind === 'brainstorm' ? '.claude/skills/issue-brainstorm/SKILL.md' : '.claude/skills/issue-plan/SKILL.md'
  const draftDir = kind === 'brainstorm' ? 'docs/superpowers/specs/drafts' : 'docs/superpowers/plans/drafts'
  const wt = WORKTREES_DIR + '/burn-' + RUNID + '-' + issue
  const br = 'burn/' + RUNID + '-' + issue
  const res = await agent(
    [
      'You are the ' + kind + ' authoring stage for issue #' + issue + ' in burn-the-board. Host: Windows, Git Bash.',
      'READ AND FOLLOW these in full, then execute the stage:',
      '  - ' + skillFile + '   (the per-stage source of truth)',
      '  - .claude/rules/github.md   (set_status, the comment templates, storage/promotion, § work-graph)',
      'Follow the skill EXACTLY, with these TWO named deviations (anchor to the skill markers, not step numbers):',
      '',
      'DEVIATION 1 — replace the skill PERSIST-DRAFT step (its "commit + push to main / primary checkout") with the',
      'per-issue worktree + canonical rebase-retry ff-push:',
      '  a. git fetch origin ; git worktree add ' + wt + ' -b ' + br + ' origin/main   (the unique burn-' + RUNID + '-' + issue + ' worktree path the run-start sweep can target),',
      '       (if ' + br + ' exists from a stale run: git worktree remove --force ' + wt + ' ; git branch -D ' + br + ' ; retry).',
      '  b. In that worktree, write the draft under ' + draftDir + '/{YYYY-MM-DD-slug}.md (date via bash date -u +%F);',
      '     immediately after the standard skill header add the line:  > **Tracking issue:** #' + issue,
      (kind === 'plan' ? '     and the next line:  > **Spec:** docs/superpowers/specs/{slug}.md   (the approved spec it derives from).' : '     (brainstorm/spec draft — no Spec header line).'),
      '  c. git add ONLY the draft file (' + draftDir + '/{YYYY-MM-DD-slug}.md — never -A/./-u/commit -a; github.md § Working-tree hygiene) ; git commit ; then the CANONICAL rebase-retry ff-push from the worktree:',
      '       repeat up to ' + MAX_ATTEMPTS + ':  git fetch origin && git rebase origin/main && git push origin HEAD:main',
      '       (abort+retry on conflict; retry on non-ff rejection).',
      '  d. Post the Spec/Plan comment (github.md template — its permalink needs the pushed commit), then advance with',
      '     `bash .claude/scripts/set-status.sh ' + issue + ' <new-status> [block]` (label + board card, one command):',
      '     brainstorm -> spec-review-queued on a drafted spec, OR brainstorming + block if you posted questions; plan -> plan-review-queued.',
      '  e. CLEANUP, success AND failure:  cd ' + REPO_DIR + ' ; git worktree remove --force ' + wt + ' ; git branch -D ' + br,
      '     (the run-start sweep is the crash backstop).',
      '',
      'DEVIATION 2 — SUPPRESS the skill brake branch (do not park on `manual`): the actionable filter already',
      'excludes braked issues, so this issue is not braked. Otherwise honor the skill, INCLUDING its claim of the Doing',
      'state (status:' + (kind === 'brainstorm' ? 'brainstorming' : 'planning') + '), its dialogue-handler / unaddressed-comment behavior, and its scope/split check (/issue-split).',
      '',
      'Report { outcome: "advanced" (drafted + advanced to the next queue) | "parked" (posted questions / hit the unaddressed-comment',
      'guard, set blocked) | "split" (ran /issue-split) | "error", toState (the status:* it now carries), draftPath, problems }.',
    ].join('\n'),
    { label: 'author:' + kind + ':' + issue, phase: 'Burn', schema: AUTHORING_SCHEMA },
  )
  const outcome = res ? res.outcome : 'error'
  if (outcome === 'advanced' || outcome === 'split') {
    report.advanced.push({ issue, fromStage: kind, toState: res.toState, outcome })
    narrate(issue, kind + ' ' + outcome + ' -> ' + res.toState)
  } else {
    report.parked.push({ issue, fromStage: kind, toState: res ? res.toState : 'unknown', outcome: outcome === 'parked' ? 'parked' : 'error' })
    narrate(issue, kind + ' ' + outcome + (res && res.problems ? ': ' + res.problems : ''))
    if (outcome === 'error') await handleMachineryFailure(issue, kind, kind === 'brainstorm' ? 'issue-brainstorm' : 'issue-plan', new Error(res ? res.problems : 'authoring agent returned no outcome'), kind === 'brainstorm' ? 'brainstorming' : 'planning')
  }
}

// ---------------------------------------------------------------------------
// Review stages (spec, plan) — the SCRIPT owns the issue-review workflow() call (the proven shape); agents
// do the entry guard + claim, then post the verdict verbatim and route per the skill, with the named
// deviation that any promote/demote git mv runs in a worktree + ff-push (concurrency-safe).
// ---------------------------------------------------------------------------
async function runReviewStage(issue, kind) {
  const reviewing = kind === 'spec' ? 'spec-reviewing' : 'plan-reviewing'
  if (DRY_RUN) {
    narrate(issue, '(dryRun) would claim status:' + reviewing + ', run issue-review, then post the verdict + route per the skill')
    report.advanced.push({ issue, fromStage: kind + '-review', toState: '(dryRun)', outcome: 'dryRun' })
    return
  }

  // 1. claim-agent: entry unaddressed-comment guard, else set_status *-reviewing (clears blocked).
  const claim = await agent(claimReviewPrompt(issue, reviewing), { label: 'claim:' + issue, phase: 'Burn', schema: CLAIM_REVIEW_SCHEMA })
  if (claim && claim.guarded) {
    report.parked.push({ issue, fromStage: kind + '-review', toState: 'status:' + reviewing + '+blocked', outcome: 'needs-you' })
    narrate(issue, kind + ' review entry-guard Needs-you -> parked blocked at ' + reviewing)
    return
  }

  // 2. the review leaf — SCRIPT-owned workflow() call (read-only on GitHub).
  const r = await workflow('issue-review', { issue, kind, roundLimit: K })
  if (!r || !r.verdict) {
    await handleMachineryFailure(issue, kind + '-review', 'issue-review', new Error('issue-review returned no verdict'), reviewing)
    return
  }
  narrate(issue, kind + ' review round ' + r.round + ' -> ' + r.verdict + ' (' + r.counts.critical + 'c/' + r.counts.high + 'h/' + r.counts.medium + 'm)')

  // 3. post + route — ONE agent posts the verdict VERBATIM, then routes per the skill's step-5 mechanics.
  const action = await agent(postAndRoutePrompt(issue, kind, reviewing, r), { label: 'route:' + issue, phase: 'Burn', schema: ROUTE_ACTION_SCHEMA })
  if (!action) {
    report.parked.push({ issue, fromStage: kind + '-review', toState: 'status:' + reviewing + '+blocked', outcome: 'machinery' })
    narrate(issue, kind + ' review route agent returned null -> parked blocked')
    await handleMachineryFailure(issue, kind + '-review', 'issue-' + kind + '-review', new Error('route agent returned null after verdict ' + r.verdict), reviewing)
    return
  }
  const toState = action.toState
  if (action.promoted) {
    report.advanced.push({ issue, fromStage: kind + '-review', toState, outcome: 'lgtm-promoted' })
    narrate(issue, kind + ' review LGTM -> promoted + advanced to ' + toState)
  } else if (toState.indexOf('blocked') !== -1 || (action && /reviewing/.test(toState))) {
    report.parked.push({ issue, fromStage: kind + '-review', toState, outcome: 'stuck-after-K' })
    narrate(issue, kind + ' review ' + r.verdict + ' -> parked ' + toState + ' (stuck after K=' + K + ')')
  } else {
    report.advanced.push({ issue, fromStage: kind + '-review', toState, outcome: 'requeued:' + r.verdict })
    narrate(issue, kind + ' review ' + r.verdict + ' -> requeued ' + toState + (action && action.demoted ? ' (demoted)' : ''))
  }
}

function claimReviewPrompt(issue, reviewing) {
  const gate = reviewing === 'spec-reviewing' ? 'spec' : 'plan'
  return [
    'Claim the ' + gate + ' review of issue #' + issue + ' for burn-the-board, applying the ENTRY unaddressed-comment guard',
    '(github.md § The unaddressed-comment guard). Host: Windows, Git Bash.',
    'First:  gh issue view ' + issue + ' --json labels,comments .',
    'ENTRY GUARD: every skill comment is an H2 ("## …"); any comment NOT starting with "##" is human. If a human comment sits',
    '  AFTER the last "##" skill comment AND is NOT a clear go-ahead/ack (a question/concern/caveat — conservatism rule), do NOT',
    '  review: run `bash .claude/scripts/set-status.sh ' + issue + ' ' + reviewing + ' block`, post the github.md',
    '  "## 🛑 Needs you — unaddressed comment" note (name the commenter + one-line gist), and return { guarded: true, claimed: false }.',
    'OTHERWISE claim it: run `bash .claude/scripts/set-status.sh ' + issue + ' ' + reviewing + ' unblock` (single-select label to',
    '  status:' + reviewing + ', clears blocked, AND the board card — one command) and return { guarded: false, claimed: true }.',
    'Post NO other comment. Return { guarded, claimed, problems }.',
  ].join('\n')
}
function postAndRoutePrompt(issue, kind, reviewing, r) {
  const skillFile = '.claude/skills/issue-' + kind + '-review/SKILL.md'
  const wt = WORKTREES_DIR + '/burn-' + RUNID + '-' + issue
  const br = 'burn/' + RUNID + '-' + issue
  const nextOnLgtm = kind === 'spec' ? 'plan-queued' : 'ready'
  const failQueue = kind === 'spec' ? 'brainstorm-queued' : (r.verdict === 'RETHINK' ? 'brainstorm-queued' : 'plan-queued')
  return [
    'Post the ' + kind + ' review verdict for issue #' + issue + ' and route it, for burn-the-board. Host: Windows, Git Bash.',
    'The review already ran (round ' + r.round + '/' + K + ', verdict ' + r.verdict + ').',
    '',
    'STEP A — POST THE VERDICT FIRST, VERBATIM. Use  gh issue comment ' + issue + ' --body-file <tmp>  (write the body to a temp',
    'file to preserve formatting exactly; do not edit/trim/re-wrap). The comment text:',
    '--- BEGIN COMMENT ---',
    r.comment,
    '--- END COMMENT ---',
    '',
    'STEP B — ROUTE. READ AND FOLLOW  ' + skillFile + '  step 5 (its routing mechanics are the SINGLE SOURCE) for verdict ' + r.verdict + ' at',
    'round ' + r.round + '/' + K + ', with TWO named deviations:',
    '  (1) IGNORE the `manual` brake branch — the actionable filter excludes braked issues, so this issue is not braked.',
    '  (2) For ANY promote/demote git mv, do it in a PER-ISSUE WORKTREE + canonical rebase-retry ff-push, NOT a commit on the',
    '      primary checkout (concurrency-safe): git fetch origin ; git worktree add ' + wt + ' -b ' + br + ' origin/main (if ' + br + ' exists:',
    '      git worktree remove --force ' + wt + ' ; git branch -D ' + br + ' ; retry) ; do the git mv there ; git add ONLY the renamed',
    '      paths (never -A/./-u/commit -a) ; git commit ; repeat up to ' + MAX_ATTEMPTS + ': git fetch origin && git rebase origin/main &&',
    '      git push origin HEAD:main (abort+retry on conflict; retry on non-ff) ; then cd ' + REPO_DIR + ' ; git worktree remove --force ' + wt + ' ;',
    '      git branch -D ' + br + '. The Spec/Plan comment-link edit and every set_status are gh/board ops — do them after the push.',
    'The branches (per the skill, do NOT re-derive — this is the gated outcome):',
    '  - LGTM -> PROMOTE the artifact (git mv draft -> top-level, edit the comment link) via the worktree ff-push above, then',
    '      `bash .claude/scripts/set-status.sh ' + issue + ' ' + nextOnLgtm + '` (no block). Set toState="status:' + nextOnLgtm + '", promoted=true, demoted=false.',
    '  - NEEDS WORK / RETHINK and round < ' + K + ' -> `bash .claude/scripts/set-status.sh ' + issue + ' ' + failQueue + ' unblock`; if a previously-promoted',
    '      top-level artifact exists, DEMOTE-ON-RE-ENTRY (git mv top-level -> drafts/ + fix the comment link) via the worktree ff-push.',
    '      Set toState="status:' + failQueue + '", promoted=false, demoted=<true only if you moved a file>.',
    '  - round >= ' + K + ' with a failing verdict -> stay status:' + reviewing + ', `bash .claude/scripts/set-status.sh ' + issue + ' ' + reviewing + ' block`,',
    '      append the "## 🛑 Stuck — needs you (after K rounds)" note. Set toState="status:' + reviewing + '+blocked", promoted=false, demoted=false.',
    'Return { toState, promoted, demoted, problems }.',
  ].join('\n')
}

// ---------------------------------------------------------------------------
// Implement stage (ready) — the SCRIPT owns the implement-plan workflow() call; agents do the entry guard +
// plan-resolution + claim, then the reconcile/close (or park). Mirrors issue-implement, minus the driver's
// snapshot-watermark commit-gate (burn-the-board relies on the entry guard, not an exit-side gate).
// ---------------------------------------------------------------------------
async function runImplementStage(issue) {
  if (DRY_RUN) {
    narrate(issue, '(dryRun) would resolve the promoted top-level plan, run implement-plan {merge}, then close on implemented+archived')
    report.advanced.push({ issue, fromStage: 'implement', toState: '(dryRun)', outcome: 'dryRun' })
    return
  }

  // 1. claim-agent: entry guard + resolve the promoted top-level plan path + claim implementing.
  const claim = await agent(implementClaimPrompt(issue), { label: 'impl-claim:' + issue, phase: 'Burn', schema: IMPL_CLAIM_SCHEMA })
  if (claim && claim.guarded) {
    report.parked.push({ issue, fromStage: 'implement', toState: 'status:implementing+blocked', outcome: 'needs-you' })
    narrate(issue, 'implement entry-guard Needs-you -> parked blocked')
    return
  }
  const planPath = claim && claim.planPath
  if (!planPath) {
    await parkBlocked(issue, 'implementing', 'status:ready but NO resolvable promoted top-level plan (docs/superpowers/plans/*.md with its > **Tracking issue:** #' + issue + ' header). Promote it via /issue-respond, or check the header.')
    report.parked.push({ issue, fromStage: 'implement', toState: 'status:implementing+blocked', outcome: 'unresolved' })
    narrate(issue, 'implement: no resolvable plan -> parked blocked')
    return
  }

  // 2. the implement leaf — SCRIPT-owned workflow() call, scoped to this one plan by path.
  const pr = await workflow('implement-plan', { plan: planPath, mode: 'merge' })
  const slug = (planPath.split('/').pop() || planPath).replace(/^\d{4}-\d{2}-\d{2}-/, '').replace(/\.md$/, '')
  const implementedArchived = !!(pr && pr.merged && pr.reason === 'merged + archived')
  const partialLand = !!(pr && !pr.merged && (pr.pushedTasks || 0) > 0)

  // 3. reconcile (issue-implement steps 5–6): close ONLY on implemented+archived.
  if (implementedArchived) {
    const recon = await agent(implementReconcilePrompt(issue, slug, pr), { label: 'impl-close:' + issue, phase: 'Burn', schema: IMPL_RECON_SCHEMA, model: MID })
    report.implemented.push(slug)
    if (recon && recon.closed) report.closed.push(issue)
    report.advanced.push({ issue, fromStage: 'implement', toState: 'done', outcome: 'implemented+closed' })
    narrate(issue, 'implemented + archived -> ' + (recon && recon.closed ? 'closed' : 'close attempted: ' + (recon ? recon.problems : 'null')))
  } else if (partialLand) {
    // Partial land = TERMINAL needs-human: a bare retry would write a failing test for code that already exists.
    await parkBlocked(issue, 'implementing',
      'PARTIAL LAND — implement-plan stopped after landing ' + (pr.pushedTasks || 0) + ' task increment(s) on main; it is NOT resumable. ' +
      'A bare retry would write a failing test for code that already exists (the BROKEN path — do NOT do it). ' +
      'Recover by trimming the promoted plan to the UN-LANDED tasks (or reverting the landed commits), THEN reply go-ahead. Problems: ' + (pr.problems || ''))
    report.parked.push({ issue, fromStage: 'implement', toState: 'status:implementing+blocked', outcome: 'partial-land' })
    narrate(issue, 'PARTIAL LAND (' + (pr.pushedTasks || 0) + ' increment(s)) -> terminal, parked implementing+blocked')
  } else {
    // Implement legitimately could not finish (work-failure): leave implementing+blocked with the error.
    await parkBlocked(issue, 'implementing', 'implement-plan did not complete: ' + (pr ? pr.reason + ' — ' + (pr.problems || '') : 'no plan result for ' + slug))
    report.parked.push({ issue, fromStage: 'implement', toState: 'status:implementing+blocked', outcome: pr ? pr.reason : 'no result' })
    narrate(issue, 'implement not completed (' + (pr ? pr.reason : 'no result') + ') -> parked implementing+blocked')
  }
}

function implementClaimPrompt(issue) {
  return [
    'Claim issue #' + issue + ' for implementation in burn-the-board, applying the ENTRY unaddressed-comment guard and',
    'resolving its promoted plan. Host: Windows, Git Bash. Mirrors issue-implement steps 1–3.',
    'First:  gh issue view ' + issue + ' --json labels,comments .',
    'ENTRY GUARD (github.md § The unaddressed-comment guard): every skill comment is an H2 ("## …"); any comment NOT starting',
    '  with "##" is human. If a human comment sits AFTER the last "##" skill comment AND is NOT a clear go-ahead/ack, do NOT',
    '  implement: run `bash .claude/scripts/set-status.sh ' + issue + ' implementing block`, post the github.md',
    '  "## 🛑 Needs you — unaddressed comment" note, and return { guarded: true, planPath: "", claimed: false }.',
    'RESOLVE THE PLAN: find the PROMOTED plan — the file directly under  docs/superpowers/plans/*.md  (top level ONLY, NOT',
    '  drafts/ or completed/) whose header has  > **Tracking issue:** #' + issue + '  . If none exists it is not actually promoted —',
    '  return { guarded: false, planPath: "", claimed: false } (do NOT claim).',
    'CLAIM: with a resolved planPath, run `bash .claude/scripts/set-status.sh ' + issue + ' implementing unblock` (label + board,',
    '  clears blocked) and post the durable breadcrumb comment  ## ⏳ implementing — pushing <slug>  (slug = filename minus the',
    '  YYYY-MM-DD- prefix and .md). Return { guarded: false, planPath: "<repo-relative path>", claimed: true }.',
    'Return { guarded, planPath, claimed, problems }.',
  ].join('\n')
}
function implementReconcilePrompt(issue, slug, pr) {
  const shas = pr && pr.commits && pr.commits.length ? pr.commits.join(' ') : '(see git log on main)'
  return [
    'Reconcile the implemented plan ' + slug + ' (issue #' + issue + ') for burn-the-board. Host: Windows, Git Bash.',
    'READ AND FOLLOW  .claude/skills/issue-implement/SKILL.md  steps 5–6 (the reconcile mechanics — the SINGLE SOURCE) for',
    'THIS one issue #' + issue + ': post the github.md Implement comment with the merged SHAs, then  gh issue close ' + issue + ' && bash',
    '.claude/scripts/set-status.sh ' + issue + ' done , then the parent rollup. The plan is ALREADY implemented + archived on main',
    '(implement-plan ran), so do steps 5–6 ONLY.',
    'The merged commit SHAs for the Implement comment: ' + shas + ' (auto-linking; one short SHA per change).',
    'Return { closed (did #' + issue + ' close), parentClosed, problems }.',
  ].join('\n')
}

// ---------------------------------------------------------------------------
// finalize() — heartbeat digest + durable run-log + structured return.
// ---------------------------------------------------------------------------
async function finalize() {
  const worked = report.advanced.length
  const parked = report.parked.length
  let digest
  if (report.preconditionSkipped) digest = 'precondition-skipped'
  else if (report.machineryErrors.length) digest = 'machinery-error x' + report.machineryErrors.length
  else if (worked > 0) digest = 'burned-' + worked
  else if (parked > 0) digest = 'parked-blocked-' + parked
  else digest = 'idle-nothing-actionable'
  hb('DONE ' + digest + ' (rounds=' + report.rounds + ', fixpoint=' + report.fixpoint + ', advanced=' + report.advanced.length + ', implemented=' + report.implemented.length + ', closed=' + report.closed.length + ', parked=' + report.parked.length + ', machinery=' + report.machineryErrors.length + ')')
  await appendRunLog(report)
  return report
}

// ---------------------------------------------------------------------------
// Top-level orchestration (RunStart -> Burn -> Report).
// ---------------------------------------------------------------------------
phase('RunStart')
const start = await runStart()
if (start.skipped) { report.preconditionSkipped = true; report.skipReason = start.reason; hb('precondition-skipped: ' + start.reason); return await finalize() }

phase('Burn')
try { await burn() }
catch (e) { hb('Burn loop aborted by an unexpected error: ' + sanitize(errText(e))); await handleMachineryFailure(0, 'burn-loop', 'burn-the-board', e) }

phase('Report')
return await finalize()

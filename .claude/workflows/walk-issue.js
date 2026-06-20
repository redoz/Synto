export const meta = {
  name: 'walk-issue',
  description:
    'Walk issue(s) through the issue-flow board to TERMINAL (closed/done OR blocked) — the one issue-flow loop. The primitive is walk(issue): advance an issue stage-by-stage via the matching issue-* skill (brainstorm -> spec-review -> plan -> plan-review -> implement -> close), re-reading its labels after each stage, until it is no longer actionable. A rolling worker pool of width N walks up to N issues concurrently and, the instant any walk finishes, refills the freed slot from a fresh proximity-sorted snapshot (closest-to-done first), to a fixpoint. SCOPE selects which issues the pool draws from: {scope:"all"} = every actionable open issue (this is /burn-the-board); {only:[n]} = just issue n (/walk-issue <n> on a unit); {epic:E} = the open children of epic E (/walk-issue <E>), rollup-closing E if all children end closed. ready->implement (default-ON) lands the plan and closes the issue. Carries NO snapshot-as-goal, NO reconcile, NO intake — it walks what is already actionable. Continuous via /loop /walk-issue or /loop /burn-the-board.',
  phases: [
    { title: 'RunStart', detail: 'infra probe + scope resolve; board/epic scope also: ff-sync + stale-worktree sweep + crash-reaper' },
    { title: 'Walk', detail: 'rolling worker pool of width N: walk each picked issue to terminal, refill each freed slot from a fresh snapshot, to fixpoint' },
    { title: 'Report', detail: 'heartbeat digest + durable run-log + structured return' },
  ],
}

// ─────────────────────────────────────────────────────────────────────────────────────────────────
// INLINED pure helpers — the Workflow runtime has NO module system (static import = syntax error;
// import() throws), so the small decision/scrub helpers are inlined here (this file is now their only
// home — the retired driver subsystem's lib/drive-fns.js is gone). walk-issue relies on each skill's
// own ENTRY unaddressed-comment guard, not an exit-side commit-gate, so it needs no route()/watermark.
// ─────────────────────────────────────────────────────────────────────────────────────────────────
const BRAKE_LABELS = ['manual']
const PHASE1_QUEUES = [
  'status:brainstorm-queued',
  'status:spec-review-queued',
  'status:plan-queued',
  'status:plan-review-queued',
]
// Proximity-to-done rank (higher = closer to leaving the board → walked off first). ready is one implement
// step from done, so it tops the order; brainstorm-queued is furthest. This ordering IS the whole policy.
const PROXIMITY = {
  'status:ready': 5,
  'status:plan-review-queued': 4,
  'status:plan-queued': 3,
  'status:spec-review-queued': 2,
  'status:brainstorm-queued': 1,
}
const SCRUB = [
  [/\b(NUGET_API_KEY|GITHUB_TOKEN|[A-Za-z0-9_]*(?:_TOKEN|_SECRET|_KEY))\b\s*[=:]\s*\S+/gi, '$1=<redacted>'],
  [/\bbearer\s+\S+/gi, 'bearer <redacted>'],
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
// args shim + config. The runtime forbids Date.now()/Math.random()/new Date() (they throw, to keep resume
// deterministic), so RUNID is passed in by the skill and every timestamp is generated inside an agent's
// Git Bash (date -u), never in JS.
// ---------------------------------------------------------------------------
let A = args || {}
if (typeof A === 'string') { try { A = JSON.parse(A) } catch { A = {} } }

const IMPLEMENT = A.implement !== false        // default true: ready->implement+close (takes items off the board)
const K = A.K || 5                              // per-stage review round limit (github.md § The round limit K)
const N = Math.max(1, A.N || 3)                 // max issues WALKED CONCURRENTLY (the rolling-pool width "N")
const MAX_ROUNDS = A.maxRounds || 200           // defensive bound on TOTAL walk dispatches (each walk drives ONE issue to
                                                //   terminal then leaves the actionable set, so this normally terminates well below the cap)
const MAX_WALK_STEPS = A.maxWalkSteps || (4 * K + 5)  // defensive per-walk stage cap (the K round-limit already bounds the review cycles)
const FF_PUSH_MAX_ATTEMPTS = 8                  // ff-push HEAD:main retry budget. The trunk is SHARED: up to N walks (and a
                                                //   co-running implement-plan) all rebase-retry ff-push to origin/main at once, so a
                                                //   small budget gets STARVED before it wins the race (this parked #143). Generous +
                                                //   an incremental backoff per attempt lets the competing push settle. Tunable.
const GRACE_MS = 15 * 60 * 1000                 // reaper recency guard: an issue updated < 15 min ago is a live run, not a crash
const WORKTREES_DIR = '.claude/worktrees'
const REPO_DIR = 'C:/dev/Synto'
const RUNID = A.runid || 'walk'                 // fresh per tick from the skill; deterministic fallback
const RUNLOG = '.claude/logs/walk-issue.jsonl'

// SCOPE — which issues the pool draws from (the ONLY difference between /walk-issue and /burn-the-board):
//   {scope:'all'} (default) — every actionable open issue (= /burn-the-board).
//   {only:[n,...]}          — exactly these issue numbers (= /walk-issue <n> on a unit; the skill passes N:1).
//   {epic:E}                — the open CHILDREN of epic E (= /walk-issue <E>), rollup-closing E at the end.
// A single only:[E] that turns out to carry the `epic` label is auto-promoted to epic mode (robustness — the
// skill normally passes {epic:E} directly). Resolved into SCOPE = { mode, inScope:Set<number>|null, epic } below.
const ONLY = Array.isArray(A.only) ? A.only.map(Number).filter((x) => Number.isInteger(x)) : null
const EPIC = A.epic != null && Number.isInteger(Number(A.epic)) ? Number(A.epic) : null
let SCOPE = { mode: 'all', inScope: null, epic: null }   // set by resolveScope() before the pool runs

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
const CHILDREN_SCHEMA = {
  type: 'object', additionalProperties: false,
  properties: { children: { type: 'array', items: { type: 'integer' } }, problems: { type: 'string' } },
  required: ['children', 'problems'],
}
const EPIC_ROLLUP_SCHEMA = {
  type: 'object', additionalProperties: false,
  properties: { closedEpic: { type: 'boolean' }, openChildren: { type: 'array', items: { type: 'integer' } }, problems: { type: 'string' } },
  required: ['closedEpic', 'problems'],
}
const ISSUE_STATE_SCHEMA = {
  type: 'object', additionalProperties: false,
  properties: { state: { type: 'string' }, labels: { type: 'array', items: { type: 'string' } } },
  required: ['state', 'labels'],
}
const ISSUE_STATE_FULL_SCHEMA = {
  type: 'object', additionalProperties: false,
  properties: { state: { type: 'string' }, labels: { type: 'array', items: { type: 'string' } }, updatedAt: { type: 'string' } },
  required: ['state', 'labels', 'updatedAt'],
}
const REAP_UNIT_SCHEMA = {
  type: 'object', additionalProperties: false,
  properties: { action: { type: 'string', enum: ['requeued', 'parked', 'skipped-live', 'noop'] }, toState: { type: 'string' }, problems: { type: 'string' } },
  required: ['action', 'toState', 'problems'],
}
const SUBSET_SCHEMA = {
  type: 'object', additionalProperties: false,
  properties: {
    dimensions: { type: 'array', items: { type: 'string' } },   // the dims to RUN ([] => full team)
    dropped: { type: 'array', items: { type: 'string' } },
    rationale: { type: 'string' },
    problems: { type: 'string' },
  },
  required: ['dimensions', 'dropped', 'rationale', 'problems'],
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
// proximityOf(labels) — the walk priority of an issue (its highest-ranked status label); 0 if none.
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
// inScope(n) — is issue n within the resolved SCOPE? (all => everything; only/epic => the resolved set.)
function inScope(n) { return SCOPE.inScope === null || SCOPE.inScope.has(n) }
function chunk(arr, n) { const out = []; for (let i = 0; i < arr.length; i += n) out.push(arr.slice(i, i + n)); return out }
function errText(e) { return (e && e.message) || String(e) }

// ---------------------------------------------------------------------------
// Logging helpers.
// ---------------------------------------------------------------------------
function hb(line) { log('[walk ' + RUNID + '] ' + line) }              // one-line liveness digest
function narrate(issue, line) { log('#' + issue + ' ' + line) }         // issue#-prefixed narrator

// appendRunLog(obj) — append one JSON line (with a bash-generated UTC timestamp) to the durable run-log.
// Observation, not a GitHub mutation. Best-effort: never aborts the run.
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
// NO recursion.
// ---------------------------------------------------------------------------
async function fileBug(skill, error) {
  const fp = fingerprint(skill, errText(error))
  const safe = sanitize(errText(error))
  try {
    const r = await agent(
      [
        'You are the self-healing reporter for walk-issue (github.md § Reporting a broken skill).',
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
        '  5. Sanitize ALWAYS — never put NUGET_API_KEY / GITHUB_TOKEN / any *_TOKEN / *_SECRET / *_KEY or raw payloads',
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
// leaves the actionable set (a human go-ahead, or a later re-run after un-blocking, recovers it). Best-effort.
async function parkBlocked(issue, status, message) {
  try {
    await agent(
      [
        'Park issue #' + issue + ' at status:' + status + ' + blocked — it is recoverable. Host: Windows, Git Bash.',
        'Run `bash .claude/scripts/set-status.sh ' + issue + ' ' + status + ' block` (single-select label + board card, sets blocked;',
        'do NOT close, do NOT strand un-blocked), then post a "## 🛑" note with this exact gist (telegraph English):',
        sanitize(message),
        'Return { parked, problems }.',
      ].join('\n'),
      { label: 'park:' + issue, phase: 'Walk', schema: PARK_SCHEMA, model: MID },
    )
  } catch (e) { log('parkBlocked #' + issue + ' failed (non-fatal): ' + sanitize(errText(e))) }
}

// handleMachineryFailure — file a de-duped bug AND park the issue blocked (so the loop converges).
async function handleMachineryFailure(issue, stage, skill, err, parkStatus) {
  const fp = fingerprint(skill, errText(err))
  report.machineryErrors.push({ issue, stage, fingerprint: fp })
  log('[classify ' + RUNID + '] ' + JSON.stringify({ skill, classification: 'machinery', fingerprint: fp }))
  if (issue && parkStatus) {
    try { await parkBlocked(issue, parkStatus, 'walk-issue ' + stage + ' machinery error: ' + sanitize(errText(err))) }
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
    { label: 'snapshot:' + RUNID, phase: 'Walk', schema: OPEN_SNAPSHOT_SCHEMA, model: FAST },
  )
  return (res && res.open) || []
}

// ffSyncPrimary() — re-fast-forward-sync the primary checkout before each refill, so a promotion/draft push
// never starts from a stale tree.
async function ffSyncPrimary() {
  const res = await agent(
    [
      'Fast-forward-sync the PRIMARY checkout before a walk-pool refill. Host: Windows, Git Bash. Run from ' + REPO_DIR + '.',
      '  git fetch origin',
      '  - HEAD == origin/main: fine.',
      '  - origin/main ancestor of HEAD (unpushed local commits): fine, leave them untouched.',
      '  - HEAD ancestor of origin/main (behind): git merge --ff-only origin/main.',
      '  - diverged: do NOT force anything; report ok=false, problems="primary checkout diverged from origin".',
      'Change no tracked files beyond the allowed fast-forward. Return { ok, problems }.',
    ].join('\n'),
    { label: 'ffsync:' + RUNID, phase: 'Walk', schema: FFSYNC_SCHEMA, model: FAST },
  )
  if (!res || !res.ok) hb('ff-sync warning: ' + (res ? res.problems : 'agent returned null'))
  return res
}

// issueLabels(n) — re-read ONE issue's current label NAMEs (cheap). Returns string[] for an OPEN issue, or
// null if it is CLOSED (terminal) or the read fails. walk() calls this after each stage to decide the next
// stage / terminal (a stage changes the labels). Read-only.
async function issueLabels(n) {
  const res = await agent(
    [
      'Read the current state of issue #' + n + '. Host: Windows, Git Bash. Read-only.',
      'Run:  gh issue view ' + n + ' --json state,labels',
      'Return { state ("OPEN" or "CLOSED"), labels (an array of label NAME strings, e.g. "status:ready") }. Modify nothing.',
    ].join('\n'),
    { label: 'relabel:' + n, phase: 'Walk', schema: ISSUE_STATE_SCHEMA, model: FAST },
  )
  if (!res || String(res.state).toUpperCase() === 'CLOSED') return null
  return res.labels || []
}

// issueState(n) — like issueLabels but ALSO returns updatedAt (for the unit reap's recency guard). Returns
// { labels, updatedAt } for an OPEN issue, or null if CLOSED or the read fails. Read-only.
async function issueState(n) {
  const res = await agent(
    [
      'Read the current state of issue #' + n + '. Host: Windows, Git Bash. Read-only.',
      'Run:  gh issue view ' + n + ' --json state,labels,updatedAt',
      'Return { state ("OPEN" or "CLOSED"), labels (array of label NAME strings, e.g. "status:ready"), updatedAt (ISO 8601 string) }. Modify nothing.',
    ].join('\n'),
    { label: 'read:' + n, phase: 'Walk', schema: ISSUE_STATE_FULL_SCHEMA, model: FAST },
  )
  if (!res || String(res.state).toUpperCase() === 'CLOSED') return null
  return { labels: res.labels || [], updatedAt: res.updatedAt || '' }
}

// resolveEpicChildren(epic) — the OPEN child issue numbers of an epic (github.md § Sub-issues & work graph).
// Read-only. Empty on no sub-issues / unavailable API (with a problems note).
async function resolveEpicChildren(epic) {
  const res = await agent(
    [
      'Resolve the OPEN child issues of epic #' + epic + ' for walk-issue. Host: Windows, Git Bash. Read-only.',
      'Epics use GitHub native sub-issues (github.md § Sub-issues & work graph). List the epic\'s children and keep',
      'only the OPEN ones. Try, in order, whatever works in this repo:',
      '  - gh sub-issue list ' + epic + '   (the gh-sub-issue extension), OR',
      '  - the GraphQL subIssues connection:  resolve OWNER/REPO via  gh repo view --json owner,name , then',
      '    gh api graphql -f query=\'{ repository(owner:"OWNER",name:"REPO"){ issue(number:' + epic + '){ subIssues(first:100){ nodes{ number state } } } } }\'',
      'Keep only state==OPEN. Return { children: [open child issue numbers], problems }. If the epic has no',
      'sub-issues or the API is unavailable, return children:[] with a short problems note (do NOT guess).',
    ].join('\n'),
    { label: 'epic-children:' + epic, phase: 'RunStart', schema: CHILDREN_SCHEMA, model: FAST },
  )
  return (res && res.children) || []
}

// resolveScope() — turn the scope args into { mode, inScope:Set<number>|null, epic:number|null, seed }, reading
// ONLY what it needs: a single only:[n] target is read DIRECTLY (issueLabels) to decide unit-vs-epic — NOT a
// whole-board snapshot — and those labels are returned in `seed` so the unit path need not re-read. (scope:'all'
// and explicit epic mode need no target read.) This is what lets the unit path skip the board-wide snapshot.
async function resolveScope() {
  let epicNum = EPIC
  const seed = {}
  if (epicNum == null && ONLY && ONLY.length === 1) {
    const st = await issueState(ONLY[0])                // direct read (labels + updatedAt), not a board snapshot
    if (st && st.labels.includes('epic')) epicNum = ONLY[0]
    else if (st) seed[ONLY[0]] = st                     // { labels, updatedAt } — used by the unit reap below
  }
  if (epicNum != null) {
    const kids = await resolveEpicChildren(epicNum)
    hb('scope: epic #' + epicNum + ' -> ' + (kids.length ? kids.length + ' open child(ren): ' + kids.join(',') : 'no open children'))
    return { mode: 'epic', inScope: new Set(kids), epic: epicNum, seed: {} }
  }
  if (ONLY && ONLY.length) { hb('scope: only ' + ONLY.join(',')); return { mode: 'only', inScope: new Set(ONLY), epic: null, seed } }
  hb('scope: all actionable open issues')
  return { mode: 'all', inScope: null, epic: null, seed: {} }
}

// rollupEpic(epic) — after the pool drains in epic mode: close the epic IFF every child ended closed, else
// leave it open. Best-effort (never aborts the run).
async function rollupEpic(epic) {
  try {
    const r = await agent(
      [
        'Roll up epic #' + epic + ' for walk-issue. Host: Windows, Git Bash. Its children were just walked.',
        'Re-resolve the epic\'s child issues and their state (gh sub-issue list ' + epic + ' or the GraphQL subIssues',
        'connection — github.md § Sub-issues & work graph).',
        'IF EVERY child is CLOSED: close the epic ->  gh issue close ' + epic + '  then  bash .claude/scripts/set-status.sh ' + epic + ' done',
        '  (an epic carries no status:*, but `done` is the close transition — it strips any label and moves the board card).',
        '  Post a brief "## ✅ Epic rolled up" comment (telegraph English: all children done).',
        'IF ANY child is still OPEN: do NOTHING to the epic — leave it open. Report its openChildren.',
        'Return { closedEpic, openChildren, problems }.',
      ].join('\n'),
      { label: 'epic-rollup:' + epic, phase: 'Report', schema: EPIC_ROLLUP_SCHEMA, model: MID },
    )
    if (r && r.closedEpic) { report.closed.push(epic); hb('epic #' + epic + ' rolled up -> closed (all children done)') }
    else hb('epic #' + epic + ' left open: ' + (r && r.openChildren && r.openChildren.length ? 'open children ' + r.openChildren.join(',') : (r ? r.problems : 'rollup agent returned null')))
  } catch (e) { hb('epic rollup #' + epic + ' failed (non-fatal): ' + sanitize(errText(e))) }
}

// ---------------------------------------------------------------------------
// Run-start: infra probe (BOTH paths) -> resolve SCOPE. For UNIT scope ({only:[n]}) that is ALL of run-start —
// it returns immediately and the issue is walked directly. For BOARD/EPIC scope it continues with the
// burn-the-board "Safe" machinery: ff-sync -> stale-worktree sweep -> minimal crash-reaper.
// NO reconcile and NO intake — walk-issue walks what is already actionable; un-parking blocked issues and
// stamping new ones are out of scope (a human un-blocks, and the next pass re-picks the issue).
// ---------------------------------------------------------------------------
async function runStart() {
  // Step 1: infra precondition probe — a deterministic SCRIPT (.claude/scripts/probe.sh). The agent RUNS it and
  // returns its single stdout JSON line; the script owns the checks (main fast-forward vs origin/main — the one
  // allowed mutation). A failure SKIPS the whole run. FAST model.
  const probeArg = IMPLEMENT ? '--implement' : '--no-implement'
  const probe = await agent(
    [
      'Run the infra precondition probe SCRIPT for walk-issue and return its result. Host: Windows, Git Bash.',
      'Work from the repo root (' + REPO_DIR + '). Run EXACTLY this one command:',
      '  bash .claude/scripts/probe.sh ' + probeArg,
      'The script prints human-readable progress to STDERR and EXACTLY ONE compact JSON line to STDOUT:',
      '  {"ok":true|false,"reason":"…"}',
      'It performs every check itself (and the single allowed fast-forward of main). Do NOT perform any check',
      'yourself, do NOT change any other file. Capture that one stdout JSON line and',
      'return it verbatim as { ok, reason }.',
    ].join('\n'),
    { label: 'probe:' + RUNID, phase: 'RunStart', schema: PROBE_SCHEMA, model: FAST },
  )
  if (!probe || !probe.ok) {
    const reason = probe ? probe.reason : 'probe agent returned null'
    hb('PRECONDITION FAILED — skipping run, NO issue touched. Blocker: ' + reason + '. Remediation: reconcile main with origin, then re-run.')
    return { skipped: true, reason }
  }

  // Step 2: resolve SCOPE up front with cheap targeted reads (resolveScope reads only the named target, never a
  // whole-board snapshot), so the UNIT path can skip every board-wide step that follows.
  const resolved = await resolveScope()
  SCOPE = { mode: resolved.mode, inScope: resolved.inScope, epic: resolved.epic }
  const seed = resolved.seed || {}

  // Unit scope ({only:[n]}) walks the named issue(s) DIRECTLY — none of the board-wide plumbing below applies:
  // ff-sync is done per-stage inside walk(); the stale-worktree sweep, board snapshot, and crash-reaper are
  // burn-the-board's concerns (a single-issue walk must not sweep the board or reap issues elsewhere).
  if (SCOPE.mode === 'only') return { skipped: false, seed }

  // ── Board / epic scope only (the burn-the-board machinery) ──────────────────────────────────────────────
  // Step 3: ff-sync the primary checkout (also done per-round; here it catches up before the sweep/reaper).
  await ffSyncPrimary()

  // Step 4: stale-worktree sweep — remove every .claude/worktrees/ tree that is not a live implement-plan
  // plan-<slug> tree (walk-* / burn-* / drive-* / smoke-* leaks from a crashed prior run). Safe under single-operator.
  const sweep = await agent(
    [
      'Sweep stale git worktrees for walk-issue. Host: Windows, Git Bash. Run from ' + REPO_DIR + '.',
      '  1. git worktree prune',
      '  2. git worktree list --porcelain  to enumerate worktrees.',
      '  3. For every worktree under ' + WORKTREES_DIR + '/ whose directory name does NOT start with "plan-"',
      '     (i.e. it is a walk-*/burn-*/drive-*/smoke-* leak from a crashed prior run, NEVER a live implement-plan plan-<slug> tree):',
      '       git worktree remove --force <path>   and, if a matching local branch exists (walk/* or burn/* or drive/* or smoke/*), git branch -D it.',
      '     Leave any plan-<slug> worktree untouched (a concurrent implement-plan may own it).',
      'Return { removed: [paths removed], problems }.',
    ].join('\n'),
    { label: 'sweep:' + RUNID, phase: 'RunStart', schema: SWEEP_SCHEMA, model: FAST },
  )
  if (sweep && sweep.removed && sweep.removed.length) hb('swept ' + sweep.removed.length + ' stale worktree(s): ' + sweep.removed.join(', '))

  // Step 5: minimal crash-reaper — recover issues orphaned in a Doing state (not blocked, not braked) by a
  // crashed prior run, with the recency guard. JS pre-filters candidates (pure label logic); the agent applies
  // the time-based guard (bash date) and the relabels (no Date.now() in JS). SCOPE is already resolved (Step 2),
  // so the reaper honors SCOPE.inScope just as before.
  const open = await snapshotOpen()
  const reapCandidates = open
    .filter((i) => doingState(i.labels) !== null && !i.labels.includes('blocked') && !isBraked(i.labels) && inScope(i.number))
    .map((i) => ({ issue: i.number, state: doingState(i.labels), updatedAt: i.updatedAt }))
  if (reapCandidates.length === 0) {
    log('reaper: no stale Doing-not-blocked candidates')
  } else {
    const reap = await agent(
      [
        'You are the run-start crash-reaper for walk-issue (github.md § Queues vs Doing). Host: Windows, Git Bash.',
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

  return { skipped: false, seed }
}

// ---------------------------------------------------------------------------
// walk(issue) — the PRIMITIVE: drive ONE issue from its current stage to TERMINAL (no longer actionable):
// closed/done, blocked, or split. Loops advanceOneStep, re-reading the issue's labels after each stage (a
// stage changes them), until burnActionable is false or the per-walk step cap is hit. Per-stage machinery
// failures are isolated INSIDE advanceOneStep (park + file bug), so a walk ENDS (the issue parks blocked,
// leaving the actionable set) rather than throwing. Returns the issue number either way.
// ---------------------------------------------------------------------------
async function walk(issue) {
  let cur = { number: issue.number, labels: issue.labels || [] }
  let steps = 0
  for (; steps < MAX_WALK_STEPS; steps++) {
    if (!burnActionable(cur.labels)) break          // terminal: closed / blocked / split / out of our control
    // ff-sync the primary checkout BEFORE each stage: every authoring/promotion/review stage reads or acts on the
    // primary checkout, which must reflect origin/main INCLUDING this walk's own prior-stage pushes (else a stage
    // reads a stale tree -> false "artifact missing" findings, e.g. a plan-review claiming the plan does not exist).
    await ffSyncPrimary()
    await advanceOneStep(cur)
    const fresh = await issueLabels(cur.number)      // re-read THIS issue (a stage changed its labels/state)
    if (fresh === null) break                         // CLOSED -> terminal (implemented+closed, or split-archived)
    cur = { number: cur.number, labels: fresh }
  }
  if (steps >= MAX_WALK_STEPS && burnActionable(cur.labels)) {
    // Defensive only — the K round-limit bounds the review cycles, so this should not be reached.
    narrate(cur.number, 'walk hit step cap (' + MAX_WALK_STEPS + ') still actionable (' + topStatus(cur.labels) + ') — leaving for the next pass')
  } else {
    narrate(cur.number, 'walk reached terminal (' + (cur.labels.length ? topStatus(cur.labels) : 'closed') + ') after ' + steps + ' stage(s)')
  }
  return cur.number
}

// ---------------------------------------------------------------------------
// The walk pool — a ROLLING worker pool of width N. Walk up to N issues to TERMINAL concurrently; the instant
// ANY walk finishes, free its slot and refill from a FRESH proximity-sorted snapshot — rather than waiting for
// the whole batch to drain (a long walk would otherwise idle the other N-1 slots). A walked issue ends
// non-actionable (closed/blocked), so it never re-enters the snapshot; the refill simply picks the next
// closest-to-done in-scope issue. Terminates at the fixpoint: nothing actionable in scope AND nothing in flight.
// ---------------------------------------------------------------------------
async function walkPool() {
  const inFlight = new Map()   // issue number -> Promise<number> (settles to the number, success OR failure)

  // nextActionable(slots) — ff-sync, re-snapshot, and return up to `slots` highest-priority IN-SCOPE actionable
  // issues NOT already in flight, closest-to-done first (oldest-updated tie-break). The fresh snapshot is what
  // makes the pool dynamic: it observes walks that just closed / parked an issue, and (in scope:'all') any new
  // actionable work, e.g. a split's children.
  async function nextActionable(slots) {
    if (slots <= 0) return []
    await ffSyncPrimary()
    const open = await snapshotOpen()
    return open
      .filter((i) => burnActionable(i.labels) && inScope(i.number) && !inFlight.has(i.number))
      .sort((a, b) => {
        const d = proximityOf(b.labels) - proximityOf(a.labels)   // closest-to-done first
        if (d !== 0) return d
        return a.updatedAt < b.updatedAt ? -1 : a.updatedAt > b.updatedAt ? 1 : 0  // tie-break: oldest first
      })
      .slice(0, slots)
  }

  // dispatch(issue) — start WALKING one issue to terminal and register its slot. walk()/advanceOneStep isolate
  // their own failures (try/catch -> park + file bug), so the tracked promise NEVER rejects; it settles to the
  // issue number either way, so Promise.race below can name the finisher and free exactly its slot.
  function dispatch(issue) {
    report.rounds++   // counts WALK dispatches (one per issue picked), not stage advances
    hb('dispatch ' + report.rounds + ': walk #' + issue.number + '(' + topStatus(issue.labels) + ') [now ' + (inFlight.size + 1) + '/' + N + ' in flight]')
    inFlight.set(issue.number, walk(issue).then(() => issue.number, () => issue.number))
  }

  // Initial fill — up to N closest-to-done in-scope issues.
  for (const issue of await nextActionable(N)) dispatch(issue)
  if (inFlight.size === 0) {
    report.fixpoint = true
    hb('nothing actionable in scope — already at the fixpoint (everything done or blocked)')
    return
  }

  // Rolling refill — every time ANY walk finishes, free its slot and top the pool back up to N from a fresh
  // snapshot. When no slot can be refilled (nothing actionable left in scope) and the pool drains, inFlight
  // empties and the loop exits.
  while (inFlight.size > 0 && report.rounds < MAX_ROUNDS) {
    const finished = await Promise.race(inFlight.values())
    inFlight.delete(finished)
    for (const issue of await nextActionable(N - inFlight.size)) dispatch(issue)
  }

  if (report.rounds >= MAX_ROUNDS && inFlight.size > 0) {
    hb('hit MAX_ROUNDS=' + MAX_ROUNDS + ' walk dispatches without reaching the fixpoint — stopping (investigate a non-advancing stage); draining ' + inFlight.size + ' in flight')
    await Promise.all(inFlight.values())   // never abandon walks mid-push
  } else {
    report.fixpoint = inFlight.size === 0
    hb('reached the fixpoint (everything done or blocked) after ' + report.rounds + ' walk dispatch(es)')
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
  const skillFile = kind === 'brainstorm' ? '.claude/skills/issue-brainstorm/SKILL.md' : '.claude/skills/issue-plan/SKILL.md'
  const draftDir = kind === 'brainstorm' ? 'docs/superpowers/specs/drafts' : 'docs/superpowers/plans/drafts'
  const wt = WORKTREES_DIR + '/walk-' + RUNID + '-' + issue
  const br = 'walk/' + RUNID + '-' + issue
  const res = await agent(
    [
      'You are the ' + kind + ' authoring stage for issue #' + issue + ' in walk-issue. Host: Windows, Git Bash.',
      'READ AND FOLLOW these in full, then execute the stage:',
      '  - ' + skillFile + '   (the per-stage source of truth)',
      '  - .claude/rules/github.md   (set_status, the comment templates, storage/promotion, § work-graph)',
      'WHEN REVISING after a failed review (this issue looped back from ' + (kind === 'brainstorm' ? 'spec' : 'plan') + ' review): the skill REQUIRES you to read the',
      '  LATEST "## ' + (kind === 'brainstorm' ? '🔬 Spec review' : '🔍 Plan review') + '" comment and treat its findings as a checklist — for EACH finding, either FIX it in the',
      '  revised ' + (kind === 'brainstorm' ? 'spec' : 'plan') + ' or briefly justify why it does not apply. Never draft over a review finding silently.',
      'Follow the skill EXACTLY, with these TWO named deviations (anchor to the skill markers, not step numbers):',
      '',
      'DEVIATION 1 — replace the skill PERSIST-DRAFT step (its "commit + push to main / primary checkout") with the',
      'per-issue worktree + canonical rebase-retry ff-push:',
      '  a. git fetch origin ; git worktree add ' + wt + ' -b ' + br + ' origin/main   (the unique walk-' + RUNID + '-' + issue + ' worktree path the run-start sweep can target),',
      '       (if ' + br + ' exists from a stale run: git worktree remove --force ' + wt + ' ; git branch -D ' + br + ' ; retry).',
      '  b. In that worktree, write the draft under ' + draftDir + '/{YYYY-MM-DD-slug}.md (date via bash date -u +%F);',
      '     immediately after the standard skill header add the line:  > **Tracking issue:** #' + issue,
      (kind === 'plan' ? '     and the next line:  > **Spec:** docs/superpowers/specs/{slug}.md   (the approved spec it derives from).' : '     (brainstorm/spec draft — no Spec header line).'),
      '  c. git add ONLY the draft file (' + draftDir + '/{YYYY-MM-DD-slug}.md — never -A/./-u/commit -a; github.md § Working-tree hygiene) ; git commit ; then the CANONICAL rebase-retry ff-push from the worktree:',
      '       repeat up to ' + FF_PUSH_MAX_ATTEMPTS + ':  git fetch origin && git rebase origin/main && git push origin HEAD:main',
      '       (abort+retry on conflict; on non-ff rejection wait a short GROWING backoff — ~2s,4s,6s... capped ~16s, e.g.',
      '       PowerShell Start-Sleep — so a concurrent walk\'s push to the shared trunk settles, then retry).',
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
    { label: 'author:' + kind + ':' + issue, phase: 'Walk', schema: AUTHORING_SCHEMA },
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

  // 1. claim-agent: entry unaddressed-comment guard, else set_status *-reviewing (clears blocked). Mechanical
  // (a yes/no entry guard + one set-status + the inlined Needs-you template) -> MID, not the session model.
  const claim = await agent(claimReviewPrompt(issue, reviewing), { label: 'claim:' + issue, phase: 'Walk', schema: CLAIM_REVIEW_SCHEMA, model: MID })
  if (claim && claim.guarded) {
    report.parked.push({ issue, fromStage: kind + '-review', toState: 'status:' + reviewing + '+blocked', outcome: 'needs-you' })
    narrate(issue, kind + ' review entry-guard Needs-you -> parked blocked at ' + reviewing)
    return
  }

  // 1.5 adaptive-rigor (review->subset knob, rigor.md § Heuristics): a MID agent reads the drafted artifact and
  // picks the floor-protected dimension subset, posting the `## ⚡ Rigor — {kind}-review: subset` comment when it
  // actually drops dimensions. The SCRIPT owns the issue-review call (a sub-agent can't), so this is exactly where
  // the chosen subset is FORWARDED. [] => full team; issue-review force-includes correctness regardless.
  const dimensions = await pickReviewSubset(issue, kind)

  // 2. the review leaf — SCRIPT-owned workflow() call (read-only on GitHub).
  const r = await workflow('issue-review', { issue, kind, roundLimit: K, dimensions })
  if (!r || !r.verdict) {
    await handleMachineryFailure(issue, kind + '-review', 'issue-review', new Error('issue-review returned no verdict'), reviewing)
    return
  }
  narrate(issue, kind + ' review round ' + r.round + ' -> ' + r.verdict + ' (' + r.counts.critical + 'c/' + r.counts.high + 'h/' + r.counts.medium + 'm)')

  // 3. post + route — ONE agent posts the verdict VERBATIM, then routes per the skill's Route step (incl. its adaptive-rigor branches: spec skip-plan, the implement tag).
  const action = await agent(postAndRoutePrompt(issue, kind, reviewing, r), { label: 'route:' + issue, phase: 'Walk', schema: ROUTE_ACTION_SCHEMA })
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

// pickReviewSubset(issue, kind) — adaptive-rigor review->subset knob (rigor.md § Heuristics). A MID agent reads
// the drafted artifact and returns the floor-protected dimension subset to RUN, posting one
// `## ⚡ Rigor — {kind}-review: subset` H2 comment ONLY when it actually drops dimensions (full rigor is the
// silent default). Returns the dimensions array forwarded to issue-review ([] => full team). Conservative: any
// failure or doubt => [] (full team) — more rigor, never less.
async function pickReviewSubset(issue, kind) {
  try {
    const res = await agent(pickSubsetPrompt(issue, kind), { label: 'subset:' + issue, phase: 'Walk', schema: SUBSET_SCHEMA, model: MID })
    const dims = res && Array.isArray(res.dimensions) ? res.dimensions : []
    const dropped = res && Array.isArray(res.dropped) ? res.dropped : []
    if (dims.length && dropped.length) narrate(issue, kind + ' review subset — running ' + dims.join(',') + '; dropped ' + dropped.join(',') + ' (floor: correctness)')
    else narrate(issue, kind + ' review — full team (no subset)')
    return dims
  } catch (e) {
    narrate(issue, kind + ' review subset pick failed (non-fatal) — full team: ' + sanitize(errText(e)))
    return []
  }
}
function pickSubsetPrompt(issue, kind) {
  const draftDir = kind === 'spec' ? 'docs/superpowers/specs/drafts' : 'docs/superpowers/plans/drafts'
  return [
    'Decide the ADAPTIVE-RIGOR review dimension subset for the ' + kind + ' of issue #' + issue + ' (walk-issue, the review->subset knob).',
    'Host: Windows, Git Bash. READ FIRST: .claude/rules/rigor.md (§ The knobs, § Heuristics, § The floor) and .claude/rules/standards.md',
    '(the 4 dimensions + 1 domain expert). Then find and read the drafted artifact under ' + draftDir + '/ linked from issue #' + issue,
    '  (the draft whose "> **Tracking issue:** #' + issue + '" header matches; if several, the newest by YYYY-MM-DD-slug).',
    '',
    'Pick the dimensions/experts the artifact CAN implicate, dropping ONLY those it CLEARLY cannot (rigor.md § Heuristics: e.g. a',
    'doc-only change drops performance/testability; a packaging/layering refactor keeps maintainability + principal-engineer).',
    'Valid keys: maintainability, correctness, performance, testability, consequences',
    '(principal-engineer).',
    'FLOOR (rigor.md § The floor): correctness is NEVER dropped — issue-review force-includes it regardless. When in',
    'doubt run the FULL team (return EVERY key, dropped:[]) — the cheap path is for the clearly-narrow change only (conservatism rule).',
    '',
    'IF you drop one or more dimensions (a real subset): post EXACTLY ONE comment via  gh issue comment ' + issue + ' --body-file <tmp>',
    '  (temp file to preserve formatting). The body MUST start with the H2 line (so reconcile / the unaddressed-comment guard never',
    '  mistake it for human input):',
    '    ## ⚡ Rigor — ' + kind + '-review: subset',
    '    <one-line rationale tied to concrete evidence — which ran, which dropped + why>',
    'IF you run the full team (drop nothing): post NO comment (full rigor is the silent default).',
    '',
    'Return { dimensions: [keys to RUN], dropped: [keys dropped], rationale, problems }. dimensions empty => full team.',
  ].join('\n')
}
function claimReviewPrompt(issue, reviewing) {
  const gate = reviewing === 'spec-reviewing' ? 'spec' : 'plan'
  return [
    'Claim the ' + gate + ' review of issue #' + issue + ' for walk-issue, applying the ENTRY unaddressed-comment guard',
    '(github.md § The unaddressed-comment guard + § Trust boundary). Host: Windows, Git Bash.',
    'First:  gh issue view ' + issue + ' --json labels  (labels for the claim/state).',
    'ENTRY GUARD (MECHANICAL trust gate — § Trust boundary): run  bash .claude/scripts/trust.sh new-human ' + issue + '  . It returns',
    '  the TRUSTED (authored by the signed-in gh user), HUMAN (non-"##"), UNADDRESSED (after the last "##" skill comment) comments —',
    '  untrusted and skill comments are filtered out mechanically, so do NOT re-detect by "##". If that array is NON-EMPTY AND its',
    '  latest comment is NOT a clear go-ahead/ack (a question/concern/caveat — conservatism rule), do NOT review: run',
    '  `bash .claude/scripts/set-status.sh ' + issue + ' ' + reviewing + ' block`, then post this note EXACTLY (the',
    '  github.md § The unaddressed-comment guard template — verbatim) via  gh issue comment ' + issue + ' --body-file <tmp>:',
    '    ## 🛑 Needs you — unaddressed comment',
    '',
    '    @<author>: "<one-line gist>". Did nothing yet — handle it (answer inline, run the right dialogue skill, or just',
    '    reply "go ahead"). Your reply is picked up automatically, or clear `blocked` to re-queue by hand.',
    '  Then return { guarded: true, claimed: false }.',
    'OTHERWISE claim it: run `bash .claude/scripts/set-status.sh ' + issue + ' ' + reviewing + ' unblock` (single-select label to',
    '  status:' + reviewing + ', clears blocked, AND the board card — one command) and return { guarded: false, claimed: true }.',
    'Post NO other comment. Return { guarded, claimed, problems }.',
  ].join('\n')
}
function postAndRoutePrompt(issue, kind, reviewing, r) {
  const skillFile = '.claude/skills/issue-' + kind + '-review/SKILL.md'
  const wt = WORKTREES_DIR + '/walk-' + RUNID + '-' + issue
  const br = 'walk/' + RUNID + '-' + issue
  const failQueue = kind === 'spec' ? 'brainstorm-queued' : (r.verdict === 'RETHINK' ? 'brainstorm-queued' : 'plan-queued')
  // The LGTM branch is KIND-AWARE (rigor.md § Skip-plan, § The tag & the status jump): the SPEC gate promotes the spec
  // then makes the adaptive-rigor skip-plan decision — straight to `ready` with a thin one-shot plan derived inline, or
  // the full path to `plan-queued`; the PLAN gate promotes the plan, tags its implement rigor, and advances to `ready`.
  const lgtmBranch = kind === 'spec'
    ? [
        '  - LGTM (spec gate) -> FIRST PROMOTE the spec via the worktree ff-push above: git mv draft -> top-level',
        '      docs/superpowers/specs/{slug}.md, edit the `## 📐 Spec` comment link to the promoted path. THEN apply',
        '      issue-spec-review\'s Route-step skip-plan decision (rigor.md § Skip-plan, § Heuristics):',
        '      * SKIP-PLAN when the spec is FULLY diff-reviewable — no schema/migration, no new module, no multi-step',
        '        sequencing, no non-obvious TDD structure: in the SAME worktree derive a thin one-shot plan inline — write',
        '        top-level docs/superpowers/plans/{YYYY-MM-DD-slug}.md (the SAME slug as the spec) whose header, immediately',
        '        after the writing-plans header, carries the three lines  > **Tracking issue:** #' + issue + '  ,  > **Spec:**',
        '        docs/superpowers/specs/{slug}.md  , and  > **Rigor:** one-shot  plus a short change description (it MAY have',
        '        NO ### Task sections — implement-plan\'s one-shot mode reads the whole plan as the change). git add ONLY that',
        '        plan file (never -A/./-u/commit -a) ; git commit ; ff-push ; edit the `## 📐 Spec` comment to note the derived',
        '        plan path ; then `bash .claude/scripts/set-status.sh ' + issue + ' ready` (no block) ; post a',
        '        "## ⚡ Rigor — spec-review: skip-plan" H2 comment (one-line rationale). Set toState="status:ready", promoted=true, demoted=false.',
        '        UPSHIFT: if while deriving you find it actually needs decomposition / schema / multi-step sequencing, do NOT',
        '        write the thin plan — `bash .claude/scripts/set-status.sh ' + issue + ' plan-queued` and post "## ⚡ Rigor —',
        '        spec-review: upshift". Set toState="status:plan-queued", promoted=true, demoted=false.',
        '      * ELSE (full path) -> `bash .claude/scripts/set-status.sh ' + issue + ' plan-queued` (no block). Set toState="status:plan-queued", promoted=true, demoted=false.',
      ]
    : [
        '  - LGTM (plan gate) -> PROMOTE the plan via the worktree ff-push above: git mv draft -> top-level',
        '      docs/superpowers/plans/{slug}.md, edit the `## 📋 Plan` comment link, and TAG its implement rigor per',
        '      issue-plan-review\'s Route step — set the header line  > **Rigor:** one-shot  when one-shot-eligible (single',
        '      concern, small, diff-reviewable, no new logic branches wanting test-first design), else ensure NO',
        '      "> **Rigor:** one-shot" line remains (absent ⇒ tdd-per-task). Then `bash .claude/scripts/set-status.sh ' + issue + ' ready`',
        '      (no block); if one-shot, post one "## ⚡ Rigor — plan-review: one-shot" comment. Set toState="status:ready", promoted=true, demoted=false.',
      ]
  return [
    'Post the ' + kind + ' review verdict for issue #' + issue + ' and route it, for walk-issue. Host: Windows, Git Bash.',
    'The review already ran (round ' + r.round + '/' + K + ', verdict ' + r.verdict + ').',
    '',
    'STEP A — POST THE VERDICT FIRST, VERBATIM. Use  gh issue comment ' + issue + ' --body-file <tmp>  (write the body to a temp',
    'file to preserve formatting exactly; do not edit/trim/re-wrap). The comment text:',
    '--- BEGIN COMMENT ---',
    r.comment,
    '--- END COMMENT ---',
    '',
    'STEP B — ROUTE. READ AND FOLLOW  ' + skillFile + '\'s Route step (its § Approval-detection routing — the SINGLE SOURCE,',
    'INCLUDING its adaptive-rigor branches) for verdict ' + r.verdict + ' at round ' + r.round + '/' + K + ', with TWO named deviations:',
    '  (1) IGNORE the `manual` brake branch — the actionable filter excludes braked issues, so this issue is not braked.',
    '  (2) For ANY git write this route makes — a promote/demote git mv, AND (spec skip-plan) the thin one-shot plan file —',
    '      do it in a PER-ISSUE WORKTREE + canonical rebase-retry ff-push, NOT a commit on the primary checkout',
    '      (concurrency-safe): git fetch origin ; git worktree add ' + wt + ' -b ' + br + ' origin/main (if ' + br + ' exists:',
    '      git worktree remove --force ' + wt + ' ; git branch -D ' + br + ' ; retry) ; make the change(s) there ; git add ONLY the',
    '      renamed/written paths (never -A/./-u/commit -a) ; git commit ; repeat up to ' + FF_PUSH_MAX_ATTEMPTS + ': git fetch origin && git rebase',
    '      origin/main && git push origin HEAD:main (abort+retry on conflict; on non-ff rejection wait a short GROWING backoff',
    '      ~2s,4s,6s... capped ~16s, e.g. PowerShell Start-Sleep, so a concurrent walk\'s push to the shared trunk settles, then retry). The spec skip-plan path makes TWO',
    '      path-scoped commits in this SAME worktree (promote the spec, then write the thin plan), pushing after each. CLEAN UP',
    '      LAST, after this route\'s final push: cd ' + REPO_DIR + ' ; git worktree remove --force ' + wt + ' ; git branch -D ' + br + '.',
    '      The Spec/Plan comment-link edits and every set_status are gh/board ops — do them after the push(es).',
    'The branches — route the GATED verdict (do not re-litigate the verdict itself), but DO make the skill\'s adaptive-rigor',
    'sub-decisions (the spec skip-plan; the plan implement tag) per its Route step:',
    ...lgtmBranch,
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
// snapshot-watermark commit-gate (walk-issue relies on the entry guard, not an exit-side gate).
// ---------------------------------------------------------------------------
async function runImplementStage(issue) {
  // 1. claim-agent: entry guard + resolve the promoted top-level plan path + claim implementing.
  const claim = await agent(implementClaimPrompt(issue), { label: 'impl-claim:' + issue, phase: 'Walk', schema: IMPL_CLAIM_SCHEMA })
  if (claim && claim.guarded) {
    report.parked.push({ issue, fromStage: 'implement', toState: 'status:implementing+blocked', outcome: 'needs-you' })
    narrate(issue, 'implement entry-guard Needs-you -> parked blocked')
    return
  }
  const planPath = claim && claim.planPath
  if (!planPath) {
    await parkBlocked(issue, 'implementing', 'status:ready but NO resolvable promoted top-level plan on origin/main (docs/superpowers/plans/*.md with its > **Tracking issue:** #' + issue + ' header). Promote it via /issue-respond, or check the header.')
    report.parked.push({ issue, fromStage: 'implement', toState: 'status:implementing+blocked', outcome: 'unresolved' })
    narrate(issue, 'implement: no resolvable plan -> parked blocked')
    return
  }

  // 2. the implement leaf — SCRIPT-owned workflow() call, scoped to this one plan by path.
  const pr = await workflow('implement-plan', { plan: planPath, mode: 'merge' })
  const slug = (planPath.split('/').pop() || planPath).replace(/^\d{4}-\d{2}-\d{2}-/, '').replace(/\.md$/, '')
  // Close-gate (defense-in-depth, paired with implement-plan's reconcileDispositions): close ONLY
  // when implement-plan ATTESTS every task is accounted (landed / already-satisfied / ride-along
  // carried). A merged+archived result that fails to attest (tasksTotal null) or under-accounts does
  // NOT close — it falls through to a safe park rather than closing an issue with un-landed work.
  const implementedArchived = !!(
    pr && pr.merged && pr.reason === 'merged + archived'
    && pr.tasksTotal != null && pr.tasksAccounted === pr.tasksTotal
  )
  const partialLand = !!(pr && !pr.merged && (pr.pushedTasks || 0) > 0)

  // 3. reconcile (issue-implement steps 5–6): close ONLY on implemented+archived.
  if (implementedArchived) {
    const recon = await agent(implementReconcilePrompt(issue, slug, pr), { label: 'impl-close:' + issue, phase: 'Walk', schema: IMPL_RECON_SCHEMA, model: MID })
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
    'Claim issue #' + issue + ' for implementation in walk-issue, applying the ENTRY unaddressed-comment guard and',
    'resolving its promoted plan. Host: Windows, Git Bash. Mirrors issue-implement steps 1–3.',
    'First:  gh issue view ' + issue + ' --json labels  (labels for the claim/state).',
    'ENTRY GUARD (github.md § The unaddressed-comment guard + § Trust boundary — MECHANICAL trust gate): run',
    '  bash .claude/scripts/trust.sh new-human ' + issue + '  . It returns the TRUSTED, HUMAN (non-"##"), UNADDRESSED comments',
    '  (untrusted/skill comments already filtered out — do NOT re-detect by "##"). If that array is NON-EMPTY AND its latest',
    '  comment is NOT a clear go-ahead/ack, do NOT implement: run `bash .claude/scripts/set-status.sh ' + issue + ' implementing block`,',
    '  post the github.md "## 🛑 Needs you — unaddressed comment" note, and return { guarded: true, planPath: "", claimed: false }.',
    'RESOLVE THE PLAN against origin/main, NOT the local working tree (the operator\'s primary checkout may be STALE —',
    '  behind origin/main — so a glob of the on-disk plans/ FALSE-NEGATIVES a plan promoted only on origin/main, the exact',
    '  bug this guards). First:  git fetch origin -q . Enumerate the PROMOTED candidates (top level ONLY, NOT drafts/ or',
    '  completed/) from origin/main with git plumbing, not the disk:',
    '    git ls-tree -r --name-only origin/main -- docs/superpowers/plans/ | grep -E "^docs/superpowers/plans/[^/]+\\.md$"',
    '  For each candidate, read its header off origin/main (NOT disk):  git show origin/main:<path>  — and pick the one whose',
    '  header carries the line  > **Tracking issue:** #' + issue + '  (newest by the YYYY-MM-DD- filename prefix if several match).',
    '  If NO candidate on origin/main has that header, it is genuinely not promoted — return { guarded: false, planPath: "",',
    '  claimed: false } (do NOT claim). Otherwise set planPath to that repo-relative path (implement-plan bases its worktree',
    '  off origin/main, so the file resolves there).',
    'CLAIM: with a resolved planPath, run `bash .claude/scripts/set-status.sh ' + issue + ' implementing unblock` (label + board,',
    '  clears blocked) and post the durable breadcrumb comment  ## ⏳ implementing — pushing <slug>  (slug = filename minus the',
    '  YYYY-MM-DD- prefix and .md). Return { guarded: false, planPath: "<repo-relative path>", claimed: true }.',
    'Return { guarded, planPath, claimed, problems }.',
  ].join('\n')
}
function implementReconcilePrompt(issue, slug, pr) {
  const shas = pr && pr.commits && pr.commits.length ? pr.commits.join(' ') : '(see git log on main)'
  return [
    'Reconcile the implemented plan ' + slug + ' (issue #' + issue + ') for walk-issue. Host: Windows, Git Bash.',
    'READ AND FOLLOW  .claude/skills/issue-implement/SKILL.md  steps 5–6 (the reconcile mechanics — the SINGLE SOURCE) for',
    'THIS one issue #' + issue + ': post the github.md Implement comment with the merged SHAs, then  gh issue close ' + issue + ' && bash',
    '.claude/scripts/set-status.sh ' + issue + ' done , then the parent rollup. The plan is ALREADY implemented + archived on main',
    '(implement-plan ran), so do steps 5–6 ONLY.',
    'The merged commit SHAs for the Implement comment: ' + shas + ' (auto-linking; one short SHA per change).',
    'Return { closed (did #' + issue + ' close), parentClosed, problems }.',
  ].join('\n')
}

// reapUnitPrompt — single-issue crash-reap for a unit walk (the one-issue analogue of the whole-board reaper in
// runStart). Same rules: recency guard, non-implementing *-ing -> its *-queued un-blocked, implementing -> stay
// implementing + blocked (never ready — a crashed implement may have partially landed). Time math in bash only.
function reapUnitPrompt(issue, labels, updatedAt) {
  const ds = doingState(labels)
  return [
    'Reap the single crash-orphan issue #' + issue + ' for a unit walk (the one-issue analogue of the whole-board',
    'crash-reaper — github.md § Queues vs Doing). Host: Windows, Git Bash. The issue is in Doing state status:' + ds + '.',
    'updatedAt = ' + updatedAt,
    'RECENCY GUARD: if updatedAt is within ' + GRACE_MS + ' ms of NOW it is a LIVE run, NOT a crash — do NOTHING and return',
    '  { action: "skipped-live", toState: "status:' + ds + '" }. Compute in bash:  now=$(date -u +%s) ; t=$(date -u -d "' + updatedAt + '" +%s) ;',
    '  if (( (now - t) * 1000 < ' + GRACE_MS + ' )) it is recent -> skipped-live.',
    'OTHERWISE reap via  bash .claude/scripts/set-status.sh ' + issue + ' <new-status> [block|unblock]  (label + board card, one command):',
    '  - status:' + ds + ' is a NON-implementing *-ing  ->  its matching *-queued, UN-blocked (brainstorming->brainstorm-queued,',
    '      planning->plan-queued, spec-reviewing->spec-review-queued, plan-reviewing->plan-review-queued). Return',
    '      { action: "requeued", toState: "status:<that-queue>" }.',
    '  - status:implementing  ->  STAY status:implementing and ADD blocked (NEVER status:ready — a crashed implement may have',
    '      partially landed; only the operator may set ready after trimming the plan). Post a brief "## 🛑" note that it was',
    '      reaped after a crash and the operator must inspect what landed before retrying. Return { action: "parked",',
    '      toState: "status:implementing+blocked" }.',
    'Return { action, toState, problems }.',
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
  else if (worked > 0) digest = 'walked-' + worked
  else if (parked > 0) digest = 'parked-blocked-' + parked
  else digest = 'idle-nothing-actionable'
  hb('DONE ' + digest + ' (rounds=' + report.rounds + ', fixpoint=' + report.fixpoint + ', advanced=' + report.advanced.length + ', implemented=' + report.implemented.length + ', closed=' + report.closed.length + ', parked=' + report.parked.length + ', machinery=' + report.machineryErrors.length + ')')
  await appendRunLog(report)
  return report
}

// ---------------------------------------------------------------------------
// Top-level orchestration (RunStart -> Walk -> Report).
// ---------------------------------------------------------------------------
phase('RunStart')
const start = await runStart()
if (start.skipped) { report.preconditionSkipped = true; report.skipReason = start.reason; hb('precondition-skipped: ' + start.reason); return await finalize() }

phase('Walk')
if (SCOPE.mode === 'only') {
  // Unit path — walk each named issue DIRECTLY to terminal. NO pool, NO board snapshot/sweep/reaper, NO
  // per-refill ff-sync: walk() does its own per-stage ff-sync and isolates per-stage failures (park + file
  // bug), so a single bad issue never aborts the others. This is what /walk-issue <n> now costs — just the walk.
  for (const n of [...SCOPE.inScope]) {
    report.rounds++
    try {
      const st = (start.seed && start.seed[n]) || await issueState(n)
      if (!st) { narrate(n, 'already closed — nothing to walk'); continue }
      let labels = st.labels
      // Reap JUST this issue if it is a crash-orphan (a stale, un-blocked, un-braked Doing state) — the unit
      // analogue of burn-the-board's whole-board reaper, scoped to the one issue we were asked to walk. PURE
      // pre-check (doingState) first => zero agents for the normal queued/ready case.
      if (doingState(labels) && !labels.includes('blocked') && !isBraked(labels)) {
        const ds = doingState(labels)
        const reap = await agent(reapUnitPrompt(n, labels, st.updatedAt), { label: 'reap:' + n, phase: 'Walk', schema: REAP_UNIT_SCHEMA, model: MID })
        if (!reap || reap.action === 'parked') {
          report.parked.push({ issue: n, fromStage: 'reap', toState: reap ? reap.toState : 'status:implementing+blocked', outcome: 'crash-orphan-parked' })
          narrate(n, 'crash-orphan (' + ds + ') -> parked ' + (reap ? reap.toState : 'implementing+blocked'))
          continue
        }
        if (reap.action === 'skipped-live') { narrate(n, 'in ' + ds + ' but updated <15m ago — looks like a live run, not reaping'); continue }
        if (reap.action === 'noop') { narrate(n, 'reap noop (' + ds + ') — nothing to walk'); continue }
        labels = (await issueLabels(n)) || []   // requeued: re-read the now-queued labels
        narrate(n, 'crash-orphan (' + ds + ') reaped -> ' + reap.toState)
      }
      if (!burnActionable(labels)) { narrate(n, 'nothing actionable (' + (labels.length ? (doingState(labels) || topStatus(labels)) : 'no status') + ') — nothing to walk'); continue }
      hb('walk #' + n + '(' + topStatus(labels) + ') [unit, not pooled]')
      await walk({ number: n, labels })
    } catch (e) {
      hb('unit walk #' + n + ' aborted by an unexpected error: ' + sanitize(errText(e)))
      await handleMachineryFailure(n, 'unit-walk', 'walk-issue', e, doingForLabels((start.seed && start.seed[n] && start.seed[n].labels) || []))
    }
  }
  report.fixpoint = true
} else {
  try { await walkPool() }
  catch (e) { hb('Walk pool aborted by an unexpected error: ' + sanitize(errText(e))); await handleMachineryFailure(0, 'walk-pool', 'walk-issue', e) }
}

phase('Report')
// Epic scope: after the pool drains, rollup-close the epic iff every child ended closed (else leave it open).
if (SCOPE.mode === 'epic' && SCOPE.epic != null) await rollupEpic(SCOPE.epic)
return await finalize()

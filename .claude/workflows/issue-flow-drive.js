export const meta = {
  name: 'issue-flow-drive',
  description:
    'The autonomous issue-flow driver (the composing workflow github.md anticipates): one background run that drains the issue-flow board to a fixpoint. Run-start (snapshot ready + infra probe + stale-worktree sweep + recency-guarded reaper + intake), a reconcile pre-pass (intake + default-routed blocked-issue router), a Phase-1 drain of the authoring/review stages (brainstorm/spec-review/plan/plan-review) to fixpoint, then by default a Phase-2 implement of human-approved status:ready plans (implement-plan {plan:<path>}, per-plan) that closes their issues. Reads labels (source of truth), pulls only from queues, stops only where a human is genuinely required. Continuous via /loop /issue-drive.',
  phases: [
    { title: 'RunStart', detail: 'snapshot ready + infra probe + worktree sweep + recency-guarded reaper + intake' },
    { title: 'Reconcile', detail: 'intake + default-routed blocked-issue input router' },
    { title: 'Phase1', detail: 'drain authoring + review stages to fixpoint' },
    { title: 'Phase2', detail: 'implement approved status:ready plans (default-ON), per-plan' },
    { title: 'Report', detail: 'heartbeat digest + durable run-log + structured return' },
  ],
}

// ─────────────────────────────────────────────────────────────────────────────────────────────────
// INLINED — keep in sync with issue-flow-smoke.js (gated copy) and lib/drive-fns.js (canonical).
// The Workflow runtime has NO module system (static import = syntax error; import() throws). These are
// the pure decision functions that authorize push/promote/close; the smoke harness's assertion tables
// (== this exact logic) are their regression gate (spec §13).
// ─────────────────────────────────────────────────────────────────────────────────────────────────
const BRAKE_LABELS = ['manual']
const PHASE1_QUEUES = [
  'status:brainstorm-queued',
  'status:spec-review-queued',
  'status:plan-queued',
  'status:plan-review-queued',
]
function collectorPredicate(labels) {
  const L = labels || []
  if (BRAKE_LABELS.some((b) => L.includes(b))) return false
  if (L.includes('blocked')) return false
  if (L.includes('epic')) return false
  return PHASE1_QUEUES.some((q) => L.includes(q))
}
function route(verdict, kind, round, K) {
  // LGTM auto-advances (promote + advance): spec -> plan-queued, plan -> ready. `manual` is the only human
  // gate and is excluded by the collector, so route() never sees a braked issue. Checked before the round
  // limit so an LGTM at round K promotes, not parks-stuck. (Keep in sync with lib/drive-fns.js + smoke.)
  if (verdict === 'LGTM') return { stay: false, block: false, requeueTo: kind === 'spec' ? 'plan-queued' : 'ready', stuck: false, promote: true }
  if (round >= K) return { stay: true, block: true, requeueTo: null, stuck: true, promote: false }
  if (kind === 'spec') return { stay: false, block: false, requeueTo: 'brainstorm-queued', stuck: false, promote: false }
  const target = verdict === 'RETHINK' ? 'brainstorm-queued' : 'plan-queued'
  return { stay: false, block: false, requeueTo: target, stuck: false, promote: false }
}
const EPOCH = { t: '1970-01-01T00:00:00.000Z', url: '' }
function afterMark(c, mark) {
  if (c.t > mark.t) return true
  if (c.t < mark.t) return false
  return (c.url || '') > (mark.url || '')
}
function norm(comments) {
  return (comments || []).map((c) => ({ t: c.createdAt, url: c.url || '', body: c.body || '' }))
}
function computeWatermark(comments) {
  const cs = norm(comments)
  if (cs.length === 0) return { ...EPOCH }
  return cs.reduce((m, c) => (afterMark(c, m) ? { t: c.t, url: c.url } : m), { ...EPOCH })
}
function isHuman(c) { return !/^\s*##/.test(c.body || '') }
function watermarkAfter(comments, mark) {
  return norm(comments).filter(isHuman).filter((c) => afterMark(c, mark))
}
function resolveReadyAllowList(readyIssues, plansIndex) {
  const resolved = [], unresolved = [], braked = []
  for (const it of readyIssues || []) {
    const L = it.labels || []
    if (BRAKE_LABELS.some((b) => L.includes(b))) { braked.push(it.number); continue }
    const plan = (plansIndex || []).find((p) => p.trackingIssue === it.number)
    if (plan) resolved.push({ issue: it.number, slug: plan.slug })
    else unresolved.push(it.number)
  }
  return { resolved, unresolved, braked }
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
// B1 — args shim + config (mirror implement-plan.js).
// NOTE: the Workflow runtime forbids Date.now()/Math.random()/new Date() (they throw, to keep resume
// deterministic). So RUNID is passed in via args (the /issue-drive skill passes a fresh one), and every
// timestamp is generated inside an agent's Git Bash (date -u), never in JS.
// ---------------------------------------------------------------------------
let A = args || {}
if (typeof A === 'string') { try { A = JSON.parse(A) } catch { A = {} } }

const AUTO_IMPLEMENT = A.autoImplement !== false       // default true (spec §6)
const DRY_RUN = !!A.dryRun                             // mutation-free preview (spec §15)
const K = A.K || 5                                    // per-stage review round limit (github.md § The round limit K)
const MAX_PARALLEL_ISSUES = A.maxParallel || 1         // spec §4.2 — ships at 1; Group F raises default to 3
const MAX_ATTEMPTS = 3                                 // ff-push rebase-retry attempts
const GRACE_MS = 15 * 60 * 1000                        // reaper recency guard (§5.0): 15 min
const MAX_PASSES = A.maxPasses || 50                   // safety bound on the Phase-1 drain loop (defensive; fixpoint normally terminates it)
const WORKTREES_DIR = '.claude/worktrees'
const REPO_DIR = 'C:/dev/OuroCore'
const RUNID = A.runid || 'drive'                       // fresh per tick from the skill; deterministic fallback
const RUNLOG = '.claude/logs/issue-flow-drive.jsonl'

// Structured-return accumulator (spec §12). Per-issue rows are { issue, fromStage, toState, outcome }.
const report = {
  runid: RUNID,
  dryRun: DRY_RUN,
  autoImplement: AUTO_IMPLEMENT,
  preconditionSkipped: false,
  fixpoint: false,
  phase1: { passes: 0, worked: [], parked: [] },
  phase2: { implemented: [], closed: [], parked: [] },
  machineryErrors: [],
}

// ---------------------------------------------------------------------------
// Schemas (every agent returns validated JSON, never prose — runtime constraint #4).
// ---------------------------------------------------------------------------
const COMMENT_ITEM = {
  type: 'object', additionalProperties: false,
  properties: { createdAt: { type: 'string' }, url: { type: 'string' }, body: { type: 'string' } },
  required: ['createdAt', 'url', 'body'],
}
const RUNSTART_SNAPSHOT_SCHEMA = {
  type: 'object', additionalProperties: false,
  properties: {
    open: {
      type: 'array', items: {
        type: 'object', additionalProperties: false,
        properties: { number: { type: 'integer' }, labels: { type: 'array', items: { type: 'string' } }, updatedAt: { type: 'string' } },
        required: ['number', 'labels', 'updatedAt'],
      },
    },
    ready: {
      type: 'array', items: {
        type: 'object', additionalProperties: false,
        properties: { number: { type: 'integer' }, labels: { type: 'array', items: { type: 'string' } }, comments: { type: 'array', items: COMMENT_ITEM } },
        required: ['number', 'labels', 'comments'],
      },
    },
  },
  required: ['open', 'ready'],
}
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
const INTAKE_SCHEMA = {
  type: 'object', additionalProperties: false,
  properties: { stamped: { type: 'array', items: { type: 'integer' } }, problems: { type: 'string' } },
  required: ['stamped', 'problems'],
}
const COMMENTS_SCHEMA = {
  type: 'object', additionalProperties: false,
  properties: { comments: { type: 'array', items: COMMENT_ITEM } },
  required: ['comments'],
}
const CLASSIFY_SCHEMA = {
  type: 'object', additionalProperties: false,
  properties: { resolved: { type: 'boolean' }, reason: { type: 'string' } },
  required: ['resolved', 'reason'],
}
const FF_PUSH_SCHEMA = {
  type: 'object', additionalProperties: false,
  properties: { pushed: { type: 'boolean' }, headSha: { type: 'string' }, problems: { type: 'string' } },
  required: ['pushed', 'problems'],
}
const FFSYNC_SCHEMA = {
  type: 'object', additionalProperties: false,
  properties: { ok: { type: 'boolean' }, problems: { type: 'string' } },
  required: ['ok', 'problems'],
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
const CLAIM_SCHEMA = {
  type: 'object', additionalProperties: false,
  properties: { claimed: { type: 'boolean' }, comments: { type: 'array', items: COMMENT_ITEM }, problems: { type: 'string' } },
  required: ['claimed', 'comments', 'problems'],
}
const POST_SCHEMA = {
  type: 'object', additionalProperties: false,
  properties: { posted: { type: 'boolean' }, problems: { type: 'string' } },
  required: ['posted', 'problems'],
}
const ROUTE_ACTION_SCHEMA = {
  type: 'object', additionalProperties: false,
  properties: { toState: { type: 'string' }, promoted: { type: 'boolean' }, demoted: { type: 'boolean' }, problems: { type: 'string' } },
  required: ['toState', 'promoted', 'demoted', 'problems'],
}
const REQUEUE_SCHEMA = {
  type: 'object', additionalProperties: false,
  properties: { toState: { type: 'string' }, problems: { type: 'string' } },
  required: ['toState', 'problems'],
}
const RESPOND_SCHEMA = {
  type: 'object', additionalProperties: false,
  properties: { classification: { type: 'string' }, committed: { type: 'boolean' }, toState: { type: 'string' }, problems: { type: 'string' } },
  required: ['classification', 'committed', 'toState', 'problems'],
}
const IMPL_CLASSIFY_SCHEMA = {
  type: 'object', additionalProperties: false,
  properties: { goAhead: { type: 'boolean' }, acted: { type: 'boolean' }, toState: { type: 'string' }, problems: { type: 'string' } },
  required: ['goAhead', 'acted', 'toState', 'problems'],
}
const PLANS_INDEX_SCHEMA = {
  type: 'object', additionalProperties: false,
  properties: {
    plans: { type: 'array', items: { type: 'object', additionalProperties: false, properties: { slug: { type: 'string' }, trackingIssue: { type: 'integer' }, path: { type: 'string' } }, required: ['slug', 'trackingIssue', 'path'] } },
  },
  required: ['plans'],
}
const CLAIM_IMPL_SCHEMA = {
  type: 'object', additionalProperties: false,
  properties: { claimed: { type: 'boolean' }, problems: { type: 'string' } },
  required: ['claimed', 'problems'],
}
const PARK_SCHEMA = {
  type: 'object', additionalProperties: false,
  properties: { parked: { type: 'boolean' }, problems: { type: 'string' } },
  required: ['parked', 'problems'],
}
const IMPL_RECON_SCHEMA = {
  type: 'object', additionalProperties: false,
  properties: { closed: { type: 'boolean' }, parentClosed: { type: 'boolean' }, problems: { type: 'string' } },
  required: ['closed', 'problems'],
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
function statusLabels(labels) { return (labels || []).filter((l) => l.startsWith('status:')) }
function doingState(labels) {
  for (const s of DOING_STATES) if ((labels || []).includes('status:' + s)) return s
  return null
}
function isBlockedDoing(labels) { return doingState(labels) !== null && (labels || []).includes('blocked') }
// The matching *-queued label for a Doing state (mechanical requeue target). `implementing` has none.
function queueForState(state) {
  switch (state) {
    case 'brainstorming': return 'brainstorm-queued'
    case 'planning': return 'plan-queued'
    case 'spec-reviewing': return 'spec-review-queued'
    case 'plan-reviewing': return 'plan-review-queued'
    default: return null
  }
}
function chunk(arr, n) { const out = []; for (let i = 0; i < arr.length; i += n) out.push(arr.slice(i, i + n)); return out }
function errText(e) { return (e && e.message) || String(e) }

// ---------------------------------------------------------------------------
// B1 — observability helpers (§12).
// ---------------------------------------------------------------------------
function hb(line) { log('[hb ' + RUNID + '] ' + line) }                 // one-line liveness digest
function narrate(issue, line) { log('#' + issue + ' ' + line) }          // issue#-prefixed narrator

// appendRunLog(obj) — append one JSON line (with a bash-generated UTC timestamp) to the durable run-log.
// Observation, not a GitHub mutation, so it runs even under DRY_RUN. Best-effort: never aborts the drive.
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
      { label: 'runlog:' + RUNID, phase: 'Report', schema: RUNLOG_SCHEMA },
    )
  } catch (e) {
    log('[runlog ' + RUNID + '] append failed (non-fatal): ' + sanitize(errText(e)))
  }
}

// ffPushPrompt(label, commitDesc) — the canonical rebase-retry fast-forward push from the PRIMARY checkout
// (the JS counterpart of github.md's ff_push snippet). HEAD must already carry the commit(s) to land.
// Used by reconcile's /issue-respond promotion and the review demote-on-re-entry (§5.0). Returns the prompt.
function ffPushPrompt(label, commitDesc) {
  return [
    'Land already-committed work on origin/main with the CANONICAL rebase-retry fast-forward push.',
    'Context: ' + label + ' — ' + commitDesc + '. Host: Windows, Git Bash. Run from the PRIMARY checkout (' + REPO_DIR + ').',
    'HEAD already carries the commit(s) to push; do NOT create new commits here.',
    'Repeat up to ' + MAX_ATTEMPTS + ' times:',
    '  git fetch origin && git rebase origin/main && git push origin HEAD:main   -> on success STOP (pushed=true).',
    '  On a rebase conflict: git rebase --abort ; loop again.',
    '  On a non-fast-forward push rejection (someone pushed first): loop again from git fetch.',
    'If all ' + MAX_ATTEMPTS + ' attempts are exhausted: run  git reset --hard origin/main  (so the next pass ff-sync still',
    '  passes), then report pushed=false with the reason (this is a machinery failure the caller will file).',
    'Return { pushed, headSha (git rev-parse --short HEAD after), problems }.',
  ].join('\n')
}
// ffPush — run the canonical ff-push agent and return the parsed result.
async function ffPush(label, commitDesc, phaseName) {
  return await agent(ffPushPrompt(label, commitDesc), { label: 'ffpush:' + label, phase: phaseName || 'Phase1', schema: FF_PUSH_SCHEMA })
}

// ---------------------------------------------------------------------------
// B6 — failure classification + self-healing (§8). Declared here (used everywhere); see fileBug below.
// ---------------------------------------------------------------------------
// classifyFailure(skill, err, hint) -> { classification, fingerprint, safe }. Logged at decision time.
// A legitimate work-failure parks `blocked` via a STRUCTURED return; an exception reaching a try/catch is
// machinery by default (a gh/git break, a thrown workflow(), a missing reference). `hint='work'` overrides.
function classifyFailure(skill, err, hint) {
  const msg = errText(err)
  const fp = fingerprint(skill, msg)
  const classification = hint === 'work' ? 'work' : 'machinery'
  log('[classify ' + RUNID + '] ' + JSON.stringify({ skill, classification, fingerprint: fp }))
  return { classification, fingerprint: fp, safe: sanitize(msg) }
}

// fileBug(skill, error) — github.md § Reporting a broken skill: best-effort, SEARCHED-FIRST, de-duped,
// SANITIZED, NO recursion. Under DRY_RUN, logs the would-be bug and creates nothing.
async function fileBug(skill, error) {
  const fp = fingerprint(skill, errText(error))
  const safe = sanitize(errText(error))
  if (DRY_RUN) { log('(dryRun) would file/recur issue-flow-bug: ' + fp); return }
  try {
    const r = await agent(
      [
        'You are the self-healing reporter for the issue-flow driver (github.md § Reporting a broken skill).',
        'A MACHINERY failure occurred in skill/stage: ' + skill,
        'Normalized fingerprint (the de-dupe key): ' + fp,
        'Sanitized error (already scrubbed of secrets — do NOT add raw payloads or secrets):',
        safe,
        '',
        'Procedure (best-effort, searched-FIRST so one breakage never spawns duplicates):',
        '  1. gh issue list --label issue-flow-bug --state open --json number,title,body',
        '  2. Judge whether any open bug is the SAME breakage (same skill + same fingerprint signature).',
        '  3. MATCH -> recur, do NOT duplicate: append a  "## 🔁 Recurred — <date>"  comment (date via',
        '     bash: date -u +%Y-%m-%d) noting this occurrence (driver runid ' + RUNID + ', the sanitized error),',
        '     and bump an occurrence count in the body. Report action="recurred", number=<that issue>.',
        '  4. NO MATCH -> file one: gh issue create  --title  "issue-flow bug: ' + skill + ' — <short signature>"',
        '     --label issue-flow-bug  (also add a severity label, e.g. high), body = the skill, the failing',
        '     step/command, the SANITIZED error above, a driver-runid cross-ref, and the date. Intake will',
        '     stamp it status:inbox. Report action="filed", number=<new issue>.',
        '  5. Sanitize ALWAYS — never put webhook_secret / OUROCORE_INGEST_TOKEN / DATABASE_URL or raw payloads',
        '     in the title or body.',
        'Return { action: "filed"|"recurred"|"noop", number, problems }.',
      ].join('\n'),
      { label: 'filebug:' + RUNID, phase: 'Report', schema: FILEBUG_SCHEMA },
    )
    log('[filebug ' + RUNID + '] ' + JSON.stringify({ skill, action: r ? r.action : 'null', number: r ? r.number : 0 }))
  } catch (e2) {
    // NO recursion: print for the operator and stop — never report the report.
    log('[filebug ' + RUNID + '] fileBug itself failed (no recursion): ' + sanitize(errText(e2)))
  }
}

// Per-unit machinery handler — log + file a bug + record. Called from the stage/route/reconcile catches.
async function handleMachineryFailure(issue, stage, skill, err) {
  const c = classifyFailure(skill, err)
  report.machineryErrors.push({ issue, stage, fingerprint: c.fingerprint })
  await appendRunLog({ unit: '#' + issue + ':' + stage, classification: c.classification, fingerprint: c.fingerprint, route: 'fileBug' })
  await fileBug(skill, err)
}

// ---------------------------------------------------------------------------
// Shared snapshot reads.
// ---------------------------------------------------------------------------
// snapshotOpen() — the lightweight per-pass open-issue snapshot (number, labels-as-names, updatedAt).
async function snapshotOpen() {
  const res = await agent(
    [
      'List the OPEN issues in this repo as structured data. Host: Windows, Git Bash. Read-only.',
      'Run:  gh issue list --state open --json number,labels,updatedAt --limit 300',
      'Return open[] where each item is { number, labels (an array of label NAME strings, e.g. "status:ready"), updatedAt }.',
      'Flatten the labels objects to their .name strings. Do not modify anything.',
    ].join('\n'),
    { label: 'snapshot:' + RUNID, phase: 'Phase1', schema: OPEN_SNAPSHOT_SCHEMA },
  )
  return (res && res.open) || []
}

// fetchComments(issue) — the per-issue comment fetch (createdAt, url, body), read-only.
async function fetchComments(issue, phaseName) {
  const res = await agent(
    [
      'Fetch the comments of issue #' + issue + ' as structured data. Host: Windows, Git Bash. Read-only.',
      'Run:  gh issue view ' + issue + ' --json comments',
      'Return comments[] where each item is { createdAt, url, body }. Do not modify anything.',
    ].join('\n'),
    { label: 'comments:' + issue, phase: phaseName || 'Phase1', schema: COMMENTS_SCHEMA },
  )
  return (res && res.comments) || []
}

// ---------------------------------------------------------------------------
// B4 — commit-gate (§9). The exit-side counterpart to github.md's entry guard.
// ---------------------------------------------------------------------------
// commitGate(issue, watermark) -> { proceed, classified }. Re-query comments, watermarkAfter(watermark);
// empty -> { proceed:true, classified:false }; else classify the latest (conservatism rule) -> Resolved
// (go-ahead/no-caveat) proceeds, Needs-you (question/caveat) does not.
async function commitGate(issue, watermark) {
  const comments = await fetchComments(issue, 'Phase1')
  const after = watermarkAfter(comments, watermark)
  if (after.length === 0) { narrate(issue, 'commit-gate: no human comment after watermark ' + watermark.t + ' — proceed'); return { proceed: true, classified: false } }
  const cls = await agent(
    [
      'Classify whether a mid-stage human comment lets an autonomous stage PROCEED, per github.md',
      '§ Approval detection — the CONSERVATISM rule. This is a TRUSTED comment (private repo, single operator, spec §10).',
      'Issue #' + issue + '. The human comment(s) that landed AFTER the stage was claimed/snapshotted (latest last):',
      JSON.stringify(after.map((c) => ({ createdAt: c.t, body: c.body })), null, 2),
      '',
      'Resolved (proceed) ONLY if the latest is an unambiguous go-ahead / ack ("ok", "lgtm", "go ahead",',
      '"looks good", "ship it", a bare 👍) with NO question and NO caveat ("looks good *but*…" is NOT resolved).',
      'Anything carrying a question, concern, change request, or any caveat => Needs you (do NOT proceed).',
      'When in doubt, choose Needs you.',
      'Return { resolved: <true=Resolved/proceed, false=Needs-you>, reason }.',
    ].join('\n'),
    { label: 'gate-classify:' + issue, phase: 'Phase1', schema: CLASSIFY_SCHEMA },
  )
  const proceed = !!(cls && cls.resolved)
  narrate(issue, 'commit-gate: ' + after.length + ' human comment(s) after watermark -> ' + (proceed ? 'Resolved (proceed)' : 'Needs-you (park)'))
  return { proceed, classified: true }
}

// ---------------------------------------------------------------------------
// B2 — run-start sequence (spec §5 steps 0–4). Ordering is load-bearing.
// ---------------------------------------------------------------------------
async function runStart() {
  // Step 0 + the shared open snapshot: snapshot `ready` (+ per-issue watermark) and all open issues, in ONE
  // read-only point-in-time fetch. The `ready` snapshot is frozen here, BEFORE the reaper/reconcile, so a
  // reaper/reconcile-produced ready never feeds THIS run's Phase 2 (§6.0).
  const snap = await agent(
    [
      'Take a single read-only point-in-time snapshot of this repo board. Host: Windows, Git Bash. Read-only.',
      '1. open: run  gh issue list --state open --json number,labels,updatedAt --limit 300  and return each as',
      '   { number, labels (array of label NAME strings), updatedAt }.',
      '2. ready: for EACH open issue that carries "status:ready" AND does NOT carry "manual",',
      '   also run  gh issue view <n> --json comments  and return { number, labels (name strings), comments:[{createdAt,url,body}] }.',
      'Flatten all labels to their .name strings. Do not modify anything.',
    ].join('\n'),
    { label: 'runstart-snapshot:' + RUNID, phase: 'RunStart', schema: RUNSTART_SNAPSHOT_SCHEMA },
  )
  const open = (snap && snap.open) || []
  const readyRaw = (snap && snap.ready) || []
  // Build the frozen ready snapshot with a commit-gate watermark each (drop any braked).
  const readySnapshot = readyRaw
    .filter((r) => !isBraked(r.labels))
    .map((r) => ({ issue: r.number, labels: r.labels, watermark: computeWatermark(r.comments) }))
  hb('run-start: ' + open.length + ' open, ' + readySnapshot.length + ' ready snapshot' + (DRY_RUN ? ' [dryRun]' : ''))

  // Step 1: infra precondition probe (§5 step 1, §8) — now the shared deterministic SCRIPT
  // (.claude/scripts/probe.sh), same as burn-the-board. The agent just RUNS it and returns its single stdout
  // JSON line; the script owns the checks (dev Postgres only when implementing; main fast-forward vs
  // origin/main — the one allowed mutation). A failure SKIPS the whole run (no issue touched). Pure-mechanical
  // now → haiku. The Postgres check is gated on AUTO_IMPLEMENT: a Phase-1-only run never touches the DB.
  const probeArg = AUTO_IMPLEMENT ? '--implement' : '--no-implement'
  const probe = await agent(
    [
      'Run the infra precondition probe SCRIPT for the autonomous issue-flow driver and return its result. Host: Windows, Git Bash.',
      'Work from the repo root (' + REPO_DIR + '). Run EXACTLY this one command:',
      '  bash .claude/scripts/probe.sh ' + probeArg,
      'The script prints human-readable progress to STDERR and EXACTLY ONE compact JSON line to STDOUT:',
      '  {"ok":true|false,"reason":"…"}',
      'It performs every check itself (and the single allowed fast-forward of main). Do NOT perform any check',
      'yourself, do NOT start the DB, do NOT change any other file. Capture that one stdout JSON line and',
      'return it verbatim as { ok, reason }.',
    ].join('\n'),
    { label: 'probe:' + RUNID, phase: 'RunStart', schema: PROBE_SCHEMA, model: 'haiku' },
  )
  if (!probe || !probe.ok) {
    const reason = probe ? probe.reason : 'probe agent returned null'
    hb('PRECONDITION FAILED — skipping run, NO issue touched. Blocker: ' + reason + '. Remediation: bring up infra-postgres-1 (podman compose -f infra/docker-compose.yml up -d) and/or reconcile main with origin, then re-run /issue-drive.')
    return { skipped: true, reason, readySnapshot, openSnapshot: open, firstActionable: false }
  }

  // Step 2: stale-worktree sweep (§5 step 2) — remove EVERY .claude/worktrees/ tree that is not a live
  // implement-plan plan-<slug> tree, regardless of runid (safe under the single-driver contract).
  if (DRY_RUN) {
    log('(dryRun) would sweep stale worktrees under ' + WORKTREES_DIR + ' (git worktree prune + remove non plan-<slug> trees)')
  } else {
    const sweep = await agent(
      [
        'Sweep stale git worktrees for the issue-flow driver. Host: Windows, Git Bash. Run from ' + REPO_DIR + '.',
        '  1. git worktree prune',
        '  2. git worktree list --porcelain  to enumerate worktrees.',
        '  3. For every worktree under ' + WORKTREES_DIR + '/ whose directory name does NOT start with "plan-"',
        '     (i.e. it is a drive-*/smoke-* leak from a crashed prior run, NEVER a live implement-plan plan-<slug> tree):',
        '       git worktree remove --force <path>   and, if a matching local branch exists (drive/* or smoke/*), git branch -D it.',
        '     Leave any plan-<slug> worktree untouched (a concurrent implement-plan may own it).',
        'Return { removed: [paths removed], problems }.',
      ].join('\n'),
      { label: 'sweep:' + RUNID, phase: 'RunStart', schema: SWEEP_SCHEMA },
    )
    if (sweep && sweep.removed && sweep.removed.length) hb('swept ' + sweep.removed.length + ' stale worktree(s): ' + sweep.removed.join(', '))
  }

  // Step 3: run-start reaper (§5.0) — recover stale Doing-not-blocked from a crashed prior run, with the
  // recency guard. JS pre-filters the candidate set (pure label logic); the agent applies the time-based
  // recency guard (bash `date`) and the relabels (no Date.now() in JS).
  const reapCandidates = open
    .filter((i) => doingState(i.labels) !== null && !i.labels.includes('blocked') && !isBraked(i.labels))
    .map((i) => ({ issue: i.number, state: doingState(i.labels), updatedAt: i.updatedAt }))
  let reaped = []
  if (reapCandidates.length === 0) {
    log('reaper: no stale Doing-not-blocked candidates')
  } else if (DRY_RUN) {
    log('(dryRun) reaper candidates (recency guard applied live): ' + JSON.stringify(reapCandidates))
  } else {
    const reap = await agent(
      [
        'You are the run-start reaper for the issue-flow driver (github.md § Queues vs Doing; spec §5.0). Host: Windows, Git Bash.',
        'Below are stale-LOOKING Doing (not-blocked, not-braked) issues. Apply the RECENCY GUARD, then reap the rest.',
        'Candidates: ' + JSON.stringify(reapCandidates),
        '',
        'RECENCY GUARD: an issue whose updatedAt is within ' + GRACE_MS + ' ms of NOW is a LIVE run (a hand-run skill or this',
        '  driver), NOT a crash — SKIP it. Compute in bash:  now=$(date -u +%s) ; t=$(date -u -d "<updatedAt>" +%s) ;',
        '  if (( (now - t) * 1000 < ' + GRACE_MS + ' )) then it is recent -> skip.',
        'REAP the demonstrably-stale remainder by running, per issue, `bash .claude/scripts/set-status.sh <issue>',
        '  <new-status> [block|unblock]` (single-select label swap + the board card together, in one command):',
        '  - any NON-implementing *-ing  ->  its matching *-queued, UN-blocked',
        '      (brainstorming->brainstorm-queued, planning->plan-queued, spec-reviewing->spec-review-queued, plan-reviewing->plan-review-queued).',
        '  - implementing  ->  STAY status:implementing and ADD blocked (NEVER status:ready — a crashed implement may have',
        '      partially landed; only the operator may set ready after trimming the plan, §6). Post a brief "## 🛑" note that it',
        '      was reaped after a crash and needs the operator to inspect what landed before retrying.',
        'Return { reaped: [{issue, from, to}], skippedRecent: [issue numbers skipped by the guard], problems }.',
      ].join('\n'),
      { label: 'reaper:' + RUNID, phase: 'RunStart', schema: REAP_SCHEMA },
    )
    reaped = (reap && reap.reaped) || []
    if (reaped.length) hb('reaped ' + reaped.length + ' stale Doing issue(s): ' + reaped.map((r) => '#' + r.issue + ' ' + r.from + '->' + r.to).join(', '))
    if (reap && reap.skippedRecent && reap.skippedRecent.length) log('reaper recency guard skipped (live): ' + reap.skippedRecent.join(', '))
  }

  // Step 4: intake sweep (§5.0 / § Intake) — stamp status:inbox on un-classified, non-epic open issues.
  const intakeCandidates = open.filter((i) => statusLabels(i.labels).length === 0 && !i.labels.includes('epic')).map((i) => i.number)
  let stamped = []
  if (intakeCandidates.length === 0) {
    log('intake: no un-classified open issues')
  } else if (DRY_RUN) {
    log('(dryRun) intake would stamp status:inbox on: ' + intakeCandidates.join(', '))
  } else {
    const intake = await agent(
      [
        'You are the intake sweep for the issue-flow driver (github.md § Intake). Host: Windows, Git Bash.',
        'For each of these open, un-classified, non-epic issues, stamp status:inbox (the single front door):',
        '  ' + JSON.stringify(intakeCandidates),
        'Run `bash .claude/scripts/set-status.sh <issue> inbox` per issue (single-select label + the board card; it',
        'creates status:inbox if missing). Skip any that already',
        'carry a status:* label or the epic label (re-check at write time). Return { stamped: [issue numbers], problems }.',
      ].join('\n'),
      { label: 'intake:' + RUNID, phase: 'RunStart', schema: INTAKE_SCHEMA },
    )
    stamped = (intake && intake.stamped) || []
    if (stamped.length) hb('intake stamped status:inbox on ' + stamped.length + ' issue(s): ' + stamped.join(', '))
  }

  // firstActionable: did the reaper/intake act, or does the snapshot hold a collectable queue issue or a
  // routable blocked-Doing? (A conservative over-approximation; the drain reaches fixpoint regardless.)
  const hasCollectable = open.some((i) => collectorPredicate(i.labels))
  const hasBlockedDoing = open.some((i) => isBlockedDoing(i.labels) && !isBraked(i.labels))
  const firstActionable = reaped.length > 0 || stamped.length > 0 || hasCollectable || hasBlockedDoing
  return { skipped: false, readySnapshot, openSnapshot: open, firstActionable }
}

// ffSyncPrimary() — re-fast-forward-sync the primary checkout before each pass (§5), so a later-pass
// reconcile promotion never pushes from a stale tree.
async function ffSyncPrimary() {
  const res = await agent(
    [
      'Fast-forward-sync the PRIMARY checkout before a driver pass. Host: Windows, Git Bash. Run from ' + REPO_DIR + '.',
      '  git fetch origin',
      '  - HEAD == origin/main: fine.',
      '  - origin/main ancestor of HEAD (unpushed local commits): fine, leave them untouched.',
      '  - HEAD ancestor of origin/main (behind): git merge --ff-only origin/main.',
      '  - diverged: do NOT force anything; report ok=false, problems="primary checkout diverged from origin".',
      'Change no tracked files beyond the allowed fast-forward. Return { ok, problems }.',
    ].join('\n'),
    { label: 'ffsync:' + RUNID, phase: 'Phase1', schema: FFSYNC_SCHEMA },
  )
  if (!res || !res.ok) hb('ff-sync warning: ' + (res ? res.problems : 'agent returned null'))
  return res
}

// ---------------------------------------------------------------------------
// B3 — reconcile router (spec §5.0). Pull-based pre-pass; default-routed blocked-issue input router.
// Serialized, per-issue try/catch isolated, exempt from the commit-gate, dryRun = detection-only.
// ---------------------------------------------------------------------------
async function reconcile(openSnapshot) {
  phase('Reconcile')
  const candidates = (openSnapshot || []).filter((i) => isBlockedDoing(i.labels) && !isBraked(i.labels))
  const routes = []
  let acted = 0
  for (const it of candidates) {
    const issue = it.number
    const state = doingState(it.labels)
    try {
      // Detect unaddressed human input: a non-## (human) comment after the last skill ## comment.
      const comments = await fetchComments(issue, 'Reconcile')
      const skillComments = comments.filter((c) => !isHuman(c))
      const skillMark = computeWatermark(skillComments)
      const unaddressed = watermarkAfter(comments, skillMark)
      if (unaddressed.length === 0) continue // parked on the human; nothing to route this pass

      if (DRY_RUN) {
        const plan = state === 'implementing' ? 'classify implement go-ahead' : (state === 'spec-reviewing' || state === 'plan-reviewing') ? 'invoke /issue-respond' : 'mechanical requeue to ' + queueForState(state)
        narrate(issue, '(dryRun) reconcile would ' + plan + ' (blocked ' + state + ', ' + unaddressed.length + ' unaddressed)')
        routes.push({ issue, fromState: state, route: plan + ' (dryRun)' }); acted++; continue
      }

      if (state === 'brainstorming' || state === 'planning') {
        // Mechanical requeue — clear blocked, *-ing -> *-queued; the next pull re-reads the answers.
        const q = queueForState(state)
        const r = await agent(requeuePrompt(issue, q), { label: 'recon-requeue:' + issue, phase: 'Reconcile', schema: REQUEUE_SCHEMA })
        narrate(issue, 'reconcile mechanical requeue ' + state + ' -> ' + (r ? r.toState : q))
        routes.push({ issue, fromState: state, route: 'requeue->' + q })
      } else if (state === 'spec-reviewing' || state === 'plan-reviewing') {
        // Review verdict: invoke /issue-respond (the sole approve-vs-feedback interpreter). A blanket
        // requeue would re-run the review and bury the verdict (§5.0). Promotion uses the canonical ff-push.
        const r = await agent(respondPrompt(issue, state), { label: 'recon-respond:' + issue, phase: 'Reconcile', schema: RESPOND_SCHEMA })
        if (r && r.committed) { const ff = await ffPush('respond #' + issue, '/issue-respond promotion', 'Reconcile'); if (!ff || !ff.pushed) hb('reconcile #' + issue + ' promotion ff-push failed: ' + (ff ? ff.problems : 'null')) }
        narrate(issue, 'reconcile /issue-respond -> ' + (r ? r.classification + ' / ' + r.toState : 'null'))
        routes.push({ issue, fromState: state, route: 'issue-respond' })
      } else if (state === 'implementing') {
        // Diagnosis/go-ahead: an unambiguous go-ahead -> ready (operator must have trimmed the plan, §6);
        // any caveat -> stay parked. Conservatism rule (github.md § Approval detection).
        const r = await agent(implementGoAheadPrompt(issue), { label: 'recon-impl:' + issue, phase: 'Reconcile', schema: IMPL_CLASSIFY_SCHEMA })
        narrate(issue, 'reconcile implementing -> ' + (r && r.goAhead ? 'go-ahead, status:ready' : 'stay parked'))
        routes.push({ issue, fromState: state, route: r && r.goAhead ? 'implement->ready' : 'stay-parked' })
      } else {
        // default: mechanical requeue to the matching *-queued.
        const q = queueForState(state) || (state + '-queued')
        const r = await agent(requeuePrompt(issue, q), { label: 'recon-default:' + issue, phase: 'Reconcile', schema: REQUEUE_SCHEMA })
        narrate(issue, 'reconcile default requeue ' + state + ' -> ' + (r ? r.toState : q))
        routes.push({ issue, fromState: state, route: 'requeue->' + q })
      }
      acted++
    } catch (e) {
      // Errors are ISOLATED per issue — log + skip, do not abort the pass.
      log('reconcile #' + issue + ' isolated error (' + state + '): ' + sanitize(errText(e)))
      await handleMachineryFailure(issue, 'reconcile:' + state, 'reconcile', e)
    }
  }
  if (acted) hb('reconcile routed ' + acted + ' blocked issue(s)')
  phase('Phase1')
  return { acted, routes }
}

function requeuePrompt(issue, queue) {
  return [
    'Reconcile mechanical requeue (github.md § Queues vs Doing, and reconcile). Host: Windows, Git Bash.',
    'Issue #' + issue + ' is a blocked Doing state with unaddressed human input that is content for the next pull.',
    'Move it with `bash .claude/scripts/set-status.sh ' + issue + ' ' + queue + ' unblock` (single-select label',
    'swap to status:' + queue + ', clears blocked, AND the board card — one command).',
    'Post NO comment. Return { toState: "status:' + queue + '", problems }.',
  ].join('\n')
}
function respondPrompt(issue, state) {
  const gate = state === 'spec-reviewing' ? 'spec' : 'plan'
  return [
    'Run the /issue-respond skill for issue #' + issue + ' (the ' + gate + ' review gate). Host: Windows, Git Bash.',
    'READ AND FOLLOW  .claude/skills/issue-respond/SKILL.md  and  .claude/rules/github.md  exactly — it is the sole',
    'approve-vs-feedback interpreter (conservatism rule). It classifies the latest human comment, then either',
    'promotes the artifact + advances (approval) or answers + routes back (feedback).',
    'ONE DEVIATION: do NOT run a plain `git push`. After the promotion `git mv` + commit on the primary checkout,',
    'STOP before pushing and leave the commit on HEAD — the driver runs the canonical rebase-retry ff-push next.',
    'Report { classification: "approval"|"feedback", committed: <true if a promotion commit is on HEAD awaiting push>, toState, problems }.',
  ].join('\n')
}
function implementGoAheadPrompt(issue) {
  return [
    'Issue #' + issue + ' is status:implementing + blocked (a failed/crashed/partial-land implement). A human commented.',
    'Classify the latest human comment per github.md § Approval detection — the CONSERVATISM rule (trusted comment, §10).',
    'Run:  gh issue view ' + issue + ' --json comments  and read the human comment(s) since the last "##" skill comment.',
    'ONLY if it is an UNAMBIGUOUS go-ahead with NO caveat/question (the operator confirms they trimmed the promoted plan to',
    'the un-landed tasks, or reverted the landed commits, per §6): run `bash .claude/scripts/set-status.sh ' + issue + ' ready',
    'unblock` (label + board, clears blocked). ANY caveat/question/diagnosis-only => do NOTHING, stay parked.',
    'A bare "retry" WITHOUT confirmation of a trimmed plan is the BROKEN path — treat it as NOT a go-ahead.',
    'Return { goAhead, acted, toState, problems }.',
  ].join('\n')
}

// ---------------------------------------------------------------------------
// B4 — Phase-1 drain loop + authoring stages.
// ---------------------------------------------------------------------------
async function drainPhase1() {
  while (report.phase1.passes < MAX_PASSES) {
    report.phase1.passes++
    if (DRY_RUN) log('(dryRun) would ff-sync the primary checkout before pass ' + report.phase1.passes)
    else await ffSyncPrimary()

    const snap = await snapshotOpen()
    await reconcile(snap)                  // re-snapshot openSnapshot at the top of each pass; route blocked input
    const post = await snapshotOpen()      // re-read AFTER reconcile mutated labels, so a same-pass requeue is collectable
    const collected = post.filter((i) => collectorPredicate(i.labels))
    if (collected.length === 0) { report.fixpoint = true; hb('Phase-1 fixpoint after ' + report.phase1.passes + ' pass(es)'); break }
    hb('pass ' + report.phase1.passes + ': collected ' + collected.length + ' issue(s) -> ' + collected.map((i) => '#' + i.number).join(', '))
    for (const batch of chunk(collected, MAX_PARALLEL_ISSUES)) {
      await parallel(batch.map((i) => () => runStage(i)))   // §4.2 width cap; one stage per issue
    }
  }
  if (report.phase1.passes >= MAX_PASSES && !report.fixpoint) hb('Phase-1 hit MAX_PASSES=' + MAX_PASSES + ' without fixpoint — stopping (investigate a non-advancing stage)')
}

// runStage(issue) — dispatch on the *-queued label; per-unit try/catch isolation + failure classification (B6).
async function runStage(issue) {
  const n = issue.number
  const labels = issue.labels || []
  let stage = '?'; let skill = '?'
  try {
    if (labels.includes('status:brainstorm-queued')) { stage = 'brainstorm'; skill = 'issue-brainstorm'; return await runAuthoringStage(n, 'brainstorm') }
    if (labels.includes('status:plan-queued')) { stage = 'plan'; skill = 'issue-plan'; return await runAuthoringStage(n, 'plan') }
    if (labels.includes('status:spec-review-queued')) { stage = 'spec-review'; skill = 'issue-spec-review'; return await runReviewStage(n, 'spec') }
    if (labels.includes('status:plan-review-queued')) { stage = 'plan-review'; skill = 'issue-plan-review'; return await runReviewStage(n, 'plan') }
    log('runStage: #' + n + ' has no recognized *-queued label, skipping')
  } catch (e) {
    // An exception escaping a stage is a MACHINERY failure (a work-failure parks blocked via a structured return).
    narrate(n, stage + ' machinery failure: ' + sanitize(errText(e)))
    await handleMachineryFailure(n, stage, skill, e)
  }
}

// runAuthoringStage(issue, kind) — kind in {brainstorm, plan}. ONE agent that reads+follows the skill +
// github.md, with the §4.3/§5.1 named deviation: the persist-draft step (commit+push to the primary
// checkout) is replaced by the manual-worktree + canonical rebase-retry ff-push; the brake branch is
// suppressed (the collector already excludes braked issues).
async function runAuthoringStage(issue, kind) {
  const next = kind === 'brainstorm' ? 'spec-review-queued' : 'plan-review-queued'
  if (DRY_RUN) {
    narrate(issue, '(dryRun) would run ' + kind + ' authoring: worktree ' + WORKTREES_DIR + '/drive-' + RUNID + '-' + issue + ', draft under ' + (kind === 'brainstorm' ? 'specs' : 'plans') + '/drafts/{date-slug}.md, ff-push, advance -> ' + next + ' (or park blocked if questions)')
    report.phase1.worked.push({ issue, fromStage: kind, toState: '(dryRun) ' + next, outcome: 'dryRun' })
    return
  }
  const skillFile = kind === 'brainstorm' ? '.claude/skills/issue-brainstorm/SKILL.md' : '.claude/skills/issue-plan/SKILL.md'
  const draftDir = kind === 'brainstorm' ? 'docs/superpowers/specs/drafts' : 'docs/superpowers/plans/drafts'
  const wt = WORKTREES_DIR + '/drive-' + RUNID + '-' + issue
  const br = 'drive/' + RUNID + '-' + issue
  const res = await agent(
    [
      'You are the ' + kind + ' authoring stage for issue #' + issue + ' in the issue-flow driver. Host: Windows, Git Bash.',
      'READ AND FOLLOW these in full, then execute the stage:',
      '  - ' + skillFile + '   (the per-stage source of truth)',
      '  - .claude/rules/github.md   (set_status, the comment templates, storage/promotion, § work-graph)',
      'Follow the skill EXACTLY, with these TWO named deviations (anchor to the skill markers, not step numbers):',
      '',
      'DEVIATION 1 — replace the skill PERSIST-DRAFT step (its "commit + push to main / primary checkout") with the',
      'manual-worktree + canonical rebase-retry ff-push (spec §5.1):',
      '  a. git fetch origin ; git worktree add ' + wt + ' -b ' + br + ' origin/main   (the unique drive-' + RUNID + '-' + issue + ' worktree path the run-start sweep can target),',
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
      'DEVIATION 2 — SUPPRESS the skill brake branch (do not park on `manual`): the collector already',
      'excludes braked issues, so this issue is not braked. Otherwise honor the skill, INCLUDING its claim of the Doing',
      'state (status:' + (kind === 'brainstorm' ? 'brainstorming' : 'planning') + '), its dialogue-handler behavior, and its scope/split check (/issue-split).',
      '',
      'Report { outcome: "advanced" (drafted + advanced to the next queue) | "parked" (posted questions / hit the unaddressed-comment',
      'guard, set blocked) | "split" (ran /issue-split) | "error", toState (the status:* it now carries), draftPath, problems }.',
    ].join('\n'),
    { label: 'author:' + kind + ':' + issue, phase: 'Phase1', schema: AUTHORING_SCHEMA },
  )
  const outcome = res ? res.outcome : 'error'
  if (outcome === 'advanced' || outcome === 'split') {
    report.phase1.worked.push({ issue, fromStage: kind, toState: res.toState, outcome })
    narrate(issue, kind + ' ' + outcome + ' -> ' + res.toState)
  } else {
    report.phase1.parked.push({ issue, fromStage: kind, toState: res ? res.toState : 'unknown', outcome: outcome === 'parked' ? 'parked' : 'error' })
    narrate(issue, kind + ' ' + outcome + (res && res.problems ? ': ' + res.problems : ''))
    await appendRunLog({ unit: '#' + issue + ':' + kind, classification: 'work', outcome, route: outcome })
  }
}

// ---------------------------------------------------------------------------
// B5 — review stages (script-owned routing, §5.2).
// ---------------------------------------------------------------------------
async function runReviewStage(issue, kind) {
  const reviewing = kind === 'spec' ? 'spec-reviewing' : 'plan-reviewing'

  // 1. claim-agent: set_status *-reviewing (clears blocked) + record the commit-gate watermark AT CLAIM.
  const claim = await agent(claimReviewPrompt(issue, reviewing), { label: 'claim:' + issue, phase: 'Phase1', schema: CLAIM_SCHEMA })
  const watermark = computeWatermark((claim && claim.comments) || [])
  narrate(issue, (DRY_RUN ? '(dryRun) ' : '') + 'claimed status:' + reviewing + '; watermark ' + watermark.t)

  // 2. the review leaf — the SCRIPT owns this script-hook workflow() call (§2/§5.2). Read-only on GitHub.
  const r = await workflow('issue-review', { issue, kind, roundLimit: K })
  if (!r || !r.verdict) {
    // The review leaf returned nothing usable — machinery failure.
    await handleMachineryFailure(issue, kind + '-review', 'issue-review', new Error('issue-review returned no verdict'))
    return
  }
  narrate(issue, kind + ' review round ' + r.round + ' -> ' + r.verdict + ' (' + r.counts.critical + 'c/' + r.counts.high + 'h/' + r.counts.medium + 'm)')

  if (DRY_RUN) {
    const d = route(r.verdict, kind, r.round, K)
    narrate(issue, '(dryRun) would post the verdict comment, then commit-gate, then route: ' + JSON.stringify(d))
    report.phase1.parked.push({ issue, fromStage: kind + '-review', toState: '(dryRun)', outcome: 'dryRun:' + r.verdict })
    return
  }

  // 3a. route-agent: POST the verdict comment VERBATIM FIRST (a mid-review comment must never discard the verdict).
  const posted = await agent(postCommentPrompt(issue, r.comment), { label: 'post-review:' + issue, phase: 'Phase1', schema: POST_SCHEMA })
  if (!posted || !posted.posted) hb('#' + issue + ' WARNING: verdict comment may not have posted: ' + (posted ? posted.problems : 'null'))

  // 3b. commit-gate exit re-check (§9) against the at-claim watermark.
  const gate = await commitGate(issue, watermark)
  if (!gate.proceed) {
    await agent(parkNeedsYouPrompt(issue, reviewing), { label: 'park:' + issue, phase: 'Phase1', schema: PARK_SCHEMA })
    report.phase1.parked.push({ issue, fromStage: kind + '-review', toState: 'status:' + reviewing + '+blocked', outcome: 'needs-you' })
    narrate(issue, 'commit-gate Needs-you -> parked blocked at ' + reviewing)
    return
  }

  // 3c. execute the route the SCRIPT computed (pure route()).
  const d = route(r.verdict, kind, r.round, K)
  const action = await agent(routeActionPrompt(issue, kind, reviewing, d, r.round, K), { label: 'route:' + issue, phase: 'Phase1', schema: ROUTE_ACTION_SCHEMA })
  // An LGTM promotion (auto-advance, §5.2) or a demote-on-re-entry both leave a commit on HEAD of the primary
  // checkout (git mv + comment-link edit, NEVER -f); land it via the canonical rebase-retry ff-push.
  if (action && (action.promoted || action.demoted)) {
    const what = action.promoted ? 'promote ' + kind + ' artifact + advance' : 'demote ' + kind + ' artifact back to drafts'
    const ff = await ffPush((action.promoted ? 'promote #' : 'demote #') + issue, what, 'Phase1')
    if (!ff || !ff.pushed) hb('#' + issue + ' ' + (action.promoted ? 'promote' : 'demote') + ' ff-push failed: ' + (ff ? ff.problems : 'null'))
  }

  const toState = action ? action.toState : 'status:' + reviewing
  if (d.promote) { report.phase1.worked.push({ issue, fromStage: kind + '-review', toState, outcome: 'lgtm-promoted:' + d.requeueTo }); narrate(issue, kind + ' review LGTM -> promoted + advanced to ' + toState) }
  else if (d.requeueTo) { report.phase1.worked.push({ issue, fromStage: kind + '-review', toState, outcome: 'requeued:' + r.verdict }); narrate(issue, kind + ' review ' + r.verdict + ' -> requeued ' + toState + (action && action.demoted ? ' (demoted)' : '')) }
  else { report.phase1.parked.push({ issue, fromStage: kind + '-review', toState, outcome: 'stuck-after-K' }); narrate(issue, kind + ' review ' + r.verdict + ' -> parked ' + toState + ' (stuck after K=' + K + ')') }
}

function claimReviewPrompt(issue, reviewing) {
  return [
    'Claim the review of issue #' + issue + ' for the issue-flow driver. Host: Windows, Git Bash.',
    'First read its comments:  gh issue view ' + issue + ' --json comments  -> return them as comments[] of {createdAt,url,body}.',
    DRY_RUN
      ? 'DRY-RUN: do NOT change any label — only return the comments (for the watermark). Set claimed=false.'
      : 'Then claim it: run `bash .claude/scripts/set-status.sh ' + issue + ' ' + reviewing + ' unblock` (single-select label to status:' + reviewing + ', clears blocked, AND the board card — one command). Set claimed=true.',
    'Post NO comment. Return { claimed, comments, problems }.',
  ].join('\n')
}
function postCommentPrompt(issue, comment) {
  return [
    'Post this consolidated review verdict comment VERBATIM on issue #' + issue + ' (github.md Review comment format).',
    'Host: Windows, Git Bash. Use  gh issue comment ' + issue + ' --body-file <tmp>  (write the body to a temp file to',
    'preserve formatting exactly; do not edit/trim/re-wrap it). The comment text:',
    '--- BEGIN COMMENT ---',
    comment,
    '--- END COMMENT ---',
    'Return { posted, problems }.',
  ].join('\n')
}
function parkNeedsYouPrompt(issue, reviewing) {
  return [
    'A human comment landed mid-review on issue #' + issue + ' and is NOT a clear go-ahead (commit-gate, §9). Do NOT advance.',
    'Host: Windows, Git Bash. Stay status:' + reviewing + ' and SET blocked via `bash .claude/scripts/set-status.sh ' + issue + ' ' + reviewing + ' block` (label + board card), and post',
    'the github.md "## 🛑 Needs you — unaddressed comment" note (name the commenter + a one-line gist; tell them their reply is',
    'picked up automatically, or to clear blocked to re-queue by hand). Return { parked, problems }.',
  ].join('\n')
}
function routeActionPrompt(issue, kind, reviewing, d, round, K) {
  const skillFile = '.claude/skills/issue-' + kind + '-review/SKILL.md'
  const lines = [
    'Execute the review route the driver ALREADY decided for issue #' + issue + ' (' + kind + ' gate, round ' + round + '/' + K + '). Host: Windows, Git Bash.',
    'READ AND FOLLOW  ' + skillFile + '  step 5 — its routing mechanics are the SINGLE SOURCE. The gated route() already picked',
    'the branch below; do NOT re-derive it from the verdict. Every label change goes through',
    '`bash .claude/scripts/set-status.sh ' + issue + ' <new-status> [block|unblock]` (label + board card together).',
    'TWO named deviations from the skill (driver / unattended context): (1) the `manual` brake branch never reaches here (the',
    'collector excludes braked issues) — ignore it; (2) for ANY promote/demote git mv + comment-link edit, COMMIT the',
    'git-mv-staged rename ONLY on the PRIMARY checkout and LEAVE it on HEAD — do NOT push (the driver runs the canonical',
    'rebase-retry ff-push next). Never -A/./-u/commit -a (github.md § Working-tree hygiene).',
  ]
  if (d.stuck) {
    lines.push('BRANCH = round >= K STUCK: follow the skill\'s "round >= K" bullet — stay status:' + reviewing + ', SET blocked, append',
      'the "## 🛑 Stuck — needs you (after K rounds)" note. Set toState="status:' + reviewing + '+blocked", promoted=false, demoted=false.')
  } else if (d.promote) {
    lines.push('BRANCH = LGTM auto-advance: follow the skill\'s LGTM bullet — promote the artifact (git mv draft -> top-level + edit',
      'the Spec/Plan comment link) and advance to status:' + d.requeueTo + '. Set toState="status:' + d.requeueTo + '", promoted=true, demoted=false.')
  } else if (d.requeueTo) {
    lines.push('BRANCH = requeue (failing verdict, round < K): follow the skill\'s NEEDS WORK / RETHINK bullet — move to status:' + d.requeueTo + ',',
      'CLEAR blocked, and DEMOTE-ON-RE-ENTRY (git mv a previously-promoted top-level artifact BACK to drafts/ + fix the comment',
      'link). Set demoted=true only if you moved a file; else false. Set promoted=false. Set toState="status:' + d.requeueTo + '".')
  } else {
    lines.push('BRANCH = (no-op) stay status:' + reviewing + '. Set toState="status:' + reviewing + '", promoted=false, demoted=false.')
  }
  lines.push('Return { toState, promoted, demoted, problems }.')
  return lines.join('\n')
}

// ---------------------------------------------------------------------------
// B5 — Phase 2: implement (spec §6, default-ON, brake-scoped, per-plan).
// ---------------------------------------------------------------------------
async function runPhase2(readySnapshot) {
  phase('Phase2')
  if (!readySnapshot || readySnapshot.length === 0) { hb('Phase 2: empty ready snapshot — skip'); return }

  // 1. Resolve the brake-scoped allow-list. Build plansIndex from PROMOTED top-level plan headers, then
  //    resolveReadyAllowList (brake RE-CHECKED here so a human can add `manual` mid-run to abort, §6.1).
  const idx = await agent(
    [
      'Build an index of PROMOTED top-level implementation plans. Host: Windows, Git Bash. Read-only.',
      'Scan ONLY  docs/superpowers/plans/*.md  (top level — NOT drafts/, NOT completed/). For each file whose header',
      'contains a line  > **Tracking issue:** #<n>  , return { slug, trackingIssue: <n>, path } where slug = the filename',
      'without the leading YYYY-MM-DD- date prefix and without the .md extension, and path = the repo-relative path to the',
      'plan file (e.g. docs/superpowers/plans/<file>.md). Skip files with no such header.',
      'Return { plans: [{slug, trackingIssue, path}] }. Do not modify anything.',
    ].join('\n'),
    { label: 'plans-index:' + RUNID, phase: 'Phase2', schema: PLANS_INDEX_SCHEMA },
  )
  const plansIndex = (idx && idx.plans) || []
  const wmMap = {}; for (const s of readySnapshot) wmMap[s.issue] = s.watermark
  const readyForResolve = readySnapshot.map((s) => ({ number: s.issue, labels: s.labels }))
  const { resolved, unresolved, braked } = resolveReadyAllowList(readyForResolve, plansIndex)
  hb('Phase 2: resolved ' + resolved.length + ', unresolved ' + unresolved.length + ', braked ' + braked.length)
  if (braked.length) log('Phase 2 braked (manual mid-run) — skipped: ' + braked.join(', '))

  // Unresolved ready issues: claim to a Doing state + park blocked (never silently stranded, §6.1).
  for (const issue of unresolved) {
    if (DRY_RUN) { narrate(issue, '(dryRun) ready but no resolvable top-level plan -> would claim implementing + park blocked'); report.phase2.parked.push({ issue, reason: 'unresolved (dryRun)' }); continue }
    try {
      await agent(
        [
          'Issue #' + issue + ' is status:ready but has NO resolvable promoted top-level plan (docs/superpowers/plans/*.md with',
          'its tracking-issue header). Do not silently strand it (§6.1). Host: Windows, Git Bash.',
          'Run `bash .claude/scripts/set-status.sh ' + issue + ' implementing block` (label + board card, sets blocked), and post a',
          '"## 🛑 Needs you — no resolvable plan" note explaining its plan was not found at top level (promote it via /issue-respond,',
          'or check the > **Tracking issue:** #' + issue + ' header). Return { parked, problems }.',
        ].join('\n'),
        { label: 'p2-unresolved:' + issue, phase: 'Phase2', schema: PARK_SCHEMA },
      )
    } catch (e) { await handleMachineryFailure(issue, 'phase2-unresolved', 'issue-implement', e) }
    report.phase2.parked.push({ issue, reason: 'unresolved' })
  }

  // 2. Implement per plan, fanned out up to MAX_PARALLEL_ISSUES at a time (default 1 = the prior sequential
  //    behavior). Each plan integrates via implement-plan's per-green-commit rebase + ff-push onto origin/main,
  //    so concurrent plans land through that optimistic-concurrency push loop rather than a lock (spec §4.2, §6).
  async function implementOne(issue, slug) {
    const watermark = wmMap[issue] || { ...EPOCH }
    const planPath = (plansIndex.find((p) => p.slug === slug) || {}).path
    try {
      // a. commit-gate re-check against THIS issue's run-start watermark (§6.2a, §9).
      const gate = await commitGate(issue, watermark)
      if (!gate.proceed) {
        await parkImplementing(issue, slug, 'A human comment landed before implement — not implemented. Resolve it (reply go-ahead, or clear blocked).')
        report.phase2.parked.push({ issue, slug, reason: 'commit-gate' })
        narrate(issue, 'Phase 2 commit-gate Needs-you -> parked implementing+blocked, NOT pushed')
        return
      }

      if (DRY_RUN) {
        narrate(issue, '(dryRun) would claim implementing, run implement-plan {plan:' + planPath + ', dry-run}, then close ONLY on implemented+archived')
        await workflow('implement-plan', { plan: planPath, mode: 'dry-run' })   // read-only / no push in dry-run mode
        report.phase2.parked.push({ issue, slug, reason: 'dryRun' })
        return
      }

      // b. claim just this issue + the durable crash-trace breadcrumb (§12).
      await agent(
        [
          'Claim issue #' + issue + ' for implementation. Host: Windows, Git Bash. Run',
          '`bash .claude/scripts/set-status.sh ' + issue + ' implementing unblock` (label + board card, clears blocked), then post the durable breadcrumb comment:',
          '  ## ⏳ implementing — pushing ' + slug,
          'Return { claimed, problems }.',
        ].join('\n'),
        { label: 'p2-claim:' + issue, phase: 'Phase2', schema: CLAIM_IMPL_SCHEMA },
      )

      // c. the implement leaf — SCRIPT-owned script-hook call, scoped to this one plan by path (§6.2c).
      const resImpl = await workflow('implement-plan', { plan: planPath, mode: 'merge' })
      const pr = resImpl
      const implementedArchived = !!(pr && pr.merged && pr.reason === 'merged + archived')
      const partialLand = !!(pr && !pr.merged && (pr.pushedTasks || 0) > 0)

      // d. reconcile (/issue-implement steps 5–7): close ONLY on implemented+archived (NOT bare merged[]).
      if (implementedArchived) {
        const closeGate = await commitGate(issue, watermark)   // final pre-close commit-gate re-check (§9)
        if (!closeGate.proceed) {
          await parkImplementing(issue, slug, 'Implemented + archived on main, but a human comment landed before close — issue kept OPEN. Reply go-ahead, or close by hand.')
          report.phase2.parked.push({ issue, slug, reason: 'pre-close commit-gate' })
          narrate(issue, 'Phase 2 implemented+archived but pre-close gate Needs-you -> kept open, parked blocked')
          return
        }
        const recon = await agent(implementReconcilePrompt(issue, slug, pr), { label: 'p2-close:' + issue, phase: 'Phase2', schema: IMPL_RECON_SCHEMA })
        report.phase2.implemented.push(slug)
        if (recon && recon.closed) report.phase2.closed.push(issue)
        narrate(issue, 'Phase 2 implemented + archived -> ' + (recon && recon.closed ? 'closed' : 'close attempted: ' + (recon ? recon.problems : 'null')))
      } else if (partialLand) {
        // Partial land = TERMINAL needs-human (§6): name the landed tasks; bare-retry is the broken path.
        await parkImplementing(issue, slug,
          'PARTIAL LAND — implement-plan stopped after landing ' + (pr.pushedTasks || 0) + ' task increment(s) on main; it is NOT resumable. ' +
          'A bare retry would write a failing test for code that already exists (the BROKEN path — do NOT do it). ' +
          'Recover by trimming the promoted plan to the UN-LANDED tasks (or reverting the landed commits), THEN reply go-ahead so reconcile routes it to status:ready. Problems: ' + (pr.problems || ''))
        report.phase2.parked.push({ issue, slug, reason: 'partial-land' })
        narrate(issue, 'Phase 2 PARTIAL LAND (' + (pr.pushedTasks || 0) + ' increment(s)) -> terminal, parked implementing+blocked')
      } else {
        // Implement legitimately could not finish (work-failure): leave implementing+blocked with the error.
        await parkImplementing(issue, slug, 'implement-plan did not complete: ' + (pr ? pr.reason + ' — ' + (pr.problems || '') : 'no plan result for ' + slug))
        report.phase2.parked.push({ issue, slug, reason: pr ? pr.reason : 'no result' })
        await appendRunLog({ unit: '#' + issue + ':implement', classification: 'work', outcome: 'not-completed', route: 'park-implementing' })
        narrate(issue, 'Phase 2 not completed (' + (pr ? pr.reason : 'no result') + ') -> parked implementing+blocked')
      }
    } catch (e) {
      // A thrown workflow()/agent is MACHINERY: park implementing+blocked AND file a bug (never closed/stranded).
      narrate(issue, 'Phase 2 machinery failure: ' + sanitize(errText(e)))
      try { await parkImplementing(issue, slug, 'implement machinery error: ' + sanitize(errText(e))) } catch (e2) { log('park after machinery failed: ' + sanitize(errText(e2))) }
      report.phase2.parked.push({ issue, slug, reason: 'machinery' })
      await handleMachineryFailure(issue, 'phase2-implement', 'implement-plan', e)
    }
  }

  // Fan out the resolved plans up to MAX_PARALLEL_ISSUES at a time (same width cap Phase 1 uses). Default 1 keeps
  // the prior strictly-sequential behavior; raise args.maxParallel to land multiple plans concurrently, each in
  // its own implement-plan worktree integrating onto origin/main via the per-green-commit rebase + ff-push loop.
  for (const batch of chunk(resolved, MAX_PARALLEL_ISSUES)) {
    await parallel(batch.map(({ issue, slug }) => () => implementOne(issue, slug)))
  }
}

// parkImplementing — leave an issue at status:implementing + blocked with a failure comment (never close/strand).
async function parkImplementing(issue, slug, message) {
  if (DRY_RUN) { narrate(issue, '(dryRun) would park implementing+blocked: ' + message); return }
  await agent(
    [
      'Park issue #' + issue + ' (plan ' + slug + ') at status:implementing + blocked — it is recoverable (a human go-ahead routes it back). Host: Windows, Git Bash.',
      'Run `bash .claude/scripts/set-status.sh ' + issue + ' implementing block` (label + board card, sets blocked; do NOT close, do NOT strand un-blocked),',
      'then post a "## 🛑" failure comment with this exact gist (telegraph English):',
      sanitize(message),
      'Return { parked, problems }.',
    ].join('\n'),
    { label: 'p2-park:' + issue, phase: 'Phase2', schema: PARK_SCHEMA },
  )
}
function implementReconcilePrompt(issue, slug, pr) {
  const shas = pr && pr.commits && pr.commits.length ? pr.commits.join(' ') : '(see git log on main)'
  return [
    'Reconcile the implemented plan ' + slug + ' (issue #' + issue + '). Host: Windows, Git Bash.',
    'READ AND FOLLOW  .claude/skills/issue-implement/SKILL.md  steps 5–6 (the reconcile mechanics — the SINGLE SOURCE) for',
    'THIS one issue #' + issue + ': post the github.md Implement comment with the merged SHAs, gh issue close #' + issue + ', then',
    'the parent rollup. The plan is ALREADY implemented + archived on main (the driver ran step 4), so do steps 5–6 ONLY.',
    'The merged commit SHAs for the Implement comment: ' + shas + ' (auto-linking; one short SHA per change).',
    'Return { closed (did #' + issue + ' close), parentClosed, problems }.',
  ].join('\n')
}

// ---------------------------------------------------------------------------
// finalize() — heartbeat digest + durable run-log + structured return (§12).
// ---------------------------------------------------------------------------
async function finalize() {
  const worked = report.phase1.worked.length + report.phase2.implemented.length
  const parked = report.phase1.parked.length + report.phase2.parked.length
  let digest
  if (report.preconditionSkipped) digest = 'precondition-skipped'
  else if (report.machineryErrors.length) digest = 'machinery-error x' + report.machineryErrors.length
  else if (worked > 0) digest = 'worked-' + worked
  else if (parked > 0) digest = 'parked-blocked-' + parked
  else digest = 'idle-at-fixpoint'
  hb('DONE ' + digest + ' (passes=' + report.phase1.passes + ', fixpoint=' + report.fixpoint + ', implemented=' + report.phase2.implemented.length + ', closed=' + report.phase2.closed.length + ', machinery=' + report.machineryErrors.length + ')')
  await appendRunLog(report)
  return report
}

// ---------------------------------------------------------------------------
// B1 — top-level orchestration (RunStart -> no-op fast path -> Phase1 -> Phase2 -> Report).
// ---------------------------------------------------------------------------
phase('RunStart')
const start = await runStart()
if (start.skipped) { report.preconditionSkipped = true; report.skipReason = start.reason; hb('precondition-skipped: ' + start.reason); return await finalize() }

// /loop no-op fast path (§5): exit early ONLY if nothing actionable AND the ready snapshot is empty.
// A ready-only board does NOT short-circuit — Phase 2 still drains it under autoImplement.
if (!start.firstActionable && start.readySnapshot.length === 0) {
  report.fixpoint = true; hb('idle-at-fixpoint'); return await finalize()
}

phase('Phase1')
try { await drainPhase1() }
catch (e) { hb('Phase 1 aborted by an unexpected error: ' + sanitize(errText(e))); await handleMachineryFailure(0, 'phase1-drain', 'issue-flow-drive', e) }

phase('Phase2')
if (AUTO_IMPLEMENT) {
  try { await runPhase2(start.readySnapshot) }
  catch (e) { hb('Phase 2 aborted by an unexpected error: ' + sanitize(errText(e))); await handleMachineryFailure(0, 'phase2-drain', 'issue-flow-drive', e) }
} else {
  hb('autoImplement=false -> Phase 1 only; ' + start.readySnapshot.length + ' ready issue(s) left at the ready gate for /issue-implement')
}

phase('Report')
return await finalize()

export const meta = {
  name: 'issue-flow-smoke',
  description: 'HARD PRECONDITION GATE for the issue-flow driver: asserts the pure decision functions, proves nested workflow()-in-parallel() (3x9), runs the manual-worktree authoring lifecycle pushing to a DISPOSABLE ref (never main), and asserts an unknown workflow() name throws. Until green, the driver does not ship.',
  phases: [
    { title: 'PureFns', detail: 'assertion tables for the decision functions' },
    { title: 'NestedFanout', detail: '3 branches x 9 leaves of nested workflow()' },
    { title: 'WorktreeLifecycle', detail: 'add->commit->rebase->ff-push to a disposable ref->remove' },
    { title: 'UnknownName', detail: 'workflow() with an unknown name throws' },
  ],
}

// ─────────────────────────────────────────────────────────────────────────────────────────────────
// INLINED from lib/drive-fns.js — the Workflow runtime has NO module system (static import = syntax
// error; import() throws). This is the GATED canonical copy: the PureFns assertion tables below prove it.
// KEEP IN SYNC with lib/drive-fns.js (the reviewed reference) and issue-flow-drive.js (the driver copy).
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
  // limit so an LGTM at round K promotes, not parks-stuck. (Keep in sync with lib/drive-fns.js + driver.)
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
  [/\b(NUGET_API_KEY|GITHUB_TOKEN|\w*_(?:TOKEN|SECRET|KEY))\b\s*[=:]\s*\S+/gi, '$1=<redacted>'],
  [/https:\/\/[^@\s/]+@/gi, 'https://<redacted>@'],
  [/x-access-token:[^@\s]+/gi, 'x-access-token:<redacted>'],
  [/Authorization:\s*\S+/gi, 'Authorization: <redacted>'],
  [/\bBearer\s+\S+/gi, 'Bearer <redacted>'],
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

// NOTE: the Workflow runtime forbids Date.now()/Math.random() (they throw, to keep resume deterministic
// — confirmed: implement-plan.js uses neither). So RUNID is passed in via args (the runtime-recommended
// "pass ids in via args" pattern). The invoker passes a fresh args.runid.
let A = args || {}
if (typeof A === 'string') { try { A = JSON.parse(A) } catch { A = {} } }
const RUNID = A.runid || 'manual'
const fails = []
const ok = (cond, msg) => { if (!cond) fails.push(msg) }
const eq = (got, want, msg) =>
  ok(JSON.stringify(got) === JSON.stringify(want), `${msg}: got ${JSON.stringify(got)} want ${JSON.stringify(want)}`)

phase('PureFns')
// route(verdict, kind, round, K) — LGTM auto-advances (promote + advance); manual is the only human gate.
eq(route('LGTM', 'spec', 1, 5), { stay: false, block: false, requeueTo: 'plan-queued', stuck: false, promote: true }, 'route LGTM spec -> promote + plan-queued')
eq(route('LGTM', 'plan', 1, 5), { stay: false, block: false, requeueTo: 'ready', stuck: false, promote: true }, 'route LGTM plan -> promote + ready')
eq(route('LGTM', 'plan', 5, 5), { stay: false, block: false, requeueTo: 'ready', stuck: false, promote: true }, 'route LGTM plan rK = promote + ready, not stuck')
eq(route('NEEDS WORK', 'spec', 1, 5), { stay: false, block: false, requeueTo: 'brainstorm-queued', stuck: false, promote: false }, 'route NW spec')
eq(route('RETHINK', 'spec', 2, 5), { stay: false, block: false, requeueTo: 'brainstorm-queued', stuck: false, promote: false }, 'route RT spec')
eq(route('NEEDS WORK', 'plan', 1, 5), { stay: false, block: false, requeueTo: 'plan-queued', stuck: false, promote: false }, 'route NW plan')
eq(route('RETHINK', 'plan', 2, 5), { stay: false, block: false, requeueTo: 'brainstorm-queued', stuck: false, promote: false }, 'route RT plan -> brainstorm')
eq(route('NEEDS WORK', 'spec', 5, 5), { stay: true, block: true, requeueTo: null, stuck: true, promote: false }, 'route NW spec rK -> stuck')
eq(route('RETHINK', 'plan', 5, 5), { stay: true, block: true, requeueTo: null, stuck: true, promote: false }, 'route RT plan rK -> stuck')

// collectorPredicate(labels)
ok(collectorPredicate(['status:brainstorm-queued']) === true, 'collect brainstorm-queued')
ok(collectorPredicate(['status:spec-review-queued']) === true, 'collect spec-review-queued')
ok(collectorPredicate(['status:plan-queued']) === true, 'collect plan-queued')
ok(collectorPredicate(['status:plan-review-queued']) === true, 'collect plan-review-queued')
ok(collectorPredicate(['status:brainstorm-queued', 'blocked']) === false, 'exclude blocked')
ok(collectorPredicate(['status:brainstorm-queued', 'manual']) === false, 'exclude manual brake')
ok(collectorPredicate(['status:ready']) === false, 'ready is NOT a phase-1 queue')
ok(collectorPredicate(['status:brainstorming']) === false, 'Doing is not a queue')
ok(collectorPredicate(['status:inbox']) === false, 'inbox not pulled')
ok(collectorPredicate(['epic']) === false, 'epic excluded')
ok(collectorPredicate([]) === false, 'unclassified excluded')

// computeWatermark / watermarkAfter
eq(computeWatermark([]), { t: '1970-01-01T00:00:00.000Z', url: '' }, 'watermark empty -> epoch')
const cs = [
  { createdAt: '2026-06-14T10:00:00Z', url: 'u#1', body: '## skill a' },
  { createdAt: '2026-06-14T11:00:00Z', url: 'u#2', body: 'human answer' },
]
eq(computeWatermark(cs), { t: '2026-06-14T11:00:00Z', url: 'u#2' }, 'watermark = latest')
ok(watermarkAfter(cs, { t: '2026-06-14T10:30:00Z', url: '' }).length === 1, 'one human comment after mark')
ok(watermarkAfter(cs, { t: '2026-06-14T11:00:00Z', url: 'u#2' }).length === 0, 'nothing after the latest')
ok(watermarkAfter([{ createdAt: '2026-06-14T12:00:00Z', url: 'u#3', body: '## skill only' }],
  { t: '1970-01-01T00:00:00.000Z', url: '' }).length === 0, '## comments are not human')

// resolveReadyAllowList
eq(resolveReadyAllowList([{ number: 35, labels: ['status:ready'] }], [{ slug: 'foo', trackingIssue: 35 }]),
  { resolved: [{ issue: 35, slug: 'foo' }], unresolved: [], braked: [] }, 'ready -> resolved')
eq(resolveReadyAllowList([{ number: 35, labels: ['status:ready', 'manual'] }], [{ slug: 'foo', trackingIssue: 35 }]),
  { resolved: [], unresolved: [], braked: [35] }, 'braked ready -> no slug')
eq(resolveReadyAllowList([{ number: 36, labels: ['status:ready'] }], []),
  { resolved: [], unresolved: [36], braked: [] }, 'ready w/o plan -> unresolved')

// sanitize / fingerprint (security-load-bearing)
ok(!sanitize('DATABASE_URL=postgres://u:p@h/db').includes('postgres://u:p@h'), 'sanitize scrubs DATABASE_URL')
ok(sanitize('git clone https://x-access-token:ghs_abc@github.com/o/r').includes('<redacted>'), 'sanitize scrubs token url')
ok(fingerprint('issue-plan', 'fatal: #51 at C:/dev/x abc1234d') === fingerprint('issue-plan', 'fatal: #777 at C:/dev/y def56789'),
  'fingerprint normalizes ids/shas/paths')

if (fails.length) { log('PURE-FN FAILURES:\n' + fails.join('\n')); throw new Error(`smoke: ${fails.length} pure-fn assertion(s) failed`) }
log('pure-fn tables: all green')

phase('NestedFanout')
// Driver-shaped parallel() of 3 branches, each NESTING a workflow() that itself parallel()s 9 leaves.
const branches = await parallel([0, 1, 2].map((b) => () => workflow('issue-flow-smoke-leaf', { branch: b, width: 9 })))
ok(branches.length === 3 && branches.every((r) => r && r.sum === 9),
  `nested 3x9 fan-out: ${JSON.stringify(branches.map((r) => r && r.sum))}`)
if (fails.length) { log(fails.join('\n')); throw new Error('smoke: nested fan-out failed (possible parent-slot deadlock)') }
log('nested 3x9 workflow()-in-parallel() completed without deadlock')

phase('WorktreeLifecycle')
const life = await agent(
  [
    'Prove the manual-worktree authoring lifecycle WITHOUT touching main. Host: Windows, Git Bash available.',
    'Run, in order, from the repo root (C:/dev/OuroCore):',
    '  git fetch origin',
    `  git worktree add .claude/worktrees/smoke-${RUNID} -b smoke/${RUNID} origin/main`,
    `  In that worktree: write a throwaway file smoke-${RUNID}.txt containing "${RUNID}", "git add" it,`,
    `     "git commit -m \\"test(smoke): disposable ${RUNID}\\"".`,
    `  git -C .claude/worktrees/smoke-${RUNID} fetch origin ; git -C .claude/worktrees/smoke-${RUNID} rebase origin/main`,
    `  git -C .claude/worktrees/smoke-${RUNID} push origin HEAD:refs/heads/smoke-${RUNID}   # DISPOSABLE ref — NEVER main`,
    'CLEANUP (run ALWAYS, success or failure):',
    `  git push origin --delete smoke-${RUNID}`,
    `  cd C:/dev/OuroCore ; git worktree remove --force .claude/worktrees/smoke-${RUNID} ; git branch -D smoke/${RUNID}`,
    'VERIFY: "git ls-remote origin refs/heads/smoke-*" prints nothing, and origin/main HEAD is unchanged from before.',
    'Report ok=true ONLY if the push to the disposable ref succeeded AND cleanup left no smoke-* ref/worktree/branch.',
  ].join('\n'),
  { label: 'smoke-worktree', phase: 'WorktreeLifecycle',
    schema: { type: 'object', additionalProperties: false, properties: { ok: { type: 'boolean' }, detail: { type: 'string' } }, required: ['ok', 'detail'] } },
)
ok(life && life.ok, `worktree lifecycle: ${life ? life.detail : 'agent returned null'}`)

phase('UnknownName')
let threw = false
try { await workflow(`definitely-not-a-workflow-${RUNID}`, {}) } catch { threw = true }
ok(threw, 'unknown workflow() name must throw')

if (fails.length) { log('SMOKE FAILURES:\n' + fails.join('\n')); throw new Error(`smoke: ${fails.length} failure(s) — driver does NOT ship`) }
log('SMOKE GREEN — primitives proven; driver may ship.')
return { ok: true, runid: RUNID }

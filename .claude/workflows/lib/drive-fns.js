// .claude/workflows/lib/drive-fns.js
// CANONICAL REFERENCE COPY — the single human-reviewed definition of the issue-flow driver's pure
// decision functions. No I/O, no gh, no git. These authorize push/promote/close (spec §13).
//
// ⚠️ The Workflow runtime has NO module system: static `import` is a syntax error and `import()` throws
// ("import() is not available in workflow scripts"). So this file CANNOT be imported. Its contents are
// INLINED VERBATIM into the two runtime scripts that need them:
//   - .claude/workflows/issue-flow-smoke.js  (the inlined copy is GATED in-place by assertion tables)
//   - .claude/workflows/issue-flow-drive.js  (the driver)
// KEEP ALL THREE IN SYNC. The smoke harness's assertion tables prove the smoke copy (== this canonical
// logic) is correct; the driver copy must match. (Plan A3-step-2 fallback; spec §13/§14.)

export const BRAKE_LABELS = ['manual'] // the brake — the single label that suppresses the driver (spec §11)

export const PHASE1_QUEUES = [
  'status:brainstorm-queued',
  'status:spec-review-queued',
  'status:plan-queued',
  'status:plan-review-queued',
]

// collectorPredicate(labels) -> bool : is this issue a Phase-1 collectable — a FREE queue, not braked,
// not blocked, not an epic? (spec §3 collector filter). `ready` is NOT a Phase-1 queue.
export function collectorPredicate(labels) {
  const L = labels || []
  if (BRAKE_LABELS.some((b) => L.includes(b))) return false
  if (L.includes('blocked')) return false
  if (L.includes('epic')) return false
  return PHASE1_QUEUES.some((q) => L.includes(q))
}

// route(verdict, kind, round, K) -> { stay, block, requeueTo, stuck, promote } : review-stage routing (spec §5.2).
// LGTM AUTO-ADVANCES: promote the artifact + advance to the next queue (spec -> plan-queued, plan -> ready).
// The `manual` brake is the only human gate; the driver's collector excludes `manual` issues entirely, so
// route() never sees one and auto-advances unconditionally. (The hand-run review skills handle the `manual`
// LGTM park themselves — see github.md § Approval detection.) LGTM is still checked BEFORE the round limit,
// so an LGTM at round K promotes rather than parking as stuck.
export function route(verdict, kind, round, K) {
  if (verdict === 'LGTM') return { stay: false, block: false, requeueTo: kind === 'spec' ? 'plan-queued' : 'ready', stuck: false, promote: true }
  if (round >= K) return { stay: true, block: true, requeueTo: null, stuck: true, promote: false }
  // failing verdict (NEEDS WORK | RETHINK), round < K
  if (kind === 'spec') return { stay: false, block: false, requeueTo: 'brainstorm-queued', stuck: false, promote: false }
  // plan gate: NEEDS WORK -> re-plan; RETHINK -> re-brainstorm (the design is wrong)
  const target = verdict === 'RETHINK' ? 'brainstorm-queued' : 'plan-queued'
  return { stay: false, block: false, requeueTo: target, stuck: false, promote: false }
}

// --- commit-gate watermark (spec §9) ---
const EPOCH = { t: '1970-01-01T00:00:00.000Z', url: '' }
function afterMark(c, mark) {
  if (c.t > mark.t) return true
  if (c.t < mark.t) return false
  return (c.url || '') > (mark.url || '') // tie-break by stable comment url
}
function norm(comments) {
  return (comments || []).map((c) => ({ t: c.createdAt, url: c.url || '', body: c.body || '' }))
}
// computeWatermark(comments) -> {t,url} : max(createdAt) tie-broken by url; epoch sentinel if none.
export function computeWatermark(comments) {
  const cs = norm(comments)
  if (cs.length === 0) return { ...EPOCH }
  return cs.reduce((m, c) => (afterMark(c, m) ? { t: c.t, url: c.url } : m), { ...EPOCH })
}
// a human comment is any whose body does NOT start with "##" (github.md § The unaddressed-comment guard).
function isHuman(c) { return !/^\s*##/.test(c.body || '') }
// watermarkAfter(comments, mark) -> human comments[] strictly after the watermark (spec §9 exit re-check).
export function watermarkAfter(comments, mark) {
  return norm(comments).filter(isHuman).filter((c) => afterMark(c, mark))
}

// resolveReadyAllowList(readyIssues, plansIndex)
//   -> { resolved:[{issue,slug}], unresolved:[issue], braked:[issue] }   (spec §6.1)
// readyIssues: [{number, labels}] (the run-start ready snapshot). plansIndex: [{slug, trackingIssue}]
// of PROMOTED top-level plans. Brake re-checked here so a human can add `manual` mid-run to abort an
// impending implement; a ready issue with no resolvable plan is `unresolved` (claim+park, never stranded).
export function resolveReadyAllowList(readyIssues, plansIndex) {
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

// sanitize(text) -> secrets/credentials scrubbed (spec §8 — never put secrets in a bug report).
const SCRUB = [
  [/\b(NUGET_API_KEY|GITHUB_TOKEN|\w*_(?:TOKEN|SECRET|KEY))\b\s*[=:]\s*\S+/gi, '$1=<redacted>'],
  [/https:\/\/[^@\s/]+@/gi, 'https://<redacted>@'],
  [/x-access-token:[^@\s]+/gi, 'x-access-token:<redacted>'],
  [/Authorization:\s*\S+/gi, 'Authorization: <redacted>'],
  [/\bBearer\s+\S+/gi, 'Bearer <redacted>'],
]
export function sanitize(text) {
  let s = String(text == null ? '' : text)
  for (const [re, to] of SCRUB) s = s.replace(re, to)
  return s
}

// fingerprint(skill, errorText) -> a normalized signature (issue numbers, ids, shas, paths stripped)
// so the SAME breakage always fingerprints the same (spec §8 / github.md § Reporting a broken skill).
export function fingerprint(skill, errorText) {
  const sig = sanitize(errorText)
    .replace(/#?\b\d{1,7}\b/g, '#N')              // issue numbers / counts
    .replace(/\b[0-9a-f]{7,40}\b/gi, '<sha>')      // git shas
    .replace(/[A-Za-z]:[\\/][^\s'"]+/g, '<path>')  // windows paths
    .replace(/(^|\s)\/[^\s'"]+/g, '$1<path>')      // posix paths
    .replace(/\s+/g, ' ').trim().slice(0, 200)
  return `${skill}: ${sig}`
}

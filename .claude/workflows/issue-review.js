export const meta = {
  name: 'issue-review',
  description: 'Review one drafted artifact (spec or plan) linked from a GitHub issue across 4 dimensions + the principal-engineer lens in change-review scope; return a consolidated verdict + comment markdown. Read-only on GitHub.',
  phases: [
    { title: 'Locate', detail: 'fetch issue + find the linked draft artifact' },
    { title: 'Review', detail: '5 reviewers judge the artifact in parallel' },
    { title: 'Synthesis', detail: 'verdict + one consolidated comment' },
  ],
}

// args: { issue: number, kind: 'spec' | 'plan', roundLimit?: number, dimensions?: string[] }
//   dimensions — optional adaptive-rigor subset (.claude/rules/rigor.md): the dimension/expert keys to run
//   (maintainability|correctness|performance|testability|consequences; 'principal-engineer' alias accepted).
//   correctness is ALWAYS force-included (the floor); absent/empty => the full team (backward compatible).
// The Workflow tool delivers tool-level `args` as a JSON string; parse it if so.
// (A nested workflow() call delivers an object, which passes straight through.)
let a = args || {}
if (typeof a === 'string') { try { a = JSON.parse(a) } catch { a = {} } }
const { issue, kind, roundLimit = 5, concurrency = 4, attempts = 3, dimensions } = a
if (!issue || (kind !== 'spec' && kind !== 'plan')) {
  throw new Error("issue-review requires args { issue: <number>, kind: 'spec' | 'plan' }")
}
const draftDir = kind === 'spec' ? 'docs/superpowers/specs/drafts' : 'docs/superpowers/plans/drafts'
const headerLine = kind === 'spec' ? '🔬 Spec review' : '🔍 Plan review'
// How many times to try each agent before giving up — intermittent provider faults (a dropped
// socket, a terminal API error) must not sink a whole review. Override via args.attempts.
const AGENT_ATTEMPTS = Math.max(1, attempts || 3)

// Per-agent model tiers (cost calibration; mirrors walk-issue.js's FAST/MID). The cheap stages drop off the
// slow session model: locate (mechanically find + measure the artifact) -> FAST; the per-dimension reviewer
// fan-out, the consolidated single-agent gate, the fan-out CONTEXT PACK, AND the SYNTHESIS merge -> MID.
// Synthesis only merges already-structured findings into one comment (no fresh code judgment), so MID suffices.
const FAST = 'haiku'   // pure-mechanical, read-only: locate + measure the artifact
const MID = 'sonnet'   // the per-dimension reviewers, the consolidated review, the context pack, the synthesis merge

const FINDING = {
  type: 'object',
  properties: {
    severity: { type: 'string', enum: ['critical', 'high', 'medium', 'low'] },
    dimension: { type: 'string' },
    location: { type: 'string' },
    finding: { type: 'string' },
    suggestion: { type: 'string' },
    snippet: { type: 'string' },
  },
  required: ['severity', 'dimension', 'location', 'finding', 'suggestion'],
  additionalProperties: false,
}
const REVIEW = {
  type: 'object',
  properties: {
    findings: { type: 'array', items: FINDING },
    questions: { type: 'array', items: { type: 'string' } },
  },
  required: ['findings', 'questions'],
  additionalProperties: false,
}

// Same 5 as evaluate.js: 4 dimensions + the principal-engineer lens.
const REVIEWERS = [
  { key: 'correctness',     role: 'correctness-reviewer', rules: '.claude/rules/correctness.md' },
  { key: 'maintainability', role: 'architect',            rules: '.claude/rules/maintainability.md' },
  { key: 'performance',     role: 'performance-reviewer', rules: '.claude/rules/performance.md' },
  { key: 'testability',     role: 'quality-engineer',     rules: '.claude/rules/testability.md' },
  { key: 'consequences',    role: 'principal-engineer',   rules: '.claude/rules/consequences.md', expert: true },
]

// Adaptive-rigor dimension subset (.claude/rules/rigor.md, the review->subset knob). When args.dimensions is a
// non-empty array, restrict the review team to those dimensions — but ALWAYS force-include correctness
// (the hard-gate floor dimension in standards.md), even if the caller omits it. This is the FLOOR backstop:
// a caller's subset can never drop the dimension that gates the verdict. Absent / empty / all-invalid =>
// full team (backward compatible — existing callers pass no dimensions and are unchanged).
const FLOOR_DIMS = ['correctness']
const ALL_DIM_KEYS = REVIEWERS.map((r) => r.key)
const DIM_ALIAS = { 'principal-engineer': 'consequences', principal: 'consequences' }
function normalizeDims(dims) {
  if (!Array.isArray(dims) || dims.length === 0) return null // null => full team
  const set = new Set()
  for (const d of dims) {
    const k = Object.prototype.hasOwnProperty.call(DIM_ALIAS, d) ? DIM_ALIAS[d] : d
    if (k && ALL_DIM_KEYS.includes(k)) set.add(k)
  }
  for (const f of FLOOR_DIMS) set.add(f) // floor: never droppable
  // If the caller named only invalid keys, the set is now just the floor — that is a real (tiny) subset, but
  // such a degenerate request almost certainly means "full team", so fall back to full.
  return set.size <= FLOOR_DIMS.length && !dims.some((d) => ALL_DIM_KEYS.includes(DIM_ALIAS[d] || d)) ? null : set
}
const dimSubset = normalizeDims(dimensions)
const activeReviewers = dimSubset ? REVIEWERS.filter((r) => dimSubset.has(r.key)) : REVIEWERS
const droppedDims = dimSubset ? REVIEWERS.filter((r) => !dimSubset.has(r.key)).map((r) => r.key) : []

const base = `You have Read/Bash/Grep/Glob over the whole repo. The project rules live in .claude/rules/*.md —
consult ONLY the file(s) your lens needs; do NOT pre-read them all (that re-reading is most of the latency).
MANDATORY: read .claude/rules/project-phase.md FIRST and let its POC->MVP calibration set your severity bar
BEFORE you judge anything — it is binding for this review (a finding is rated by whether it blocks the POC today).
OPTIONAL: .claude/rules/architecture.md carries the domain context + project layering if your lens needs it.
For every critical/high finding include a 3-10 line code/artifact snippet, or it is rejected.`

// Cap the reviewer fan-out (default 4, override via args.concurrency) so the 5-agent panel can't trip
// provider rate limits — the whole panel used to fire at once (up to the runtime's min(16, cores-2) cap).
// 4 (was 3) clears a small floor-protected subset in fewer waves while staying rate-limit-safe.
const REVIEW_CONCURRENCY = Math.max(1, concurrency || 4)

// mapLimit(items, limit, fn) — run fn over items with at most `limit` agents in flight at once: a rolling
// worker pool (no batch barrier — each freed slot refills immediately), results returned in input order.
// Mirrors parallel()'s failure semantics (a thrown task lands as null in its slot, so callers keep using
// .filter(Boolean)).
async function mapLimit(items, limit, fn) {
  const results = new Array(items.length)
  const width = Math.max(1, Math.min(limit, items.length))
  let next = 0
  async function worker() {
    while (next < items.length) {
      const i = next++
      try { results[i] = await fn(items[i], i) } catch { results[i] = null }
    }
  }
  await Promise.all(Array.from({ length: width }, worker))
  return results
}

// withRetry(label, attempts, fn) — re-run `fn` until it yields a truthy result or `attempts` are
// exhausted, then return null. An intermittent provider fault (a dropped socket, a terminal API error)
// surfaces TWO ways from agent(): it may THROW, or it may RESOLVE TO null (the harness's terminal-error
// contract). Both are transient and worth retrying, so we treat a null result and a thrown error
// identically. Each attempt is a fresh agent() call. The caller decides what an exhausted-null means:
// a single-point step throws a clear error; the reviewer fan-out drops + logs and carries on.
async function withRetry(label, attempts, fn) {
  for (let attempt = 1; attempt <= attempts; attempt++) {
    try {
      const res = await fn()
      if (res) return res
      if (attempt < attempts) log(`${label}: agent returned nothing (attempt ${attempt}/${attempts}) — retrying`)
    } catch (err) {
      if (attempt < attempts) log(`${label}: ${(err && err.message) || err} (attempt ${attempt}/${attempts}) — retrying`)
    }
  }
  return null
}

// ---------------------------------------------------------------------------
// Consolidated single-agent review (POC token budget). ONE agent does locate + all-lens review +
// synthesis in a single pass, instead of the N-reviewer fan-out + synthesis. It reads the rules, the
// artifact, and the touched code ONCE and judges across all 4 dimensions + the principal-engineer lens (or the
// floor-protected subset), applying the POC severity calibration. Used for: the SPEC gate ALWAYS
// (self-locating, `located` = null), and the PLAN gate BY DEFAULT — only LARGE/complex plans (or a plan
// whose floor-protected subset is already small) fan out to the N-agent panel + synthesis. When called for
// a plan it receives the pre-resolved `located` (artifactPath + priorRounds) so it does not re-locate. Same
// return contract { verdict, comment, questions, counts, round } as the fan-out path, so both callers —
// the issue-spec-review / issue-plan-review skills and walk-issue's runReviewStage — are unchanged. Runs on
// MID (sonnet), consistent with the per-dimension reviewers.
// ---------------------------------------------------------------------------
const SMALL_TEAM = 4   // active-reviewer count at/under which a PLAN review takes the single consolidated agent
// Plan-size fan-out trigger (tunable): a plan exceeding EITHER threshold takes the N-agent fan-out + synthesis;
// otherwise the single consolidated agent reviews it (the default for plans). Measured by the locate agent.
const LARGE_PLAN_LINES = 400
const LARGE_PLAN_TASKS = 6
// Periodic-full backstop for diff-scoped re-review (POC token budget). A PLAN re-review (round >= 2) judges
// only the CHANGED sections (incrementalReview); but every FULL_REVIEW_EVERY-th round resets to a COMPLETE
// review, so a regression that creeps in over several quiet diff-scoped rounds is still caught. A round is
// "full" when round === 1 || round % FULL_REVIEW_EVERY === 1 -> rounds 1, 4, 7, … For the default K=5:
// rounds 1 & 4 are full, rounds 2/3/5 are diff-scoped.
const FULL_REVIEW_EVERY = 3
const CONSOLIDATED_RESULT = {
  type: 'object',
  properties: {
    artifactPath: { type: 'string' },
    round: { type: 'integer' },
    verdict: { type: 'string', enum: ['RETHINK', 'NEEDS WORK', 'LGTM'] },
    comment: { type: 'string' },
    questions: { type: 'array', items: { type: 'string' } },
    counts: {
      type: 'object',
      properties: {
        critical: { type: 'integer' },
        high: { type: 'integer' },
        medium: { type: 'integer' },
      },
      required: ['critical', 'high', 'medium'],
      additionalProperties: false,
    },
  },
  required: ['artifactPath', 'round', 'verdict', 'comment', 'questions', 'counts'],
  additionalProperties: false,
}
// Diff-scoped re-review result (incrementalReview). Same review shape as CONSOLIDATED_RESULT minus the echoed
// artifactPath/round (we hold those), PLUS two fields: `fallback` (the agent could not diff — < 2 commits for
// the plan file — so the workflow runs a full review instead) and `diffRange` ("<prev>..<head>" actually
// diffed, for the log; "" on fallback).
const INCREMENTAL_RESULT = {
  type: 'object',
  properties: {
    fallback: { type: 'boolean' },
    diffRange: { type: 'string' },
    verdict: { type: 'string', enum: ['RETHINK', 'NEEDS WORK', 'LGTM'] },
    comment: { type: 'string' },
    questions: { type: 'array', items: { type: 'string' } },
    counts: {
      type: 'object',
      properties: {
        critical: { type: 'integer' },
        high: { type: 'integer' },
        medium: { type: 'integer' },
      },
      required: ['critical', 'high', 'medium'],
      additionalProperties: false,
    },
  },
  required: ['fallback', 'diffRange', 'verdict', 'comment', 'questions', 'counts'],
  additionalProperties: false,
}

// consolidatedReview(kind, located) — the single-agent review shared by the spec gate (always) and the
// default plan gate. Judges ONLY the active lenses when a subset is in effect (floor-protected: correctness
// is always kept — exactly the spec gate's old specTeamLabel logic). `kind` changes only the team
// noun and the DESIGN-vs-implementation-APPROACH framing. `located` (when provided — the plan path) gives the
// agent the artifact path + round directly so it skips self-locating; when null (the spec gate) it self-locates
// with its original step-1. The github.md "Review comment" format is INLINED into the prompt so the agent need
// not read github.md for it.
async function consolidatedReview(kind, located) {
  phase('Review')
  const teamNoun = kind === 'spec' ? 'spec-review' : 'plan-review'
  // Floor-protected dimension subset (.claude/rules/rigor.md). The single agent narrows WHAT it judges, not
  // the agent count; correctness is always kept (the floor).
  const teamLabel = dimSubset
    ? `a FLOOR-PROTECTED SUBSET of the ${teamNoun} team — judge ONLY these lenses: ${activeReviewers
        .map((r) => (r.expert ? r.role : r.key))
        .join(', ')} (correctness is ALWAYS included — the floor). Do NOT raise findings for the dropped lenses (${droppedDims.join(', ')})`
    : `the ENTIRE ${teamNoun} team in one agent — the 4 dimensions (maintainability, correctness, performance, testability) AND the 1 domain expert (principal-engineer/consequences)`
  if (dimSubset) log(`issue-review(${kind}): dimension subset — judging ${activeReviewers.map((r) => r.key).join(', ')}; dropped ${droppedDims.join(', ') || '(none)'}`)
  const judgeText = kind === 'spec'
    ? 'Judge the DESIGN across every lens: boundaries, the runtime/generator + consumer-surface/generator-internal layering, second-order consequences, incremental-generator cacheability, the one-way-door generated-output/package-identity contracts, and POC readiness — is this the right approach?'
    : 'Judge the implementation APPROACH across every lens: task correctness, TDD coverage, exact-path/type consistency, layering, and POC readiness — will following this plan produce correct, well-layered code?'
  const commentLabel = kind === 'spec' ? 'Spec' : 'Plan'
  // Step 1: self-locate (spec gate, located=null) OR use the pre-resolved path/round (plan gate).
  const locateStep = located
    ? `1. The artifact is at ${located.artifactPath}; this is round ${located.priorRounds + 1}. Read it in full.
   (You MAY \`gh issue view ${issue} --json comments\` for extra context, but USE this exact path and round —
   set artifactPath and round in your result to them.)`
    : `1. Locate. Fetch the issue and its comments:
     gh issue view ${issue} --json number,title,body,comments
   This issue tracks a drafted ${kind} under ${draftDir}/. From the latest "${commentLabel}" comment's
   "Full ${kind}:" link line (or by matching the issue slug) find the artifact file path
   (repo-relative). Count how many prior "## ${headerLine}" comments already exist = priorRounds;
   round = priorRounds + 1.`
  const res = await withRetry(`${kind}-review:${issue}`, AGENT_ATTEMPTS, () => agent(
    `${base}
The project-phase calibration above is binding for THIS review — apply it as you judge every lens below.

You are ${teamLabel}. Do a CHANGE REVIEW of a proposed ${kind}:

${locateStep}
2. Read the ${kind} in full, and the existing code it touches, to judge feasibility and consequences.
   Consult each active lens's own checklist in .claude/rules/*.md (e.g. correctness.md, maintainability.md) only as needed.
3. ${judgeText} Collect findings (severity, dimension, location, finding, suggestion) with a 3-10 line
   evidence snippet on every critical/high (no snippet => drop the finding), and open questions for the author.
4. Decide the VERDICT (standards.md "Review Verdicts"):
   - RETHINK — any wrong-approach / boundary-violation / wrong-layering finding (fix direction first);
   - NEEDS WORK — sound approach but one or more critical/high findings (after POC calibration);
   - LGTM — no critical or high findings (after POC calibration).
5. Write ONE consolidated comment EXACTLY in this format (this IS the github.md "Review comment" template —
   reproduce it precisely; do NOT read another file for it):
     ## ${headerLine} — round {round} — {VERDICT}

     {one-paragraph synthesis}

     ### {dimension} — {severity}
     {the concern, located by section or code path}
     > {3-10 line evidence snippet — REQUIRED on every critical/high}
     **Suggestion:** {what to change}

     ### Questions for author
     - {open question}

     <sub>reviewed ${kind} slug \`{slug}\` · round {round}/${roundLimit} · {critical}c/{high}h/{medium}m</sub>
   Emit one "### {dimension} — {severity}" block per KEPT finding (the "> " evidence snippet is required on
   every critical/high, plus a "**Suggestion:**"); {slug} is the artifact filename without its ".md" extension;
   the footer counts are over the findings you KEPT after calibration and must match.
Return { artifactPath, round, verdict, comment, questions, counts } where counts are over the findings you
KEPT after calibration and match the footer.`,
    { label: `${kind}-review:${issue}`, phase: 'Review', schema: CONSOLIDATED_RESULT, model: MID },
  ))
  if (!res) throw new Error(`issue-review: single-agent ${kind} review failed after ${AGENT_ATTEMPTS} attempts`)
  // When pre-located (plan gate), trust the round we computed from priorRounds, not the agent's echo.
  return { verdict: res.verdict, comment: res.comment, questions: res.questions, counts: res.counts, round: located ? located.priorRounds + 1 : res.round }
}

// Dispatch. The SPEC gate is ALWAYS one self-locating consolidated agent (unchanged).
if (kind === 'spec') return await consolidatedReview('spec', null)

// PLAN gate. Locate FIRST (we need the plan's size + round to decide), then route:
//   - FULL round (round 1, and every FULL_REVIEW_EVERY-th round after) -> fullPlanReview: the complete review
//     (consolidated-vs-fan-out per the size gate) — unchanged behavior.
//   - re-review round (round >= 2, not a backstop round) -> incrementalReview: a cheaper DIFF-SCOPED re-review
//     that judges only the delta since the last round (with a floor full-plan pass + prior-finding carry-over).
// The locate agent also MEASURES the plan (planLines = wc -l; taskCount = "### " Task headings) so the size
// decision is data-driven and logged, never silent.
phase('Locate')
const located = await withRetry(`locate:${issue}`, AGENT_ATTEMPTS, () => agent(
  `${base}
Fetch issue #${issue} with its comments:
  gh issue view ${issue} --json number,title,body,comments
This issue tracks a drafted ${kind} under ${draftDir}/. From the latest "${kind}" comment
(its "Full ${kind}:" link line) or by matching the issue slug, find the artifact file path.
Also count how many prior "## ${headerLine}" comments already exist (the review-round history),
and MEASURE the artifact file:
  - planLines  — its total line count:  wc -l < <artifactPath>   (integer).
  - taskCount  — the number of "### " task-section headings:  grep -c '^### ' <artifactPath>   (integer; 0 if none).
Return: artifactPath (repo-relative), problem (issue title + body + what it must solve),
priorRounds (integer count of prior ${kind} reviews), planLines (integer), taskCount (integer).`,
  {
    label: `locate:${issue}`, phase: 'Locate', model: FAST,
    schema: {
      type: 'object',
      properties: {
        artifactPath: { type: 'string' },
        problem: { type: 'string' },
        priorRounds: { type: 'integer' },
        planLines: { type: 'integer' },
        taskCount: { type: 'integer' },
      },
      required: ['artifactPath', 'problem', 'priorRounds', 'planLines', 'taskCount'],
      additionalProperties: false,
    },
  },
))
if (!located) throw new Error(`issue-review: locate failed after ${AGENT_ATTEMPTS} attempts`)
const round = located.priorRounds + 1

// Periodic-full backstop (FULL_REVIEW_EVERY): round 1 and every FULL_REVIEW_EVERY-th round after get the
// COMPLETE review (fullPlanReview); the intervening re-review rounds (>= 2) judge only the DELTA since the
// last round (incrementalReview — which itself falls back to a full review if the plan file has < 2 commits).
const fullRound = round === 1 || round % FULL_REVIEW_EVERY === 1
if (!fullRound) {
  log(`issue-review(plan): round ${round} — diff-scoped re-review (periodic full every ${FULL_REVIEW_EVERY} rounds)`)
  return await incrementalReview(located)
}
log(`issue-review(plan): round ${round} — full review (backstop: round 1 / every ${FULL_REVIEW_EVERY}th round)`)
return await fullPlanReview(located)

// incrementalReview(located) — DIFF-SCOPED plan re-review (rounds >= 2 that are not a periodic-full backstop).
// ONE consolidated MID agent that: (1) diffs the LAST plan revision via git; (2) re-reads the prior round's
// findings via gh; (3) judges the DELTA — NEW issues in the changed sections, PLUS the status of every prior
// finding (an unaddressed / partially-addressed prior critical/high stays a LIVE finding and counts toward the
// verdict; prior unanswered questions carry forward). The FLOOR holds exactly as consolidatedReview: the active
// lenses respect dimSubset (correctness always kept, dropped lenses raise nothing), and correctness
// additionally does a LIGHT full-plan pass to catch regressions OUTSIDE the diff. The github.md "Review
// comment" template is INLINED. Same { verdict, comment, questions, counts, round } contract as every path. If
// the plan file has < 2 commits (defensive — shouldn't happen at round >= 2) the agent signals `fallback` and we
// run the full review instead. Runs on MID (sonnet).
async function incrementalReview(located) {
  phase('Review')
  const round = located.priorRounds + 1
  if (dimSubset) log(`issue-review(plan): diff-scoped subset — judging ${activeReviewers.map((r) => r.key).join(', ')}; dropped ${droppedDims.join(', ') || '(none)'}`)
  const activeLensList = activeReviewers.map((r) => (r.expert ? r.role : r.key)).join(', ')
  const droppedClause = dimSubset ? ` Do NOT raise findings for the DROPPED lenses (${droppedDims.join(', ')}).` : ''
  const res = await withRetry(`plan-review:${issue}`, AGENT_ATTEMPTS, () => agent(
    `${base}
The project-phase calibration above is binding for THIS review — apply it as you judge every lens below.

You are the ${kind}-review team doing a DIFF-SCOPED RE-REVIEW (round ${round}) of a plan already reviewed at
least once. Judge the DELTA since the last round, NOT the whole plan from scratch.

1. GET THE REVISION UNDER REVIEW. The plan file is ${located.artifactPath}. Run:
     git log --format=%H -- ${located.artifactPath}
   This lists the file's commit hashes, NEWEST FIRST. Let HEAD = the 1st hash, PREV = the 2nd hash. The
   revision under review is the diff of the last revision:
     git diff PREV HEAD -- ${located.artifactPath}
   IF git log returns FEWER THAN 2 hashes for this file (defensive — should not happen at round >= 2), you
   CANNOT diff: return { fallback: true, diffRange: "", verdict: "NEEDS WORK", comment: "", questions: [],
   counts: { critical: 0, high: 0, medium: 0 } } and STOP — the workflow runs a full review instead.
   Otherwise set fallback: false and diffRange to "<PREV>..<HEAD>" (short hashes are fine).
2. GET THE PRIOR ROUND'S FINDINGS. Run:  gh issue view ${issue} --json comments
   Find the LATEST comment whose body starts with "## ${headerLine}" — that is the previous round's review
   (its "### {dimension} — {severity}" findings and its "Questions for author"). Reconcile each below.
3. JUDGE THE DELTA across the ACTIVE lenses (${activeLensList}):
   - For each ACTIVE lens, review ONLY the CHANGED sections of the plan (the diff from step 1) for NEW issues
     this revision introduces.${droppedClause}
   - FLOOR REGRESSION GUARD: correctness must ALSO do a LIGHT FULL-PLAN pass — read the WHOLE
     current ${located.artifactPath} (not just the diff) to catch regressions introduced OUTSIDE the changed
     lines. (Other lenses may Read more on demand but default to the diff.)
   - RECONCILE EACH PRIOR FINDING: classify it addressed / partially-addressed / not-addressed. An unaddressed
     OR partially-addressed prior CRITICAL or HIGH finding STAYS A LIVE finding — re-emit it as its own
     "### {dimension} — {severity}" block (note it is carried over) and COUNT it toward the verdict. Carry any
     prior UNANSWERED questions forward into "Questions for author".
   Every critical/high (NEW or carried-over) needs a 3-10 line evidence snippet, or drop it.
4. Decide the VERDICT (standards.md "Review Verdicts") over the LIVE findings (new + still-open prior):
   - RETHINK — any wrong-approach / boundary-violation / wrong-layering finding (fix direction first);
   - NEEDS WORK — one or more LIVE critical/high findings (after POC calibration);
   - LGTM — no LIVE critical or high findings (after POC calibration).
5. Write ONE consolidated comment EXACTLY in this format (this IS the github.md "Review comment" template —
   reproduce it precisely; do NOT read another file for it):
     ## ${headerLine} — round ${round} — {VERDICT}

     {one-paragraph synthesis — STATE that this is a diff-scoped incremental re-review, and recap which prior
     findings were addressed vs which remain open}

     ### {dimension} — {severity}
     {the concern, located by section or code path}
     > {3-10 line evidence snippet — REQUIRED on every critical/high}
     **Suggestion:** {what to change}

     ### Questions for author
     - {open question}

     <sub>reviewed ${kind} slug \`{slug}\` · round ${round}/${roundLimit} · {critical}c/{high}h/{medium}m</sub>
   Emit one "### {dimension} — {severity}" block per LIVE finding (new + carried-over); the "> " evidence
   snippet is required on every critical/high, plus a "**Suggestion:**". {slug} is the plan filename without
   its ".md" extension; the footer counts are over the LIVE findings and must match.
Return { fallback, diffRange, verdict, comment, questions, counts } where counts are over the LIVE findings
(new + still-open prior critical/high + mediums) and match the footer.`,
    { label: `plan-review:${issue}`, phase: 'Review', schema: INCREMENTAL_RESULT, model: MID },
  ))
  if (!res) throw new Error(`issue-review: diff-scoped plan re-review failed after ${AGENT_ATTEMPTS} attempts`)
  if (res.fallback) {
    log(`issue-review(plan): diff-scoped re-review found < 2 commits for ${located.artifactPath} — falling back to a full review`)
    return await fullPlanReview(located)
  }
  log(`issue-review(plan): diff-scoped re-review (round ${round}, diffing ${res.diffRange || 'last revision'})`)
  return { verdict: res.verdict, comment: res.comment, questions: res.questions, counts: res.counts, round }
}

// fullPlanReview(located) — the COMPLETE plan review (the original plan-gate behavior, unchanged): the size
// gate picks the single consolidated agent (the DEFAULT) vs the N-agent fan-out + synthesis (LARGE/complex
// plans; a small floor-protected subset also takes the consolidated shape). Used for FULL rounds (round 1 and
// every FULL_REVIEW_EVERY-th round) and as incrementalReview's < 2-commits fallback. Same { verdict, comment,
// questions, counts, round } contract as every path.
async function fullPlanReview(located) {
  const round = located.priorRounds + 1
  // Size decision (tunable thresholds above). Consolidate unless the plan is large/complex; a small
  // floor-protected subset (<= SMALL_TEAM active reviewers) also takes the cheaper consolidated shape.
  const bigPlan = located.planLines > LARGE_PLAN_LINES || located.taskCount > LARGE_PLAN_TASKS
  const smallSubset = !!(dimSubset && activeReviewers.length <= SMALL_TEAM)
  if (smallSubset || !bigPlan) {
    log(`issue-review(plan): consolidated (planLines=${located.planLines} taskCount=${located.taskCount}${smallSubset ? '; small subset' : ''})`)
    return await consolidatedReview('plan', located)
  }
  log(`issue-review(plan): fan-out (large plan: planLines=${located.planLines} taskCount=${located.taskCount})`)

  phase('Review')
  // Floor-protected dimension subset (.claude/rules/rigor.md): the plan-gate fan-out runs ONLY the active
  // reviewers (correctness always kept — the floor). Log the deliberate drop so a partial-coverage
  // verdict never looks full (no silent caps).
  if (dimSubset) {
    log(`issue-review(plan): dimension subset — running ${activeReviewers.map((r) => r.key).join(', ')} (+floor correctness); dropped ${droppedDims.join(', ') || '(none)'}`)
  }

  // Context pack (fan-out ONLY): resolve the code the plan touches ONCE, so the N reviewers don't each re-explore
  // the same files (the ~Nx re-explore tax that makes the fan-out slow). A MID agent returns a bounded (~6k-token)
  // pack — every touched file named with a 1-line role + the key code excerpts, summarized not dumped — injected
  // into every reviewer's prompt. Best-effort: if it fails after retries, log and fall back to the prior behavior
  // (each reviewer explores the code itself) — never block the review. Reviewers keep Read/Grep either way.
  const CONTEXT_PACK_SCHEMA = {
    type: 'object',
    properties: { pack: { type: 'string' } },
    required: ['pack'],
    additionalProperties: false,
  }
  const packRes = await withRetry(`contextpack:${issue}`, AGENT_ATTEMPTS, () => agent(
    `${base}
Resolve, ONCE, the existing code the proposed ${kind} touches, so a panel of reviewers need not each re-explore it.
The ${kind} to read: ${located.artifactPath} — read it in full, then open the files it changes or depends on.
The problem it must solve (issue #${issue}):
${located.problem}
Produce a BOUNDED "context pack" (HARD CAP ~6000 tokens — summarize, do NOT dump whole files):
  - a list of the touched files, each with a 1-line role ("what it is / why the plan touches it"); and
  - the KEY code excerpts a reviewer needs (the functions / types / signatures the plan modifies or relies
    on), trimmed to the load-bearing lines with an elision marker (…) where you cut.
Prefer breadth (name every touched file) over depth (do NOT paste long bodies); stay under the cap.
Return { pack } — one markdown string.`,
    { label: `contextpack:${issue}`, phase: 'Review', schema: CONTEXT_PACK_SCHEMA, model: MID },
  ))
  const packText = packRes && packRes.pack ? packRes.pack : null
  if (packText) log(`issue-review(plan): context pack resolved (${packText.length} chars) — shared with ${activeReviewers.length} reviewer(s)`)
  else log(`issue-review(plan): context pack unavailable — reviewers explore the code themselves (fallback)`)

  const reviews = await mapLimit(activeReviewers, REVIEW_CONCURRENCY, (r) =>
      withRetry(`review:${issue}:${r.key}`, AGENT_ATTEMPTS, () => agent(
        `You are the ${r.role}${r.expert ? ' (domain expert — not a single dimension)' : ` for the ${r.key} dimension`}, doing a CHANGE REVIEW of a proposed ${kind}.
${base}
Read the "Scope Guidance → Change review" guidance in ${r.rules}.
The proposed ${kind} to review: ${located.artifactPath} — read it in full.
The problem it must solve (issue #${issue}):
${located.problem}
${packText
  ? `Here is the code the ${kind} touches, PRE-RESOLVED — rely on this; only Read/Grep more if a specific finding requires it:\n${packText}`
  : `Also read the existing code the ${kind} touches, to judge feasibility and consequences.`}
${kind === 'spec'
  ? 'Judge the DESIGN: boundaries, layering, second-order consequences, and readiness — is this the right approach?'
  : 'Judge the implementation APPROACH: task correctness, TDD coverage, exact-path/type consistency, and layering — will following this plan produce correct, well-layered code?'}
Return findings[] (severity, dimension, location, finding, suggestion, snippet) and questions[]
(open questions the author must answer before we proceed). Evidence (a 3-10 line snippet) is
required for every critical/high.`,
        { label: `review:${issue}:${r.key}`, phase: 'Review', schema: REVIEW, model: MID },
      )).then((res) => (res ? { key: r.key, role: r.role, expert: !!r.expert, ...res } : null)),
  )
  // Graceful degradation: a reviewer that exhausted its retries is null in its slot. Drop it, but
  // LOUDLY — a partial-coverage verdict must never look like a full one (no silent caps). If EVERY
  // reviewer died there is nothing to judge, so fail rather than synthesize a vacuous LGTM.
  const ok = reviews.filter(Boolean)
  const dropped = activeReviewers.filter((_, i) => !reviews[i])
  if (dropped.length) {
    log(`issue-review: ${dropped.length}/${activeReviewers.length} reviewer(s) failed after ${AGENT_ATTEMPTS} attempts and were dropped — verdict is over the remaining ${ok.length}: ${dropped.map((r) => r.key).join(', ')}`)
  }
  if (!ok.length) throw new Error(`issue-review: all ${activeReviewers.length} reviewers failed after ${AGENT_ATTEMPTS} attempts — cannot synthesize a verdict`)
  const findings = ok.flatMap((r) => r.findings.map((f) => ({ ...f, reviewer: r.key })))
  const questions = ok.flatMap((r) => r.questions)
  const counts = {
    critical: findings.filter((f) => f.severity === 'critical').length,
    high: findings.filter((f) => f.severity === 'high').length,
    medium: findings.filter((f) => f.severity === 'medium').length,
  }

  phase('Synthesis')
  const synth = await withRetry(`synthesis:${issue}`, AGENT_ATTEMPTS, () => agent(
    `${base}
You are the team-lead. Resolve conflicts and synthesize ONE consolidated ${kind}-review comment.
The reviewed artifact is: ${located.artifactPath}
Findings (each tagged with the reviewer that raised it):
${JSON.stringify(findings, null, 2)}
Open questions for the author:
${JSON.stringify(questions, null, 2)}
Decide the VERDICT (standards.md "Review Verdicts"):
- RETHINK — any wrong-approach / boundary-violation / wrong-layering finding (fix direction first);
- NEEDS WORK — sound approach but one or more critical/high findings;
- LGTM — no critical or high findings.
Write the comment EXACTLY in this format (this IS the github.md "Review comment" template — reproduce it
precisely; do NOT read another file for it):
  ## ${headerLine} — round ${round} — {VERDICT}

  {one-paragraph synthesis}

  ### {dimension} — {severity}
  {the concern, located by section or code path}
  > {3-10 line evidence snippet — REQUIRED on every critical/high}
  **Suggestion:** {what to change}

  ### Questions for author
  - {open question}

  <sub>reviewed ${kind} slug \`{slug}\` · round ${round}/${roundLimit} · ${counts.critical}c/${counts.high}h/${counts.medium}m</sub>
Emit one "### {dimension} — {severity}" block per real finding (the "> " evidence snippet is required on
every critical/high, plus a "**Suggestion:**"); {slug} is the reviewed artifact's filename without its
".md" extension. Return { verdict, comment }.`,
    {
      label: `synthesis:${issue}`, phase: 'Synthesis', model: MID,
      schema: {
        type: 'object',
        properties: {
          verdict: { type: 'string', enum: ['RETHINK', 'NEEDS WORK', 'LGTM'] },
          comment: { type: 'string' },
        },
        required: ['verdict', 'comment'],
        additionalProperties: false,
      },
    },
  ))
  if (!synth) throw new Error(`issue-review: synthesis failed after ${AGENT_ATTEMPTS} attempts`)
  return { verdict: synth.verdict, comment: synth.comment, questions, counts, round }
}

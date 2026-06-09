export const meta = {
  name: 'issue-review',
  description: 'Review one drafted artifact (spec or plan) linked from a GitHub issue across 4 dimensions + the principal-engineer lens in change-review scope; return a consolidated verdict + comment markdown. Read-only on GitHub.',
  phases: [
    { title: 'Locate', detail: 'fetch issue + find the linked draft artifact' },
    { title: 'Review', detail: '5 reviewers judge the artifact in parallel' },
    { title: 'Synthesis', detail: 'verdict + one consolidated comment' },
  ],
}

// args: { issue: number, kind: 'spec' | 'plan', roundLimit?: number }
// The Workflow tool delivers tool-level `args` as a JSON string; parse it if so.
// (A nested workflow() call delivers an object, which passes straight through.)
let a = args || {}
if (typeof a === 'string') { try { a = JSON.parse(a) } catch { a = {} } }
const { issue, kind, roundLimit = 5, concurrency = 3, attempts = 3 } = a
if (!issue || (kind !== 'spec' && kind !== 'plan')) {
  throw new Error("issue-review requires args { issue: <number>, kind: 'spec' | 'plan' }")
}
const draftDir = kind === 'spec' ? 'docs/superpowers/specs/drafts' : 'docs/superpowers/plans/drafts'
const headerLine = kind === 'spec' ? '🔬 Spec review' : '🔍 Plan review'
// How many times to try each agent before giving up — intermittent provider faults (a dropped
// socket, a terminal API error) must not sink a whole review. Override via args.attempts.
const AGENT_ATTEMPTS = Math.max(1, attempts || 3)

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
  { key: 'correctness', role: 'correctness-reviewer', rules: '.claude/rules/correctness.md' },
  { key: 'maintainability', role: 'architect', rules: '.claude/rules/maintainability.md' },
  { key: 'performance', role: 'performance-reviewer', rules: '.claude/rules/performance.md' },
  { key: 'testability', role: 'quality-engineer', rules: '.claude/rules/testability.md' },
  { key: 'consequences', role: 'principal-engineer', rules: '.claude/rules/consequences.md', expert: true },
]

const base = `You have Read/Bash/Grep/Glob over the whole repo. Before reading the artifact, read:
- .claude/rules/architecture.md (domain context, project layering, navigation, evidence requirement)
- .claude/rules/principles.md and .claude/rules/standards.md (the review-verdict vocabulary)
- .claude/rules/project-phase.md (POC->MVP severity calibration)
- .claude/rules/github.md (the issue-flow conventions, incl. the review-comment format)
Read .claude/rules/project-phase.md FIRST and let it set your severity bar BEFORE you judge anything.
For every critical/high finding include a 3-10 line code/artifact snippet, or it is rejected.`

// Cap the reviewer fan-out (default 3, override via args.concurrency) so the 5-agent panel can't trip
// provider rate limits — the whole panel used to fire at once (up to the runtime's min(16, cores-2) cap).
const REVIEW_CONCURRENCY = Math.max(1, concurrency || 3)

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
// SPEC gate — single-agent review (POC token budget).
// The spec gate runs as ONE agent: locate + all-lens review + synthesis in a single
// pass, instead of the 5-reviewer fan-out the plan gate still uses below. One agent
// reads the rules, the spec, and the touched code ONCE and judges across all 4
// dimensions + the principal-engineer lens, applying the POC severity calibration. Same return
// contract { verdict, comment, questions, counts, round } as the plan path, so both
// callers — the issue-spec-review skill and the driver's runReviewStage — are unchanged.
// ---------------------------------------------------------------------------
if (kind === 'spec') {
  phase('Review')
  const SPEC_RESULT = {
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
  const res = await withRetry(`spec-review:${issue}`, AGENT_ATTEMPTS, () => agent(
    `${base}
The project-phase calibration above is binding for THIS review — apply it as you judge every lens below.

You are the ENTIRE spec-review team in one agent — the 4 dimensions (correctness, maintainability,
performance, testability) AND the principal-engineer lens (consequences). Do a CHANGE REVIEW of a
proposed spec:

1. Locate. Fetch the issue and its comments:
     gh issue view ${issue} --json number,title,body,comments
   This issue tracks a drafted spec under ${draftDir}/. From the latest "Spec" comment's
   "Full spec:" link line (or by matching the issue slug) find the artifact file path
   (repo-relative). Count how many prior "## ${headerLine}" comments already exist = priorRounds;
   round = priorRounds + 1.
2. Read the spec in full, and the existing code it touches, to judge feasibility and consequences.
   Consult each dimension/expert's "Scope Guidance -> Change review" section in .claude/rules/*.md
   (correctness, maintainability, performance, testability, consequences) as needed.
3. Judge the DESIGN across every lens: boundaries, layering, second-order consequences, and POC
   readiness — is this the right approach? Collect findings (severity, dimension, location, finding,
   suggestion) with a 3-10 line evidence snippet on every critical/high (no snippet => drop the
   finding), and open questions for the author.
4. Decide the VERDICT per .claude/rules/standards.md "Review Verdicts":
   - RETHINK — any wrong-approach / boundary-violation / wrong-layering finding (fix direction first);
   - NEEDS WORK — sound approach but one or more critical/high findings (after POC calibration);
   - LGTM — no critical or high findings (after POC calibration).
5. Write ONE consolidated comment EXACTLY in the "Review comment" format in .claude/rules/github.md:
   header "## ${headerLine} — round {round} — {VERDICT}", a one-paragraph synthesis, one
   "### {dimension} — {severity}" block per real finding (a "> " evidence snippet on every
   critical/high and a "**Suggestion:**"), a "### Questions for author" list, and the footer
   "<sub>reviewed spec slug \`{slug}\` · round {round}/${roundLimit} · {critical}c/{high}h/{medium}m</sub>".
Return { artifactPath, round, verdict, comment, questions, counts } where counts are over the
findings you KEPT after calibration and match the footer.`,
    { label: `spec-review:${issue}`, phase: 'Review', schema: SPEC_RESULT },
  ))
  if (!res) throw new Error(`issue-review: single-agent spec review failed after ${AGENT_ATTEMPTS} attempts`)
  return { verdict: res.verdict, comment: res.comment, questions: res.questions, counts: res.counts, round: res.round }
}

phase('Locate')
const located = await withRetry(`locate:${issue}`, AGENT_ATTEMPTS, () => agent(
  `${base}
Fetch issue #${issue} with its comments:
  gh issue view ${issue} --json number,title,body,comments
This issue tracks a drafted ${kind} under ${draftDir}/. From the latest "${kind}" comment
(its "Full ${kind}:" link line) or by matching the issue slug, find the artifact file path.
Also count how many prior "## ${headerLine}" comments already exist (the review-round history).
Return: artifactPath (repo-relative), problem (issue title + body + what it must solve),
priorRounds (integer count of prior ${kind} reviews).`,
  {
    label: `locate:${issue}`, phase: 'Locate',
    schema: {
      type: 'object',
      properties: {
        artifactPath: { type: 'string' },
        problem: { type: 'string' },
        priorRounds: { type: 'integer' },
      },
      required: ['artifactPath', 'problem', 'priorRounds'],
      additionalProperties: false,
    },
  },
))
if (!located) throw new Error(`issue-review: locate failed after ${AGENT_ATTEMPTS} attempts`)
const round = located.priorRounds + 1

phase('Review')
const reviews = await mapLimit(REVIEWERS, REVIEW_CONCURRENCY, (r) =>
    withRetry(`review:${issue}:${r.key}`, AGENT_ATTEMPTS, () => agent(
      `You are the ${r.role}${r.expert ? ' (domain expert — not a single dimension)' : ` for the ${r.key} dimension`}, doing a CHANGE REVIEW of a proposed ${kind}.
${base}
Read the "Scope Guidance → Change review" guidance in ${r.rules}.
The proposed ${kind} to review: ${located.artifactPath} — read it in full.
The problem it must solve (issue #${issue}):
${located.problem}
Also read the existing code the ${kind} touches, to judge feasibility and consequences.
${kind === 'spec'
  ? 'Judge the DESIGN: boundaries, layering, second-order consequences, and readiness — is this the right approach?'
  : 'Judge the implementation APPROACH: task correctness, TDD coverage, exact-path/type consistency, and layering — will following this plan produce correct, well-layered code?'}
Return findings[] (severity, dimension, location, finding, suggestion, snippet) and questions[]
(open questions the author must answer before we proceed). Evidence (a 3-10 line snippet) is
required for every critical/high.`,
      { label: `review:${issue}:${r.key}`, phase: 'Review', schema: REVIEW },
    )).then((res) => (res ? { key: r.key, role: r.role, expert: !!r.expert, ...res } : null)),
)
// Graceful degradation: a reviewer that exhausted its retries is null in its slot. Drop it, but
// LOUDLY — a partial-coverage verdict must never look like a full one (no silent caps). If EVERY
// reviewer died there is nothing to judge, so fail rather than synthesize a vacuous LGTM.
const ok = reviews.filter(Boolean)
const dropped = REVIEWERS.filter((_, i) => !reviews[i])
if (dropped.length) {
  log(`issue-review: ${dropped.length}/${REVIEWERS.length} reviewer(s) failed after ${AGENT_ATTEMPTS} attempts and were dropped — verdict is over the remaining ${ok.length}: ${dropped.map((r) => r.key).join(', ')}`)
}
if (!ok.length) throw new Error(`issue-review: all ${REVIEWERS.length} reviewers failed after ${AGENT_ATTEMPTS} attempts — cannot synthesize a verdict`)
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
Findings (each tagged with the reviewer that raised it):
${JSON.stringify(findings, null, 2)}
Open questions for the author:
${JSON.stringify(questions, null, 2)}
Decide the VERDICT per .claude/rules/standards.md "Review Verdicts":
- RETHINK — any wrong-approach / boundary-violation / wrong-layering finding (fix direction first);
- NEEDS WORK — sound approach but one or more critical/high findings;
- LGTM — no critical or high findings.
Write the comment EXACTLY in the "Review comment" format in .claude/rules/github.md, using:
header "## ${headerLine} — round ${round} — {VERDICT}", a one-paragraph synthesis, one
"### {dimension} — {severity}" block per real finding (with the "> " evidence snippet on every
critical/high and a "**Suggestion:**"), a "### Questions for author" list, and the footer
"<sub>reviewed ${kind} slug \`{slug}\` · round ${round}/${roundLimit} · ${counts.critical}c/${counts.high}h/${counts.medium}m</sub>".
Return { verdict, comment }.`,
  {
    label: `synthesis:${issue}`, phase: 'Synthesis',
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

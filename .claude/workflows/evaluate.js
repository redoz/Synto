export const meta = {
  name: 'evaluate',
  description: 'Full-codebase quality evaluation across 4 dimensions + the principal-engineer lens: baseline, file triage, two reviewer rounds (discovery then cross-pollinated verification), synthesis, and GitHub issue sync.',
  phases: [
    { title: 'Baseline', detail: 'dotnet build / test / format' },
    { title: 'Triage', detail: 'map src/ projects to relevant dimensions' },
    { title: 'Round 1', detail: '5 reviewers discover findings in parallel' },
    { title: 'Round 2', detail: '5 fresh reviewers verify + cross-pollinate' },
    { title: 'Synthesis', detail: 'scorecard, root causes, action plan' },
    { title: 'Sync', detail: 'reconcile findings with GitHub evaluation issues' },
  ],
}

// args: { skipSync?: boolean } — GitHub issue sync is OFF by default (findings-only run);
// pass { skipSync: false } to reconcile findings with `evaluation`-labelled issues.
const { skipSync = true, concurrency = 3, attempts = 3 } = args || {}
// How many times to try each agent before giving up — intermittent provider faults (a dropped
// socket, a terminal API error) must not sink a whole evaluation. Override via args.attempts.
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
const FINDINGS = {
  type: 'object',
  properties: { findings: { type: 'array', items: FINDING } },
  required: ['findings'],
  additionalProperties: false,
}
const R2 = {
  type: 'object',
  properties: {
    findings: { type: 'array', items: FINDING },
    rejected: {
      type: 'array',
      items: {
        type: 'object',
        properties: { location: { type: 'string' }, reason: { type: 'string' } },
        required: ['location', 'reason'],
        additionalProperties: false,
      },
    },
  },
  required: ['findings', 'rejected'],
  additionalProperties: false,
}

// 4 dimensions + the principal-engineer lens.
const REVIEWERS = [
  { key: 'correctness', role: 'correctness-reviewer', rules: '.claude/playbook/correctness.md' },
  { key: 'maintainability', role: 'architect', rules: '.claude/playbook/maintainability.md' },
  { key: 'performance', role: 'performance-reviewer', rules: '.claude/playbook/performance.md' },
  { key: 'testability', role: 'quality-engineer', rules: '.claude/playbook/testability.md' },
  { key: 'consequences', role: 'principal-engineer', rules: '.claude/playbook/consequences.md', expert: true },
]

const base = `You have Read/Bash/Grep/Glob over the whole repo. Before reading source, read:
- .claude/playbook/architecture.md (domain context, project layering, navigation, evidence requirement)
- .claude/playbook/principles.md and .claude/playbook/standards.md
- .claude/playbook/project-phase.md (POC->MVP severity calibration)
- README.md, the per-project *.csproj under src/*, plus docs/superpowers/specs/ (the authoritative design docs) to orient
For every critical/high finding include a 3-10 line code snippet, or it is rejected.`

// Cap the reviewer fan-out (default 3, override via args.concurrency) so the 5-agent panel can't trip
// provider rate limits — both rounds used to fire the whole panel at once (up to min(16, cores-2)).
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

phase('Baseline')
const baseline = await withRetry('baseline', AGENT_ATTEMPTS, () => agent(
  `${base}
Run the solution baseline and report results verbatim (summarize long output):
- dotnet build --no-restore -c Debug   (solution Synto.slnx; report the build result AND any
  analyzer warnings it surfaces)
- dotnet test --no-build -c Debug      (Microsoft Testing Platform v2 runner — xUnit v3 + Verify
  snapshot tests, ~35 tests)
- dotnet format --verify-no-changes
Build failure = critical correctness finding. Failing tests = critical/high correctness.
If any test is a known environmental failure (not a code defect), record it under knownFailures
and do NOT count it as a correctness finding.
Analyzer warnings are findings (the correctness/performance analyzers can be high), but NOT a hard
green-gate failure — TreatWarningsAsErrors is not set in Synto. fmt drift is low.`,
  {
    label: 'baseline',
    phase: 'Baseline',
    schema: {
      type: 'object',
      properties: {
        build: { type: 'string' },
        test: { type: 'string' },
        analyzers: { type: 'string' },
        fmt: { type: 'string' },
        knownFailures: { type: 'array', items: { type: 'string' } },
      },
      required: ['build', 'test', 'analyzers', 'fmt', 'knownFailures'],
      additionalProperties: false,
    },
  },
))
if (!baseline) throw new Error(`evaluate: baseline failed after ${AGENT_ATTEMPTS} attempts`)

phase('Triage')
const triage = await withRetry('triage', AGENT_ATTEMPTS, () => agent(
  `${base}
Produce a file triage map: for each project under src/ (src/Synto, src/Synto.SourceGenerator,
src/Synto.Bootstrap, src/Synto.Diagnostics), list which quality dimensions are most relevant
(use each project's *.csproj for what it owns and its dependency/package direction, plus the repo
README.md for intent/functionality). This guides reviewers on what to read in depth.`,
  { label: 'triage', phase: 'Triage', schema: { type: 'object', properties: { map: { type: 'string' } }, required: ['map'], additionalProperties: false } },
))
if (!triage) throw new Error(`evaluate: triage failed after ${AGENT_ATTEMPTS} attempts`)

const sharedCtx = `${base}
Baseline results:
  build: ${baseline.build}
  test: ${baseline.test}
  analyzers: ${baseline.analyzers}
Known test failures: ${JSON.stringify(baseline.knownFailures)}
File triage map (read your high-relevance projects in depth, skim the rest):
${triage.map}`

phase('Round 1')
const round1 = await mapLimit(REVIEWERS, REVIEW_CONCURRENCY, (r) =>
    withRetry(`r1:${r.key}`, AGENT_ATTEMPTS, () => agent(
      `You are the ${r.role}${r.expert ? ' (domain expert — not a single dimension)' : ` for the ${r.key} dimension`}.
${sharedCtx}
Read your ${r.expert ? 'focus areas' : 'checklist'} in ${r.rules}.
${r.expert ? 'Use your focus areas to find issues the dimension specialists miss. Tag each finding with the quality dimension it counts toward.' : `Review the codebase for ${r.key}.`}
Return findings[] (severity, dimension, location, finding, suggestion, snippet).`,
      { label: `r1:${r.key}`, phase: 'Round 1', schema: FINDINGS },
    )).then((res) => (res ? { key: r.key, role: r.role, expert: !!r.expert, findings: res.findings } : null)),
)

// Graceful degradation: a reviewer that exhausted its retries is null in its slot — drop it but LOUDLY.
// Round 1 all-failing is survivable (Round 2 still discovers findings), so log only; don't fail here.
const ok1 = round1.filter(Boolean)
const dropped1 = REVIEWERS.filter((_, i) => !round1[i])
if (dropped1.length) {
  log(`evaluate Round 1: ${dropped1.length}/${REVIEWERS.length} reviewer(s) failed after ${AGENT_ATTEMPTS} attempts and were dropped: ${dropped1.map((r) => r.key).join(', ')}`)
}
const r1by = {}
for (const r of ok1) r1by[r.key] = r.findings
const allR1 = ok1.flatMap((r) => r.findings)
const highR1 = allR1.filter((f) => f.severity === 'critical' || f.severity === 'high')
const medR1 = allR1.filter((f) => f.severity === 'medium')

phase('Round 2')
const round2 = await mapLimit(REVIEWERS, REVIEW_CONCURRENCY, (r) => {
    // Domain experts need more cross-dimension context to spot convergent/spec patterns.
    const cross = r.expert
      ? `All other dimensions' findings (you need mediums too):\n${JSON.stringify([...highR1, ...medR1], null, 2)}`
      : `Critical/high findings from other dimensions:\n${JSON.stringify(highR1.filter((f) => f.dimension !== r.key), null, 2)}`
    return withRetry(`r2:${r.key}`, AGENT_ATTEMPTS, () => agent(
      `You are a FRESH ${r.role} (clean context) doing Round 2 verification + cross-pollination.
${sharedCtx}
Read ${r.rules}.
Your own Round 1 findings (verify each critical/high against the real code; drop any you cannot confirm):
${JSON.stringify(r1by[r.key] || [], null, 2)}
${cross}
Tasks: VERIFY your criticals/highs (confirm or reject with code evidence), CHALLENGE
false positives in others' findings, REINFORCE ones you independently agree with,
DISCOVER new findings visible only with cross-dimensional context, ADJUST severities.
${r.expert ? 'Also: promote convergent mediums to high/critical based on shared trajectory; challenge workarounds where a proper mechanism exists.' : ''}
Return findings[] (the kept + new, post-verification) and rejected[] (location + why).`,
      { label: `r2:${r.key}`, phase: 'Round 2', schema: R2 },
    )).then((res) => (res ? { key: r.key, role: r.role, expert: !!r.expert, ...res } : null))
})

// Same graceful degradation as Round 1 — but if EVERY Round 2 reviewer died there are no verified
// findings, and synthesizing would emit a vacuous PASS scorecard, so fail loudly instead.
const ok2 = round2.filter(Boolean)
const dropped2 = REVIEWERS.filter((_, i) => !round2[i])
if (dropped2.length) {
  log(`evaluate Round 2: ${dropped2.length}/${REVIEWERS.length} reviewer(s) failed after ${AGENT_ATTEMPTS} attempts and were dropped: ${dropped2.map((r) => r.key).join(', ')}`)
}
if (!ok2.length) throw new Error(`evaluate: all ${REVIEWERS.length} Round 2 reviewers failed after ${AGENT_ATTEMPTS} attempts — cannot synthesize a scorecard`)
const finalFindings = ok2.flatMap((r) => r.findings)
const rejected = ok2.flatMap((r) => r.rejected.map((x) => ({ ...x, dimension: r.key })))

phase('Synthesis')
const synth = await withRetry('synthesis', AGENT_ATTEMPTS, () => agent(
  `${base}
You are the team-lead. Synthesize the final evaluation report from the verified
Round 2 findings. You have code access to resolve conflicts.
Verified findings:
${JSON.stringify(finalFindings, null, 2)}
Rejected (for the audit trail):
${JSON.stringify(rejected, null, 2)}
Produce the full report markdown per .claude/playbook/standards.md scoring model:
Quality Scorecard table (per-dimension score + counts), quality gate PASS/FAIL,
Findings by dimension, Domain Expert Assessment (Consequences), Root Causes
(findings sharing a cause, grouped), Prioritized Action Plan (Immediate / Next
sprint / Backlog), Strengths (3-5 bullets), Verification Notes (rejected + why).
Return the report as one markdown string and the gate result.`,
  {
    label: 'synthesis',
    phase: 'Synthesis',
    schema: {
      type: 'object',
      properties: { report: { type: 'string' }, gate: { type: 'string', enum: ['PASS', 'FAIL'] } },
      required: ['report', 'gate'],
      additionalProperties: false,
    },
  },
))
if (!synth) throw new Error(`evaluate: synthesis failed after ${AGENT_ATTEMPTS} attempts`)

let syncSummary = 'GitHub sync skipped.'
if (!skipSync) {
  phase('Sync')
  const inventory = await withRetry('sync:inventory', AGENT_ATTEMPTS, () => agent(
    `${base}
Fetch all open evaluation-tracked issues INCLUDING comments (comments carry later
decisions — deferred, superseded, handled by a PR):
  gh issue list --label evaluation --state open --limit 200 --json number,title,labels,body,comments
Return the raw JSON as a string.`,
    { label: 'sync:inventory', phase: 'Sync', schema: { type: 'object', properties: { issuesJson: { type: 'string' } }, required: ['issuesJson'], additionalProperties: false } },
  ))
  if (!inventory) throw new Error(`evaluate: sync inventory failed after ${AGENT_ATTEMPTS} attempts`)
  const sync = await withRetry('sync:apply', AGENT_ATTEMPTS, () => agent(
    `${base}
You are the github-sync agent. You have gh + code access. Reconcile this evaluation's
findings with existing GitHub issues. Use the \`evaluation\` label on all (create it
and dimension/severity labels if missing; prefer existing repo labels otherwise).
First get today's date: run \`date +%F\` and use it wherever {today} appears below.

Current verified findings:
${JSON.stringify(finalFindings, null, 2)}

Existing open evaluation issues (body + comments):
${inventory.issuesJson}

Do all of:
1. STALE REVIEW — for each existing issue with no matching finding, read the code and
   decide: Fixed (close with "Resolved — no longer found in evaluation {today}."),
   Still valid (keep open, comment that manual review confirms it), Needs update
   (edit body/labels), or Already superseded/deferred per a comment (respect it).
2. SYNC FINDINGS — match each finding by file+description. Existing: update body/labels
   if changed, else comment "Re-confirmed in evaluation {today}." New: create issue
   titled "{dimension}: {short finding}" with labels evaluation,{dimension},{severity}
   and the standard body (Dimension/Severity/Location, ## Finding, ## Suggestion,
   ## Root Cause, footer "Found in evaluation {today}").
Return a summary: created/updated/closed/kept counts with issue numbers, and total open.`,
    { label: 'sync:apply', phase: 'Sync', schema: { type: 'object', properties: { summary: { type: 'string' } }, required: ['summary'], additionalProperties: false } },
  ))
  if (!sync) throw new Error(`evaluate: sync apply failed after ${AGENT_ATTEMPTS} attempts`)
  syncSummary = sync.summary
}

return { report: synth.report, gate: synth.gate, syncSummary, counts: { findings: finalFindings.length, rejected: rejected.length } }

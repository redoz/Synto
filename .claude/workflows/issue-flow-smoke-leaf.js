// .claude/workflows/issue-flow-smoke-leaf.js
export const meta = {
  name: 'issue-flow-smoke-leaf',
  description: 'Smoke-test leaf: parallel() of N trivial agents, so the driver smoke test can prove the workflow()-inside-parallel() nesting shape. No side effects.',
  phases: [{ title: 'Leaf', detail: 'parallel trivial agents' }],
}
let a = args || {}
if (typeof a === 'string') { try { a = JSON.parse(a) } catch { a = {} } }
const width = a.width || 9        // inner fan-out width (production review width = 9)
const branch = a.branch ?? '?'
phase('Leaf')
const ones = await parallel(
  Array.from({ length: width }, (_, i) => () =>
    agent(`Reply with exactly the single character: 1 (nothing else). [leaf ${branch}#${i}]`, {
      label: `smoke-leaf:${branch}:${i}`,
      phase: 'Leaf',
      schema: { type: 'object', additionalProperties: false, properties: { one: { type: 'integer' } }, required: ['one'] },
    }).then(() => 1),
  ),
)
return { branch, width, sum: ones.length }

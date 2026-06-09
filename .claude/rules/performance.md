# Performance

Evaluate whether the generator uses resources appropriately and keeps the build fast.

## Checklist

### Hot Paths
- For a build-time generator the hot path is **the compiler and the IDE**, not runtime.
  The generator runs on **every keystroke** in a loaded solution; the #1 performance
  concern is **incremental-generator cacheability** — staying cached so unrelated edits do
  no work.
- Are there hot paths doing unnecessary work? (re-parsing or re-quoting on edits that don't
  touch a `[Template]`; re-walking the whole `SyntaxTree` where a syntactic predicate would
  gate it)
- Are there O(n²) or worse patterns over data that grows with the file/compilation? (nested
  scans of members, repeated `SyntaxNode` walks, quadratic using-set merges)
- Is per-invocation work **bounded**? Quoting one template should scale with that
  template's size, not with the whole compilation.

### Incremental Pipeline (cacheability)
- Are pipeline models **equatable value types** so Roslyn can short-circuit unchanged
  stages? (`TemplateInfo`, `DiagnosticInfo`, `EquatableArray<T>`, `LocationInfo`) A model
  that isn't structurally equatable — or that uses default reference equality on an array —
  invalidates every downstream step on every run.
- Does any stage **capture `Compilation`, `ISymbol`, or `SyntaxNode`**? That is the
  cardinal cacheability sin: it pins large graphs in the cache and defeats equality, so the
  generator re-runs continuously. Extract the few primitives you need into the value-type
  model and drop the live references.
- Is the **predicate cheap and selective**? Prefer `ForAttributeWithMetadataName`
  (syntactic, fast) over scanning every node and asking the semantic model; keep the
  predicate allocation-free and the transform minimal.
- Are stages **split so unrelated edits don't cascade**? Keep diagnostics, parse, and emit
  separated so that a body edit doesn't re-run target validation, and a usings change
  doesn't re-quote bodies.

### Efficiency
- Is there unnecessary allocation or materialization in the pipeline? (re-`ToImmutableArray()`-ing
  unchanged collections, LINQ chains allocating per node, boxing value types, building
  throwaway strings) — all of this runs on every keystroke.
- Does the quoter avoid redundant `NormalizeWhitespace`/full re-render when a smaller
  construction would do, and avoid rebuilding the whole using set per node?
- Are `EquatableArray<T>`, spans, or pooled builders used where a naive `List`/array
  re-allocation would churn?
- Is the post-init injected surface emitted **once** (it's constant), not rebuilt per
  compilation?

## Scope Guidance

- **Full evaluation**: Trace the incremental pipeline end to end — predicate selectivity,
  the equatability of every stage, what each stage captures, and allocation in the
  quoter/formatter hot loops. Confirm an unrelated edit produces a cache hit.
- **Change review**: Focus on whether changes break cacheability (a non-equatable model, a
  captured `Compilation`/`ISymbol`/`SyntaxNode`, a broadened predicate), add
  allocation/materialization to a per-keystroke stage, or introduce an O(n²) walk.

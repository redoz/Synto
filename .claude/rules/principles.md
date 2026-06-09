# Design Principles

Stable rules about how Synto is built. These change rarely. All review and
evaluation skills reference these principles.

## Layering

- **Runtime vs generator is the primary seam.** `src/Synto` (ships as `Synto.Core`) is the
  runtime/toolkit library — markers, emitted helpers, and the quoter machinery. The
  generators (`src/Synto.SourceGenerator` → `Synto`, `src/Synto.Diagnostics` →
  `Synto.Diagnostics`) consume that library at analysis time; consumers do not.
- **The umbrella generator owns all syntax features as folders/namespaces.** `Templating/`
  today, `Matching/` next — each a sibling under `src/Synto.SourceGenerator`, never a new
  package. A feature adds its own injected markers/helpers under its own namespace and does
  not entangle them with another feature's.
- **`Synto.Diagnostics` is a separate product.** A general-purpose `[Diagnostic]` generator
  for any Roslyn author; its generated output is self-contained and pulls in no Synto
  runtime. Keep it independent of the syntax toolkit.
- **Self-host bootstrap chain is load-bearing.** `src/Synto` consumes `src/Synto.Bootstrap`
  as an analyzer to generate part of its own quoter. Don't break the build ordering
  (Bootstrap before `Synto`) when changing references or packability.

## Boundaries

- **The consumer-facing surface is minimal and closed.** Only the five markers
  (`TemplateAttribute`, `InlineAttribute`, `RuntimeAttribute`, `TemplateOption`,
  `Syntax`/`Syntax<T>`) and the three emitted helpers (`ToSyntax`, `ToTypeSyntax`,
  `OrNullLiteralExpression`) are allowed to reach consumer code. Everything else
  (`CSharpSyntaxQuoter`, `SyntaxFormatter`, `UsingDirectiveSet`, `SymbolExtensions`, the
  list extensions) is **generator-internal** and must have zero occurrences in consumer output.
- **The injected surface is `internal`; the emitted helpers are `file`-scoped.** Injected
  markers stay `internal` so they neither pollute the consumer's public API nor collide
  across referenced assemblies, yet stay visible to attribute discovery. Helpers are emitted
  as `file static class` into the single file that calls them, so a private copy can never
  clash with `Synto.Core`'s public copies (the CS0121 ambiguity an injected `internal` copy
  would cause). Don't widen either accessibility without understanding the collision it reopens.
- **The `Synto` generator package carries no forced runtime dependency.** Consumers add one
  package and get self-contained generated code. `Synto.dll` ships only under
  `analyzers/dotnet/cs` (analysis-time), never as a `lib/` runtime dependency.
- **Package identity is a contract.** `Synto` (generator), `Synto.Core` (runtime),
  `Synto.Diagnostics` (diagnostics) name a fixed three-package model. Renaming or merging
  them is a boundary change, not a refactor.

## Incremental-generator cacheability

- **Pipeline values are equatable value types.** Everything flowing between provider stages
  must compare by value (records, `EquatableArray<T>`, primitives). A non-equatable value in
  the pipeline silently defeats caching.
- **Never capture `Compilation`, `ISymbol`, `SemanticModel`, or `SyntaxNode` into pipeline
  state.** Do all semantic work inside the transform and emit a small equatable result
  (`TemplateGenerationResult` = generated text + `EquatableArray<DiagnosticInfo>`). Capturing
  any of these roots the compilation in memory and re-runs the generator on every keystroke.
- **Cacheability is correctness *and* performance.** A change that breaks it is not a
  micro-optimization deferral — it regresses the editor experience for every consumer.

## Abstraction Integrity

- **Single source of truth for the injected surface.** Markers and helpers are authored once
  in `src/Synto`, embedded as resources, and rewritten at inject time
  (`public`→`internal` for markers, `public`→`file` for helpers). The generator must not
  re-declare a marker or helper; injection must never drift from `Synto.Core`'s definition.
- **The quoter hides the factory mechanics.** Callers compose syntax through the markers and
  the toolkit; they should not need to know how `CSharpSyntaxQuoter`, `UsingDirectiveSet`, or
  `SyntaxFormatter` assemble the output.
- **Generated output is self-contained by construction.** Helper injection is decided by
  *scanning the emitted factory* for real calls (not by builder flags), so the surface is
  complete for whatever the generator emits and nothing unused is dragged in. When an
  abstraction forces a consumer to reach for a generator-internal type, the boundary is
  wrong — move it.

## Simplicity

- **No backward-compatibility layers.** Synto is pre-1.0 and experimental; when something
  changes, change it rather than carrying a compat shim. (The deliberate exception is the
  *consumer-visible* contract — markers, generated-output shape, package identity — which is
  a one-way door and is treated as such; see `project-phase.md`.)
- **No speculative abstraction.** Build for what ships now — Templating — and let Matching
  arrive as a sibling namespace when it is actually built, not as a pre-carved framework.
- **Code belongs where it fits the layering.** Runtime helpers in `Synto.Core`,
  generator logic in the umbrella generator, embedded resources as the bridge. Convenience
  placement ("I'll just declare it in the generator for now") creates drift between the
  injected copy and the real one.
- Three similar lines of code is better than a premature abstraction.

## Platform constraints

- **`netstandard2.0` everywhere.** Generators and the runtime target `netstandard2.0` so they
  load in the compiler host; don't reach for APIs unavailable there. The `Roslyn 5.0` floor is
  forward-compatible (a generator built against 5.0 loads on any host ≥ 5.0) — raising it
  raises the minimum SDK consumers need, so treat the floor as a deliberate, costly decision.
- **The generator runs inside the compiler.** No file I/O, network, environment, or process
  work at generation time; inputs come only from the compilation. Generator analyzer rules are
  enforced (`EnforceExtendedAnalyzerRules`).
- **Failures are diagnostics, not exceptions.** A generation problem is reported through a
  `DiagnosticDescriptor` (`SY*`) / `DiagnosticInfo` and surfaced to the consumer's build; an
  exception escaping the generator is a bug. Catch-convert-report on reachable error paths.
- **No secrets in source or output.** `NUGET_API_KEY`, `GITHUB_TOKEN`, and any `*_TOKEN` /
  `*_SECRET` / `*_KEY` / bearer-token value must never be committed or emitted into generated
  code or diagnostics.

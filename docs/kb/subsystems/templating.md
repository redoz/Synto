---
type: Subsystem
title: Templating
description: "[Template] → generated factory: the staged binding-time pipeline that splits a method body into quoted output vs live, factory-time computation."
resource: src/Synto.SourceGenerator/Templating
project: projects/synto-sourcegenerator
entrypoints: ["src/Synto.SourceGenerator/Templating/TemplateFactorySourceGenerator.cs:17"]
tags: [templating, staging, binding-time, pipeline]
timestamp: 2026-06-29T00:00:00Z
---

# Responsibility

The largest and gnarliest subsystem. A `[Template(typeof(Target))]` method/class
is the *source*; the generator completes it into a static **factory** on the
target that builds syntax. The hard part is **binding-time staging**: deciding
which parts of the body are emitted as output syntax ("quoted") and which run at
factory-build time over live values ("staged"), unrolling control flow driven by
live data.

# Mental model: quoted vs staged

- **Quoted** (default) — emitted as output-world syntax. The body literally
  becomes the code the factory produces.
- **Live roots** — `Template.Parameter<T>()`, `[Unquote]`/`Template.Unquote<T>()`,
  `[Quote]`/`Template.Quote<T>()`. Values supplied/computed at factory time.
- **Staged control** — a `foreach`/`for`/`while`/`if` *driven by a live root* is
  **unrolled** at factory time. Driven only by quoted values → stays a runtime
  construct. `[Quote]` deliberately lifts a value **without** making it drive
  control (the liveness shield).
- `BindingTimeClassifier` partitions the body into `Quoted` / `Unquote` /
  `StagedControl` by seeding from live roots and propagating via def-use; an
  "impossible cut" (live depends on quoted) is error **SY1013**.

# Layout (post-modularization 2026-06-29)

The former 1234-LOC `TemplateFactorySourceGenerator` was decomposed into a thin
shell + an ordered builder + a document assembler, each ≤ ~400 LOC:

- **Shell** — `TemplateFactorySourceGenerator.cs:17` (`[Generator]`), `Initialize`
  (`:20`); the `GenerateTemplate` transform is tracking-named `Transform`/`Result`
  (`TrackingNames.cs:10`–`11`).
- **Document assembler** — `TemplateDocumentBuilder.Build` (`TemplateDocumentBuilder.cs:22`):
  invokes the factory builder, then wraps the factory in target ancestry, scan-injects
  file-local helpers (`FindReferencedHelpers` vs `FileLocalHelpers` registry), merges
  usings, prepends `#nullable enable`, formats, names the `.g.cs`.
- **Factory builder** — `TemplateFactoryBuilder.Build` (`TemplateFactoryBuilder.cs:24`):
  ordered per-feature steps over a `TemplateBuildContext` accumulator (whose field order
  is the load-bearing step contract).

# Pipeline (ordered, the spine)

The ordered steps run inside `TemplateFactoryBuilder.Build` (`TemplateFactoryBuilder.cs:24`):

| # | Step | Where |
|---|------|-------|
| 0 | `[Generator]` over `[Template]`; transform | `TemplateFactorySourceGenerator.cs:17`, `:20` |
| 1 | Discover target/source/options | `TemplateInfo.cs:26` |
| 2 | Validate (target class/partial, declared in source; SY1001–4) | `TemplateValidator.cs:18` |
| 3 | `TemplateScope` + trim foreign child `[Template]`s | `TemplateScope.cs`, `TemplateScopedWalker.cs:18` |
| 4–5 | Lift staged type params / `[Splice]` / `[Syntax]` params | `StagedTypeParameterFinder.cs`, `SpliceParameterFinder.cs`, `SyntaxParameterFinder.cs` |
| 6 | Staged roots + **classify binding time** | `StagedParameterFinder.cs`, `StagedRootFinder.cs`, `QuoteCallFinder.cs`, `BindingTimeClassifier.cs:102` |
| 7 | Impossible-cut check (live ⟵ quoted) → SY1013 | `TemplateFactoryBuilder.cs` |
| 8 | Discover staged regions (loops/ifs to unroll) + consumed nodes | `StagedRegionFinder.cs:14` |
| 9 | Scalar member-access fold (`columns.Count` → literal) | `TemplateFactoryBuilder.cs` |
| 10–12 | Lift staged params/locals; init `ValueLift` (lazy `[Runtime]` cache) | `ValueLift.cs:16`, `:68` |
| 13–15 | Lift `[Unquote]`/`[Quote]` roots + inline `Quote(value)` | `QuoteParameterFinder.cs`, `QuoteCallFinder.cs` |
| 16 | Syntax-builder facade calls | `FacadeCallFinder.cs:19` (`FindBuilderCalls`), `SyntaxBuilderRegistry.cs:17`, `FacadeArgumentBinder.cs:16`, `FacadeShape.cs` |
| 17 | Inline `Template.Splice(node)` | `SpliceCallFinder.cs` |
| 18 | Emit staged regions (unroll → `BuildList`) | `StagedRegionEmitter.cs:20` → `StagedLivenessAnalysis.cs`, `RootRenameRewriter.cs`, `StagedScaffoldBuilder.cs:19`, `StagedHelperCallFactory.cs` |
| 19 | `[Splice]` member generators → local functions | `SpliceMemberGeneratorEmitter.cs:24` |
| 20 | Branch pruning | `BranchPruneIdentifier.cs` |
| 21 | Construct `TemplateSyntaxQuoter`, quote the source | `TemplateSyntaxQuoter.cs:30`, `TemplateSyntaxQuoterInvoker.cs:20` |
| 22 | Assemble factory + helpers + unit; `AddSource` | `TemplateDocumentBuilder.cs:22` |

Staged-string interpolation bare holes are fused into literal text during quoting by
`InterpolationFold` (`InterpolationFold.cs:41`), which the quoter delegates to via a
`Visit` callback (`TemplateSyntaxQuoter.cs:43`, `:74`).

# Scope: child templates

`TemplateScope` / `TemplateScopedWalker` (`TemplateScopedWalker.cs:18`) implement
the **child-template ownership boundary**: a nested method-level `[Template]`
inside a carrier class is processed on its own and **excluded** from the parent's
live-staging walk. Every per-template finder derives from `TemplateScopedWalker`,
which skips foreign child subtrees in `VisitMethodDeclaration`.

# Consumer surface (in `src/Synto/Templating/`)

- `[Template]` — `TemplateAttribute.cs:6` (Method | Class | Struct; `Options` for Bare/Single).
- `[Unquote]` — `UnquoteAttribute.cs` (param/type; lifts, **may drive** control).
- `[Quote]` — `QuoteAttribute.cs` (param-only; lifts, **never** drives control).
- `[Splice]` — `SpliceAttribute.cs` (param/type/method; verbatim, no lift).
- `[SyntaxBuilder]` + `[Quoted]` + `[ReturnType]` — facade builders.
- `[Runtime]` — `RuntimeAttribute.cs` (marks a class exposing `ToSyntax(this T)` for custom-type lifts).
- Inline facades on `Template` (`Template.Parameter.cs`): `Parameter<T>()` (`:22`),
  `Unquote<T>()` (`:34`), `Quote<T>()` (`:48`), `Splice()` (`:59`), `Member<T>()`
  (`:71`), `TypeOf()` (`:80`) — all inert (`=> default!`), recognized by binding.

> `Live<T>()` is a **method** carrier, not `[Live]`; a Live/Inline → Unquote/Splice
> rename is in flight (this doc uses target names). See [glossary](/glossary.md).

# Invariants

- Honor the [cacheability contract](/architecture/incremental-pipeline.md): every
  stage tracking-named (`TrackingNames.cs:10`), equatable results only. `TemplateBuildContext`
  is transform-local mutable scratch — never equatable, never crosses a provider.
- `TemplateBuildContext` **field order is the step-ordering contract** — steps in
  `TemplateFactoryBuilder.Build` (`TemplateFactoryBuilder.cs:24`) must not be reordered;
  live-region container replacements are added LAST.
- One `ValueLift` (`ValueLift.cs:16`) per build, with a lazy `[Runtime]` converter cache
  shared across `[Unquote]`/`[Quote]`/inline-`Quote` lifts.
- File-local helpers are injected by **scanning the emitted factory** against the
  `FileLocalHelpers` registry (`TemplateDocumentBuilder.FindReferencedHelpers`), never via flags.
- Impossible cut (live ⟵ quoted) is a hard error (SY1013), not a silent miscompile.

# Related

- [Incremental pipeline](/architecture/incremental-pipeline.md) · [Quoting](/subsystems/quoting.md)
- [Decorations](/subsystems/decorations.md) · [ObjectReader example](/examples/objectreader.md)

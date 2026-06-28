---
type: Subsystem
title: Templating
description: "[Template] → generated factory: the staged binding-time pipeline that splits a method body into quoted output vs live, factory-time computation."
resource: src/Synto.SourceGenerator/Templating
project: projects/synto-sourcegenerator
entrypoints: ["src/Synto.SourceGenerator/Templating/TemplateFactorySourceGenerator.cs:17"]
tags: [templating, staging, binding-time, pipeline]
timestamp: 2026-06-28T00:00:00Z
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

# Pipeline (ordered, the spine)

Orchestrated by `TemplateFactorySourceGenerator.ProcessTemplate`. Key stages:

| # | Step | Where |
|---|------|-------|
| 0 | `[Generator]` registration over `[Template]` | `TemplateFactorySourceGenerator.cs:17` |
| 1 | Discover target/source/options | `TemplateInfo.cs:26` |
| 2 | Validate (target partial, declared in source) | `TemplateFactorySourceGenerator.cs:81` |
| 3 | Build `TemplateScope` (child-template boundary) | `TemplateScope.cs:46` |
| 4 | Find child `[Template]`s + staged type params | `ChildTemplateFinder.cs:21`, `StagedTypeParameterFinder.cs` |
| 5 | Find `[Splice]` / plain `ExpressionSyntax` params | `SpliceParameterFinder.cs`, `SyntaxParameterFinder.cs` |
| 6 | Find live `Template.Parameter<T>()` roots | `StagedParameterFinder.cs:67` |
| 7 | Find `Unquote` locals + `[Unquote]` params | `StagedRootFinder.cs` |
| 8 | Find inline `Quote(value)` boundaries | `QuoteCallFinder.cs` |
| 9 | **Classify binding time** | `BindingTimeClassifier.cs:102` |
| 10 | Discover staged regions (loops/ifs to unroll) | `StagedRegionEmitter.cs:72` |
| 11 | Staged scalar member-access fold (e.g. `columns.Count` → literal) | `TemplateFactorySourceGenerator.cs:673` |
| 12–16 | Lift params/locals/`[Unquote]`/`[Quote]`/inline `Quote` into factory params + `.ToSyntax()` calls | `TemplateFactorySourceGenerator.cs:696`–`984` |
| 17 | Syntax-builder facade calls | `SyntaxBuilderFinder.cs` |
| 18 | Inline `Template.Splice(node)` | `SpliceCallFinder.cs` |
| 19 | Emit staged regions (unroll → `BuildList`) | `StagedRegionEmitter.Emit` |
| 20 | `[Splice]` member generators → local functions | `SpliceMemberGeneratorFinder.cs` |
| 21 | Branch pruning | `BranchPruneIdentifier.cs` |
| 22–23 | Construct `TemplateSyntaxQuoter`, quote the source | `TemplateSyntaxQuoter.cs`, `TemplateSyntaxQuoterInvoker.cs:20` |
| 24–28 | Assemble factory method + file-local helpers + compilation unit; `AddSource` | `TemplateFactorySourceGenerator.cs:134`–`203` |

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
  stage tracking-named (`TrackingNames.cs:8`), equatable results only.
- Impossible cut (live ⟵ quoted) is a hard error (SY1013), not a silent miscompile.

# Related

- [Incremental pipeline](/architecture/incremental-pipeline.md) · [Quoting](/subsystems/quoting.md)
- [Decorations](/subsystems/decorations.md) · [ObjectReader example](/examples/objectreader.md)

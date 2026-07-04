---
type: Subsystem
title: Templating
description: "[Template<T>]/[Template] → generated factory: the staged binding-time pipeline that splits a method body into quoted output vs live, factory-time computation, plus parent-driven sub-templates."
resource: src/Synto.SourceGenerator/Templating
project: projects/synto-sourcegenerator
entrypoints: ["src/Synto.SourceGenerator/Templating/Pipeline/TemplateFactorySourceGenerator.cs:17"]
tags: [templating, staging, binding-time, pipeline, sub-templates]
timestamp: 2026-07-04T00:00:00Z
---

# Responsibility

The largest and gnarliest subsystem. A `[Template<Target>]` method/class
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

- **Shell** — `Pipeline/TemplateFactorySourceGenerator.cs:17` (`[Generator]`), `Initialize`
  (`:20`). **Two** FAWMN sources: the parameterless `[Template]` → `GenerateBareTemplate` (`:30`,
  `:55`), tracking-named `Transform`/`Result`; the generic `[Template<T>]` → `GenerateTemplate`
  (`:44`, `:71`), tracking-named `GenericTransform`/`GenericResult` (distinct so all-steps
  cacheability guards stay unambiguous) — `Diagnostics/TrackingNames.cs:10`–`16`.
- **Document assembler** — `TemplateDocumentBuilder.Build` (`Pipeline/TemplateDocumentBuilder.cs:22`):
  invokes the factory builder, then wraps the factory in target ancestry, scan-injects
  file-local helpers (`FindReferencedHelpers` vs `FileLocalHelpers` registry), merges
  usings, prepends `#nullable enable`, formats, names the `.g.cs`.
- **Factory builder** — `TemplateFactoryBuilder.Build` (`Pipeline/TemplateFactoryBuilder.cs:24`):
  ordered per-feature steps over a `TemplateBuildContext` accumulator (whose field order
  is the load-bearing step contract).

# Pipeline (ordered, the spine)

The ordered steps run inside `TemplateFactoryBuilder.Build` (`Pipeline/TemplateFactoryBuilder.cs:24`):

| # | Step | Where |
|---|------|-------|
| 0 | `[Generator]` over both `[Template<T>]` / `[Template]`; two transforms | `Pipeline/TemplateFactorySourceGenerator.cs:17`, `:20` |
| 1 | Discover target/source/options | `Pipeline/TemplateInfo.cs:26` |
| 2 | Validate (target class/partial, declared in source; SY1001–4) | `Pipeline/TemplateValidator.cs:18` |
| 3 | `TemplateScope` + trim foreign children; recurse bare sub-templates | `Pipeline/TemplateScopedWalker.cs:18`, `Pipeline/TemplateFactoryBuilder.cs:164` |
| 4–5 | Lift staged type params / `[Splice]` / `[Syntax]` params | `Discovery/StagedTypeParameterFinder.cs`, `Discovery/SpliceParameterFinder.cs`, `Discovery/SyntaxParameterFinder.cs` |
| 6 | Staged roots + **classify binding time** | `Discovery/StagedParameterFinder.cs`, `Discovery/StagedRootFinder.cs`, `Discovery/QuoteCallFinder.cs`, `Analysis/BindingTimeClassifier.cs:102` |
| 7 | Impossible-cut check (live ⟵ quoted) → SY1013 | `Pipeline/TemplateFactoryBuilder.cs` |
| 8 | Discover staged regions (loops/ifs to unroll) + consumed nodes | `Discovery/StagedRegionFinder.cs:14` |
| 9 | Scalar member-access fold (`columns.Count` → literal) | `Pipeline/TemplateFactoryBuilder.cs` |
| 10–12 | Lift staged params/locals; init `ValueLift` (lazy `[Runtime]` cache) | `Lifting/ValueLift.cs:16`, `:68` |
| 13–15 | Lift `[Unquote]`/`[Quote]` roots + inline `Quote(value)` | `Discovery/QuoteParameterFinder.cs`, `Discovery/QuoteCallFinder.cs` |
| 16 | Syntax-builder facade calls | `Discovery/FacadeCallFinder.cs:19` (`FindBuilderCalls`), `Builders/SyntaxBuilderRegistry.cs:17`, `Builders/FacadeArgumentBinder.cs:16`, `Builders/FacadeShape.cs` |
| 17 | Inline `Template.Splice(node)` | `Discovery/SpliceCallFinder.cs` |
| 18 | Emit staged regions (unroll → `BuildList`) | `Staging/StagedRegionEmitter.cs:20` → `Analysis/StagedLivenessAnalysis.cs`, `Staging/RootRenameRewriter.cs`, `Staging/StagedScaffoldBuilder.cs:19`, `Staging/StagedHelperCallFactory.cs` |
| 19 | `[Splice]` member generators → local functions | `Staging/SpliceMemberGeneratorEmitter.cs:24` |
| 20 | Branch pruning | `Analysis/BranchPruneIdentifier.cs` |
| 21 | Build the per-node quoter, quote the source | `Quoting/TemplateQuoterFactory.cs:17`, `Quoting/TemplateSyntaxQuoterInvoker.cs:20`, `Quoting/PerNodePipelineQuoter.cs:18` (per-node/-owner step dispatch) |
| 22 | Assemble factory + helpers + unit; `AddSource` | `Pipeline/TemplateDocumentBuilder.cs:22` |

Staged-string interpolation bare holes are fused into literal text during quoting by
`InterpolationFold` (`Quoting/InterpolationFold.cs:41`), which the owner-keyed
`InterpolationFoldStep` (`Quoting/ListPipeline/InterpolationFoldStep.cs:16`) delegates to via the
quoter's `QuoteContext` re-entry callback.

# Surface: two `[Template]` forms + declaration-rules matrix

The `[Template(typeof(T))]` `Type`-ctor form is **gone** (pre-GA clean break). Two marker
attributes replace it (`Definition/TemplateAttribute.cs`): generic `TemplateAttribute<T>`
(`:23`) pins the factory target via its type argument; parameterless `TemplateAttribute`
(`:11`) is targetless and only valid **nested** as a sub-template. The declaration-rules
matrix (spec §4), enforced across the two transforms:

| Placement | `[Template<T>]` | bare `[Template]` |
|-----------|-----------------|-------------------|
| top-level | ✅ public factory on `T` | ❌ **SY1033** (needs a target) |
| nested in carrier | ❌ **SY1034** (must not pin) | ✅ parent-driven sub-template |

- **SY1033** / **SY1034** — `Diagnostics/TemplateDiagnostics.cs:375`, `:389`. SY1033 is raised
  by the bare transform (`GenerateBareTemplate`, `:62`); SY1034 by the generic transform
  (`GenerateTemplate`, `:78`), which then emits no factory (no sibling).
- The nested/top-level distinction is one shared ancestor check —
  `NestedTemplateCheck.IsNestedInTemplateCarrier` (`Discovery/NestedTemplateCheck.cs:18`),
  matching either attribute symbol on an ancestor `TypeDeclarationSyntax` (generic matched on
  its original definition). Result is a bool consumed entirely inside the transform.
- Consumer **LangVersion floor is C# 11** (generic attributes). Acceptable pre-GA.

# Scope: child templates → parent-driven sub-templates

`TemplateScope` / `TemplateScopedWalker` (`Pipeline/TemplateScopedWalker.cs:18`) implement
the **child-template ownership boundary**: a nested method-level `[Template]`
inside a carrier class is **excluded** from the parent's live-staging walk (every
per-template finder derives from `TemplateScopedWalker`, which skips foreign child subtrees
in `VisitMethodDeclaration`).

The old independent-sibling generation model for nested templates is **dead**. A nested bare
`[Template]` is now built **by its parent** into the parent's single `{Target}.{Source}.g.cs`
document as an **`internal static`** method — not a private one and not a separate `.g.cs`.
`ResolveScopeAndTrimForeignChildren` (`Pipeline/TemplateFactoryBuilder.cs:164`) trims each
foreign child from the parent's quoted output, then for a bare child recursively calls
`TemplateFactoryBuilder.Build` with `SyntaxKind.InternalKeyword` (`:184`) and folds the
result into the parent's method list. `internal` (not `private`) so a carrier's own
`[Splice]` source — and sibling carriers targeting the same class — can name the sub-template
across the class boundary (same-assembly); `private` broke ObjectReader's self-compiling
dog-food (CS0122). Recursion is **one level**: a child method carrier resolves to an empty
scope. `TemplateInfo.CreateSubTemplate` (`Pipeline/TemplateInfo.cs:66`) adopts **only** the
bare form (a nested generic returns `null` there and is SY1034'd by its own transform).

# Consumer surface (in `src/Synto/Templating/`)

- `[Template<T>]` / `[Template]` — `Definition/TemplateAttribute.cs:23` / `:11` (Method | Class | Struct;
  `Options` for Bare/Single). Generic pins the target; parameterless is a nested sub-template only. See
  the declaration-rules matrix above.
- `[Unquote]` — `Staging/UnquoteAttribute.cs` (param/type; lifts, **may drive** control).
- `[Quote]` — `Definition/QuoteAttribute.cs` (param-only; lifts, **never** drives control).
- `[Splice]` — `Staging/SpliceAttribute.cs` (param/type/method; verbatim, no lift).
- `[SyntaxBuilder]` + `[Quoted]` + `[ReturnType]` — facade builders (`Builders/`).
- `[Runtime]` — `Staging/RuntimeAttribute.cs` (marks a class exposing `ToSyntax(this T)` for custom-type lifts).
- Inline facades on `Template` (`Staging/Template.Parameter.cs`): `Parameter<T>()` (`:22`),
  `Unquote<T>()` (`:34`), `Quote<T>()` (`:48`), `Splice()` (`:59`), `Member<T>()`
  (`:71`), `TypeOf()` (`:80`) — all inert (`=> default!`), recognized by binding.

> `Unquote<T>()` is a **method** carrier, not `[Unquote]`. The Live/Inline →
> Unquote/Splice rename shipped 2026-06-27 (`Live`/`Inline`/`AsSyntax` deleted; see
> spec `docs/superpowers/specs/2026-06-27-quote-unquote-splice-naming.md` §9). See
> [glossary](/glossary.md).

# Invariants

- Honor the [cacheability contract](/architecture/incremental-pipeline.md): every
  stage tracking-named (`Diagnostics/TrackingNames.cs:10`), equatable results only. `TemplateBuildContext`
  is transform-local mutable scratch — never equatable, never crosses a provider.
- `TemplateBuildContext` **field order is the step-ordering contract** — steps in
  `TemplateFactoryBuilder.Build` (`Pipeline/TemplateFactoryBuilder.cs:24`) must not be reordered;
  live-region container replacements are added LAST.
- One `ValueLift` (`Lifting/ValueLift.cs:16`) per build, with a lazy `[Runtime]` converter cache
  shared across `[Unquote]`/`[Quote]`/inline-`Quote` lifts.
- File-local helpers are injected by **scanning the emitted factory** against the
  `FileLocalHelpers` registry (`TemplateDocumentBuilder.FindReferencedHelpers`), never via flags.
- Impossible cut (live ⟵ quoted) is a hard error (SY1013), not a silent miscompile.

# Related

- [Incremental pipeline](/architecture/incremental-pipeline.md) · [Quoting](/subsystems/quoting.md)
- [Decorations](/subsystems/decorations.md) · [ObjectReader example](/examples/objectreader.md)

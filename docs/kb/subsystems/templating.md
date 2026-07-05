---
type: Subsystem
title: Templating
description: "[Template<T>]/[Template] → generated factory: the staged binding-time pipeline that splits a method body into quoted output vs live, factory-time computation, plus parent-driven sub-templates."
resource: src/Synto.Generators/Templating
project: projects/synto-sourcegenerator
entrypoints: ["src/Synto.Generators/Templating/TemplateFactorySourceGenerator.cs:21"]
tags: [templating, staging, binding-time, pipeline, sub-templates]
timestamp: 2026-07-05T00:00:00Z
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
- **Live roots** — `[Unquote]`/`Template.Unquote<T>()` (value-source local or
  parameter-source live root), `[Quote]`/`Template.Quote<T>()`. Values
  supplied/computed at factory time.
- **Staged control** — a `foreach`/`for`/`while`/`if` *driven by a live root* is
  **unrolled** at factory time. Driven only by quoted values → stays a runtime
  construct. `[Quote]` deliberately lifts a value **without** making it drive
  control (the liveness shield).
- `BindingTimeClassifier` partitions the body into `Quoted` / `Unquote` /
  `StagedControl` by seeding from live roots and propagating via def-use; an
  "impossible cut" (live depends on quoted) is error **SY1013**.

# Layout (post-reorg 2026-07-05)

Folder = namespace under `Synto.Generators.Templating.*`. The `[Generator]` shell sits
at the feature root; its carved collaborators sit in role folders (`Emit/`, `Discovery/`,
`BindingTime/`, `Lifting/`, `Staging/`, `Quoting/` + `Quoting/Passes/`, `Builders/`,
`Decorations/`, `Diagnostics/`), each ≤ ~400 LOC:

- **Shell** — `TemplateFactorySourceGenerator.cs:21` (`[Generator]`), `Initialize`
  (`:23`). **Two** FAWMN sources: the parameterless `[Template]` → `GenerateBareTemplate` (`:36`,
  `:58`), tracking-named `Transform`/`Result`; the generic `[Template<T>]` → `GenerateTemplate`
  (`:50`, `:74`), tracking-named `GenericTransform`/`GenericResult` (distinct so all-steps
  cacheability guards stay unambiguous) — `Diagnostics/TrackingNames.cs:10`–`16`.
- **Document assembler** — `TemplateDocumentBuilder.Build` (`Emit/TemplateDocumentBuilder.cs:23`):
  invokes the factory builder, then wraps the factory in target ancestry, scan-injects
  file-local helpers (`FindReferencedHelpers` `:137` vs the `Prelude/FileLocalHelpers` registry),
  merges usings, prepends `#nullable enable`, formats, names the `.g.cs`.
- **Factory builder** — `TemplateFactoryBuilder.Build` (`Emit/TemplateFactoryBuilder.cs:32`):
  ordered per-feature steps over a `TemplateBuildContext` (`Emit/TemplateBuildContext.cs:24`)
  accumulator (whose field order is the load-bearing step contract).

# Pipeline (ordered, the spine)

The ordered steps run inside `TemplateFactoryBuilder.Build` (`Emit/TemplateFactoryBuilder.cs:32`):

| # | Step | Where |
|---|------|-------|
| 0 | `[Generator]` over both `[Template<T>]` / `[Template]`; two transforms | `TemplateFactorySourceGenerator.cs:21`, `:23` |
| 1 | Discover target/source/options | `Emit/TemplateInfo.cs:26` |
| 2 | Validate (target class/partial, declared in source; SY1001–4) | `Emit/TemplateValidator.cs:16` |
| 3 | `TemplateScope` + trim foreign children; recurse bare sub-templates | `Emit/TemplateScopedWalker.cs:18`, `Emit/TemplateFactoryBuilder.cs:172` |
| 4–5 | Lift staged type params / `[Splice]` params | `Discovery/StagedTypeParameterFinder.cs`, `Discovery/SpliceParameterFinder.cs` |
| 6 | Staged roots + **classify binding time** | `Discovery/StagedParameterFinder.cs`, `Discovery/StagedRootFinder.cs`, `Discovery/QuoteCallFinder.cs`, `BindingTime/BindingTimeClassifier.cs:102` |
| 7 | Impossible-cut check (live ⟵ quoted) → SY1013 | `Emit/TemplateFactoryBuilder.cs` |
| 8 | Discover staged regions (loops/ifs to unroll) + consumed nodes | `Discovery/StagedRegionFinder.cs:13` |
| 9 | Scalar member-access fold (`columns.Count` → literal) | `Emit/TemplateFactoryBuilder.cs` |
| 10–12 | Lift staged params/locals; init `ValueLift` (lazy `[Runtime]` cache) | `Lifting/ValueLift.cs:18`, `:70` |
| 13–15 | Lift `[Unquote]`/`[Quote]` roots + inline `Quote(value)` | `Discovery/QuoteParameterFinder.cs`, `Discovery/QuoteCallFinder.cs` |
| 16 | Syntax-builder facade calls | `Builders/FacadeCallFinder.cs:22` (`FindBuilderCalls`), `Builders/SyntaxBuilderRegistry.cs:12`, `Builders/FacadeArgumentBinder.cs:15`, `Builders/FacadeShape.cs:56` |
| 17 | Inline `Template.Splice(node)` | `Discovery/SpliceCallFinder.cs` |
| 18 | Emit staged regions (unroll → `BuildList`) | `Staging/StagedRegionEmitter.cs:22` → `Staging/StagedLivenessAnalysis.cs:12`, `Staging/RootRenameRewriter.cs`, `Staging/StagedScaffoldBuilder.cs:20`, `Staging/StagedHelperCallFactory.cs` |
| 19 | `[Splice]` member generators → local functions | `Staging/SpliceMemberGeneratorEmitter.cs:15` |
| 20 | Branch pruning | `BindingTime/BranchPruneIdentifier.cs` |
| 21 | Build the per-node quoter, quote the source | `Quoting/TemplateQuoterFactory.cs:16`, `Quoting/TemplateSyntaxQuoterInvoker.cs:11`, `Quoting/PerNodePipelineQuoter.cs:19` (per-node/-owner step dispatch) |
| 22 | Assemble factory + helpers + unit; `AddSource` | `Emit/TemplateDocumentBuilder.cs:23` |

Staged-string interpolation bare holes are fused into literal text during quoting by
`InterpolationFold` (`Quoting/InterpolationFold.cs:41`), which the owner-keyed
`InterpolationFoldStep` (`Quoting/Passes/InterpolationFoldStep.cs:17`) delegates to via the
quoter's `QuoteContext` re-entry callback. The other core passes live beside it under
`Quoting/Passes/` (`QuoteStep`, `MemberSegmentStep`, `ReplaceStep`, `TrimStep`); feature
passes live with their feature (`Decorations/DecorateStep.cs`, the builder steps under `Builders/`).

# Surface: two `[Template]` forms + declaration-rules matrix

The `[Template(typeof(T))]` `Type`-ctor form is **gone** (pre-GA clean break). Two marker
attributes replace it (`src/Synto.Core/Templating/Definition/Surface/TemplateAttribute.cs`): generic
`TemplateAttribute<T>` (`:23`) pins the factory target via its type argument; parameterless
`TemplateAttribute` (`:11`) is targetless and only valid **nested** as a sub-template. The
declaration-rules matrix (spec §4), enforced across the two transforms:

| Placement | `[Template<T>]` | bare `[Template]` |
|-----------|-----------------|-------------------|
| top-level | ✅ public factory on `T` | ❌ **SY1033** (needs a target) |
| nested in carrier | ❌ **SY1034** (must not pin) | ✅ parent-driven sub-template |

- **SY1033** / **SY1034** — `Diagnostics/TemplateDiagnostics.cs:384`, `:398`. SY1033 is raised
  by the bare transform (`GenerateBareTemplate`, `:69`); SY1034 by the generic transform
  (`GenerateTemplate`, `:84`), which then emits no factory (no sibling).
- The nested/top-level distinction is one shared ancestor check —
  `NestedTemplateCheck.IsNestedInTemplateCarrier` (`Discovery/NestedTemplateCheck.cs:18`),
  matching either attribute symbol on an ancestor `TypeDeclarationSyntax` (generic matched on
  its original definition). Result is a bool consumed entirely inside the transform.
- Consumer **LangVersion floor is C# 11** (generic attributes). Acceptable pre-GA.

# Scope: child templates → parent-driven sub-templates

`TemplateScope` (`Emit/TemplateScope.cs:24`) / `TemplateScopedWalker` (`Emit/TemplateScopedWalker.cs:18`)
implement the **child-template ownership boundary**: a nested method-level `[Template]`
inside a carrier class is **excluded** from the parent's live-staging walk (every
per-template finder derives from `TemplateScopedWalker`, which skips foreign child subtrees
in `VisitMethodDeclaration` `:27`).

The old independent-sibling generation model for nested templates is **dead**. A nested bare
`[Template]` is now built **by its parent** into the parent's single `{Target}.{Source}.g.cs`
document as an **`internal static`** method — not a private one and not a separate `.g.cs`.
`ResolveScopeAndTrimForeignChildren` (`Emit/TemplateFactoryBuilder.cs:172`) trims each
foreign child from the parent's quoted output, then for a bare child recursively calls
`TemplateFactoryBuilder.Build` with `SyntaxKind.InternalKeyword` (`:192`) and folds the
result into the parent's method list. `internal` (not `private`) so a carrier's own
`[Splice]` source — and sibling carriers targeting the same class — can name the sub-template
across the class boundary (same-assembly); `private` broke ObjectReader's self-compiling
dog-food (CS0122). Recursion is **one level**: a child method carrier resolves to an empty
scope. `TemplateInfo.CreateSubTemplate` (`Emit/TemplateInfo.cs:66`) adopts **only** the
bare form (a nested generic returns `null` there and is SY1034'd by its own transform).

# Consumer surface (in `src/Synto.Core/Templating/`)

- `[Template<T>]` / `[Template]` — `Definition/Surface/TemplateAttribute.cs:23` / `:11` (Method | Class | Struct;
  `Options` for Bare/Single). Generic pins the target; parameterless is a nested sub-template only. See
  the declaration-rules matrix above.
- `[Unquote]` — `Staging/Surface/UnquoteAttribute.cs` (param/type; lifts, **may drive** control).
- `[Quote]` — `Definition/Surface/QuoteAttribute.cs` (param-only; lifts, **never** drives control).
- `[Splice]` — `Staging/Surface/SpliceAttribute.cs` (param/type/method; verbatim, no lift).
- `[SyntaxBuilder]` + `[Quoted]` + `[ReturnType]` — facade builders (`Builders/Surface/`).
- `[Runtime]` — `Staging/Surface/RuntimeAttribute.cs` (marks a class exposing `ToSyntax(this T)` for custom-type lifts).
- Inline facades on `Template` (`Staging/Surface/Template.cs`, renamed from `Template.Parameter.cs`):
  `Unquote<T>(value)` (`:22`), `Unquote<T>(parameterName?)` (`:42`), `Quote<T>()` (`:56`), `Splice()` (`:67`),
  `Member<T>()` (`:79`), `TypeOf()` (`:88`) — all inert (`=> default!`), recognized by binding.

> `Unquote<T>()` is a **method** carrier, not `[Unquote]`. The Live/Inline →
> Unquote/Splice rename shipped 2026-06-27 (`Live`/`Inline`/`AsSyntax` deleted; see
> spec `docs/superpowers/specs/2026-06-27-quote-unquote-splice-naming.md` §9). See
> [glossary](/glossary.md).

# Invariants

- Honor the [cacheability contract](/architecture/incremental-pipeline.md): every
  stage tracking-named (`Diagnostics/TrackingNames.cs:10`), equatable results only. `TemplateBuildContext`
  is transform-local mutable scratch — never equatable, never crosses a provider.
- `TemplateBuildContext` **field order is the step-ordering contract** — steps in
  `TemplateFactoryBuilder.Build` (`Emit/TemplateFactoryBuilder.cs:32`) must not be reordered;
  live-region container replacements are added LAST.
- One `ValueLift` (`Lifting/ValueLift.cs:18`) per build, with a lazy `[Runtime]` converter cache
  shared across `[Unquote]`/`[Quote]`/inline-`Quote` lifts.
- File-local helpers are injected by **scanning the emitted factory** against the
  `Prelude/FileLocalHelpers` registry (`TemplateDocumentBuilder.FindReferencedHelpers`), never via flags.
- Impossible cut (live ⟵ quoted) is a hard error (SY1013), not a silent miscompile.

# Related

- [Incremental pipeline](/architecture/incremental-pipeline.md) · [Quoting](/subsystems/quoting.md)
- [Decorations](/subsystems/decorations.md) · [ObjectReader example](/examples/objectreader.md)

# Synto — codebase quality evaluation (issues triage)

**Date:** 2026-07-03 · **Evaluator:** Claude (Fable 5), full static read of `src/` + `test/`
**Scope:** production code weighted toward `src/Synto` and `src/Synto.SourceGenerator` (Templating core), plus Matching, Synto.Diagnostics, Bootstrap, and test coverage.
**Standards:** `.claude/playbook/standards.md` severities, calibrated per `project-phase.md` (pre-release). Findings tagged `pre-release` are down-rated per that calibration.
**This file is local only — not synced to GitHub, no issues created anywhere.**

**Headline:** no critical findings. The load-bearing discipline — incremental cacheability — is **uniformly clean across all five generators**: every pipeline flows only equatable value types (`*GenerationResult` records + `EquatableArray<DiagnosticInfo>` + `LocationInfo`), all semantic work stays transform-local, and top-level catches convert transform failures to SY0000-family diagnostics. The 6 high findings concentrate in three clusters: (1) the just-landed builder-facade recognition, (2) exception paths that escape the transform try/catch (hint-name construction + one pre-try call), and (3) emit/compose bugs that put non-compiling code in consumer output.

## Summary table

| # | Sev | Dimension | Area | Title |
|---|-----|-----------|------|-------|
| 1 | high | correctness | `src/Synto.SourceGenerator/Templating/Discovery/FacadeCallFinder.cs:52-70` | Name-only facade recognition hijacks bound user method calls |
| 2 | high | correctness | `src/Synto.SourceGenerator/Templating/Analysis/BindingTimeClassifier.cs:239-247` | Spurious SY1013 for symbols declared inside the binding expression |
| 3 | high | correctness | `src/Synto.SourceGenerator/Templating/Pipeline/TemplateFactorySourceGenerator.cs:39` | `TemplateInfo.Create` runs outside the SY0000 net; null-unbox on malformed `Options` escapes |
| 4 | high | correctness | `Pipeline/TemplateDocumentBuilder.cs:103` (+3 generators) | Hint-name collisions / invalid characters crash `Emit` — no catch there |
| 5 | high | correctness | `src/Synto.Diagnostics/DiagnosticsGenerator.cs:315-318` | Generated partial re-emits containing class's attributes / base list / primary ctor |
| 6 | high | correctness | `src/Synto/Decorations/SealedAttributeExtensions.cs:10-15` | `ApplySealedAttribute` emits illegal `partial sealed` modifier order |
| 7 | medium | correctness | `Templating/Builders/FacadeArgumentBinder.cs:67-73` | Facade binding is strictly positional; named arguments silently mispair |
| 8 | medium | correctness | `Templating/Staging/StagedScaffoldBuilder.cs:211-226` | Element/member mutation of a live target misclassified; assignment lifted as its value |
| 9 | medium | correctness | `Templating/Staging/SpliceMemberGeneratorEmitter.cs:95-117` | Unrewritten `Parameter<T>()` shapes flow verbatim into the factory |
| 10 | medium | correctness | `Templating/Builders/FacadeShape.cs:129-189` | Builder-declaration misuse surfaces as cryptic errors in generated files, not SY diagnostics |
| 11 | medium | correctness | `Pipeline/TemplateFactoryBuilder.cs:779` | `FacadeCallFinder` is the only unscoped finder — child-template subtrees leak into the parent pass |
| 12 | medium | correctness | `Pipeline/TemplateFactoryBuilder.cs:387-398` | `TransplantCarrierUsings` both over- and under-transplants |
| 13 | medium | correctness | `Templating/Quoting/TemplateSyntaxQuoterInvoker.cs:47-58` | Bodyless `Bare` template: `TryQuote` returns false with `error == null` → silent no-output |
| 14 | medium | correctness/consequences | `src/Synto/Quoting/CSharpSyntaxQuoter.cs:183-187` | `TemplateOption.PreserveTrivia` is a silent no-op |
| 15 | medium (pre-release) | correctness | `src/Synto/Decorations/ClassDeclarationSyntaxExtensions.cs:19-27` | Generic / non-class targets & ancestors: arity-0 rebuild lands output on the wrong type, or SY0000 |
| 16 | medium | correctness | `Matching/Emit/MatchNodeWalker.cs:155-167` | Reused `[Capture] Stmt` parameter emits duplicate local (CS0128), no diagnostic |
| 17 | medium (pre-release) | correctness | `Matching/Emit/MatchRunAligner.cs:118-124,190` | Variable-run alignment silently pins the run start/tail beyond what anchors demand |
| 18 | medium | correctness | `src/Synto/Formatting/SyntaxFormatter.cs:60-82` | `VisitListElement` peeks a stack pushed only for separated lists → `InvalidOperationException` |
| 19 | medium (pre-release) | correctness | `src/Synto/Syntax/LiteralSyntaxExtensions.cs:66-74` | `ToSyntax(double/float)` emits bare `NaN`/`Infinity` identifiers |
| 20 | medium | consequences | `src/Synto/Synto.csproj:58-61` | Synto.Core's actual public surface (~30 types) contradicts the stated 5-markers+3-helpers contract |
| 21 | medium | correctness/testability | `test/Synto.Diagnostics.Test/snapshots/*` + `src/Synto.Diagnostics/DiagnosticsGenerator.cs` | Injected-attribute text is checkout-dependent (CRLF) — snapshots flapping right now |
| 22 | medium | testability | `Templating/Builders/SyntaxBuilderFacadeGenerator.cs:23-33` | Only shipped generator with no tracking names / cacheability guard |
| 23 | medium | testability | `test/Synto.Test/Templating/DecorationTests.cs`, `SyntaxBuilderTest.cs:64-86` | Decorations and user `[SyntaxBuilder]` builders are never executed end-to-end |
| 24 | medium | maintainability | `Templating/Quoting/PerNodePipelineQuoter.cs:61-67` | List-seam dispatch keyed on owner node; correctness rests on per-step self-enforced guards |
| 25 | low (pre-release) | correctness | `Templating/Quoting/NodePipeline/BuilderStep.cs:47` | Builder-staged verbatim spine is not root-renamed |
| 26 | low | correctness | `Discovery/FacadeCallFinder.cs:67` | `ReturnsStatement` taken from `candidates[0]`, not the compiler-resolved overload |
| 27 | low | correctness | `Discovery/FacadeCallFinder.cs:106-113` | `ShareBindingShape` reports SY1018 "ambiguous" for what is really a SY1017-class shape error |
| 28 | low | testability | `test/Synto.Test/Templating/SwitchBuilderTest.cs` | `WhenArm`/`DefaultArm` arms only post-gen-compiled, never executed |
| 29 | low (pre-release) | correctness | `src/Synto/Quoting/CSharpSyntaxQuoter.cs:138-199,227-240` | Base-quoter fallbacks: unguarded casts / `NotImplementedException` / `ValueText` on `@`-identifiers |
| 30 | low (pre-release) | correctness | `Templating/Analysis/BranchPruneIdentifier.cs:45-49` | Only `ParameterList` is protected from pruning; other required-but-emptiable containers unguarded |
| 31 | low (pre-release) | correctness | grouped (6 anchors) | Edge inputs degrade to opaque SY0000/SDG0000 instead of targeted diagnostics |
| 32 | low | maintainability | `Discovery/SpliceParameterFinder.cs:33-37` (+3 finders) | Factory-signature determinism rests on `Dictionary` insertion-order behavior |
| 33 | low (pre-release) | performance | grouped (3 anchors) | In-transform hot-path churn: O(n²) classifier, loop-invariant quoter rebuilds, per-call registry walk |
| 34 | low | maintainability | grouped (3 anchors) | Latent traps: `InterpolationFoldStep` ctx caching, duplicated `RunSegment` assembly, `GetTypeName` collapse |
| 35 | low | consequences | `src/Synto/Templating/Builders/SyntoBuilders.cs:36-51` | Injected `SyntoBuilders` uses `params IEnumerable<T>` → silently raises consumer LangVersion floor to C# 13 |
| 36 | low | testability | grouped (5 anchors) | Test-infra gaps: SY1003/SY1006 driver facts, run-collect executed only in examples, harness copy-paste, orphan guard scope, dead test dir |

---

## High

### 1. Name-only facade recognition hijacks bound user method calls
**Severity:** high · **Dimension:** correctness
**Location:** `src/Synto.SourceGenerator/Templating/Discovery/FacadeCallFinder.cs:52-70` (with `Quoting/NodePipeline/BuilderStep.cs:52-57`)
User `[SyntaxBuilder]` facade calls are recognized purely by simple name (`GetInvokedSimpleName`, which also matches receiver-qualified calls), with no check that the invocation *failed to bind*. The injected `SyntoBuilders` puts `Switch`, `Case`, `Default`, `Arm`, `Return` into every consumer assembly, so quoted template code calling a real user method with one of those common names (`settings.Default()`, `validator.Return(x)`) is silently rewritten into a `global::…SyntoBuilders.X(...)` call — and `BuilderStep` drops the receiver entirely. Silent wrong generated code under realistic naming.
**Direction:** only treat an invocation as a facade call when `GetSymbolInfo` yields no/error symbol (or binds to the builder itself for the `FacadeMatchesBuilder` case). A successfully bound, unrelated method must never be hijacked.

### 2. Spurious SY1013 for symbols declared inside the binding expression
**Severity:** high · **Dimension:** correctness
**Location:** `src/Synto.SourceGenerator/Templating/Analysis/BindingTimeClassifier.cs:239-247` (bail at `Pipeline/TemplateFactoryBuilder.cs:337-344`)
`FindImpossibleCuts` flags any `ILocalSymbol`/`IParameterSymbol` not in `_live` — but `_live` only contains roots plus locals reached through declarator initializers. A lambda parameter, `out var`, or pattern variable declared *inside* the binding expression itself (`Unquote(liveItems.Select(x => x.Length).Sum())`) is never in `_live`, so a perfectly factory-time-evaluable binding is reported as SY1013 and generation hard-bails with no factory. LINQ over a live collection is exactly the shape the feature invites (per the ObjectReader dog-food).
**Direction:** exclude identifiers whose declaring symbol is itself declared within the binding expression (declaring-syntax span containment), or pre-collect locally-declared symbols and skip them.

### 3. `TemplateInfo.Create` runs outside the SY0000 net; null-unbox on malformed `Options` escapes the generator
**Severity:** high · **Dimension:** correctness
**Location:** `src/Synto.SourceGenerator/Templating/Pipeline/TemplateFactorySourceGenerator.cs:39` (try begins at :52) + `Pipeline/TemplateInfo.cs:56-62`
`TemplateInfo.Create(syntaxContext)` is called before the `try` that converts exceptions to `Diagnostics.InternalError`. Inside it, `options = (TemplateOption)namedArgs.Value.Value!` unboxes a `TypedConstant.Value` that is null whenever the user is mid-edit (`[Template(typeof(X), Options = <error>)]` → `Kind == Error`). The NRE escapes the transform: Roslyn reports opaque CS8785 and the generator's entire output is dropped — during ordinary typing. Same pattern at `TemplateInfo.cs:35` (`ApplicationSyntaxReference!`).
**Direction:** move the `Create` call inside the `try`; guard the named-arg read (skip on `Kind == Error` / pattern-match the boxed value). Mid-keystroke states are the most realistic input a generator faces.

### 4. Hint-name construction can collide or contain invalid characters; `AddSource` throws in `Emit`, which has no catch — one bad input kills the whole generator
**Severity:** high · **Dimension:** correctness (systemic across all four shipped generators)
**Locations:**
- Templating: `Pipeline/TemplateDocumentBuilder.cs:103` — hint = `{TargetFullName}.{method name}.g.cs`; two same-named `[Template]` overloads (legal C#) or same-named templates in different carriers targeting one type collide.
- Diagnostics: `src/Synto/Emit/GeneratedSourceFilenameExtensions.cs:10-19` (`GenericsOptions.None` + `ParameterOptions.None`) + `DiagnosticsGenerator.cs:336` — two `[Diagnostic]` partial-method overloads map to one hint.
- Matching: `Matching/Emit/MatchComposer.cs:83` + `Matching/Pipeline/MatchInfo.cs:16-19` — `TargetNameFormat` includes type parameters, so a generic target puts `<`/`>` (invalid hint chars) in the name; same-named `[Match]` methods collide.
- Builders: `Templating/Builders/SyntaxBuilderFacadeGenerator.cs:82-95` — blacklist sanitization misses `?` (e.g. `int?` parameter) and `*`.
In every case the `ArgumentException` is thrown inside `RegisterSourceOutput`'s emit action (e.g. `TemplateFactorySourceGenerator.cs:74-81`), *outside* the transform try/catch → CS8785, all output dropped, no SY diagnostic.
**Direction:** (a) make hint names unique + sanitized by construction — a shared helper that whitelists Roslyn's allowed characters and appends a signature/arity disambiguator; (b) defensively wrap each `Emit`'s `AddSource` so a collision degrades to a reported diagnostic instead of disabling the generator.

### 5. Diagnostics generated partial re-emits the containing class's attribute lists, base list, and primary-constructor parameters
**Severity:** high · **Dimension:** correctness
**Location:** `src/Synto.Diagnostics/DiagnosticsGenerator.cs:315-318`
`var classDecl = parent.WithMembers(List(members))` clones the *user's* `ClassDeclarationSyntax`, keeping its `AttributeLists`, `BaseList`, and (C# 12) `ParameterList`. Any attribute on the containing class (`[ExcludeFromCodeCoverage]`, `[GeneratedCode]`, …) is duplicated across the two partial declarations → CS0579 for `AllowMultiple=false` attributes; a primary constructor is duplicated → compile error. Breaks the consumer's build under entirely ordinary conditions for this separately-shipped product.
**Direction:** rebuild a clean partial shell as `MatchComposer` does — `ClassDeclaration(parent.Identifier).WithModifiers(parent.Modifiers).WithTypeParameterList(parent.TypeParameterList)`; keep type parameters/constraints, drop attributes/base list/parameter list.

### 6. `ApplySealedAttribute` emits illegal `partial sealed` modifier order
**Severity:** high · **Dimension:** correctness
**Location:** `src/Synto/Decorations/SealedAttributeExtensions.cs:10-15` (doubly shipped: public Synto.Core API **and** file-local helper injected into every consumer factory via `FileLocalHelpers.cs:68`)
`sealed` is appended after all existing modifiers, so decorating a quoted `public partial class C` yields `public partial sealed class C` → CS0267 (`partial` must immediately precede the type keyword) in the consumer's generated file. Quoting partial classes is the mainstream case for Synto's audience (generator authors emitting partial types), and `[Sealed]` is a shipped decoration. Undetected because decoration output is never executed/rendered in tests (see #23).
**Direction:** insert `sealed` before any `partial` token (mirror `ApplyVisibilityAttribute`'s prepend approach); add a rendered-output test with `partial` in the modifier list.

---

## Medium

### 7. Facade argument binding is strictly positional; named arguments silently mispair
**Severity:** medium · **Dimension:** correctness
**Location:** `src/Synto.SourceGenerator/Templating/Builders/FacadeArgumentBinder.cs:67-73` (same hole in `FacadeCallFinder.TryBuiltIn` :78-94)
`ArgumentSyntax.NameColon` is never consulted. A facade call with reordered named arguments — legal C# against the synthesized facade — pairs each expression with the wrong builder parameter, swapping quoted/staged treatment with no diagnostic. Wrong generated code, silent.
**Direction:** honor `NameColon` when pairing, or detect any named argument and emit an SY diagnostic ("named arguments not supported on builder facades").

### 8. Element/member mutation of a live target is misclassified; the assignment is lifted as its *value*
**Severity:** medium · **Dimension:** correctness
**Location:** `src/Synto.SourceGenerator/Templating/Staging/StagedScaffoldBuilder.cs:211-226, 248-274`
`IsStagedMutation` only recognizes a mutation whose target is a bare identifier resolving to a live symbol. For `counts[i] = v;` or `acc.Total = v;` (all roots live) the target resolves to an indexer/property symbol not in the staged set → the statement is treated as a quoted island, and `CollectLiftPoints` lifts the whole assignment expression as `.ToSyntax()` — the generated output receives the assignment's *value* (e.g. the statement `5;`) while the mutation runs as a factory-time side effect. Silent nonsense output for a plausible accumulator pattern.
**Direction:** in `IsStagedMutation`, walk element/member access down to the root expression and treat the mutation as staged when the root is live; additionally make `IsPurelyStaged` refuse assignment expressions (lifting an assignment as a value is never meaningful).

### 9. Unrewritten `Parameter<T>()` shapes in a `[Splice]` generator body flow verbatim into the factory
**Severity:** medium · **Dimension:** correctness
**Location:** `src/Synto.SourceGenerator/Templating/Staging/SpliceMemberGeneratorEmitter.cs:95-117`
Only two exact shapes are rewritten (single-declarator `var x = Parameter<T>();`, and inline `Parameter<T>("constName")`). Any other use — inline zero-arg call, compound initializer, non-constant name — survives verbatim into the emitted factory, where `Parameter` fails to resolve (CS0103) or resolves to the inert marker. Broken consumer output with no SY diagnostic.
**Direction:** after rewriting, scan the body for any remaining bound `Template.Parameter` invocation and report an SY diagnostic (mirroring the staged-region bail-out pattern).

### 10. Builder-declaration misuse surfaces as cryptic compile errors in generated files instead of SY diagnostics
**Severity:** medium · **Dimension:** correctness
**Locations:** `Templating/Builders/FacadeShape.cs:129-156, 187-189`; `SyntaxBuilderRegistry.cs:27-30`; `SyntaxBuilderFacadeGenerator.cs:67, 169-173`
Gaps: `[TypeArg]` + `[Quoted]` on one parameter — documented as forbidden — is silently half-ignored; a `[Quoted] params` parameter drops `IsParams` (call shape changes silently); duplicate generated type-parameter names / `TResult` collisions produce CS0692; a `private static` `[SyntaxBuilder]` passes registry filtering (doc says "public") and fails CS0122; non-partial or interface containers produce CS0260/CS0261. All user errors, all currently surfacing inside generated files.
**Direction:** extend `FacadeShape.Derive`'s `annotationError` to cover combined markers, params propagation, and name collisions; check accessibility and container shape in the registry/facade generator and report located SY diagnostics.

### 11. `FacadeCallFinder` is the only unscoped finder — foreign child-template subtrees leak into the parent's builder pass
**Severity:** medium · **Dimension:** correctness/maintainability
**Location:** `src/Synto.SourceGenerator/Templating/Pipeline/TemplateFactoryBuilder.cs:779` + `Discovery/FacadeCallFinder.cs:41`
Every other per-template finder rebases on `TemplateScopedWalker` so a nested child `[Template]` subtree is skipped; this one LINQ-scans the whole carrier. A builder-arg error inside a *child* template fails the parent build too (double-reported), `TransplantCarrierUsings` fires for a parent with no builder calls of its own, and child spine nodes pollute `BuilderStagedConsumedNodes`.
**Direction:** thread `ctx.Scope` into `FindBuilderCalls` and skip foreign subtrees (or convert to a `TemplateScopedWalker`).

### 12. `TransplantCarrierUsings` both over- and under-transplants
**Severity:** medium · **Dimension:** correctness (folds seed finding: unused-usings noise)
**Location:** `src/Synto.SourceGenerator/Templating/Pipeline/TemplateFactoryBuilder.cs:387-398`
Two defects in one function: (a) *over*: whenever **any** builder call exists (`ctx.Plan.BuilderCalls.Count > 0`) **all** carrier usings are transplanted, so factories gain spurious unused usings (e.g. `using System.Linq;`) even when no verbatim staged code needs them; (b) *under*: the exclusion `usingName.ToString().StartsWith("Synto", …)` at :394 also eats legitimate consumer namespaces like `Syntonic.Helpers`/`SyntoX`, whose absence then breaks verbatim scaffold code with no diagnostic pointing at the cause.
**Direction:** fix the prefix test to `== "Synto" || StartsWith("Synto.")`; gate the builder-call trigger on builder calls that actually carry staged (verbatim) arguments — or better, collect the namespaces the verbatim spine actually references.

### 13. Bodyless `Bare` template: `TryQuote` returns false with `error == null` → silent no-output
**Severity:** medium · **Dimension:** correctness
**Location:** `src/Synto.SourceGenerator/Templating/Quoting/TemplateSyntaxQuoterInvoker.cs:47-58` (caller check at `TemplateFactoryBuilder.cs:930-940`)
A `Bare` template on a bodyless method (abstract/extern/partial-definition) hits neither the block-body nor expression-body branch; `Expression` and `Error` both stay null, and the caller only reports when `error is { }` — the user gets no factory and no explanation. The false-with-null-error contract is also easy for future callers to misuse.
**Direction:** emit `BareSourceCannotBeEmpty` (or a dedicated "template must have a body") in the both-null case; tighten the contract so `false` implies `error != null`.

### 14. `TemplateOption.PreserveTrivia` is a silent no-op
**Severity:** medium · **Dimension:** correctness/consequences
**Location:** `src/Synto/Quoting/CSharpSyntaxQuoter.cs:183-187` (wired from `TemplateFactoryBuilder.cs:927`)
`Visit(SyntaxTriviaList)` always returns an empty `TriviaList()` (`// TODO this isn't really supported`), yet the option changes token-emission shape (paying the wider-emission cost) and is a consumer-visible `TemplateOption` member. A shipped option that silently does nothing is output-shape/API debt — and options are a one-way door once consumers set them.
**Direction:** implement trivia quoting (whitespace/comments at minimum) or remove/diagnostic-gate the option until it works.

### 15. Generic / non-class targets & ancestors: arity-0 rebuild lands output on the wrong type, or an unguarded cast degrades to SY0000
**Severity:** medium (pre-release: exotic-ish inputs) · **Dimension:** correctness
**Locations:** `src/Synto/Decorations/ClassDeclarationSyntaxExtensions.cs:19-27` (shared `WithAncestryFrom`), `Templating/Pipeline/TemplateDocumentBuilder.cs:36-40`, `Templating/Pipeline/TemplateValidator.cs:41-51`, `Matching/Emit/MatchComposer.cs:54-56`, `DiagnosticsGenerator.cs:137`
Systemic compose-family weakness: rebuilt partials and ancestors carry no `TypeParameterList`, so a generic target/ancestor gets a *different*, arity-0 type declared beside it — compiles cleanly, factory silently lands on the wrong type. And validation walks stop silently at the first non-`ClassDeclarationSyntax` ancestor (partial `struct`/`record`), after which `WithAncestryFrom`'s hard cast throws `InvalidCastException` → opaque SY0000 instead of the precise SY1004-class diagnostic the validators exist to give.
**Direction:** one shared fix: carry `TypeParameterList` + constraints in the rebuilds, and extend each validator to reject non-class (and, until supported, generic) targets/ancestors with a located usage diagnostic.

### 16. Matching: reused `[Capture] Stmt` parameter emits a duplicate local (CS0128) with no diagnostic
**Severity:** medium · **Dimension:** correctness
**Location:** `src/Synto.SourceGenerator/Matching/Emit/MatchNodeWalker.cs:155-167` (also via `Exactly` in `MatchRunAligner.cs:265-275`)
Expression captures handle parameter reuse (unique temp + `IsEquivalentTo` guard at :127-147), but `EmitStatementCapture` never checks `BoundCaptureLocals` — a pattern using the same statement hole twice declares `cap_s` twice and duplicates the record member. Non-compiling generated source, no diagnostic.
**Direction:** mirror the expression-capture reuse branch, or raise a "capture reused" diagnostic.

### 17. Matching: variable-run alignment silently pins the run start/tail beyond what anchors demand
**Severity:** medium (pre-release: v1 no-backtracking design) · **Dimension:** correctness
**Location:** `src/Synto.SourceGenerator/Matching/Emit/MatchRunAligner.cs:118-124, 190`
`_var` always absorbs the *entire* remaining slack, so with no anchors a `{ a(); x.Some(); b(); }` pattern only matches blocks whose last statement is `b()` (under-matching "contains" semantics), and with `anchorEnd` only, fixed elements before the variable are pinned to the block start. Both silent.
**Direction:** either scan `_var` (bounded backtrack for the single split) or extend SY1204 to reject the shapes the straight-line split cannot honor.

### 18. `SyntaxFormatter.VisitListElement` peeks a stack pushed only for separated lists
**Severity:** medium · **Dimension:** correctness
**Location:** `src/Synto/Formatting/SyntaxFormatter.cs:60-82` (with `_indentOn` set at :35-45)
`CSharpSyntaxRewriter` routes non-separated `SyntaxList<T>` elements through `VisitListElement` too, but only the `SeparatedSyntaxList` overload pushes `_listCount`. A non-separated list reached under an invocation while `_indentOn` is true and before any separated push — e.g. interpolated-string contents inside the invocation expression — hits `Peek()` on an empty stack (`InvalidOperationException` → opaque SY0000 for the whole template); when the stack is non-empty it peeks the *enclosing* list's count (wrong indent decision). Interpolated strings in factory output are a shipped feature, so this is closer than it looks.
**Direction:** push in the `SyntaxList<TNode>` overload symmetrically (correct counts) — a bare `_listCount.Count > 0` guard fixes the crash but keeps the wrong-count case.

### 19. `ToSyntax(double/float)` emits bare `NaN`/`Infinity` identifiers
**Severity:** medium (pre-release for likelihood; kept medium as a public-contract + injected-helper hole) · **Dimension:** correctness
**Location:** `src/Synto/Syntax/LiteralSyntaxExtensions.cs:66-74`
`SyntaxFactory.Literal(double.NaN).Text` is `"NaN"` — an identifier, not a literal — so unquoting a non-finite value produces CS0103 in generated code. Ships twice (public Synto.Core API + injected file-local helper).
**Direction:** special-case non-finite values to `double.NaN` / `double.PositiveInfinity` / `double.NegativeInfinity` member-access expressions.

### 20. Synto.Core's actual public surface contradicts its stated contract
**Severity:** medium · **Dimension:** consequences (one-way door — settle before first publish)
**Location:** `src/Synto/Synto.csproj:58-61` (comment claims "only the 5 markers + 3 emitted helpers are public surface")
~30 public types actually ship: all 10 helper classes (`LiteralSyntaxExtensions`, `RuntimeTypeExtensions`, `QuoteSyntaxExtensions`, `CollectionSyntaxExtensions`, `InterpolationSyntaxExtensions`, the 5 `Apply*AttributeExtensions`) plus the Matching pattern markers. Since `FileLocalHelpers` injects file-scoped copies of the helpers into consumer output, the public copies are a duplicate surface that freezes at 1.0. Pre-release is exactly the cheap moment to decide.
**Direction:** audit each public type — bless it explicitly or internalize it (with `InternalsVisibleTo` for the generators, which the csproj already wires); update the csproj comment to match reality either way.

### 21. Diagnostics injected-attribute text is checkout-dependent (CRLF) — snapshots are flapping right now
**Severity:** medium · **Dimension:** correctness (output determinism) / testability
**Location:** `test/Synto.Diagnostics.Test/snapshots/DiagnosticsGeneratorTest.With*Namespace*#Synto.Diagnostics.DiagnosticsAttribute.g.verified.cs` (both currently modified in the working tree) + the raw-string attribute source in `src/Synto.Diagnostics/DiagnosticsGenerator.cs`
`test/.gitattributes` pins `*.verified.cs` to `eol=lf` precisely because snapshot byte-stability underpins the caching guarantees, but the post-init `DiagnosticsAttribute` text inherits the *source file's* checkout line endings (the generator `.cs` is not eol-pinned), so Windows re-accepts write CRLF and dirty the tree while CI writes LF. The generated output itself differs by machine — a determinism defect, not just test noise.
**Direction:** normalize (`Replace("\r\n", "\n")`) at the emission site, renormalize the two snapshots, and optionally add a no-`\r` assertion on post-init output across generators.

### 22. `SyntaxBuilderFacadeGenerator` is the only shipped generator with no cacheability guard
**Severity:** medium · **Dimension:** testability
**Location:** `src/Synto.SourceGenerator/Templating/Builders/SyntaxBuilderFacadeGenerator.cs:23-33`
No `WithTrackingName`, no `AllStepsCachedOrUnchanged` test — while Template, Match, Bootstrap, and Diagnostics all have both. Today the pipeline flows only an equatable `(string,string)?`, so it's safe by construction; that is exactly the unverified state the all-steps guard exists to pin. A future `Combine`/symbol capture regresses silently.
**Direction:** add tracking names and clone the `Generator_IsIncremental_OnUnrelatedEdit` fact from `test/Synto.Bootstrap.Test/CSharpSyntaxQuoterGeneratorTest.cs:29-55` with a `[SyntaxBuilder]` fixture.

### 23. Decorations and user `[SyntaxBuilder]` builders are never executed end-to-end
**Severity:** medium · **Dimension:** testability
**Locations:** `test/Synto.Test/Templating/DecorationTests.cs` (driver/snapshot + post-gen compile only), `SyntaxBuilderTest.cs:64-86` (same)
No test invokes a *generated* decorated factory and asserts the rendered declaration carries `public sealed partial …` — issue #6 is exactly the class of bug this gap admits (compiles, passes, breaks consumers). Likewise no executed proof that a user builder's `[Quoted]` parameter receives the argument's quote at factory run time; the only executed builder path is the built-in Switch/Case projection (`SwitchBuilderTest.cs:242-309`), whose Emit+`Assembly.Load`+Invoke tail is ready to copy.
**Direction:** one executed decoration fact (marker set including `[Sealed]` on a partial class) + extend one `VerifyBuilderTemplate` case with the existing Emit+Invoke tail.

### 24. List-seam dispatch keyed on owner node; correctness rests on per-step self-enforced guards
**Severity:** medium · **Dimension:** maintainability
**Location:** `src/Synto.SourceGenerator/Templating/Quoting/PerNodePipelineQuoter.cs:61-67`
List steps are keyed on `nodeList[0].Parent`, so *every* `SyntaxList<T>` of a keyed owner (a type's `AttributeLists` as well as its `Members`) dispatches through that owner's steps; each step must re-implement the "is this actually my list" fall-through check (documented as load-bearing in `InterpolationFoldStep.cs:27` and `MemberSegmentStep.cs:29-40`). The default for the next list step written is wrong unless its author rediscovers the invariant; an owner with two same-element-type lists is indistinguishable even with the guard; empty lists never dispatch.
**Direction:** key the map on (owner, list identity) — e.g. record the intended element type per owner in `PlanAssembler` and do the type check once, centrally, in the quoter.

---

## Low

### 25. Builder-staged verbatim spine is not root-renamed
**Severity:** low (pre-release) · **Dimension:** correctness · *(seed finding, verified)*
**Location:** `src/Synto.SourceGenerator/Templating/Quoting/NodePipeline/BuilderStep.cs:47` (staged arm; `NestedBuilderCallRewriter` :66-84)
A builder call's staged argument is emitted verbatim without applying `ctx.RootNames`, so it works only while the factory parameter name equals the source local name; a `ParamNames` dedup collision (`_`-suffix) yields an unresolved reference in the factory. **Direction:** thread root renaming through `BuilderStep`/`NestedBuilderCallRewriter` (apply `RootRenameRewriter` to the staged spine before emission).

### 26. `BuilderCall.ReturnsStatement` taken from `candidates[0]`, not the compiler-resolved overload
**Severity:** low · **Dimension:** correctness · *(seed finding, verified)*
**Location:** `src/Synto.SourceGenerator/Templating/Discovery/FacadeCallFinder.cs:67` → `Builders/FacadeArgumentBinder.cs:79` (`shape.ReturnsStatement`)
For shared-shape same-name overloads the statement-splice flag comes from whichever candidate the registry enumerated first — statement-splice correctness depends on declaration order of the two `Switch` overloads. **Direction:** OR the flag across shared-shape candidates (or include it in the `ShareBindingShape` key so divergent flags are a conflict).

### 27. `ShareBindingShape` reports SY1018 "ambiguous" for what is really a shape/annotation error
**Severity:** low · **Dimension:** correctness (diagnostic precision) · *(seed finding, verified)*
**Location:** `src/Synto.SourceGenerator/Templating/Discovery/FacadeCallFinder.cs:106-113` (report at :63)
Any candidate that fails `FacadeShape.Derive` makes the overload set "not shared" → SY1018 *ambiguous builder*, even when the real problem is one overload's return-shape/annotation error (SY1017 territory). Misleading for the user fixing the builder. **Direction:** when a candidate fails to derive, report the derive error (per-candidate) instead of folding it into ambiguity.

### 28. `WhenArm`/`DefaultArm` switch-expression arms are never executed
**Severity:** low · **Dimension:** testability · *(seed finding)*
**Location:** `test/Synto.Test/Templating/SwitchBuilderTest.cs` (the projection tests now execute `Switch`/`Case`/`Arm`, but `WhenArm`/`DefaultArm` remain post-gen-compile only)
**Direction:** extend the existing Emit+Invoke case with a guarded arm and a default arm; assert the rendered arms.

### 29. Base-quoter fallback arms: unguarded casts, `NotImplementedException`, and `ValueText` identifiers
**Severity:** low (pre-release) · **Dimension:** correctness
**Location:** `src/Synto/Quoting/CSharpSyntaxQuoter.cs:138-199` (token fallbacks: `(string)token.Value!` at :146; `NotImplementedException` arms) and :227-240 (`VisitIdentifierName` uses `ValueText`, so `@class` quotes to the bare keyword `class` — invalid output)
All contained by the transform-boundary catch (degrade to generic SY0000 rather than crash), and verbatim-keyword identifiers are exotic. The comment "there's a reason for it, and if we remove this we could probably rediscover what it is" (:228-229) is a maintainability smell worth resolving while the bootstrap-sync constraint is fresh. **Direction:** a structured "unsupported syntax" exception carrying node/token location mapped to a targeted diagnostic; use `Identifier.Text` to preserve `@`; record or rediscover the override's reason.

### 30. `BranchPruneIdentifier` protects only `ParameterList` from pruning
**Severity:** low (pre-release) · **Dimension:** correctness
**Location:** `src/Synto.SourceGenerator/Templating/Analysis/BranchPruneIdentifier.cs:45-49`
The "can be empty, but not optional" argument applies equally to `ArgumentListSyntax`, `BracketedParameterListSyntax`, `TypeArgumentListSyntax`, …; the single-case veto shows the failure mode was patched pointwise. Not clearly reachable from today's trim seeds. **Direction:** invert to an allowlist of prunable kinds (or consult parent-slot optionality).

### 31. Edge inputs degrade to opaque SY0000/SDG0000 instead of targeted diagnostics (grouped)
**Severity:** low (pre-release) · **Dimension:** correctness (diagnostic quality)
**Locations:** `Discovery/SyntaxParameterFinder.cs:46-53` (hard `!` on marker resolution; also skips `base.VisitParameter`, unlike its siblings at `SpliceParameterFinder.cs:92`); `Pipeline/TemplateInfo.cs:89-90` (`Arguments.First()` on `[Template()]`); `Pipeline/TemplateFactoryBuilder.cs:615,632` (`Parameter.Type!` NREs for markers on lambda parameters); `src/Synto.Diagnostics/DiagnosticsGenerator.cs:198-201` (`ArgumentList!` mid-typing); `Templating/Staging/StagedRegionEmitter.cs:69-73` (fixed-sibling quote silently dropped when the quoter returns null — contrast the island path's `UnsupportedStagedShape`); `Shared/SymbolMetadataExtensions.cs:72-82` (nested `[Runtime]` classes never discovered → misleading SY1008).
All caught (or silent) rather than crashing, so low individually — but the pattern is worth a sweep. **Direction:** null-tolerant guards + located SY diagnostics on each; document "top-level" in the SY1008 message or descend into nested types.

### 32. Factory-signature determinism rests on `Dictionary` insertion-order behavior in four finders
**Severity:** low · **Dimension:** maintainability (determinism)
**Locations:** `Discovery/SpliceParameterFinder.cs:33-37`, `QuoteParameterFinder.cs:38-42`, `SyntaxParameterFinder.cs:23-29`, `StagedTypeParameterFinder.cs:53-57`
Generated factory parameter order relies on .NET `Dictionary`'s undocumented insertion-order-without-removals behavior. `StagedRootFinder.cs:129-131` already keeps deliberate insertion-ordered side lists for exactly this reason. **Direction:** follow the `StagedRootFinder` pattern in the four siblings.

### 33. In-transform hot-path churn (grouped)
**Severity:** low (pre-release) · **Dimension:** performance
**Locations:** `Analysis/BindingTimeClassifier.cs:199-224` (each identifier symbol-resolved once per ancestor expression — quadratic with `GetSymbolInfo` per step); `Staging/StagedRegionEmitter.cs:68-70` (loop-invariant step maps + quoter rebuilt per fixed statement); `Builders/SyntaxBuilderRegistry.cs` (full assembly-type walk per `FindBuilderCalls` call, i.e. per template per increment)
All inside the transform (cacheability unaffected) and small at current scale. **Direction:** bottom-up single-pass classification with memoized symbol lookups; hoist the invariant quoter; cache the registry per compilation-identity within the transform call.

### 34. Latent maintainability traps (grouped)
**Severity:** low · **Dimension:** maintainability
**Locations:** `Quoting/ListPipeline/InterpolationFoldStep.cs:29-33` (`_fold ??=` caches the first `QuoteContext`; safe only under an unenforced one-quoter-per-step invariant — build per call instead); `Staging/StagedHelperCallFactory.cs:26-56` vs `SpliceMemberGeneratorEmitter.cs:62-71` (`ListSegment.Run` assembly duplicated because `RunSegment` hardcodes `StatementSyntax`; bare vs fully-qualified identifier policies also diverge — parameterize and reuse); `src/Synto/Emit/UsingDirectiveSet.cs:45-60` (`GetTypeName` collapses every qualified name to its simple name and throws on bare generic/alias-qualified names — fine for current controlled callers, a trap for the next one).

### 35. Injected `SyntoBuilders` silently raises the consumer LangVersion floor to C# 13
**Severity:** low · **Dimension:** consequences (one-way-door watch)
**Location:** `src/Synto/Templating/Builders/SyntoBuilders.cs:36, 41, 46, 51`
`params IEnumerable<T>` (params collections, C# 13) in a file injected as *source* into consumer compilations means a `<LangVersion>12</LangVersion>` consumer gets errors inside code they never wrote. Acceptable pre-1.0, but it's an injected-surface decision to make deliberately. **Direction:** decide and document the injected surface's minimum LangVersion; if lower is desired, use `IEnumerable<T>` or `params T[]` overloads.

### 36. Test-infrastructure gaps (grouped)
**Severity:** low · **Dimension:** testability
**Locations:** templating SY1003 has no driver fact (`Templating/Pipeline/TemplateValidator.cs:22` path; siblings SY1001/1002/1004 covered at `test/Synto.Test/Templating/SimpleTemplateTest.cs:575-648`) and SY1006 is pinned only inside a Verify snapshot (`…FunctionWithMultipleStatementAsSingle.verified.txt:8`); the run-collect unroll is *executed* only by the drift-allowed examples suite (`examples/…/ObjectReaderBehaviorTests.cs:23-56`); the templating driver harness is copy-pasted across ~10 test files with already-drifted `MetadataReference` sets (Matching solved this with `MatchTestHarness.cs`); `SnapshotOrphanGuardTest.cs:17` covers only Synto.Test; `test/Synto.Example.ObjectReader.Test/` is a dead untracked husk (bin/obj + empty snapshots, no csproj) — delete it.
**Direction:** two ten-line driver facts (SY1003, SY1006); one in-repo executed run-collect round-trip; extract a shared templating harness; generalize the orphan guard; remove the dead directory.

---

## Overall assessment

**Dimension scores** (per `standards.md`): Correctness **Needs improvement** (6 high, 0 critical) · Maintainability **Good enough** (2 medium) · Performance **Excellent** (cacheability verified clean end-to-end in all five pipelines; only grouped in-transform tuning lows) · Testability **Good enough** (guards exist for every load-bearing invariant; gaps are at the feature edges). Quality-gate tiers: **pass** (no correctness critical; maintainability/performance good enough; testability not broken) — with correctness carrying a real high-severity backlog.

**Strengths.** The core discipline is genuinely intact: all five generators do their semantic work transform-locally and cache only equatable text+diagnostics results — the single most common way projects like this rot, and it is clean here, *and* it is pinned by all-steps cacheability tests in four of five generators. The transform-boundary catch-to-SY0000 pattern, the `LocationInfo` diagnostic carrier, the single-source-of-truth injected surface with scan-based helper selection, and the snapshot+executed-round-trip test culture are all better than typical for a pre-1.0 generator toolkit.

**Top 3 to do first:**
1. **Close the escape hatches around the SY0000 net (#3, #4).** Move `TemplateInfo.Create` inside the try and make hint names unique+sanitized (shared helper) with a defensive `Emit`. These are the only paths where ordinary input (mid-keystroke edits, method overloads, a generic target) disables an entire generator with an opaque CS8785 — the exact failure mode the architecture was built to prevent, and cheap to fix across all four generators at once.
2. **Fix facade-call recognition to require bind-failure (#1)** — it silently rewrites consumers' own `Default()`/`Return()`/`Case()` calls today — and while in the file, fold in #7 (named args), #26, and #27.
3. **Un-block the staged-binding LINQ shape (#2)** — the spurious SY1013 hard-stops exactly the `Unquote(live.Select(...))` pattern the dog-food probe says users will write first.

Honorable mention: #5 and #6 are one-file fixes that put non-compiling code into consumer builds today; #21 is actively dirtying the working tree and is a two-line normalization.

# `[Splice]` Member-Generator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Plan style:** This plan pins **contracts, interfaces, signatures, and tests** — not prescribed implementation bodies (matching `2026-06-28-quote-value-marker.md`). Each task states exact public/internal signatures, the locked behavior, and concrete test scenarios; the implementer chooses the generator implementation that satisfies them. **Test code is the spec** — write it as given, adapting only to exact harness helper names already present in `test/Synto.Test`.
>
> **Source of truth:** `docs/superpowers/specs/2026-06-28-splice-member-generator-design.md` (committed at `fc3ab5d6` on bookmark `experimental`, APPROVED). All §6 decisions are resolved — surface is locked below.

**Goal:** Add a `[Splice]`-marked **static** method inside a `[Template]` type that runs at factory-build time and returns `MemberDeclarationSyntax` / `IEnumerable<MemberDeclarationSyntax>`, which Synto splices into the generated type as members (one per column / nested type per shape / etc.).

**Architecture:** Purely additive. Widen the existing `[Splice]` marker to also target `Method`. A new finder (modeled on `SpliceParameterFinder`) discovers `[Splice]` methods and classifies their return shape; the orchestrator validates them (three new diagnostics), emits each valid generator's body as **factory-time code** (not quoted, not unrolled) with its `Parameter<>()` calls rewritten to the factory parameters, and splices the returned members into the type's member list via the existing `CollectionSyntaxExtensions.BuildList<MemberDeclarationSyntax>` helper (already generic over `TNode : SyntaxNode`), lifted from the statement axis to the member axis. The quoter is not touched; the pipeline value stays the equatable `TemplateGenerationResult`.

**Tech Stack:** C# / .NET 10 SDK (`global.json` pin), Roslyn `Microsoft.CodeAnalysis.CSharp` (5.0 floor), `netstandard2.0` for generator + runtime, xUnit v3 (MTP v2) + Verify snapshots, jujutsu (jj) over git.

## Global Constraints

Every task implicitly includes these (from the spec + `principles.md` + `architecture.md`):

- **Cacheability is sacred.** Never capture `Compilation`/`ISymbol`/`SemanticModel`/`SyntaxNode` into pipeline state. ALL new analysis runs inside the `ForAttributeWithMetadataName` transform of `TemplateFactorySourceGenerator`; the only value flowing out stays the equatable `TemplateGenerationResult` (generated text + `EquatableArray<DiagnosticInfo>`). Task 3 ships an incremental-caching guard asserting **all** tracked steps (`TemplateTrackingNames.Transform` and `.Result`) are `Cached`/`Unchanged` on an unrelated edit (per `CacheabilityAssert.AllStepsCachedOrUnchanged`; iterate every tracked step, not just the terminal one).
- **The quoter is not modified.** `src/Synto/CSharpSyntaxQuoter*.cs` and `src/Synto.Bootstrap/CSharpSyntaxQuoter*.cs` stay byte-identical. The member splice is expressed only through the orchestrator's member-list emission + the existing `BuildList`/`ListSegment` helper. If any task finds it *must* touch a quoter `Visit*` method, **stop and raise it** — that contradicts the design.
- **Surface is minimal, `internal`, single-source-of-truth.** `[Splice]` is authored once in `src/Synto/Templating/SpliceAttribute.cs`, already embedded under the `Synto.Runtime.*` LogicalName prefix and injected `internal` by `SurfaceInjectionGenerator`. Task 1 only **widens its `AttributeUsage`** — no new embed, no csproj change. The generator must never re-declare the marker.
- **No forced runtime dependency in consumer output.** Generated output references only BCL + Roslyn + the consumer's own code; zero `Synto.*` runtime dependency. The generator body is emitted into the consumer's factory and runs when the consumer invokes it.
- **The generator is synchronous** (spec §8). No `async` / `Task` / `IAsyncEnumerable` return support in this feature.
- **Failures are diagnostics, not exceptions.** Invalid `[Splice]` method shapes are reported via new descriptors (SY1019–SY1021), never thrown. A returned node that is a valid `MemberDeclarationSyntax` but illegal in member position is **not** policed by Synto — it fails the post-generation compile as an ordinary CS error (spec §5).
- **Platform:** `netstandard2.0` everywhere for generator + runtime; `EnforceExtendedAnalyzerRules` stays on.
- **Green gate (run before every commit):** `dotnet build -c Debug` (0 errors; analyzer **warnings** are findings, not gate failures) · `dotnet test --no-build -c Debug` (all green — **never** pass `--nologo`; MTP filter syntax is `-- --filter-method "*Name*"`) · `dotnet format whitespace` then `dotnet format whitespace --verify-no-changes` (no diffs; whitespace scope only).
- **Snapshots:** rebaseline via Verify-accept (move `*.received.*` over `*.verified.*`) + delete orphans; the snapshot orphan guard enforces every snapshot maps to a test method. **NEVER hand-edit a `.verified.cs`.**
- **Commits:** Conventional Commits, one commit per task, **no AI/Claude footer**. VCS is **jj** (`jj commit -m "…"`); **never** `git`.
- **Branch / integration:** work lands on bookmark **`experimental`** (currently at `fc3ab5d6`); **no push**; do not move the bookmark by hand (the operator/integration advances it). Keep the parked operator files **`.rtk/filters.toml`** and **`CLAUDE.md`** OUT of every commit (they live uncommitted in the working copy) — commit by-path.

## Locked Names (resolved in spec §6 — do not re-litigate)

- Marker: `Synto.Templating.SpliceAttribute`, widened to `[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.GenericParameter | AttributeTargets.Method, AllowMultiple = false)]`. Authored `public`, injected `internal`. No inline facade (member-position splicing is the declaration itself).
- Return contract keyed on the **base** `Microsoft.CodeAnalysis.CSharp.Syntax.MemberDeclarationSyntax` (covers nested types, fields, properties, delegates — spec §6.4): a `[Splice]` method returns `MemberDeclarationSyntax` (or a subtype) **or** `System.Collections.Generic.IEnumerable<MemberDeclarationSyntax>` (or of a subtype).
- Diagnostics (next free is **SY1019**): `SY1019` non-static, `SY1020` bad return type, `SY1021` has parameters. Category `Synto.Usage`, `DiagnosticSeverity.Error`.
- Finder: `SpliceMemberGeneratorFinder.FindGenerators(SemanticModel, SyntaxNode) : IReadOnlyList<SpliceMemberGenerator>`.

## File Structure

**Create (generator, `src/Synto.SourceGenerator/Templating/`):**
- `SpliceMemberGeneratorFinder.cs` — discovers `[Splice]` **method** declarations and classifies each (static?, parameterless?, return shape). Mirror the two-phase walker style of `SpliceParameterFinder.cs`.

**Create (test, `test/Synto.Test/Templating/`):**
- `SpliceMemberGeneratorTest.cs` — invalid-shape diagnostics (Task 2), a snapshot of the canonical member-per-column emission (Task 3), and the incremental-caching guard (Task 3). Self-contained harness with the Roslyn reference set (mirror `InjectedSurfaceCompletenessTest`'s references) so a template referencing `IEnumerable<MemberDeclarationSyntax>` binds.

**Modify:**
- `src/Synto/Templating/SpliceAttribute.cs` — widen `AttributeUsage` to add `AttributeTargets.Method`; extend the doc-comment to describe the member-generator role.
- `src/Synto.SourceGenerator/Diagnostics.cs` — add `SpliceMethodMustBeStatic` (SY1019), `SpliceMethodBadReturnType` (SY1020), `SpliceMethodHasParameters` (SY1021).
- `src/Synto.SourceGenerator/Templating/TemplateFactorySourceGenerator.cs` — call the finder; emit SY1019–SY1021 for invalid shapes; for valid generators, emit the body as factory-time code (`Parameter<>()` rewritten to factory params), fold its `Parameter<>()` into the factory signature, splice returned members into the type's member list via `BuildList<MemberDeclarationSyntax>` at the generator's declaration position, and exclude the generator method from the quoter (it is not a quoted output member).
- `src/Synto.SourceGenerator/Templating/BindingTimeClassifier.cs` — only if needed: ensure a `[Splice]` generator method body is **not** classified/unrolled (its `foreach` over a `Parameter<>()` is a real factory-time loop, not a staged region). Preferred: the orchestrator excludes the generator method from the classified member set, requiring no classifier change — confirm during Task 3.
- `test/Synto.Test/Templating/InjectedSurfaceCompletenessTest.cs` — the headline compile-assert (Task 3), the nested-type + order-preservation compile-asserts (Task 4).
- `test/Synto.Test/RoundTripTests.cs` — a `[Splice]` member-generator round-trip (Task 5).
- `examples/Synto.Examples/Program.cs` — an ObjectReader-shaped `[Splice]` demo (Task 5).
- `docs/superpowers/notes/2026-06-27-live-staged-templates-friction.md` — append genuine friction findings (all tasks).

**Reuse (do not modify):** the quoter files; `SurfaceInjectionGenerator.cs` (iterates `Synto.Runtime.*` as-is — re-injects the widened `SpliceAttribute` with no code change); `CollectionSyntaxExtensions.BuildList<TNode>`/`ListSegment<TNode>` (already generic over `TNode : SyntaxNode` — works for `MemberDeclarationSyntax` unchanged); `StagedParameterFinder` (already discovers `Parameter<>()` across all members and dedups by `(name, type)` — covers the generator body's calls).

---

### Task 1: Surface — widen `[Splice]` to target methods

**Files:**
- Modify: `src/Synto/Templating/SpliceAttribute.cs`
- Test: `test/Synto.Test/snapshots/SurfaceInjectionTest.*#SpliceAttribute*.verified.cs` (snapshot-driven; the injected `#SpliceAttribute` snapshot updates)

**Interfaces:**
- Produces: `SpliceAttribute` with `[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.GenericParameter | AttributeTargets.Method, AllowMultiple = false)]`.

- [ ] **Step 1: Confirm the baseline injection snapshot is green** — `dotnet test --no-build -c Debug -- --filter-method "*VerifyInjectedSurface*"`. Expected: PASS (records the current `#SpliceAttribute` snapshot with `Parameter | GenericParameter`).
- [ ] **Step 2: Widen the `AttributeUsage`** in `SpliceAttribute.cs` to add `AttributeTargets.Method`. Extend the `<summary>` to state: "On a **static method** inside a `[Template]` type, `[Splice]` marks an auto-invoked **member generator** — a factory-time method returning `MemberDeclarationSyntax` / `IEnumerable<MemberDeclarationSyntax>` whose returned members are spliced into the generated type. Contrast `[Unquote]` (value lift). The method runs at factory-build time; output-world members are referenced as syntax, never touched as instances."
- [ ] **Step 3: Build** — `dotnet build -c Debug`. Expected: 0 errors.
- [ ] **Step 4: Re-run the injection snapshot test; accept the updated snapshot.** `dotnet test --no-build -c Debug -- --filter-method "*VerifyInjectedSurface*"` produces a `*.received.*` for `#SpliceAttribute` showing the widened `AttributeUsage`. Verify-accept (move received→verified). Re-run: PASS. Confirm no orphan-guard failure.
- [ ] **Step 5: Green gate + commit (by-path; exclude `.rtk/filters.toml`, `CLAUDE.md`).**

```bash
jj commit -m "feat(templating): widen [Splice] to target methods (member-generator surface)" \
  src/Synto/Templating/SpliceAttribute.cs 'glob:"test/Synto.Test/snapshots/**"'
```

---

### Task 2: `[Splice]` method finder + invalid-shape diagnostics (SY1019–SY1021)

Discover `[Splice]`-marked methods, classify each, and reject invalid shapes with diagnostics **before** building the emission path. TDD: errors first.

**Files:**
- Create: `src/Synto.SourceGenerator/Templating/SpliceMemberGeneratorFinder.cs`
- Modify: `src/Synto.SourceGenerator/Diagnostics.cs` (3 descriptors)
- Modify: `src/Synto.SourceGenerator/Templating/TemplateFactorySourceGenerator.cs` (call finder; emit diagnostics; skip invalid generators)
- Test: `test/Synto.Test/Templating/SpliceMemberGeneratorTest.cs` (new)

**Interfaces:**
- Produces:
  ```csharp
  internal enum SpliceMemberReturnShape { Single, Enumerable, Invalid }

  internal sealed class SpliceMemberGenerator
  {
      public MethodDeclarationSyntax Method { get; }
      public bool IsStatic { get; }
      public bool HasParameters { get; }
      public SpliceMemberReturnShape ReturnShape { get; }   // computed via SemanticModel; no symbol retained
  }

  internal sealed class SpliceMemberGeneratorFinder : CSharpSyntaxWalker
  {
      public static IReadOnlyList<SpliceMemberGenerator> FindGenerators(SemanticModel semanticModel, SyntaxNode node);
  }
  ```
  `ReturnShape`: resolve the method's return type; `Single` if it is `MemberDeclarationSyntax` or a subtype; `Enumerable` if it is `System.Collections.Generic.IEnumerable<T>` where `T` is `MemberDeclarationSyntax` or a subtype; `Invalid` otherwise. Use `Compilation.GetTypeByMetadataName("Microsoft.CodeAnalysis.CSharp.Syntax.MemberDeclarationSyntax")` and the special `IEnumerable<T>` type. Nothing is captured into pipeline state (the result is consumed entirely inside the transform).
- Produces (diagnostics, in `Diagnostics.cs`, mirroring the `BuilderBadReturnShape`/SY1017 pattern):
  ```csharp
  public static DiagnosticInfo SpliceMethodMustBeStatic(Location? location, string methodName);      // SY1019
  public static DiagnosticInfo SpliceMethodBadReturnType(Location? location, string methodName, string returnType); // SY1020
  public static DiagnosticInfo SpliceMethodHasParameters(Location? location, string methodName);      // SY1021
  ```
  Messages (category `Synto.Usage`, `DiagnosticSeverity.Error`):
  - SY1019 "[Splice] member generator '{0}' must be static (it runs at factory-build time; an instance method could reach output-world members)."
  - SY1020 "[Splice] member generator '{0}' returns '{1}', which is not a supported shape (expected MemberDeclarationSyntax or IEnumerable<MemberDeclarationSyntax>)."
  - SY1021 "[Splice] member generator '{0}' must be parameterless (inputs are supplied via Parameter<T>(); the generator is auto-invoked with no caller)."

- [ ] **Step 1: Write the failing diagnostic tests.** Create `test/Synto.Test/Templating/SpliceMemberGeneratorTest.cs` with a harness mirroring `InjectedSurfaceCompletenessTest`'s reference set (corlib, netstandard, System.Runtime, Console, `Microsoft.CodeAnalysis.SyntaxNode`, `Microsoft.CodeAnalysis.CSharp.SyntaxKind`, `System.Collections.Immutable`, System.Linq, System.Collections, System.Runtime.Extensions) and a `RunAndGetDiagnostics(string source)` helper (parse → `CSharpGeneratorDriver.Create(new SurfaceInjectionGenerator(), new TemplateFactorySourceGenerator())` → `RunGenerators` → `GetRunResult().Diagnostics`). Each template references only the injected surface + Roslyn:

```csharp
private const string NonStaticGenerator =
    """
    using System.Collections.Generic;
    using Synto.Templating;
    using static Synto.Templating.Template;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    partial class Factory {}
    [Template(typeof(Factory))]
    public class Reader {
        [Splice]                                                   // instance -> SY1019
        IEnumerable<MemberDeclarationSyntax> Gen() { yield break; }
    }
    """;

private const string BadReturnGenerator =
    """
    using Synto.Templating;
    using static Synto.Templating.Template;
    partial class Factory {}
    [Template(typeof(Factory))]
    public class Reader {
        [Splice] static int Gen() => 0;                            // bad return -> SY1020
    }
    """;

private const string HasParametersGenerator =
    """
    using System.Collections.Generic;
    using Synto.Templating;
    using static Synto.Templating.Template;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    partial class Factory {}
    [Template(typeof(Factory))]
    public class Reader {
        [Splice] static IEnumerable<MemberDeclarationSyntax> Gen(int x) { yield break; }  // params -> SY1021
    }
    """;

[Fact]
public void NonStaticSpliceMethod_ReportsSY1019()
{
    var diagnostics = RunAndGetDiagnostics(NonStaticGenerator);
    Assert.Single(diagnostics, d => d.Id == "SY1019");
}

[Fact]
public void BadReturnTypeSpliceMethod_ReportsSY1020()
{
    var diagnostics = RunAndGetDiagnostics(BadReturnGenerator);
    Assert.Single(diagnostics, d => d.Id == "SY1020");
}

[Fact]
public void ParameterizedSpliceMethod_ReportsSY1021()
{
    var diagnostics = RunAndGetDiagnostics(HasParametersGenerator);
    Assert.Single(diagnostics, d => d.Id == "SY1021");
}
```

- [ ] **Step 2: Run; verify they fail** — `dotnet test --no-build -c Debug -- --filter-method "*SpliceMethod*"`. Expected: FAIL (no SY1019/1020/1021 yet; `[Splice]` on a method is recognized by the compiler post-Task-1 but ignored by the generator).
- [ ] **Step 3: Implement** `SpliceMemberGeneratorFinder` (discover `[Splice]` methods via attribute-symbol match like `SpliceParameterFinder.VisitParameter`, but over `VisitMethodDeclaration`; compute `IsStatic`, `HasParameters`, `ReturnShape`) + the 3 descriptors + orchestrator wiring: call `FindGenerators` inside the transform; for each generator, emit SY1019 if not static, SY1021 if it has parameters, SY1020 if `ReturnShape == Invalid`; an invalid generator is **skipped** (not emitted, and not left as a quoted output member). A valid generator is recognized but **not yet emitted** (Task 3) — for now it is simply skipped from the quoted member set so generation stays green.
- [ ] **Step 4: Run; verify PASS** — `dotnet test --no-build -c Debug -- --filter-method "*SpliceMethod*"`.
- [ ] **Step 5: Green gate + commit (by-path).**

```bash
jj commit -m "feat(templating): [Splice] member-generator finder + invalid-shape diagnostics (SY1019-1021)" \
  src/Synto.SourceGenerator/Templating/SpliceMemberGeneratorFinder.cs \
  src/Synto.SourceGenerator/Diagnostics.cs \
  src/Synto.SourceGenerator/Templating/TemplateFactorySourceGenerator.cs \
  'glob:"test/Synto.Test/**"'
```

---

### Task 3: Emit the generator + splice members (the headline)

A valid `[Splice]` static generator's body is emitted as **factory-time code** (its `foreach` is a real loop, not unrolled), its `Parameter<>()` calls fold into the `Factory` signature and are rewritten to the factory parameters, and its returned members are spliced into the produced type's member list via `BuildList<MemberDeclarationSyntax>` at the generator's declaration position. The generator method is trimmed from the output type.

**Files:**
- Modify: `src/Synto.SourceGenerator/Templating/TemplateFactorySourceGenerator.cs`
- Modify (only if needed): `src/Synto.SourceGenerator/Templating/BindingTimeClassifier.cs`
- Test: `test/Synto.Test/Templating/InjectedSurfaceCompletenessTest.cs` (compile-assert), `test/Synto.Test/Templating/SpliceMemberGeneratorTest.cs` (snapshot + cacheability)

**Interfaces:**
- Consumes: `SpliceMemberGeneratorFinder.FindGenerators` (Task 2); `StagedParameterFinder` (factory-parameter fold + dedup); `CollectionSyntaxExtensions.BuildList<MemberDeclarationSyntax>` / `ListSegment<MemberDeclarationSyntax>.Run` (the existing file-local helper, unchanged).
- Locked behavior:
  - The generator method body is emitted **verbatim as factory-time C#** (NOT passed through the quoter/`BindingTimeClassifier`); only its `Parameter<>()` invocations are rewritten to the corresponding factory parameter identifiers (the same parameters `StagedParameterFinder` folds — deduped by `(name, type)` with the rest of the template).
  - The generated type's member list is emitted as `CollectionSyntaxExtensions.BuildList<MemberDeclarationSyntax>(…segments…)` where each fixed quoted member is a single-node segment and the generator contributes a `ListSegment<MemberDeclarationSyntax>.Run(<generated members>)` **at its declaration position** among the siblings.
  - The `[Splice]` generator method does **not** appear as a member of the generated type.

- [ ] **Step 1: Write the headline failing compile-assert test** in `InjectedSurfaceCompletenessTest.cs` — `SpliceMemberGenerator_EmitsMemberPerColumn_Compiles()`. Template references ONLY the injected surface + Roslyn + std:

```csharp
private const string SpliceMemberGeneratorTemplate =
    """
    using System;
    using System.Collections.Generic;
    using Synto.Templating;
    using static Synto.Templating.Template;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
    using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;

    partial class Factory {}

    public readonly record struct Col(int Ordinal, string Name);

    [Template(typeof(Factory))]
    public class Reader {
        [Splice]
        static IEnumerable<MemberDeclarationSyntax> Accessors() {
            var columns = Parameter<IReadOnlyList<Col>>();   // folds into the Factory signature
            foreach (var c in columns)                       // real factory-time loop (NOT unrolled)
                yield return MethodDeclaration(PredefinedType(Token(IntKeyword)), Identifier("Get" + c.Name))
                    .AddModifiers(Token(PublicKeyword))
                    .WithExpressionBody(ArrowExpressionClause(
                        LiteralExpression(NumericLiteralExpression, Literal(c.Ordinal))))
                    .WithSemicolonToken(Token(SemicolonToken));
        }
    }
    """;
```

Use the same driver + reference set as the other `InjectedSurfaceCompletenessTest` methods. Assert: generator diagnostics empty; a `Factory.Reader` tree exists; **post-generation `output.GetDiagnostics()` has no errors**; and the factory source `Contains("columns")` (the folded parameter) and `Contains("BuildList<MemberDeclarationSyntax>")` (the member splice happened). Mirror the assertion structure of `QuoteParam_InLoopCondition_KeepsRuntimeLoop_Compiles`.

- [ ] **Step 2: Run it; verify it fails** — Task 2 recognizes the generator but does not emit it, so the produced `Reader` has no generated members and the member splice is absent: the assert on `BuildList<MemberDeclarationSyntax>` fails (and/or `columns` is not folded). Expected: FAIL.
- [ ] **Step 3: Implement** the emission: rewrite the generator body's `Parameter<>()` calls to the factory parameters; emit the body as a factory-time helper that produces the members; fold the generator's `Parameter<>()` into the factory signature (via the existing `StagedParameterFinder` dedup); replace the type's member-list emission with `BuildList<MemberDeclarationSyntax>(…)` interleaving the generator's `Run(...)` at its position with the quoted fixed members; ensure the generator method is excluded from the quoted member set and from `BindingTimeClassifier` (its `foreach` must not unroll). Touch `BindingTimeClassifier.cs` only if exclusion cannot be done purely in the orchestrator.
- [ ] **Step 4: Run; verify PASS** — `dotnet test --no-build -c Debug -- --filter-method "*SpliceMemberGenerator_EmitsMemberPerColumn*"`.
- [ ] **Step 5: Add the snapshot + cacheability tests** in `SpliceMemberGeneratorTest.cs`:
  - `MemberGenerator_CanonicalShape` (snapshot): the `SpliceMemberGeneratorTemplate` above, verified via `Verify(result).UseDirectory("snapshots")` — pins the `BuildList<MemberDeclarationSyntax>(ListSegment<MemberDeclarationSyntax>.Run(...))` member-list emission shape. Accept the snapshot.
  - `SpliceMemberGeneratorTemplate_IsIncrementalOnUnrelatedEdit` (cacheability): mirror `SimpleTemplateTest.StagedTemplate_IsIncrementalOnUnrelatedEdit` — run with `trackIncrementalGeneratorSteps: true`, add an unrelated syntax tree, and assert `CacheabilityAssert.AllStepsCachedOrUnchanged(result, new[] { TemplateTrackingNames.Transform, TemplateTrackingNames.Result })`.
- [ ] **Step 6: Run all; accept snapshots; green gate + commit (by-path, include new snapshots).**

```bash
jj commit -m "feat(templating): [Splice] static method splices generated members into the type" \
  src/Synto.SourceGenerator/Templating/TemplateFactorySourceGenerator.cs \
  src/Synto.SourceGenerator/Templating/BindingTimeClassifier.cs \
  'glob:"test/Synto.Test/**"'
```

(Drop `BindingTimeClassifier.cs` from the commit path list if Step 3 did not modify it.)

---

### Task 4: Nested types + order preservation (the `MemberDeclarationSyntax`-base payoff)

Lock the two behaviors the base-type contract unlocks: a generator may emit **nested types**, and generated members land at the generator's **position** among fixed quoted siblings.

**Files:**
- Test: `test/Synto.Test/Templating/InjectedSurfaceCompletenessTest.cs`

**Interfaces:**
- Consumes: the Task 3 emission path (no new production code expected; if a test fails, fix-forward the orchestrator).

- [ ] **Step 1: Write the nested-type compile-assert** `SpliceMemberGenerator_EmitsNestedType_Compiles()`. Template generator `yield return`s a nested `record` per column (a `RecordDeclarationSyntax`, which is a `MemberDeclarationSyntax`):

```csharp
private const string SpliceNestedTypeTemplate =
    """
    using System;
    using System.Collections.Generic;
    using Synto.Templating;
    using static Synto.Templating.Template;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
    using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;

    partial class Factory {}

    public readonly record struct Col(int Ordinal, string Name);

    [Template(typeof(Factory))]
    public class Shapes {
        [Splice]
        static IEnumerable<MemberDeclarationSyntax> Nested() {
            var columns = Parameter<IReadOnlyList<Col>>();
            foreach (var c in columns)
                yield return ClassDeclaration("Shape_" + c.Name).AddModifiers(Token(PublicKeyword));
        }
    }
    """;
```

Assert (same harness): generator diagnostics empty; `Factory.Shapes` tree exists; post-generation compile has no errors; factory source `Contains("BuildList<MemberDeclarationSyntax>")`.

- [ ] **Step 2: Run; verify PASS** (Task 3's base-type keying should already cover it). If it fails, fix-forward Task 3's emission so it does not assume `MethodDeclarationSyntax`.
- [ ] **Step 3: Write the order-preservation compile-assert** `SpliceMemberGenerator_PreservesDeclarationOrder_Compiles()`. A template with a **fixed quoted member**, then the `[Splice]` generator, then another **fixed quoted member**:

```csharp
private const string SpliceOrderTemplate =
    """
    using System;
    using System.Collections.Generic;
    using Synto.Templating;
    using static Synto.Templating.Template;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
    using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;

    partial class Factory {}

    public readonly record struct Col(int Ordinal, string Name);

    [Template(typeof(Factory))]
    public class Reader {
        public int First() => 0;                                  // fixed quoted member
        [Splice]
        static IEnumerable<MemberDeclarationSyntax> Middle() {
            var columns = Parameter<IReadOnlyList<Col>>();
            foreach (var c in columns)
                yield return MethodDeclaration(PredefinedType(Token(IntKeyword)), Identifier("Get" + c.Name))
                    .AddModifiers(Token(PublicKeyword))
                    .WithExpressionBody(ArrowExpressionClause(
                        LiteralExpression(NumericLiteralExpression, Literal(c.Ordinal))))
                    .WithSemicolonToken(Token(SemicolonToken));
        }
        public int Last() => 1;                                   // fixed quoted member
    }
    """;
```

Assert: post-generation compile has no errors, and the factory source places the generator's `Run(...)` segment **between** the `First` and `Last` member segments (assert the `BuildList<MemberDeclarationSyntax>(...)` argument order has `First` before the `Run(` and `Run(` before `Last` via ordinal `IndexOf` checks on the factory source string).

- [ ] **Step 4: Run; verify PASS.** Fix-forward if the generator's run is not interleaved at its declaration position.
- [ ] **Step 5: Green gate + commit (by-path).**

```bash
jj commit -m "test(templating): [Splice] member generator emits nested types, preserves order" \
  src/Synto.SourceGenerator/Templating/TemplateFactorySourceGenerator.cs \
  'glob:"test/Synto.Test/**"'
```

(Drop the `TemplateFactorySourceGenerator.cs` path if no fix-forward was needed.)

---

### Task 5: Round-trip + dog-food example + friction wrap-up

Prove the feature end-to-end against the re-parse round-trip harness and dog-food it in the examples.

**Files:**
- Modify: `test/Synto.Test/RoundTripTests.cs` (add `SpliceMembers`)
- Modify: `examples/Synto.Examples/Program.cs` (a `[Splice]` member-generator demo)
- Modify: `docs/superpowers/notes/2026-06-27-live-staged-templates-friction.md`

**Interfaces:**
- Consumes: the Task 3 `[Splice]` member-generator path.

- [ ] **Step 1: Write the round-trip test** `RoundTripTests.SpliceMembers`: a `[Splice]` member generator over a fixed two-column input; assert `Factory.Reader(cols)` produces a type whose normalized text equals the expected class with one accessor per column. Model the harness on the existing round-trip tests (invoke the factory, `NormalizeWhitespace(eol: Environment.NewLine)`, compare to an expected literal). Concrete expected shape (two columns `{0,"Id"}`, `{1,"Name"}` with the Task-3 generator body):

```csharp
string expected = """
                  public class Reader
                  {
                      public int GetId() => 0;
                      public int GetName() => 1;
                  }
                  """;
```

(A comment should name the contrast with the statement-level `RunCollectTest` unroll: here the loop produces *members*, not statements, and cite spec §2–§3.)

- [ ] **Step 2: Run; verify PASS** — `dotnet test --no-build -c Debug -- --filter-method "*SpliceMembers*"`.
- [ ] **Step 3: Add the `Program.cs` demo** — an ObjectReader-shaped `[Splice]` member generator beside the existing `[Unquote]`/`[Quote]` examples, printing the generated type so the example output shows one accessor member per column. Add a `Choice` entry for it in the selection prompt.
- [ ] **Step 4: Append friction findings** to the friction note (e.g. any strain in excluding the generator body from the classifier, the `Parameter<>()`-rewrite-in-factory-code seam, DRY wins/losses). Empty is acceptable — do not manufacture.
- [ ] **Step 5: Full green gate** (`build` · `test --no-build` · `format whitespace --verify-no-changes`) + commit (by-path).

```bash
jj commit -m "test(templating): [Splice] member-generator round-trip + example demo" \
  test/Synto.Test/RoundTripTests.cs examples/Synto.Examples/Program.cs \
  docs/superpowers/notes/2026-06-27-live-staged-templates-friction.md
```

---

## Self-Review (against spec `2026-06-28-splice-member-generator-design.md`)

- **§3 widen `[Splice]` to `Method`, static, return-type dispatch:** Task 1 (surface) + Task 2 (finder classifies return shape, static/params rules). ✓
- **§3 auto-invoked, spliced at declaration position, generator trimmed:** Task 3 (emit + `BuildList` splice + trim) + Task 4 (order preservation). ✓
- **§3 body is ordinary factory-time C#; `foreach` is a real loop (not unrolled):** Task 3 locked behavior (generator body excluded from quoter/classifier; only `Parameter<>()` rewritten). ✓
- **§4 `Parameter<>()` folds via existing dedup; member splice reuses `BuildList`, lifted to the member axis; emit-and-call not inline:** Task 3. ✓
- **§4 cacheability:** Task 3 incremental guard (all tracked steps). ✓
- **§5 three diagnostics (SY1019 non-static, SY1020 bad return, SY1021 has params); no placement policing:** Task 2 (diagnostics) + Global Constraints (placement → post-gen CS error). ✓
- **§6.4 return contract on the `MemberDeclarationSyntax` base → nested types:** Task 4 nested-type compile-assert. ✓
- **§7 headline compile-assert, nested type, order, diagnostics, cacheability, dog-food:** Tasks 3/4/2/5. ✓
- **§8 synchronous only:** Global Constraints (no async surface). ✓
- **Placeholder scan:** no TBD/TODO; every task has concrete test scenarios + asserts. ✓
- **Type consistency:** `SpliceMemberGenerator`/`SpliceMemberReturnShape`/`SpliceMemberGeneratorFinder.FindGenerators`/`SpliceMethodMustBeStatic`/`SpliceMethodBadReturnType`/`SpliceMethodHasParameters` used consistently across tasks. ✓

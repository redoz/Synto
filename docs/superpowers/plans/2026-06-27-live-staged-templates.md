# Live (Staged) Templates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Plan style:** This plan pins **contracts, interfaces, signatures, and tests** — not prescribed implementation bodies (matching the `objectreader-example` / `match-replace` plans). Each task states exact public/internal signatures, the locked behavior, and concrete test scenarios; the implementer chooses the generator implementation that satisfies them. **Test code is the spec** — write it as given, adapting only to exact harness helper names already present in `test/Synto.Test`.
>
> **Source of truth:** `docs/superpowers/specs/2026-06-27-live-staged-templates-design.md` (committed at `49b5945`). Every "ILLUSTRATIVE" spelling there is an owner decision resolved in **Task 0** before any snapshot is frozen.

**Goal:** Add a binding-time split to `[Template]` bodies so that data marked **live** has its dataflow trace emitted **verbatim as executing C#** into the generated factory (runs at template-invocation time), while everything else is **quoted** as today — yielding loop-unrolling / branch-specialization and a generic, user-extensible **syntax-builder** surface, with the ObjectReader dog-food as the acceptance bar.

**Architecture:** All new behavior rides the existing `TemplateSyntaxQuoter` seam (`unquotedReplacements : Dictionary<SyntaxNode, ExpressionSyntax>` + `trimNodes`); the base `CSharpSyntaxQuoter`/`CSharpSyntaxQuoterBase` and the bootstrap copy are **not modified**. New finders (mirroring `InlinedParameterFinder`/`SyntaxParameterFinder`) discover live roots and builder calls; a binding-time classifier partitions nodes; live control regions are precomputed into a single replacement expression (verbatim scaffold + collected quoted islands) keyed at the **owning container** node. New consumer surface (`Parameter<T>()`, `[Live]`, the syntax-builder facades/built-ins `Member`/`TypeOf`) is authored once in `src/Synto`, injected `internal` via the existing embed→rewrite path; collection helpers are file-local injected like `ToSyntax`. All staging is emit-time inside the factory; the pipeline value stays the equatable `TemplateGenerationResult`.

**Tech Stack:** C# / .NET 10 SDK (`global.json` pins `10.0.300`), Roslyn `Microsoft.CodeAnalysis.CSharp` (5.0 floor), `netstandard2.0` for generator + runtime, xUnit v3 (MTP v2) + Verify snapshots, jujutsu (jj) over git.

## Global Constraints

Every task implicitly includes these (copied from the spec + `principles.md` + `architecture.md`):

- **Cacheability is sacred (spec §8).** Never capture `Compilation`/`ISymbol`/`SemanticModel`/`SyntaxNode` into pipeline state. ALL new analysis and staging runs inside the `ForAttributeWithMetadataName` transform of `TemplateFactorySourceGenerator`; the only value flowing out stays the equatable `TemplateGenerationResult` (generated text + `EquatableArray<DiagnosticInfo>`). A new incremental-caching guard test (Task 10) must show `Cached/Unchanged` on an unrelated edit for a **staged** template.
- **The quoter is not modified (spec §5).** `src/Synto/CSharpSyntaxQuoter.cs`, `CSharpSyntaxQuoter.Generated.cs`, and the bootstrap copy `src/Synto.Bootstrap/CSharpSyntaxQuoter.cs` stay byte-identical in their per-node-kind handling. Staging is expressed only through `unquotedReplacements`/`trimNodes` entries and by running fresh `TemplateSyntaxQuoter` instances on islands. If any task finds it *must* touch a quoter `Visit*` method, **stop and raise it** — that contradicts the spec and is an owner decision.
- **Surface is minimal, `internal`, single-source-of-truth (`principles.md`).** New markers/facades are authored once under `src/Synto/Templating/`, embedded in `Synto.SourceGenerator.csproj` under the `Synto.Runtime.*` LogicalName prefix, and injected `internal` by `SurfaceInjectionGenerator` (the `public`→`internal` rewrite). New emitted **helpers** (collection builders) are authored once in `src/Synto`, embedded under `Synto.Helper.*`, and emitted **`file static`** by the `FindReferencedHelpers` scan. The generator must **never re-declare** a marker/helper.
- **No forced runtime dependency in consumer output.** User-authored syntax builders are called **fully-qualified as static methods** (exactly like `[Runtime]` converters today — `TemplateFactorySourceGenerator.cs:424-443`), never injected as a copy. Generated output references only BCL + Roslyn + the consumer's own code; zero `Synto.*` runtime dependency.
- **Failures are diagnostics, not exceptions (`principles.md`).** Every new error path is catch-convert-report to a `SY####` `DiagnosticInfo`; the existing `try/catch` → `Diagnostics.InternalError` in `GenerateTemplate` is the backstop. No new unguarded `!`/`.First()`/`.Single()` on input-dependent data.
- **Diagnostics are first-class.** Every staging/builder behavior ships **with its diagnostic and a test asserting the diagnostic Id + a real source span** (mirror `AssertHasRealSpan` in `SimpleTemplateTest`), not just the happy path.
- **Platform:** `netstandard2.0` everywhere for generator + runtime; no APIs unavailable there; `EnforceExtendedAnalyzerRules` stays on. New `SY####` descriptors are listed in `AnalyzerReleases.Unshipped.md`.
- **Green gate (run before every commit):** `dotnet build -c Debug` (0 errors; analyzer **warnings** are findings, not gate failures) · `dotnet test --no-build -c Debug` (all green — **never** pass `--nologo`) · `dotnet format whitespace` then `dotnet format whitespace --verify-no-changes` (no diffs; **whitespace scope only**). The self-host bootstrap must still build Bootstrap before `Synto`.
- **Commits:** Conventional Commits, **no AI/Claude footer**, one commit per task. VCS is **jj**: `jj commit -m "…"`. **Never** `git`.
- **Branch:** experimental work lands on **`experimental/live-staged-templates`** (created off the spec commit `49b5945`), never `main`; **no push**; do not move the integration bookmark (the operator advances it after review).
- **Friction log (secondary deliverable):** append genuine findings to `docs/superpowers/notes/2026-06-27-live-staged-templates-friction.md` as you go (where the seam strained, where the quoter *almost* needed changing, builder/usings sharp edges). Empty findings are fine — do not manufacture friction.

## File Structure

**Create (runtime surface, `src/Synto/Templating/`):**
- `LiveAttribute.cs` — the `[Live]` capability marker (spec §3.2). Authored `public`, injected `internal`.
- `Template.Parameter.cs` (or fold into a `Template.cs`) — the `Template` static class hosting `Parameter<T>(string? parameterName = null)` and the built-in builder **facades** `Member<TValue>(object instance, string name)` / `TypeOf(string name)` (spec §3.1, §5.1). Inert (`=> default!`). Exact spelling per **Task 0**.
- `SyntaxBuilderAttribute.cs` — marks an individual `public static` method as a factory-time syntax builder (the public extensibility contract; mirrors `[Runtime]`), `[AttributeUsage(AttributeTargets.Method)]`, no ctor arg. Spelling locked in §4.
- `QuotedAttribute.cs` — marks a `[SyntaxBuilder]` method parameter whose corresponding call argument is **quoted** (output-world island) rather than passed as a live value. Required because the parameter *type* cannot disambiguate "quote this island into an `ExpressionSyntax`" from "pass a live `ExpressionSyntax` through unquoted" (deliberate metaprogramming). Authored `public`, injected `internal`. Spelling locked in §6.
- `ReturnTypeAttribute.cs` — marks the `[Quoted(AsTypeArg = true)]` builder param whose synthesized generic type parameter is the facade's **return type** (§5). Authored `public`, injected `internal`, `[AttributeUsage(AttributeTargets.Parameter)]`, no properties.

**Create (built-in builders, `src/Synto/`):**
- `SyntoBuilders.cs` — the built-in `[SyntaxBuilder]` static class providing `ExpressionSyntax Member(ExpressionSyntax instance, string name)` and `TypeSyntax TypeOf(string name)`. Called fully-qualified at factory time; NOT injected (it lives in `Synto.Core`)… **resolved in Task 0/3:** built-in builders are emitted/located the same way as a user builder (discovered + called fully-qualified) — they must reach consumer output with **no `Synto.*` dependency**, so if a built-in builder cannot be a plain `Synto.Core` static (because consumers don't reference it), it is injected `internal` like a marker. Pin in Task 0.

**Create (emitted helpers, `src/Synto/`):**
- `CollectionSyntaxExtensions.cs` — file-local collection helpers (spec §6): build a `SyntaxList<TNode>` from a mix of fixed nodes and node-runs. (SeparatedSyntaxList helper is an explicit scope note, Task 5.) Authored `public`, emitted `file static` by the scan.

**Create (generator, `src/Synto.SourceGenerator/Templating/`):**
- `LiveParameterFinder.cs` — discovers `Parameter<T>()` roots (both positions), resolves identity `(name, T)`, dedup, naming diagnostics.
- `LiveRootFinder.cs` — discovers `[Live]` declarations + `[Live]` method parameters.
- `SyntaxBuilderFinder.cs` — discovers `[SyntaxBuilder]` classes + matches carrier facade calls to builders (mirror `RuntimeConverterFinder`).
- `BindingTimeClassifier.cs` — the dataflow partition (quoted / live-value / live-control) given the roots; impossible-cut detection.
- `LiveRegionEmitter.cs` — precomputes the run+collect replacement expression for a live control region (verbatim scaffold + collected islands), keyed at the container.

**Modify:**
- `src/Synto.SourceGenerator/Synto.SourceGenerator.csproj` — add `EmbeddedResource` items for the new markers (`Synto.Runtime.*`), the new helper (`Synto.Helper.*`), and (if injected) the built-in builders.
- `src/Synto.SourceGenerator/Templating/FileLocalHelpers.cs` — add a `HelperEntry` for the collection helper(s).
- `src/Synto.SourceGenerator/Templating/TemplateFactorySourceGenerator.cs` — wire the new finders + classifier + region emitter into `CreateSyntaxFactoryMethod` (new `unquotedReplacements`/`trimNodes` entries; usings transplant for verbatim live code).
- `src/Synto.SourceGenerator/Diagnostics.cs` — new `SY####` descriptors (numbers assigned in Task 0).
- `src/Synto.SourceGenerator/AnalyzerReleases.Unshipped.md` — list the new descriptors.
- `examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Generator/ReaderTemplate.cs` + `ObjectReaderGenerator.cs` + `Model.cs` — dog-food migration (Task 9).
- `test/Synto.Test/Templating/InjectedSurfaceCompletenessTest.cs` — add a staged rich template (Task 10).
- `Synto.slnx` — no new projects (all changes are within existing projects).

**Reuse (do not modify):** `src/Synto/CSharpSyntaxQuoter*.cs`, `src/Synto.Bootstrap/*`, `src/Synto.SourceGenerator/Templating/TemplateSyntaxQuoter.cs` (the seam is already sufficient — confirm, don't change), `SurfaceInjectionGenerator.cs` (the rewrite path is reused as-is for new markers).

---

### Task 0: Decision gate — resolve open spellings & scope, freeze locked names (NO code)

The spec's surface spellings are a **one-way door** (snapshots pin them). Resolve every open decision before any snapshot freezes. This task produces a **Locked Names** section appended to the plan and **stops for owner sign-off**; no source changes.

**Open decisions to resolve (record each answer verbatim):**

1. **Marker namespace / spelling (spec §10.1).** Confirm `Synto.Templating.Template.Parameter<T>(string? parameterName = null)` as a static method; confirm the `[Live]` attribute name; confirm whether `Member`/`TypeOf` **facades** live on the same `Template` static class (so `using static Synto.Templating.Template;` exposes `Parameter`/`Member`/`TypeOf` together).
2. **`Parameter<T>()` return form under `netstandard2.0` (spec §10.2).** Confirm the `T`-returning inert function (`=> default!`) is the chosen carrier form (so `var x = Parameter<T>()` infers `x : T`).
3. **In-scope control-flow trim (spec §10.3).** Confirm whether v1 ships the full statement-level set (`for`/`foreach`/`while`/`if` + live locals + mutable accumulation, Tasks 6–7) or a smaller first cut (e.g. `foreach` + value-lift only). This sets which sub-tasks of Task 7 are in/out.
4. **Syntax-builder reach (owner answered: PUBLIC from v1).** The generic facade+builder mechanism ships its public extensibility contract in v1; `Member`/`TypeOf` are its first built-in instances. **Confirm** and pin the `[SyntaxBuilder]` attribute spelling.
5. **Facade↔builder pairing spelling.** Choose how a carrier facade is paired to its factory-time builder: (a) name + parameter-arity match within a `[SyntaxBuilder]` scan, (b) explicit `[SyntaxBuilder(typeof(Facade))]` / facade attribute reference, or (c) Synto synthesizes the facade from the builder. **Default for the plan: (b) explicit attribute pairing** (most diagnosable); confirm or override.
6. **Per-argument binding rule — by explicit attribute, NOT by parameter type (owner: corrected).** A parameter's *type* cannot decide binding-time: an `ExpressionSyntax` parameter might want the call argument **quoted** (the common island case) OR want a **live `ExpressionSyntax` value passed through unquoted** (deliberate metaprogramming) — the type is identical in both. So binding-time is declared **explicitly per builder parameter**: a **`[Quoted]`** parameter means "quote the corresponding call argument (output-world syntax) and pass the resulting node"; an **unmarked** parameter means "pass the live/computed value verbatim" (covers `string`, `int`, `T`, AND the meta `ExpressionSyntax node` splice). **Default for the plan:** unmarked = live, `[Quoted]` is the opt-in (the dangerous quote operation is the explicit one). Confirm, or choose the stricter alternative (require an explicit `[Quoted]`/`[Live]` on *every* builder parameter — maximal diagnostics, no defaulting). This rule powers the builder binding diagnostics (Task 3).
7. **Built-in builder delivery.** Confirm whether `Member`/`TypeOf` builders are plain `Synto.Core` statics called fully-qualified, or injected `internal` (needed if consumers don't reference `Synto.Core`). Decides whether output stays `Synto.*`-free.
8. **Diagnostic numbers.** Assign the next free `SY1010…` block for: missing parameter name, explicit-name collision, conflicting `(name, T)`, impossible cut, unsupported live shape, unpaired builder, builder argument-binding mismatch, builder bad return shape, ambiguous builder. (Current max is `SY1009`.)

- [ ] **Step 1: Produce the Locked Names table** — append a `## Locked Names (resolved Task 0)` section to this plan capturing answers to 1–8 (exact spellings, exact `SY####` numbers).
- [ ] **Step 2: STOP for owner sign-off.** Do not start Task 1 until the owner confirms the table. (This is the explicit owner-decision checkpoint the spec §10 requires.)
- [ ] **Step 3: Commit the plan update**

```bash
jj commit -m "docs(templating): lock live-staged-templates surface decisions (plan Task 0)"
```

---

### Task 1: `Parameter<T>()` live-parameter marker — depth-0 lift to a factory parameter

**Files:**
- Create: `src/Synto/Templating/Template.Parameter.cs` (or `Template.cs`)
- Create: `src/Synto.SourceGenerator/Templating/LiveParameterFinder.cs`
- Modify: `src/Synto.SourceGenerator/Synto.SourceGenerator.csproj` (embed `Synto.Runtime.Template.cs`), `TemplateFactorySourceGenerator.cs` (wire finder: add factory parameter + trim declaration), `Diagnostics.cs` + `AnalyzerReleases.Unshipped.md` (3 naming diagnostics)
- Test: `test/Synto.Test/Templating/LiveParameterTest.cs` (+ snapshots under `snapshots/`)

**Interfaces:**
- Produces: `Synto.Templating.Template.Parameter<T>(string? parameterName = null) : T` (inert, `=> default!`), injected `internal`. `LiveParameterFinder.FindLiveParameters(SemanticModel, SyntaxNode) : IEnumerable<LiveParameter>` where `LiveParameter` carries the resolved parameter name, type `T` (as `TypeSyntax`/symbol display), the originating declaration/invocation to trim, and the reference sites. Identity is `(name, T)`: same → one factory parameter referenced at each site; same name, different `T` → diagnostic.
- Consumes: the existing `CreateSyntaxFactoryMethod` trim+add-parameter path (mirror `InlinedParameterFinder` at `TemplateFactorySourceGenerator.cs:358-468`).
- Behavior (depth-0, this task): a `Parameter<T>()` value consumed in **quoted value position** lifts exactly like an `[Inline]` value — to `value.ToSyntax()` / a literal — but the value originates as a **factory parameter** (caller-supplied) rather than a method parameter marked `[Inline]`. Staging over the value (loops) arrives in Tasks 4–7.

- [ ] **Step 1: Write the failing tests** (`LiveParameterTest.cs`, mirror `SimpleTemplateTest` harness — `VerifyTemplate` for snapshots, `RunAndGetDiagnostics` + `AssertHasRealSpan` for diagnostics):

```csharp
// Snapshot: declaration-initializer position — the binding names the parameter.
[Fact]
public async Task DeclarationParameter_LiftsToFactoryParameter()
{
    await VerifyTemplate(
        """
        using Synto.Templating;
        using static Synto.Templating.Template;
        partial class Factory {}
        public class TestClass {
            [Template(typeof(Factory))]
            void Build() {
                var count = Parameter<int>();   // -> factory param `int count`; used as a value below
                System.Console.WriteLine(count);
            }
        }
        """);
    // Expected golden: Factory.Build(int count) { ... } and `count` lifted via count.ToSyntax() at the use site.
}

// Snapshot: inline position — parameterName REQUIRED.
[Fact]
public async Task InlineParameter_WithExplicitName_Lifts()
{
    await VerifyTemplate(
        """
        using Synto.Templating;
        using static Synto.Templating.Template;
        partial class Factory {}
        public class TestClass {
            [Template(typeof(Factory))]
            void Build() => System.Console.WriteLine(Parameter<int>("fieldCount"));
        }
        """);
}

// Diagnostic: inline with no binding and no name.
[Fact]
public void InlineParameter_MissingName_ReportsDiagnostic()
{
    var diagnostics = RunAndGetDiagnostics(
        """
        using Synto.Templating;
        using static Synto.Templating.Template;
        partial class Factory {}
        public class TestClass {
            [Template(typeof(Factory))]
            void Build() => System.Console.WriteLine(Parameter<int>());
        }
        """);
    var diag = Assert.Single(diagnostics, d => d.Id == "SY1010"); // missing-parameter-name (Task 0 §8)
    AssertHasRealSpan(diag);
}

// Diagnostic: same name, different T.
[Fact]
public void ConflictingParameterType_ReportsDiagnostic()
{
    var diagnostics = RunAndGetDiagnostics(
        """
        using Synto.Templating;
        using static Synto.Templating.Template;
        partial class Factory {}
        public class TestClass {
            [Template(typeof(Factory))]
            void Build() {
                var a = Parameter<int>("x");
                var b = Parameter<string>("x");
                System.Console.WriteLine($"{a}{b}");
            }
        }
        """);
    var diag = Assert.Single(diagnostics, d => d.Id == "SY1012"); // conflicting-(name,T) (Task 0 §8)
    AssertHasRealSpan(diag);
}
```

- [ ] **Step 2: Run to verify they fail** — `dotnet build -c Debug` then `dotnet test`. Expected: RED (marker not injected; `Parameter` unresolved; diagnostics absent).
- [ ] **Step 3: Implement (by contract)** — (a) author `Template.Parameter<T>` inert marker (spelling per Task 0) and embed it (`<EmbeddedResource Include="..\Synto\Templating\Template.Parameter.cs" LogicalName="Synto.Runtime.Template.cs" />`); (b) `LiveParameterFinder` recognizes the marker **by binding** (`GetSymbolInfo(invocation.Expression).Symbol` → `Template.Parameter`, like `SyntaxParameterFinder.cs:60-67`), resolves `(name, T)` identity with inferred-name auto-dedup (`while(!names.Add(n)) n+='_'`) and explicit-name collision/conflict diagnostics; (c) in `CreateSyntaxFactoryMethod`, add the factory parameter, register the use-site lift in `unquotedReplacements`, and trim the declaration/inline marker via `trimNodes`; (d) add the 3 descriptors (`SY1010/1011/1012`).
- [ ] **Step 4: Run tests + gate** — accept the two new snapshot goldens after confirming the factory signature gained the parameter and the use site lifts. All prior snapshots **unchanged** (this adds a new marker; existing templates don't use it).
- [ ] **Step 5: Commit** — `jj commit -m "feat(templating): Parameter<T>() live-parameter marker with identity/dedup diagnostics"`

---

### Task 2: `[Live]` capability marker — bound roots and live method parameters (depth-0)

**Files:**
- Create: `src/Synto/Templating/LiveAttribute.cs`
- Create: `src/Synto.SourceGenerator/Templating/LiveRootFinder.cs`
- Modify: `Synto.SourceGenerator.csproj` (embed `Synto.Runtime.LiveAttribute.cs`), `TemplateFactorySourceGenerator.cs` (treat `[Live]` roots; route depth-0 bound locals to the `preamble`), `LiveAttribute` AttributeUsage
- Test: `test/Synto.Test/Templating/LiveRootTest.cs` (+ snapshots)

**Interfaces:**
- Produces: `Synto.Templating.LiveAttribute` (`[AttributeUsage(AttributeTargets.Local | AttributeTargets.Parameter)]` — confirm targets in Task 0), injected `internal`. `LiveRootFinder.FindLiveRoots(SemanticModel, SyntaxNode) : IEnumerable<LiveRoot>` carrying the marked declaration/parameter symbol + node, classified as a live root (origin = bound).
- Consumes: Task 1's parameter/preamble plumbing. Behavior (depth-0): a `[Live] var n = <expr>;` whose `n` is consumed as a **value** runs `<expr>` at factory time (hoisted into the factory body / `preamble`), with `n` a real runtime local, and the use site lifts `n` to syntax. `[Live]` on a method parameter gives that parameter **live capability** (vs `[Inline]`'s splice). The depth-0 win of staging-by-intent over control flow lands in Tasks 6–7.

- [ ] **Step 1: Write the failing test:**

```csharp
[Fact]
public async Task LiveLocal_RunsInFactory_AndLiftsValue()
{
    await VerifyTemplate(
        """
        using Synto.Templating;
        partial class Factory {}
        public class TestClass {
            [Template(typeof(Factory))]
            void Build() {
                [Live] var n = 2 + 3;          // runs at factory time -> 5
                System.Console.WriteLine(n);   // lifts to literal 5 in the OUTPUT
            }
        }
        """);
    // Expected golden: the factory body computes n at runtime; the emitted Console.WriteLine arg is the literal 5
    // (the `[Live] var n` declaration is trimmed from the quoted output).
}
```

- [ ] **Step 2: Run to verify it fails** — RED (`[Live]` unresolved).
- [ ] **Step 3: Implement (by contract)** — author + embed `LiveAttribute`; `LiveRootFinder` recognizes it by attribute symbol (mirror `InlinedParameterFinder.VisitParameter`); for a depth-0 bound local, hoist the (verbatim) declaration into the factory `preamble` and register the use-site lift + trim. (Live locals that are part of a control-flow region are handled in Task 6, not hoisted to preamble — pin the partition rule here in the finder's contract.)
- [ ] **Step 4: Run tests + gate** — accept the snapshot; prior snapshots unchanged.
- [ ] **Step 5: Commit** — `jj commit -m "feat(templating): [Live] capability marker for bound roots and live parameters"`

---

### Task 3: Generic syntax-builder mechanism + `Member`/`TypeOf` built-ins (no staging)

**Files:**
- Create: `src/Synto/Templating/SyntaxBuilderAttribute.cs`, `src/Synto/SyntoBuilders.cs`, facade methods on `Template` (`Member`/`TypeOf`)
- Create: `src/Synto.SourceGenerator/Templating/SyntaxBuilderFinder.cs`
- Modify: `Synto.SourceGenerator.csproj` (embeds per Task 0 §7), `TemplateFactorySourceGenerator.cs` (replace a recognized facade call with a fully-qualified builder invocation over processed args), `Diagnostics.cs` + unshipped (builder diagnostics)
- Test: `test/Synto.Test/Templating/SyntaxBuilderTest.cs` (+ snapshots)

**Interfaces:**
- Produces:
  - `Synto.Templating.SyntaxBuilderAttribute` (marks a static class of factory-time builders; spelling per Task 0 §4), injected `internal`.
  - Built-in facades on `Template`: `Member<TValue>(object instance, string name) : TValue` and `TypeOf(string name) : System.Type` (inert), and built-in builders `ExpressionSyntax Member([Quoted] ExpressionSyntax instance, string name)` / `TypeSyntax TypeOf(string name)` (delivery per Task 0 §7).
  - `SyntaxBuilderFinder.FindBuilders(Compilation, INamedTypeSymbol syntaxBuilderAttribute) : ImmutableArray<INamedTypeSymbol>` and `FindBuilderFor(facadeSymbol) : BuilderMatch?` (mirror `RuntimeConverterFinder.FindRuntimeClasses`/`FindConvertersFor` — deterministic order, scoped to the compilation assembly). The `BuilderMatch` carries, per parameter, its binding-time read from `[Quoted]` (Task 0 §6).
- Behavior: a recognized facade call in the carrier is replaced — at the `unquotedReplacements` boundary, keyed at the **invocation node** — by a **fully-qualified static invocation of the paired builder**, where each argument is processed per the builder parameter's binding attribute (Task 0 §6): a **`[Quoted]`** builder parameter receives the **quote** of the call argument (the island), an **unmarked** parameter receives the **live/computed value** verbatim — including a live `ExpressionSyntax` passed through **unquoted** for meta cases. No copy of the builder is injected (no `Synto.*` dependency). This task uses **constant/quoted** arguments only — staging-driven builder args (live `c.Name` inside a loop) arrive once Task 6 lands.

- [ ] **Step 1: Write the failing tests:**

```csharp
// Built-in identifier builder with a CONSTANT name -> member access in output.
[Fact]
public async Task Member_WithConstantName_EmitsMemberAccess()
{
    await VerifyTemplate(
        """
        using Synto.Templating;
        using static Synto.Templating.Template;
        partial class Factory {}
        public class TestClass {
            [Template(typeof(Factory))]
            void Build<[Inline(AsSyntax = true)] T>(T instance) {
                var x = Member<object>(instance, "Name");   // -> instance.Name in OUTPUT (identifier, not "Name")
                System.Console.WriteLine(x);
            }
        }
        """);
    // Expected golden: emitted code is `instance.Name` (MemberAccessExpression), NOT the string literal "Name".
}

// User-authored builder is discovered and called (public extensibility, v1).
[Fact]
public async Task UserSyntaxBuilder_IsDiscoveredAndInvoked()
{
    await VerifyTemplate(
        """
        using Synto.Templating;
        using Microsoft.CodeAnalysis.CSharp.Syntax;
        using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
        partial class Factory {}

        // facade (carrier-callable, inert) + builder (factory-time), paired per Task 0 §5:
        static class MyHoles {
            public static T Cast<T>(object x) => default!;          // facade
        }
        [SyntaxBuilder(typeof(MyHoles))]                            // pairing (illustrative, Task 0 §5)
        static class MyBuilders {
            // both args are output-world islands -> [Quoted] (Task 0 §6)
            public static ExpressionSyntax Cast([Quoted] TypeSyntax t, [Quoted] ExpressionSyntax x) => CastExpression(t, x);
        }

        public class TestClass {
            [Template(typeof(Factory))]
            void Build([Inline(AsSyntax = true)] System.Type t, [Inline(AsSyntax = true)] object x) {
                System.Console.WriteLine(MyHoles.Cast<int>(x));
            }
        }
        """);
    // Expected golden: the factory calls global::MyBuilders.Cast(<quote of t>, <quote of x>); no Synto runtime dep.
}

// META: an UNMARKED ExpressionSyntax builder parameter receives a LIVE ExpressionSyntax passed through
// UNQUOTED (the case the parameter TYPE cannot disambiguate; Task 0 §6).
[Fact]
public async Task UnmarkedExpressionSyntaxParam_PassesLiveSyntaxThroughUnquoted()
{
    await VerifyTemplate(
        """
        using Synto.Templating;
        using Microsoft.CodeAnalysis.CSharp.Syntax;
        using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
        using static Synto.Templating.Template;
        partial class Factory {}

        static class Meta { public static object Splice(ExpressionSyntax node) => default!; } // facade
        [SyntaxBuilder(typeof(Meta))]
        static class MetaBuilders {
            // `node` UNMARKED => the live ExpressionSyntax argument is passed VERBATIM, not re-quoted.
            public static ExpressionSyntax Splice(ExpressionSyntax node) => node;
        }

        public class TestClass {
            [Template(typeof(Factory))]
            void Build() {
                // a live ExpressionSyntax computed at factory time, spliced into the OUTPUT unchanged:
                var node = Parameter<ExpressionSyntax>("node");
                System.Console.WriteLine(Meta.Splice(node));
            }
        }
        """);
    // Expected golden: the factory calls global::MetaBuilders.Splice(node) where `node` is the live factory
    // parameter passed straight through — NO second round of quoting applied to it.
}

// Diagnostic: facade with no paired builder.
[Fact]
public void UnpairedFacade_ReportsDiagnostic()
{
    var diagnostics = RunAndGetDiagnostics(/* facade called but no [SyntaxBuilder] match */
        """
        using Synto.Templating;
        partial class Factory {}
        static class Holes { public static T Foo<T>(object o) => default!; }
        public class TestClass {
            [Template(typeof(Factory))]
            void Build([Inline(AsSyntax = true)] object o) => System.Console.WriteLine(Holes.Foo<int>(o));
        }
        """);
    var diag = Assert.Single(diagnostics, d => d.Id == "SY1015"); // unpaired-builder (Task 0 §8)
    AssertHasRealSpan(diag);
}

// Diagnostic: argument binding mismatch (live value into an ExpressionSyntax slot it cannot satisfy, or vice versa).
[Fact]
public void BuilderArgBindingMismatch_ReportsDiagnostic()
{
    var diagnostics = RunAndGetDiagnostics(/* see Task 0 §6 rule */ "...");
    var diag = Assert.Single(diagnostics, d => d.Id == "SY1016"); // builder-arg-binding-mismatch
    AssertHasRealSpan(diag);
}
```

- [ ] **Step 2: Run to verify they fail** — RED.
- [ ] **Step 3: Implement (by contract)** — author + embed `[SyntaxBuilder]`, the `Member`/`TypeOf` facades and built-in builders (delivery per Task 0 §7); `SyntaxBuilderFinder` discovers builders and pairs to facades (per Task 0 §5); in `CreateSyntaxFactoryMethod`, when a recognized facade invocation is encountered, build the `unquotedReplacements` entry = `global::<Builder>.<Method>(<processed args>)` where each arg is processed by the per-parameter binding rule (quote → run the quoter on that arg subtree with current lifts; live/const → verbatim). Emit the 4 builder diagnostics (`SY1015/1016/1017/1018` — unpaired, arg-mismatch, bad-return-shape, ambiguous).
- [ ] **Step 4: Run tests + gate** — accept goldens; confirm zero `Synto.*` in the generated output (extend the assertion like `ZeroCollisionTest`); prior snapshots unchanged.
- [ ] **Step 5: Commit** — `jj commit -m "feat(templating): user-extensible syntax-builder mechanism + Member/TypeOf built-ins"`

---

### Task 4: Binding-time classifier — dataflow partition (quoted / live-value / live-control)

**Files:**
- Create: `src/Synto.SourceGenerator/Templating/BindingTimeClassifier.cs`
- Test: `test/Synto.Test/Templating/BindingTimeClassifierTest.cs` (unit test the classifier directly via `InternalsVisibleTo Synto.Test`)

**Interfaces:**
- Produces: `BindingTimeClassifier.Classify(SemanticModel, SyntaxNode body, IReadOnlyCollection<LiveRoot> roots) : BindingTimePartition` where the partition exposes, per node: `Quoted` (independent of every live root — default/unchanged), `LiveValue` (an expression whose value depends on a root, consumed in value position), `LiveControl` (a control-flow construct whose **driving expression** — iteration source / condition — is live). Plus `ImpossibleCuts : IEnumerable<(SyntaxNode, reason)>` — a node forced live that transitively depends on a quoted/generated-world value.
- Behavior: liveness propagates from roots by def-use / dataflow (spec §4); control flow whose driver is **quoted** stays quoted (emitted as real output control flow); conservatism is expected (under-approximate through opaque boundaries — `[Live]` is the manual escape hatch). This task is **analysis-only**: it produces the partition but does not yet change emission, so all existing snapshots stay byte-identical.

- [ ] **Step 1: Write the failing tests** — unit tests over parsed bodies asserting classification (no snapshots):

```csharp
[Fact] public void ValueDependingOnRoot_IsLiveValue() { /* var x = root + 1; -> x is LiveValue */ }
[Fact] public void ForeachOverRoot_IsLiveControl() { /* foreach(var c in root) -> the foreach is LiveControl */ }
[Fact] public void IndependentNode_IsQuoted()      { /* throw OutOfRange(i); -> Quoted */ }
[Fact] public void ForeachOverQuotedSource_StaysQuoted() { /* foreach over a non-root -> NOT live control */ }
[Fact] public void LiveDependingOnGeneratedWorld_IsImpossibleCut() { /* [Live] x = <quoted-only value> -> ImpossibleCut */ }
```

(Construct `SemanticModel` via the existing `SimpleTemplateTest` compilation helper; expose `BindingTimeClassifier` to the test via the already-present `InternalsVisibleTo`.)

- [ ] **Step 2: Run to verify they fail** — RED (`BindingTimeClassifier` absent).
- [ ] **Step 3: Implement (by contract)** — the classifier walks the body, seeds liveness from roots, propagates via `DataFlowAnalysis`/symbol def-use, classifies each node, and records impossible cuts. Pure analysis inside the transform; no pipeline capture.
- [ ] **Step 4: Run tests + gate** — classifier units green; **all existing snapshots unchanged** (no emission change yet).
- [ ] **Step 5: Commit** — `jj commit -m "feat(templating): binding-time classifier (quoted/live-value/live-control partition)"`

---

### Task 5: File-local collection helpers (`SyntaxList<TNode>` from fixed nodes + runs)

**Files:**
- Create: `src/Synto/CollectionSyntaxExtensions.cs`
- Modify: `Synto.SourceGenerator.csproj` (embed `Synto.Helper.CollectionSyntaxExtensions.cs`), `FileLocalHelpers.cs` (add `HelperEntry`)
- Test: `test/Synto.Test/Templating/CollectionHelperInjectionTest.cs`

**Interfaces:**
- Produces: a `public static` helper (authored once, emitted `file static` by the scan) that builds a `SyntaxList<TNode>` from a mix of **fixed nodes** and **node runs** (an `IEnumerable<TNode>` produced by an unrolled live region), in slot order — the incorporation logic of spec §5.3/§6. Method name(s) are the scan key in `FileLocalHelpers.Entries` (e.g. `BuildList`).
- **Scope note (explicit):** the `SeparatedSyntaxList<TNode>` helper (separator interleaving) is **not** built this task — the in-scope dog-food reshapes switch→if-chain to stay in non-separated statement lists. Log it in the friction note as a known later-cut item; do not stub it.
- Consumes: the `FindReferencedHelpers` scan (`TemplateFactorySourceGenerator.cs:228-243`) and `FileLocalHelpers.Load` rewrite (`public`→`file`).

- [ ] **Step 1: Write the failing test** — assert that a factory which references the helper gets the `file static` copy injected and compiles against ONLY the injected surface (mirror `InjectedSurfaceCompletenessTest` style):

```csharp
[Fact]
public void CollectionHelper_IsInjected_WhenReferenced()
{
    // Drive a (hand-written, for this task) factory body that calls BuildList(...), run both generators,
    // compile the post-generation output with NO Synto.Core reference, assert zero error diagnostics
    // (a dropped/uninjected helper => CS0103/CS1061 here). Also assert the helper is `file`-scoped.
}
```

- [ ] **Step 2: Run to verify it fails** — RED (helper not registered/injected).
- [ ] **Step 3: Implement (by contract)** — author the helper in `src/Synto`; embed it under `Synto.Helper.CollectionSyntaxExtensions.cs`; add its `HelperEntry(methodName, Load(...))` to `FileLocalHelpers.Entries`. The scan + `public`→`file` rewrite are reused unchanged.
- [ ] **Step 4: Run tests + gate** — green; existing helper injection tests unchanged.
- [ ] **Step 5: Commit** — `jj commit -m "feat(templating): file-local collection helper for SyntaxList runs"`

---

### Task 6: Run + collect — unroll `foreach` over a live root (the core staging path)

**Files:**
- Create: `src/Synto.SourceGenerator/Templating/LiveRegionEmitter.cs`
- Modify: `TemplateFactorySourceGenerator.cs` (key the region replacement at the owning container; transplant carrier usings into the factory file), `Diagnostics.cs` (unsupported-live-shape catch-all)
- Test: `test/Synto.Test/Templating/RunCollectTest.cs` (+ snapshots)

**Interfaces:**
- Produces: `LiveRegionEmitter` that, given a `LiveControl` `foreach` region + the classifier partition + the live roots, **precomputes** a single `ExpressionSyntax` replacement for the region's **owning container** (the enclosing `BlockSyntax` / list owner — a real `SyntaxNode`, per spec §5.3) and registers it in `unquotedReplacements`. The replacement = the **verbatim** loop scaffold (with live-root references rewritten to factory parameter names) wrapping, for each **quoted island** in the loop body, a **collector call** (`BuildList`-style) over the island's quote (produced by a fresh `TemplateSyntaxQuoter` whose map carries the live-value lifts, e.g. `c.Ordinal` → `c.Ordinal.ToSyntax()`). Fixed quoted siblings of the region (e.g. a trailing `throw`) are concatenated at the container.
- Behavior: the quoter is **not** invoked on the container (the map hit returns the precomputed expression and it never descends — `TemplateSyntaxQuoter.Visit` already does this). **Usings transplant (F2):** the carrier compilation-unit's `using` directives needed by the verbatim live scaffold (e.g. `System.Linq` for `.Where`) are merged into the factory file's usings (deduped against `RequiredUsings`), so verbatim live code resolves; collisions (e.g. carrier `using static …Template;`) are excluded. Unsupported live shapes degrade to `SY1014` (unsupported-live-shape), never a mis-expansion.

- [ ] **Step 1: Write the failing test** (the canonical if-chain unroll):

```csharp
[Fact]
public async Task ForeachOverLiveParameter_UnrollsToIfChain()
{
    await VerifyTemplate(
        """
        using Synto.Templating;
        using System.Collections.Generic;
        using static Synto.Templating.Template;
        partial class Factory {}
        public readonly record struct Col(int Ordinal, string Name);
        public class TestClass {
            [Template(typeof(Factory))]
            string GetName(int i) {
                var columns = Parameter<IReadOnlyList<Col>>();
                foreach (var c in columns)            // live control -> unrolls in the factory
                    if (i == c.Ordinal)               // c.Ordinal -> int literal
                        return c.Name;                // c.Name    -> string literal
                throw new System.IndexOutOfRangeException(); // quoted island, verbatim
            }
        }
        """);
    // Expected golden: Factory.GetName(int i, IReadOnlyList<Col> columns) whose RETURNED body is the BLOCK
    // built from BuildList(<foreach-collected run>, <quote of throw>); the foreach runs verbatim in the factory.
}
```

- [ ] **Step 2: Run to verify it fails** — RED.
- [ ] **Step 3: Implement (by contract)** — `LiveRegionEmitter` precomputes the container replacement (scaffold + islands + fixed siblings via the Task 5 helper); wire it into `CreateSyntaxFactoryMethod` after the finders/classifier; implement the usings transplant; emit `SY1014` for shapes the v1 emitter doesn't handle. **Guard:** if implementation tempts a `Visit*` change in the quoter, stop and raise (Global Constraints).
- [ ] **Step 4: Run tests + gate** — accept the golden after confirming the verbatim scaffold + collected run + factory parameter; run the post-generation compile assertion (the unrolled output compiles). Existing snapshots unchanged.
- [ ] **Step 5: Commit** — `jj commit -m "feat(templating): unroll live foreach via run-verbatim islands + container keying"`

---

### Task 7: Extend run + collect to `for`/`while`/`if`, live locals, mutable accumulation

> **Scope is gated by Task 0 §3.** If the owner chose the smaller first cut, implement only the confirmed shapes and mark the rest as a logged later-cut item (do NOT stub).

**Files:**
- Modify: `LiveRegionEmitter.cs`, `BindingTimeClassifier.cs` (if a shape needs extra classification), `TemplateFactorySourceGenerator.cs`
- Test: `test/Synto.Test/Templating/RunCollectShapesTest.cs` (+ snapshots, one per shape)

**Interfaces:**
- Produces: the same container-keyed run+collect for additional live control shapes — `for`/`while` with live conditions, `if` with a live condition (branch specialization), live locals **inside** a region (stay in the verbatim scaffold, not the preamble), and mutable accumulation across iterations. No new public surface.
- Behavior: arbitrary imperative live code works because the scaffold is verbatim (spec §5.2) — the only rewrites remain root→param-name and island→collector (+ the Task 6 usings transplant).

- [ ] **Step 1: Write the failing tests** — one snapshot per confirmed shape:

```csharp
[Fact] public async Task LiveFor_Unrolls()    { /* for (int k=0;k<n;k++) <quoted island> ; n live */ }
[Fact] public async Task LiveWhile_Unrolls()  { /* while(<live cond>) <quoted island> */ }
[Fact] public async Task LiveIf_Specializes() { /* if (<live cond>) <quoted A> else <quoted B> -> one branch */ }
[Fact] public async Task MutableAccumulationAcrossIterations_Works() { /* live accumulator drives N islands */ }
```

- [ ] **Step 2: Run to verify they fail** — RED.
- [ ] **Step 3: Implement (by contract)** — generalize the emitter across the confirmed shapes; keep islands quoted via fresh quoter instances.
- [ ] **Step 4: Run tests + gate** — accept goldens; post-generation compile assertions pass.
- [ ] **Step 5: Commit** — `jj commit -m "feat(templating): live for/while/if + live locals + mutable accumulation"`

---

### Task 8: Impossible-cut and unsupported-live-shape diagnostics (staging error paths)

**Files:**
- Modify: `TemplateFactorySourceGenerator.cs` (report classifier `ImpossibleCuts`; bail cleanly), `Diagnostics.cs`, `AnalyzerReleases.Unshipped.md`
- Test: `test/Synto.Test/Templating/StagingDiagnosticsTest.cs`

**Interfaces:**
- Produces: `SY1013` (impossible cut — a live fragment depending transitively on a quoted/generated-world value) and confirms `SY1014` (unsupported live shape). Both catch-convert-report; never thrown, never mis-expanded.
- Consumes: `BindingTimeClassifier.ImpossibleCuts` (Task 4) and the emitter's shape guard (Task 6).

- [ ] **Step 1: Write the failing tests:**

```csharp
[Fact]
public void ImpossibleCut_ReportsSY1013()
{
    var diagnostics = RunAndGetDiagnostics(
        """
        using Synto.Templating;
        partial class Factory {}
        public class TestClass {
            [Template(typeof(Factory))]
            void Build(int generatedWorld /* quoted */) {
                [Live] var bad = generatedWorld + 1;  // live fragment needs a value that exists only in OUTPUT
                System.Console.WriteLine(bad);
            }
        }
        """);
    var diag = Assert.Single(diagnostics, d => d.Id == "SY1013");
    AssertHasRealSpan(diag);
}

[Fact]
public void UnsupportedLiveShape_ReportsSY1014() { /* a live construct the v1 emitter doesn't handle */ }
```

- [ ] **Step 2: Run to verify they fail** — RED.
- [ ] **Step 3: Implement (by contract)** — report `ImpossibleCuts` as `SY1013` with the offending span and bail (return null after diagnostics, like the converter-error path at `TemplateFactorySourceGenerator.cs:470-473`); ensure the emitter's catch-all surfaces `SY1014`.
- [ ] **Step 4: Run tests + gate** — green; happy-path snapshots unchanged.
- [ ] **Step 5: Commit** — `jj commit -m "feat(templating): impossible-cut + unsupported-live-shape diagnostics"`

---

### Task 9: Dog-food — migrate `ObjectReaderTemplate` onto the live-staged surface (acceptance)

**Files:**
- Modify: `examples/.../ReaderTemplate.cs` (data-driven members become live foreach + `Member`/`TypeOf`; delete placeholder bodies + `CA1822` pragma), `examples/.../ObjectReaderGenerator.cs` (delete `BuildVariableMembers`/`SpecializeMember`/`Parse`; pass `columns` to the factory), `examples/.../Model.cs` (add `Ordinal` to `ColumnInfo` if needed — value-equatable `int`)
- Modify: `examples/.../snapshots/ObjectReaderSnapshotTests.Generates_Specialized_Reader_For_Person#ObjectReader.g.verified.cs` (re-baseline), friction note
- Test: behavioral additions in `examples/.../ObjectReaderBehaviorTests.cs` (cast-less getters, DataTable.Load)

**Interfaces:**
- Produces: no ObjectReader public-surface change. The factory contract widens by the `columns` live parameter (spec walkthrough §4.2). The 4 reshaped members + 12 cast-less getters express as live `foreach` + `Member`/`TypeOf`; `GetValue`/cast-less getters read `_e.Current.<col>` directly (no boxing).
- Consumes: Tasks 1–8. **Acceptance bar:** the behavioral roundtrip stays green; cast-less getters return the member directly; `GetDateTime` on a `Person` (no DateTime column) emits zero arms and throws `InvalidCastException`; `DataTable.Load` still works.

- [ ] **Step 1: Write the failing tests** (extend behavioral suite):

```csharp
[Fact]
public void CastlessGetters_ReadMemberDirectly() // the new capability the feature unlocks
{
    using var r = ObjectReader.Create(Sample(), "Name", "Age");
    Assert.True(r.Read());
    Assert.Equal("Ada", r.GetString(0));   // direct _e.Current.Name, no (string)GetValue boxing
    Assert.Equal(36,    r.GetInt32(1));     // direct _e.Current.Age
    Assert.Throws<System.InvalidCastException>(() => r.GetDateTime(0)); // no DateTime column -> zero arms
}
```

(The existing `Create_IsIntercepted_AndReadsRowsDirectly` and `Create_FeedsDataTableLoad` stay as the regression guard.)

- [ ] **Step 2: Run to verify it fails** — RED (carrier still placeholder-based; cast-less getters delegate to `GetValue`).
- [ ] **Step 3: Implement (by contract)** — rewrite the carrier's data-driven members per the walkthrough §3.1 using `Parameter<EquatableArray<ColumnInfo>>()` (or `[Live]`), live `foreach`, `Member`/`TypeOf`; delete the generator's text-gluing; pass `model.Columns` to `Factory.ObjectReaderTemplate`. Append substantive friction findings.
- [ ] **Step 4: Run tests + gate** — re-baseline the ObjectReader snapshot after confirming the if-chain + cast-less shape (spec walkthrough §5.2) and **zero `Synto.*` / zero reflection** in output; behavioral + DataTable tests green; Synto core snapshots unchanged.
- [ ] **Step 5: Commit** — `jj commit -m "refactor(objectreader): data-driven members via live-staged templates (dog-food)"`

---

### Task 10: Cacheability guard, injected-surface completeness, docs

**Files:**
- Modify: `test/Synto.Test/Templating/InjectedSurfaceCompletenessTest.cs` (add a staged rich template), add a staged incremental-caching test (mirror `SimpleTemplateTest.GeneratorIsIncrementalOnUnrelatedEdit`)
- Modify: `README.md` / spec status, friction note finalize, `docs/superpowers/specs/2026-06-27-live-staged-templates-design.md` (mark delivered)

**Interfaces:** Consumes the whole feature. Produces no new API.

- [ ] **Step 1: Write the guard tests:**

```csharp
[Fact]
public void StagedTemplate_IsIncrementalOnUnrelatedEdit()
{
    // A template using Parameter<T>() + live foreach + Member must keep Transform/Result tracked steps
    // Cached/Unchanged on an unrelated edit (spec §8 — no Compilation/SemanticModel/SyntaxNode captured).
}

[Fact]
public void InjectedSurfaceIsCompleteForStagedTemplate()
{
    // A rich template exercising Parameter<T>() + [Live] + Member/TypeOf + live foreach + the collection
    // helper, compiled against ONLY the injected surface (no Synto.Core) -> zero error diagnostics.
}
```

- [ ] **Step 2: Run to verify they fail** — RED (until guards added).
- [ ] **Step 3: Implement (by contract)** — add the two guards; finalize README/spec/friction. Confirm the bootstrap chain still builds (Bootstrap before `Synto`) and the quoter sources are byte-identical to their pre-feature state.
- [ ] **Step 4: Final whole-repo gate** — `dotnet build -c Debug` (0 errors), `dotnet test --no-build -c Debug` (all green), `dotnet format whitespace --verify-no-changes` (clean). Confirm the only changed snapshots are the new live-staged ones + the re-baselined ObjectReader one; the diff to `CSharpSyntaxQuoter*.cs` / bootstrap is empty.
- [ ] **Step 5: Commit** — `jj commit -m "test(templating): cacheability + injected-surface guards for live-staged templates; docs"`

---

## Self-Review (against the spec)

- **§3.1 `Parameter<T>()` (both positions, identity/dedup/diagnostics)** → Task 1. ✓
- **§3.2 `[Live]` (bound roots + live method parameters)** → Task 2. ✓
- **§3.3 coexist with `[Inline]` (two named surfaces, no unification)** → honored: `[Inline]` untouched; `Parameter`/`[Live]` are new (Tasks 1–2). ✓
- **§4 propagation (live set; quoted/live-value/live-control; lift/collect/impossible-cut)** → Task 4 (classifier) + Tasks 6–7 (emit) + Task 8 (impossible cut). ✓
- **§5.1 lift (value → literal/`ToSyntax`/identifier/type) — absorbs P0** → Task 3 (`Member`/`TypeOf` builders give the carrier surface the spec §3 omitted; raised and resolved with the owner: generic builder mechanism, public v1). ✓
- **§5.2 run-verbatim with island collection** → Task 6 (+ Task 7 shapes). ✓
- **§5.3 boundary node keying (value at node; run at owning container)** → Task 6 (container keying). ✓
- **§5.4 plumbing reuse (find like `InlinedParameterFinder`; trim+add; preamble)** → Tasks 1–2/6. ✓
- **§6 collection helpers (file-local, scan-injected, no runtime dep)** → Task 5 (SyntaxList; SeparatedSyntaxList logged out-of-cut). ✓
- **§7 diagnostics (all 5 + builder family)** → Tasks 1 (naming), 3 (builder), 8 (impossible/unsupported). ✓
- **§8 cacheability (sacred)** → Global Constraints + Task 10 guard. ✓
- **§9 scope (statement-level live sections; type-level roots; out: member-list expansion, `[Inline]` unification)** → Tasks 1–9 honor it; member-list expansion is NOT introduced. ✓
- **§10 open decisions (1 namespace, 2 ns2.0 form, 3 trim) + the new sub-decisions (builder reach/pairing/arg-binding/delivery, F2 usings)** → Task 0 decision gate + per-task checkpoints. ✓
- **Dog-food (the 4 switch members + cast-less getters)** → Task 9 (acceptance). ✓
- **Quoter untouched** → Global Constraints + Task 10 empty-diff confirmation. ✓

**Placeholder scan:** the only deliberately-open items are the Task 0 spellings/numbers and the Task 7 scope trim — both pinned to an explicit owner checkpoint, not left as "TODO". `SY####` numbers are placeholders ONLY until Task 0 assigns them; every test that asserts an Id must be updated to the Task 0 number. The `SeparatedSyntaxList` helper is an explicit logged out-of-cut item, not a stub. No bare "add error handling".

**Type consistency:** `Template.Parameter<T>` / `Template.Member<T>` / `Template.TypeOf`, `LiveAttribute`, `SyntaxBuilderAttribute`, `LiveParameterFinder`/`LiveRootFinder`/`SyntaxBuilderFinder`/`BindingTimeClassifier`/`LiveRegionEmitter`, `BindingTimePartition` (`Quoted`/`LiveValue`/`LiveControl`/`ImpossibleCuts`), `FileLocalHelpers.HelperEntry`/`BuildList`, `SY1010…SY1018` used identically across tasks. ✓

> **Note on `SY####` numbers:** all `SY10xx` Ids in Tasks 1/3/8 are provisional pending Task 0 §8 assignment against the current `SY1009` max — update the test assertions to the assigned numbers when Task 0 closes.

---

## Locked Names (resolved Task 0)

> Resolved with the owner on 2026-06-27. These are the **frozen surface spellings** — Task 1's snapshots pin them. Any change after Task 1 starts is a re-baseline, not a free edit.

### §1 — Markers & facade home (CONFIRMED as plan default)

| Concern | Locked spelling |
|---|---|
| Live parameter carrier | `Synto.Templating.Template.Parameter<T>(string? parameterName = null) : T` — inert `=> default!`, injected `internal` |
| Live capability marker | `Synto.Templating.LiveAttribute` → `[Live]`, `[AttributeUsage(AttributeTargets.Local \| AttributeTargets.Parameter)]`, injected `internal` |
| Built-in facade home | `Member`/`TypeOf` are **hand-authored** static methods on `Synto.Templating.Template` (NOT synthesized — synthesis is the user-extensibility path only), injected `internal`, so `using static Synto.Templating.Template;` exposes `Parameter`/`Member`/`TypeOf` together |
| Built-in facade signatures | `Member<TValue>(object instance, string name) : TValue` · `TypeOf(string name) : System.Type` (both inert `=> default!`) |

### §2 — `Parameter<T>()` carrier form (CONFIRMED)

`netstandard2.0`-safe inert generic method `=> default!`; `var x = Parameter<T>()` infers `x : T`. No alternative carrier form.

### §3 — In-scope control-flow staging (RESOLVED: **full statement set**)

v1 ships the complete statement-level live set: `foreach` **and** `for` **and** `while` (live driver) + `if` branch-specialization (live condition) + **live locals inside a region** + **mutable accumulation across iterations**. All of Task 7's sub-shapes are **in scope**. (`SeparatedSyntaxList` collection helper remains the only logged out-of-cut item, per Task 5.)

### §4 — `[SyntaxBuilder]` spelling (RESOLVED: **method-level**; reach = PUBLIC v1)

`Synto.Templating.SyntaxBuilderAttribute` → `[SyntaxBuilder]`, `[AttributeUsage(AttributeTargets.Method)]`, **no constructor argument** (pairing is by synthesis, not `typeof` — see §5). Injected `internal`. Marks an individual **`public static` method** as a factory-time builder; Synto synthesizes **one facade per marked method**, discovered via `ForAttributeWithMetadataName` on the method attribute (mirrors how `[Template]`/`[Match]` methods are found — cleaner for incremental caching). **Chosen over class-level** (the as-drafted default): `[Runtime]` can be class-level only because it scans for the fixed `ToSyntax` signature, but builder method names are **arbitrary**, so a class-level mark would force *every* `public static` method in the class to be a builder. The containing class may freely hold non-builder helpers.

### §5 — Facade↔builder pairing (RESOLVED: **author-declared synthesis**)

The author writes **only** the `[SyntaxBuilder]` builder method and annotates its parameters; **Synto synthesizes the inert carrier-callable facade** from those annotations and emits it (inert, `internal`) into the consumer compilation so the carrier can call it by the builder's method name. No author-written facade; no `typeof(Facade)` pairing.

**Facade-derivation rule (mechanical, frozen):**

| Builder element | Synthesized facade element |
|---|---|
| `[Quoted]` param (bare) | facade **value** param typed `object` |
| `[Quoted(As = typeof(X))]` param | facade **value** param typed `X` |
| `[Quoted(AsTypeArg = true)]` param | facade **generic type parameter** (PascalCased param name, e.g. `t` → `T`) |
| `[ReturnType]` param (requires `AsTypeArg = true` on the same param) | makes that generic type parameter the facade **return type** |
| unmarked (live) param | facade **value** param, **same type** as the builder param |
| builder returns `ExpressionSyntax`, **no** `[ReturnType]` param | facade returns a **fresh** result type param `TResult` |
| builder returns `ExpressionSyntax`, **one** `[ReturnType]` param | facade returns that param's generic type parameter (e.g. `Cast` → `T`) |
| builder returns `TypeSyntax` | facade returns `System.Type` |
| builder returns any other syntax kind | **not supported in v1** → `SY1017` |

Worked examples this rule must reproduce:
- `ExpressionSyntax Cast([Quoted(AsTypeArg = true), ReturnType] TypeSyntax t, [Quoted] ExpressionSyntax x)` → facade `T Cast<T>(object x)` (author writes `Cast<int>(x)`).
- the **same** builder **without** `[ReturnType]` on `t` → facade `TResult Cast<TResult, T>(object x)` (author writes `Cast<int, int>(x)`) — reuse of the type island as the result is opt-in, never implicit.
- built-in `Member` builder `ExpressionSyntax Member([Quoted] ExpressionSyntax instance, string name)` (no `AsTypeArg`, no `[ReturnType]`) → facade `TResult Member<TResult>(object instance, string name)` (consumer writes `Member<object>(instance, "Name")`; the `TValue` name in earlier drafts == this `TResult`).
- built-in `TypeOf` builder `TypeSyntax TypeOf(string name)` → facade `System.Type TypeOf(string name)`.

> **RESOLVED (owner, 2026-06-27):** the facade result type is **explicitly opted in** via a standalone `[ReturnType]` marker on a `[Quoted(AsTypeArg = true)]` param — there is **no** implicit "sole `AsTypeArg`" rule. Default (no `[ReturnType]`) = a fresh `TResult`. Rationale: no implicit count-and-guess; reusing a type island as the result is author-declared and visible at the builder declaration. `[ReturnType]` on a non-`AsTypeArg` param, or more than one `[ReturnType]` param on a builder, is a facade-synthesis error → `SY1015`. Everything else in the rule is forced by the worked examples.

**Type islands stay value-typed (resolved with owner; `[Inline]` precedent noted).** A type island is expressed as a `[Quoted(AsTypeArg = true)] TypeSyntax` **value parameter** on the builder — NOT as a builder generic type parameter — because a type island may be output-world syntax with **no loadable CLR `System.Type`** at factory time (e.g. `List<{a generated type}>`), so the builder must receive a spliceable `TypeSyntax`. The *facade* it synthesizes to is generic (`Cast<T>`); the genericity is a facade-world (carrier) concept, while the builder operates in syntax-world. **Logged future sugar (out of v1):** allow the type island to be a builder **generic type parameter** instead of a value param — `Cast<[Quoted] T>([Quoted] ExpressionSyntax x)`, with `[ReturnType]` then on that generic param — mirroring `[Inline]`'s `GenericParameter` target. Deferred because it needs a "materialize `TypeSyntax` of `T`" intrinsic in the builder body plus a CLR-type→syntax round-trip that breaks for type islands with **no loadable `System.Type`** (e.g. `List<{a generated type}>`); the v1 value-param form quotes the type argument's syntax directly and avoids both. Note that a builder generic param constrained `where T : TypeSyntax` is explicitly **NOT** this path — that genericity is on the syntax-node axis, not the carrier axis, so a `[ReturnType]` there would name a `TypeSyntax`, not the carrier type.

### §6 — Per-parameter binding (RESOLVED: **`[Quoted]` opt-in, unmarked = live**)

`Synto.Templating.QuotedAttribute` → `[Quoted]`, `[AttributeUsage(AttributeTargets.Parameter)]`, injected `internal`. Properties: `bool AsTypeArg` (default `false`), `System.Type? As` (default `null`). An **unmarked** builder parameter receives the **live/computed value verbatim** (covers `string`/`int`/`T` **and** a live `ExpressionSyntax` passed through **unquoted** for meta-splice). A **`[Quoted]`** parameter receives the **quote** of the call argument (the output-world island). No "explicit on every param" requirement — the dangerous quote op is the explicit one.

Companion marker: `Synto.Templating.ReturnTypeAttribute` → `[ReturnType]`, `[AttributeUsage(AttributeTargets.Parameter)]`, injected `internal`, no properties. Placed on a `[Quoted(AsTypeArg = true)]` param to make that param's synthesized generic type parameter the facade's **return type** (§5). Misuse (`[ReturnType]` without `AsTypeArg`, or more than one per builder) → `SY1015`.

### §7 — Built-in builder delivery (RESOLVED: **injected `internal`**)

The built-in `Member`/`TypeOf` **builders** are embedded + injected `internal` into the consumer compilation (like a marker) and called **fully-qualified** at factory time. Guarantees **zero `Synto.*` dependency** in generated output even when the consumer never references `Synto.Core`.

### §8 — Diagnostic numbers (ASSIGNED: `SY1010`–`SY1018`, after current max `SY1009`)

| Id | Meaning | Introduced in |
|---|---|---|
| `SY1010` | `Parameter<T>()` in inline position with no binding and no explicit name | Task 1 |
| `SY1011` | explicit `parameterName` collision (two different `Parameter` sites declare the same name) | Task 1 |
| `SY1012` | conflicting `(name, T)` — same name, different `T` | Task 1 |
| `SY1013` | impossible cut — a live fragment transitively depends on a quoted/generated-world value | Task 8 |
| `SY1014` | unsupported live shape — a live construct the v1 emitter does not handle | Task 6/8 |
| `SY1015` | **facade-synthesis error** — a `[SyntaxBuilder]` method whose annotations can't synthesize a valid facade (e.g. `AsTypeArg` on a non-`TypeSyntax` param, `[ReturnType]` on a non-`AsTypeArg` param, more than one `[ReturnType]` param, name collision in the synthesized facade) *(replaces the old "unpaired facade"; pairing no longer exists)* | Task 3 |
| `SY1016` | builder argument-binding mismatch — a call argument can't satisfy its builder parameter's binding (e.g. `[Quoted]` arg isn't quotable; `AsTypeArg` island isn't a type) | Task 3 |
| `SY1017` | builder bad return shape — builder returns a syntax kind not supported by the derivation rule | Task 3 |
| `SY1018` | ambiguous builder — two `[SyntaxBuilder]` methods synthesize colliding facades | Task 3 |

### Ripple into Task 3 (consequence of §5 = synthesis — implementer must apply)

Task 3's contract and tests change from the as-drafted "explicit `typeof(Facade)` pairing" to synthesis:
- `SyntaxBuilderFinder` discovers **`[SyntaxBuilder]`-marked methods** (not classes — §4), via `ForAttributeWithMetadataName`, and `FindBuilderFor` keys off the **synthesized facade identity**, not an author-written facade; `[SyntaxBuilder]` carries no `typeof` arg. (The as-drafted Task 3 `FindBuilders(...) : ImmutableArray<INamedTypeSymbol>` over classes becomes a method-symbol discovery.)
- Test `UserSyntaxBuilder_IsDiscoveredAndInvoked`: delete the hand-written `MyHoles` facade and `[SyntaxBuilder(typeof(MyHoles))]`; the builder is `[SyntaxBuilder] static ExpressionSyntax Cast([Quoted(AsTypeArg = true), ReturnType] TypeSyntax t, [Quoted] ExpressionSyntax x)`; the carrier calls the **synthesized** facade `T Cast<T>(object x)` as `Cast<int>(x)`; assert the synthesized facade matches the derivation rule and the factory calls `global::MyBuilders.Cast(<quote t>, <quote x>)`.
- Test `UnpairedFacade_ReportsDiagnostic` → rewrite as `InvalidBuilderAnnotation_ReportsSY1015` (facade-synthesis error); "unpaired" is unreachable under synthesis.
- `Member`/`TypeOf` **facades** stay hand-authored on `Template` (§1) — only **user** builders are synthesized.

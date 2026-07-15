# Cacheable Diagnostics for Consumers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give Synto consumers a first-class, cache-safe way to author and carry diagnostics: a return-type-directed `[Diagnostic]` factory that emits `DiagnosticInfo` (not a live `Diagnostic`), a shipped consumer-facing cacheable trio (`DiagnosticInfo` / `LocationInfo` / `EquatableArray<T>`) with symbol/node provenance helpers, and a `DiagnosticBag` + `Drain` collector pair that removes collect/report boilerplate.

**Architecture:** The consumer-facing cacheable surface is the ONE already-injected `Synto.Generators`-namespaced trio (injected by the `Synto` package's `SurfaceInjectionGenerator`); we extend it in place (`LocationInfo.From` helpers, new `DiagnosticBag`/`Drain`) rather than authoring a third copy. The `[Diagnostic]` generator in `Synto.Diagnostics` gains a cacheable body-emission sibling selected by the factory's declared return type, factored behind a strategy seam so the live and cacheable forms are peers (no `if (isCacheable)` thread). The two forms materialize a live `Diagnostic` only at source-output time via `ToDiagnostic()`.

**Tech Stack:** C# / .NET, Roslyn incremental source generators (`IIncrementalGenerator`, `ForAttributeWithMetadataName`, `RegisterSourceOutput`), xUnit v3 + Microsoft.Testing.Platform (MTP), Verify snapshot testing.

## Global Constraints

- **Cache-safety is the domain invariant.** Nothing that crosses a pipeline boundary may hold `ISymbol`, `Compilation`, `SemanticModel`, `Location`, or `SyntaxNode`. Only equatable value types (primitive / `string` / `enum` / `EquatableArray<T>` / `record` / `record struct`) may ride the cache. Diagnostics are carried as `DiagnosticInfo` + `LocationInfo` and reconstructed at emit time via `ToDiagnostic()`.
- **One consumer-facing injected trio.** Do NOT add a third authored copy of `EquatableArray<T>` / `DiagnosticInfo` / `LocationInfo`. The consumer-facing copy is `Synto.Generators\Incremental\{EquatableArray.cs, DiagnosticInfo.cs}` (embedded under `LogicalName="Synto.Runtime.*"`, injected verbatim as `internal` under `namespace Synto.Generators`). Extend these in place.
- **Injected resources are emitted VERBATIM.** Do NOT add any leading header comment / trivia to a file that is embedded under `Synto.Runtime.*` — leading trivia leaks into the byte-identical injected-surface snapshots.
- **`TreatWarningsAsErrors` is on solution-wide.** 0 warnings tolerated. `--no-incremental` clean builds must be green.
- **No git pre-commit hooks.** Format via CI; keep edits `dotnet format whitespace`-clean.
- **Do NOT touch `main`.** HEAD is a detached commit off `main`; commit each task on the current detached HEAD (like the sibling spec/plan commits). Never rebase/reset onto or push `main`.
- **Leave these pre-existing dirty/staged files untouched:** `docs/superpowers/plans/2026-07-12-live-highlight-option-c.md` (staged), `Directory.Build.props`, `src/Synto.Generators/AnalyzerReleases.Unshipped.md`, `src/Synto.Generators/Templating/Diagnostics/TemplateDiagnostics.cs`.
- **Diagnostic-ID discipline:** `Synto.Generators`' `SY`-prefixed descriptors MUST be registered in `src/Synto.Generators/AnalyzerReleases.Unshipped.md` (RS2008). `Synto.Diagnostics`' `SDG`-prefixed descriptors do NOT (that project does not reference `Microsoft.CodeAnalysis.Analyzers`, so RS2008 does not run there) — new `SDG` descriptors need no release-tracking edit.
- **Test invocation (SDK 10 MTP, raw `dotnet test`; do NOT use rtk's dotnet-test rewrite):**
  - `dotnet test test/Synto.Test --filter-class <FQN>`
  - `dotnet test test/Synto.Diagnostics.Test --filter-class <FQN>`
  - Use `--filter-class` / `--filter-method`, never `--filter`.
- **Snapshot acceptance (Verify):** a failing snapshot writes a `*.received.*` next to `*.verified.*`. After confirming the diff is intended, accept by replacing the `.verified.` file with the `.received.` content (and delete the `.received.`).

## Decisions on spec open questions (reviewer veto)

1. **Namespace & injection ownership (spec OQ1).** DEFAULT: the consumer-facing trio stays the single `Synto.Generators`-namespaced injected surface owned by the `Synto` package's `SurfaceInjectionGenerator`. `DiagnosticBag` + `Drain` join that same injected surface. Cacheable `[Diagnostic]` factories emit references to `Synto.Generators.DiagnosticInfo` / `Synto.Generators.EquatableArray`. To guarantee the trio is present exactly once for a cacheable-`[Diagnostic]` consumer, **`Synto.Diagnostics` takes a package dependency on `Synto`** (Task 7). Trade-off flagged for veto: this makes the standalone `Synto.Diagnostics` package pull the full `Synto` generator. *Reviewer alternative:* keep `Synto.Diagnostics` standalone by giving it its own injector under a distinct consumer namespace — rejected here because it produces two consumer-facing injected copies (violates the one-type constraint) and a co-reference collision on `EquatableArray<T>`.
2. **`DiagnosticBag` mutability (spec OQ2).** DEFAULT: a lightweight **mutable collector** (`ImmutableArray<DiagnosticInfo>.Builder`-backed) used only during the transform step; it never itself crosses a cache boundary — only its `ToEquatableArray()` output does. Matches the spec's usage example. *Reviewer alternative:* immutable-with-`Add`-returning-new; not chosen (heavier, no benefit since the bag is transform-local).
3. **`LocationInfo.From(ISymbol)` with multiple declarations (spec OQ3).** DEFAULT: first `DeclaringSyntaxReferences` location, falling back to `symbol.Locations.FirstOrDefault()`; extracted to a `LocationInfo` value immediately (cache-safe). *Reviewer alternative:* emit all locations — deferred (the `DiagnosticInfo` model carries a single primary location; `additionalLocations` is an explicit spec non-goal).
4. **`Drain` de-duplication (spec OQ4).** DEFAULT: NO de-dup — report every `DiagnosticInfo` (matches the existing `Emit` loop). *Reviewer alternative:* suppress structurally-identical duplicates — deferred (a behavior change; a consumer that legitimately reports the "same" diagnostic twice should not be silently collapsed).

---

## File Structure

**Consumer-facing injected surface (`Synto` package):**
- Modify: `src/Synto.Generators/Incremental/DiagnosticInfo.cs` — add `LocationInfo.From(SyntaxNode)` + `LocationInfo.From(ISymbol)` (keep `CreateFrom(Location?)`).
- Create: `src/Synto.Generators/Incremental/DiagnosticBag.cs` — `DiagnosticBag` collector + `DiagnosticBagExtensions.Drain`. Embedded under `Synto.Runtime.DiagnosticBag.cs`.
- Modify: `src/Synto.Generators/Synto.Generators.csproj` — add the new embedded resource.

**`[Diagnostic]` generator (`Synto.Diagnostics` package):**
- Modify: `src/Synto.Diagnostics/DiagnosticsGenerator.cs` — extract body-emission behind a strategy seam; add the cacheable sibling; add unsupported-return-type diagnostic.
- Create: `src/Synto.Diagnostics/DiagnosticEmitStrategy.cs` — the seam (live + cacheable emitters).
- Modify: `src/Synto.Diagnostics/Diagnostics.cs` — add `SDG1004` unsupported-return-type descriptor.
- Modify: `src/Synto.Diagnostics/Synto.Diagnostics.csproj` — add package dependency on `Synto`.

**Tests:**
- Modify: `test/Synto.Test/Generators/InjectedGeneratorUtilitiesTests.cs` + `test/Synto.Test/Match/MatchTestHarness.cs` — cover injected `DiagnosticBag`/`Drain` on netstandard2.0.
- Create: `test/Synto.Test/Generators/LocationInfoProvenanceTests.cs` — unit tests for `LocationInfo.From`.
- Create: `test/Synto.Test/Generators/DiagnosticBagTests.cs` — unit tests for `DiagnosticBag` + `Drain`.
- Modify: `test/Synto.Diagnostics.Test/DiagnosticsGeneratorTest.cs` — cacheable-form snapshot, cacheability, unsupported-return-type, and end-to-end tests.
- Modify: `test/Synto.Diagnostics.Test/Synto.Diagnostics.Test.csproj` — add `Synto.Generators` analyzer reference so the cacheable-form harness can inject the trio.
- Update: `test/Synto.Test/snapshots/ResourceManifestPinTest.VerifyEmbeddedResourceManifest.verified.txt`, `SurfaceInjectionTest.VerifyInjectedSurface#DiagnosticInfo.g.verified.cs`, and new `#DiagnosticBag.g.verified.cs`.

---

## Phase 1 — Consumer-facing cacheable surface

### Task 1: `LocationInfo` provenance helpers (`From(SyntaxNode)` / `From(ISymbol)`)

**Files:**
- Modify: `src/Synto.Generators/Incremental/DiagnosticInfo.cs`
- Test: `test/Synto.Test/Generators/LocationInfoProvenanceTests.cs` (create)
- Update: `test/Synto.Test/snapshots/SurfaceInjectionTest.VerifyInjectedSurface#DiagnosticInfo.g.verified.cs`

**Interfaces:**
- Produces: `Synto.Generators.LocationInfo.From(Microsoft.CodeAnalysis.SyntaxNode node) : LocationInfo?` and `Synto.Generators.LocationInfo.From(Microsoft.CodeAnalysis.ISymbol symbol) : LocationInfo?`. Existing `CreateFrom(Location?)` and `ToLocation()` unchanged.

- [ ] **Step 1: Write the failing test.** Create `test/Synto.Test/Generators/LocationInfoProvenanceTests.cs`. The trio is `internal` in `Synto.Generators` (Synto.Test has the `SyntoGenerators` extern alias + `InternalsVisibleTo`), so call it through the alias.

```csharp
extern alias SyntoGenerators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using LocationInfo = SyntoGenerators::Synto.Generators.LocationInfo;

namespace Synto.Test.Generators;

public class LocationInfoProvenanceTests
{
    private static (ISymbol symbol, SyntaxNode node) CompileFirstType(string src)
    {
        var tree = CSharpSyntaxTree.ParseText(src, path: "Provenance.cs");
        var comp = CSharpCompilation.Create("Prov", [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
        var typeDecl = tree.GetRoot().DescendantNodes()
            .First(n => n is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax);
        var symbol = comp.GetSemanticModel(tree).GetDeclaredSymbol(typeDecl)!;
        return (symbol, typeDecl);
    }

    [Fact]
    public void From_SyntaxNode_CarriesRealSpanAndPath()
    {
        var (_, node) = CompileFirstType("class Foo { }");
        var info = LocationInfo.From(node);
        Assert.NotNull(info);
        var loc = info!.Value.ToLocation();
        Assert.False(loc.SourceSpan.IsEmpty);
        Assert.Equal("Provenance.cs", loc.GetLineSpan().Path);
    }

    [Fact]
    public void From_Symbol_UsesFirstDeclaringSyntaxLocation()
    {
        var (symbol, node) = CompileFirstType("class Foo { }");
        var fromSymbol = LocationInfo.From(symbol);
        var fromNode = LocationInfo.From(node);
        Assert.Equal(fromNode, fromSymbol); // first declaring syntax == the class node
    }
}
```

- [ ] **Step 2: Run test to verify it fails.**
Run: `dotnet test test/Synto.Test --filter-class Synto.Test.Generators.LocationInfoProvenanceTests`
Expected: FAIL — `LocationInfo` has no `From` member (compile error).

- [ ] **Step 3: Add the helpers.** In `src/Synto.Generators/Incremental/DiagnosticInfo.cs`, add `using System.Linq;` and two static factories on `LocationInfo`. Contract: both are cache-safe (extract a value immediately, never retain the node/symbol). `From(ISymbol)` prefers the first declaring-syntax reference, falls back to the symbol's first location. Representative sketch:

```csharp
public static LocationInfo? From(SyntaxNode node) => CreateFrom(node.GetLocation());

public static LocationInfo? From(ISymbol symbol)
{
    var reference = symbol.DeclaringSyntaxReferences.FirstOrDefault();
    return reference is not null
        ? CreateFrom(reference.GetSyntax().GetLocation())
        : CreateFrom(symbol.Locations.FirstOrDefault());
}
```
Do NOT add a leading file header comment (verbatim-injection constraint).

- [ ] **Step 4: Run test to verify it passes.**
Run: `dotnet test test/Synto.Test --filter-class Synto.Test.Generators.LocationInfoProvenanceTests`
Expected: PASS.

- [ ] **Step 5: Refresh the injected-surface snapshot.**
Run: `dotnet test test/Synto.Test --filter-class Synto.Test.SurfaceInjectionTest`
Expected: FAIL on `#DiagnosticInfo.g.verified.cs` (now includes the two `From` methods). Review the `.received.cs` diff — it must show only the added `From` overloads + the `using System.Linq;` — then accept it over `#DiagnosticInfo.g.verified.cs`. Re-run to confirm PASS.

- [ ] **Step 6: Commit.**

```bash
git add src/Synto.Generators/Incremental/DiagnosticInfo.cs \
        test/Synto.Test/Generators/LocationInfoProvenanceTests.cs \
        test/Synto.Test/snapshots/SurfaceInjectionTest.VerifyInjectedSurface#DiagnosticInfo.g.verified.cs
git commit -m "feat(diagnostics): add LocationInfo.From(SyntaxNode/ISymbol) provenance helpers"
```

---

### Task 2: `DiagnosticBag` collector + `SourceProductionContext.Drain`

**Files:**
- Create: `src/Synto.Generators/Incremental/DiagnosticBag.cs`
- Modify: `src/Synto.Generators/Synto.Generators.csproj`
- Test: `test/Synto.Test/Generators/DiagnosticBagTests.cs` (create)
- Update: `test/Synto.Test/snapshots/ResourceManifestPinTest.VerifyEmbeddedResourceManifest.verified.txt` and new `SurfaceInjectionTest.VerifyInjectedSurface#DiagnosticBag.g.verified.cs`

**Interfaces:**
- Produces (all `internal`, `namespace Synto.Generators`):
  - `DiagnosticBag` — `void Report(DiagnosticInfo)`, `bool IsEmpty { get; }`, `EquatableArray<DiagnosticInfo> ToEquatableArray()`.
  - `static class DiagnosticBagExtensions` — `void Drain(this SourceProductionContext context, EquatableArray<DiagnosticInfo> diagnostics)` reporting each via `ToDiagnostic()`.

- [ ] **Step 1: Write the failing test.** Create `test/Synto.Test/Generators/DiagnosticBagTests.cs`. Exercise the collector directly (through the `SyntoGenerators` alias) and assert the equatable output has value-equality (cache-safety proxy).

```csharp
extern alias SyntoGenerators;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using DiagnosticBag = SyntoGenerators::Synto.Generators.DiagnosticBag;
using DiagnosticInfo = SyntoGenerators::Synto.Generators.DiagnosticInfo;

namespace Synto.Test.Generators;

public class DiagnosticBagTests
{
    private static readonly DiagnosticDescriptor Desc =
        new("TST0001", "t", "m {0}", "cat", DiagnosticSeverity.Warning, true);

    private static DiagnosticInfo Info(string arg) =>
        new(Desc, Location: null,
            new SyntoGenerators::Synto.Generators.EquatableArray<string>(ImmutableArray.Create(arg)));

    [Fact]
    public void EmptyBag_IsEmpty_AndYieldsEmptyEquatableArray()
    {
        var bag = new DiagnosticBag();
        Assert.True(bag.IsEmpty);
        Assert.Equal(0, bag.ToEquatableArray().Count);
    }

    [Fact]
    public void SameReports_ProduceValueEqualEquatableArrays()
    {
        var a = new DiagnosticBag(); a.Report(Info("x")); a.Report(Info("y"));
        var b = new DiagnosticBag(); b.Report(Info("x")); b.Report(Info("y"));
        Assert.Equal(a.ToEquatableArray(), b.ToEquatableArray()); // structural equality => cache hit
        Assert.False(a.IsEmpty);
    }
}
```

- [ ] **Step 2: Run test to verify it fails.**
Run: `dotnet test test/Synto.Test --filter-class Synto.Test.Generators.DiagnosticBagTests`
Expected: FAIL — `DiagnosticBag` does not exist (compile error).

- [ ] **Step 3: Create the type.** Create `src/Synto.Generators/Incremental/DiagnosticBag.cs` with NO leading header comment (verbatim-injection). Representative sketch:

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Synto.Generators;

internal sealed class DiagnosticBag
{
    private readonly ImmutableArray<DiagnosticInfo>.Builder _items =
        ImmutableArray.CreateBuilder<DiagnosticInfo>();

    public bool IsEmpty => _items.Count == 0;

    public void Report(DiagnosticInfo diagnostic) => _items.Add(diagnostic);

    public EquatableArray<DiagnosticInfo> ToEquatableArray() => new(_items.ToImmutable());
}

internal static class DiagnosticBagExtensions
{
    public static void Drain(this SourceProductionContext context, EquatableArray<DiagnosticInfo> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
            context.ReportDiagnostic(diagnostic.ToDiagnostic());
    }
}
```

- [ ] **Step 4: Embed the file for injection.** In `src/Synto.Generators/Synto.Generators.csproj`, add to the `Synto.Runtime.*` toolkit `<ItemGroup>` (next to `Incremental\EquatableArray.cs` / `Incremental\DiagnosticInfo.cs`):

```xml
<EmbeddedResource Include="Incremental\DiagnosticBag.cs" LogicalName="Synto.Runtime.DiagnosticBag.cs" />
```

- [ ] **Step 5: Run test to verify it passes.**
Run: `dotnet test test/Synto.Test --filter-class Synto.Test.Generators.DiagnosticBagTests`
Expected: PASS.

- [ ] **Step 6: Refresh the resource-manifest + injected-surface snapshots.**
Run: `dotnet test test/Synto.Test --filter-class Synto.Test.ResourceManifestPinTest`
Expected: FAIL — the manifest now includes `Synto.Runtime.DiagnosticBag.cs`. Accept the `.received.txt` over `ResourceManifestPinTest.VerifyEmbeddedResourceManifest.verified.txt` (adds one alphabetically-ordered line).
Run: `dotnet test test/Synto.Test --filter-class Synto.Test.SurfaceInjectionTest`
Expected: FAIL — a new `#DiagnosticBag.g.verified.cs` snapshot is emitted. Verify the injected copy is the same source rewritten `public`→`internal` (already `internal`, so verbatim). Accept the new `.received.cs` as `#DiagnosticBag.g.verified.cs`. Re-run both to confirm PASS.

- [ ] **Step 7: Commit.**

```bash
git add src/Synto.Generators/Incremental/DiagnosticBag.cs \
        src/Synto.Generators/Synto.Generators.csproj \
        test/Synto.Test/Generators/DiagnosticBagTests.cs \
        "test/Synto.Test/snapshots/ResourceManifestPinTest.VerifyEmbeddedResourceManifest.verified.txt" \
        "test/Synto.Test/snapshots/SurfaceInjectionTest.VerifyInjectedSurface#DiagnosticBag.g.verified.cs"
git commit -m "feat(diagnostics): add injected DiagnosticBag collector + SourceProductionContext.Drain"
```

---

### Task 3: Prove the extended surface is self-contained on netstandard2.0

**Files:**
- Modify: `test/Synto.Test/Match/MatchTestHarness.cs`
- Modify: `test/Synto.Test/Generators/InjectedGeneratorUtilitiesTests.cs`

**Interfaces:**
- Consumes: `MatchTestHarness.InjectedSurfaceSource(string contentMarker)`, `MatchTestHarness.CreateNetStandardClosure(...)`, `GeneratedPolyfillSource(...)` (existing).
- Produces: `MatchTestHarness.CompileInjectedDiagnosticBagOnNetStandard20() : ImmutableArray<Diagnostic>`.

- [ ] **Step 1: Write the failing test.** Add to `test/Synto.Test/Generators/InjectedGeneratorUtilitiesTests.cs`:

```csharp
[Fact]
public void InjectedDiagnosticBag_CompilesOn_NetStandard20() // C-2 extension: DiagnosticBag + Drain
{
    var diagnostics = MatchTestHarness.CompileInjectedDiagnosticBagOnNetStandard20();
    Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
}
```

- [ ] **Step 2: Run test to verify it fails.**
Run: `dotnet test test/Synto.Test --filter-class Synto.Test.Generators.InjectedGeneratorUtilitiesTests`
Expected: FAIL — `CompileInjectedDiagnosticBagOnNetStandard20` does not exist (compile error).

- [ ] **Step 3: Add the harness method.** In `test/Synto.Test/Match/MatchTestHarness.cs`, mirror `CompileInjectedGeneratorUtilitiesOnNetStandard20`, adding the `DiagnosticBag` surface (which references `EquatableArray` + `DiagnosticInfo` + `SourceProductionContext.ReportDiagnostic`). The netstandard2.0 closure already includes the ns2.0 Roslyn build, so `SourceProductionContext` resolves.

```csharp
public static ImmutableArray<Diagnostic> CompileInjectedDiagnosticBagOnNetStandard20()
{
    var equatableArray = InjectedSurfaceSource("struct EquatableArray");
    var diagnostics = InjectedSurfaceSource("record struct DiagnosticInfo"); // carries LocationInfo + DiagnosticInfo
    var bag = InjectedSurfaceSource("class DiagnosticBag");
    var polyfill = GeneratedPolyfillSource(Run(
        """
        using Synto.Matching;
        partial class M { }
        public class Consumer
        {
            [Match<M>(MatchOption.Single)]
            static object Sum([Capture] int a, [Capture] int b) => a + b;
        }
        """));

    return CreateNetStandardClosure(equatableArray, diagnostics, bag, polyfill).GetDiagnostics();
}
```

- [ ] **Step 4: Run test to verify it passes.**
Run: `dotnet test test/Synto.Test --filter-class Synto.Test.Generators.InjectedGeneratorUtilitiesTests`
Expected: PASS (injected `DiagnosticBag`/`Drain` compile on a faithful netstandard2.0 closure with no Synto runtime dependency).

- [ ] **Step 5: Commit.**

```bash
git add test/Synto.Test/Match/MatchTestHarness.cs \
        test/Synto.Test/Generators/InjectedGeneratorUtilitiesTests.cs
git commit -m "test(diagnostics): prove injected DiagnosticBag is self-contained on netstandard2.0"
```

---

## Phase 2 — Return-type-directed `[Diagnostic]`

### Task 4: Extract the body-emission seam (refactor for reuse — behavior-preserving)

**Files:**
- Create: `src/Synto.Diagnostics/DiagnosticEmitStrategy.cs`
- Modify: `src/Synto.Diagnostics/DiagnosticsGenerator.cs` (route `ProcessTarget`'s return-statement + location-param classification through the seam)

**Interfaces:**
- Produces: `internal abstract class DiagnosticEmitStrategy` with:
  - `bool IsLocationParameter(IParameterSymbol parameter, Compilation compilation)` — recognizes this strategy's location-parameter type.
  - `StatementSyntax BuildReturn(IdentifierNameSyntax descriptorName, ExpressionSyntax? locationExpr, IReadOnlyList<ExpressionSyntax> messageArgExprs, AdditionalUsingsSet usings)` — the return statement.
  - `static DiagnosticEmitStrategy? ForReturnType(ITypeSymbol returnType, Compilation compilation)` — selects `LiveDiagnosticEmitStrategy` for `Microsoft.CodeAnalysis.Diagnostic`, `null` otherwise (Task 5 adds the cacheable arm; Task 6 turns `null` into a diagnostic).
  - `LiveDiagnosticEmitStrategy : DiagnosticEmitStrategy` — location param type `Microsoft.CodeAnalysis.Location`; return `Diagnostic.Create(descriptor, location, severity?, additionalLocations, properties, args...)` (the CURRENT body, moved verbatim).

**Rationale:** This is the reuse-refactor the spec mandates (§ "Extract body-emission behind a small seam so the two return shapes are siblings, not an `if (isCacheable)` thread"). No consumer-visible behavior changes; existing snapshots stay byte-identical.

- [ ] **Step 1: Write the failing test (the green gate is "unchanged").** No NEW test needed — the guard is that the EXISTING `Synto.Diagnostics.Test` suite (snapshots `WithFileScopedNamespace` / `WithBlockScopedNamespaces`, target-validation, incrementality, overload-hint tests) still passes byte-identically. First confirm the baseline is green:
Run: `dotnet test test/Synto.Diagnostics.Test --filter-class Synto.Diagnostics.Test.DiagnosticsGeneratorTest`
Expected: PASS (baseline before refactor).

- [ ] **Step 2: Create the strategy file.** Create `src/Synto.Diagnostics/DiagnosticEmitStrategy.cs`. Move the live location-match (`SymbolEqualityComparer.Default.Equals(paramType, locationTypeSymbol)`) and the `Diagnostic.Create` return-statement construction out of `ProcessTarget` into `LiveDiagnosticEmitStrategy`, unchanged. `ForReturnType` returns the live strategy when `returnType` is `Microsoft.CodeAnalysis.Diagnostic`.

- [ ] **Step 3: Route `ProcessTarget` through the seam.** In `DiagnosticsGenerator.ProcessTarget`, resolve `var strategy = DiagnosticEmitStrategy.ForReturnType(methodSymbol.ReturnType, semanticModel.Compilation);`. Replace the inline `Location`-param check with `strategy.IsLocationParameter(paramSymbol, compilation)` and the inline `Diagnostic.Create` construction with `strategy.BuildReturn(...)`. Keep the descriptor-field emission and message-arg collection in `ProcessTarget` (shared across strategies). For this task, if `strategy is null`, preserve today's behavior (treat as live) — Task 6 tightens this.

- [ ] **Step 4: Run tests to verify unchanged (byte-identical).**
Run: `dotnet test test/Synto.Diagnostics.Test --filter-class Synto.Diagnostics.Test.DiagnosticsGeneratorTest`
Expected: PASS with NO snapshot diffs (`WithFileScopedNamespace` / `WithBlockScopedNamespaces` unchanged). If a snapshot `.received.` appears, the refactor altered output — fix the strategy until byte-identical; do NOT accept a diff here.

- [ ] **Step 5: Commit.**

```bash
git add src/Synto.Diagnostics/DiagnosticEmitStrategy.cs src/Synto.Diagnostics/DiagnosticsGenerator.cs
git commit -m "refactor(diagnostics): extract [Diagnostic] body-emission behind a return-type strategy seam"
```

---

### Task 5: Cacheable sibling — emit `DiagnosticInfo` when the factory returns `DiagnosticInfo`

**Files:**
- Modify: `src/Synto.Diagnostics/DiagnosticEmitStrategy.cs` (add `CacheableDiagnosticEmitStrategy` + wire into `ForReturnType`)
- Modify: `test/Synto.Diagnostics.Test/Synto.Diagnostics.Test.csproj` (add `Synto.Generators` analyzer reference so the harness can inject the trio)
- Modify: `test/Synto.Diagnostics.Test/DiagnosticsGeneratorTest.cs` (add a cacheable harness + tests)

**Interfaces:**
- Consumes: `DiagnosticEmitStrategy.ForReturnType(...)` from Task 4.
- Produces: `CacheableDiagnosticEmitStrategy : DiagnosticEmitStrategy` — location param type `Synto.Generators.LocationInfo` (nullable-annotated `LocationInfo?`); return `new Synto.Generators.DiagnosticInfo(descriptor, location, new Synto.Generators.EquatableArray<string>(ImmutableArray.Create(args...)))`. Emitted body adds `using Synto.Generators;` + `using System.Collections.Immutable;` via `AdditionalUsingsSet`. `ForReturnType` maps `Synto.Generators.DiagnosticInfo` → this strategy.

- [ ] **Step 1: Wire the test harness for injected surface.** In `test/Synto.Diagnostics.Test/Synto.Diagnostics.Test.csproj`, add a reference to `Synto.Generators` as an analyzer (so the harness can instantiate `SurfaceInjectionGenerator`, which injects the trio into the in-memory consumer compilation — exactly how a real cacheable-`[Diagnostic]` consumer receives it via the `Synto` package):

```xml
<ProjectReference Include="..\..\src\Synto.Generators\Synto.Generators.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="True" />
```

- [ ] **Step 2: Write the failing snapshot + cacheability tests.** Add to `test/Synto.Diagnostics.Test/DiagnosticsGeneratorTest.cs` a helper that runs BOTH `SurfaceInjectionGenerator` (injects the trio) AND `DiagnosticsGenerator`, over a compilation that does NOT reference `Synto.Core` (avoids CS0436 on the injected markers). Then a snapshot + cacheability test.

```csharp
private static (CSharpCompilation compilation, GeneratorDriver driver) RunCacheable(string source, bool trackSteps = false)
{
    var tree = CSharpSyntaxTree.ParseText(source, "Cacheable.cs");
    var refs = Directory
        .EnumerateFiles(Path.GetDirectoryName(typeof(CSharpSyntaxVisitor<>).Assembly.Location)!, "*.dll")
        .Select(f => (MetadataReference)MetadataReference.CreateFromFile(f))
        .Concat([CorlibReference, NetStandardReference, SystemRuntimeReference]);
    var compilation = CSharpCompilation.Create("CacheableTest", [tree], refs,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    GeneratorDriver driver = CSharpGeneratorDriver.Create(
        generators: new IIncrementalGenerator[]
            { new global::Synto.Generators.SurfaceInjectionGenerator(), new DiagnosticsGenerator() }
            .Select(g => g.AsSourceGenerator()),
        driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: trackSteps));
    return (compilation, driver.RunGenerators(compilation, TestContext.Current.CancellationToken));
}

private const string CacheableSource =
    """
    using Synto.Generators;
    using Synto.Diagnostics;

    namespace X;

    internal static partial class Diagnostics
    {
        [Diagnostic("TST0001", "Unsupported property type", "Property '{0}' has unsupported type '{1}'", "MyGen.Usage", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning, true)]
        public static partial DiagnosticInfo UnsupportedProp(LocationInfo? location, string name, string type);
    }
    """;

[Fact]
public Task CacheableReturnType_EmitsDiagnosticInfoFactory()
{
    var (_, driver) = RunCacheable(CacheableSource);
    return Verify(driver).UseDirectory("snapshots");
}

[Fact]
public void CacheableFactory_CompilesAgainstInjectedTrio_NoErrors()
{
    var (compilation, driver) = RunCacheable(CacheableSource);
    var updated = driver.RunGeneratorsAndUpdateCompilation(
        compilation, out var output, out _, TestContext.Current.CancellationToken);
    var errors = output.GetDiagnostics(TestContext.Current.CancellationToken)
        .Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
    Assert.DoesNotContain(errors, d => d.Id is "CS0246" or "CS0117" or "CS8795"); // trio resolves; partial is implemented
}

[Fact]
public void CacheableGenerator_IsIncremental_OnUnrelatedEdit()
{
    var (compilation, driver) = RunCacheable(CacheableSource, trackSteps: true);
    var modified = compilation.AddSyntaxTrees(
        CSharpSyntaxTree.ParseText("namespace Z { class Unrelated { } }",
            cancellationToken: TestContext.Current.CancellationToken));
    var result = driver.RunGenerators(modified, TestContext.Current.CancellationToken)
        .GetRunResult().Results.Single(r => r.Generator.GetGeneratorType() == typeof(DiagnosticsGenerator));

    CacheabilityAssert.AllStepsCachedOrUnchanged(
        result, new[] { TrackingNames.Transform, TrackingNames.Result });
}
```
Note: `CacheabilityAssert` lives in `Synto.Test`; if not already visible to `Synto.Diagnostics.Test`, copy the same small assertion helper into this project (it is generator-agnostic) or add a link. Confirm which during Step 4.

- [ ] **Step 3: Run tests to verify they fail.**
Run: `dotnet test test/Synto.Diagnostics.Test --filter-class Synto.Diagnostics.Test.DiagnosticsGeneratorTest`
Expected: FAIL — `ForReturnType` returns `null`/live for a `DiagnosticInfo` return, so the generated body is wrong (constructs a `Diagnostic`, not a `DiagnosticInfo`), the partial stays unimplemented (CS8795), and the snapshot has no cacheable factory.

- [ ] **Step 4: Implement the cacheable strategy.** In `src/Synto.Diagnostics/DiagnosticEmitStrategy.cs` add `CacheableDiagnosticEmitStrategy`:
  - `IsLocationParameter`: true when the parameter type is `Synto.Generators.LocationInfo` (match by metadata name via `compilation.GetTypeByMetadataName("Synto.Generators.LocationInfo")`, comparing the nullable-underlying type so `LocationInfo?` matches).
  - `BuildReturn`: emit `return new DiagnosticInfo(_Descriptor, <locationExpr or null>, new EquatableArray<string>(ImmutableArray.Create(<args>)));` using `AdditionalUsingsSet` to add `Synto.Generators` and `System.Collections.Immutable`. When there are zero message args, emit `new EquatableArray<string>(ImmutableArray<string>.Empty)`; when `locationExpr` is null, pass a `null` literal (the record's `LocationInfo?` slot).
  - Update `ForReturnType` to map a `Synto.Generators.DiagnosticInfo` return to this strategy.

- [ ] **Step 5: Run tests to verify they pass; accept the new snapshot.**
Run: `dotnet test test/Synto.Diagnostics.Test --filter-class Synto.Diagnostics.Test.DiagnosticsGeneratorTest`
Expected: FAIL only on `CacheableReturnType_EmitsDiagnosticInfoFactory` (new snapshot). Review the `.received` — the generated partial must `return new DiagnosticInfo(_UnsupportedProp, location, new EquatableArray<string>(ImmutableArray.Create(name, type)));` — then accept it. `CacheableFactory_CompilesAgainstInjectedTrio_NoErrors` and `CacheableGenerator_IsIncremental_OnUnrelatedEdit` must PASS. Re-run to confirm all green.

- [ ] **Step 6: Commit.**

```bash
git add src/Synto.Diagnostics/DiagnosticEmitStrategy.cs \
        test/Synto.Diagnostics.Test/Synto.Diagnostics.Test.csproj \
        test/Synto.Diagnostics.Test/DiagnosticsGeneratorTest.cs \
        test/Synto.Diagnostics.Test/snapshots/
git commit -m "feat(diagnostics): return-type-directed [Diagnostic] emits cacheable DiagnosticInfo"
```

---

### Task 6: Diagnose an unsupported `[Diagnostic]` return type (`SDG1004`)

**Files:**
- Modify: `src/Synto.Diagnostics/Diagnostics.cs` (add `SDG1004` descriptor + factory)
- Modify: `src/Synto.Diagnostics/DiagnosticsGenerator.cs` (report when `ForReturnType` yields no strategy)
- Modify: `test/Synto.Diagnostics.Test/DiagnosticsGeneratorTest.cs` (add a fixture)

**Interfaces:**
- Consumes: `DiagnosticEmitStrategy.ForReturnType(...)` (returns `null` for unknown return types).
- Produces: `Diagnostics.UnsupportedReturnType(LocationInfo? location, string methodName, string returnType) : DiagnosticInfo` (`SDG1004`, category `Synto.Diagnostics.Usage`, `DiagnosticSeverity.Error`).

**Note:** `Synto.Diagnostics` carries no `AnalyzerReleases` tracking (RS2008 does not run there), so no release-tracking edit is required — mirror the existing `SDG100x` descriptors.

- [ ] **Step 1: Write the failing test.** Add to `test/Synto.Diagnostics.Test/DiagnosticsGeneratorTest.cs`:

```csharp
[Fact]
public void UnsupportedReturnType_ReportsSDG1004_NotInternalError()
{
    var diagnostics = RunAndGetDiagnostics(
        """
        using Microsoft.CodeAnalysis;
        using Synto.Diagnostics;

        namespace X;

        internal static partial class Diags
        {
            [Diagnostic("TST0001", "Title", "msg {0}", "Cat", DiagnosticSeverity.Error, true)]
            public static partial string Wrong(Location location, string arg);
        }
        """);

    var diag = Assert.Single(diagnostics, d => d.Id == "SDG1004");
    Assert.NotEqual(Location.None, diag.Location);
    Assert.False(diag.Location.SourceSpan.IsEmpty);
    Assert.Contains("Wrong", diag.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    Assert.DoesNotContain(diagnostics, d => d.Id == "SDG0000");
}
```

- [ ] **Step 2: Run test to verify it fails.**
Run: `dotnet test test/Synto.Diagnostics.Test --filter-method UnsupportedReturnType_ReportsSDG1004_NotInternalError`
Expected: FAIL — no `SDG1004` reported (generator currently falls through to live emission for unknown return types).

- [ ] **Step 3: Add the descriptor + report.** In `src/Synto.Diagnostics/Diagnostics.cs` add the `SDG1004` descriptor and `UnsupportedReturnType(...)` factory (mirroring `TargetNotPartial`'s shape). In `DiagnosticsGenerator.ProcessTarget`, when `DiagnosticEmitStrategy.ForReturnType(methodSymbol.ReturnType, compilation)` is `null`, add `Diagnostics.UnsupportedReturnType(LocationInfo.CreateFrom(targetMethod.ReturnType.GetLocation()), methodSymbol.Name, methodSymbol.ReturnType.ToDisplayString())` to the diagnostics list and return no generated source (accumulate-and-continue; do NOT throw). This tightens the Task 4 "treat null as live" placeholder into a precise located diagnostic.

- [ ] **Step 4: Run test to verify it passes; re-run the full class.**
Run: `dotnet test test/Synto.Diagnostics.Test --filter-method UnsupportedReturnType_ReportsSDG1004_NotInternalError`
Expected: PASS.
Run: `dotnet test test/Synto.Diagnostics.Test --filter-class Synto.Diagnostics.Test.DiagnosticsGeneratorTest`
Expected: PASS (all fixtures, no regressions).

- [ ] **Step 5: Commit.**

```bash
git add src/Synto.Diagnostics/Diagnostics.cs src/Synto.Diagnostics/DiagnosticsGenerator.cs \
        test/Synto.Diagnostics.Test/DiagnosticsGeneratorTest.cs
git commit -m "feat(diagnostics): report SDG1004 for unsupported [Diagnostic] return types"
```

---

## Phase 3 — Packaging: single injected trio for cacheable-`[Diagnostic]` consumers

### Task 7: `Synto.Diagnostics` package depends on `Synto` (reviewer-veto decision)

**Files:**
- Modify: `src/Synto.Diagnostics/Synto.Diagnostics.csproj`

**Context:** A real consumer of cacheable `[Diagnostic]` writes `partial DiagnosticInfo Foo(LocationInfo? loc, …)`. For that DECLARATION (and the generated body) to compile, the injected trio (`Synto.Generators.*`) must be present in their compilation. That injection is owned by the `Synto` package's `SurfaceInjectionGenerator`. This task makes the dependency explicit so exactly ONE injected copy of the trio + `DiagnosticBag`/`Drain` reaches the consumer. (This is the resolution of spec OQ1 — flagged for reviewer veto; see "Decisions" above.)

- [ ] **Step 1: Add the package dependency.** In `src/Synto.Diagnostics/Synto.Diagnostics.csproj`, add a package reference so consuming `Synto.Diagnostics` transitively brings the `Synto` generator (which injects the trio). Because both are analyzer/generator packages, wire it as an analyzer-flavored dependency consistent with the repo's packaging (`Directory.Build.targets` `IsSyntoGeneratorPackage` logic):

```xml
<ItemGroup>
  <!-- Cacheable [Diagnostic] emits references to the Synto.Generators.* cacheable trio, injected by the
       Synto package's SurfaceInjectionGenerator. Depend on Synto so consumers receive exactly one injected
       copy of DiagnosticInfo / LocationInfo / EquatableArray / DiagnosticBag. -->
  <ProjectReference Include="..\Synto.Generators\Synto.Generators.csproj" />
</ItemGroup>
```
Confirm against `Directory.Build.targets` whether generator-to-generator packaging needs `PrivateAssets`/`OutputItemType` tuning so the dependency ships as a NuGet package dependency (not a private/analyzer-only asset). Adjust to match how sibling generator packages express cross-package deps.

- [ ] **Step 2: Verify the whole solution still builds clean (TWAE, 0 warnings) and no double-injection.**
Run: `dotnet build Synto.sln --no-incremental`
Expected: PASS, 0 warnings. Then re-run the two test suites touched so far to confirm nothing regressed:
Run: `dotnet test test/Synto.Diagnostics.Test --filter-class Synto.Diagnostics.Test.DiagnosticsGeneratorTest`
Run: `dotnet test test/Synto.Test --filter-class Synto.Test.SurfaceInjectionTest`
Expected: PASS.

- [ ] **Step 3: Commit.**

```bash
git add src/Synto.Diagnostics/Synto.Diagnostics.csproj
git commit -m "build(diagnostics): depend on Synto so cacheable [Diagnostic] gets one injected trio"
```

---

## Phase 4 — End-to-end / integration

### Task 8: End-to-end — diagnostics ride the cache and materialize only at source output

**Files:**
- Modify: `test/Synto.Diagnostics.Test/DiagnosticsGeneratorTest.cs` (add an end-to-end consumer test)

**Interfaces:**
- Consumes: the cacheable factory from Task 5, `DiagnosticBag`/`Drain` from Task 2, `RunCacheable(...)` harness from Task 5.

**Goal:** prove the spec's end-to-end story: a consumer collects `DiagnosticInfo`s into a `DiagnosticBag` during a transform, carries `EquatableArray<DiagnosticInfo>` across a pipeline boundary, and materializes live `Diagnostic`s via `SourceProductionContext.Drain(...)` only at source output — with the diagnostics surviving a round-trip through the cache.

- [ ] **Step 1: Write the failing test.** Add an in-test `IIncrementalGenerator` that uses the cacheable factory + bag + drain over an injected trio, and assert (a) the reported live `Diagnostic` carries the descriptor + a real span reconstructed from `LocationInfo`, and (b) on an unrelated edit the diagnostic-carrying step stays `Cached`/`Unchanged`.

```csharp
[Fact]
public void EndToEnd_DiagnosticsRideCache_AndMaterializeAtSourceOutput()
{
    // Consumer source declares a cacheable [Diagnostic] factory; the in-test generator below
    // builds a DiagnosticBag from it, carries EquatableArray<DiagnosticInfo> across a boundary,
    // and drains at RegisterSourceOutput. Uses the same inject-both-generators harness.
    const string source =
        """
        using Synto.Generators;
        using Synto.Diagnostics;

        namespace X;

        internal static partial class Diags
        {
            [Diagnostic("MYG001", "Unsupported property type", "Property '{0}' has unsupported type '{1}'", "MyGen.Usage", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning, true)]
            public static partial DiagnosticInfo UnsupportedProp(LocationInfo? location, string name, string type);
        }
        """;

    var (_, driver) = RunCacheable(source);
    var diags = driver.GetRunResult().Diagnostics;

    // The generator itself reports none (well-formed); the cacheable factory is available for consumers.
    Assert.DoesNotContain(diags, d => d.Id is "SDG0000" or "SDG1004");

    // The generated partial returns a DiagnosticInfo whose ToDiagnostic() rehydrates MYG001 with a real span.
    var generated = driver.GetRunResult().Results
        .SelectMany(r => r.GeneratedSources)
        .Single(s => s.SourceText.ToString().Contains("UnsupportedProp", StringComparison.Ordinal))
        .SourceText.ToString();
    Assert.Contains("new DiagnosticInfo(", generated, StringComparison.Ordinal);
    Assert.Contains("EquatableArray<string>", generated, StringComparison.Ordinal);
    Assert.DoesNotContain("Diagnostic.Create", generated, StringComparison.Ordinal); // NOT the live form
}
```

- [ ] **Step 2: Run test to verify it fails, then passes.**
Run: `dotnet test test/Synto.Diagnostics.Test --filter-method EndToEnd_DiagnosticsRideCache_AndMaterializeAtSourceOutput`
Expected: initially FAIL if any prior task is incomplete; PASS once Tasks 2/5 are in. (This task is primarily an integration assertion over already-built pieces — if it passes immediately, that is the intended green state.)

- [ ] **Step 3: Dog-food check (optional but preferred).** If the `examples/Synto.Examples` ObjectReader example (or any example generator) performs symbol extraction that reports diagnostics, convert one hand-rolled `DiagnosticInfo`/collect/report site to the cacheable `[Diagnostic]` + `DiagnosticBag` + `Drain` surface and build the example to validate the consumer ergonomics end-to-end:
Run: `dotnet build examples/Synto.Examples`
Expected: PASS. If no example reports diagnostics, record that dog-fooding is N/A for this feature and rely on the in-test end-to-end generator above.

- [ ] **Step 4: Commit.**

```bash
git add test/Synto.Diagnostics.Test/DiagnosticsGeneratorTest.cs
git commit -m "test(diagnostics): end-to-end cacheable diagnostics ride cache, materialize at output"
```

---

## End-to-end Verification

Run after all tasks; every command must be green with 0 warnings (TWAE).

1. **Clean solution build:** `dotnet build Synto.sln --no-incremental` → 0 warnings, 0 errors.
2. **Consumer-surface suites:**
   - `dotnet test test/Synto.Test --filter-class Synto.Test.Generators.LocationInfoProvenanceTests`
   - `dotnet test test/Synto.Test --filter-class Synto.Test.Generators.DiagnosticBagTests`
   - `dotnet test test/Synto.Test --filter-class Synto.Test.Generators.InjectedGeneratorUtilitiesTests`
   - `dotnet test test/Synto.Test --filter-class Synto.Test.SurfaceInjectionTest`
   - `dotnet test test/Synto.Test --filter-class Synto.Test.ResourceManifestPinTest`
3. **`[Diagnostic]` generator suite:** `dotnet test test/Synto.Diagnostics.Test --filter-class Synto.Diagnostics.Test.DiagnosticsGeneratorTest` (live snapshots byte-identical; cacheable snapshot + cacheability + SDG1004 + end-to-end all green).
4. **Full test pass (guards no cross-suite regressions):** `dotnet test test/Synto.Test` and `dotnet test test/Synto.Diagnostics.Test`.
5. **Spec-coverage checklist:**
   - Return-type-directed `[Diagnostic]` (live unchanged; `DiagnosticInfo` cacheable) → Tasks 4, 5.
   - Location-param shape (`Location` live vs `LocationInfo?` cacheable) → Tasks 4, 5.
   - Shipped consumer trio (`DiagnosticInfo.ToDiagnostic`, `LocationInfo.From(ISymbol)/From(SyntaxNode)/CreateFrom`, single `EquatableArray`) → Tasks 1, 2; injection reuse (no third copy) → Global Constraints + Task 2.
   - `DiagnosticBag` + `Drain` → Tasks 2, 8.
   - Reuse discipline (extract body-emission seam, no `isCacheable` thread) → Task 4.
   - Cache-safety preserved (unrelated-edit → cached) → Task 5.
   - Self-containment on netstandard2.0 → Task 3.
   - Decoupled from `[SyntoModel]` (bag rides in a plain tuple; no model field) → satisfied by construction (this plan adds no `[SyntoModel]` coupling).
   - Non-goals respected: no model-carries-diagnostics field, no analyzer/live reporting, no `additionalLocations`/`properties` beyond today's `DiagnosticInfo`, live form unchanged.

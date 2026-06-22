# Synto Generator-Author Utilities Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Plan style:** Pins **contracts, interfaces, signatures, and tests** — not prescribed implementation bodies (matching the match-replace / objectreader plans). Full test code is the spec. The cacheability types are injected **from Synto's existing internal files** (no new copy); only `Interceptors` is authored fresh.

**Goal:** Ship two small, injected generator-author utilities — a cacheability toolkit (`EquatableArray<T>` / `LocationInfo` / `DiagnosticInfo`) and an interceptor-attribute helper — under `Synto.Generators`, sourced single-copy, and prove them by deleting the ObjectReader example's hand-rolled plumbing.

**Architecture:** Extend `SurfaceInjectionGenerator` with a **namespace-shift** (`Synto`→`Synto.Generators`) for a designated resource group, then **embed Synto's existing internal `EquatableArray.cs`/`DiagnosticInfo.cs`** into that group — so one physical file is compiled internally *and* injected downstream as `Synto.Generators.*`. The shift keeps the injected name distinct from the IVT-visible internal copy, so `Synto.Test` doesn't collide. `Interceptors` (no internal counterpart) is authored once in `src/Synto/Generators`, `<Compile Remove>`d, and embedded as-is. Then refactor the example onto the new surface.

**Tech Stack:** C# / .NET, Roslyn incremental source generators + `CSharpSyntaxRewriter`, Synto surface-injection, xUnit v3 (MTP v2) + Verify snapshots, jujutsu (jj).

**Spec:** `docs/superpowers/specs/2026-06-22-generator-utilities-design.md` (contracts C-1…C-6; mechanism §5).

## Global Constraints

- **One source, no mirror (C-1/D1).** The cacheability types are injected **from their existing internal files** (`src/Synto.SourceGenerator/EquatableArray.cs`, `DiagnosticInfo.cs`) via embed + namespace-shift — those files stay compiled for Synto's own pipeline **and** are added as `<EmbeddedResource>` (a file may be both Compile and EmbeddedResource). **Do NOT** author a second copy and **do NOT** relocate/modify the internal files. `Interceptors` is the one new authored file (`src/Synto/Generators/Interceptors.cs`), `<Compile Remove>`d from `Synto.Core` + embedded.
- **Namespace-shift is selective (C-4).** The new `SurfaceInjectionGenerator` rewrite retargets `namespace Synto`→`namespace Synto.Generators` **only** for the designated shift-group resources (convention: a distinct LogicalName prefix). Marker resources keep their authored namespaces — their injected goldens must **not** change.
- **Namespace + visibility.** Injected cacheability types land in **`Synto.Generators`**, `internal` (the existing `public`→`internal` rewrite is a no-op on the already-`internal` source; the shift does the namespace). `Interceptors` is authored `public` in `Synto.Generators` and rewritten to `internal` on injection.
- **Self-contained on `netstandard2.0` (C-2).** Compiles in a consumer's ns2.0 generator with only BCL + Roslyn. `record struct` `init` needs `IsExternalInit`, already emitted unconditionally by `MatchFactorySourceGenerator` at post-init — **no new polyfill**. Proven by a self-containment test mirroring `MatchTestHarness.CompileInjected*OnNetStandard20`.
- **No output-shape change (C-4).** No consumer *generated output* changes; the **example's snapshots stay byte-identical**; no existing marker golden changes.
- **`Synto.Test` stays green — the collision check.** The namespace-shift keeps the injected copy in `Synto.Generators`, distinct from the IVT-visible internal `Synto.EquatableArray`; `PipelineEquatabilityTests` (bridges `EquatableArray` with internal `TemplateGenerationResult`/`MatchGenerationResult`) must build + pass unchanged.
- **`Synto.Diagnostics` untouched (C-5).**
- **Green gate (per task):** `dotnet build -c Debug` (0 errors) · `dotnet test --no-build -c Debug` (all green; **never** `--nologo`) · `dotnet format whitespace` then `dotnet format whitespace --verify-no-changes`. Root `.editorconfig` pins `end_of_line=lf`.
- **Commits:** Conventional Commits, **no AI/Claude footer**, one per task. **jj**: `jj commit -m "…"`. Branch **`experimental/object-reader`**; never main; no `jj git push`; do not move the bookmark.
- **Locked names:** namespace `Synto.Generators`; injected `EquatableArray<T>` / `LocationInfo` / `DiagnosticInfo` (from the internal files, shifted); `Interceptors` with `public const string AttributeHintName = "InterceptsLocationAttribute.g.cs"`, `public const string AttributeSource`, `public static void AddDefinition(SourceProductionContext)` + `AddDefinition(IncrementalGeneratorPostInitializationContext)`; example carrier rename `DiagnosticInfo` → `PendingDiagnostic`.

## File Structure

**Create:**
- `src/Synto/Generators/Interceptors.cs` — `public static class Interceptors` (new; the only new authored type).
- `test/Synto.Test/Generators/InjectedGeneratorUtilitiesTests.cs` — self-containment + namespace-shift tests.

**Modify:**
- `src/Synto.SourceGenerator/SurfaceInjectionGenerator.cs` — add the selective `namespace Synto`→`Synto.Generators` rewrite for the shift group.
- `src/Synto.SourceGenerator/Synto.SourceGenerator.csproj` — embed `EquatableArray.cs` + `DiagnosticInfo.cs` (shift group) and `..\Synto\Generators\Interceptors.cs` (plain group).
- `src/Synto/Synto.csproj` — `<Compile Remove="Generators\Interceptors.cs" />`.
- `test/Synto.Test/Match/MatchTestHarness.cs` — add `CompileInjectedGeneratorUtilitiesOnNetStandard20()`.
- `test/Synto.Test/snapshots/` — new injected goldens (review + accept).
- `examples/.../Generator/Model.cs`, `examples/.../Generator/ObjectReaderGenerator.cs` (Task 3).

**Do NOT modify:** `src/Synto.SourceGenerator/EquatableArray.cs`, `src/Synto.SourceGenerator/DiagnosticInfo.cs` (embedded *as-is*; their content is the single source).

**Reuse:** `MatchFactorySourceGenerator`'s `IsExternalInit` polyfill, `MatchTestHarness.{CreateNetStandardClosure,InjectedSurfaceSource,GeneratedPolyfillSource,Run}`, the existing `SurfaceInjectionTest` (auto-covers new resources).

---

### Task 1: Namespace-shifting injection of the cacheability toolkit

**Files:**
- Modify: `src/Synto.SourceGenerator/SurfaceInjectionGenerator.cs`, `src/Synto.SourceGenerator/Synto.SourceGenerator.csproj`, `test/Synto.Test/Match/MatchTestHarness.cs`
- Test: `test/Synto.Test/Generators/InjectedGeneratorUtilitiesTests.cs`; new injected goldens `…#EquatableArray.g.verified.cs`, `…#DiagnosticInfo.g.verified.cs`

**Interfaces:**
- Produces (injected `internal`, derived from the internal files via embed + shift):
  ```csharp
  namespace Synto.Generators;  // ← rewritten from `namespace Synto` on injection
  internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T> where T : IEquatable<T> { /* Synto's existing copy */ }
  internal record struct LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan) { public Location ToLocation(); public static LocationInfo? CreateFrom(Location?); }
  internal record struct DiagnosticInfo(DiagnosticDescriptor Descriptor, LocationInfo? Location, EquatableArray<string> MessageArgs) { public Diagnostic ToDiagnostic(); }
  ```
- `SurfaceInjectionGenerator` gains: a selective `namespace Synto`→`namespace Synto.Generators` rewrite for the designated shift-group resources (in addition to `public`→`internal`).
- Contract: **C-1**, **C-2**, **C-3**, **C-4**, the collision check.

- [ ] **Step 1: Write the failing test**

`test/Synto.Test/Generators/InjectedGeneratorUtilitiesTests.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Xunit;

namespace Synto.Test.Generators;

public class InjectedGeneratorUtilitiesTests
{
    [Fact]
    public void InjectedCacheabilityTypes_LandInSyntoGeneratorsNamespace() // C-1 shift fired, scoped
    {
        var equatableArray = MatchTestHarness.InjectedSurfaceSource("struct EquatableArray");
        Assert.Contains("namespace Synto.Generators", equatableArray);

        var diagnostics = MatchTestHarness.InjectedSurfaceSource("record struct DiagnosticInfo");
        Assert.Contains("namespace Synto.Generators", diagnostics);
    }

    [Fact]
    public void InjectedCacheabilityToolkit_CompilesOn_NetStandard20() // C-2
    {
        var diagnostics = MatchTestHarness.CompileInjectedGeneratorUtilitiesOnNetStandard20();
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }
}
```

Add to `test/Synto.Test/Match/MatchTestHarness.cs` (mirror `CompileInjectedMatchReplaceSurfaceOnNetStandard20`):

```csharp
/// <summary>
/// C-2 self-containment proof for the injected generator-author utilities: compiles the injected (and
/// namespace-shifted) EquatableArray + LocationInfo + DiagnosticInfo on the FAITHFUL netstandard2.0
/// closure, alongside the IsExternalInit polyfill the record structs need. Returns the resulting
/// diagnostics (must be error-free). Task 2 extends this to also include Interceptors.
/// </summary>
public static ImmutableArray<Diagnostic> CompileInjectedGeneratorUtilitiesOnNetStandard20()
{
    var equatableArray = InjectedSurfaceSource("struct EquatableArray");
    var diagnostics = InjectedSurfaceSource("record struct DiagnosticInfo"); // DiagnosticInfo.cs carries LocationInfo + DiagnosticInfo
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

    return CreateNetStandardClosure(equatableArray, diagnostics, polyfill).GetDiagnostics();
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet build -c Debug`.
Expected: FAIL — `CompileInjectedGeneratorUtilitiesOnNetStandard20`/`InjectedSurfaceSource("struct EquatableArray")` find no injected `EquatableArray`/`DiagnosticInfo` resource yet (none embedded; no shift group). RED.

- [ ] **Step 3: Implement (by contract)**

1. `SurfaceInjectionGenerator.cs` — in `BuildSurface`, recognize a **shift-group** prefix (e.g. resources named `Synto.RuntimeGenerated.*` — implementer's choice) and, for those, additionally rewrite the file's namespace `Synto`→`Synto.Generators` before emit. Implement as a `CSharpSyntaxRewriter` over `FileScopedNamespaceDeclarationSyntax`/`NamespaceDeclarationSyntax` (replace a leading `Synto` qualifier with `Synto.Generators`), composed with the existing `PublicToInternalRewriter`. The plain `Synto.Runtime.*` path is unchanged (markers untouched). Derive the hint name from the prefix as today.
2. `src/Synto.SourceGenerator/Synto.SourceGenerator.csproj` — embed the two **existing** internal files into the shift group, leaving them compiled (a file can be both `Compile` and `EmbeddedResource`):
   ```xml
   <EmbeddedResource Include="EquatableArray.cs" LogicalName="Synto.RuntimeGenerated.EquatableArray.cs" />
   <EmbeddedResource Include="DiagnosticInfo.cs" LogicalName="Synto.RuntimeGenerated.DiagnosticInfo.cs" />
   ```
   Do **not** add `<Compile Remove>` for these — they must stay compiled for Synto's own pipeline.

- [ ] **Step 4: Run the tests + gate**

Run the green gate. Review + accept the new injected goldens (`…#EquatableArray.g.verified.cs`, `#DiagnosticInfo.g.verified.cs`) after confirming each shows `namespace Synto.Generators` + `internal`. **Confirm `PipelineEquatabilityTests` still passes** (collision check) and **no marker golden changed** (the shift is selective). Expected: both new facts PASS; goldens green; whole suite green; format clean.

- [ ] **Step 5: Commit**

```bash
jj commit -m "feat(gen-utils): inject EquatableArray + DiagnosticInfo via namespace-shifting SurfaceInjection"
```

---

### Task 2: Inject the interceptor-attribute helper (`Interceptors`)

**Files:**
- Create: `src/Synto/Generators/Interceptors.cs`
- Modify: `src/Synto/Synto.csproj`, `src/Synto.SourceGenerator/Synto.SourceGenerator.csproj`, `test/Synto.Test/Match/MatchTestHarness.cs`
- Test: `test/Synto.Test/Generators/InjectedGeneratorUtilitiesTests.cs`; new golden `…#Interceptors.g.verified.cs`

**Interfaces:**
- Produces (authored `public` in `Synto.Generators`, injected `internal`):
  ```csharp
  namespace Synto.Generators;
  public static class Interceptors
  {
      public const string AttributeHintName = "InterceptsLocationAttribute.g.cs";
      public const string AttributeSource;   // canonical BCL-only InterceptsLocationAttribute(int version, string data)
      public static void AddDefinition(SourceProductionContext context);
      public static void AddDefinition(IncrementalGeneratorPostInitializationContext context);
  }
  ```
- Contract: **C-2**; **`AttributeSource` byte-identical** to the example's current `InterceptsLocationAttributeSource` const so the example's `InterceptsLocationAttribute.g.cs` snapshot stays unchanged (Task 3).

- [ ] **Step 1: Write the failing test**

Extend `InjectedGeneratorUtilitiesTests.cs`:

```csharp
[Fact]
public void InjectedInterceptorsHelper_DefinesTheCanonicalAttribute() // C-4
{
    var source = MatchTestHarness.InjectedSurfaceSource("class Interceptors");
    Assert.Contains("namespace System.Runtime.CompilerServices", source);
    Assert.Contains("class InterceptsLocationAttribute", source);
    Assert.Contains("InterceptsLocationAttribute(int version, string data)", source);
}
```

Extend `CompileInjectedGeneratorUtilitiesOnNetStandard20()` to also compile the helper:

```csharp
    var interceptors = InjectedSurfaceSource("class Interceptors");
    return CreateNetStandardClosure(equatableArray, diagnostics, interceptors, polyfill).GetDiagnostics();
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --no-build -c Debug` (build first).
Expected: FAIL — no `Synto.Runtime.Interceptors` resource yet. RED.

- [ ] **Step 3: Implement (by contract)**

1. `src/Synto/Generators/Interceptors.cs`:

```csharp
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Synto.Generators;

/// <summary>Emits the canonical <c>System.Runtime.CompilerServices.InterceptsLocationAttribute</c>
/// definition (which the SDK does not ship) that a version-stamped <c>[InterceptsLocation]</c> binds to.
/// The interceptor method body itself stays the author's concern.</summary>
public static class Interceptors
{
    public const string AttributeHintName = "InterceptsLocationAttribute.g.cs";

    // MUST stay byte-identical to the ObjectReader example's previous const (Task 3 snapshot is unchanged).
    public const string AttributeSource =
        """
        // <auto-generated/>
        namespace System.Runtime.CompilerServices
        {
            [global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = true)]
            internal sealed class InterceptsLocationAttribute : global::System.Attribute
            {
                public InterceptsLocationAttribute(int version, string data)
                {
                    _ = version;
                    _ = data;
                }
            }
        }
        """;

    public static void AddDefinition(SourceProductionContext context) =>
        context.AddSource(AttributeHintName, SourceText.From(AttributeSource, Encoding.UTF8));

    public static void AddDefinition(IncrementalGeneratorPostInitializationContext context) =>
        context.AddSource(AttributeHintName, SourceText.From(AttributeSource, Encoding.UTF8));
}
```

2. `src/Synto/Synto.csproj` — add `<Compile Remove="Generators\Interceptors.cs" />` (embed-only).
3. `src/Synto.SourceGenerator/Synto.SourceGenerator.csproj` — `<EmbeddedResource Include="..\Synto\Generators\Interceptors.cs" LogicalName="Synto.Runtime.Interceptors.cs" />` (plain group — already in `Synto.Generators`, no shift).

- [ ] **Step 4: Run the tests + gate**

Run the green gate. Review + accept the new `…#Interceptors.g.verified.cs` golden (`public`→`internal`-rewritten helper, namespace `Synto.Generators` as authored). Expected: both new facts PASS; Task 1 facts still green; whole suite green; format clean.

- [ ] **Step 5: Commit**

```bash
jj commit -m "feat(gen-utils): inject Interceptors InterceptsLocationAttribute-definition helper"
```

---

### Task 3: Close the loop — refactor the ObjectReader example onto the toolkit

**Files:**
- Modify: `examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Generator/Model.cs`, `examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Generator/ObjectReaderGenerator.cs`

**Interfaces:** Consumes Tasks 1–2 (`Synto.Generators.{EquatableArray,LocationInfo,DiagnosticInfo,Interceptors}`). Produces no new API. Contract: **C-6** — example builds green, all its tests pass, **both example snapshots byte-identical**.

- [ ] **Step 1: Establish the guard (existing tests are the spec)**

Run: `dotnet test --no-build -c Debug` (after a build).
Expected: all example tests PASS (baseline) before refactoring.

- [ ] **Step 2: Refactor (by contract)**

1. `Model.cs`:
   - Add `using Synto.Generators;`.
   - **Delete** the hand-rolled `internal readonly struct EquatableArray<T>` (~60 lines) and `internal readonly record struct LocationInfo` — now injected by `Synto.Generators`.
   - **Rename** the carrier `DiagnosticInfo(DiagnosticKind Kind, …)` → `PendingDiagnostic` (avoids clashing with the injected `Synto.Generators.DiagnosticInfo`); comment: `// stopgap until Synto.Diagnostics supports the cacheable path (its own future spec)`.
   - Update `ObjectReaderModel`'s `EquatableArray<DiagnosticInfo> Diagnostics` → `EquatableArray<PendingDiagnostic>`. Keep `ColumnInfo`, `DiagnosticKind`, `ObjectReaderModel`.
   - The injected `EquatableArray<T>` ctor takes `ImmutableArray<T>` (Synto's shape), not `T[]` — change call sites from `new EquatableArray<…>(x.ToArray())` to `new EquatableArray<…>(x.ToImmutableArray())`.
2. `ObjectReaderGenerator.cs`:
   - Rename all `DiagnosticInfo` references to `PendingDiagnostic`.
   - **Delete** the `private const string InterceptsLocationAttributeSource = …` (~16 lines).
   - Replace `spc.AddSource("InterceptsLocationAttribute.g.cs", SourceText.From(InterceptsLocationAttributeSource, …))` with `global::Synto.Generators.Interceptors.AddDefinition(spc);`.

- [ ] **Step 3: Run the example tests + gate**

Run the green gate. **Both example goldens (`…#ObjectReader.g.verified.cs`, `…#InterceptsLocationAttribute.g.verified.cs`) MUST be unchanged** — if `#InterceptsLocationAttribute` changed, `Interceptors.AttributeSource` drifted (Task 2 contract); fix the source to match, do **not** re-accept. Expected: all example tests PASS; both snapshots unchanged; whole suite green; format clean. `Model.cs` + `ObjectReaderGenerator.cs` are ≈90 lines lighter.

- [ ] **Step 4: Commit**

```bash
jj commit -m "refactor(objectreader): consume Synto.Generators toolkit; drop hand-rolled plumbing"
```

---

## Self-Review (against the spec)

- **§3 D1 one source via embed + namespace-shift; internals untouched; Interceptors authored fresh** → Task 1 (SurfaceInjectionGenerator shift + embed existing files) + Task 2 (Interceptors). ✓
- **§5 mechanism (selective shift; cacheability from internal files; Interceptors plain)** → Task 1 + Task 2 wiring. ✓
- **§3 D3 interceptor helper = attribute definition only** → Task 2. ✓
- **§3 D4 / C-5 Synto.Diagnostics untouched** → no task modifies it; example keeps `DiagnosticKind`→factory (renamed carrier). ✓
- **§3 D5 / C-6 prove by deletion; example snapshots byte-identical** → Task 3. ✓
- **§3 D6 internal copies untouched, no mirror** → Task 1 embeds-as-is, no `<Compile Remove>` on the internal files, no relocation. ✓
- **C-1 single source** → Task 1 (existing files) + Task 2 (one authored file). ✓
- **C-2 self-contained on ns2.0 (IsExternalInit already unconditional)** → Task 1 (`CompileInjectedGeneratorUtilitiesOnNetStandard20`), extended Task 2. ✓
- **C-3 collision-safe; Synto.Test green (PipelineEquatabilityTests); shift selective (markers unchanged)** → Task 1 Step 4 gate (explicit). ✓
- **C-4 no output-shape change; example snapshots byte-identical** → Task 2 (`AttributeSource` byte-identical) + Task 3 Step 3. ✓
- **§9 testing (namespace-shift assert, self-containment, injected goldens, core suite green, example green + unchanged snapshot)** → Tasks 1–3. ✓
- **§8 non-goals (no Synto.Diagnostics change, no interceptor body gen, no internal de-dup, no public Synto.Core API)** → honored: internals untouched; `Interceptors` Compile-Removed; helper is attribute-only. ✓

**Placeholder scan:** none — the shift-group prefix is explicitly an implementer choice with a concrete example; the internal files to embed are named exactly; test code is complete. ✓

**Type consistency:** `Synto.Generators.{EquatableArray<T>, LocationInfo, DiagnosticInfo, Interceptors}`, `AttributeHintName`/`AttributeSource`/`AddDefinition`, `CompileInjectedGeneratorUtilitiesOnNetStandard20`, and the example rename `DiagnosticInfo`→`PendingDiagnostic` are used identically across tasks. ✓

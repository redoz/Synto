# Synto Generator-Author Utilities Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Plan style:** Pins **contracts, interfaces, signatures, and tests** — not prescribed implementation bodies (matching the match-replace / objectreader plans). Full test code is the spec; the injectable types **mirror Synto's existing internal shapes** (cited inline), so "implement" means "author the public mirror + wire injection".

**Goal:** Ship two small, additive, injected generator-author utilities — a cacheability toolkit (`EquatableArray<T>` / `LocationInfo` / `DiagnosticInfo`) and an interceptor-attribute helper — under `Synto.Generators`, and prove them by deleting the ObjectReader example's hand-rolled copies.

**Architecture:** Author the injectable types once in `src/Synto/Generators/` (namespace `Synto.Generators`), `<Compile Remove>` them from `Synto.Core`, and embed them as `Synto.Runtime.*` resources so the existing `SurfaceInjectionGenerator` injects them `internal` into every consumer (rewriting `public`→`internal`). Synto's own internal `EquatableArray`/`DiagnosticInfo` copies are **left untouched** (relocating them collides in `Synto.Test`). Then refactor the example onto the new surface.

**Tech Stack:** C# / .NET, Roslyn incremental source generators, Synto surface-injection, xUnit v3 (MTP v2) + Verify snapshots, jujutsu (jj).

**Spec:** `docs/superpowers/specs/2026-06-22-generator-utilities-design.md` (contracts C-1…C-6).

## Global Constraints

- **Single-sourced injectable surface (C-1/D1).** Author each injectable type **once** in `src/Synto/Generators/`, `<Compile Remove>` it from `src/Synto/Synto.csproj` (embed-only, like `MatchReplaceExtensions.cs`), and embed it as a `Synto.Runtime.*` resource in `src/Synto.SourceGenerator/Synto.SourceGenerator.csproj`. **Do NOT** touch `Synto.SourceGenerator`'s internal `EquatableArray.cs` / `DiagnosticInfo.cs` (no relocation — it collides in `Synto.Test`).
- **Namespace + visibility.** All new types live in **`namespace Synto.Generators`**, authored **`public`**; `SurfaceInjectionGenerator`'s `PublicToInternalRewriter` flips the top-level type to `internal` on injection (members keep their authored accessibility).
- **Mirror the canonical shapes.** The injectable `EquatableArray<T>` / `LocationInfo` / `DiagnosticInfo` mirror `src/Synto.SourceGenerator/EquatableArray.cs` and `DiagnosticInfo.cs` exactly (same members/equality), changed only in namespace + `public`.
- **Self-contained on `netstandard2.0` (C-2).** Compiles in a consumer's ns2.0 generator with only BCL + the Roslyn surface. `record struct` `init` needs `IsExternalInit`, already emitted unconditionally by `MatchFactorySourceGenerator` at post-init (hint `Synto.Matching.IsExternalInit.g.cs`) — **no new polyfill**. Proven by a self-containment test mirroring `MatchTestHarness.CompileInjected*OnNetStandard20`.
- **No semantic / output-shape change (C-4).** No templating/matching behavior changes; no consumer *generated output* changes. New injected-surface goldens are added; no existing golden's meaning changes; the **example's snapshots stay byte-identical**.
- **`Synto.Test` stays green — the collision check.** The injectable copy (namespace `Synto.Generators`) must not collide with the internal copy (namespace `Synto`); `PipelineEquatabilityTests` (bridges `EquatableArray` with internal `TemplateGenerationResult`/`MatchGenerationResult`) must build + pass unchanged.
- **`Synto.Diagnostics` untouched (C-5).** No change to the `[Diagnostic]` generator or its output.
- **Green gate (per task):** `dotnet build -c Debug` (0 errors; analyzer warnings are findings, not gate failures) · `dotnet test --no-build -c Debug` (all green; **never** `--nologo`) · `dotnet format whitespace` then `dotnet format whitespace --verify-no-changes` (whitespace scope only). Root `.editorconfig` pins `end_of_line=lf`.
- **Commits:** Conventional Commits, **no AI/Claude footer**, one per task. VCS is **jj**: `jj commit -m "…"`. Branch **`experimental/object-reader`** (current tip); never main; no `jj git push`; do not move the bookmark.
- **Locked names:** namespace `Synto.Generators`; `EquatableArray<T>`, `LocationInfo`, `DiagnosticInfo` (mirrors); `Interceptors` with `public const string AttributeHintName = "InterceptsLocationAttribute.g.cs"`, `public const string AttributeSource`, `public static void AddDefinition(SourceProductionContext)` + `AddDefinition(IncrementalGeneratorPostInitializationContext)`; example carrier rename `DiagnosticInfo` → `PendingDiagnostic`.

## File Structure

**Create:**
- `src/Synto/Generators/EquatableArray.cs` — `public readonly struct EquatableArray<T>` (mirror of the internal copy).
- `src/Synto/Generators/Diagnostics.cs` — `public record struct LocationInfo` + `public record struct DiagnosticInfo` (mirror).
- `src/Synto/Generators/Interceptors.cs` — `public static class Interceptors` (new).
- `test/Synto.Test/Generators/InjectedGeneratorUtilitiesTests.cs` — self-containment tests.

**Modify:**
- `src/Synto/Synto.csproj` — `<Compile Remove>` the three `Generators\*.cs`.
- `src/Synto.SourceGenerator/Synto.SourceGenerator.csproj` — three `<EmbeddedResource>` entries.
- `test/Synto.Test/Match/MatchTestHarness.cs` — add `CompileInjectedGeneratorUtilitiesOnNetStandard20()`.
- `test/Synto.Test/snapshots/` — new injected-surface goldens (auto-named; review + accept).
- `examples/.../Generator/Model.cs`, `examples/.../Generator/ObjectReaderGenerator.cs` (Task 3).

**Reuse (do not modify):** `SurfaceInjectionGenerator` (auto-discovers the new `Synto.Runtime.*` resources), `MatchFactorySourceGenerator`'s `IsExternalInit` polyfill, `MatchTestHarness.{CreateNetStandardClosure,InjectedSurfaceSource,GeneratedPolyfillSource,Run}`, the existing `SurfaceInjectionTest` (auto-covers new resources).

---

### Task 1: Inject the cacheability toolkit (`EquatableArray<T>` + `LocationInfo` + `DiagnosticInfo`)

**Files:**
- Create: `src/Synto/Generators/EquatableArray.cs`, `src/Synto/Generators/Diagnostics.cs`
- Modify: `src/Synto/Synto.csproj`, `src/Synto.SourceGenerator/Synto.SourceGenerator.csproj`, `test/Synto.Test/Match/MatchTestHarness.cs`
- Test: `test/Synto.Test/Generators/InjectedGeneratorUtilitiesTests.cs`; new injected goldens `SurfaceInjectionTest…#EquatableArray.g.verified.cs`, `#Diagnostics.g.verified.cs`

**Interfaces:**
- Produces (authored `public` in `src/Synto/Generators`, injected `internal` as `Synto.Generators.*`):
  ```csharp
  namespace Synto.Generators;
  public readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T> where T : IEquatable<T>
  { public static readonly EquatableArray<T> Empty; public EquatableArray(ImmutableArray<T> array); /* Count, indexer, structural Equals/GetHashCode, GetEnumerator */ }
  public record struct LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
  { public Location ToLocation(); public static LocationInfo? CreateFrom(Location? location); }
  public record struct DiagnosticInfo(DiagnosticDescriptor Descriptor, LocationInfo? Location, EquatableArray<string> MessageArgs)
  { public Diagnostic ToDiagnostic(); }
  ```
- Consumes: `SurfaceInjectionGenerator` (injection), `MatchTestHarness` ns2.0-closure helpers. Contract: **C-1**, **C-2**, **C-3**, **C-4**, and the `Synto.Test` collision check.

- [ ] **Step 1: Write the failing test**

`test/Synto.Test/Generators/InjectedGeneratorUtilitiesTests.cs`:

```csharp
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Synto.Test.Generators;

public class InjectedGeneratorUtilitiesTests
{
    [Fact]
    public void InjectedCacheabilityToolkit_CompilesOn_NetStandard20() // C-2
    {
        var diagnostics = MatchTestHarness.CompileInjectedGeneratorUtilitiesOnNetStandard20();
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }
}
```

Add the harness helper to `test/Synto.Test/Match/MatchTestHarness.cs` (mirror `CompileInjectedMatchReplaceSurfaceOnNetStandard20`):

```csharp
/// <summary>
/// C-2 self-containment proof for the injected generator-author utilities: compiles the injected
/// EquatableArray + LocationInfo + DiagnosticInfo (Task 1) on the FAITHFUL netstandard2.0 closure,
/// alongside the IsExternalInit polyfill the record structs need. Returns the resulting diagnostics
/// (must be error-free). Task 2 extends this to also include Interceptors.
/// </summary>
public static ImmutableArray<Diagnostic> CompileInjectedGeneratorUtilitiesOnNetStandard20()
{
    var equatableArray = InjectedSurfaceSource("struct EquatableArray");
    var diagnostics = InjectedSurfaceSource("record struct DiagnosticInfo"); // Diagnostics.cs carries LocationInfo + DiagnosticInfo
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
Expected: FAIL to compile — `CompileInjectedGeneratorUtilitiesOnNetStandard20` references `InjectedSurfaceSource("struct EquatableArray")`, but no `Synto.Runtime.EquatableArray`/`Synto.Runtime.Diagnostics` resource is injected yet (the helper returns no such file / the test type is unresolved). RED.

- [ ] **Step 3: Implement (by contract)**

1. `src/Synto/Generators/EquatableArray.cs` — author `public readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T> where T : IEquatable<T>` **mirroring `src/Synto.SourceGenerator/EquatableArray.cs` verbatim**, changed only to `namespace Synto.Generators;` and `public`. (`Empty`, `ImmutableArray<T>` backing, `Count`, indexer, `Equals` via `EqualityComparer<T>.Default`, `GetHashCode`, `GetEnumerator`.)
2. `src/Synto/Generators/Diagnostics.cs` — author `public record struct LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)` (with `ToLocation()` + `CreateFrom(Location?)`) and `public record struct DiagnosticInfo(DiagnosticDescriptor Descriptor, LocationInfo? Location, EquatableArray<string> MessageArgs)` (with `ToDiagnostic()`), **mirroring `src/Synto.SourceGenerator/DiagnosticInfo.cs`**, changed only to `namespace Synto.Generators;` and `public`.
3. `src/Synto/Synto.csproj` — add `<Compile Remove="Generators\EquatableArray.cs" />` and `<Compile Remove="Generators\Diagnostics.cs" />` (next to the existing `Matching\MatchReplaceExtensions.cs` remove). Confirm `src/Synto` still builds (these are embed-only).
4. `src/Synto.SourceGenerator/Synto.SourceGenerator.csproj` — add `<EmbeddedResource Include="..\Synto\Generators\EquatableArray.cs" LogicalName="Synto.Runtime.EquatableArray.cs" />` and `<EmbeddedResource Include="..\Synto\Generators\Diagnostics.cs" LogicalName="Synto.Runtime.Diagnostics.cs" />` (next to the existing `Synto.Runtime.*` markers).

- [ ] **Step 4: Run the test + gate**

Run the green gate. Review + accept the new injected-surface goldens (`…#EquatableArray.g.verified.cs`, `#Diagnostics.g.verified.cs`) after confirming each is the `internal`-rewritten authored source (the `public`→`internal` flip, `#nullable enable` header). **Crucially confirm `PipelineEquatabilityTests` still passes** (the collision check) and the example still builds (its own `EquatableArray` is a different namespace — no clash). Expected: the self-containment fact PASSES; new goldens green; whole suite green; format clean.

- [ ] **Step 5: Commit**

```bash
jj commit -m "feat(gen-utils): inject EquatableArray + LocationInfo + DiagnosticInfo cacheability toolkit"
```

---

### Task 2: Inject the interceptor-attribute helper (`Interceptors`)

**Files:**
- Create: `src/Synto/Generators/Interceptors.cs`
- Modify: `src/Synto/Synto.csproj`, `src/Synto.SourceGenerator/Synto.SourceGenerator.csproj`, `test/Synto.Test/Match/MatchTestHarness.cs`
- Test: `test/Synto.Test/Generators/InjectedGeneratorUtilitiesTests.cs`; new golden `…#Interceptors.g.verified.cs`

**Interfaces:**
- Produces (injected `internal` as `Synto.Generators.Interceptors`):
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
- Consumes: Task 1's harness helper (extend it). Contract: **C-2**, **C-4**, and (load-bearing for Task 3) **`AttributeSource` is byte-identical** to the example's current `InterceptsLocationAttributeSource` const so the example's `InterceptsLocationAttribute.g.cs` snapshot stays unchanged.

- [ ] **Step 1: Write the failing test**

Extend `InjectedGeneratorUtilitiesTests.cs`:

```csharp
[Fact]
public void InjectedInterceptorsHelper_DefinesTheCanonicalAttribute() // C-4: exact attribute the version-stamped usage binds to
{
    var source = MatchTestHarness.InjectedSurfaceSource("class Interceptors");
    Assert.Contains("namespace System.Runtime.CompilerServices", source);
    Assert.Contains("class InterceptsLocationAttribute", source);
    Assert.Contains("InterceptsLocationAttribute(int version, string data)", source);
}
```

Extend `CompileInjectedGeneratorUtilitiesOnNetStandard20()` in `MatchTestHarness.cs` to also compile the helper (it must be self-contained too):

```csharp
    var interceptors = InjectedSurfaceSource("class Interceptors");
    return CreateNetStandardClosure(equatableArray, diagnostics, interceptors, polyfill).GetDiagnostics();
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --no-build -c Debug` (build first).
Expected: FAIL — `InjectedSurfaceSource("class Interceptors")` finds no `Synto.Runtime.Interceptors` resource yet. RED.

- [ ] **Step 3: Implement (by contract)**

1. `src/Synto/Generators/Interceptors.cs` — author:

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

    // MUST stay byte-identical to the ObjectReader example's previous const so its snapshot is unchanged.
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

2. `src/Synto/Synto.csproj` — add `<Compile Remove="Generators\Interceptors.cs" />`.
3. `src/Synto.SourceGenerator/Synto.SourceGenerator.csproj` — add `<EmbeddedResource Include="..\Synto\Generators\Interceptors.cs" LogicalName="Synto.Runtime.Interceptors.cs" />`.

- [ ] **Step 4: Run the test + gate**

Run the green gate. Review + accept the new `…#Interceptors.g.verified.cs` golden (the `public`→`internal`-rewritten helper). Expected: both new facts PASS; Task 1 facts still green; whole suite green; format clean.

- [ ] **Step 5: Commit**

```bash
jj commit -m "feat(gen-utils): inject Interceptors InterceptsLocationAttribute-definition helper"
```

---

### Task 3: Close the loop — refactor the ObjectReader example onto the toolkit

**Files:**
- Modify: `examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Generator/Model.cs`, `examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Generator/ObjectReaderGenerator.cs`

**Interfaces:** Consumes Tasks 1–2 (`Synto.Generators.{EquatableArray,LocationInfo,DiagnosticInfo,Interceptors}`). Produces no new API. Contract: **C-6** — the example builds green, all its tests pass, and **both example snapshots are byte-identical** (the refactor changes how the generator is built, not what it emits).

- [ ] **Step 1: Establish the guard (no new test — existing tests are the spec)**

The example's existing `ObjectReaderBehaviorTests`, `ObjectReaderDiagnosticsTests`, `ObjectReaderSnapshotTests`, and `ObjectReaderIncrementalTests` are the regression guard. Confirm they are green before refactoring:

Run: `dotnet test --no-build -c Debug` (after a build).
Expected: all example tests PASS (baseline).

- [ ] **Step 2: Refactor (by contract)**

1. `Model.cs`:
   - Add `using Synto.Generators;`.
   - **Delete** the hand-rolled `internal readonly struct EquatableArray<T>` (~60 lines) and `internal readonly record struct LocationInfo` — now supplied by `Synto.Generators`.
   - **Rename** the carrier `internal readonly record struct DiagnosticInfo(DiagnosticKind Kind, …)` → `PendingDiagnostic` (avoids clashing with the injected `Synto.Generators.DiagnosticInfo`); add a comment: `// stopgap until Synto.Diagnostics supports the cacheable path (its own future spec)`.
   - Update `ObjectReaderModel`'s `EquatableArray<DiagnosticInfo> Diagnostics` field to `EquatableArray<PendingDiagnostic>`. Keep `ColumnInfo`, `DiagnosticKind`, `ObjectReaderModel`.
   - The `EquatableArray<T>` constructor call sites change from `new EquatableArray<…>(arr.ToArray())` (T[] ctor) to `new EquatableArray<…>(arr.ToImmutableArray())` (the mirror takes `ImmutableArray<T>`). Adjust accordingly.
2. `ObjectReaderGenerator.cs`:
   - Rename all `DiagnosticInfo` references to `PendingDiagnostic`.
   - **Delete** the `private const string InterceptsLocationAttributeSource = …` (~16 lines).
   - Replace `spc.AddSource("InterceptsLocationAttribute.g.cs", SourceText.From(InterceptsLocationAttributeSource, …))` with `global::Synto.Generators.Interceptors.AddDefinition(spc);`.

- [ ] **Step 3: Run the example tests + gate**

Run the green gate. **The two example goldens (`ObjectReaderSnapshotTests…#ObjectReader.g.verified.cs`, `#InterceptsLocationAttribute.g.verified.cs`) MUST be unchanged** — if `#InterceptsLocationAttribute` changed, `Interceptors.AttributeSource` drifted from the original const (Task 2 contract); fix the source to match rather than re-accepting the golden. Expected: all example tests PASS; both example snapshots unchanged; whole suite green; format clean. The example's `Model.cs` and `ObjectReaderGenerator.cs` are ≈90 lines lighter.

- [ ] **Step 4: Commit**

```bash
jj commit -m "refactor(objectreader): consume Synto.Generators toolkit; drop hand-rolled plumbing"
```

---

## Self-Review (against the spec)

- **§3 D1 single-sourced injectable surface (author in src/Synto/Generators, Compile-Remove, embed; internals untouched)** → Task 1 + Task 2 (files + csproj wiring). ✓
- **§3 D2 namespace `Synto.Generators`, unconditional internal injection** → Task 1/2 (authored public → rewriter flips). ✓
- **§3 D3 interceptor helper = attribute definition only** → Task 2 (`AddDefinition` + `AttributeSource`; body stays bespoke). ✓
- **§3 D4 / C-5 Synto.Diagnostics untouched** → no task modifies it; the example keeps `DiagnosticKind`→factory (renamed carrier). ✓
- **§3 D5 / C-6 prove by deletion; snapshots unchanged** → Task 3. ✓
- **§3 D6 internal duplicates left as-is** → no relocation in any task. ✓
- **C-1 single source** → Task 1/2 (one authored copy each, embedded). ✓
- **C-2 self-contained on ns2.0 (IsExternalInit already unconditional)** → Task 1 (`CompileInjectedGeneratorUtilitiesOnNetStandard20`), extended Task 2. ✓
- **C-3 collision-safe; Synto.Test green (PipelineEquatabilityTests)** → Task 1 Step 4 gate (explicit check). ✓
- **C-4 no output-shape change; example snapshots byte-identical** → Task 2 (`AttributeSource` byte-identical) + Task 3 Step 3 (goldens unchanged). ✓
- **§9 testing (self-containment, injected goldens, core suite green, example green + unchanged snapshot)** → Tasks 1–3. ✓
- **§8 non-goals (no Synto.Diagnostics change, no interceptor body gen, no internal de-dup, no public Synto.Core API)** → honored: types are Compile-Removed (not Synto.Core public API); helper is attribute-only; internals untouched. ✓

**Placeholder scan:** none — the one "mirror the internal file" instruction cites the exact source file to copy; test code is complete. ✓

**Type consistency:** `Synto.Generators.{EquatableArray<T>, LocationInfo, DiagnosticInfo, Interceptors}`, `AttributeHintName`/`AttributeSource`/`AddDefinition`, `CompileInjectedGeneratorUtilitiesOnNetStandard20`, and the example rename `DiagnosticInfo`→`PendingDiagnostic` are used identically across tasks. ✓

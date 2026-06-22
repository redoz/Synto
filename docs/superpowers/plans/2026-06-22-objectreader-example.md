# ObjectReader Dog-Food Example — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Plan style:** This plan pins **contracts, interfaces, signatures, and tests** — not prescribed implementation bodies (matching the match-replace / matching-dsl plans). Each task states exact public signatures, the locked output shape, and concrete test code; the implementer chooses the generator implementation that satisfies them. **Test code is the spec** — write it as given, adapting only to the exact harness helper names.

**Goal:** Re-introduce `ObjectReader` as a working, runnable dog-food example — a FastMember-equivalent that turns `ObjectReader.Create<T>(data, members…)` into a source-generated, type-specialized `IDataReader` (no runtime reflection), built by dog-fooding Synto's templating and measuring the friction.

**Architecture:** Four projects under one `examples/Synto.Example.ObjectReader/` umbrella — a net10.0 runtime API, a netstandard2.0 incremental generator that **intercepts** constant `Create` calls and emits a specialized `file`-scoped `IDataReader`, a runnable net10.0 Demo, and a co-located net10.0 test project (Verify snapshot + behavioral roundtrip + diagnostics). The generator is built bottom-up: a walking-skeleton interceptor first (de-risk interception with raw `SyntaxFactory`), then member resolution, then diagnostics + an equatable pipeline, then the reader skeleton is migrated onto a Synto `[Template]` (the dog-food payoff), with friction logged throughout.

**Tech Stack:** C# / .NET 10, Roslyn incremental source generators (`IIncrementalGenerator`, C# interceptors via `SemanticModel.GetInterceptableLocation`), Synto Templating (`[Template]`/`[Inline]`/`Syntax<T>`) + `Synto.Diagnostics`, xUnit v3 (MTP v2) + Verify snapshots, jujutsu (jj) VCS.

**Spec:** `docs/superpowers/specs/2026-06-22-objectreader-example-design.md` (contracts C-1…C-6; risks R1…R3).

## Global Constraints

Every task implicitly includes these (copied from the spec):

- **Working, not stubbed (D4 / C-3).** Every emitted `IDataReader` member is implemented or throws `NotSupportedException` **with a reason**. **Zero `NotImplementedException`.** The functional bar is a real roundtrip: read rows back and `DataTable.Load(reader)`. Nulls surface as **`DBNull.Value`**; `IsDBNull` agrees; `Read()` before first row / past end throws `InvalidOperationException`.
- **No reflection on the hot path (C-1).** Generated switch arms access members **directly** (`_e.Current.Name`). No `System.Reflection` / `Reflection.Emit` / `dynamic` in generated code.
- **Incremental cacheability (C-5).** The generator **never** captures `Compilation` / `ISymbol` / `SemanticModel` / `SyntaxNode` into cached pipeline state. Per-call-site analysis runs inside the syntax/semantic transform and flows out an **equatable value** (target type name + ordered resolved columns with type names + the interceptable-location payload + equatable diagnostics). Emission happens in `RegisterSourceOutput` from that value alone.
- **No exceptions escape (C-5 / Synto principle).** Generation failures on a reachable path become a `SOR0000` diagnostic — never thrown.
- **Self-contained generated output (C-6).** Emitted reader + interceptor reference only BCL (`System.Data`, `System.Collections.Generic`) and the consumer's own types — **no `Synto.*` runtime dependency** in the consumer (Synto's surface is generator-internal/injected).
- **TFMs:** `.Generator` = **netstandard2.0** (required — Roslyn loads analyzers as ns2.0). `.Api` / `.Demo` / `.Tests` = **net10.0**.
- **Interceptors:** use `SemanticModel.GetInterceptableLocation(invocation)` → `location.GetInterceptsLocationAttributeSyntax()` (the supported, version-stamped model — **never** the deprecated `InterceptsLocation(file, line, char)`). Consumers (`.Demo`, `.Tests`) enable it with `<InterceptorsNamespaces>$(InterceptorsNamespaces);Synto.Example.ObjectReader.Generated</InterceptorsNamespaces>`.
- **Green gate (run before every commit):** `dotnet build -c Debug` (0 errors; analyzer **warnings** are findings, not gate failures) · `dotnet test --no-build -c Debug` (all green — **never** pass `--nologo`; MTP treats it as unknown and runs zero tests, exit 5) · `dotnet format whitespace` then `dotnet format whitespace --verify-no-changes` (no diffs; **whitespace scope only** — never full `dotnet format`). Root `.editorconfig` pins `end_of_line=lf` + final newline.
- **Commits:** Conventional Commits, **no AI/Claude footer**, one commit per task. VCS is **jj**: `jj commit -m "…"` finalizes `@` and opens a new change on top. **Never** `git`.
- **Branch:** work on **`experimental/object-reader`** (already created at the spec commit `3db69a7c`, off the `experimental/matching` tip). Never main; **no `jj git push`**; do **not** move the bookmark — the operator advances it after review.
- **Friction log (the secondary deliverable, §11 of the spec).** Append genuine findings to `docs/superpowers/notes/2026-06-22-objectreader-synto-friction.md` as you go (verbose constructs, strategy-A→B drops, "Synto can't express this", interceptor plumbing). Empty findings are fine — do not manufacture friction.
- **Locked names:** API `Synto.Example.ObjectReader.Api.ObjectReader.Create<T>(IEnumerable<T>, params string[])`; generator `ObjectReaderGenerator`; emitted namespace `Synto.Example.ObjectReader.Generated`; emitted reader `ObjectReader_{TypeShortName}_{N}` (`file sealed`); interceptor holder `ObjectReaderInterceptors` (`file static`); diagnostics `SOR0000`/`SOR0001`/`SOR0002`, categories `ObjectReader.Internal` / `ObjectReader.Usage`. The generator recognizes calls by the metadata name string `"Synto.Example.ObjectReader.Api.ObjectReader"` (it does **not** reference `.Api`).

## File Structure

**Create (under `examples/Synto.Example.ObjectReader/`):**
- `Synto.Example.ObjectReader.Api/Synto.Example.ObjectReader.Api.csproj` — net10.0 runtime surface. No Synto reference.
- `Synto.Example.ObjectReader.Api/ObjectReader.cs` — `public static class ObjectReader` + `Create<T>`.
- `Synto.Example.ObjectReader.Generator/Synto.Example.ObjectReader.Generator.csproj` — ns2.0 generator.
- `Synto.Example.ObjectReader.Generator/ObjectReaderGenerator.cs` — the `IIncrementalGenerator`.
- `Synto.Example.ObjectReader.Generator/Model.cs` — equatable pipeline model (`ColumnInfo`, `ObjectReaderModel`, a minimal `EquatableArray<T>`, `DiagnosticInfo` carrier).
- `Synto.Example.ObjectReader.Generator/Diagnostics.cs` — `Synto.Diagnostics` `[Diagnostic]` descriptors (Task 3).
- `Synto.Example.ObjectReader.Generator/ReaderTemplate.cs` — the `[Template]` reader skeleton (Task 4).
- `Synto.Example.ObjectReader.Demo/Synto.Example.ObjectReader.Demo.csproj` — net10.0 console.
- `Synto.Example.ObjectReader.Demo/Program.cs`, `Synto.Example.ObjectReader.Demo/Person.cs`.
- `Synto.Example.ObjectReader.Tests/Synto.Example.ObjectReader.Tests.csproj` — net10.0 test project.
- `Synto.Example.ObjectReader.Tests/{ObjectReaderApiTests,ObjectReaderBehaviorTests,ObjectReaderSnapshotTests,ObjectReaderDiagnosticsTests}.cs`, `ModuleInitializer.cs`, `GeneratorHarness.cs`.
- `Synto.Example.ObjectReader/README.md` — short "what/why/how it dog-foods Synto" (Task 5).
- `docs/superpowers/notes/2026-06-22-objectreader-synto-friction.md` — friction log (Task 1 creates header).

**Modify:**
- `Synto.slnx` — add the four projects under the `/examples/` folder.
- `docs/superpowers/specs/2026-06-22-objectreader-example-design.md` — mark delivered (Task 5).

**Reuse (do not modify):** `Synto.SourceGenerator` (consumed by `.Generator` as an analyzer — injects the `internal` `[Template]`/`[Inline]`/`Syntax<T>` surface), `Synto.Diagnostics` (analyzer), the central package versions in `Directory.Packages.props` (`xunit.v3.mtp-v2`, `Verify.XunitV3`, `Verify.SourceGenerators`, `Microsoft.CodeAnalysis.CSharp`, `Microsoft.Testing.Extensions.CodeCoverage` — all already pinned), `test/Synto.Test`'s generator-driver + `ModuleInitializer` pattern (mirror it).

---

### Task 1: `.Api` runtime surface + Tests scaffold — un-intercepted `Create` throws (C-4)

**Files:**
- Create: `examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Api/Synto.Example.ObjectReader.Api.csproj`, `.../Api/ObjectReader.cs`
- Create: `examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Tests/Synto.Example.ObjectReader.Tests.csproj`, `.../Tests/ObjectReaderApiTests.cs`
- Create: `docs/superpowers/notes/2026-06-22-objectreader-synto-friction.md` (header only)
- Modify: `Synto.slnx`

**Interfaces:**
- Produces: `Synto.Example.ObjectReader.Api.ObjectReader.Create<T>(IEnumerable<T> source, params string[] members) : IDataReader` — un-intercepted body throws descriptive `NotSupportedException` (C-4). Consumed by every later task and by `.Demo`/`.Tests`.

- [ ] **Step 1: Write the failing test**

`examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Tests/ObjectReaderApiTests.cs`:

```csharp
using Synto.Example.ObjectReader.Api;
using Xunit;

namespace Synto.Example.ObjectReader.Tests;

public class ObjectReaderApiTests
{
    private sealed record Widget(string Sku, int Qty);

    [Fact]
    public void Create_WhenNotIntercepted_ThrowsDescriptiveNotSupported() // C-4
    {
        // A NON-constant member list is never intercepted (the SOR0002 path); the runtime
        // fallback must throw a descriptive NotSupportedException. Stays valid for every later
        // task because this call is never a candidate for interception.
        var data = new[] { new Widget("A", 1) };
        string[] members = GetMembers(); // runtime value → not compile-time constant

        var ex = Assert.Throws<NotSupportedException>(() => ObjectReader.Create(data, members));
        Assert.Contains("constant", ex.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    private static string[] GetMembers() => ["Sku", "Qty"];
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet build -c Debug` (the `.Api` type / project does not exist yet).
Expected: FAIL to compile — `Synto.Example.ObjectReader.Api` is unresolved. RED.

- [ ] **Step 3: Implement (by contract)**

1. `Synto.Example.ObjectReader.Api.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
</Project>
```

2. `Api/ObjectReader.cs` — the locked surface; the un-intercepted body throws (C-4):

```csharp
using System.Data;

namespace Synto.Example.ObjectReader.Api;

/// <summary>
/// FastMember-equivalent entry point: exposes a sequence of <typeparamref name="T"/> as an
/// <see cref="IDataReader"/>. A call whose <paramref name="members"/> are a compile-time-constant
/// list is intercepted by the ObjectReader generator and routed to a type-specialized reader with
/// no runtime reflection.
/// </summary>
public static class ObjectReader
{
    /// <summary>Create an <see cref="IDataReader"/> over <paramref name="source"/> exposing the named members as columns.</summary>
    public static IDataReader Create<T>(IEnumerable<T> source, params string[] members)
        => throw new NotSupportedException(
            "ObjectReader.Create was not intercepted by the source generator. 'members' must be a " +
            "compile-time-constant list of names, and interceptors must be enabled " +
            "(<InterceptorsNamespaces> must include Synto.Example.ObjectReader.Generated).");
}
```

3. `Synto.Example.ObjectReader.Tests.csproj` (mirror `test/Synto.Test`'s MTP wiring; `.Generator` analyzer ref is added in Task 2):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="xunit.v3.mtp-v2" />
    <PackageReference Include="Microsoft.Testing.Extensions.CodeCoverage" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Synto.Example.ObjectReader.Api\Synto.Example.ObjectReader.Api.csproj" />
  </ItemGroup>
</Project>
```

4. `Synto.slnx` — add under the existing `/examples/` folder:

```xml
    <Project Path="examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Api/Synto.Example.ObjectReader.Api.csproj" />
    <Project Path="examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Tests/Synto.Example.ObjectReader.Tests.csproj" />
```

5. `docs/superpowers/notes/2026-06-22-objectreader-synto-friction.md` — create with a header:

```markdown
# ObjectReader → Synto friction log

Living log of where building the ObjectReader dog-food example against Synto-as-is hurt.
Feeds a separate Synto-improvement spec (spec §11). One entry per finding: what hurt, why,
"Synto could make this easier by …". No findings yet.
```

- [ ] **Step 4: Run the test + gate**

Run the green gate. Expected: `Create_WhenNotIntercepted_ThrowsDescriptiveNotSupported` PASSES; build 0 errors; format clean. Append any scaffolding friction (e.g. csproj boilerplate) to the friction log if notable.

- [ ] **Step 5: Commit**

```bash
jj commit -m "feat(objectreader): Api surface + Tests scaffold; un-intercepted Create throws (C-4)"
```

---

### Task 2: Walking-skeleton generator — intercept a constant `Create<T>` call, emit a specialized reader, prove the roundtrip (R1, C-1, C-3)

**Files:**
- Create: `.../Generator/Synto.Example.ObjectReader.Generator.csproj`, `.../Generator/ObjectReaderGenerator.cs`, `.../Generator/Model.cs`
- Create: `.../Demo/Synto.Example.ObjectReader.Demo.csproj`, `.../Demo/Program.cs`, `.../Demo/Person.cs`
- Create: `.../Tests/ObjectReaderBehaviorTests.cs`, `.../Tests/ObjectReaderSnapshotTests.cs`, `.../Tests/GeneratorHarness.cs`, `.../Tests/ModuleInitializer.cs`; snapshot golden under `.../Tests/snapshots/`
- Modify: `.../Tests/Synto.Example.ObjectReader.Tests.csproj` (analyzer+assembly ref to `.Generator`, `<InterceptorsNamespaces>`, Verify + Roslyn packages); `Synto.slnx`

**Interfaces:**
- Produces:
  - `ObjectReaderGenerator : IIncrementalGenerator` — finds invocations of `Synto.Example.ObjectReader.Api.ObjectReader.Create`, resolves the **constant** member list against `T`'s public instance properties/fields (by exact name, in call order), and emits one source file per compilation containing, per intercepted call: a `file sealed class ObjectReader_{TypeShortName}_{N} : IDataReader` and a `[InterceptsLocation]` method on `file static class ObjectReaderInterceptors`. **This task uses raw `SyntaxFactory`** (no `[Template]` yet) and resolves members **silently** (unknown/non-constant → skip interception; diagnostics land in Task 3).
  - Equatable model in `Model.cs`: `readonly record struct ColumnInfo(string Name, string ColumnTypeName)`; `readonly record struct ObjectReaderModel(string TargetTypeQualifiedName, string TargetTypeShortName, EquatableArray<ColumnInfo> Columns, string InterceptsLocationAttribute)`; a minimal `readonly struct EquatableArray<T> : IEquatable<…>` (structural equality over an underlying array). These flow through the pipeline (C-5).
- Consumes: Task 1's `ObjectReader.Create`. Contract: **C-1**, **C-3**, **C-5**, **C-6**, **R1**.

- [ ] **Step 1: Write the failing tests**

`ObjectReaderBehaviorTests.cs` — the load-bearing proof (runtime interception + a real roundtrip):

```csharp
using System.Collections.Generic;
using System.Data;
using Synto.Example.ObjectReader.Api;
using Xunit;

namespace Synto.Example.ObjectReader.Tests;

public class ObjectReaderBehaviorTests
{
    private sealed record Person(string Name, int Age, string? Nickname);

    private static Person[] Sample() =>
    [
        new Person("Ada", 36, "Countess"),
        new Person("Alan", 41, null),
    ];

    [Fact]
    public void Create_IsIntercepted_AndReadsRowsDirectly() // R1 + C-1 + C-3
    {
        using IDataReader reader = ObjectReader.Create(Sample(), "Name", "Age", "Nickname");

        Assert.Equal(3, reader.FieldCount);
        Assert.Equal("Name", reader.GetName(0));
        Assert.Equal(typeof(int), reader.GetFieldType(1));
        Assert.Equal(2, reader.GetOrdinal("Nickname"));

        var rows = new List<object[]>();
        while (reader.Read())
        {
            var values = new object[reader.FieldCount];
            reader.GetValues(values);
            rows.Add(values);
        }

        Assert.Equal(2, rows.Count);
        Assert.Equal("Ada", rows[0][0]);
        Assert.Equal(36, rows[0][1]);
        Assert.Equal(DBNull.Value, rows[1][2]); // null member → DBNull.Value (C-3)
    }

    [Fact]
    public void Create_FeedsDataTableLoad() // C-3 functional bar via a real ADO.NET sink
    {
        using IDataReader reader = ObjectReader.Create(Sample(), "Name", "Age");
        var table = new DataTable();
        table.Load(reader);

        Assert.Equal(2, table.Columns.Count);
        Assert.Equal("Name", table.Columns[0].ColumnName);
        Assert.Equal(typeof(string), table.Columns[0].DataType);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal("Alan", table.Rows[1]["Name"]);
    }
}
```

`ObjectReaderSnapshotTests.cs` — pin the generated output shape (CSharpGeneratorDriver + Verify):

```csharp
using System.Threading.Tasks;
using Xunit;

namespace Synto.Example.ObjectReader.Tests;

public class ObjectReaderSnapshotTests
{
    [Fact]
    public Task Generates_Specialized_Reader_For_Person()
    {
        const string source = """
            using System.Collections.Generic;
            using Synto.Example.ObjectReader.Api;

            public sealed record Person(string Name, int Age);

            public static class Demo
            {
                public static void Run(IEnumerable<Person> people)
                {
                    using var r = ObjectReader.Create(people, "Name", "Age");
                }
            }
            """;

        return GeneratorHarness.Verify(source);
    }
}
```

`GeneratorHarness.cs` — pin the harness **contract** (implementer mirrors `test/Synto.Test`'s driver harness):

```csharp
using System.Threading.Tasks;

namespace Synto.Example.ObjectReader.Tests;

internal static class GeneratorHarness
{
    /// <summary>
    /// Builds an in-memory net10 CSharpCompilation over <paramref name="source"/> referencing the
    /// ObjectReader API surface, runs <c>ObjectReaderGenerator</c> through a CSharpGeneratorDriver,
    /// and snapshots the driver result. Mirror test/Synto.Test's generator-driver harness.
    /// </summary>
    public static Task Verify(string source); // implement to return Verifier.Verify(driver).UseDirectory("snapshots")
}
```

`ModuleInitializer.cs` (mirror `test/Synto.Test`):

```csharp
using System.Runtime.CompilerServices;

namespace Synto.Example.ObjectReader.Tests;

internal static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Init()
    {
        VerifySourceGenerators.Initialize();
        VerifierSettings.UseDirectory("snapshots");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build -c Debug`.
Expected: FAIL to compile — `ObjectReaderGenerator`, `GeneratorHarness.Verify` body, and the analyzer wiring don't exist; `Create` calls aren't intercepted. RED.

- [ ] **Step 3: Implement (by contract)**

1. `Synto.Example.ObjectReader.Generator.csproj` — ns2.0 generator (Roslyn only this task; Synto + Diagnostics arrive in Tasks 3–4):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" />
  </ItemGroup>
</Project>
```

2. `Model.cs` — the equatable types from **Interfaces** (`ColumnInfo`, `ObjectReaderModel`, minimal `EquatableArray<T>`). Pure value types; no Roslyn types stored (C-5). If hand-rolling `EquatableArray<T>` feels redundant against Synto's internal one, **log it** (a candidate "Synto could expose a cacheability toolkit" finding).

3. `ObjectReaderGenerator.cs` — `[Generator(LanguageNames.CSharp)] IIncrementalGenerator`. Behavior:
   - **Predicate** (cheap, syntax-only): an `InvocationExpressionSyntax` whose expression is a member access named `Create` (`IdentifierName` or `GenericNameSyntax`) with `≥ 1` argument.
   - **Transform** (semantic, runs once per candidate, captures nothing): confirm the target symbol is `ObjectReader.Create` on the type with metadata name `"Synto.Example.ObjectReader.Api.ObjectReader"`; read the single generic type argument `T`; read arguments `1..n` as **constant** strings (`GetConstantValue`); resolve each against `T`'s public instance properties/fields by exact name (skip unknown/non-constant silently this task); compute the interceptable-location attribute via `semanticModel.GetInterceptableLocation(invocation)!.GetInterceptsLocationAttributeSyntax()`. Emit an **equatable `ObjectReaderModel`** (or null if not a real target). **No `Compilation`/`ISymbol`/`SyntaxNode` in the result** (C-5).
   - **Output** (`RegisterSourceOutput` over the collected models): assign each model a stable `{N}` by position in the deterministic collected order; build, with raw `SyntaxFactory`, the locked output of spec §5 — `file sealed class ObjectReader_{TypeShortName}_{N} : IDataReader` + the `[InterceptsLocation]` interceptor on `file static class ObjectReaderInterceptors`; `NormalizeWhitespace()`; `AddSource("ObjectReader.g.cs", …)`. Required members per C-3 (`FieldCount`, `GetName`, `GetOrdinal`, `GetFieldType`, `GetValue`, `GetValues`, `Read`, `IsDBNull`, `Close`/`Dispose`, the typed getters routing through `GetValue` with a cast, indexers); `GetData`/`GetSchemaTable`/`GetBytes`/`GetChars` → `NotSupportedException` **unless** `DataTable.Load` forces a minimal `GetSchemaTable` (the `Create_FeedsDataTableLoad` test decides — implement what it needs). Nulls → `DBNull.Value`.
   - **R1 (interceptor signature):** settle whether the interceptor is generic `Create_{N}<T>` or non-generic with `T` substituted, so it binds to the inferred `Create<Person>(…)` call. **Spike this first** with one hard-coded shape if needed; record the resolution (and any ugliness) in the friction log.

4. `Demo` — `Person.cs` (a small POCO) + `Program.cs` that builds a `Person[]`, calls `ObjectReader.Create(people, "Name", "Age")`, iterates the reader, and prints a table to the console. `Synto.Example.ObjectReader.Demo.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
    <InterceptorsNamespaces>$(InterceptorsNamespaces);Synto.Example.ObjectReader.Generated</InterceptorsNamespaces>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Synto.Example.ObjectReader.Api\Synto.Example.ObjectReader.Api.csproj" />
    <ProjectReference Include="..\Synto.Example.ObjectReader.Generator\Synto.Example.ObjectReader.Generator.csproj"
                      OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
```

5. Extend `Synto.Example.ObjectReader.Tests.csproj` — reference `.Generator` **as analyzer AND assembly** (so behavioral tests get real interception and the snapshot harness can `new ObjectReaderGenerator()`), enable interceptors, add Verify + Roslyn:

```xml
  <PropertyGroup>
    <InterceptorsNamespaces>$(InterceptorsNamespaces);Synto.Example.ObjectReader.Generated</InterceptorsNamespaces>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" />
    <PackageReference Include="Verify.XunitV3" />
    <PackageReference Include="Verify.SourceGenerators" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Synto.Example.ObjectReader.Generator\Synto.Example.ObjectReader.Generator.csproj"
                      OutputItemType="Analyzer" PrivateAssets="all" ReferenceOutputAssembly="true" />
  </ItemGroup>
```

6. `Synto.slnx` — add `.Generator` and `.Demo` under `/examples/`.

- [ ] **Step 4: Run the tests + gate**

Run the green gate. Review + accept the new snapshot golden (`…SnapshotTests.Generates_Specialized_Reader_For_Person#…verified.cs`) after confirming it is the locked §5 shape (interceptor + specialized reader, zero `NotImplementedException`, no `Synto.*`/reflection). Expected: both behavioral facts PASS, the snapshot accepted, Task 1's fact still green, build 0 errors, format clean. Append R1/interceptor-plumbing friction.

- [ ] **Step 5: Commit**

```bash
jj commit -m "feat(objectreader): generator intercepts Create and emits a specialized IDataReader"
```

---

### Task 3: Diagnostics (`SOR0000`/`SOR0001`/`SOR0002`) + equatable diagnostic flow (C-5 / D5)

**Files:**
- Create: `.../Generator/Diagnostics.cs`
- Modify: `.../Generator/Synto.Example.ObjectReader.Generator.csproj` (add `Synto.Diagnostics` analyzer ref + `AnalyzerReleases.*`), `.../Generator/ObjectReaderGenerator.cs` (report diagnostics; carry them equatably), `.../Generator/Model.cs` (add a `DiagnosticInfo` carrier)
- Create: `.../Tests/ObjectReaderDiagnosticsTests.cs` (+ helper in `GeneratorHarness.cs` returning diagnostics)

**Interfaces:**
- Produces: `Diagnostics.MemberNotFound(Location?, string memberName)` → `SOR0001` (Warning); `Diagnostics.MembersNotConstant(Location?)` → `SOR0002` (Warning); `Diagnostics.InternalError(Location?, string type, string message)` → `SOR0000` (Error). The transform attaches an equatable `EquatableArray<DiagnosticInfo>` to `ObjectReaderModel`; the output stage replays them via `context.ReportDiagnostic`.
- Consumes: Task 2's generator + model. Contract: **D5**, **C-2**, **C-5** (diagnostics never break caching; exceptions → `SOR0000`, never thrown).

- [ ] **Step 1: Write the failing tests**

`ObjectReaderDiagnosticsTests.cs`:

```csharp
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Synto.Example.ObjectReader.Tests;

public class ObjectReaderDiagnosticsTests
{
    [Fact]
    public void UnknownMember_ReportsSOR0001_AndSkipsColumn() // C-2 / D5
    {
        const string source = """
            using System.Collections.Generic;
            using Synto.Example.ObjectReader.Api;
            public sealed record Person(string Name, int Age);
            public static class C { public static void M(IEnumerable<Person> p)
                => ObjectReader.Create(p, "Name", "Nope", "Age"); }
            """;

        (var diagnostics, var generated) = GeneratorHarness.Run(source);

        var sor0001 = Assert.Single(diagnostics, d => d.Id == "SOR0001");
        Assert.Equal(DiagnosticSeverity.Warning, sor0001.Severity);
        Assert.Contains("Nope", sor0001.GetMessage());
        Assert.DoesNotContain("\"Nope\"", generated); // bad column skipped (C-2)
        Assert.Contains("\"Name\"", generated);
        Assert.Contains("\"Age\"", generated);
    }

    [Fact]
    public void NonConstantMembers_ReportsSOR0002_AndDoesNotIntercept() // C-4 / D5
    {
        const string source = """
            using System.Collections.Generic;
            using Synto.Example.ObjectReader.Api;
            public sealed record Person(string Name, int Age);
            public static class C { public static void M(IEnumerable<Person> p, string[] names)
                => ObjectReader.Create(p, names); }
            """;

        (var diagnostics, var generated) = GeneratorHarness.Run(source);

        Assert.Contains(diagnostics, d => d.Id == "SOR0002");
        Assert.DoesNotContain("InterceptsLocation", generated); // not intercepted
    }
}
```

Add to `GeneratorHarness.cs`:

```csharp
/// <summary>Runs ObjectReaderGenerator over <paramref name="source"/>; returns generator
/// diagnostics and the concatenated generated text. (Same driver as Verify, different assertion.)</summary>
public static (System.Collections.Immutable.ImmutableArray<Microsoft.CodeAnalysis.Diagnostic> Diagnostics, string Generated) Run(string source);
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --no-build -c Debug` (build first). Expected: FAIL — no `SOR0001`/`SOR0002` reported yet; `GeneratorHarness.Run` is unimplemented. RED.

- [ ] **Step 3: Implement (by contract)**

1. Add to `.csproj`: `<ProjectReference Include="..\..\..\src\Synto.Diagnostics\Synto.Diagnostics.csproj" OutputItemType="Analyzer" PrivateAssets="all" ReferenceOutputAssembly="false" />` and the two `AnalyzerReleases.Shipped.md` / `AnalyzerReleases.Unshipped.md` `AdditionalFiles` (mirror the original example) to satisfy `EnforceExtendedAnalyzerRules` (RS2008). List `SOR0000/0001/0002` in `AnalyzerReleases.Unshipped.md`.
2. `Diagnostics.cs` — `internal static partial class Diagnostics` with `[Diagnostic("SOR0001", "Member not found", "Member '{0}' was not found on type '{1}'; the column is skipped.", "ObjectReader.Usage", DiagnosticSeverity.Warning, true)] public static partial Diagnostic MemberNotFound(Location? location, string memberName, string typeName);` and analogous `SOR0002` (Warning, `ObjectReader.Usage`) + `SOR0000` (Error, `ObjectReader.Internal`). Dog-foods `Synto.Diagnostics` (D5).
3. `Model.cs` — add `readonly record struct DiagnosticInfo(string Id, string Message, /* equatable location payload */ …)`; add `EquatableArray<DiagnosticInfo> Diagnostics` to `ObjectReaderModel`. The transform records `SOR0001` per unknown member (and still skips it — C-2) and `SOR0002` when members aren't constant (and emits no interceptor for that call). Wrap the transform's body so any unexpected exception becomes a `SOR0000` `DiagnosticInfo` — **never thrown** (C-5).
4. `ObjectReaderGenerator.cs` output stage — replay `model.Diagnostics` through `context.ReportDiagnostic(...)` before/with emission.

- [ ] **Step 4: Run the tests + gate**

Run the green gate. The happy-path snapshot from Task 2 must be **unchanged** (diagnostics only affect error inputs). Expected: both diagnostics facts PASS; all earlier facts green; build 0 errors (Synto.Diagnostics may add analyzer warnings — findings, not failures); format clean.

- [ ] **Step 5: Commit**

```bash
jj commit -m "feat(objectreader): SOR0001/0002/0000 diagnostics via Synto.Diagnostics; equatable flow"
```

---

### Task 4: Dog-food Synto — migrate the reader skeleton onto a `[Template]` (strategy A) + friction log

**Files:**
- Create: `.../Generator/ReaderTemplate.cs` (the `[Template]` invariant skeleton + `Factory` partial)
- Modify: `.../Generator/Synto.Example.ObjectReader.Generator.csproj` (add `Synto.SourceGenerator` analyzer ref), `.../Generator/ObjectReaderGenerator.cs` (build the reader from `Factory.*` + `Syntax<>` holes instead of raw `SyntaxFactory`)
- Modify: `docs/superpowers/notes/2026-06-22-objectreader-synto-friction.md` (the core findings)

**Interfaces:**
- Produces: no public-surface change. The emitted reader is now constructed by quoting a Synto `[Template]` skeleton with the per-type variable parts supplied as `Syntax<>` holes (or, where a hole genuinely cannot be expressed, raw `SyntaxFactory` for *that piece only* — a logged strategy-A→B drop, D3/R2).
- Consumes: Tasks 2–3. Contract: **D3** (heavy Synto, drop-on-wall), **R2**, and the output stays byte-equivalent enough that the behavioral tests pass (snapshot re-accepted if formatting shifts).

- [ ] **Step 1: Write the failing test**

No new behavioral contract — this is a generation-strategy migration guarded by the **existing** Task 2/3 tests. Add one guard that the dog-food actually happened (the generator now depends on Synto's injected surface):

```csharp
// in ObjectReaderSnapshotTests.cs
[Fact]
public void Generator_UsesSyntoTemplate_NotHandRolledFactorySoupOnly()
{
    // Sentinel: the reader skeleton is produced via a Synto [Template] Factory. Asserted indirectly —
    // ReaderTemplate.cs exists and the generator references Factory.*; kept as a guard so a regression
    // that rips Synto back out is visible. (Adapt to a concrete, cheap observable if one is handy.)
    Assert.True(System.IO.File.Exists(
        System.IO.Path.Combine(GeneratorHarness.GeneratorProjectDir, "ReaderTemplate.cs")));
}
```

> If a file-existence sentinel reads as too weak against the harness, drop it — the **load-bearing** guarantee for this task is that Task 2/3's behavioral + snapshot + diagnostics tests stay green while the reader is built from a `[Template]`. Do not over-invest in a meta-assertion.

- [ ] **Step 2: Run to verify (RED/strategy)**

Run: `dotnet test --no-build -c Debug`. Expected: the sentinel fails (no `ReaderTemplate.cs`); the existing behavioral/snapshot/diagnostics tests are the real guard and must stay green through the migration.

- [ ] **Step 3: Implement (by contract)**

1. Add `<ProjectReference Include="..\..\..\src\Synto.SourceGenerator\Synto.SourceGenerator.csproj" OutputItemType="Analyzer" PrivateAssets="all" ReferenceOutputAssembly="false" />` to the generator csproj. This injects the `internal` `[Template]`/`[Inline]`/`Syntax<T>`/`Factory` surface into `.Generator` (no `Synto.Core` reference — consume the generator only, per the architecture doc).
2. `ReaderTemplate.cs` — author the **invariant** `IDataReader` skeleton once as a `[Template(typeof(Factory))]` (enumerator/`Read`/`Close`/`Dispose`/`NextResult`/`Depth`/`IsClosed`/`RecordsAffected`, typed getters routing through `GetValue`, indexers, `IsDBNull`, `GetValues`, unsupported members). The per-type variable parts (`FieldCount`, the `GetValue`/`GetName`/`GetOrdinal`/`GetFieldType` switch arms, the element type `T`) are `[Inline(InlineOption.Syntax)]` / `Syntax<>` holes. Add the `static partial class Factory { }` target.
3. `ObjectReaderGenerator.cs` — replace the raw-`SyntaxFactory` reader construction with `Factory.*(...)` calls, supplying the holes from the `ObjectReaderModel`. **Where a `[Template]` hole genuinely cannot express a shape** (e.g. a switch whose arms are list-driven — try `Pattern.Replace` over a placeholder, or fall back), keep raw `SyntaxFactory` for that one piece and **log the drop** with a concrete "Synto could make this easier by …" note (R2, the experiment's payoff). The interceptor emission can stay raw `SyntaxFactory` (it's not the dog-food target).
4. Append the substantive findings to the friction log: what the template expressed cleanly, what it couldn't, every A→B drop, the `EquatableArray`/`DiagnosticInfo` hand-rolling (Task 2/3), and the interceptor plumbing (R1/R3).

- [ ] **Step 4: Run the tests + gate**

Run the green gate. If `NormalizeWhitespace()` output shifts, re-accept the snapshot golden after confirming it is still the locked §5 shape (same members, same interceptor, zero `NotImplementedException`, no `Synto.*` in output — C-6). Expected: all behavioral + diagnostics facts green; snapshot re-accepted; build 0 errors; format clean.

- [ ] **Step 5: Commit**

```bash
jj commit -m "refactor(objectreader): build the reader from a Synto [Template] skeleton (dog-food)"
```

---

### Task 5: Runnable Demo polish, README, friction-log finalize, mark spec delivered

**Files:**
- Modify: `.../Demo/Program.cs` (deterministic, readable output)
- Create: `examples/Synto.Example.ObjectReader/README.md`
- Modify: `docs/superpowers/notes/2026-06-22-objectreader-synto-friction.md` (summary + ranked top findings), `docs/superpowers/specs/2026-06-22-objectreader-example-design.md` (status → delivered)

**Interfaces:** Consumes the whole example (Tasks 1–4). Produces no new API.

- [ ] **Step 1: Add the runnable-Demo smoke check**

```csharp
// in ObjectReaderBehaviorTests.cs — proves the Demo's own call path is intercepted end-to-end
[Fact]
public void Demo_PrintsTwoDataRows()
{
    var people = Synto.Example.ObjectReader.Demo.Program.SampleData();
    using var reader = Synto.Example.ObjectReader.Api.ObjectReader.Create(people, "Name", "Age");
    int rows = 0;
    while (reader.Read()) rows++;
    Assert.Equal(people.Length, rows);
}
```

> Requires `Program.SampleData()` to be a `public static Person[]` the Demo also prints from. If exposing Demo internals to the test reads wrong, instead assert on the Demo via the existing behavioral tests and drop this — the Demo's value is being *runnable*, verified by `dotnet run`.

- [ ] **Step 2: Run to verify it fails**, then **Step 3: implement**

Make `Program.SampleData()` public; have `Program.Main` print the rows via the intercepted reader. Write `README.md`: what ObjectReader is (FastMember-via-source-gen), the one-line call, how it dog-foods Synto (`[Template]` skeleton + `Syntax<>` holes + `Synto.Diagnostics`), how to run (`dotnet run --project …Demo`), and a pointer to the friction log + spec. Finalize the friction log: a short summary + the ranked top "Synto could make this easier by …" findings that seed the later Synto-improvement spec (spec §11). Mark the spec `Status:` delivered.

- [ ] **Step 4: Final whole-repo gate + run the Demo**

Run the full green gate across the solution: `dotnet build -c Debug` (0 errors), `dotnet test --no-build -c Debug` (all green — the new example tests plus the pre-existing suite untouched), `dotnet format whitespace --verify-no-changes` (clean). Then `dotnet run --project examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Demo` and confirm it prints the rows (the runnable proof). Confirm the only new snapshot is the ObjectReader one; the Synto core snapshots are untouched (the example adds a *consumer*, it does not change Synto's surface).

- [ ] **Step 5: Commit**

```bash
jj commit -m "docs(objectreader): runnable Demo, README, friction log + spec delivered"
```

---

## Self-Review (against the spec)

- **§1 motivation / §12 concept (FastMember-equivalent, source-gen, no reflection)** → Task 2 (specialized reader, C-1) + Task 5 (Demo/README). ✓
- **D1 FastMember-identical `Create<T>`** → Task 1 (API signature). ✓
- **D2 interceptor dispatch** → Task 2 (`GetInterceptableLocation`, R1 spike). ✓
- **D3 heavy Synto, drop-on-wall** → Task 4 (`[Template]` skeleton; logged A→B drops). ✓
- **D4 / C-3 working reader (DataTable.Load, DBNull, no NotImplementedException)** → Task 2 (`Create_IsIntercepted…`, `Create_FeedsDataTableLoad`). ✓
- **D5 / C-2 diagnostics (SOR0001 warn + skip, SOR0002, SOR0000 never thrown)** → Task 3. ✓
- **C-5 cacheability (equatable model, nothing rooted, exceptions→SOR0000)** → Task 2 (`Model.cs`) + Task 3 (equatable diagnostics, exception guard). ✓
- **C-6 self-contained output (no `Synto.*`/reflection)** → Task 2/4 (snapshot acceptance criteria). ✓
- **§4 four-project layout, ns2.0 generator, net10 rest, co-located test (D6)** → Tasks 1–2 (csprojs + `Synto.slnx`). ✓
- **§8 testing (snapshot + behavioral + diagnostics + Demo)** → Tasks 2/3/5. ✓
- **R1 generic-call interception** → Task 2 Step 3 (spike-first). **R2 list-driven template holes** → Task 4. **R3 `<InterceptorsNamespaces>` plumbing** → Task 2 (Demo + Tests csproj). ✓
- **§11 friction log (phased)** → created Task 1, appended Tasks 2–4, finalized Task 5. ✓
- **§10 non-goals** (no `TypeAccessor`, no reflection fallback, no nested paths, **no Synto change**) → honored: only the `ObjectReader` half; un-intercepted throws (C-4); Synto untouched (only consumed). ✓

**Placeholder scan:** the only deliberately-open items are R1's interceptor signature and the GetSchemaTable-for-DataTable.Load detail — both pinned to a **test that forces the decision**, not left as "TODO". No bare "add error handling" / "similar to Task N". ✓

**Type consistency:** `ObjectReader.Create<T>(IEnumerable<T>, params string[])`, `ObjectReaderGenerator`, `ObjectReaderModel`/`ColumnInfo`/`EquatableArray<T>`/`DiagnosticInfo`, `GeneratorHarness.Verify`/`.Run`, `Synto.Example.ObjectReader.Generated`, `ObjectReader_{TypeShortName}_{N}`, `ObjectReaderInterceptors`, `SOR0000/0001/0002` used identically across tasks. ✓

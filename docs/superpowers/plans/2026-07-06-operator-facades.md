# Operator facades Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Retire the last hardwired-by-name facade (`TypeOf`) by generalizing `[SyntaxBuilder]` to
synthesize a *fixed-return* value facade into a *different* host type, then ship `typeof`/`nameof`/`sizeof`
as three ordinary `[SyntaxBuilder]`s in an internal `OperatorBuilders` type whose inert value facades are
synthesized into a hand-authored `Operators` shell.

**Architecture:** `[SyntaxBuilder]` gains two optional `System.Type` properties — `Returns` (the fixed
value-position return type of the synthesized facade) and `Facade` (the author-facing type the facade is
emitted into, a *different* type than the builder to dodge the CS0111 that a return-type-only twin would
cause in the builder's own type). `FacadeShape` learns the fixed-return mode; `SyntaxBuilderFacadeGenerator`
learns to emit into the `Facade` host. Operators then become plain builders recognized by the *existing*
structural SyntaxBuilder path, and the hardwired `TryBuiltIn`/`IsBuiltInFacadeName` arm in `FacadeCallFinder`
plus `Template.TypeOf`/`SyntoBuilders.TypeOf` are deleted. No `OperatorKind` enum, no generator `switch`, no
parallel recognition framework — the emit logic lives in each builder's method body.

**Tech Stack:** Roslyn incremental source generators (`netstandard2.0`, Roslyn 5.0 floor); C#; Verify
snapshot tests + `CSharpGeneratorDriver`; jj (Jujutsu) over git; MTP v2 (`dotnet test`).

**Design source:** `docs/superpowers/specs/2026-07-06-operator-facades-design.md` (approved 2026-07-06).
This plan implements exactly that shape; it does not redesign. Section references below (§3.2, §3.3, §5,
§8) point into that spec.

## Global Constraints

- **jj-native, off `main`.** All work lands on bookmark `experimental/operator-facades`. Never move
  `main` (memory: experimental-features-own-branch). Each task ends with a `rtk jj commit` carrying the
  `Plan:` / `Plan-Tasks:` trailers below. **This plan document only DESCRIBES those commit steps — do not
  execute any jj/git command while drafting; the implementer runs them.**
- **No git hooks.** Format via `dotnet format whitespace`; CI/green-gate is the enforcement (memory:
  no-git-pre-commit-hooks).
- **Test invocation (MTP / SDK 10):** raw `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug`.
  Not via rtk. Filter (if needed) via `--filter-method` / `--filter-class`, NOT `--filter` (memory:
  dotnet-test-mtp-invocation). The ObjectReader example has its own test project:
  `dotnet test --project examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Tests/Synto.Example.ObjectReader.Tests.csproj -c Debug`.
- **Cacheability is a hard invariant.** No `Compilation`/`ISymbol`/`SemanticModel`/`SyntaxNode` may enter
  cached pipeline state; all recognition/derivation stays inside the `ForAttributeWithMetadataName`
  transform, exactly as today. The existing `FacadeGenerator_IsIncremental_OnUnrelatedEdit` guard
  (`SyntaxBuilderTest.cs:1018`) must stay green.
- **`netstandard2.0` everywhere; failures are diagnostics, not exceptions.** A declaration-site problem is
  a located `DiagnosticInfo` (`SY*`), never a throw.
- **Generated OUTPUT is a one-way contract.** `typeof(<col type>)` in the ObjectReader's generated reader
  stays byte-identical. The factory *body* (the intermediate `…TypeOf(…)` builder call) may change its
  fully-qualified target (`SyntoBuilders.TypeOf` → `OperatorBuilders.TypeOf`); that is a benign snapshot
  re-accept, and the ObjectReader example itself snapshots only the final reader output (unchanged).

**Commit-message trailer block (append to every task's commit):**

```
Plan: docs/superpowers/plans/2026-07-06-operator-facades.md
Plan-Tasks: <this task's number>
```

---

## Orientation (read before starting)

**Surface authored in `src/Synto.Core/Templating/` (embedded + injected as `internal` by
`SurfaceInjectionGenerator`):**
- `Builders/Surface/SyntaxBuilderAttribute.cs` — the attribute to extend (currently empty; `public sealed
  class SyntaxBuilderAttribute : Attribute`). `Surface/*.cs` files are BOTH `<Compile>`d into `Synto.Core`
  and embedded under `Synto.Runtime.*` (see `src/Synto.Generators/Synto.Generators.csproj:48`).
- `Builders/InjectedOnly/SyntoBuilders.cs` — `Member` (the reference migrated builder, `[SyntaxBuilder]`)
  and `TypeOf(string) : ExpressionSyntax` (the emit builder to remove, `:41`). `InjectedOnly/*.cs` files are
  `<Compile Remove>`d from `Synto.Core` (`src/Synto.Core/Synto.Core.csproj:44`) and embedded under
  `Synto.Runtime.*` (`Synto.Generators.csproj:49`) — the injected internal copy is the only one.
- `Staging/Surface/Template.cs` — `TypeOf(string) : System.Type` inert value facade to remove (`:87`).

**Generator (`src/Synto.Generators/Templating/Builders/`):**
- `SyntaxBuilderFacadeGenerator.cs` — synthesizes each builder's inert facade. `Build` (`:61`) walks
  `builder.ContainingType`'s chain (`:94-104`) and reopens each as `partial` — the CS0111 placement
  constraint the spec calls out (§3.2). `ValidateDeclaration` (`:135`) returns a human reason for a
  declaration-site misuse → reported as **SY1031** (`InvalidBuilderDeclaration`). `FacadeMatchesBuilder`
  (`:168`) suppresses the facade when it would be signature-identical to the builder.
- `FacadeShape.cs` — `Derive` (`:95`) computes the facade's return: `[ReturnType]` param → that type,
  else `ExpressionSyntax` return → fresh `TResult` (`:224-228`), else concrete node verbatim. `Returns` is
  the new *fixed* return mode.
- `FacadeCallFinder.cs` — recognition. The **by-name `TypeOf` arm to DELETE**: `TryBuiltIn` (`:152-166`),
  `IsBuiltInFacadeName` (`:198-199`), the built-in branch (`:82-99`), and the `BuiltInBuilderType` const
  (`:31`). The **structural user-builder branch to KEEP** (`:106-139`) already claims `Member` and will
  claim the operators unchanged.
- `FacadeArgumentBinder.cs` — `Bind` (`:18`) derives the shape and emits `builderType =
  builder.ContainingType.ToDisplayString(FullyQualifiedFormat)` (`:110`); for an operator this is
  `OperatorBuilders`, so emit stays a fully-qualified static call.
- `../../Symbols/SymbolMetadataExtensions.cs` — `FindAttribute` (`:23`), `GetNamedTypeDisplay` (`:45`,
  returns a fully-qualified display string for a `System.Type`-valued named arg). A new
  `GetNamedType(AttributeData, string) : INamedTypeSymbol?` is needed for the `Facade` host (the generator
  needs the type SYMBOL to walk its namespace + containing chain, not just a display string).
- `../Diagnostics/TemplateDiagnostics.cs` — `InvalidBuilderDeclaration` (`:233`, **SY1031**). The
  `Returns`-without-`Facade` declaration error routes through this existing descriptor (a new reason
  string) — **no new SY id is allocated** (SY1031 is literally "Invalid [SyntaxBuilder] declaration '{0}':
  {1}").

**Injection wiring & snapshots:**
- `src/Synto.Generators/SurfaceInjectionGenerator.cs` — embeds every `Synto.Runtime.*` resource, rewrites
  `public`→`internal`, emits as post-init source. New `InjectedOnly/*.cs` files are picked up
  automatically by the `..\Synto.Core\**\InjectedOnly\*.cs` glob — no generator code change needed.
- `test/Synto.Test/snapshots/ResourceManifestPinTest.VerifyEmbeddedResourceManifest.verified.txt` — pins
  the exact embedded-resource set; **gains** `Synto.Runtime.OperatorBuilders.cs` and
  `Synto.Runtime.Operators.cs` (re-accept).
- `test/Synto.Test/SurfaceInjectionTest.cs` (`VerifyInjectedSurface`) — snapshots every injected file.
  `#SyntaxBuilderAttribute.g` changes (two new props); `#SyntoBuilders.g` and `#Template.g` lose `TypeOf`;
  new `#OperatorBuilders.g` and `#Operators.g` appear.

**Consumer dog-food & tests using `TypeOf`:**
- `examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Generator/ReaderTemplate.cs:8-9` —
  `using static Synto.Templating.Template;` + `using static Synto.Templating.SyntoBuilders;`. `:86` —
  `Template.TypeOf(c.ColumnTypeName)` (qualified today because both imports define `TypeOf`).
- `test/Synto.Test/Templating/InjectedSurfaceCompletenessTest.cs:135,150,120-127,581-584` — the
  `StagedRichTemplate` uses `Template.TypeOf("System.Int32")` and doc-comments about it.
- `test/Synto.Test/Templating/SyntaxBuilderTest.cs` — the builder test harness (`RunFacadeGeneratorSource`,
  `RunFacadeGeneratorDiagnostics`, `VerifyBuilderTemplate`, `RunAndGetGeneratedText`). Model the new tests
  on these.

**Diagnostic ledger:** `src/Synto.Generators/AnalyzerReleases.Unshipped.md` — no new rule row is added
(reusing SY1031). If the implementer nonetheless introduces a dedicated ID, SY1036 is the next free
sequential (SY1030 is an existing gap; prefer NOT to reuse it).

---

## Task 1 — `[SyntaxBuilder(Returns=, Facade=)]`: fixed-return facade synthesized into a different host

Add the two attribute properties, teach `FacadeShape` the fixed-return mode, teach the facade generator to
emit into the `Facade` host, and diagnose `Returns`-without-`Facade` at the declaration. This task is
purely *additive* — it introduces a capability exercised only by throwaway test builders, so the whole
existing suite stays green. Fold in the one expected surface-snapshot move (the attribute gains two props).

**Files:**
- Modify: `src/Synto.Core/Templating/Builders/Surface/SyntaxBuilderAttribute.cs`
- Modify: `src/Synto.Generators/Symbols/SymbolMetadataExtensions.cs` (add `GetNamedType`)
- Modify: `src/Synto.Generators/Templating/Builders/FacadeShape.cs` (`Derive`)
- Modify: `src/Synto.Generators/Templating/Builders/SyntaxBuilderFacadeGenerator.cs` (`Build`,
  `ValidateDeclaration`, the containing-chain emission)
- Test: `test/Synto.Test/Templating/SyntaxBuilderTest.cs`
- Re-accept: `test/Synto.Test/snapshots/SurfaceInjectionTest.VerifyInjectedSurface#SyntaxBuilderAttribute.g.verified.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces:
  - `SyntaxBuilderAttribute.Returns : System.Type?` and `SyntaxBuilderAttribute.Facade : System.Type?`
    (optional named properties on the method-level attribute).
  - `SymbolMetadataExtensions.GetNamedType(AttributeData attr, string name) : INamedTypeSymbol?` — the
    named `System.Type`-valued argument as a type symbol (or `null`).
  - `FacadeShape.Derive` honoring a fixed `Returns` type: when set, the derived
    `FacadeShape.ReturnTypeDisplay` is that type's fully-qualified display and `FreshReturnTypeParam` is
    `false` (no `TResult`).
  - `SyntaxBuilderFacadeGenerator` emitting the synthesized facade into the `Facade` type's
    namespace + containing chain + name when `Facade` is set (else today's behavior: the builder's own
    containing type).
  - **SY1031** `InvalidBuilderDeclaration` fired when `Returns` is set without `Facade`.

- [ ] **Step 1: Write the failing test — fixed-return facade lands in the Facade host**

Add to `SyntaxBuilderTest`. Uses the existing `RunFacadeGeneratorSource` helper (runs only
`SyntaxBuilderFacadeGenerator`, returns the synthesized source).

```csharp
// Operator-facade mechanism (spec §3.2/§3.3): [SyntaxBuilder(Returns=…, Facade=…)] synthesizes a
// FIXED-return value facade into a DIFFERENT host type than the builder — a same-signature twin in the
// builder's own type would be CS0111. Here the ExpressionSyntax builder `T` synthesizes a
// `System.Type`-returning facade emitted into `Values`, NOT into `Ops`.
[Fact]
public void FixedReturnFacade_SynthesizedIntoFacadeHost()
{
    var facadeSource = RunFacadeGeneratorSource(
        """
        using Synto.Templating;
        using Microsoft.CodeAnalysis.CSharp.Syntax;
        using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

        static partial class Ops {
            [SyntaxBuilder(Returns = typeof(System.Type), Facade = typeof(Values))]
            public static ExpressionSyntax T(string name) => TypeOfExpression(ParseTypeName(name));
        }

        static partial class Values {}
        """);

    // The facade returns the FIXED System.Type (no fresh TResult) ...
    Assert.Contains("System.Type T(string name)", facadeSource, StringComparison.Ordinal);
    Assert.DoesNotContain("TResult", facadeSource, StringComparison.Ordinal);
    // ... and is emitted into the Facade host `Values`, not the builder's own `Ops`.
    Assert.Contains("partial class Values", facadeSource, StringComparison.Ordinal);
    Assert.DoesNotContain("partial class Ops", facadeSource, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug --filter-method FixedReturnFacade_SynthesizedIntoFacadeHost`
Expected: FAIL — today the builder returns `ExpressionSyntax` so the facade gets a fresh `TResult`, and it
is emitted into `Ops` (the builder's own type). (`Returns`/`Facade` don't yet exist, so the source may not
even carry the named args — write them anyway; the property additions in Step 4 make it meaningful.)

- [ ] **Step 3: Write the failing declaration-diagnostic test — `Returns` without `Facade` → SY1031**

Uses the existing `RunFacadeGeneratorDiagnostics` helper.

```csharp
// spec §3.3: Returns set without Facade is a declaration-site error — a fixed-return facade placed in the
// builder's OWN type would be CS0111. The generator diagnoses it (SY1031) rather than emitting broken source.
[Fact]
public void FixedReturnWithoutFacade_ReportsSY1031()
{
    var diagnostics = RunFacadeGeneratorDiagnostics(
        """
        using Synto.Templating;
        using Microsoft.CodeAnalysis.CSharp.Syntax;
        using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

        static partial class Ops {
            [SyntaxBuilder(Returns = typeof(System.Type))]   // no Facade -> error
            public static ExpressionSyntax T(string name) => TypeOfExpression(ParseTypeName(name));
        }
        """);
    var diag = Assert.Single(diagnostics, d => d.Id == "SY1031");
    AssertHasRealSpan(diag);
}
```

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug --filter-method FixedReturnWithoutFacade_ReportsSY1031`
Expected: FAIL — no SY1031 today (the named arg is ignored, a `TResult` facade is synthesized into `Ops`).

- [ ] **Step 4: Add the two attribute properties**

Edit `SyntaxBuilderAttribute.cs` — add optional named properties (keep the existing doc, extend it):

```csharp
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class SyntaxBuilderAttribute : Attribute
{
    /// <summary>
    /// The fixed value-position return type of the synthesized facade (e.g. <c>typeof(System.Type)</c> for
    /// a <c>typeof</c>-style operator). Unset → today's behavior: an <c>ExpressionSyntax</c> builder gets a
    /// fresh generic <c>TResult</c> return, a <c>[ReturnType]</c> parameter supplies it, else the concrete
    /// node type is used. When set, <see cref="Facade"/> is required — a fixed-return facade shares the
    /// builder's exact signature except return type and would be CS0111 in the builder's own type.
    /// </summary>
    public Type? Returns { get; set; }

    /// <summary>
    /// The author-facing type the synthesized facade is emitted into. Unset → the builder's own containing
    /// type (today's behavior, valid when the facade differs by generic arity/return). Required whenever
    /// <see cref="Returns"/> is set.
    /// </summary>
    public Type? Facade { get; set; }
}
```

- [ ] **Step 5: Add the `GetNamedType` symbol helper**

Edit `SymbolMetadataExtensions.cs` — mirror `GetNamedTypeDisplay` but return the type symbol:

```csharp
public static INamedTypeSymbol? GetNamedType(AttributeData attr, string name)
{
    foreach (var kv in attr.NamedArguments)
    {
        if (kv.Key == name && kv.Value.Value is INamedTypeSymbol t)
            return t;
    }

    return null;
}
```

- [ ] **Step 6: Teach `FacadeShape.Derive` the fixed-`Returns` mode**

Edit `FacadeShape.Derive`. Near the top (after computing `returnDisplay`/`returnsExpression`/etc.), read the
builder's `[SyntaxBuilder]` attribute and its `Returns`:

```csharp
var builderAttr = SymbolMetadataExtensions.FindAttribute(
    builder, typeof(global::Synto.Templating.SyntaxBuilderAttribute).FullName!);
string? fixedReturnDisplay = builderAttr is null
    ? null
    : SymbolMetadataExtensions.GetNamedTypeDisplay(builderAttr, "Returns");
```

Then in the return-type decision block (currently `if (returnsType) … else if (returnTypeParamName …) …
else if (returnsExpression) … else …`), give the fixed return the highest precedence and reject a
conflicting `[ReturnType]`:

```csharp
bool freshReturnTypeParam = false;
string returnTypeDisplay;
if (fixedReturnDisplay is not null)
{
    if (returnTypeParamName is not null)
    {
        annotationError = "a [SyntaxBuilder] with Returns must not also have a [ReturnType] parameter";
        return null;
    }
    returnTypeDisplay = fixedReturnDisplay;   // fixed value type; no TResult, no [ReturnType]
}
else if (returnsType)
{
    returnTypeDisplay = "global::System.Type";
}
else if (returnTypeParamName is not null)
{
    returnTypeDisplay = returnTypeParamName;
}
else if (returnsExpression)
{
    freshReturnTypeParam = true;
    returnTypeDisplay = FreshReturnTypeParamName;
}
else
{
    returnTypeDisplay = returnDisplay;
}
```

(No other `FacadeShape` change: `FreshReturnTypeParam` stays `false` for the fixed case, so
`FacadeMatchesBuilder` sees `ReturnTypeDisplay = "global::System.Type"` ≠ the builder's `ExpressionSyntax`
return and correctly synthesizes a facade rather than suppressing it.)

- [ ] **Step 7: Teach `SyntaxBuilderFacadeGenerator` the `Facade` host + the `Returns`-without-`Facade` guard**

In `Build`, after `ValidateDeclaration`/`Derive`, resolve the emission host. Read the `Facade` symbol from
the builder attribute and, when present, walk ITS namespace + containing chain + name instead of the
builder's:

```csharp
var builderAttr = SymbolMetadataExtensions.FindAttribute(
    builder, typeof(global::Synto.Templating.SyntaxBuilderAttribute).FullName!);
INamedTypeSymbol host = builderAttr is not null
    && SymbolMetadataExtensions.GetNamedType(builderAttr, "Facade") is { } facadeHost
        ? facadeHost
        : builder.ContainingType;
```

Then replace the namespace + `chain` derivation (`SyntaxBuilderFacadeGenerator.cs:86-104`) so it is built
from `host` rather than `builder.ContainingType` (namespace from `host.ContainingNamespace`; chain by
walking `host` upward). The emission style is unchanged — each level is reopened as
`partial <keyword> <Name>` exactly as today (this is how `Member`'s facade already reopens the static
`SyntoBuilders`; a static `Facade` host behaves identically).

Add the declaration guard to `ValidateDeclaration` (it already returns a human reason routed to SY1031).
Read the attribute and reject `Returns` set without `Facade`:

```csharp
var attr = SymbolMetadataExtensions.FindAttribute(
    builder, typeof(global::Synto.Templating.SyntaxBuilderAttribute).FullName!);
if (attr is not null
    && SymbolMetadataExtensions.GetNamedTypeDisplay(attr, "Returns") is not null
    && SymbolMetadataExtensions.GetNamedType(attr, "Facade") is null)
{
    return "a [SyntaxBuilder] with a fixed Returns type must also set Facade "
         + "(a same-signature facade in the builder's own type would be CS0111)";
}
```

Also extend `ValidateDeclaration`'s partial/accessibility checks to cover the `Facade` host when set (walk
the host's containing chain the same way it walks the builder's), so a non-partial or inaccessible facade
host is reported as SY1031 rather than producing broken source.

- [ ] **Step 8: Run the two new tests + the full suite**

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug --filter-method FixedReturnFacade_SynthesizedIntoFacadeHost`
Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug --filter-method FixedReturnWithoutFacade_ReportsSY1031`
Expected: PASS both.
Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug`
Expected: PASS, except the one *expected* surface snapshot mismatch below.

- [ ] **Step 9: Re-accept the attribute surface snapshot**

`SurfaceInjectionTest.VerifyInjectedSurface#SyntaxBuilderAttribute.g` now shows the two new `Returns`/`Facade`
properties. Confirm the diff is exactly those two properties (+ doc) and accept it (move the received file
over the verified, per the repo's Verify workflow). No other injected-surface snapshot changes in this task.

- [ ] **Step 10: Format + commit**

Run: `dotnet format whitespace --verify-no-changes` (fix if dirty). Then describe (do NOT execute here):

```
rtk jj commit -m "feat(templating): add [SyntaxBuilder] Returns/Facade fixed-return facade synthesis

Plan: docs/superpowers/plans/2026-07-06-operator-facades.md
Plan-Tasks: 1"
```

---

## Task 2 — Introduce `OperatorBuilders`/`Operators` and inject them (TypeOf only, both paths coexist)

Author the internal emit-builder host `OperatorBuilders` with the `TypeOf` builder, plus the hand-authored
`Operators` shell the facade is synthesized into, and wire both into injection. `TypeOf` now exists on BOTH
paths (the legacy `Template.TypeOf`/`SyntoBuilders.TypeOf` still live; the new structural
`OperatorBuilders.TypeOf`), but no call site imports `Operators` yet, so nothing binds to the new path and
the suite stays green. This isolates the new surface + its snapshots from the recognition flip in Task 3.

**Files:**
- Create: `src/Synto.Core/Templating/Builders/InjectedOnly/OperatorBuilders.cs`
- Create: `src/Synto.Core/Templating/Builders/InjectedOnly/Operators.cs`
- Test: `test/Synto.Test/Templating/SyntaxBuilderTest.cs`
- Re-accept: `ResourceManifestPinTest.VerifyEmbeddedResourceManifest.verified.txt`
- New snapshots: `SurfaceInjectionTest.VerifyInjectedSurface#OperatorBuilders.g.verified.cs`,
  `…#Operators.g.verified.cs`

**Interfaces:**
- Consumes: `[SyntaxBuilder(Returns=, Facade=)]` + fixed-return facade synthesis (Task 1).
- Produces:
  - `internal static partial class Synto.Templating.OperatorBuilders` with
    `internal static ExpressionSyntax TypeOf(string name)` marked
    `[SyntaxBuilder(Returns = typeof(System.Type), Facade = typeof(Operators))]`.
  - `public static partial class Synto.Templating.Operators` (empty shell; injected `internal`; the
    `TypeOf` value facade is synthesized into it by `SyntaxBuilderFacadeGenerator`).

- [ ] **Step 1: Write the failing test — the operator surface injects and the value facade synthesizes**

Add to `SyntaxBuilderTest`. This proves (a) the injected surface now carries `OperatorBuilders` with a
`[SyntaxBuilder]` `TypeOf`, and (b) a `System.Type`-returning `TypeOf` facade is synthesized into
`Operators` so a bare `using static …Operators; TypeOf(name)` type-checks in value position.

```csharp
// Task 2: the injected operator surface exists and its inert value facade synthesizes. A carrier that
// imports `using static …Operators` and writes `System.Type t = TypeOf("System.Int32");` type-checks
// against the synthesized `Operators.TypeOf(string) : System.Type`, and the factory rewrites the call to
// the fully-qualified `OperatorBuilders.TypeOf(...)`. Uses the three-generator driver like
// BuiltInBuilderOutput_CompilesAlongsidePublicSyntoCore.
[Fact]
public void OperatorSurface_TypeOf_Synthesizes_And_Compiles()
{
    const string source =
        """
        using Synto.Templating;
        using static Synto.Templating.Operators;
        partial class Factory {}
        public class TestClass {
            [Template<Factory>]
            System.Type Build(int i) => TypeOf("System.Int32");
        }
        """;

    var compilation = CompilationWithSource(source);
    var driver = CSharpGeneratorDriver.Create(
        new SurfaceInjectionGenerator(),
        new SyntaxBuilderFacadeGenerator(),
        new TemplateFactorySourceGenerator());
    driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var genDiagnostics, TestContext.Current.CancellationToken);

    Assert.Empty(genDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
    var errors = output.GetDiagnostics(TestContext.Current.CancellationToken)
        .Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
    Assert.True(errors.Count == 0,
        "Post-generation compilation reported errors: " + string.Join("; ", errors.Select(d => d.Id + " " + d.GetMessage(System.Globalization.CultureInfo.InvariantCulture))));
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug --filter-method OperatorSurface_TypeOf_Synthesizes_And_Compiles`
Expected: FAIL — `Operators`/`OperatorBuilders` don't exist yet, so `using static …Operators` and `TypeOf`
don't resolve (CS-level errors in the post-gen compile).

- [ ] **Step 3: Author `OperatorBuilders`**

Create `src/Synto.Core/Templating/Builders/InjectedOnly/OperatorBuilders.cs`. Author `public`-free
(internal host authors never import it); the `SyntoBuilders` file is the template for the resource
conventions (usings, `namespace Synto.Templating`, `internal static partial`):

```csharp
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto.Templating;

/// <summary>
/// The factory-time emit builders for C#'s value-producing keyword operators (<c>typeof</c>/<c>nameof</c>/
/// <c>sizeof</c>). Each is an ordinary <c>[SyntaxBuilder]</c>: it runs at factory-build time and returns the
/// operator expression. Their inert value-position facades are synthesized into <see cref="Operators"/> (a
/// DIFFERENT type — a same-signature facade here would be CS0111), which authors import via
/// <c>using static …Operators</c> and call by bare name. Injected <c>internal</c> into the consumer's
/// generator compilation by <c>SurfaceInjectionGenerator</c>; authors never import THIS type.
/// </summary>
internal static partial class OperatorBuilders
{
    /// <summary>Builds <c>typeof(name)</c> from a factory-time type name.</summary>
    [SyntaxBuilder(Returns = typeof(System.Type), Facade = typeof(Operators))]
    internal static ExpressionSyntax TypeOf(string name) => TypeOfExpression(ParseTypeName(name));
}
```

- [ ] **Step 4: Author the `Operators` shell**

Create `src/Synto.Core/Templating/Builders/InjectedOnly/Operators.cs`. It must be `partial` (so the
synthesized facade can reopen it) and `public` here (injected → `internal` by the rewriter):

```csharp
namespace Synto.Templating;

/// <summary>
/// The author-facing value view of the operator builders (<see cref="OperatorBuilders"/>). Authors write
/// <c>using static Synto.Templating.Operators;</c> and bare <c>TypeOf(...)</c> / <c>NameOf(...)</c> /
/// <c>SizeOf(...)</c>. This shell is empty: the inert, fixed-return value facades are SYNTHESIZED into it by
/// <c>SyntaxBuilderFacadeGenerator</c> from the <c>[SyntaxBuilder(Returns=…, Facade=typeof(Operators))]</c>
/// builders. Authored once here, injected <c>internal</c> into the consumer compilation.
/// </summary>
public static partial class Operators
{
}
```

Both files live under an `InjectedOnly/` folder, so `Synto.Core.csproj`'s `<Compile Remove="**\InjectedOnly\*.cs" />`
keeps them out of the `Synto.Core` compile, and `Synto.Generators.csproj`'s
`..\Synto.Core\**\InjectedOnly\*.cs` glob embeds them under `Synto.Runtime.OperatorBuilders.cs` /
`Synto.Runtime.Operators.cs`. **No csproj or generator edit is required** — verify this by inspection (the
glob + the mirror-remove already exist).

- [ ] **Step 5: Run the new test + full suite; re-accept the injection snapshots**

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug --filter-method OperatorSurface_TypeOf_Synthesizes_And_Compiles`
Expected: PASS.
Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug`
Expected: PASS except the *expected* new/changed snapshots:
- `ResourceManifestPinTest.VerifyEmbeddedResourceManifest` — gains `Synto.Runtime.OperatorBuilders.cs` and
  `Synto.Runtime.Operators.cs`. Accept.
- `SurfaceInjectionTest.VerifyInjectedSurface` — two NEW files `#OperatorBuilders.g` and `#Operators.g`
  (the `internal`-rewritten shells). Accept both. Confirm `#SyntoBuilders.g` / `#Template.g` still carry the
  legacy `TypeOf` (unchanged in this task).

Verify no other snapshot moved and no error diagnostics appeared. (In particular, `OperatorBuilders.TypeOf`
being registered as a builder must NOT trip SY1018 against anything — its facade name `TypeOf` is unique in
the registry; the legacy `Template.TypeOf`/`SyntoBuilders.TypeOf` are recognized by BINDING, not registered
structurally, so there is no name collision.)

- [ ] **Step 6: Format + commit**

Run: `dotnet format whitespace --verify-no-changes`. Then describe (do NOT execute):

```
rtk jj commit -m "feat(templating): add OperatorBuilders/Operators surface with typeof builder

Plan: docs/superpowers/plans/2026-07-06-operator-facades.md
Plan-Tasks: 2"
```

---

## Task 3 — Migrate `TypeOf` onto the structural path; delete the by-name arm and the legacy facades

Flip `TypeOf` recognition to the structural SyntaxBuilder path and remove the hardwired machinery: delete
`Template.TypeOf`, `SyntoBuilders.TypeOf`, and `FacadeCallFinder`'s `TryBuiltIn` / `IsBuiltInFacadeName` /
built-in branch / `BuiltInBuilderType`. Repoint every `TypeOf` call site (ObjectReader dog-food +
`InjectedSurfaceCompletenessTest`) to bare `TypeOf(...)` via `using static …Operators`. Do all three in one
task — leaving any subset in place either double-recognizes the call or leaves a dead facade. The generated
OUTPUT (`typeof(<type>)`) is unchanged; only the factory-body target and injected-surface snapshots move.

**Files:**
- Modify: `src/Synto.Generators/Templating/Builders/FacadeCallFinder.cs` (delete the built-in arm)
- Modify: `src/Synto.Core/Templating/Builders/InjectedOnly/SyntoBuilders.cs` (delete `TypeOf`; refresh doc)
- Modify: `src/Synto.Core/Templating/Staging/Surface/Template.cs` (delete `TypeOf`; refresh doc)
- Modify: `examples/…/ReaderTemplate.cs` (`using static …Operators`; bare `TypeOf`)
- Modify: `test/Synto.Test/Templating/InjectedSurfaceCompletenessTest.cs` (`StagedRichTemplate` + docs)
- Test: `test/Synto.Test/Templating/SyntaxBuilderTest.cs` (recognition-not-by-name pin)
- Re-accept: `#SyntoBuilders.g`, `#Template.g` surface snapshots; any factory-body snapshot carrying
  `SyntoBuilders.TypeOf`.

**Interfaces:**
- Consumes: the `OperatorBuilders.TypeOf` structural builder (Task 2).
- Produces: `TypeOf` recognized exclusively via the structural registry; `FacadeCallFinder` no longer
  references `Template`, `TypeOf`, `TryBuiltIn`, `IsBuiltInFacadeName`, or `BuiltInBuilderType`.

- [ ] **Step 1: Write the failing test — recognition is by attribute, not by hardcoded name**

Add to `SyntaxBuilderTest`. Two assertions in one behavioral pin: a bare `TypeOf(...)` is recognized and
rewritten to `OperatorBuilders.TypeOf` (the structural registry claimed it), and the generator source no
longer contains the hardwired name-matching machinery (a light source-level guard that the by-name arm is
gone).

```csharp
// spec §5: TypeOf rides the structural [SyntaxBuilder] path — recognized because OperatorBuilders.TypeOf is
// a registered builder, NOT because the finder hardcodes the name "TypeOf". The rewritten factory calls the
// fully-qualified OperatorBuilders.TypeOf; and no facade is recognized by a hardcoded name any more.
[Fact]
public void TypeOf_RecognizedStructurally_NotByHardcodedName()
{
    var generated = RunAndGetGeneratedTextWithInjectedSurface(
        """
        using Synto.Templating;
        using static Synto.Templating.Operators;
        partial class Factory {}
        public class TestClass {
            [Template<Factory>]
            System.Type Build(int i) => TypeOf("System.Int32");
        }
        """);

    // Rewritten to the structural builder target (OperatorBuilders), not the deleted SyntoBuilders.TypeOf.
    Assert.Contains("OperatorBuilders.TypeOf", generated, StringComparison.Ordinal);
    Assert.DoesNotContain("SyntoBuilders.TypeOf", generated, StringComparison.Ordinal);
}
```

Also add a source-guard test that the by-name arm is physically gone (reads the generator source file via
reflection-free means is awkward; instead assert the behavior above AND rely on the compile removing
`IsBuiltInFacadeName`). If a source-text guard is desired, prefer a focused reflection check that
`FacadeCallFinder` exposes no member named `TryBuiltIn`/`IsBuiltInFacadeName`:

```csharp
[Fact]
public void FacadeCallFinder_HasNoHardcodedBuiltInArm()
{
    var t = typeof(TemplateFactorySourceGenerator).Assembly
        .GetType("Synto.Generators.Templating.Builders.FacadeCallFinder", throwOnError: true)!;
    var names = t.GetMethods(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance)
        .Select(m => m.Name).ToList();
    Assert.DoesNotContain("TryBuiltIn", names);
    Assert.DoesNotContain("IsBuiltInFacadeName", names);
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug --filter-method FacadeCallFinder_HasNoHardcodedBuiltInArm`
Expected: FAIL — `TryBuiltIn` / `IsBuiltInFacadeName` still exist on `FacadeCallFinder`.

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug --filter-method TypeOf_RecognizedStructurally_NotByHardcodedName`
Note: this one may ALREADY pass after Task 2 — a bare `TypeOf(...)` imported via `using static …Operators`
is UNBOUND at analysis time (the `Operators` shell is empty and the synthesized facade is invisible to the
analyzing generator), so the structural branch already claims it and emits `OperatorBuilders.TypeOf`. It is
a REGRESSION PIN that the migration preserves structural recognition, not a red-first driver. The red-first
driver for this task is the reflection guard above plus the compile break in Steps 5–6: once the legacy arm
and `Template.TypeOf` are deleted (Steps 3–4), the still-qualified `Template.TypeOf(...)` call sites
(ObjectReader `ReaderTemplate.cs`, `InjectedSurfaceCompletenessTest.StagedRichTemplate`) fail to compile
until repointed to bare `TypeOf(...)`.

- [ ] **Step 3: Delete the by-name arm in `FacadeCallFinder`**

In `FacadeCallFinder.cs`:
- Delete the built-in branch in `VisitInvocationExpression` (`:82-99`) — the whole
  `if (_templateSymbol is not null && … IsBuiltInFacadeName(bound.Name)) { … }` block.
- Delete `TryBuiltIn` (`:152-166`) and `IsBuiltInFacadeName` (`:198-199`).
- Delete the `BuiltInBuilderType` const (`:31`) and the `_templateSymbol` field + its initialization in
  the constructor (`:34`, `:46`) — they become unused.
- Update the class `<summary>` (`:12-28`): drop the "recognized by binding … `Template.TypeOf`" rationale;
  state that ALL facades (including the operators) ride the structural SyntaxBuilder path.

The structural branch (`:106-139`) is untouched — it already claims `TypeOf` because
`OperatorBuilders.TypeOf` is in `_buildersByName` and a bare `TypeOf(...)` is UNBOUND during analysis (the
`Operators` shell is empty and the synthesized facade is invisible to the analyzing generator, exactly like
`Member`).

- [ ] **Step 4: Delete the legacy `TypeOf` facades**

- In `SyntoBuilders.cs`, delete the `TypeOf(string name) => TypeOfExpression(ParseTypeName(name))` method
  (`:26-41`, including its long doc). Update the class `<summary>` (`:8-17`) to drop the
  "only `Template.TypeOf` remains hardwired" clause — nothing is hardwired now; `Member` and the operators
  all ride the structural path.
- In `Template.cs`, delete the `TypeOf(string name) : System.Type` inert facade (`:71-87`, including its
  doc). Leave `Unquote`/`Quote`/`Splice` untouched.

- [ ] **Step 5: Repoint the ObjectReader dog-food**

In `examples/…/ReaderTemplate.cs`:
- Add `using static Synto.Templating.Operators;` to the using block (`:8-9` area). Keep
  `using static Synto.Templating.SyntoBuilders;` (still needed for `Member`/`Switch`/`Case`/`Return`).
- Change `return Template.TypeOf(c.ColumnTypeName);` (`:86`) to `return TypeOf(c.ColumnTypeName);` and
  update the trailing comment (the qualification rationale is gone; `Operators.TypeOf` is the only bare
  `TypeOf` in scope now that `Template.TypeOf` is deleted).

- [ ] **Step 6: Repoint `InjectedSurfaceCompletenessTest`**

In `StagedRichTemplate` (`InjectedSurfaceCompletenessTest.cs:129-156`):
- Add `using static Synto.Templating.Operators;` to the template's using block.
- Change `var t = Template.TypeOf("System.Int32");` (`:150`) to `var t = TypeOf("System.Int32");` and drop
  the "qualified" comment.
- Update the doc-comments that describe `TypeOf` as recognized-by-binding (`:120-127`, `:581-584`) to say it
  now rides the structural path (like `Member`), so the synthesized `Operators.TypeOf` facade is what the
  ORIGINAL call type-checks against, supplied by `SyntaxBuilderFacadeGenerator` (already in this test's
  driver at `:585-588`).

- [ ] **Step 7: Run the full suite; re-accept the moved snapshots**

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug`
Expected PASS with these *expected* moves:
- `SurfaceInjectionTest.VerifyInjectedSurface#SyntoBuilders.g` — loses `TypeOf`. Accept.
- `…#Template.g` — loses `TypeOf`. Accept.
- Any factory-body snapshot that emitted `global::Synto.Templating.SyntoBuilders.TypeOf(...)` now emits
  `…OperatorBuilders.TypeOf(...)` — a benign target move; the surrounding generated code and the final
  `typeof(...)` output are otherwise identical. Diff each to CONFIRM only the `SyntoBuilders`→
  `OperatorBuilders` segment changed, then accept.

If any OTHER factory snapshot or the ObjectReader `#ObjectReader.g.verified.cs` output changed, STOP and
diagnose — the generated reader's `typeof(<type>)` must be byte-identical (memory:
generated-output-semantic-not-byte-identical allows benign re-accepts, but a changed `typeof` payload is a
regression).

- [ ] **Step 8: Run the ObjectReader example tests**

Run: `dotnet test --project examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Tests/Synto.Example.ObjectReader.Tests.csproj -c Debug`
Expected: PASS with the reader snapshot unchanged (the generated `GetFieldType` still returns
`typeof(<col type>)`). The behavior tests (round-trip through the generated reader) prove semantic
equivalence.

- [ ] **Step 9: Format + commit**

Run: `dotnet format whitespace --verify-no-changes`. Then describe (do NOT execute):

```
rtk jj commit -m "feat(templating): migrate typeof onto structural SyntaxBuilder path; delete by-name arm

Plan: docs/superpowers/plans/2026-07-06-operator-facades.md
Plan-Tasks: 3"
```

---

## Task 4 — `NameOf` operator (`nameof(...)`)

Add `NameOf` as a second operator builder, proving the mechanism is general (not `typeof`-shaped) and
covering the spec's high-demand case (generated code uses `nameof` constantly). One builder method + one
attribute line; the facade synthesizes into `Operators` automatically.

**Files:**
- Modify: `src/Synto.Core/Templating/Builders/InjectedOnly/OperatorBuilders.cs`
- Test: `test/Synto.Test/Templating/SyntaxBuilderTest.cs`
- Re-accept: `#OperatorBuilders.g`, `#Operators.g` surface snapshots.

**Interfaces:**
- Produces: `internal static ExpressionSyntax OperatorBuilders.NameOf(string name)` marked
  `[SyntaxBuilder(Returns = typeof(string), Facade = typeof(Operators))]`, with a synthesized
  `string Operators.NameOf(string)` value facade.

- [ ] **Step 1: Write the failing output test**

```csharp
// spec §2/§5: NameOf("Foo.Bar") emits `nameof(Foo.Bar)` in output, and the value facade returns `string`.
[Fact]
public void NameOf_EmitsNameofExpression()
{
    var generated = RunAndGetGeneratedTextWithInjectedSurface(
        """
        using Synto.Templating;
        using static Synto.Templating.Operators;
        partial class Factory {}
        public class TestClass {
            [Template<Factory>]
            string Build() => NameOf("Foo.Bar");
        }
        """);

    // The factory rewrites the call to the fully-qualified builder ...
    Assert.Contains("OperatorBuilders.NameOf", generated, StringComparison.Ordinal);
}
```

Add a companion end-to-end proof modeled on `UserBuilder_Executed_QuotedArgumentReachesBuilder`
(`SyntaxBuilderTest.cs:1064`): emit the generated assembly, invoke the factory, render the returned node,
and assert `Assert.Contains("nameof(Foo.Bar)", rendered)`. This pins the actual OUTPUT (not just the rewrite
target).

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug --filter-method NameOf_EmitsNameofExpression`
Expected: FAIL — `NameOf` is not a registered builder; the call does not resolve.

- [ ] **Step 3: Add the `NameOf` builder**

In `OperatorBuilders.cs`, add:

```csharp
/// <summary>Builds <c>nameof(name)</c> from a factory-time name expression.</summary>
[SyntaxBuilder(Returns = typeof(string), Facade = typeof(Operators))]
internal static ExpressionSyntax NameOf(string name) =>
    InvocationExpression(IdentifierName("nameof"),
        ArgumentList(SingletonSeparatedList(Argument(ParseExpression(name)))));
```

- [ ] **Step 4: Run the tests + full suite; re-accept snapshots**

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug`
Expected: PASS; `#OperatorBuilders.g` and `#Operators.g` gain `NameOf` (accept). Confirm the rendered-output
assertion (`nameof(Foo.Bar)`) passes.

- [ ] **Step 5: Format + commit**

Run: `dotnet format whitespace --verify-no-changes`. Then describe (do NOT execute):

```
rtk jj commit -m "feat(templating): add NameOf operator (nameof)

Plan: docs/superpowers/plans/2026-07-06-operator-facades.md
Plan-Tasks: 4"
```

---

## Task 5 — `SizeOf` operator (`sizeof(...)`)

Complete the closed family with `SizeOf`, delivering all three operators the spec scopes (§7). Same shape as
`NameOf`: one builder method + one facade.

**Files:**
- Modify: `src/Synto.Core/Templating/Builders/InjectedOnly/OperatorBuilders.cs`
- Test: `test/Synto.Test/Templating/SyntaxBuilderTest.cs`
- Re-accept: `#OperatorBuilders.g`, `#Operators.g` surface snapshots.

**Interfaces:**
- Produces: `internal static ExpressionSyntax OperatorBuilders.SizeOf(string name)` marked
  `[SyntaxBuilder(Returns = typeof(int), Facade = typeof(Operators))]`, with a synthesized
  `int Operators.SizeOf(string)` value facade.

- [ ] **Step 1: Write the failing output test**

```csharp
// spec §2/§5: SizeOf("int") emits `sizeof(int)` in output, and the value facade returns `int`.
[Fact]
public void SizeOf_EmitsSizeofExpression()
{
    var generated = RunAndGetGeneratedTextWithInjectedSurface(
        """
        using Synto.Templating;
        using static Synto.Templating.Operators;
        partial class Factory {}
        public class TestClass {
            [Template<Factory>]
            int Build() => SizeOf("int");
        }
        """);

    Assert.Contains("OperatorBuilders.SizeOf", generated, StringComparison.Ordinal);
}
```

Add the end-to-end rendered-output proof (as in Task 4) asserting `Assert.Contains("sizeof(int)", rendered)`.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug --filter-method SizeOf_EmitsSizeofExpression`
Expected: FAIL — `SizeOf` is not a registered builder.

- [ ] **Step 3: Add the `SizeOf` builder**

In `OperatorBuilders.cs`, add:

```csharp
/// <summary>Builds <c>sizeof(name)</c> from a factory-time type name.</summary>
[SyntaxBuilder(Returns = typeof(int), Facade = typeof(Operators))]
internal static ExpressionSyntax SizeOf(string name) => SizeOfExpression(ParseTypeName(name));
```

- [ ] **Step 4: Run the tests + full suite; re-accept snapshots**

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug`
Expected: PASS; `#OperatorBuilders.g` and `#Operators.g` gain `SizeOf` (accept). Confirm the rendered-output
assertion (`sizeof(int)`) passes.

- [ ] **Step 5: Format + commit**

Run: `dotnet format whitespace --verify-no-changes`. Then describe (do NOT execute):

```
rtk jj commit -m "feat(templating): add SizeOf operator (sizeof)

Plan: docs/superpowers/plans/2026-07-06-operator-facades.md
Plan-Tasks: 5"
```

---

## Verification (whole change)

- **Recognition is by attribute, not by name.** `FacadeCallFinder` has no `TryBuiltIn` /
  `IsBuiltInFacadeName` / `BuiltInBuilderType` / `_templateSymbol` (grep is clean;
  `FacadeCallFinder_HasNoHardcodedBuiltInArm` green). All three operators are recognized through the
  structural registry.
- **Surface moved as designed.** `#Template.g` and `#SyntoBuilders.g` no longer contain `TypeOf`;
  `#OperatorBuilders.g` (three `[SyntaxBuilder]` operators) and `#Operators.g` (empty shell) exist;
  `#SyntaxBuilderAttribute.g` carries `Returns`/`Facade`; `ResourceManifestPinTest` lists
  `Synto.Runtime.OperatorBuilders.cs` + `Synto.Runtime.Operators.cs`.
- **Output is unchanged for `typeof`.** The ObjectReader reader snapshot
  (`ObjectReaderSnapshotTests…#ObjectReader.g.verified.cs`) still emits `typeof(<col type>)`; only the
  factory-body target moved to `OperatorBuilders.TypeOf`. ObjectReader behavior tests green.
- **`nameof`/`sizeof` correctness proven.** Rendered-output tests assert `nameof(Foo.Bar)` and `sizeof(int)`.
- **Cacheability preserved.** `FacadeGenerator_IsIncremental_OnUnrelatedEdit` green; no
  `Compilation`/`ISymbol`/`SemanticModel`/`SyntaxNode` entered cached state (all reads stay in the
  `ForAttributeWithMetadataName` transform).
- **No new diagnostic ID.** `Returns`-without-`Facade` routes through the existing SY1031; the
  `AnalyzerReleases.Unshipped.md` ledger is unchanged.
- `dotnet format whitespace --verify-no-changes` clean; full `Synto.Test` + ObjectReader tests green.
- **Follow-up (not a plan task):** run the `refresh-kb` skill to drop `Template.TypeOf`/`SyntoBuilders.TypeOf`
  from the KB surface listings (`docs/kb/subsystems/templating.md`, `docs/kb/glossary.md`) and add the
  `Operators`/`OperatorBuilders` pair + the `[SyntaxBuilder(Returns=,Facade=)]` properties.

## Open questions carried from the spec (§8) — noted, not blocking

- **`Facade` defaulting.** This plan requires explicit `Facade =` (SY1031 if `Returns` is set without it),
  matching the spec default. Inferring a conventional facade host from `Returns` is deliberately out of
  scope.
- **Host-type naming.** `Operators` (author-facing) and `OperatorBuilders` (internal) are the spec's
  proposed names and the reorg's role-folder home is `Templating/Builders/InjectedOnly/`. If a reviewer
  prefers a different pairing, it is a mechanical rename confined to Task 2.
- **Exact recognition wiring.** This plan pins the BEHAVIOR (bare unbound `TypeOf`/`NameOf`/`SizeOf` claimed
  by the structural registry, exactly like `Member`) and leaves the implementer to match `Member`'s
  recognition code precisely — the structural branch in `FacadeCallFinder` is reused verbatim.

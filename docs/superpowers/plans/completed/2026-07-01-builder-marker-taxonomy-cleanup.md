# Builder Marker-Taxonomy Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the `[Quoted(AsTypeArg = true)]` bool with a dedicated `[TypeArg]` parameter attribute (a sibling of `[Quoted]`, carrying an optional `Name` and `Constraint`), make `[ReturnType]` require `[TypeArg]`, and support a generic `where T : Constraint` clause on the facade — a behavior-preserving cleanup of the `[SyntaxBuilder]` parameter-marker surface plus one small additive capability.

**Architecture:** `[SyntaxBuilder]` facades are reverse-engineered from the builder impl's parameter markers by `FacadeShape.Derive` (source-generator side). Today a `bool AsTypeArg` on `[Quoted]` silently reinterprets a `TypeSyntax` *value* parameter as a facade *generic* type parameter, an encoding that needs runtime validation to police illegal combos. This plan lands the new `[TypeArg]` marker additively first (dual-recognized alongside the legacy bool, proven by a new test), then flips every consumer to `[TypeArg]` and deletes `AsTypeArg`. The generated facade (`T Cast<T>(object x)`) and every consumer call site (`Cast<int>(x)`) are unchanged — only the impl-side marker and the injected marker surface change. A final task then adds an optional `Constraint` on `[TypeArg]` — the one place the facade emitter gains a `where`-clause — additive and byte-identical for unconstrained builders.

**Tech Stack:** C# / Roslyn incremental source generators (`netstandard2.0`), xUnit v3 on Microsoft.Testing.Platform (`net10.0`), Verify snapshot testing, jj (Jujutsu) VCS.

## Global Constraints

- **Behavior-preserving.** The consumer call site `Cast<int>(x)` is unchanged; the synthesized facade stays `T Cast<T>(object x)`. Only the impl-side marker migrates and the injected marker surface changes. Generated-factory snapshots (`SyntaxBuilderTest.*`) must stay byte-identical.
- **Incremental cacheability.** No `ISymbol`/`SemanticModel` may enter pipeline *state*. All marker reading stays inside the generator transform (`FacadeShape.Derive` / `FacadeArgumentBinder.Bind`), exactly as today — nodes in, nodes out.
- **Single source of truth for the marker surface.** Consumer-facing marker types are authored once in `src/Synto/Templating/...` and injected `internal` into consumers by `SurfaceInjectionGenerator`; a new marker MUST be added as an `<EmbeddedResource>` in `src/Synto.SourceGenerator/Synto.SourceGenerator.csproj` with a `Synto.Runtime.<Name>.cs` LogicalName, or it will not be injected.
- **VCS is jj, not git.** Use `jj commit` / `jj status`. Never `git add`. **Two pre-existing CRLF-only snapshots** under `test/Synto.Diagnostics.Test/snapshots/*DiagnosticsAttribute.g.verified.cs` are EOL noise that jj does not track — never stage them.
- **Tests run under MTP.** Use raw `dotnet test --project <csproj>` (do NOT route `dotnet test` through `rtk` — it mangles MTP args). The full `Synto.Test` project run is the green gate.
- **Snapshot accept is mechanical.** A surface change makes `SurfaceInjectionTest` fail and write `*.received.cs` next to `*.verified.cs`. Accept by overwriting `.verified.cs` with `.received.cs`. Run the failing test with `DiffEngine_Disabled=true` so no diff tool launches.
- **Format gate:** `dotnet format whitespace --verify-no-changes` must be clean (whitespace scope only).
- **`[Quoted].As` is KEPT** (orthogonal, harmless, preserves behavior).

---

### Task 1: Introduce `[TypeArg]` additively (dual-recognized with legacy `AsTypeArg`)

Add the new `[TypeArg]` parameter attribute, inject it into the consumer surface, and teach `FacadeShape.Derive` to recognize it — while the legacy `[Quoted(AsTypeArg = true)]` path keeps working so every existing test stays green. Proven by a new post-generation compile test that also exercises the `Name` override.

**Files:**
- Create: `src/Synto/Templating/Builders/TypeArgAttribute.cs`
- Modify: `src/Synto.SourceGenerator/Synto.SourceGenerator.csproj` (add one `<EmbeddedResource>` after the `ReturnTypeAttribute.cs` line, ~line 53)
- Modify: `src/Synto.SourceGenerator/Shared/SymbolMetadataExtensions.cs` (add `GetNamedString`)
- Modify: `src/Synto.SourceGenerator/Templating/Builders/FacadeShape.cs` (replace the `foreach (var p in builder.Parameters)` loop body, lines 87-130)
- Test: `test/Synto.Test/Templating/SyntaxBuilderTest.cs` (add one `[Fact]`)
- Snapshot (new, accepted mechanically): `test/Synto.Test/snapshots/SurfaceInjectionTest.VerifyInjectedSurface#TypeArgAttribute.g.verified.cs`

**Interfaces:**
- Produces:
  - `public sealed class Synto.Templating.TypeArgAttribute : Attribute` with `public string? Name { get; set; }`, `[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]`.
  - `SymbolMetadataExtensions.GetNamedString(AttributeData attr, string name) -> string?`.
  - `FacadeShape.Derive` recognizes a `[TypeArg]` parameter as `BuilderArgKind.QuotedTypeArg`, generic name = `TypeArg.Name ?? Pascal(parameterName)`.
- Consumes (unchanged): `BuilderArgKind.QuotedTypeArg`, `FacadeParam`, `SymbolMetadataExtensions.{FindAttribute,GetNamedBool,GetNamedTypeDisplay}`.

- [ ] **Step 1: Write the failing test**

Add this `[Fact]` to `test/Synto.Test/Templating/SyntaxBuilderTest.cs` (e.g. after `UserSyntaxBuilder_IsDiscoveredAndInvoked`, ~line 164). It is compile-only (no snapshot) and exercises both `[TypeArg]` and its `Name` override:

```csharp
    // [TypeArg] surfaces a facade generic type parameter; Name decouples the facade type-param name from the
    // builder parameter name. Proven by post-generation compile: the synthesized `TResult Cast<TResult>(object x)`
    // resolves the consumer's `Cast<int>(x)`. Compile-only (no snapshot).
    [Fact]
    public void TypeArg_WithName_SynthesizesGenericFacadeThatCompiles()
    {
        const string source =
            """
            using Synto.Templating;
            using Microsoft.CodeAnalysis.CSharp.Syntax;
            using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
            partial class Factory {}

            static partial class MyBuilders {
                [SyntaxBuilder]
                public static ExpressionSyntax Cast([TypeArg(Name = "TResult"), ReturnType] TypeSyntax targetType, [Quoted] ExpressionSyntax x)
                    => CastExpression(targetType, x);
            }

            public class TestClass {
                [Template(typeof(Factory))]
                void Build([Splice] object x) {
                    System.Console.WriteLine(MyBuilders.Cast<int>(x));
                }
            }
            """;

        var compilation = CompilationWithSource(source);

        var fullDriver = CSharpGeneratorDriver.Create(
            new SyntaxBuilderFacadeGenerator(),
            new TemplateFactorySourceGenerator());
        fullDriver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var genDiagnostics, TestContext.Current.CancellationToken);

        Assert.Empty(genDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var errors = output.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(errors.Count == 0,
            "Post-generation compilation reported errors: " + string.Join("; ", errors.Select(d => d.Id + " " + d.GetMessage())));
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj`
Expected: FAIL — `TypeArg_WithName_SynthesizesGenericFacadeThatCompiles` reports post-generation errors (`[TypeArg]` is unresolved / the facade is not synthesized as generic). All other tests still pass.

- [ ] **Step 3: Create the `[TypeArg]` attribute**

Create `src/Synto/Templating/Builders/TypeArgAttribute.cs`:

```csharp
using System;

namespace Synto.Templating;

/// <summary>
/// Marks a <c>[SyntaxBuilder]</c> method parameter that surfaces on the synthesized facade as a generic
/// <em>type parameter</em> rather than a value parameter. The corresponding call argument is a type island
/// (e.g. the <c>int</c> in <c>Cast&lt;int&gt;(x)</c>), quoted to a <c>TypeSyntax</c> and passed to the builder
/// body. A type-arg parameter is inherently quoted, so <c>[TypeArg]</c> is a <em>sibling</em> of
/// <c>[Quoted]</c> — never combine the two on one parameter. Authored <c>public</c> here and injected
/// <c>internal</c> into the consumer compilation by <c>SurfaceInjectionGenerator</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class TypeArgAttribute : Attribute
{
    /// <summary>
    /// Names the synthesized facade generic type parameter, decoupling it from the builder parameter name.
    /// Default (<c>null</c>) uses <c>Pascal(parameterName)</c>. For example a builder parameter
    /// <c>targetType</c> with <c>Name = "TResult"</c> surfaces as <c>&lt;TResult&gt;</c>.
    /// </summary>
    public string? Name { get; set; }
}
```

- [ ] **Step 4: Wire the embedded-resource injection**

In `src/Synto.SourceGenerator/Synto.SourceGenerator.csproj`, add this line immediately after the `ReturnTypeAttribute.cs` `<EmbeddedResource>` (currently ~line 53):

```xml
    <EmbeddedResource Include="..\Synto\Templating\Builders\TypeArgAttribute.cs" LogicalName="Synto.Runtime.TypeArgAttribute.cs" />
```

- [ ] **Step 5: Add the `GetNamedString` metadata helper**

In `src/Synto.SourceGenerator/Shared/SymbolMetadataExtensions.cs`, add this method after `GetNamedTypeDisplay` (~line 54):

```csharp
    public static string? GetNamedString(AttributeData attr, string name)
    {
        foreach (var kv in attr.NamedArguments)
        {
            if (kv.Key == name && kv.Value.Value is string s)
                return s;
        }

        return null;
    }
```

- [ ] **Step 6: Teach `FacadeShape.Derive` to recognize `[TypeArg]` (dual-support)**

In `src/Synto.SourceGenerator/Templating/Builders/FacadeShape.cs`, replace the entire `foreach (var p in builder.Parameters) { ... }` loop (lines 87-130) with:

```csharp
        foreach (var p in builder.Parameters)
        {
            var quoted = SymbolMetadataExtensions.FindAttribute(p, typeof(global::Synto.Templating.QuotedAttribute).FullName!);
            var typeArg = SymbolMetadataExtensions.FindAttribute(p, typeof(global::Synto.Templating.TypeArgAttribute).FullName!);
            bool isReturnType = SymbolMetadataExtensions.FindAttribute(p, typeof(global::Synto.Templating.ReturnTypeAttribute).FullName!) is not null;

            // Legacy [Quoted(AsTypeArg = true)] is still honored during migration (removed in Task 2);
            // [TypeArg] is the going-forward marker for a facade generic type parameter.
            bool legacyAsTypeArg = quoted is not null && SymbolMetadataExtensions.GetNamedBool(quoted, "AsTypeArg");
            bool isTypeIsland = typeArg is not null || legacyAsTypeArg;

            if (isReturnType && !isTypeIsland)
            {
                annotationError = $"[ReturnType] on parameter '{p.Name}' requires [TypeArg]";
                return null;
            }

            if (isTypeIsland)
            {
                // Type island: facade generic type parameter. Name override (from [TypeArg]) else Pascal(param).
                string genericName = (typeArg is not null ? SymbolMetadataExtensions.GetNamedString(typeArg, "Name") : null)
                                     ?? Pascal(p.Name);
                genericParameters.Add(genericName);
                parameters.Add(new FacadeParam(p.Name, BuilderArgKind.QuotedTypeArg, genericName, null));

                if (isReturnType)
                {
                    returnTypeParamCount++;
                    returnTypeParamName = genericName;
                }

                continue;
            }

            if (quoted is not null)
            {
                // [Quoted] value island: facade value param typed `As` (if given) else object.
                string valueType = SymbolMetadataExtensions.GetNamedTypeDisplay(quoted, "As") ?? "object";
                parameters.Add(new FacadeParam(p.Name, BuilderArgKind.Quoted, null, valueType));
                continue;
            }

            // Unmarked (staged): facade value param, same type as the builder param.
            parameters.Add(new FacadeParam(p.Name, BuilderArgKind.Staged, null, p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
        }
```

- [ ] **Step 7: Run the new test to verify it passes**

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj`
Expected: `TypeArg_WithName_SynthesizesGenericFacadeThatCompiles` PASSES. Exactly one test now fails: `SurfaceInjectionTest.VerifyInjectedSurface` (a new injected file `TypeArgAttribute.g.cs` has no accepted snapshot yet).

- [ ] **Step 8: Accept the new injected-surface snapshot**

The failing `SurfaceInjectionTest` wrote `test/Synto.Test/snapshots/SurfaceInjectionTest.VerifyInjectedSurface#TypeArgAttribute.g.received.cs`. Accept it (PowerShell):

```powershell
Get-ChildItem test/Synto.Test/snapshots -Filter 'SurfaceInjectionTest.*.received.cs' | ForEach-Object {
    Move-Item -LiteralPath $_.FullName -Destination ($_.FullName -replace '\.received\.cs$', '.verified.cs') -Force
}
```

Confirm the new `test/Synto.Test/snapshots/SurfaceInjectionTest.VerifyInjectedSurface#TypeArgAttribute.g.verified.cs` reads (mechanically produced; eyeball it):

```csharp
//HintName: TypeArgAttribute.g.cs
#nullable enable
using System;

namespace Synto.Templating;

/// <summary>
/// Marks a <c>[SyntaxBuilder]</c> method parameter that surfaces on the synthesized facade as a generic
/// <em>type parameter</em> rather than a value parameter. The corresponding call argument is a type island
/// (e.g. the <c>int</c> in <c>Cast&lt;int&gt;(x)</c>), quoted to a <c>TypeSyntax</c> and passed to the builder
/// body. A type-arg parameter is inherently quoted, so <c>[TypeArg]</c> is a <em>sibling</em> of
/// <c>[Quoted]</c> — never combine the two on one parameter. Authored <c>public</c> here and injected
/// <c>internal</c> into the consumer compilation by <c>SurfaceInjectionGenerator</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
internal sealed class TypeArgAttribute : Attribute
{
    /// <summary>
    /// Names the synthesized facade generic type parameter, decoupling it from the builder parameter name.
    /// Default (<c>null</c>) uses <c>Pascal(parameterName)</c>. For example a builder parameter
    /// <c>targetType</c> with <c>Name = "TResult"</c> surfaces as <c>&lt;TResult&gt;</c>.
    /// </summary>
    public string? Name { get; set; }
}
```

- [ ] **Step 9: Run the full suite to verify green**

Run: `$env:DiffEngine_Disabled='true'; dotnet test --project test/Synto.Test/Synto.Test.csproj`
Expected: PASS, 0 failures. Confirm no stray `test/Synto.Test/snapshots/*.received.cs` remain.

- [ ] **Step 10: Verify formatting**

Run: `dotnet format whitespace --verify-no-changes`
Expected: exit 0, no changes reported.

- [ ] **Step 11: Commit**

```bash
jj commit -m "feat(builders): add [TypeArg] parameter marker (sibling of [Quoted])"
```

---

### Task 2: Flip consumers to `[TypeArg]`-only and delete `AsTypeArg`

Migrate every `[Quoted(AsTypeArg = true)]` usage to `[TypeArg]`, remove the `AsTypeArg` property and its legacy recognition branch, retarget `[ReturnType]`'s docs/validation to `[TypeArg]`, and re-accept the two changed injected-surface snapshots. The generated facade and consumer call sites are unchanged, so `SyntaxBuilderTest`'s factory snapshots stay byte-identical.

**Files:**
- Modify: `test/Synto.Test/Templating/SyntaxBuilderTest.cs` (3 in-source-string marker migrations + 1 comment)
- Modify: `src/Synto/Templating/Builders/QuotedAttribute.cs` (delete `AsTypeArg`, refresh `As` doc)
- Modify: `src/Synto/Templating/Builders/ReturnTypeAttribute.cs` (doc: `AsTypeArg` → `[TypeArg]`)
- Modify: `src/Synto.SourceGenerator/Templating/Builders/FacadeShape.cs` (drop the legacy `AsTypeArg` branch)
- Modify: `src/Synto.SourceGenerator/Templating/Builders/BuilderCallModel.cs` (`BuilderArgKind` doc: `[Quoted(AsTypeArg = true)]` → `[TypeArg]`)
- Snapshots (accepted mechanically): `test/Synto.Test/snapshots/SurfaceInjectionTest.VerifyInjectedSurface#QuotedAttribute.g.verified.cs` (shrinks), `...#ReturnTypeAttribute.g.verified.cs` (doc)

**Interfaces:**
- Consumes (from Task 1): `[TypeArg]` recognition in `FacadeShape.Derive`; `SymbolMetadataExtensions.GetNamedString`.
- Produces: `QuotedAttribute` with only `public Type? As { get; set; }` (no `AsTypeArg`); `FacadeShape.Derive` recognizes type islands solely via `[TypeArg]`.

- [ ] **Step 1: Migrate the test source strings to `[TypeArg]`**

In `test/Synto.Test/Templating/SyntaxBuilderTest.cs`, make these three edits (they are inside the in-memory-compilation string literals):

1. In `UserSyntaxBuilder_IsDiscoveredAndInvoked` (~line 154), replace:

```csharp
                public static ExpressionSyntax Cast([Quoted(AsTypeArg = true), ReturnType] TypeSyntax t, [Quoted] ExpressionSyntax x) => CastExpression(t, x);
```

with:

```csharp
                public static ExpressionSyntax Cast([TypeArg, ReturnType] TypeSyntax t, [Quoted] ExpressionSyntax x) => CastExpression(t, x);
```

2. In `InvalidBuilderAnnotation_ReportsSY1015` (~line 205), replace the comment:

```csharp
                // [ReturnType] without [Quoted(AsTypeArg = true)] -> facade-synthesis error.
```

with:

```csharp
                // [ReturnType] without [TypeArg] -> facade-synthesis error.
```

(The offending code `public static ExpressionSyntax Foo([ReturnType] ExpressionSyntax x) => x;` is left unchanged — it still has no `[TypeArg]`, so it still raises SY1015.)

3. In `BuilderArgBindingMismatch_ReportsSY1016` (~line 231), replace:

```csharp
                public static ExpressionSyntax Cast([Quoted(AsTypeArg = true), ReturnType] TypeSyntax t, [Quoted] ExpressionSyntax x) => x;
```

with:

```csharp
                public static ExpressionSyntax Cast([TypeArg, ReturnType] TypeSyntax t, [Quoted] ExpressionSyntax x) => x;
```

- [ ] **Step 2: Run tests to verify still green (dual-support keeps them passing)**

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj`
Expected: PASS. The migrated builders now use `[TypeArg]` (recognized since Task 1); the facade shape is identical (`t` → `Pascal("t")` = `T`), so `SyntaxBuilderTest.UserSyntaxBuilder_IsDiscoveredAndInvoked`'s factory snapshot is unchanged.

- [ ] **Step 3: Delete `AsTypeArg` from `QuotedAttribute`**

Replace `src/Synto/Templating/Builders/QuotedAttribute.cs` in full with:

```csharp
using System;

namespace Synto.Templating;

/// <summary>
/// Marks a <c>[SyntaxBuilder]</c> method parameter whose corresponding call argument is <em>quoted</em> (an
/// output-world syntax island) rather than passed through as a live value. The parameter <em>type</em> cannot
/// disambiguate "quote this island into an <c>ExpressionSyntax</c>" from "pass a live <c>ExpressionSyntax</c>
/// value through unquoted" (deliberate metaprogramming), so binding-time is declared explicitly: an
/// <c>[Quoted]</c> parameter receives the quote of the call argument; an unmarked parameter receives the
/// live/computed value verbatim. Authored <c>public</c> here and injected <c>internal</c> into the consumer
/// compilation by <c>SurfaceInjectionGenerator</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class QuotedAttribute : Attribute
{
    /// <summary>
    /// When set, the synthesized facade value parameter is typed as this type instead of <c>object</c>.
    /// Default <c>null</c>.
    /// </summary>
    public Type? As { get; set; }
}
```

- [ ] **Step 4: Retarget `[ReturnType]`'s doc comment**

Replace the `<summary>` doc block in `src/Synto/Templating/Builders/ReturnTypeAttribute.cs` so the class reads:

```csharp
/// <summary>
/// Placed on a <c>[TypeArg]</c> <c>[SyntaxBuilder]</c> parameter to make that parameter's synthesized generic
/// type parameter the facade's <em>return type</em>. Without any <c>[ReturnType]</c> parameter the synthesized
/// facade returns a fresh <c>TResult</c> type parameter (reusing a type island as the result is opt-in, never
/// implicit). Misuse — <c>[ReturnType]</c> on a non-<c>[TypeArg]</c> parameter, or more than one
/// <c>[ReturnType]</c> parameter on a builder — is a facade-synthesis error. Authored <c>public</c> here and
/// injected <c>internal</c> into the consumer compilation by <c>SurfaceInjectionGenerator</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class ReturnTypeAttribute : Attribute
{
}
```

- [ ] **Step 5: Drop the legacy `AsTypeArg` branch from `FacadeShape.Derive`**

In `src/Synto.SourceGenerator/Templating/Builders/FacadeShape.cs`, replace the `foreach (var p in builder.Parameters) { ... }` loop (the dual-support version from Task 1) with the final `[TypeArg]`-only version:

```csharp
        foreach (var p in builder.Parameters)
        {
            var quoted = SymbolMetadataExtensions.FindAttribute(p, typeof(global::Synto.Templating.QuotedAttribute).FullName!);
            var typeArg = SymbolMetadataExtensions.FindAttribute(p, typeof(global::Synto.Templating.TypeArgAttribute).FullName!);
            bool isReturnType = SymbolMetadataExtensions.FindAttribute(p, typeof(global::Synto.Templating.ReturnTypeAttribute).FullName!) is not null;

            if (isReturnType && typeArg is null)
            {
                annotationError = $"[ReturnType] on parameter '{p.Name}' requires [TypeArg]";
                return null;
            }

            if (typeArg is not null)
            {
                // [TypeArg] type island: facade generic type parameter. Name override else Pascal(param).
                string genericName = SymbolMetadataExtensions.GetNamedString(typeArg, "Name") ?? Pascal(p.Name);
                genericParameters.Add(genericName);
                parameters.Add(new FacadeParam(p.Name, BuilderArgKind.QuotedTypeArg, genericName, null));

                if (isReturnType)
                {
                    returnTypeParamCount++;
                    returnTypeParamName = genericName;
                }

                continue;
            }

            if (quoted is not null)
            {
                // [Quoted] value island: facade value param typed `As` (if given) else object.
                string valueType = SymbolMetadataExtensions.GetNamedTypeDisplay(quoted, "As") ?? "object";
                parameters.Add(new FacadeParam(p.Name, BuilderArgKind.Quoted, null, valueType));
                continue;
            }

            // Unmarked (staged): facade value param, same type as the builder param.
            parameters.Add(new FacadeParam(p.Name, BuilderArgKind.Staged, null, p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
        }
```

- [ ] **Step 6: Refresh the `BuilderArgKind` doc comment**

In `src/Synto.SourceGenerator/Templating/Builders/BuilderCallModel.cs`, replace the `enum BuilderArgKind` `<summary>` block (lines 7-12) with:

```csharp
/// <summary>
/// How a single syntax-builder parameter's call argument is bound at factory time (Locked Names §6). An
/// <c>[Quoted]</c> parameter receives the <em>quote</em> of the call argument (an output-world island); a
/// <c>[TypeArg]</c> parameter receives the quote of a facade <em>type</em> argument; an unmarked parameter
/// receives the live/computed value verbatim.
/// </summary>
```

- [ ] **Step 7: Build and run tests — verify green except the two changed snapshots**

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj`
Expected: builds clean (no reference to `AsTypeArg` remains anywhere). Only `SurfaceInjectionTest.VerifyInjectedSurface` fails — `QuotedAttribute.g.cs` shrank (no `AsTypeArg`) and `ReturnTypeAttribute.g.cs`'s doc changed. Every other test (including all `SyntaxBuilderTest` snapshot tests and `InvalidBuilderAnnotation_ReportsSY1015`) passes.

- [ ] **Step 8: Accept the two changed injected-surface snapshots**

Run with the diff tool disabled, then move the received files (PowerShell):

```powershell
Get-ChildItem test/Synto.Test/snapshots -Filter 'SurfaceInjectionTest.*.received.cs' | ForEach-Object {
    Move-Item -LiteralPath $_.FullName -Destination ($_.FullName -replace '\.received\.cs$', '.verified.cs') -Force
}
```

Confirm `SurfaceInjectionTest.VerifyInjectedSurface#QuotedAttribute.g.verified.cs` no longer contains `AsTypeArg` (only the `As` property remains) and `...#ReturnTypeAttribute.g.verified.cs` references `[TypeArg]` (not `AsTypeArg`).

- [ ] **Step 9: Run the full suite to verify green**

Run: `$env:DiffEngine_Disabled='true'; dotnet test --project test/Synto.Test/Synto.Test.csproj`
Expected: PASS, 0 failures. No stray `test/Synto.Test/snapshots/*.received.cs` remain. Confirm no residual `AsTypeArg` in tracked source:

Run: `grep -rn "AsTypeArg" src test --include=*.cs`
Expected: no matches (build-output DLLs under `bin/`/`obj/` are ignorable and not tracked by jj).

- [ ] **Step 10: Verify formatting**

Run: `dotnet format whitespace --verify-no-changes`
Expected: exit 0, no changes reported.

- [ ] **Step 11: Commit**

```bash
jj commit -m "refactor(builders): drop QuotedAttribute.AsTypeArg for dedicated [TypeArg]; [ReturnType] requires [TypeArg]"
```

---

### Task 3: Add a generic `where T : Constraint` clause to `[TypeArg]`

Give `[TypeArg]` an optional `Constraint` (a `Type?`) that surfaces as a `where T : Constraint` clause on the synthesized facade, so a consumer type argument that violates it is a compile error. Additive and behavior-preserving for existing builders (which set no constraint → byte-identical facades). Builds on the `[TypeArg]`-only model from Task 2.

**Files:**
- Modify: `src/Synto/Templating/Builders/TypeArgAttribute.cs` (add `Constraint` property)
- Modify: `src/Synto.SourceGenerator/Templating/Builders/FacadeShape.cs` (add a `FacadeGenericParam` type; change `GenericParameters` to carry a per-param constraint; read `Constraint` in `Derive`)
- Modify: `src/Synto.SourceGenerator/Templating/Builders/SyntaxBuilderFacadeGenerator.cs` (emit the `where` clause; read `.Name` from the new type)
- Test: `test/Synto.Test/Templating/SyntaxBuilderTest.cs` (add two `[Fact]`s)
- Snapshot (re-accepted): `test/Synto.Test/snapshots/SurfaceInjectionTest.VerifyInjectedSurface#TypeArgAttribute.g.verified.cs`

**Interfaces:**
- Consumes (from Tasks 1-2): `[TypeArg]` recognition in `FacadeShape.Derive`; `SymbolMetadataExtensions.GetNamedTypeDisplay`.
- Produces:
  - `TypeArgAttribute.Constraint` (`Type?`).
  - `internal readonly struct FacadeGenericParam { public string Name; public string? ConstraintDisplay; }`.
  - `FacadeShape.GenericParameters` becomes `IReadOnlyList<FacadeGenericParam>` (was `IReadOnlyList<string>`): each `[TypeArg]` generic carries `ConstraintDisplay = GetNamedTypeDisplay(typeArg, "Constraint")`; the fresh `TResult` carries `null`.
  - Facade emission appends ` where <Name> : <ConstraintDisplay>` for each constrained generic, before the `=> default!;` body.

- [ ] **Step 1: Add the `Constraint` property to `[TypeArg]`**

Replace `src/Synto/Templating/Builders/TypeArgAttribute.cs` in full with (adds the `Constraint` property; the `Name` property and docs are unchanged from Task 1):

```csharp
using System;

namespace Synto.Templating;

/// <summary>
/// Marks a <c>[SyntaxBuilder]</c> method parameter that surfaces on the synthesized facade as a generic
/// <em>type parameter</em> rather than a value parameter. The corresponding call argument is a type island
/// (e.g. the <c>int</c> in <c>Cast&lt;int&gt;(x)</c>), quoted to a <c>TypeSyntax</c> and passed to the builder
/// body. A type-arg parameter is inherently quoted, so <c>[TypeArg]</c> is a <em>sibling</em> of
/// <c>[Quoted]</c> — never combine the two on one parameter. Authored <c>public</c> here and injected
/// <c>internal</c> into the consumer compilation by <c>SurfaceInjectionGenerator</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class TypeArgAttribute : Attribute
{
    /// <summary>
    /// Names the synthesized facade generic type parameter, decoupling it from the builder parameter name.
    /// Default (<c>null</c>) uses <c>Pascal(parameterName)</c>. For example a builder parameter
    /// <c>targetType</c> with <c>Name = "TResult"</c> surfaces as <c>&lt;TResult&gt;</c>.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// When set, constrains the synthesized facade type parameter to this base class or interface, emitting a
    /// <c>where T : Constraint</c> clause so the consumer's supplied type argument must satisfy it. Default
    /// (<c>null</c>) emits an unconstrained type parameter. A single base/interface type only.
    /// </summary>
    public Type? Constraint { get; set; }
}
```

- [ ] **Step 2: Accept the changed injected-surface snapshot (surface-only change)**

The `Constraint` property changes the injected `TypeArgAttribute` surface. Regenerate and accept it before touching behavior:

Run: `$env:DiffEngine_Disabled='true'; dotnet test --project test/Synto.Test/Synto.Test.csproj`
Expected: only `SurfaceInjectionTest.VerifyInjectedSurface` fails (`TypeArgAttribute.g.cs` now has `Constraint`).

Accept (PowerShell):

```powershell
Get-ChildItem test/Synto.Test/snapshots -Filter 'SurfaceInjectionTest.*.received.cs' | ForEach-Object {
    Move-Item -LiteralPath $_.FullName -Destination ($_.FullName -replace '\.received\.cs$', '.verified.cs') -Force
}
```

Re-run: `dotnet test --project test/Synto.Test/Synto.Test.csproj` → PASS (the `Constraint` property is present but not yet emitted anywhere, so no behavior changed).

- [ ] **Step 3: Write the failing constraint test**

Add this `[Fact]` to `test/Synto.Test/Templating/SyntaxBuilderTest.cs` (after `TypeArg_WithName_SynthesizesGenericFacadeThatCompiles`). It feeds a type argument that violates the constraint and expects the compiler to reject it (`CS0311`), which can only happen if the `where` clause is actually emitted:

```csharp
    // [TypeArg(Constraint = ...)] emits a `where T : Constraint` clause on the facade, so a consumer type
    // argument that violates it is a compile error (CS0311). This proves the constraint is emitted, not ignored.
    [Fact]
    public void TypeArg_Constraint_RejectsViolatingTypeArgument()
    {
        const string source =
            """
            using Synto.Templating;
            using Microsoft.CodeAnalysis.CSharp.Syntax;
            using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
            partial class Factory {}

            static partial class MyBuilders {
                [SyntaxBuilder]
                public static ExpressionSyntax Cast([TypeArg(Name = "T", Constraint = typeof(System.IComparable)), ReturnType] TypeSyntax targetType, [Quoted] ExpressionSyntax x)
                    => CastExpression(targetType, x);
            }

            public class TestClass {
                [Template(typeof(Factory))]
                void Build([Splice] object x) {
                    System.Console.WriteLine(MyBuilders.Cast<object>(x)); // object does NOT satisfy `where T : IComparable`
                }
            }
            """;

        var compilation = CompilationWithSource(source);

        var fullDriver = CSharpGeneratorDriver.Create(
            new SyntaxBuilderFacadeGenerator(),
            new TemplateFactorySourceGenerator());
        fullDriver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var genDiagnostics, TestContext.Current.CancellationToken);

        Assert.Empty(genDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var errors = output.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.Contains(errors, d => d.Id == "CS0311");
    }
```

- [ ] **Step 4: Run the test to verify it fails**

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj`
Expected: FAIL — `TypeArg_Constraint_RejectsViolatingTypeArgument`. The `Constraint` is read but ignored, so the facade is emitted as `T Cast<T>(object x)` with no `where` clause; `Cast<object>(x)` compiles, so no `CS0311` is produced. All other tests pass.

- [ ] **Step 5: Carry the constraint through `FacadeShape`**

In `src/Synto.SourceGenerator/Templating/Builders/FacadeShape.cs`, make these four edits.

(a) Add the `FacadeGenericParam` type immediately after the `FacadeParam` class's closing brace (just before the `/// <summary>` that documents `FacadeShape`):

```csharp
/// <summary>One synthesized facade generic type parameter: its name and an optional base-type constraint.</summary>
internal readonly struct FacadeGenericParam
{
    public FacadeGenericParam(string name, string? constraintDisplay)
    {
        Name = name;
        ConstraintDisplay = constraintDisplay;
    }

    public string Name { get; }

    /// <summary>Fully-qualified base type for a <c>where Name : X</c> clause, or <c>null</c> when unconstrained.</summary>
    public string? ConstraintDisplay { get; }
}
```

(b) Change the constructor parameter type and the property type from `IReadOnlyList<string>` to `IReadOnlyList<FacadeGenericParam>`. Replace:

```csharp
        IReadOnlyList<string> genericParameters,
```

with:

```csharp
        IReadOnlyList<FacadeGenericParam> genericParameters,
```

and replace:

```csharp
    /// <summary>The synthesized facade's generic type parameters, in facade order (a leading fresh TResult first when <see cref="FreshReturnTypeParam"/>).</summary>
    public IReadOnlyList<string> GenericParameters { get; }
```

with:

```csharp
    /// <summary>The synthesized facade's generic type parameters (name + optional constraint), in facade order (a leading fresh TResult first when <see cref="FreshReturnTypeParam"/>).</summary>
    public IReadOnlyList<FacadeGenericParam> GenericParameters { get; }
```

(c) In `Derive`, change the local declaration:

```csharp
        var genericParameters = new List<string>();
```

to:

```csharp
        var genericParameters = new List<FacadeGenericParam>();
```

and, inside the `if (typeArg is not null)` branch, replace:

```csharp
                string genericName = SymbolMetadataExtensions.GetNamedString(typeArg, "Name") ?? Pascal(p.Name);
                genericParameters.Add(genericName);
```

with:

```csharp
                string genericName = SymbolMetadataExtensions.GetNamedString(typeArg, "Name") ?? Pascal(p.Name);
                string? constraintDisplay = SymbolMetadataExtensions.GetNamedTypeDisplay(typeArg, "Constraint");
                genericParameters.Add(new FacadeGenericParam(genericName, constraintDisplay));
```

(d) Change the fresh-`TResult` insertion:

```csharp
        if (freshReturnTypeParam)
            genericParameters.Insert(0, FreshReturnTypeParamName);
```

to:

```csharp
        if (freshReturnTypeParam)
            genericParameters.Insert(0, new FacadeGenericParam(FreshReturnTypeParamName, null));
```

- [ ] **Step 6: Emit the `where` clause in the facade generator**

In `src/Synto.SourceGenerator/Templating/Builders/SyntaxBuilderFacadeGenerator.cs`, replace the whole `AppendFacadeMethod` method with:

```csharp
    private static void AppendFacadeMethod(StringBuilder sb, FacadeShape shape)
    {
        sb.Append("internal static ").Append(shape.ReturnTypeDisplay).Append(' ').Append(shape.MethodName);

        if (shape.GenericParameters.Count > 0)
        {
            sb.Append('<');
            for (int i = 0; i < shape.GenericParameters.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(shape.GenericParameters[i].Name);
            }
            sb.Append('>');
        }

        sb.Append('(');
        bool first = true;
        foreach (var p in shape.Parameters)
        {
            if (p.Kind == BuilderArgKind.QuotedTypeArg)
                continue;
            if (!first) sb.Append(", ");
            first = false;
            sb.Append(p.ValueTypeDisplay).Append(' ').Append(p.ParameterName);
        }
        sb.Append(')');

        foreach (var gp in shape.GenericParameters)
        {
            if (gp.ConstraintDisplay is not null)
                sb.Append(" where ").Append(gp.Name).Append(" : ").Append(gp.ConstraintDisplay);
        }

        sb.Append(" => default!;\n");
    }
```

(Unconstrained builders have no `ConstraintDisplay`, so the emitted text is byte-identical to before — the `<...>` names are unchanged and no `where` clause is appended.)

- [ ] **Step 7: Run the constraint test to verify it passes**

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj`
Expected: `TypeArg_Constraint_RejectsViolatingTypeArgument` PASSES (the facade is now `internal static T Cast<T>(object x) where T : global::System.IComparable => default!;`, so `Cast<object>(x)` produces `CS0311`). All existing snapshot tests remain green (unconstrained facades unchanged).

- [ ] **Step 8: Add the satisfying-type test (guard against over-constraining)**

Add this `[Fact]` to `test/Synto.Test/Templating/SyntaxBuilderTest.cs` (after the previous one):

```csharp
    // A consumer type argument that SATISFIES the [TypeArg] constraint still compiles cleanly.
    [Fact]
    public void TypeArg_Constraint_AcceptsSatisfyingTypeArgument()
    {
        const string source =
            """
            using Synto.Templating;
            using Microsoft.CodeAnalysis.CSharp.Syntax;
            using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
            partial class Factory {}

            static partial class MyBuilders {
                [SyntaxBuilder]
                public static ExpressionSyntax Cast([TypeArg(Name = "T", Constraint = typeof(System.IComparable)), ReturnType] TypeSyntax targetType, [Quoted] ExpressionSyntax x)
                    => CastExpression(targetType, x);
            }

            public class TestClass {
                [Template(typeof(Factory))]
                void Build([Splice] object x) {
                    System.Console.WriteLine(MyBuilders.Cast<string>(x)); // string satisfies `where T : IComparable`
                }
            }
            """;

        var compilation = CompilationWithSource(source);

        var fullDriver = CSharpGeneratorDriver.Create(
            new SyntaxBuilderFacadeGenerator(),
            new TemplateFactorySourceGenerator());
        fullDriver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var genDiagnostics, TestContext.Current.CancellationToken);

        Assert.Empty(genDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var errors = output.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(errors.Count == 0,
            "Post-generation compilation reported errors: " + string.Join("; ", errors.Select(d => d.Id + " " + d.GetMessage())));
    }
```

- [ ] **Step 9: Run the full suite to verify green**

Run: `$env:DiffEngine_Disabled='true'; dotnet test --project test/Synto.Test/Synto.Test.csproj`
Expected: PASS, 0 failures. No stray `test/Synto.Test/snapshots/*.received.cs` remain.

- [ ] **Step 10: Verify formatting**

Run: `dotnet format whitespace --verify-no-changes`
Expected: exit 0, no changes reported.

- [ ] **Step 11: Commit**

```bash
jj commit -m "feat(builders): [TypeArg] supports an optional generic where-constraint"
```

---

## Deferred / out of scope (do NOT implement here)

- **`typeof` / `System.Type`-value materialization** of a type-arg (the `Cast(typeof(int), x)` and data-driven `c.ClrType` forms). Recorded in the spec as future materialization *options* of `[TypeArg]`, designed against real usage when switch type/property patterns are built. No attribute or code for it now.
- **`[Quoted, TypeArg]` co-occurrence diagnostic.** The taxonomy makes them siblings; `[TypeArg]` is authoritative when both are present (the `[Quoted]` branch is unreachable for such a parameter). No new diagnostic is added — out of scope, and the spec did not request one.
- **KB / doc refresh.** `docs/kb/subsystems/templating.md` (and the switch-builders design/plan docs) still mention `AsTypeArg`. Updating the KB is a follow-up via the `refresh-kb` skill, not part of this code plan. The switch-builders design/plan docs are Plan B and are intentionally untouched here.
- **Advanced `[TypeArg]` constraints.** Task 3 supports a single base-class/interface constraint. `class` / `struct` / `notnull` / `new()` / multiple constraints are future work if a real caller needs them — no attribute shape is committed for them now.

## Self-Review notes

- **Spec coverage:** Delete `AsTypeArg` (Task 2 Step 3) ✓; add `TypeArgAttribute` with `Name` (Task 1 Step 3) ✓; `FacadeShape` discriminates by `[TypeArg]` with name `Name ?? Pascal(param)` (Task 1 Step 6 → Task 2 Step 5) ✓; `[ReturnType]` requires `[TypeArg]` + message update (Task 2 Step 5 error string, Step 4 doc) ✓; keep `[Quoted].As` (Task 2 Step 3 retains `As`) ✓; embedded-resource injection + snapshot (Task 1 Steps 4/8) ✓; behavior-preserving `Cast<int>(x)` / `T Cast<T>(object x)` (byte-identical `SyntaxBuilderTest` snapshots, asserted Task 2 Step 2) ✓; incremental cacheability (marker reads stay in transform — no pipeline-state change) ✓; `[TypeArg].Constraint` → `where T : Constraint` (Task 3, driven by a CS0311 rejection test + a satisfying-type guard; unconstrained facades byte-identical) ✓.
- **Type consistency:** `TypeArgAttribute.Name` (string?) read via `GetNamedString`; `[Quoted].As` read via `GetNamedTypeDisplay`; both feed `FacadeParam` / `BuilderArgKind.QuotedTypeArg` unchanged. Method names consistent across tasks (`GetNamedString`, `Derive`, `Pascal`).
- **No placeholders:** every code step carries full, compilable content.

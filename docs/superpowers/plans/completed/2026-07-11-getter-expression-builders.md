# Getter Expression Builders (Coalesce + ordinal Arm) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Retire the raw-Roslyn tower in ObjectReader's nullable-reference typed-getter arm by adding a `Coalesce` (`??`) syntax builder and an ordinal-labelled `Arm` overload, so the guarded arm reads entirely through the builder surface.

**Architecture:** Two new `[SyntaxBuilder]`s plus one generator widening. (1) `Coalesce` is a fixed-`ExpressionSyntax`-facade operator builder (`Returns=typeof(ExpressionSyntax), Facade=typeof(Operators)`) so bare `Coalesce(left, right)` composes with no type argument, its `[Quoted]` operands expanding nested `Member`/`Throw` via re-entry. (2) An `Arm([Constant] SyntaxNode ordinal, [Quoted] ExpressionSyntax value)` overload lets the author pass a factory-time ordinal (`c.Ordinal`) instead of a hand-built `ConstantPattern(LiteralExpression(...))`. (3) Because that overload's binding shape differs from the existing staged `Arm`, `FacadeCallFinder` gains argument-type overload resolution (today it reports SY1018 on any non-shape-sharing same-name overloads). Finally the ObjectReader dog-food is rewritten onto both.

**Tech Stack:** C# Roslyn incremental source generators; xUnit v3 + Verify snapshots; jj-driven git; .NET SDK 10 (MTP test runner).

## Global Constraints

- **`TreatWarningsAsErrors` is global** (Directory.Build.props). Any new warning — including CS8019 unused-using and IDE analyzers — fails the build solution-wide. Every task ends on a clean build.
- **Cacheability preserved.** No `ISymbol`/`SemanticModel`/`SyntaxNode` may enter cached incremental-pipeline state. New generator code runs *inside* the existing `ForAttributeWithMetadataName` transform (as `FacadeCallFinder` already does); it may use the `SemanticModel` locally but must capture nothing into pipeline outputs.
- **Builders are authored in `src/Synto.Core`**, `<Compile Remove>`d from Synto.Core's own build, and injected `internal` into the consumer's generator compilation by `SurfaceInjectionGenerator`. They are called fully-qualified at factory time; never add a Synto.Core runtime dependency.
- **Output is semantic-equivalence, NOT byte-identical.** ObjectReader snapshot re-accepts are expected and benign; behavior tests (`ObjectReaderBehaviorTests`) must stay green.
- **jj drives git HEAD.** Commit each task with `jj commit <paths> -m "..."` to isolate from the user's working-copy edit (`ReaderTemplate.cs` is currently `M`). Never move `main`; do experimental work on an `experimental/*` bookmark.
- **Test invocation (SDK 10 MTP):** raw `dotnet test --project <csproj> --filter-method "*Name*"` (the `rtk` dotnet-test rewrite mangles args; use plain `dotnet`).

---

### Task 1: `Coalesce` — the `??` expression builder

Adds a fixed-`ExpressionSyntax`-facade operator builder so `Coalesce(left, right)` emits `left ?? right`, with `[Quoted]` operand islands that expand nested builder calls (`Member`, `Throw`) via re-entry. Fixed facade (into `Operators`) means the call needs no type argument — the whole point, since a fresh-`TResult` facade (`Coalesce<TResult>`) couldn't infer `TResult` and would force `Coalesce<ExpressionSyntax>(...)`.

**Files:**
- Modify: `src/Synto.Core/Templating/Builders/InjectedOnly/OperatorBuilders.cs`
- Test: `test/Synto.Test/Templating/SyntaxBuilderTest.cs`

**Interfaces:**
- Consumes: the operator-facade mechanism — `[SyntaxBuilder(Returns=…, Facade=typeof(Operators))]` synthesizes a fixed-return facade into `Operators` (proven by `FixedReturnFacade_SynthesizedIntoFacadeHost`); `FacadeShape.Derive` short-circuits to `returnTypeDisplay = fixedReturnDisplay` before the fresh-`TResult` branch, so any `Returns` type (including `ExpressionSyntax`) is accepted verbatim.
- Produces: `OperatorBuilders.Coalesce(ExpressionSyntax left, ExpressionSyntax right) : ExpressionSyntax`, surfaced to authors as the bare `Operators.Coalesce(object left, object right) : ExpressionSyntax` facade.

- [ ] **Step 1: Write the failing test (rewrite target + nested re-entry)**

Add to `SyntaxBuilderTest.cs` (mirrors `NameOf_EmitsNameofExpression`):

```csharp
// Coalesce (spec §2.1): a fixed-ExpressionSyntax-facade `??` builder. `Coalesce(a, b)` rewrites to the
// fully-qualified OperatorBuilders.Coalesce; its [Quoted] operands are islands, so a nested Member(...) call
// expands via re-entry (the factory references the real builder, never the inert facade).
[Fact]
public void Coalesce_RewritesToBuilder_AndExpandsNestedMember()
{
    var generated = RunAndGetGeneratedTextWithInjectedSurface(
        """
        using Synto.Templating;
        using static Synto.Templating.Operators;
        using static Synto.Templating.SyntoBuilders;
        partial class Factory {}
        public class TestClass {
            [Template<Factory>]
            object Build([Splice] object obj)
                => Coalesce(Member<object>(obj, "X"), Member<object>(obj, "Y"));
        }
        """);

    Assert.Contains("OperatorBuilders.Coalesce", generated, StringComparison.Ordinal);
    // nested Member islands expanded to the real builder, not left as the inert facade call.
    Assert.Contains("SyntoBuilders.Member", generated, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj --filter-method "*Coalesce_RewritesToBuilder_AndExpandsNestedMember*"`
Expected: FAIL — `Coalesce` is not a registered builder, so the call is quoted verbatim (no `OperatorBuilders.Coalesce` in output).

- [ ] **Step 3: Add the `Coalesce` builder**

In `OperatorBuilders.cs`, add after `SizeOf` (the file already has `using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;`; add `using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;` at the top for `CoalesceExpression`):

```csharp
    /// <summary>Builds <c>left ?? right</c> (a null-coalescing expression) from two quoted operand islands.
    /// Fixed <c>ExpressionSyntax</c> facade (into <see cref="Operators"/>) so a bare, type-argument-free
    /// <c>Coalesce(...)</c> composes inside an island; the operands expand nested builders via re-entry.</summary>
    [SyntaxBuilder(Returns = typeof(ExpressionSyntax), Facade = typeof(Operators))]
    internal static ExpressionSyntax Coalesce([Quoted] ExpressionSyntax left, [Quoted] ExpressionSyntax right) =>
        BinaryExpression(CoalesceExpression, left, right);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj --filter-method "*Coalesce_RewritesToBuilder_AndExpandsNestedMember*"`
Expected: PASS.

- [ ] **Step 5: Write the executed end-to-end test (bare call compiles + renders `?? throw`)**

Add to `SyntaxBuilderTest.cs` (mirrors `NameOf_Executed_EmitsNameofExpression` exactly — three-generator driver, emit, load, invoke, render):

```csharp
// Coalesce executed end-to-end: the BARE `Coalesce(...)` (no type argument) type-checks against the fixed
// ExpressionSyntax facade, post-gen compiles clean, and renders `obj.X ?? throw new ...` — proving both the
// `??` emission and nested Member/Throw re-entry.
[Fact]
public void Coalesce_Executed_EmitsCoalesceWithThrow()
{
    const string source =
        """
        using Synto.Templating;
        using static Synto.Templating.Operators;
        using static Synto.Templating.SyntoBuilders;

        partial class Factory {}

        [Template<Factory>]
        public class Reader {
            public object Build([Splice] object obj)
                => Coalesce(Member<object>(obj, "X"), Throw<object>(new System.InvalidCastException("boom")));
        }
        """;

    var compilation = CompilationWithSource(source);

    var fullDriver = CSharpGeneratorDriver.Create(
        new SurfaceInjectionGenerator(),
        new SyntaxBuilderFacadeGenerator(),
        new TemplateFactorySourceGenerator());
    fullDriver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var genDiagnostics, TestContext.Current.CancellationToken);

    Assert.Empty(genDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
    var compileErrors = output.GetDiagnostics(TestContext.Current.CancellationToken)
        .Where(d => d.Severity == DiagnosticSeverity.Error)
        .ToList();
    Assert.True(compileErrors.Count == 0,
        "Generated output did not compile: " + string.Join("; ", compileErrors.Select(d => d.Id + " " + d.GetMessage(System.Globalization.CultureInfo.InvariantCulture))));

    using var peStream = new System.IO.MemoryStream();
    var emit = output.Emit(peStream, cancellationToken: TestContext.Current.CancellationToken);
    Assert.True(emit.Success, "Emit failed: " + string.Join("; ", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id + " " + d.GetMessage(System.Globalization.CultureInfo.InvariantCulture))));

    var assembly = Assembly.Load(peStream.ToArray());
    var readerFactory = assembly.GetType("Factory")!.GetMethod("Reader")!;
    var built = readerFactory.Invoke(null, new object?[] { null });

    var node = Assert.IsAssignableFrom<Microsoft.CodeAnalysis.SyntaxNode>(built!);
    var rendered = node.NormalizeWhitespace().ToString();
    Assert.Contains("?? throw", rendered, System.StringComparison.Ordinal);
    Assert.Contains(".X", rendered, System.StringComparison.Ordinal);
}
```

- [ ] **Step 6: Run both Coalesce tests**

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj --filter-method "*Coalesce*"`
Expected: PASS (both).

- [ ] **Step 7: Full Synto.Test run (guard the injected-surface snapshots)**

Adding a builder changes the injected `Operators`/`OperatorBuilders` surface, so `SurfaceInjectionTest` and `InjectedSurfaceCompletenessTest` snapshots will need re-accepting.

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj`
Expected: any failures are Verify snapshot diffs on the injected surface (a new `Coalesce` facade in `Operators.g` / builder in `OperatorBuilders.g`). Review each diff shows only the added `Coalesce` member, then accept:

Run: `dotnet verify accept -y` *(or move each `*.received.*` over its `*.verified.*` if the Verify CLI is unavailable)*
Re-run the suite; Expected: PASS.

- [ ] **Step 8: Commit**

```bash
jj commit src/Synto.Core/Templating/Builders/InjectedOnly/OperatorBuilders.cs test/Synto.Test -m "feat(builders): add Coalesce (??) fixed-facade syntax builder"
```

---

### Task 2: Argument-type overload resolution in `FacadeCallFinder`

Today, two same-name builder overloads that survive type-argument narrowing must **share a binding shape** (identical quoted/staged/constant/type-arg kind sequence) or `FacadeCallFinder` reports SY1018 (`FacadeCallFinder.cs:111`). Task 3's ordinal `Arm` overload has a *different* shape from the staged `Arm` (`[Constant, Quoted]` vs `[Staged, Quoted]`), so it would trip SY1018. This task teaches the finder to resolve such overloads by replicating C# overload resolution over the **synthesized facade value-parameter types**, choosing a single builder before binding. Proven here with synthetic user builders, in isolation.

**Files:**
- Modify: `src/Synto.Generators/Templating/Builders/FacadeCallFinder.cs`
- Test: `test/Synto.Test/Templating/SyntaxBuilderTest.cs`

**Interfaces:**
- Consumes: `FacadeShape.Derive` (per-candidate shape); `SemanticModel.GetTypeInfo`; `Compilation.ClassifyConversion`; `SymbolMetadataExtensions.FindAttribute` (already imported via `Synto.Generators.Symbols` — same helper `FacadeShape` uses); the marker types `QuotedAttribute`/`ConstantAttribute`/`TypeArgAttribute` from `Synto.Templating`.
- Produces: a private `ResolveOverloadByArguments(List<IMethodSymbol>, InvocationExpressionSyntax) : IMethodSymbol?` that returns the single matching overload, or `null` (→ caller reports SY1018). Behavior for shape-*sharing* overloads (the two `Switch` forms) is unchanged — they never reach this path.

- [ ] **Step 1: Write the failing test**

Add to `SyntaxBuilderTest.cs`:

```csharp
// Overload resolution (Task 2 widening): two same-name overloads with DIFFERENT binding shapes — staged
// `Pick(ExpressionSyntax, …)` and constant `Pick([Constant] SyntaxNode, …)`. Before the widening they trip
// SY1018 (ShareBindingShape rejects the differing kinds); after, the finder resolves each call by argument
// type — an int argument binds the constant overload (lifted via ToSyntax), an ExpressionSyntax argument the
// staged overload (passed verbatim).
//
// The `Pick(7, Picks.Wrap(v))` call is the guard for the resolver's object-facing rule (spec
// `2026-07-11-builder-overload-resolution-design.md` §4): its second argument is a NESTED facade call
// (`Wrap`), which is UNBOUND in the analyzed compilation (a synthesized facade, not present) — so
// `GetTypeInfo(...).Type` on it is null/error. A resolver that type-checked EVERY argument would reject both
// `Pick` candidates at that `[Quoted]` slot and spuriously report SY1018. Because the two overloads differ
// only at the object-facing value param, the int/first-arg alone must decide. This mirrors the real dog-food
// call `Arm(c.Ordinal, Member<TRet>(...))` — do NOT drop this call; it is the case the naive resolver fails.
[Fact]
public void NonSharedShapeOverloads_ResolvedByArgumentType_NoSY1018()
{
    const string source =
        """
        using Synto.Templating;
        using Microsoft.CodeAnalysis;
        using Microsoft.CodeAnalysis.CSharp;
        using Microsoft.CodeAnalysis.CSharp.Syntax;
        using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
        partial class Factory {}

        static partial class Picks {
            [SyntaxBuilder]
            public static SwitchExpressionArmSyntax Pick(ExpressionSyntax pattern, [Quoted] ExpressionSyntax v)
                => SwitchExpressionArm(ConstantPattern(pattern), v);
            [SyntaxBuilder]
            public static SwitchExpressionArmSyntax Pick([Constant] SyntaxNode label, [Quoted] ExpressionSyntax v)
                => SwitchExpressionArm(ConstantPattern((ExpressionSyntax)label), v);
            // A nested [Quoted]-island builder returning ExpressionSyntax — its bare facade call is unbound in
            // the analyzed compilation, so it stresses the object-facing rule when used as a `[Quoted]` argument.
            [SyntaxBuilder]
            public static ExpressionSyntax Wrap([Quoted] ExpressionSyntax inner) => inner;
        }

        public class TestClass {
            [Template<Factory>]
            void Build([Splice] object v) {
                var a = Picks.Pick(42, v);                                             // int -> constant overload
                var b = Picks.Pick(LiteralExpression(SyntaxKind.NullLiteralExpression), v); // ExpressionSyntax -> staged
                var c = Picks.Pick(7, Picks.Wrap(v));                                  // nested UNBOUND facade as [Quoted] arg -> constant overload, no SY1018
                System.Console.WriteLine(a);
                System.Console.WriteLine(b);
                System.Console.WriteLine(c);
            }
        }
        """;

    var diagnostics = RunAndGetDiagnostics(source);
    Assert.DoesNotContain(diagnostics, d => d.Id == "SY1018");
    Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

    // The int-arg calls resolved to the CONSTANT overload (their arguments are lifted via ToSyntax); the
    // ExpressionSyntax-arg call resolved to the STAGED overload (its argument is passed verbatim, not lifted).
    var generated = RunAndGetGeneratedText(source);
    Assert.Contains("42.ToSyntax()", generated, StringComparison.Ordinal);
    Assert.Contains("7.ToSyntax()", generated, StringComparison.Ordinal); // nested-facade arg did not block resolution
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj --filter-method "*NonSharedShapeOverloads_ResolvedByArgumentType_NoSY1018*"`
Expected: FAIL — SY1018 is reported (the two `Pick` overloads don't share a binding shape).

- [ ] **Step 3: Add the resolver and route the non-shared-shape branch through it**

In `FacadeCallFinder.cs`, add `using System.Linq;` and `using Synto.Generators.Symbols;` if not already present (the file already references `Synto.Templating`, `Microsoft.CodeAnalysis`, and `Synto.Generators.Templating.Lifting`). Replace the SY1018 bail + bind (the block at lines 111-120) with:

```csharp
            IMethodSymbol chosen;
            if (resolved.Count > 1 && !ShareBindingShape(resolved))
            {
                // Differing binding shapes (e.g. staged `Arm(ExpressionSyntax,…)` vs constant `Arm([Constant],…)`):
                // bounded argument-directed resolution over the synthesized facade value-param types (only staged
                // params discriminate; object-facing islands never do). SY1018 fires only when that leaves zero or
                // a tied set — a predictable error with a fix-it, never an arbitrary pick.
                var picked = ResolveOverloadByArguments(resolved, invocation);
                if (picked is null)
                {
                    _diagnostics.Add(TemplateDiagnostics.AmbiguousBuilder(invocation.GetLocation(), simpleName));
                    base.VisitInvocationExpression(invocation);
                    return;
                }
                chosen = picked;
            }
            else
            {
                chosen = resolved[0];
            }

            var match = FacadeArgumentBinder.Bind(_semanticModel, invocation, chosen, _valueLift, _diagnostics);
            if (match is not null)
                _calls.Add(match);
```

Then add these private members to the class:

```csharp
    /// <summary>
    /// Resolves same-name builder overloads whose binding shapes DIFFER, with a bounded rule (spec
    /// 2026-07-11-builder-overload-resolution-design.md §2). ONLY [Staged] value params discriminate: a [Staged]
    /// param faces the builder parameter type and the argument's resolved type must implicitly convert to it. A
    /// [Quoted]/[Constant] param is object-facing and ALWAYS applicable — it is an island that accepts any
    /// expression and is NEVER type-checked here (its argument is frequently a nested, still-unbound facade call
    /// whose type cannot be resolved; type-checking it would spuriously reject the candidate — see spec §4). A
    /// [TypeArg] param is not a value argument (handled upstream by type-argument-arity narrowing). Among
    /// applicable candidates the most concretely-typed one wins (fewest object-facing params — staged
    /// <c>Arm(ExpressionSyntax, object)</c> beats constant <c>Arm(object, object)</c> when an ExpressionSyntax
    /// argument fits both). Returns null on zero applicable or a tie, so the caller reports SY1018. Runs entirely
    /// inside the transform; captures nothing into pipeline state.
    /// </summary>
    private IMethodSymbol? ResolveOverloadByArguments(List<IMethodSymbol> candidates, InvocationExpressionSyntax invocation)
    {
        var args = invocation.ArgumentList.Arguments;
        var objectType = _semanticModel.Compilation.GetSpecialType(SpecialType.System_Object);

        var applicable = new List<(IMethodSymbol Method, int ObjectFacing)>();
        foreach (var candidate in candidates)
        {
            var facadeParams = FacadeValueParams(candidate, objectType);
            if (facadeParams is null || facadeParams.Count != args.Count)
                continue;

            bool ok = true;
            for (int i = 0; i < args.Count; i++)
            {
                if (facadeParams[i].ObjectFacing)
                    continue; // island: accepts any expression, never type-checked (may be an unbound nested facade)

                // Staged param: the argument must be a real, bound factory-time expression that converts.
                var argType = _semanticModel.GetTypeInfo(args[i].Expression).Type;
                if (argType is null ||
                    !_semanticModel.Compilation.ClassifyConversion(argType, facadeParams[i].Type).IsImplicit)
                {
                    ok = false;
                    break;
                }
            }
            if (ok)
            {
                int objectFacing = facadeParams.Count(p => p.ObjectFacing);
                applicable.Add((candidate, objectFacing));
            }
        }

        if (applicable.Count == 0)
            return null;

        int min = applicable.Min(a => a.ObjectFacing);
        var best = applicable.Where(a => a.ObjectFacing == min).ToList();
        return best.Count == 1 ? best[0].Method : null;
    }

    /// <summary>
    /// The synthesized facade's value parameters in call order (type-argument params skipped), each carrying its
    /// classification type and whether it is object-facing. A [Staged] param carries its builder parameter type
    /// and discriminates (not object-facing); a [Quoted]/[Constant] param is object-facing (an island, never
    /// classified — its facade type is immaterial here, so <c>object</c> is stored). Null when the candidate does
    /// not derive a valid facade.
    /// </summary>
    private List<(ITypeSymbol Type, bool ObjectFacing)>? FacadeValueParams(IMethodSymbol builder, ITypeSymbol objectType)
    {
        if (FacadeShape.Derive(builder, out var annotationError, out var returnShapeError) is null
            || annotationError is not null || returnShapeError is not null)
            return null;

        var result = new List<(ITypeSymbol, bool)>();
        foreach (var p in builder.Parameters)
        {
            if (SymbolMetadataExtensions.FindAttribute(p, typeof(global::Synto.Templating.TypeArgAttribute).FullName!) is not null)
                continue; // type argument, not a value argument

            bool objectFacing =
                SymbolMetadataExtensions.FindAttribute(p, typeof(global::Synto.Templating.QuotedAttribute).FullName!) is not null
                || SymbolMetadataExtensions.FindAttribute(p, typeof(global::Synto.Templating.ConstantAttribute).FullName!) is not null;
            result.Add((objectFacing ? objectType : p.Type, objectFacing));
        }
        return result;
    }
```

> **Note on the object-facing rule:** `[Quoted]`/`[Constant]` arguments are the real dog-food's nested builder islands (`Member<TRet>(…)`, `Coalesce(…)`) — UNBOUND in the analyzed compilation, so `GetTypeInfo(...).Type` on them is null/error. They must never gate applicability (spec §4); only `[Staged]` params carry a bound, classifiable type. The `Pick(7, Picks.Wrap(v))` line in Step 1 is the guard.
>
> **Note on `params`:** the only shape-sharing overload set today (the two `Switch` forms) never reaches this path, and neither new overload uses `params`, so exact `Count == args.Count` matching is sufficient. If a future non-shared-shape overload adds `params`, extend the count check then — do not add speculative handling now (YAGNI).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj --filter-method "*NonSharedShapeOverloads_ResolvedByArgumentType_NoSY1018*"`
Expected: PASS.

- [ ] **Step 5: Guard — genuinely ambiguous overloads still report SY1018**

Add a regression so the widening doesn't silently swallow real ambiguity (this keeps the existing `AmbiguousBuilder_ReportsSY1018` intent alive for the new path):

```csharp
// The widening must NOT swallow genuine ambiguity: two non-shared-shape overloads BOTH applicable to the same
// argument with no most-specific winner still report SY1018.
[Fact]
public void NonSharedShapeOverloads_TrulyAmbiguous_StillReportsSY1018()
{
    var diagnostics = RunAndGetDiagnostics(
        """
        using Synto.Templating;
        using Microsoft.CodeAnalysis;
        using Microsoft.CodeAnalysis.CSharp.Syntax;
        partial class Factory {}

        static partial class Amb {
            // Both faces are `object` (one [Quoted], one [Constant]) -> a `null` argument is applicable to both
            // with equal object-param counts -> tied -> SY1018.
            [SyntaxBuilder]
            public static ExpressionSyntax Two([Quoted] ExpressionSyntax x) => x;
            [SyntaxBuilder]
            public static ExpressionSyntax Two([Constant] SyntaxNode x) => (ExpressionSyntax)x;
        }

        public class TestClass {
            [Template<Factory>]
            void Build() => System.Console.WriteLine(Amb.Two(null));
        }
        """);
    var diag = Assert.Single(diagnostics, d => d.Id == "SY1018");
    AssertHasRealSpan(diag);
}
```

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj --filter-method "*NonSharedShapeOverloads*"`
Expected: PASS (both).

- [ ] **Step 5b: Upgrade the SY1018 message to be actionable**

The widening changes when SY1018 fires — no longer "any non-shape-sharing overloads" but "the arguments don't distinguish the overloads". Make the message tell the author how to fix it. Find the `SY1018`/`AmbiguousBuilder` descriptor (in `TemplateDiagnostics`) and replace its message with actionable guidance, e.g.:

> Cannot determine which `{0}` overload this call targets — the candidates differ in binding shape and the arguments don't distinguish them. Fix by one of: (a) give the overloads a distinguishing non-`[Quoted]`/`[Constant]` (staged) parameter whose type the argument selects; (b) rename one overload; (c) pass an argument whose type matches exactly one overload.

Keep the existing message argument (`{0}` = the builder name) and the diagnostic id/severity/category unchanged — only the format string changes, so no new args and nothing new enters equatable diagnostic state (spec §5). Rendering the candidate signature list is deferred (spec §9). If `AmbiguousBuilder_ReportsSY1018` asserts on message text, update that assertion.

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj --filter-method "*AmbiguousBuilder_ReportsSY1018*"`
Expected: PASS.

- [ ] **Step 6: Full Synto.Test run — no regression, cacheability intact**

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj`
Expected: PASS. In particular `AmbiguousBuilder_ReportsSY1018`, the `Switch` overload tests (`SwitchBuilderTest`), and the template-factory cacheability asserts stay green (the finder still captures nothing into pipeline state).

- [ ] **Step 7: Commit**

```bash
jj commit src/Synto.Generators/Templating/Builders/FacadeCallFinder.cs test/Synto.Test -m "feat(builders): resolve non-shape-sharing same-name overloads by argument type"
```

---

### Task 3: Ordinal `Arm` overload (`[Constant]` ordinal)

Adds `Arm([Constant] SyntaxNode ordinal, [Quoted] ExpressionSyntax value)` so the author writes `Arm(c.Ordinal, value)` — a factory-time ordinal lifted to a constant literal at the binding seam — instead of hand-building `ConstantPattern(LiteralExpression(NumericLiteralExpression, Literal(...)))`. The builder param is `SyntaxNode` (not `ExpressionSyntax`) so its signature differs from the existing staged `Arm(ExpressionSyntax, ExpressionSyntax)` (otherwise CS0111); the lifted node (`ExpressionSyntax` from `.ToSyntax()`) binds fine and is cast back. Relies on Task 2's overload resolution.

**Files:**
- Modify: `src/Synto.Core/Templating/Builders/InjectedOnly/SyntoBuilders.cs`
- Test: `test/Synto.Test/Templating/SyntaxBuilderTest.cs`

**Interfaces:**
- Consumes: Task 2's `ResolveOverloadByArguments` (so `Arm(int, …)` and `Arm(ExpressionSyntax, …)` both resolve, no SY1018); the `[Constant]` value-lift (int → `(…).ToSyntax()`, proven by `ConstantArgs_LiftedViaToSyntaxAtBindingSeam`).
- Produces: `SyntoBuilders.Arm(SyntaxNode ordinal, ExpressionSyntax value) : SwitchExpressionArmSyntax`, surfaced as the `Arm(object ordinal, object value)` facade. The existing `Arm(ExpressionSyntax pattern, [Quoted] ExpressionSyntax value)` is unchanged and still binds for pre-built patterns.

- [ ] **Step 1: Write the failing test**

Add to `SyntaxBuilderTest.cs`:

```csharp
// Ordinal Arm overload (spec §2.2): `Arm(c.Ordinal, value)` lifts the factory-time ordinal to a constant literal
// pattern (via ToSyntax) and pairs it with the quoted value island — no hand-built ConstantPattern(Literal(...)).
// Resolves against the staged `Arm(ExpressionSyntax, …)` overload via Task 2's argument-type resolution.
[Fact]
public void OrdinalArm_LiftsOrdinal_AndCompiles()
{
    const string source =
        """
        using Synto.Templating;
        using static Synto.Templating.SyntoBuilders;
        partial class Factory {}
        public class TestClass {
            [Template<Factory>]
            object Build([Splice] object obj) => Switch<object>(obj, Arm(2, Member<object>(obj, "X")));
        }
        """;

    var generated = RunAndGetGeneratedTextWithInjectedSurface(source);
    Assert.Contains("SyntoBuilders.Arm", generated, StringComparison.Ordinal);
    Assert.Contains("2.ToSyntax()", generated, StringComparison.Ordinal); // ordinal lifted, not hand-built

    // Post-gen compiles clean (the two Arm overloads coexist, no SY1018).
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

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj --filter-method "*OrdinalArm_LiftsOrdinal_AndCompiles*"`
Expected: FAIL — `Arm(2, …)` binds int against the only `Arm(ExpressionSyntax, …)` facade → SY1016/binding mismatch or carrier compile error (no ordinal overload exists yet).

- [ ] **Step 3: Add the ordinal `Arm` overload**

In `SyntoBuilders.cs`, add `using Microsoft.CodeAnalysis;` (for `SyntaxNode`) to the existing usings, and add this overload immediately after the existing `Arm` (line 57):

```csharp
    /// <summary>Builds <c>ordinal =&gt; value</c> — an ordinal-labelled switch-expression arm: a factory-time
    /// ordinal ([Constant], value-lifted to a constant literal pattern) + a quoted value island. Overloads the
    /// staged <see cref="Arm(Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax, Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax)"/>;
    /// the builder param is <c>SyntaxNode</c> (not <c>ExpressionSyntax</c>) so its signature differs from the
    /// staged overload — the lifted <c>ExpressionSyntax</c> binds and is cast back.</summary>
    [SyntaxBuilder]
    public static SwitchExpressionArmSyntax Arm([Constant] SyntaxNode ordinal, [Quoted] ExpressionSyntax value) =>
        SwitchExpressionArm(ConstantPattern((ExpressionSyntax)ordinal), value);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj --filter-method "*OrdinalArm_LiftsOrdinal_AndCompiles*"`
Expected: PASS.

- [ ] **Step 5: Write the regression test (pre-built pattern Arm still binds)**

Add to `SyntaxBuilderTest.cs`:

```csharp
// Regression (spec §4): the EXISTING staged `Arm(<built pattern>, value)` overload still binds — an
// ExpressionSyntax first argument resolves to the staged overload (more specific than the ordinal's `object`
// facade param), passed verbatim (never lifted via ToSyntax).
[Fact]
public void StagedArm_PrebuiltPattern_StillBinds()
{
    const string source =
        """
        using Synto.Templating;
        using Microsoft.CodeAnalysis.CSharp;
        using Microsoft.CodeAnalysis.CSharp.Syntax;
        using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
        using static Synto.Templating.SyntoBuilders;
        partial class Factory {}
        public class TestClass {
            [Template<Factory>]
            object Build([Splice] object obj) => Switch<object>(obj,
                Arm(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(2)), Member<object>(obj, "X")));
        }
        """;

    var generated = RunAndGetGeneratedTextWithInjectedSurface(source);
    Assert.Contains("SyntoBuilders.Arm", generated, StringComparison.Ordinal);
    // The pre-built pattern is passed verbatim — NOT lifted through ToSyntax (that path is the ordinal overload).
    Assert.DoesNotContain("Literal(2).ToSyntax()", generated, StringComparison.Ordinal);

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

- [ ] **Step 6: Run both Arm tests**

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj --filter-method "*Arm*"`
Expected: PASS.

- [ ] **Step 7: Full Synto.Test run + accept injected-surface snapshots**

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj`
Expected: failures are Verify snapshot diffs on the injected surface showing only the added `Arm` overload in `SyntoBuilders.g` (and its `Arm(object, object)` facade). Review, then accept:

Run: `dotnet verify accept -y`
Re-run; Expected: PASS.

- [ ] **Step 8: Commit**

```bash
jj commit src/Synto.Core/Templating/Builders/InjectedOnly/SyntoBuilders.cs test/Synto.Test -m "feat(builders): add ordinal [Constant] Arm overload"
```

---

### Task 4: Rewrite the ObjectReader typed-getter arm onto the builders

Replaces the hand-built `SwitchExpressionArm`/`BinaryExpression`/`MemberAccessExpression`/`ConstantPattern`/`LiteralExpression` tower in `TypedGetter<TRet>` with `Arm(c.Ordinal, …)` + `Coalesce(…, Throw(…))`, so both branches ride `Arm(c.Ordinal, …)` and the guarded branch differs only by wrapping its member in `Coalesce`. Validated by building the example and running its behavior tests (Synto.Test being green does NOT prove the dog-food surface — the example must build).

**Files:**
- Modify: `examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Generator/ReaderTemplate.cs` (the `TypedGetter<TRet>` body — the `return Switch<TRet>(...)` at lines ~132-145)

**Interfaces:**
- Consumes: `Coalesce` (Task 1), the ordinal `Arm` overload (Task 3). `Member<TRet>`/`Throw<TRet>`/`Throw<object>`/`Switch<TRet>`/`DefaultArm` are unchanged (kept with their type arguments — the `2.3` type-argument shed is out of scope; a fixed facade on `Member`/`Throw` would break every `Member<object>`/`Switch<TRet>` caller across the suite).

- [ ] **Step 1: Read the current getter body**

Read `ReaderTemplate.cs` around the `TypedGetter<TRet>` method (the `return Switch<TRet>(...)` block, ~lines 132-145) so the exact projection and surrounding lambda are in hand.

- [ ] **Step 2: Rewrite the arm projection**

Replace the `return Switch<TRet>(...)` expression's `.Select(c => …)` body — currently a ternary whose plain branch is `Arm(LiteralExpression(NumericLiteralExpression, Literal(c.Ordinal)), Member<TRet>(_e.Current, c.Name))` and whose guarded branch is the raw `SwitchExpressionArm(ConstantPattern(LiteralExpression(...)), BinaryExpression(CoalesceExpression, MemberAccessExpression(...), Throw<ExpressionSyntax>(...)))` — with:

```csharp
        return Switch<TRet>(i, columns.Where(c => c.ColumnTypeName == clrTypeName).Select(c =>
            !c.IsNullableReference
                ? Arm(c.Ordinal, Member<TRet>(_e.Current, c.Name))
                : Arm(c.Ordinal, Coalesce(
                    Member<object>(_e.Current, c.Name),
                    Throw<object>(new InvalidCastException($"Field {i} null; call IsDBNull first.")))))
            .Append(DefaultArm(Throw<TRet>(new InvalidCastException($"Field {i} is not {typeLabel} column.")))));
```

Both branches now build `SwitchExpressionArmSyntax` via the ordinal `Arm(object, object)` facade (the ternary's common type). The guarded branch differs only in wrapping the member in `Coalesce(…, Throw(…))`. The `$"Field {i} …"` interpolations stay output-world (`i` is the getter's runtime parameter), exactly as before — they ride inside `[Quoted]` islands.

- [ ] **Step 3: Ensure `Coalesce`/`Operators` is imported**

`ReaderTemplate.cs` already has `using static Synto.Templating.Operators;` (for `TypeOf`) and `using static Synto.Templating.SyntoBuilders;` (for `Member`/`Arm`/`Switch`/`Throw`). `Coalesce` lives on `Operators`, so no new using is needed. Confirm both `using static` lines are present.

- [ ] **Step 4: Remove now-dead raw-syntax imports**

The rewrite deletes the last uses of raw `SyntaxKind` members (`NumericLiteralExpression`, `SimpleMemberAccessExpression`, `CoalesceExpression`) in this file. Under global TWAE an unused `using static ...SyntaxKind;` is a CS8019 build error. After Step 2:
- If `using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;` is now unused (grep the file for any remaining bare `SyntaxKind`-member references — e.g. in other members), remove that line.
- Keep `using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;` (still used by `Identifier(...)` in the `[Splice] TypedGetters()` renames) and `using Microsoft.CodeAnalysis.CSharp.Syntax;` (still used by `MemberDeclarationSyntax`).

Let the build in Step 5 confirm; if it flags a still-used import as removed, restore it.

- [ ] **Step 5: Build the example generator + consumer**

The example test project references the generator, Api, and Demo, so building it drives the whole dog-food:

Run: `dotnet build examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Tests/Synto.Example.ObjectReader.Tests.csproj`
Expected: SUCCESS, 0 warnings. If the generator's factory-time compile fails (e.g. an unresolved facade or a type-argument error in the rewritten arm), fix the arm expression per the interfaces above and rebuild.

- [ ] **Step 6: Run the ObjectReader behavior tests**

Run: `dotnet test --project examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Tests/Synto.Example.ObjectReader.Tests.csproj`
Expected: PASS — the generated `IDataReader` still produces ADO.NET-correct typed getters (nullable-reference columns → `?? throw InvalidCastException`; others → plain access). If the example has Verify snapshots of generated source, expect benign re-accepts (the arm is now assembled by builders); review the diff shows only the arm shape changed to `X ?? throw …` / `N => …`, then accept.

- [ ] **Step 7: Cacheability + full Synto.Test regression**

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj`
Expected: PASS, including all cacheability asserts (no `SyntaxNode`/`ISymbol`/`SemanticModel` entered pipeline state in Task 2).

- [ ] **Step 8: Commit**

```bash
jj commit examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Generator/ReaderTemplate.cs -m "refactor(example): ObjectReader typed-getter arm rides Coalesce + ordinal Arm"
```

> **Note — do NOT sweep the user's WIP.** The user has an unrelated in-progress working-copy edit elsewhere in `ReaderTemplate.cs` (a `Math.Min` refactor in `GetValues`, NOT the getter arm). An isolated implement-plan workspace bases off a commit, so that uncommitted edit is not present there and `jj commit <file>` is clean — but before committing, run `jj diff` on the file and confirm it shows ONLY the typed-getter arm change. If any other hunk appears (i.e. run applied in a dirty workspace), `jj split` the file to separate the arm change before committing; never fold the user's WIP into this commit.

---

## Self-Review

**Spec coverage:**
- §2.1 `Coalesce` (fixed facade, no type-arg, nested re-entry) → Task 1. ✓
- §2.2 ordinal `Arm` (`[Constant]` overload, existing staged Arm preserved) → Task 3, enabled by Task 2. ✓ (Q2 resolved to the overload path per the user; the SY1018 blocker is handled by Task 2's generator widening, whose resolver is specified in `docs/superpowers/specs/2026-07-11-builder-overload-resolution-design.md` — object-facing args never gate resolution (spec §4), ambiguity emits an actionable SY1018 (spec §5).)
- §2.3 `Throw`/`Member` type-argument shed → **explicitly out of scope** (spec §6 "drop if it balloons": a fixed facade on `Member`/`Throw` breaks every `<T>` caller across the suite). Arm operands keep `<object>`/`<TRet>`. ✓ (documented)
- §3 rewrite `TypedGetter` onto the builders → Task 4. ✓
- §4 contracts: `Coalesce(a,b)`→`a ?? b` (Task 1 executed test); no call-site type arg (Task 1 bare-call compile); `Arm(c.Ordinal, value)` + staged-Arm regression (Task 3); semantic-not-byte-identical + behavior green (Task 4); cacheability preserved (Task 2 note + Task 4 Step 7). ✓
- §7 open questions: Q1 host = `Operators` (Task 1); Q2 = ordinal overload + widening (Tasks 2-3, per user); Q3 `Returns=typeof(ExpressionSyntax)` accepted with no new mode (Task 1 interfaces, confirmed against `FacadeShape.Derive`). ✓

**Placeholder scan:** every code step carries complete source; commands have expected outcomes; no TBD/"handle edge cases". ✓

**Type consistency:** `Coalesce(ExpressionSyntax,ExpressionSyntax):ExpressionSyntax` (builder) / `Coalesce(object,object):ExpressionSyntax` (facade); `Arm(SyntaxNode,ExpressionSyntax):SwitchExpressionArmSyntax` (builder) / `Arm(object,object)` (facade); `ResolveOverloadByArguments(List<IMethodSymbol>, InvocationExpressionSyntax):IMethodSymbol?` used consistently in Task 2. `BuilderArgKind` = `{Staged, Quoted, QuotedTypeArg, Constant}`. ✓

# `Pattern.Replace` Match-Driven Rewrite — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Plan style:** This plan pins **contracts, interfaces, signatures, and tests** — not prescribed implementation bodies (matching the ForMatch / matching-dsl plans). Each task states exact public/internal signatures and concrete test intent; the implementer chooses the implementation that satisfies them. Test code is the spec — write it as given, adapting to the exact harness helpers.

**Goal:** Add `MatchPattern<T>.Replace(...)` — the consumer-facing, Regex-parallel helper that rewrites the nodes a Synto `[Match]` pattern matches in a syntax tree.

**Architecture:** A new injected `ReplaceOption { All, First }` enum (parallel to `MatchOption`) and a `Replace` extension on the injected `MatchPattern<TMatch>` (parallel to `Regex.Replace`). `Replace` walks `root` descendants-and-self with a private `CSharpSyntaxRewriter`, replacing each match (outermost-wins, single pass) with the consumer's `Func<Matched<TMatch>, SyntaxNode>` and returning the rewritten `TRoot`. It is imperative (returns a `SyntaxNode`), never part of cached pipeline state. The surface is injected `internal` into the consumer's generator project via the existing surface-injection path and is self-contained on `netstandard2.0`.

**Tech Stack:** C# / .NET, Roslyn incremental source generators (`IIncrementalGenerator`, `CSharpSyntaxRewriter`), xUnit v3 + Verify snapshots, jujutsu (jj) VCS.

**Spec:** `docs/superpowers/specs/2026-06-22-match-replace-design.md` (contracts C-R1…C-R5).

## Global Constraints

Every task implicitly includes these (verbatim from the spec / inherited from the ForMatch + matching-dsl contracts):

- **Self-contained, no runtime dependency.** Injected helpers compile on **`netstandard2.0`** with no Synto runtime-package reference. (C-R4.) The faithful `netstandard2.0` reference closure already exists in `MatchTestHarness` (the ns2.0 Roslyn build + deps, commit `db12ede8`); extend it, do not rebuild it.
- **Imperative, never cached.** `Replace` returns a `SyntaxNode`; it is transform-local / `RegisterSourceOutput`-stage. No `SyntaxNode` may enter **cached** incremental pipeline state. (C-R3.) There is no incremental/provider variant in this increment.
- **Outermost-wins, single pass.** A matched subtree is replaced wholesale and not descended into; matches inside a replaced subtree are not applied; replacements are not re-scanned. No fixpoint, no bottom-up. (C-R1.) Anything else is the consumer's own rewriter with `pattern.Match(node)` — explicit non-goal.
- **Green gate (run before every commit):** `dotnet build --no-restore -c Debug` (0 errors; analyzer warnings are findings, not gate failures) · `dotnet test --no-build -c Debug` (all green) · `dotnet format whitespace` then `dotnet format whitespace --verify-no-changes` (no diffs). **Whitespace scope only** — never full `dotnet format` (it applies CA1515/CA1815 rewrites that break the build). Root `.editorconfig` pins `end_of_line=lf`.
- **Release sanity:** `dotnet build --no-restore -c Release` stays at 0 errors with only the two pre-existing RS2008 warnings (`SY0000` Bootstrap, `SY9999` test) — no new ones.
- **Commits:** Conventional Commits, **no AI/Claude footer**, one commit per task. VCS is jj: `jj commit -m "…"` finalizes `@` and opens a new change on top.
- **Branch:** work on `experimental/matching` (tip currently `db12ede8`, with the design spec `85f4e336` on top). Never main; no `jj git push`; do **not** move the `experimental/matching` bookmark — the operator advances it after review.
- **Locked names:** `Replace`, `ReplaceOption` (`All = 0`, `First = 1`), `SyntoMatchReplaceExtensions`; namespace `Synto.Matching`.

## File Structure

**Create:**
- `src/Synto/Matching/ReplaceOption.cs` — the `public enum ReplaceOption { All = 0, First = 1 }`. **Compiled into `Synto.Core` AND embedded** for injection — authored exactly like `src/Synto/Matching/MatchOption.cs` (pure enum, no Roslyn dependency).
- `src/Synto/Matching/MatchReplaceExtensions.cs` — `public static class SyntoMatchReplaceExtensions` with the `Replace` extension + a private `CSharpSyntaxRewriter`. **`<Compile Remove>`d from `Synto.Core`** (it references the injected `MatchPattern<T>` / `Matched<T>`, which are not compiled into `Synto.Core`) and embedded for injection — authored like `src/Synto/Matching/ForMatchProviderExtensions.cs`.
- `test/Synto.Test/Match/MatchReplaceTests.cs` — functional + self-containment tests.

**Modify:**
- `src/Synto/Synto.csproj` — add `<Compile Remove="Matching\MatchReplaceExtensions.cs" />` (next to the existing ForMatch removes). **Do NOT** remove `ReplaceOption.cs` — it compiles, like `MatchOption.cs`.
- `src/Synto.SourceGenerator/Synto.SourceGenerator.csproj` — add two `<EmbeddedResource>` entries: `Synto.Runtime.ReplaceOption.cs` and `Synto.Runtime.MatchReplaceExtensions.cs` (next to the existing `Synto.Runtime.MatchOption.cs` / `Synto.Runtime.ForMatch*.cs`).
- `test/Synto.Test/Match/MatchTestHarness.cs` — add `CompileInjectedMatchReplaceSurfaceOnNetStandard20()` (mirror `CompileInjectedForMatchExtensionsOnNetStandard20`).
- `test/Synto.Test/snapshots/` — new auto-generated injected goldens `SurfaceInjectionTest.VerifyInjectedSurface#ReplaceOption.g.verified.cs` and `#MatchReplaceExtensions.g.verified.cs` (review + accept).

**Reuse (do not modify):** `SurfaceInjectionGenerator` (auto-discovers the new `Synto.Runtime.*` resources, rewrites `public`→`internal`), the injected `Matched<T>` / `MatchPattern<T>` surface, `MatchTestHarness.AssertCapture` / `Run` / `CreateNetStandardClosure` / `InjectedSurfaceSource`.

---

### Task 1: `ReplaceOption` + `Replace` (All mode) — surface, injection, self-containment

**Files:**
- Create: `src/Synto/Matching/ReplaceOption.cs`, `src/Synto/Matching/MatchReplaceExtensions.cs`
- Modify: `src/Synto/Synto.csproj`, `src/Synto.SourceGenerator/Synto.SourceGenerator.csproj`, `test/Synto.Test/Match/MatchTestHarness.cs`
- Test: `test/Synto.Test/Match/MatchReplaceTests.cs`; new goldens `SurfaceInjectionTest.VerifyInjectedSurface#ReplaceOption.g.verified.cs`, `#MatchReplaceExtensions.g.verified.cs`

**Interfaces:**
- Produces (authored `public` in `src/Synto`, injected `internal` into consumers):
  ```csharp
  namespace Synto.Matching;

  public enum ReplaceOption { All = 0, First = 1 }

  public static class SyntoMatchReplaceExtensions
  {
      // reads: M.SumPattern.Replace(root, static m => SyntaxFactory.BinaryExpression(SyntaxKind.MultiplyExpression, m.Captures.A, m.Captures.B))
      public static TRoot Replace<TRoot, TMatch>(
          this MatchPattern<TMatch> pattern,
          TRoot root,
          Func<Matched<TMatch>, SyntaxNode> replacement,
          ReplaceOption option = ReplaceOption.All)
          where TRoot : SyntaxNode
          where TMatch : class;
  }
  ```
- Consumes: the injected `Matched<TMatch>` / `MatchPattern<TMatch>` surface (its `Match` / `MatchFn`), `Microsoft.CodeAnalysis.CSharp.CSharpSyntaxRewriter`, the surface-injection path.
- Contract: **C-R1** (outermost-wins, single pass), **C-R2** (returns `TRoot`; root-self match returns the replacement), **C-R4** (self-contained on ns2.0).

- [ ] **Step 1: Write the failing tests**

In `test/Synto.Test/Match/MatchReplaceTests.cs` — the `All`-mode behavioral contract plus self-containment. The `[Match<M>]` pattern is declared inline (mirroring `ForMatchTests`); `M` is the shared `private partial class M` the matching tests target.

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto.Test.Match;

public class MatchReplaceTests
{
    private partial class M { }

    // Sum matches `a + b` and captures both operands (A, B : ExpressionSyntax).
    [global::Synto.Matching.Match<M>(global::Synto.Matching.MatchOption.Single)]
    private static object Sum([global::Synto.Matching.Capture] int a, [global::Synto.Matching.Capture] int b) => a + b;

    private static ExpressionSyntax Mul(global::Synto.Matching.Matched<M.SumMatch> m) =>
        SyntaxFactory.BinaryExpression(SyntaxKind.MultiplyExpression, m.Captures.A, m.Captures.B);

    [Fact]
    public void Replace_All_RewritesEveryNonNestedMatch() // C-R1 (All)
    {
        var root = SyntaxFactory.ParseExpression("f(1 + 2, 3 + 4)"); // two sibling Sum matches
        var rewritten = M.SumPattern.Replace(root, static m => Mul(m));
        Assert.Equal("f(1 * 2, 3 * 4)", rewritten.NormalizeWhitespace().ToString());
    }

    [Fact]
    public void Replace_All_OutermostWins_DoesNotRewriteInsideAReplacedSubtree() // C-R1
    {
        // The outer `(1 + 2) + (3 + 4)` matches Sum; its inner `1 + 2` / `3 + 4` are inside the
        // replaced subtree and must NOT be separately rewritten.
        var root = SyntaxFactory.ParseExpression("(1 + 2) + (3 + 4)");
        var rewritten = M.SumPattern.Replace((ExpressionSyntax)root, static m => Mul(m));
        Assert.Equal("(1 + 2) * (3 + 4)", rewritten.NormalizeWhitespace().ToString());
    }

    [Fact]
    public void Replace_NoMatch_ReturnsRootUnchanged()
    {
        var root = SyntaxFactory.ParseExpression("f(1 - 2)"); // subtraction: no Sum match
        var rewritten = M.SumPattern.Replace(root, static m => Mul(m));
        Assert.Same(root, rewritten); // C-R1/C-R2: no needless rewriting, same instance
    }

    [Fact]
    public void Replace_RootItselfMatches_ReturnsReplacementAsTRoot() // C-R2
    {
        var root = SyntaxFactory.ParseExpression("1 + 2"); // the root IS the match
        ExpressionSyntax rewritten = M.SumPattern.Replace((ExpressionSyntax)root, static m => Mul(m));
        Assert.Equal("1 * 2", rewritten.NormalizeWhitespace().ToString());
    }

    [Fact]
    public void InjectedMatchReplaceSurface_CompilesOn_NetStandard20() // C-R4
    {
        var diagnostics = MatchTestHarness.CompileInjectedMatchReplaceSurfaceOnNetStandard20();
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }
}
```

Add the harness helper to `MatchTestHarness.cs` (mirror `CompileInjectedForMatchExtensionsOnNetStandard20`):

```csharp
/// <summary>
/// C-R4 self-containment proof for the injected Replace surface: compiles the injected
/// SyntoMatchReplaceExtensions + ReplaceOption alongside the data surface and the IsExternalInit polyfill
/// on the FAITHFUL netstandard2.0 closure. Returns the resulting diagnostics (must be error-free).
/// </summary>
public static ImmutableArray<Diagnostic> CompileInjectedMatchReplaceSurfaceOnNetStandard20()
{
    var dataSurface = InjectedSurfaceSource("readonly struct MatchPattern");
    var replaceOption = InjectedSurfaceSource("enum ReplaceOption");
    var replaceExtensions = InjectedSurfaceSource("class SyntoMatchReplaceExtensions");
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

    return CreateNetStandardClosure(dataSurface, replaceOption, replaceExtensions, polyfill).GetDiagnostics();
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet build --no-restore -c Debug`
Expected: FAIL to compile — `M.SumPattern.Replace` and `ReplaceOption` do not exist yet (and `MatchReplaceTests` references them). This is the RED state.

- [ ] **Step 3: Implement (by contract)**

1. Author `src/Synto/Matching/ReplaceOption.cs` — `public enum ReplaceOption { All = 0, First = 1 }` with XML docs mirroring `MatchOption.cs` (note: NOT `[Flags]`, mutually-exclusive cardinality; default `All`).
2. Author `src/Synto/Matching/MatchReplaceExtensions.cs` — `public static class SyntoMatchReplaceExtensions` with the `Replace` signature from **Interfaces**. Behavior:
   - Walk `root` **descendants-and-self** with a private `CSharpSyntaxRewriter` internal to the file. At each visited node run `pattern.Match(node)` (the injected `MatchPattern<TMatch>.Match`); on a non-null result, return `replacement(new Matched<TMatch>(node, captures))` **without** descending into the matched subtree (do not call `base.Visit` on it). Descend normally into non-matching nodes.
   - This task implements `ReplaceOption.All` only (`First` lands in Task 2). The `option` parameter exists in the signature now; `All` is the default and the only path exercised here.
   - Return `(TRoot)` the rewriter's result so the root container type is preserved; when `root` itself matched, the replacement is returned (cast to `TRoot`). When nothing matched, return `root` unchanged (the rewriter returns the same instance — keep it; do not reallocate).
3. Register both files in `src/Synto.SourceGenerator/Synto.SourceGenerator.csproj` as `<EmbeddedResource Include="..\Synto\Matching\ReplaceOption.cs" LogicalName="Synto.Runtime.ReplaceOption.cs" />` and `<EmbeddedResource Include="..\Synto\Matching\MatchReplaceExtensions.cs" LogicalName="Synto.Runtime.MatchReplaceExtensions.cs" />`.
4. In `src/Synto/Synto.csproj`, add `<Compile Remove="Matching\MatchReplaceExtensions.cs" />` (it references the removed `MatchPattern<T>` / `Matched<T>`). Leave `ReplaceOption.cs` compiled. Confirm `src/Synto` still builds.

- [ ] **Step 4: Run tests + gate**

Run the green gate (Global Constraints). Review + accept the two new injected goldens (`SurfaceInjectionTest.VerifyInjectedSurface#ReplaceOption.g.verified.cs`, `#MatchReplaceExtensions.g.verified.cs`) after confirming each is the `internal`-rewritten authored source. Expected: all five new facts PASS; injection goldens green; build 0 errors; format clean.

- [ ] **Step 5: Commit**

```bash
jj commit -m "feat(matching): inject ReplaceOption + Pattern.Replace (All) match-driven rewrite"
```

---

### Task 2: `Replace` — `ReplaceOption.First`

**Files:**
- Modify: `src/Synto/Matching/MatchReplaceExtensions.cs` (the private rewriter — short-circuit after the first match)
- Test: `test/Synto.Test/Match/MatchReplaceTests.cs`; the `#MatchReplaceExtensions.g.verified.cs` golden may be unaffected (no signature change) — confirm.

**Interfaces:**
- Produces: no new signature — extends `Replace`'s behavior for `option == ReplaceOption.First`.
- Consumes: Task 1's `Replace` + rewriter.
- Contract: **C-R5** (`First` rewrites the earliest match in document order, then short-circuits).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void Replace_First_RewritesOnlyTheFirstMatch() // C-R5
{
    var root = SyntaxFactory.ParseExpression("f(1 + 2, 3 + 4)");
    var rewritten = M.SumPattern.Replace(root, static m => Mul(m), global::Synto.Matching.ReplaceOption.First);
    Assert.Equal("f(1 * 2, 3 + 4)", rewritten.NormalizeWhitespace().ToString()); // only the first Sum rewritten
}

[Fact]
public void Replace_First_ShortCircuits_EvaluatorInvokedExactlyOnce() // C-R5
{
    var root = SyntaxFactory.ParseExpression("f(1 + 2, 3 + 4)");
    var calls = 0;
    M.SumPattern.Replace(root, m => { calls++; return Mul(m); }, global::Synto.Matching.ReplaceOption.First);
    Assert.Equal(1, calls); // does not rewrite-all-then-take-first
}
```

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test --no-build -c Debug` (build first). Expected: `Replace_First_*` FAIL — `First` currently behaves like `All` (both matches rewritten / evaluator called twice).

- [ ] **Step 3: Implement (by contract)**

In the private rewriter, thread the `ReplaceOption`. For `First`: replace the first match encountered in document order, set a "done" flag, and thereafter return nodes unchanged without invoking the matcher or the evaluator again (short-circuit — do not match-all-then-trim). `All` behavior is unchanged.

- [ ] **Step 4: Run tests + gate**

Run the green gate. Expected: both `First` facts PASS; all Task 1 facts still green; goldens unchanged (no signature/surface change — if the injected golden *did* change, confirm it is only the rewriter body and re-accept). Build/format clean.

- [ ] **Step 5: Commit**

```bash
jj commit -m "feat(matching): Pattern.Replace ReplaceOption.First (first match, short-circuit)"
```

---

### Task 3: Realistic end-to-end + Template-as-replacement capstone + final audit

**Files:**
- Test: `test/Synto.Test/Match/MatchReplaceTests.cs` (a realistic compilation-unit rewrite + the Matching + Templating capstone)
- Modify: `docs/superpowers/specs/2026-06-22-match-replace-design.md` (mark the design delivered)

**Interfaces:** Consumes the full surface (Tasks 1–2). Produces no new API.

- [ ] **Step 1: Write the tests**

```csharp
[Fact]
public void Replace_OverCompilationUnit_UsesNodeAndCaptures() // realistic end-to-end
{
    var root = SyntaxFactory.ParseCompilationUnit("class C { int F() => g(1 + 2) + h(3 + 4); }");
    // Every Sum -> Mul; the matched node is available via m.Node (here: assert leftmost-first document order is preserved by All).
    var rewritten = M.SumPattern.Replace(root, static m => Mul(m));
    Assert.Equal("class C { int F() => g(1 * 2) + h(3 * 4); }",
        rewritten.NormalizeWhitespace(indentation: "", eol: " ").ToString().Replace("  ", " ").Trim());
    // (adapt the normalization to the harness's canonical-text helper if one exists.)
}

[Fact]
public void Replace_WithTemplateBuiltReplacement_Composes() // Matching + Templating capstone
{
    // The replacement is built by a Synto [Template] factory rather than raw SyntaxFactory — proving the
    // open Func<Matched<T>, SyntaxNode> lets Templating compose with Matching for free.
    [global::Synto.Templating.Template(typeof(Factory), Options = global::Synto.Templating.TemplateOption.Single)]
    static void MulT([global::Synto.Templating.Inline(AsSyntax = true)] int a, [global::Synto.Templating.Inline(AsSyntax = true)] int b) { _ = a * b; }

    var root = SyntaxFactory.ParseExpression("g(1 + 2)");
    // Extract the `a * b` expression the template produced (adapt the extraction to the exact Template API:
    // Factory.MulT(...) returns the `_ = a * b;`-shaped node; reach its right-hand multiply expression).
    var rewritten = M.SumPattern.Replace(root, static m =>
        ((AssignmentExpressionSyntax)((ExpressionStatementSyntax)Factory.MulT(m.Captures.A, m.Captures.B)).Expression).Right);

    Assert.Equal("g(1 * 2)", rewritten.NormalizeWhitespace().ToString());
}

// at file scope, the Template factory target (mirrors examples/Synto.Examples):
// static partial class Factory { }
```

> If wiring the `[Template]` extraction proves fiddly against the exact Template API, it is acceptable to assert the weaker-but-sufficient fact that a `Factory`-produced `SyntaxNode` is accepted by `Replace` and yields the expected text — the composition is guaranteed by the `Func<…, SyntaxNode>` signature, not by a special code path. Do **not** spend the task fighting the Template API; the realistic end-to-end test above is the load-bearing one.

- [ ] **Step 2: Run to verify fail**, then **Step 3: adjust** — the surface already exists (Tasks 1–2); these tests exercise it end-to-end. If a real gap surfaces (e.g. `m.Node` not threaded, a `First`/`All` document-order surprise), fix it minimally in `MatchReplaceExtensions.cs` and note it.

- [ ] **Step 4: Final whole-feature gate**

Run the full green gate across the repo. Confirm: `dotnet build -c Debug` 0 errors; all tests green (the ForMatch + matching suite plus the new `MatchReplaceTests`); `dotnet format whitespace --verify-no-changes` clean; `dotnet build -c Release` 0 errors with no new RS2008. Review all new/updated goldens once more (only the two additive injected goldens from Task 1 should exist; the matcher snapshots are untouched — `Replace` is pure injected surface, not emitted per pattern).

In `docs/superpowers/specs/2026-06-22-match-replace-design.md`, note the design as delivered (surface + `ReplaceOption { All, First }` + the resolved root-self / short-circuit decisions).

- [ ] **Step 5: Commit**

```bash
jj commit -m "test(matching): Pattern.Replace end-to-end + Template-composition capstone"
```

---

## Self-Review (against the spec)

- **§3 D1 pattern-subject, Regex-parallel `Replace`** → Task 1 (signature) + Tasks 1–2 (behavior). ✓
- **§3 D2 open replacement; Template free** → Task 1 (`Func<…, SyntaxNode>`) + Task 3 (Template capstone). ✓
- **§3 D3 fixed traversal; `ReplaceOption { All, First }` (plain enum, not `[Flags]`)** → Task 1 (enum + All) + Task 2 (First). ✓
- **§3 D4 return-type preserved (`TRoot`)** → Task 1 (`Replace_RootItselfMatches…`, return type in signature). ✓
- **§3 D5 extension placement (injected extensions, data surface clean)** → Task 1 (`MatchReplaceExtensions.cs` Compile-Removed + embedded; `ReplaceOption.cs` compiled + embedded). ✓
- **§5 semantics (outermost-wins, descendants-and-self, First short-circuits)** → Task 1 (`OutermostWins`, `RootItselfMatches`) + Task 2 (`First_ShortCircuits`). ✓
- **§6 contracts:** C-R1 → Task 1; C-R2 → Task 1 (`RootItselfMatches`, return type); C-R3 → imperative-by-construction (no provider variant; nothing cached); C-R4 → Task 1 (`InjectedMatchReplaceSurface_CompilesOn_NetStandard20`); C-R5 → Task 2. ✓
- **§7 edge cases** (capture-less, no-match same-instance, overlapping, root-self) → Task 1 (`NoMatch_ReturnsRootUnchanged`, `RootItselfMatches`, `OutermostWins`). ✓
- **§8 testing** (round-trip All/First, Template-as-replacement, no-match, captures, self-containment, snapshots) → Tasks 1–3. ✓
- **§9 non-goals** (no traversal options, no code-fix/pipeline) → honored: only `All`/`First`; no provider variant; `Replace` is imperative. ✓
- **§10 resolved decisions** (root-self → replace + return replacement; `First` short-circuits) → Task 1 + Task 2. ✓

No placeholders; signatures are consistent across tasks (`Replace<TRoot, TMatch>(this MatchPattern<TMatch>, TRoot, Func<Matched<TMatch>, SyntaxNode>, ReplaceOption)` / `ReplaceOption { All = 0, First = 1 }` / `SyntoMatchReplaceExtensions` used identically throughout).

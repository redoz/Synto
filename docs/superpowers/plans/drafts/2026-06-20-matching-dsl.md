# Matching DSL (v1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **Tracking issue:** #92
> **Spec:** docs/superpowers/specs/2026-06-20-matching-dsl.md

**Goal:** Add `Synto.Matching` — the inverse of Templating — as a sibling feature inside the one umbrella generator: a `[Match<M>]` pattern method is compiled into a bespoke straight-line Roslyn matcher (`M.Pattern(SyntaxNode) -> PatternMatch?`) that structurally recognizes a tree and returns typed captures.

**Architecture:** A new `src/Synto.SourceGenerator/Matching/` folder holds a second `IIncrementalGenerator` (`MatchFactorySourceGenerator`) that mirrors `TemplateFactorySourceGenerator`: all semantic work runs inside a `ForAttributeWithMetadataName` transform keyed on the generic attribute `Synto.Matching.MatchAttribute`1`, flowing out an equatable `MatchGenerationResult` (generated text + `EquatableArray<DiagnosticInfo>`). The consumer marker surface is authored once in `src/Synto/Matching/`, embedded as `Synto.Runtime.*` resources, and injected `internal` by the existing `SurfaceInjectionGenerator` (no new generator, no new package). The emitter (`MatchEmitter`) lowers a pattern via a **generic structural walk**: it tests the candidate node kind-by-kind / child-by-child using `ChildNodesAndTokens()` (no per-`SyntaxKind` switch), stops descending at holes, and captures them — producing readable, steppable, self-contained Roslyn that depends only on `Microsoft.CodeAnalysis`.

**Tech Stack:** C# / .NET 10 SDK, `netstandard2.0`, Roslyn (`Microsoft.CodeAnalysis.CSharp`) 5.0 floor, generic attributes (C# 11) on the consumer side, Verify snapshot tests + `CSharpGeneratorDriver`, xUnit v3 / MTP v2.

## Global Constraints

- **`netstandard2.0`** for the generator and the runtime markers; **Roslyn 5.0 floor**; **no file/network/process/environment work at generation time** — inputs come only from the compilation.
- **Pipeline values are equatable value types.** Never capture `Compilation`, `ISymbol`, `SemanticModel`, or `SyntaxNode` into pipeline state. Do all semantic work inside the FAWMN transform and flow out only an equatable `MatchGenerationResult` (`string? FileName`, `string? Source`, `EquatableArray<DiagnosticInfo>`).
- **Failures become a `DiagnosticInfo` (SY-series), never a thrown exception.** Wrap generation in `try/catch` that converts any escaping exception to `Diagnostics.InternalError` (**SY0000**).
- **Matching is a sibling folder/namespace** under the one umbrella generator (`src/Synto.SourceGenerator/Matching/`, namespace `Synto.Matching`). **No new package.** Consumer markers are injected `internal` under `namespace Synto.Matching`; any emitted helper is `file`-scoped. Generated output is **self-contained** (references only Roslyn — no Synto runtime dependency).
- **Generic-attribute discovery (spike-verified on the Roslyn 5.0 floor — evidence below):** key FAWMN on the arity-suffixed metadata name `` Synto.Matching.MatchAttribute`1 `` and read the matcher target type off `ctx.Attributes[0].AttributeClass!.TypeArguments[0]`. Do **not** add a `[Match(typeof(M))]` fallback — it is unnecessary (see evidence).

  > **Spike evidence (the spec's one must-verify-before-freeze item, §10) — already done, recorded here.** A throwaway spike against **`Microsoft.CodeAnalysis.CSharp` 5.0.0** confirmed end-to-end:
  > - `ForAttributeWithMetadataName("Synto.Matching.MatchAttribute`1", …)` **fires** on a `[Match<M>]`-annotated method. The **backtick-`1` arity suffix is REQUIRED** — the bare name `"Synto.Matching.MatchAttribute"` matches **nothing** (a generic attribute's metadata name carries its arity), so the metadata-name constant must keep the `` `1 `` suffix.
  > - `ctx.Attributes[0].AttributeClass.TypeArguments[0]` resolves to a **real `NamedType`** — for `[Match<int>]` the `int` symbol with **`IsErrorType == false`** — so the matcher target is readable directly off the attribute; no `typeof` ctor-arg round-trip is needed.
  > - **No generator diagnostics** were produced.
  >
  > Conclusion: the generic `[Match<M>]` form works on the 5.0 floor; the `[Match(typeof(M))]` fallback is **not** needed and is intentionally omitted. No spike *task* remains — the result is recorded here as the evidence the plan review (round 1) asked for, replacing the prior unsupported "verified" claim.
- **Output shape is pinned by Verify snapshots.** Every new emission path gets a snapshot test; an unexplained snapshot change is a finding, not a rubber stamp.
- **Diagnostics ID ranges:** Matching's new pattern-specific diagnostics live in the reserved **`SY12xx`** range (SY1201–SY1204 here). Do **not** reuse `SY1008`/`SY1009`. Each descriptor is registered in `AnalyzerReleases.Unshipped.md` **in the same task that adds it** (Tasks 14–17), not deferred to a single end-of-plan task, so each intermediate green-gate stays RS2008-clean.

---

## Execution Model & Branch Isolation (read before running any task)

**This feature does NOT land on `main`.** Issue #92 mandates the entire Matching feature land on the long-lived **`experimental/matching`** branch (the branch this plan, spec, and the unpushed stack already live on). The shipped `Synto` generator package's injected marker surface (the 7 new consumer-facing types — `MatchAttribute<TMatcher>`, `MatchOption`, `CaptureAttribute`, `CaptureAttribute<TNode>`, `Stmt`, `Statement`/`Expr`/`Block`) and the new `SY1201`–`SY1204` diagnostics are a **one-way door** the moment any consumer compiles against a published package (`consequences.md`, `project-phase.md` one-way-doors). They MUST NOT reach `main` until the feature is *deliberately* merged as a whole — an experimental branch exists precisely to hold that surface off the published contract while it is still free to change.

**Branch contract (binds every task):**
- **Base AND push target = `experimental/matching`.** Every task's commit is a *local* commit on `experimental/matching` (or a worktree branched from it), never on `main`. Read every bare `git commit -m "…"` in the tasks below as a **local commit on `experimental/matching`** — **no task pushes**.
- **The per-green ff-push-to-main is suppressed.** github.md's standard `ready→implement` flow rebases each green commit onto `origin/main` and fast-forward-pushes `HEAD` straight to `main`. That path is **disabled** for this plan — it would land 18 partial, mid-development commits *and* the one-way-door surface on `main`. This project does **not** practice full CI yet, so there is no green-gate-to-main to honor; do **not** route this plan through the standard main-pushing flow.
- **How to execute:** **`superpowers:subagent-driven-development` in a throwaway worktree branched from `experimental/matching`** — a fresh subagent per task, commits land on the worktree's branch, never `main`.
  - **Do NOT use `implement-plan` (incl. `{mode:"dry-run"}`).** It hardcodes its worktree base to `origin/main` (`.claude/workflows/implement-plan.js`: `git worktree add … -b plan/<slug> origin/main`). `origin/main` is *before* this feature's 15-commit stack (markers, packaging redesign, .NET 10 baseline), so that worktree cannot compile the Matching code. `dry-run` only relaxes the *push*, not the *base* — it does not help here. The base **must** be `experimental/matching`.
- **Final integration is a single deliberate batched push to `experimental/matching`** (`git push origin HEAD:experimental/matching`) **after the whole plan is green** — not per-commit. Merging `experimental/matching → main` is a separate, later, human decision **outside this plan**.

---

## File Structure

**New runtime markers (authored once, public, in `src/Synto`):**
- `src/Synto/Matching/MatchAttribute.cs` — `public sealed class MatchAttribute<TMatcher> : Attribute`.
- `src/Synto/Matching/MatchOption.cs` — `public enum MatchOption { None, Bare, Single }` (plain, non-`[Flags]`).
- `src/Synto/Matching/CaptureAttribute.cs` — `CaptureAttribute` and `CaptureAttribute<TNode>`.
- `src/Synto/Matching/Markers.cs` — `Stmt` (instance quantifier holder), `Statement` / `Expr` / `Block` (static marker holders).

**New generator (`src/Synto.SourceGenerator/Matching/`):**
- `MatchTrackingNames.cs` — pipeline stage tracking names.
- `MatchGenerationResult.cs` — equatable result struct.
- `MatchInfo.cs` — transform-local model (holds `SemanticModel`/symbols; never flows through the pipeline).
- `MatchFactorySourceGenerator.cs` — the `[Generator]`; FAWMN transform + emit.
- `MatchMarkers.cs` — resolves the marker `INamedTypeSymbol`s + classifies pattern nodes as holes.
- `MatchEmitter.cs` — the pattern→matcher lowering (generic structural walk + capture extraction + record/class emission).
- `MatchDiagnostics.cs` — SY1201–SY1204 descriptors/factories.

**Modified:**
- `src/Synto.SourceGenerator/Synto.SourceGenerator.csproj` — add `Synto.Runtime.*` `<EmbeddedResource>` items for the new markers.
- `src/Synto.SourceGenerator/Diagnostics.cs` — add `LocationInfo`-based overloads of **all four** target-validation factories — `TargetNotDeclaredInSource` (SY1003), `TargetNotClass` (SY1002), `TargetNotPartial` (SY1001), `TargetAncestorNotPartial` (SY1004) — reused for matcher-target validation so Matching mirrors Templating's full four-arm check; DRY — same descriptors/messages, **no new IDs**. (Templating's existing overloads take a `TargetType`, which encodes a `GetReferenceLocation()`; Matching has only the attribute `Location`, hence the `LocationInfo`-based twins.)
- `src/Synto.SourceGenerator/AnalyzerReleases.Unshipped.md` — register each of SY1201/SY1202/SY1203/SY1204 **in the task that adds its descriptor** (SY1201→Task 14, SY1203→Task 15, SY1204→Task 16, SY1202→Task 17), so no intermediate green-gate emits an unregistered descriptor (RS2008). Task 18 only *verifies* all four are present.

**New tests (`test/Synto.Test/Match/`, the existing folder placeholder):**
- `MatchTestHarness.cs` — shared in-memory compilation + driver helpers (mirrors `SimpleTemplateTest`). **Every test file carries `using Synto.Matching;` and `using System.Linq;`** (the `[Match<…>]`/`MatchOption`/`Capture` forms and the LINQ over `Diagnostics` won't compile otherwise — round-1 low). `Run(...)` **asserts the consumer source compiles before generation** (`compilation.GetDiagnostics()` has no errors), mirroring `SimpleTemplateTest.VerifyTemplate`, so a snippet that doesn't bind fails loudly as a bad fixture rather than silently producing no matcher.
- `MatchSnapshotTests.cs` — golden `*.verified.cs` per output shape (Verify).
- `MatchRoundTripTests.cs` — in-process behavioral round-trips (mirrors `RoundTripTests`; the generator already runs as an analyzer on `Synto.Test`). **Round-trip patterns are authored as per-`[Fact]` local functions** (Templating's `RoundTripTests` isolation model), not class-scope methods, so one non-compiling pattern can't gate the whole partial-class test assembly (round-1 low/question). The generator discovers `[Match<M>]` on a local function exactly as it does on a method — `MatchInfo.Create` and the emitter's body extractors already handle `LocalFunctionStatementSyntax` — so the matcher still lands in `partial class M` and the `[Fact]` calls `M.<Pattern>(…)`. (`M` stays a single `private static partial class M { }` shared by the file; only the pattern methods move into their `[Fact]`s.)
- `MatchDiagnosticsTests.cs` — one driver test per SY12xx arm + the full four-arm target validation (SY1001/SY1002/SY1003/SY1004) + a positive `[Match<M>]` type-arg readback + SY0000 + single-pattern and **cross-pattern** cacheability.
- `MatchSurfaceTests.cs` — validates the **public `Synto.Core` marker shape** binds (it references `SyntoCoreAssembly` and runs **no** generator, so it exercises the public Core copy, *not* the injected-internal surface). The real injected-`internal` gate is the existing **`SurfaceInjectionTest`** golden (each Task 1–3 Step 5) plus the round-trips; this file's comment says so explicitly (round-1 testability medium — the prior comment over-claimed).

---

## Design notes the emitter tasks rely on (read once)

**Hole detection (leak-free, by binding — spec §3.1).** Inside the transform we hold the `SemanticModel`, so a pattern node is a *hole* only when it **binds** to a marker:
- **Expression capture** — an `IdentifierNameSyntax` whose `GetSymbolInfo(...).Symbol` is one of the pattern method's `[Capture]`/`[Capture<T>]` parameters.
- **Expression wildcard** — an `InvocationExpressionSyntax` binding to `Expr.Any<T>()`.
- **Statement hole** — an `ExpressionStatementSyntax` wrapping an invocation of a quantifier (`One/Opt/Some/All/Exactly`) on either a `[Capture] Stmt` parameter (capture) or the static `Statement` holder (wildcard).
- **Anchor** — an `ExpressionStatementSyntax` wrapping `Block.Start()` / `Block.End()`.

Everything else is **literal** shape, matched exactly. Detection is by symbol, never by type/shape, so literal `foreach`/`default`/`StatementSyntax` locals in matched code never collide.

**Marker resolution (symbol identity, not string-matching — mirrors the existing finders).** `MatchMarkers.Create` resolves each marker type **once** into an `INamedTypeSymbol` via `Compilation.GetTypeByMetadataName(typeof(global::Synto.Matching.X).FullName!)` and classifies holes by **`SymbolEqualityComparer.Default.Equals(...)`**, never by `ToDisplayString()` string equality. This is exactly how `SyntaxParameterFinder.cs` (`GetTypeByMetadataName(typeof(Synto.Templating.Syntax).FullName!)`, and for the generic `Syntax<>` the unbound `ConstructUnboundGenericType()` compared via `SymbolEqualityComparer.Default` at `:79`) and `InlinedParameterFinder.cs` already resolve their markers — Matching does **not** introduce a parallel format-dependent path. The unbound generic `CaptureAttribute<>` is resolved with `…GetTypeByMetadataName(typeof(CaptureAttribute<>).FullName!).ConstructUnboundGenericType()` and an attribute's `AttributeClass.ConstructUnboundGenericType()` is compared against it. (The generator project already references `Synto.Core`/`src/Synto`, so `typeof(global::Synto.Matching.*)` binds — same as Templating's `typeof(InlineAttribute)`.)

**Generic structural walk (`MatchEmitter.EmitNodeMatch`).** Two trees are structurally equal (trivia-insensitive, the same notion as `SyntaxNode.IsEquivalentTo(other, topLevel:false)`) iff they have the same `RawKind`, the same number of `ChildNodesAndTokens()`, equal token texts, and structurally-equal child nodes. The emitter unrolls this comparison at generation time over the *known* pattern tree, emitting `if (...) return null;` guards against a runtime candidate, and at a hole position it **captures** instead of comparing. Because `ChildSyntaxList` (returned by `ChildNodesAndTokens()`) has an `int` indexer returning `SyntaxNodeOrToken`, child navigation is generic — **no per-`SyntaxKind` switch is needed**.

> **Kind guard is load-bearing — assert `RawKind`, not just the .NET type (plan-review medium).** At each node position the emitter must guard BOTH the .NET type (`is not {TypeName} {tmp}` — needed to bind `{tmp}` for child navigation) **and** the `RawKind` (`{tmp}.IsKind(SyntaxKind.{pattern.Kind()})`). Checking only the .NET type name over-accepts kinds that share a type but differ in kind — e.g. `ArrayInitializerExpression` vs `CollectionInitializerExpression` (both `InitializerExpressionSyntax`, both tokenizing as `{ 1 , 2 }`), or the several `BinaryExpressionSyntax` kinds (`AddExpression`/`SubtractExpression`/…) — which would silently mis-match. (The binary-operator *token* text is also compared, but the kind guard is the correct, direct check and closes the design-vs-impl gap the structural-equality note promises.)
>
> **`IsKind` availability on the 5.0 floor (author question).** `CSharpExtensions.IsKind` is defined for **both** `SyntaxNode?` and `SyntaxNodeOrToken` in `Microsoft.CodeAnalysis.CSharp` (present since Roslyn 1.0, so available on the 5.0 floor and imported into generated output). So both `{tmp}.IsKind(SyntaxKind.X)` (node position) and `{list}[{i}].IsKind(SyntaxKind.X)` (the token branch, where `{list}[{i}]` is a `SyntaxNodeOrToken`) bind without an `.AsToken()`/`.Kind()` workaround.
>
> **Generated indexing is deliberately readable, not micro-optimized (round-1 low, decided).** The emitted matcher indexes `ChildNodesAndTokens()` / `Statements[..]` directly and uses `Skip/Take` for slices. This is **Shinn-style straight-line, "reads like hand-written" code — the spec's explicit emission goal (§5)** — and it runs at the **consumer's runtime** (when they call `M.Foo(node)`), **not** in the generator/incremental hot path, so it is outside Synto's caching-performance concern. Binding each child once or pre-sizing slices would churn **every** snapshot golden for negligible benefit, so it is **deliberately deferred** (the shape is snapshot-frozen); revisit only if a consumer profiles a real matcher hot loop.

**Accessor type contract (the walk's one invariant — plan-review high).** Every `accessor` string threaded through `EmitNodeMatch`/`EmitCapture`/`EmitStatementCapture` is a C# expression whose **static type is one of exactly two**: `SyntaxNode` (the `node` parameter, and every node-child boundary, projected via `…ChildNodesAndTokens()[i].AsNode()`) or `StatementSyntax` (the `_blk.Statements[i]` indexer in the statement/Bare/None paths). It is **never** a raw `SyntaxNodeOrToken` (tokens are consumed inline by the token branch) and **never** double-projected. The rules that keep this true:
- **Node-child recursion passes the `.AsNode()` accessor unchanged** — `EmitNodeMatch(body, $"{listVar}[{i}].AsNode()", child.AsNode()!, ctx)`. A hole branch reached through that recursion **must not append a second `.AsNode()`** (the round-1 high: `accessor + ".AsNode()"` → `…AsNode().AsNode()`, CS1061).
- **Holes type-narrow *at* the hole**, regardless of the accessor's static type: a capture/statement-capture emits `if ({accessor} is not {MemberType} {local}) return null;` (mirroring `EmitCapture`). The `is`-pattern both rejects a non-matching node **and** binds a `{MemberType}`-typed local, so a captured statement binds to a `StatementSyntax` record member with **no CS0266** even when `{accessor}` is statically `SyntaxNode`. This makes `EmitStatementCapture` correct at **both** its call sites — the `StatementSyntax`-typed `_blk.Statements[i]` indexer (the `is not StatementSyntax` is a redundant-but-harmless true-narrow) **and** the `SyntaxNode`-typed `…AsNode()` embedded-statement child (where it is the load-bearing narrow). Never bind with a bare `var {local} = {accessor};` into a typed member.

**Emitter-core design — `EmitAnchoredRun` + `MatchContext` (pinned up front; filled in by Tasks 11–17, never re-plumbed).** Statement-`Single`, `Bare`, and `None` all lower to **one** shared run-alignment core so later tasks add *behavior*, not *contract*. `MatchContext` is introduced in its **final shape at Task 5** (incl. `Diagnostics`/`Aborted`, unused until the diagnostic tasks); the `EmitAnchoredRun` **signature is pinned at Task 11** and only its body grows (12 adds the variable split, 13 the `anchorStart`/`anchorEnd` exact-bound, 14 threads real anchors, 16/17 use `ctx.Diagnostics`/`ctx.Aborted`). Neither gains fields after it is introduced:

```csharp
// the single alignment core — final signature pinned at Task 11:
private static void EmitAnchoredRun(
    List<StatementSyntax> body,
    IReadOnlyList<StatementSyntax> statements,   // RAW pattern statements (kept for diagnostic Locations)
    List<RunElement> run,                         // anchors already extracted
    MatchContext ctx,
    bool anchorStart, bool anchorEnd);

// MatchContext — final shape pinned at Task 5 (see Task 5's MatchEmitter listing):
//   required MatchInfo Info; required MatchMarkers Markers;
//   List<Capture> Captures; HashSet<string> BoundCaptureLocals;
//   List<DiagnosticInfo> Diagnostics; bool Aborted; string NextTmp();
```

`Emit` wires `ctx.Diagnostics` into the outer `diagnostics` list and, after each option branch, does `if (ctx.Aborted) return null;` (diagnostics-only emit, like Templating's converter-error bail). Tasks 12–17 **fill in** these fields; they do **not** change the signature or add fields. Statement-`Single` (Task 10) and the fixed/variable Bare paths (Tasks 11/12) all route through `EmitAnchoredRun` (Task 10's single-statement case is a one-element run with `anchorStart:false, anchorEnd:false`).

**Generated shape (frozen, snapshot-pinned).**
- File name: `{TargetFullName}.{PatternName}.g.cs` (mirrors Templating).
- A top-level `public sealed record {PatternName}Match(...members...)` in the target's namespace; members are the `[Capture]` params **in signature order**, typed per their placement.
- A `partial class {TargetName} { public static {PatternName}Match? {PatternName}(SyntaxNode node) { ...guards...; return new {PatternName}Match(...); } }`, wrapped in the target's ancestry via `WithAncestryFrom`.
- Usings: `Microsoft.CodeAnalysis`, `Microsoft.CodeAnalysis.CSharp`, `Microsoft.CodeAnalysis.CSharp.Syntax` (plus `System.Linq` once Task 11 emits `Skip`/`Take` slices); emitted under `#nullable enable`; normalized with `NormalizeWhitespace()` then `SyntaxFormatter.Format(...)`.
- Capture local naming: `cap_{paramName}`; record member naming: `{ParamName}` with the first letter upper-cased.

> **One-way-door surface decisions (confirmed frozen — plan-review question).** These are deliberately permanent and snapshot-pinned, chosen for reuse by the deferred `foreach` repetition (§3.7) without a reshape:
> - **Captured statement-run member type is `SyntaxList<StatementSyntax>`** (and `StatementSyntax?` for `Opt`, `StatementSyntax` for `One`) — the spec's §3.4 table, anchored across the quantifiers and the deferred `foreach` (spec §10). Not `IReadOnlyList<T>`.
> - **Result type is a `public sealed record {PatternName}Match(...)`** — a sibling top-level record *class* in the target's namespace.
> - **Pattern-name uniqueness per target is a real constraint.** The matcher method `{PatternName}(SyntaxNode)` and the record `{PatternName}Match` are keyed on the pattern method's **name**; two `[Match<M>]` patterns with the **same method name** targeting the **same** class collide (CS0111 on the matcher method, duplicate record) — the same shape as two same-named partial methods. v1 treats this as expected author error (the host C# compiler reports the collision on the generated members); a dedicated `SY12xx` "duplicate pattern name" diagnostic is a **deferred** nicety, not a v1 gate. Distinct targets, or distinct pattern names, never collide.
>
> **Namespace placement of generator-internal Matching types (plan-review question / low).** `MatchEmitter`/`MatchMarkers`/`MatchInfo`/`MatchGenerationResult`/`MatchFactorySourceGenerator`/`MatchDiagnostics`/`MatchTrackingNames` live in **`namespace Synto.Matching`** (feature-scoped), a deliberate, low-risk divergence from Templating's flat `namespace Synto` for its internals. Rationale: architecture.md names the feature namespaces `Synto.Templating.*` / `Synto.Matching.*`, and these are generator-assembly-internal types that never reach consumer output, so they cannot collide with the *consumer* `Synto.Matching` markers (which live in the referenced `Synto.Core` assembly, a different assembly). This keeps the whole feature in one navigable namespace. (`Diagnostics.cs` stays in `namespace Synto` — it is shared with Templating and reused via the new overloads.)

---

### Task 1: Marker surface — `MatchAttribute<TMatcher>` + `MatchOption`

**Files:**
- Create: `src/Synto/Matching/MatchOption.cs`
- Create: `src/Synto/Matching/MatchAttribute.cs`
- Modify: `src/Synto.SourceGenerator/Synto.SourceGenerator.csproj` (add two `<EmbeddedResource>` items)
- Test: `test/Synto.Test/Match/MatchSurfaceTests.cs`

**Interfaces:**
- Produces: `Synto.Matching.MatchOption { None = 0, Bare = 1, Single = 2 }`; `Synto.Matching.MatchAttribute<TMatcher> : Attribute` with ctor `(MatchOption option = MatchOption.None)` and property `MatchOption Option`. Metadata name of the injected/embedded type: `` Synto.Matching.MatchAttribute`1 ``.

- [ ] **Step 1: Write the failing test**

`test/Synto.Test/Match/MatchSurfaceTests.cs`:

```csharp
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Synto.Test.Match;

public class MatchSurfaceTests
{
    private static readonly MetadataReference Corlib = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
    private static readonly MetadataReference NetStandard = MetadataReference.CreateFromFile(Assembly.Load("netstandard, Version=2.0.0.0").Location);
    private static readonly MetadataReference SystemRuntime = MetadataReference.CreateFromFile(Assembly.Load("System.Runtime, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location);

    // Validates the PUBLIC Synto.Core marker shape: this compilation references SyntoCoreAssembly (the public
    // Core markers) and runs NO generator, so it binds the public Core copy — it does NOT exercise the
    // injected-INTERNAL surface. The injected-internal gate is the SurfaceInjectionTest golden (Task 1–3
    // Step 5) plus the round-trips; this test only proves the consumer-facing forms are shaped to compile.
    [Fact]
    public void InjectedMatchAttributeAndOptionBind()
    {
        var compilation = CSharpCompilation.Create("Test",
            new[]
            {
                CSharpSyntaxTree.ParseText(
                    """
                    using Synto.Matching;
                    partial class M { }
                    static class Patterns {
                        [Match<M>(MatchOption.Bare)]
                        static void P() { }
                    }
                    """),
            },
            new[] { Corlib, NetStandard, SystemRuntime, MetadataReference.CreateFromFile(SyntoCoreAssembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.Empty(errors);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Synto.Test --filter "FullyQualifiedName~MatchSurfaceTests.InjectedMatchAttributeAndOptionBind"`
Expected: FAIL — compilation errors (`MatchOption`/`Match<>` not found): the marker types do not exist yet.

- [ ] **Step 3: Write minimal implementation**

`src/Synto/Matching/MatchOption.cs`:

```csharp
namespace Synto.Matching;

/// <summary>
/// Selects which slice of a <c>[Match]</c> pattern method is the shape to recognize. Unlike Templating's
/// <c>[Flags]</c> <c>TemplateOption</c>, these are mutually-exclusive cardinalities (one node vs a run vs a
/// whole declaration), so this is a plain enum.
/// </summary>
public enum MatchOption
{
    /// <summary>Match a method / local-function declaration, fully bounded by its own braces.</summary>
    None = 0,

    /// <summary>Match a run of statements contained in a block (regex-like "contains" unless anchored).</summary>
    Bare = 1,

    /// <summary>Match a single statement (block-rooted) or, for an expression-bodied pattern, the handed expression node.</summary>
    Single = 2,
}
```

`src/Synto/Matching/MatchAttribute.cs`:

```csharp
using System;

namespace Synto.Matching;

/// <summary>
/// Marks a method as a Synto match pattern. <typeparamref name="TMatcher"/> is the partial class that
/// receives the generated <c>static PatternMatch? Pattern(SyntaxNode node)</c> matcher. The pattern method's
/// own name names the matcher; it is never matched literally.
/// </summary>
/// <typeparam name="TMatcher">The partial class the generated matcher is emitted into.</typeparam>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class MatchAttribute<TMatcher> : Attribute
{
    public MatchAttribute(MatchOption option = MatchOption.None)
    {
        Option = option;
    }

    /// <summary>Which slice of the pattern method is the shape (see <see cref="MatchOption"/>).</summary>
    public MatchOption Option { get; }
}
```

Add to `src/Synto.SourceGenerator/Synto.SourceGenerator.csproj`, inside the existing `Synto.Runtime.*` `<ItemGroup>` (the block that already embeds `TemplateAttribute.cs` etc.):

```xml
    <EmbeddedResource Include="..\Synto\Matching\MatchOption.cs" LogicalName="Synto.Runtime.MatchOption.cs" />
    <EmbeddedResource Include="..\Synto\Matching\MatchAttribute.cs" LogicalName="Synto.Runtime.MatchAttribute.cs" />
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Synto.Test --filter "FullyQualifiedName~MatchSurfaceTests.InjectedMatchAttributeAndOptionBind"`
Expected: PASS. (`SurfaceInjectionGenerator` auto-injects every `Synto.Runtime.*` resource, so the new markers are now injected `internal` into the test compilation and bind.)

- [ ] **Step 5: Accept the surface snapshot, then commit**

The existing `SurfaceInjectionTest.VerifyInjectedSurface` now emits two new injected files. Run it, review the new `*.received.cs` under `test/Synto.Test/snapshots/` (confirm `internal enum MatchOption` and `internal sealed class MatchAttribute<TMatcher>`), then accept them as goldens.

Run: `dotnet test test/Synto.Test --filter "FullyQualifiedName~SurfaceInjectionTest"`
Expected: FAIL first (new received files) → review → accept (`*.received.cs` → `*.verified.cs`) → PASS.

```bash
git add src/Synto/Matching/MatchOption.cs src/Synto/Matching/MatchAttribute.cs \
        src/Synto.SourceGenerator/Synto.SourceGenerator.csproj \
        test/Synto.Test/Match/MatchSurfaceTests.cs \
        test/Synto.Test/snapshots/SurfaceInjectionTest.VerifyInjectedSurface#MatchOption.g.verified.cs \
        test/Synto.Test/snapshots/SurfaceInjectionTest.VerifyInjectedSurface#MatchAttribute.g.verified.cs
git commit -m "feat(matching): inject Match<TMatcher> attribute + MatchOption marker surface"
```

---

### Task 2: Marker surface — `CaptureAttribute` + `CaptureAttribute<TNode>`

**Files:**
- Create: `src/Synto/Matching/CaptureAttribute.cs`
- Modify: `src/Synto.SourceGenerator/Synto.SourceGenerator.csproj`
- Test: `test/Synto.Test/Match/MatchSurfaceTests.cs` (add a `[Fact]`)

**Interfaces:**
- Produces: `Synto.Matching.CaptureAttribute : Attribute` (`AttributeTargets.Parameter`); `Synto.Matching.CaptureAttribute<TNode> : Attribute` (`AttributeTargets.Parameter`). Metadata names `Synto.Matching.CaptureAttribute` and `` Synto.Matching.CaptureAttribute`1 ``.

- [ ] **Step 1: Write the failing test** — add to `MatchSurfaceTests`:

```csharp
    [Fact]
    public void InjectedCaptureAttributesBind()
    {
        var compilation = CSharpCompilation.Create("Test",
            new[]
            {
                CSharpSyntaxTree.ParseText(
                    """
                    using Microsoft.CodeAnalysis.CSharp.Syntax;
                    using Synto.Matching;
                    partial class M { }
                    static class Patterns {
                        [Match<M>(MatchOption.Single)]
                        static object P([Capture] object x, [Capture<BinaryExpressionSyntax>] object y) => x;
                    }
                    """),
            },
            new[]
            {
                Corlib, NetStandard, SystemRuntime,
                MetadataReference.CreateFromFile(SyntoCoreAssembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.CSharp.Syntax.BinaryExpressionSyntax).Assembly.Location),
            },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Synto.Test --filter "FullyQualifiedName~MatchSurfaceTests.InjectedCaptureAttributesBind"`
Expected: FAIL — `Capture`/`Capture<>` not found.

- [ ] **Step 3: Write minimal implementation**

`src/Synto/Matching/CaptureAttribute.cs`:

```csharp
using System;

namespace Synto.Matching;

/// <summary>
/// Marks a pattern-method parameter as an expression hole. The parameter's declared CLR type is
/// compile-glue + IntelliSense only; the captured result is an <c>ExpressionSyntax</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class CaptureAttribute : Attribute
{
}

/// <summary>
/// Marks a pattern-method parameter as an expression hole narrowed to <typeparamref name="TNode"/>: the
/// position only matches that node kind, and the captured result member is typed <typeparamref name="TNode"/>.
/// </summary>
/// <typeparam name="TNode">The Roslyn syntax-node type the hole must match and is captured as.</typeparam>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class CaptureAttribute<TNode> : Attribute
{
}
```

Add to the csproj `Synto.Runtime.*` group:

```xml
    <EmbeddedResource Include="..\Synto\Matching\CaptureAttribute.cs" LogicalName="Synto.Runtime.CaptureAttribute.cs" />
```

> Note: the one `CaptureAttribute.cs` file declares **both** the non-generic and generic attribute. `SurfaceInjectionGenerator.PublicToInternalRewriter` rewrites every top-level type in the resource, so both become `internal`. The injected hint name is `CaptureAttribute.g.cs`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Synto.Test --filter "FullyQualifiedName~MatchSurfaceTests.InjectedCaptureAttributesBind"`
Expected: PASS.

- [ ] **Step 5: Accept the new surface snapshot + commit**

Run `SurfaceInjectionTest`, review/accept `SurfaceInjectionTest.VerifyInjectedSurface#CaptureAttribute.g.verified.cs` (must contain both `internal sealed class CaptureAttribute` and `internal sealed class CaptureAttribute<TNode>`).

```bash
git add src/Synto/Matching/CaptureAttribute.cs src/Synto.SourceGenerator/Synto.SourceGenerator.csproj \
        test/Synto.Test/Match/MatchSurfaceTests.cs \
        test/Synto.Test/snapshots/SurfaceInjectionTest.VerifyInjectedSurface#CaptureAttribute.g.verified.cs
git commit -m "feat(matching): inject Capture / Capture<TNode> hole markers"
```

---

### Task 3: Marker surface — `Stmt`, `Statement`, `Expr`, `Block`

**Files:**
- Create: `src/Synto/Matching/Markers.cs`
- Modify: `src/Synto.SourceGenerator/Synto.SourceGenerator.csproj`
- Test: `test/Synto.Test/Match/MatchSurfaceTests.cs` (add a `[Fact]`)

**Interfaces:**
- Produces (all under `namespace Synto.Matching`):
  - `Stmt` — instance quantifier holder: `StatementSyntax One()`, `StatementSyntax? Opt()`, `SyntaxList<StatementSyntax> Some()`, `SyntaxList<StatementSyntax> All()`, `SyntaxList<StatementSyntax> Exactly(int n)`.
  - `Statement` — static wildcard holder: same five verbs, `static`, returning `void`.
  - `Expr` — static `T Any<T>()`.
  - `Block` — static `void Start()`, `void End()`.

> The return types are compile-glue for the *pattern body* (these methods are never executed — the body is phantom). `Stmt`'s instance verbs return Roslyn types only so a future direct caller sees the captured-shape type; using them as statement-position invocations is legal regardless. They reference `Microsoft.CodeAnalysis` types, so the marker file (and the injected copy) needs `using Microsoft.CodeAnalysis;` — the consumer already references Roslyn (they are a Roslyn generator author).

- [ ] **Step 1: Write the failing test** — add to `MatchSurfaceTests`:

```csharp
    [Fact]
    public void InjectedQuantifierAndWildcardMarkersBind()
    {
        var compilation = CSharpCompilation.Create("Test",
            new[]
            {
                CSharpSyntaxTree.ParseText(
                    """
                    using Synto.Matching;
                    partial class M { }
                    static class Patterns {
                        [Match<M>(MatchOption.Bare)]
                        static void P([Capture] bool cond, [Capture] Stmt guard, [Capture] Stmt rest)
                        {
                            if (cond)
                                guard.One();
                            Statement.One();
                            rest.All();
                            _ = Expr.Any<bool>();
                            Block.End();
                        }
                    }
                    """),
            },
            new[]
            {
                Corlib, NetStandard, SystemRuntime,
                MetadataReference.CreateFromFile(SyntoCoreAssembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.SyntaxNode).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.CSharp.Syntax.StatementSyntax).Assembly.Location),
            },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Synto.Test --filter "FullyQualifiedName~MatchSurfaceTests.InjectedQuantifierAndWildcardMarkersBind"`
Expected: FAIL — `Stmt`/`Statement`/`Expr`/`Block` not found.

- [ ] **Step 3: Write minimal implementation**

`src/Synto/Matching/Markers.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto.Matching;

/// <summary>
/// The type of a <c>[Capture]</c> statement hole. Referenced at a statement position via a fluent
/// quantifier — <c>guard.One()</c>, <c>tail.Opt()</c>, <c>body.Some()</c>, <c>rest.All()</c>,
/// <c>mid.Exactly(n)</c> — which determines the captured member's type. The bodies never run (the pattern
/// is phantom); the return types document the captured shape.
/// </summary>
public sealed class Stmt
{
    private Stmt() { }

    public StatementSyntax One() => null!;
    public StatementSyntax? Opt() => null;
    public SyntaxList<StatementSyntax> Some() => default;
    public SyntaxList<StatementSyntax> All() => default;
    public SyntaxList<StatementSyntax> Exactly(int n) => default;
}

/// <summary>
/// Statement wildcard markers — match a run/one/optional statement without capturing. The static twin of
/// <see cref="Stmt"/>'s instance verbs (C# forbids a static and instance method of the same name on one type,
/// hence two surfaces sharing the verbs).
/// </summary>
public static class Statement
{
    public static void One() { }
    public static void Opt() { }
    public static void Some() { }
    public static void All() { }
    public static void Exactly(int n) { }
}

/// <summary>Expression wildcard marker — matches any expression of compile-glue type <typeparamref name="T"/> without capturing.</summary>
public static class Expr
{
    public static T Any<T>() => default!;
}

/// <summary>Block-scope anchors for <c>Bare</c>/<c>Single</c> patterns: pin the matched run to the first/last edge of its block.</summary>
public static class Block
{
    /// <summary>The matched run is first in its block.</summary>
    public static void Start() { }

    /// <summary>The matched run is last in its block.</summary>
    public static void End() { }
}
```

Add to the csproj `Synto.Runtime.*` group:

```xml
    <EmbeddedResource Include="..\Synto\Matching\Markers.cs" LogicalName="Synto.Runtime.Markers.cs" />
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Synto.Test --filter "FullyQualifiedName~MatchSurfaceTests.InjectedQuantifierAndWildcardMarkersBind"`
Expected: PASS.

- [ ] **Step 5: Accept the new surface snapshot + commit**

Run `SurfaceInjectionTest`, review/accept `SurfaceInjectionTest.VerifyInjectedSurface#Markers.g.verified.cs` (all four marker types `internal`).

```bash
git add src/Synto/Matching/Markers.cs src/Synto.SourceGenerator/Synto.SourceGenerator.csproj \
        test/Synto.Test/Match/MatchSurfaceTests.cs \
        test/Synto.Test/snapshots/SurfaceInjectionTest.VerifyInjectedSurface#Markers.g.verified.cs
git commit -m "feat(matching): inject Stmt/Statement/Expr/Block quantifier + anchor markers"
```

---

### Task 4: Pipeline scaffold + matcher-target validation

**Files:**
- Create: `src/Synto.SourceGenerator/Matching/MatchTrackingNames.cs`
- Create: `src/Synto.SourceGenerator/Matching/MatchGenerationResult.cs`
- Create: `src/Synto.SourceGenerator/Matching/MatchInfo.cs`
- Create: `src/Synto.SourceGenerator/Matching/MatchFactorySourceGenerator.cs`
- Modify: `src/Synto.SourceGenerator/Diagnostics.cs` (add `Location`-based overloads)
- Create: `test/Synto.Test/Match/MatchTestHarness.cs`
- Test: `test/Synto.Test/Match/MatchDiagnosticsTests.cs`

**Interfaces:**
- Consumes: `EquatableArray<DiagnosticInfo>`, `DiagnosticInfo`, `LocationInfo`, `Diagnostics.InternalError` (existing).
- Produces:
  - `MatchTrackingNames.Transform` / `.Result` (`const string`).
  - `MatchGenerationResult(string? FileName, string? Source, EquatableArray<DiagnosticInfo> Diagnostics)`.
  - `MatchInfo` with `SemanticModel SemanticModel`, `AttributeSyntax AttributeSyntax`, `INamedTypeSymbol Target`, `IMethodSymbol PatternSymbol`, `SyntaxNode PatternSyntax`, `string PatternName`, `MatchOption Option`, and a static `MatchInfo? Create(GeneratorAttributeSyntaxContext)`.
  - `MatchFactorySourceGenerator : IIncrementalGenerator`.
  - `LocationInfo`-based overloads of all four target-validation factories: `Diagnostics.TargetNotDeclaredInSource` (SY1003), `Diagnostics.TargetNotClass` (SY1002), `Diagnostics.TargetNotPartial` (SY1001), `Diagnostics.TargetAncestorNotPartial` (SY1004) — reusing the existing descriptors, so `ValidateTarget` mirrors Templating's full four-arm check.

- [ ] **Step 1: Write the failing tests**

`test/Synto.Test/Match/MatchTestHarness.cs`:

```csharp
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Synto.Matching;   // MatchFactorySourceGenerator lives in namespace Synto.Matching

namespace Synto.Test.Match;

/// <summary>
/// In-memory compilation + driver harness for the Matching generator, mirroring SimpleTemplateTest: the
/// consumer source binds against the PUBLIC Synto.Core markers (via SyntoCoreAssembly), and only
/// MatchFactorySourceGenerator runs (snapshot/diagnostic stability).
/// </summary>
internal static class MatchTestHarness
{
    private static readonly MetadataReference Corlib = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
    private static readonly MetadataReference NetStandard = MetadataReference.CreateFromFile(Assembly.Load("netstandard, Version=2.0.0.0").Location);
    private static readonly MetadataReference SystemRuntime = MetadataReference.CreateFromFile(Assembly.Load("System.Runtime, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location);
    private static readonly MetadataReference Roslyn = MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.SyntaxNode).Assembly.Location);
    private static readonly MetadataReference RoslynCSharp = MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax).Assembly.Location);

    public static CSharpCompilation CreateCompilation(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: "Pattern.cs");
        return CSharpCompilation.Create("Test",
            new[] { tree },
            new[] { Corlib, NetStandard, SystemRuntime, Roslyn, RoslynCSharp, MetadataReference.CreateFromFile(SyntoCoreAssembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    public static GeneratorDriver Run(string source)
    {
        var compilation = CreateCompilation(source);

        // Assert the CONSUMER source compiles before generation (mirrors SimpleTemplateTest.VerifyTemplate):
        // a fixture that doesn't bind is a bad test, not a generator result — fail it loudly here rather than
        // silently produce no matcher. (Excludes generated-code errors — only the input tree is checked.)
        var consumerErrors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.Empty(consumerErrors);

        var driver = CSharpGeneratorDriver.Create(new MatchFactorySourceGenerator());
        return driver.RunGenerators(compilation);
    }

    public static System.Collections.Immutable.ImmutableArray<Diagnostic> Diagnostics(string source)
        => Run(source).GetRunResult().Diagnostics;
}
```

> The consumer-compile assert applies to the **diagnostic/snapshot** harness only. The negative target-validation tests (struct / non-partial / non-source target) deliberately feed source that *does* compile as plain C# (a non-partial class, a struct, a nested matcher) — the matcher misuse is semantic, caught by `ValidateTarget`, not by the C# compiler — so the assert holds for them too.

`test/Synto.Test/Match/MatchDiagnosticsTests.cs`:

```csharp
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Synto.Matching;   // MatchFactorySourceGenerator / MatchTrackingNames live in namespace Synto.Matching

namespace Synto.Test.Match;

public class MatchDiagnosticsTests
{
    [Fact]
    public void TargetNotPartial_ReportsSY1001()
    {
        var diagnostics = MatchTestHarness.Diagnostics(
            """
            using Synto.Matching;
            class M { }            // a class, in source, but not partial -> SY1001
            static class Patterns {
                [Match<M>(MatchOption.Single)]
                static object P([Capture] int a) => a;
            }
            """);

        var diag = Assert.Single(diagnostics, d => d.Id == "SY1001");
        Assert.NotEqual(Location.None, diag.Location);
        Assert.False(diag.Location.SourceSpan.IsEmpty);
    }

    [Theory]                                    // arm 2: struct/record-struct/interface target -> SY1002 (NOT SY1003)
    [InlineData("struct")]
    [InlineData("record struct")]
    [InlineData("interface")]
    public void TargetNotClass_ReportsSY1002(string kind)
    {
        var diagnostics = MatchTestHarness.Diagnostics(
            $$"""
            using Synto.Matching;
            partial {{kind}} M { }
            static class Patterns {
                [Match<M>(MatchOption.Single)]
                static object P([Capture] int a) => a;
            }
            """);

        // The target IS declared in source, so SY1003 would be wrong; it must be SY1002 (not a class).
        Assert.Single(diagnostics, d => d.Id == "SY1002");
        Assert.DoesNotContain(diagnostics, d => d.Id == "SY1003");
    }

    [Fact]                                      // arm 4: matcher nested under a NON-partial ancestor -> SY1004
    public void TargetAncestorNotPartial_ReportsSY1004()
    {
        var diagnostics = MatchTestHarness.Diagnostics(
            """
            using Synto.Matching;
            class Outer {                 // ancestor not partial
                public partial class M { }
            }
            static class Patterns {
                [Match<Outer.M>(MatchOption.Single)]
                static object P([Capture] int a) => a;
            }
            """);

        var diag = Assert.Single(diagnostics, d => d.Id == "SY1004");
        Assert.NotEqual(Location.None, diag.Location);
        Assert.False(diag.Location.SourceSpan.IsEmpty);
    }

    [Fact]   // positive end-to-end: a well-formed [Match<M>] reads the type arg and emits an M.<Pattern> matcher
    public void WellFormedMatch_ReadsTypeArg_AndEmitsMatcher()
    {
        var result = MatchTestHarness.Run(
            """
            using Synto.Matching;
            partial class M { }
            static class Patterns {
                [Match<M>(MatchOption.Single)]
                static object One() => 1;
            }
            """).GetRunResult();

        Assert.Empty(result.Diagnostics);                                  // no validation/internal error
        var generated = Assert.Single(result.GeneratedTrees);              // exactly one matcher file
        var text = generated.GetText().ToString();
        Assert.Contains("partial class M", text);                          // emitted into the type-arg target M
        Assert.Contains("One(SyntaxNode node)", text);                     // the matcher method named for the pattern
    }

    [Fact]
    public void Generator_IsIncremental_OnUnrelatedEdit()
    {
        const string source =
            """
            using Synto.Matching;
            partial class M { }
            static class Patterns {
                [Match<M>(MatchOption.Single)]
                static object Sum([Capture] int a, [Capture] int b) => a + b;
            }
            """;

        var compilation = MatchTestHarness.CreateCompilation(source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { new MatchFactorySourceGenerator().AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));
        driver = driver.RunGenerators(compilation);

        var modified = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("namespace Other { internal sealed class Unrelated { } }"));
        driver = driver.RunGenerators(modified);

        var result = driver.GetRunResult().Results.Single();
        foreach (var trackingName in new[] { MatchTrackingNames.Transform, MatchTrackingNames.Result })
        {
            Assert.True(result.TrackedSteps.ContainsKey(trackingName), $"no tracked step '{trackingName}'");
            var outputs = result.TrackedSteps[trackingName].SelectMany(step => step.Outputs);
            Assert.All(outputs, o => Assert.True(
                o.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                $"step '{trackingName}' had reason {o.Reason}"));
        }
    }

    [Fact]   // cross-pattern: editing one [Match] pattern must leave the OTHER's Transform/Result Cached
    public void Generator_IsIncremental_AcrossPatterns_OnEditingOne()
    {
        const string source =
            """
            using Synto.Matching;
            partial class M { }
            static class Patterns {
                [Match<M>(MatchOption.Single)] static object A([Capture] int a) => a;
                [Match<M>(MatchOption.Single)] static object B([Capture] int b) => b;
            }
            """;

        var compilation = MatchTestHarness.CreateCompilation(source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { new MatchFactorySourceGenerator().AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));
        driver = driver.RunGenerators(compilation);

        // Edit ONLY pattern B's body (a -> a, b -> b + 1); A's transform/result must stay cached.
        var edited = compilation.ReplaceSyntaxTree(
            compilation.SyntaxTrees.Single(),
            CSharpSyntaxTree.ParseText(source.Replace("=> b;", "=> b + 1;"), path: "Pattern.cs"));
        driver = driver.RunGenerators(edited);

        var result = driver.GetRunResult().Results.Single();
        // At least one Transform/Result output stays Cached/Unchanged (pattern A's), proving per-pattern caching.
        foreach (var trackingName in new[] { MatchTrackingNames.Transform, MatchTrackingNames.Result })
        {
            var outputs = result.TrackedSteps[trackingName].SelectMany(s => s.Outputs).ToArray();
            Assert.Contains(outputs, o => o.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged);
        }
    }
}
```

> Both cacheability tests lock the tracking-name contract and re-run green throughout. On the unrelated-edit test the `Result` step **does** have an output even before Task 5 emits source — a `MatchGenerationResult` whose `Source` is `null` — and that equatable value is reported **`Cached`** on an unrelated edit (it is *not* an empty set; the earlier note's "empty set" reasoning was wrong). Once Task 5 emits non-null source the same step stays `Cached`. The cross-pattern test (`…AcrossPatterns…`) additionally proves that editing one pattern leaves the other's per-pattern Transform/Result outputs cached — Matching ships multiple patterns per class far more naturally than Templating, so this is the realistic regression to guard.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Synto.Test --filter "FullyQualifiedName~MatchDiagnosticsTests.TargetNotPartial_ReportsSY1001"`
Expected: FAIL — `MatchFactorySourceGenerator` / `MatchTrackingNames` do not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

`src/Synto.SourceGenerator/Matching/MatchTrackingNames.cs`:

```csharp
namespace Synto.Matching;

/// <summary>Tracking names for the matching pipeline's stages (test-observability hooks, not a consumer contract).</summary>
internal static class MatchTrackingNames
{
    public const string Transform = "Synto.Matching.Transform";
    public const string Result = "Synto.Matching.Result";
}
```

`src/Synto.SourceGenerator/Matching/MatchGenerationResult.cs`:

```csharp
namespace Synto.Matching;

/// <summary>
/// The value-equatable output of processing a single <c>[Match]</c> pattern: generated text + diagnostic
/// data only (never SemanticModel/symbols/syntax), so the incremental pipeline caches and never roots the
/// compilation. Mirrors <c>TemplateGenerationResult</c>.
/// </summary>
internal record struct MatchGenerationResult(string? FileName, string? Source, EquatableArray<DiagnosticInfo> Diagnostics);
```

`src/Synto.SourceGenerator/Matching/MatchInfo.cs`:

```csharp
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto.Matching;

/// <summary>
/// Transform-local model for one <c>[Match]</c> pattern. Holds the SemanticModel and symbols, so it MUST stay
/// inside the FAWMN transform and never flow through the pipeline (only <see cref="MatchGenerationResult"/>
/// does). Mirrors <c>TemplateInfo</c>.
/// </summary>
internal sealed class MatchInfo
{
    private MatchInfo(
        SemanticModel semanticModel, AttributeSyntax attributeSyntax, INamedTypeSymbol target,
        IMethodSymbol patternSymbol, SyntaxNode patternSyntax, string patternName, MatchOption option)
    {
        SemanticModel = semanticModel;
        AttributeSyntax = attributeSyntax;
        Target = target;
        PatternSymbol = patternSymbol;
        PatternSyntax = patternSyntax;
        PatternName = patternName;
        Option = option;
    }

    public SemanticModel SemanticModel { get; }
    public AttributeSyntax AttributeSyntax { get; }
    public INamedTypeSymbol Target { get; }
    public IMethodSymbol PatternSymbol { get; }
    public SyntaxNode PatternSyntax { get; }
    public string PatternName { get; }
    public MatchOption Option { get; }

    public static MatchInfo? Create(GeneratorAttributeSyntaxContext ctx)
    {
        var attr = ctx.Attributes.FirstOrDefault();
        if (attr is null)
            return null;

        // Generic attribute discovery (verified on the Roslyn 5.0 floor): the matcher target is the
        // attribute's single type argument, read straight off the AttributeClass.
        if (attr.AttributeClass is not { TypeArguments.Length: 1 } attrClass)
            return null;
        if (attrClass.TypeArguments[0] is not INamedTypeSymbol target)
            return null;

        if (attr.ApplicationSyntaxReference?.GetSyntax() is not AttributeSyntax attrSyntax)
            return null;

        if (ctx.TargetSymbol is not IMethodSymbol patternSymbol)
            return null;

        var option = MatchOption.None;
        if (attr.ConstructorArguments.Length == 1 && attr.ConstructorArguments[0].Value is int raw)
            option = (MatchOption)raw;

        return new MatchInfo(ctx.SemanticModel, attrSyntax, target, patternSymbol, ctx.TargetNode, patternSymbol.Name, option);
    }
}
```

`src/Synto.SourceGenerator/Matching/MatchFactorySourceGenerator.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;          // SyntaxKind + SyntaxTokenList.Any(SyntaxKind)
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Synto.Matching;

/// <summary>
/// Compiles each <c>[Match&lt;TMatcher&gt;]</c> pattern method into a bespoke straight-line Roslyn matcher
/// emitted into <c>partial class TMatcher</c>. Sibling of <c>TemplateFactorySourceGenerator</c>; all semantic
/// work happens inside the transform so the pipeline value is an equatable <see cref="MatchGenerationResult"/>.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class MatchFactorySourceGenerator : IIncrementalGenerator
{
    // Generic attribute: metadata name carries the arity suffix.
    private const string MatchAttributeMetadataName = "Synto.Matching.MatchAttribute`1";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var results = context.SyntaxProvider.ForAttributeWithMetadataName(
                MatchAttributeMetadataName,
                static (node, _) => true,
                static (ctx, _) => GenerateMatcher(ctx))
            .WithTrackingName(MatchTrackingNames.Transform)
            .Where(static result => result is not null)
            .WithTrackingName(MatchTrackingNames.Result);

        context.RegisterSourceOutput(results, static (spc, result) => Emit(spc, result!.Value));
    }

    private static MatchGenerationResult? GenerateMatcher(GeneratorAttributeSyntaxContext ctx)
    {
        var info = MatchInfo.Create(ctx);
        if (info is null)
            return null;

        var assemblyName = ctx.SemanticModel.Compilation.AssemblyName;

        var diagnostics = new List<DiagnosticInfo>();
        string? fileName = null;
        string? source = null;

        try
        {
            if (ValidateTarget(diagnostics, assemblyName, info)
                && MatchEmitter.Emit(diagnostics, info) is { } generated)
            {
                fileName = generated.FileName;
                source = generated.Source;
            }
        }
#pragma warning disable CA1031 // intentional: convert ANY generation exception into a diagnostic
        catch (Exception ex)
#pragma warning restore CA1031
        {
            diagnostics.Add(Diagnostics.InternalError(ex));
        }

        return new MatchGenerationResult(fileName, source, new EquatableArray<DiagnosticInfo>(diagnostics.ToImmutableArray()));
    }

    // Mirrors Templating's four-arm ValidateTemplate EXACTLY (NotDeclaredInSource -> NotClass -> NotPartial ->
    // AncestorNotPartial), reusing the SAME descriptors via the new LocationInfo overloads. Each reachable
    // misuse maps to its precise SY1xxx; nothing falls through to a malformed `partial class` that cascades
    // into CS0260/CS0101 (correctness.md's bar). Splitting "in source" from "is a class" is load-bearing: a
    // struct/interface IS in source, so it must be SY1002, never SY1003.
    private static bool ValidateTarget(List<DiagnosticInfo> diagnostics, string? assemblyName, MatchInfo info)
    {
        var location = LocationInfo.CreateFrom(info.AttributeSyntax.GetLocation());
        var fullName = info.Target.ToDisplayString();

        // Arm 1 — must be declared in source (not a metadata/referenced-assembly type).
        if (info.Target.DeclaringSyntaxReferences.FirstOrDefault() is not { } syntaxRef)
        {
            diagnostics.Add(Diagnostics.TargetNotDeclaredInSource(location, fullName, assemblyName));
            return false;
        }

        // Arm 2 — must be a (non-record) class. A record CLASS parses as RecordDeclarationSyntax, not
        // ClassDeclarationSyntax; Templating accepts only ClassDeclarationSyntax, so mirror that contract
        // exactly (a `record class` target is SY1002, same as a struct/interface) — do not widen it here.
        if (syntaxRef.GetSyntax() is not ClassDeclarationSyntax classSyntax)
        {
            diagnostics.Add(Diagnostics.TargetNotClass(location, fullName));
            return false;
        }

        // Arm 3 — the target class itself must be partial.
        if (!classSyntax.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            diagnostics.Add(Diagnostics.TargetNotPartial(location, fullName));
            return false;
        }

        // Arm 4 — every ancestor class must be partial, else the emitted `partial class Outer { … }` collides
        // with the user's non-partial outer (CS0260/CS0101). Report each non-partial ancestor; fail if any.
        bool ancestryPartial = true;
        for (var parent = classSyntax.Parent; parent is ClassDeclarationSyntax parentClass; parent = parentClass.Parent)
        {
            if (!parentClass.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                diagnostics.Add(Diagnostics.TargetAncestorNotPartial(location, fullName, parentClass.Identifier.Text));
                ancestryPartial = false;
            }
        }

        return ancestryPartial;
    }

    private static void Emit(SourceProductionContext context, MatchGenerationResult result)
    {
        foreach (var diagnostic in result.Diagnostics)
            context.ReportDiagnostic(diagnostic.ToDiagnostic());

        if (result.FileName is not null && result.Source is not null)
            context.AddSource(result.FileName, SourceText.From(result.Source, Encoding.UTF8));
    }
}
```

Add the stub emitter so the scaffold compiles — `src/Synto.SourceGenerator/Matching/MatchEmitter.cs`:

```csharp
using System.Collections.Generic;

namespace Synto.Matching;

internal static partial class MatchEmitter
{
    /// <summary>
    /// Lowers a pattern into (fileName, source), or returns null after adding diagnostics. Implemented
    /// incrementally across the emitter tasks; the scaffold returns null (no emission yet).
    /// </summary>
    public static (string FileName, string Source)? Emit(List<DiagnosticInfo> diagnostics, MatchInfo info)
    {
        _ = diagnostics;
        _ = info;
        return null;
    }
}
```

Add to `src/Synto.SourceGenerator/Diagnostics.cs` **`LocationInfo`-based overloads of all four** target-validation factories beside the existing `TargetType`-based ones; they reuse the SAME private descriptors (`_targetNotDeclaredInSource` SY1003, `_targetNotClass` SY1002, `_targetNotPartial` SY1001, `_targetAncestorNotPartial` SY1004), decoupled from `TargetType`/`typeof`:

```csharp
    public static DiagnosticInfo TargetNotDeclaredInSource(LocationInfo? location, string fullName, string? projectName)
    {
        return new DiagnosticInfo(_targetNotDeclaredInSource,
            location,
            new EquatableArray<string>(ImmutableArray.Create(fullName, projectName ?? "<unknown>")));
    }

    public static DiagnosticInfo TargetNotClass(LocationInfo? location, string fullName)
    {
        return new DiagnosticInfo(_targetNotClass,
            location,
            new EquatableArray<string>(ImmutableArray.Create(fullName)));
    }

    public static DiagnosticInfo TargetNotPartial(LocationInfo? location, string fullName)
    {
        return new DiagnosticInfo(_targetNotPartial,
            location,
            new EquatableArray<string>(ImmutableArray.Create(fullName)));
    }

    public static DiagnosticInfo TargetAncestorNotPartial(LocationInfo? location, string fullName, string ancestorName)
    {
        return new DiagnosticInfo(_targetAncestorNotPartial,
            location,
            new EquatableArray<string>(ImmutableArray.Create(fullName, ancestorName)));
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/Synto.Test --filter "FullyQualifiedName~MatchDiagnosticsTests"`
Expected: PASS — `TargetNotPartial_ReportsSY1001`, `TargetNotClass_ReportsSY1002` (×3), `TargetAncestorNotPartial_ReportsSY1004`, `WellFormedMatch_ReadsTypeArg_AndEmitsMatcher`, `Generator_IsIncremental_OnUnrelatedEdit`, `Generator_IsIncremental_AcrossPatterns_OnEditingOne`.

- [ ] **Step 5: Commit** (local commit on `experimental/matching` — no push)

```bash
git add src/Synto.SourceGenerator/Matching/ src/Synto.SourceGenerator/Diagnostics.cs \
        test/Synto.Test/Match/MatchTestHarness.cs test/Synto.Test/Match/MatchDiagnosticsTests.cs
git commit -m "feat(matching): pipeline scaffold + four-arm matcher-target validation (SY1001-SY1004)"
```

---

### Task 5: Emitter core — expression-`Single`, literal (no captures) + the generic structural walk

**Files:**
- Create: `src/Synto.SourceGenerator/Matching/MatchMarkers.cs`
- Modify: `src/Synto.SourceGenerator/Matching/MatchEmitter.cs` (replace the stub `Emit`)
- Test: `test/Synto.Test/Match/MatchRoundTripTests.cs`, `test/Synto.Test/Match/MatchSnapshotTests.cs`

**Interfaces:**
- Produces:
  - `MatchMarkers` — resolves marker symbols and classifies pattern nodes. For this task: `static IReadOnlyList<IParameterSymbol> CaptureParameters(MatchInfo)` (params carrying `Capture`/`Capture<T>`); `bool IsExpressionBodied(MatchInfo, out ExpressionSyntax expr)`.
  - `MatchEmitter.Emit` — for `MatchOption.Single` + expression-bodied pattern with zero captures, emits the record + matcher via the generic walk.
  - `MatchEmitter.EmitNodeMatch(List<StatementSyntax> body, string accessor, SyntaxNode pattern, MatchContext ctx)` — the reusable generic structural walk (extended in later tasks for holes).

- [ ] **Step 1: Write the failing round-trip test**

`test/Synto.Test/Match/MatchRoundTripTests.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Synto.Matching;

#pragma warning disable CS8321 // local pattern functions are consumed by the generator, not called here
#pragma warning disable CS0162 // a trailing Block.End() anchor sits after a phantom `return` -> unreachable (Task 14)

namespace Synto.Test.Match;

/// <summary>
/// In-process behavioral round-trips: the Matching generator runs as an analyzer on Synto.Test, so the
/// [Match<M>] local functions below produce real M.* matchers compiled into this assembly. Mirrors
/// RoundTripTests for Templating.
/// </summary>
public partial class MatchRoundTripTests
{
    private static partial class M { }

    [Fact]
    public void LiteralOne_MatchesOne_RejectsTwo()
    {
        // Pattern authored as a per-[Fact] LOCAL FUNCTION (Templating's RoundTripTests isolation): the
        // generator discovers [Match<M>] on the local function and emits M.LiteralOne; one non-compiling
        // pattern fails only its own [Fact], not the whole partial-class test assembly. (M is the shared
        // private static partial class above; only the pattern moves into the [Fact].)
        [Match<M>(MatchOption.Single)]
        static object LiteralOne() => 1;

        Assert.NotNull(M.LiteralOne(SyntaxFactory.ParseExpression("1")));
        Assert.Null(M.LiteralOne(SyntaxFactory.ParseExpression("2")));
        Assert.Null(M.LiteralOne(SyntaxFactory.ParseExpression("1 + 1")));
    }
}
```

> **Round-trip authoring convention (applies to every later round-trip Step).** For brevity the subsequent tasks show each pattern as a class-scope `private static` method; **author each as a local function inside its own `[Fact]`** as shown here (the `[Match<M>]`, `[Capture]`, quantifier forms are identical — only the placement moves). This is Templating's isolation model and keeps one bad fixture from gating the suite. The shared `private static partial class M { }` stays at class scope.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Synto.Test --filter "FullyQualifiedName~MatchRoundTripTests.LiteralOne_MatchesOne_RejectsTwo"`
Expected: FAIL — `M.LiteralOne` does not exist (no emission yet; compile error).

- [ ] **Step 3: Write minimal implementation**

`src/Synto.SourceGenerator/Matching/MatchMarkers.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto.Matching;

/// <summary>
/// Resolves the Synto.Matching marker symbols from the compilation and classifies pattern nodes as holes by
/// BINDING (spec §3.1) — never by type/shape — so literal matched code can never be mistaken for a hole.
/// </summary>
internal sealed class MatchMarkers
{
    private MatchMarkers(
        IReadOnlyList<IParameterSymbol> captures,
        IReadOnlyDictionary<IParameterSymbol, ITypeSymbol?> narrows,
        INamedTypeSymbol? exprMarker, INamedTypeSymbol? statementMarker, INamedTypeSymbol? blockMarker)
    {
        Captures = captures;
        Narrows = narrows;
        ExprMarker = exprMarker;
        StatementMarker = statementMarker;
        BlockMarker = blockMarker;
    }

    /// <summary>The pattern method's <c>[Capture]</c>/<c>[Capture&lt;T&gt;]</c> parameters, in signature order.</summary>
    public IReadOnlyList<IParameterSymbol> Captures { get; }

    /// <summary>For a <c>[Capture&lt;TNode&gt;]</c> param, the narrow type; null for plain <c>[Capture]</c>.</summary>
    public IReadOnlyDictionary<IParameterSymbol, ITypeSymbol?> Narrows { get; }

    /// <summary>Resolved <c>Synto.Matching.Expr</c> wildcard holder (used by <c>IsExpressionWildcard</c>, Task 8).</summary>
    public INamedTypeSymbol? ExprMarker { get; }

    /// <summary>Resolved <c>Synto.Matching.Statement</c> wildcard holder (used by <c>TryGetStatementHole</c>, Task 11).</summary>
    public INamedTypeSymbol? StatementMarker { get; }

    /// <summary>Resolved <c>Synto.Matching.Block</c> anchor holder (used by <c>TryGetAnchor</c>, Task 14).</summary>
    public INamedTypeSymbol? BlockMarker { get; }

    public static MatchMarkers Create(MatchInfo info)
    {
        var compilation = info.SemanticModel.Compilation;

        // Resolve every marker ONCE by metadata name and compare by symbol identity (SymbolEqualityComparer),
        // exactly as SyntaxParameterFinder.cs:79 / InlinedParameterFinder do — never ToDisplayString(). The
        // generator references Synto.Core, so typeof(global::Synto.Matching.*) binds (like Templating's
        // typeof(InlineAttribute)); GetTypeByMetadataName turns that into the compilation's symbol.
        var captureAttr = compilation.GetTypeByMetadataName(typeof(global::Synto.Matching.CaptureAttribute).FullName!);
        var captureAttrUnbound = compilation
            .GetTypeByMetadataName(typeof(global::Synto.Matching.CaptureAttribute<>).FullName!)   // metadata name "Synto.Matching.CaptureAttribute`1"
            ?.ConstructUnboundGenericType();
        var exprMarker = compilation.GetTypeByMetadataName(typeof(global::Synto.Matching.Expr).FullName!);
        var statementMarker = compilation.GetTypeByMetadataName(typeof(global::Synto.Matching.Statement).FullName!);
        var blockMarker = compilation.GetTypeByMetadataName(typeof(global::Synto.Matching.Block).FullName!);

        var captures = new List<IParameterSymbol>();
        var narrows = new Dictionary<IParameterSymbol, ITypeSymbol?>(SymbolEqualityComparer.Default);

        foreach (var p in info.PatternSymbol.Parameters)
        {
            foreach (var a in p.GetAttributes())
            {
                var ac = a.AttributeClass;
                if (ac is null) continue;

                if (SymbolEqualityComparer.Default.Equals(ac, captureAttr))
                {
                    captures.Add(p);
                    narrows[p] = null;
                }
                else if (ac.IsGenericType && captureAttrUnbound is not null
                         && SymbolEqualityComparer.Default.Equals(ac.ConstructUnboundGenericType(), captureAttrUnbound))
                {
                    captures.Add(p);
                    narrows[p] = ac.TypeArguments[0];
                }
            }
        }

        return new MatchMarkers(captures, narrows, exprMarker, statementMarker, blockMarker);
    }

    /// <summary>Does <paramref name="id"/> bind to one of the capture parameters? If so, return it.</summary>
    public bool TryGetCapture(SemanticModel model, IdentifierNameSyntax id, out IParameterSymbol capture)
    {
        var symbol = model.GetSymbolInfo(id).Symbol;
        if (symbol is IParameterSymbol p && Captures.Any(c => SymbolEqualityComparer.Default.Equals(c, p)))
        {
            capture = p;
            return true;
        }

        capture = null!;
        return false;
    }
}
```

> `MatchMarkers.Create` is cheap (a handful of `GetTypeByMetadataName` lookups + the parameter scan) and is built once per pattern in `MatchEmitter.Emit`. Where later tasks need it during a node walk (e.g. `TryFindDeferredForeach`, Task 15) they reuse `ctx.Markers` rather than rebuilding it per node.

Replace the body of `src/Synto.SourceGenerator/Matching/MatchEmitter.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.Formatting;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto.Matching;

internal static partial class MatchEmitter
{
    private static readonly SymbolDisplayFormat TargetNameFormat = new(
        SymbolDisplayGlobalNamespaceStyle.Omitted,
        SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        SymbolDisplayGenericsOptions.IncludeTypeParameters);

    /// <summary>A captured member to surface on the result record.</summary>
    private sealed class Capture
    {
        public string LocalName = "";   // e.g. cap_a
        public string MemberName = "";  // e.g. A
        public string MemberType = "";  // e.g. ExpressionSyntax
        public int Ordinal;             // the [Capture] parameter's signature position (record member order)
    }

    /// <summary>
    /// Mutable per-pattern emit state threaded through the walk. **Introduced here in its FINAL shape and
    /// frozen** (see the "Emitter-core design" design note) — Tasks 11–17 fill in *behavior* using
    /// <c>Diagnostics</c>/<c>Aborted</c>, they do NOT add fields or re-plumb the contract. `Diagnostics`/
    /// `Aborted` are unused until the run-alignment tasks (SY1202/SY1204) but exist now so the shape never
    /// churns across the shared `EmitAnchoredRun` refactor.
    /// </summary>
    private sealed class MatchContext
    {
        public required MatchInfo Info;
        public required MatchMarkers Markers;
        public readonly List<Capture> Captures = new();
        public readonly HashSet<string> BoundCaptureLocals = new();
        public readonly List<DiagnosticInfo> Diagnostics = new();  // emitter-raised findings (SY1202/SY1204, Tasks 16/17)
        public bool Aborted;                                       // a branch set this + returned -> Emit emits diagnostics-only
        private int _tmp;
        public string NextTmp() => "n" + _tmp++;
    }

    public static (string FileName, string Source)? Emit(List<DiagnosticInfo> diagnostics, MatchInfo info)
    {
        var markers = MatchMarkers.Create(info);
        var ctx = new MatchContext { Info = info, Markers = markers };

        var body = new List<StatementSyntax>();

        switch (info.Option)
        {
            case MatchOption.Single when TryGetExpressionBody(info, out var expr):
                EmitNodeMatch(body, "node", expr, ctx);
                break;
            default:
                // Other options arrive in later tasks; for now nothing to emit.
                return null;
        }

        return Compose(info, ctx, body);
    }

    /// <summary>Expression-bodied pattern? (the arrow body IS the matched expression — expression-Single, §4).</summary>
    private static bool TryGetExpressionBody(MatchInfo info, out ExpressionSyntax expr)
    {
        switch (info.PatternSyntax)
        {
            case MethodDeclarationSyntax { ExpressionBody.Expression: { } e }:
                expr = e;
                return true;
            case LocalFunctionStatementSyntax { ExpressionBody.Expression: { } e }:
                expr = e;
                return true;
            default:
                expr = null!;
                return false;
        }
    }

    /// <summary>
    /// The generic structural walk: emit guards asserting the runtime <paramref name="accessor"/> (a
    /// <c>SyntaxNode?</c>-typed expression) structurally equals <paramref name="pattern"/>, capturing at holes.
    /// Extended in later tasks for capture/wildcard/anchor/quantifier holes.
    /// </summary>
    private static void EmitNodeMatch(List<StatementSyntax> body, string accessor, SyntaxNode pattern, MatchContext ctx)
    {
        // Literal node: assert BOTH the .NET type (binds {tmp} for child navigation) AND the RawKind, so a
        // kind that shares a .NET type but differs in kind is not over-accepted — InitializerExpressionSyntax
        // (Array vs Collection), BinaryExpressionSyntax (Add vs Subtract vs ...), etc. (see the structural-walk
        // design note: "assert RawKind, not just the .NET type"). `pattern.Kind()` is the compile-time kind;
        // `{tmp}.IsKind(SyntaxKind.X)` is the runtime guard (CSharpExtensions.IsKind, present on the 5.0 floor).
        var typeName = pattern.GetType().Name; // e.g. "BinaryExpressionSyntax" (all *Syntax types are imported)
        var tmp = ctx.NextTmp();
        body.Add(ParseStatement($"if ({accessor} is not {typeName} {tmp} || !{tmp}.IsKind(SyntaxKind.{pattern.Kind()})) return null;"));

        var children = pattern.ChildNodesAndTokens();
        var listVar = tmp + "c";
        body.Add(ParseStatement($"var {listVar} = {tmp}.ChildNodesAndTokens();"));
        body.Add(ParseStatement($"if ({listVar}.Count != {children.Count}) return null;"));

        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i];
            if (child.IsToken)
            {
                var token = child.AsToken();
                var kind = token.Kind().ToString();
                var literal = SymbolDisplay.FormatLiteral(token.Text, quote: true);
                body.Add(ParseStatement(
                    $"if (!{listVar}[{i}].IsKind(SyntaxKind.{kind}) || {listVar}[{i}].AsToken().Text != {literal}) return null;"));
            }
            else
            {
                EmitNodeMatch(body, $"{listVar}[{i}].AsNode()", child.AsNode()!, ctx);
            }
        }
    }

    /// <summary>Wrap the matcher body + result record into the final compilation unit and format it.</summary>
    private static (string FileName, string Source) Compose(MatchInfo info, MatchContext ctx, List<StatementSyntax> body)
    {
        var matchTypeName = info.PatternName + "Match";

        // return new {Pattern}Match(cap_p1, cap_p2, ...);  (captures already in signature order)
        var args = string.Join(", ", ctx.Captures.Select(c => c.LocalName));
        body.Add(ParseStatement($"return new {matchTypeName}({args});"));

        // public static {Pattern}Match? {Pattern}(SyntaxNode node) { <body> }
        var matcher = (MethodDeclarationSyntax)ParseMemberDeclaration(
            $"public static {matchTypeName}? {info.PatternName}(SyntaxNode node) {{ }}")!;
        matcher = matcher.WithBody(Block(body));

        MemberDeclarationSyntax targetSyntax = ClassDeclaration(info.Target.Name)
            .WithModifiers(((ClassDeclarationSyntax)info.Target.DeclaringSyntaxReferences[0].GetSyntax()).Modifiers)
            .AddMembers(matcher)
            .WithAncestryFrom(info.Target);

        // record positional parameters in signature order
        var recordParams = string.Join(", ", ctx.Captures.Select(c => $"{c.MemberType} {c.MemberName}"));
        var record = (RecordDeclarationSyntax)ParseMemberDeclaration(
            $"public sealed record {matchTypeName}({recordParams});")!;

        // Place the record beside the class in the same namespace (mirrors the helper-injection pattern in
        // TemplateFactorySourceGenerator); for the global namespace it joins the compilation unit.
        var compilationUnit = CompilationUnit();
        targetSyntax = targetSyntax switch
        {
            FileScopedNamespaceDeclarationSyntax fileNs => fileNs.WithMembers(fileNs.Members.Insert(0, record)),
            NamespaceDeclarationSyntax ns => ns.WithMembers(ns.Members.Insert(0, record)),
            _ => targetSyntax,
        };
        compilationUnit = compilationUnit.AddMembers(targetSyntax);
        if (targetSyntax is not (FileScopedNamespaceDeclarationSyntax or NamespaceDeclarationSyntax))
            compilationUnit = compilationUnit.AddMembers(record);

        compilationUnit = compilationUnit
            .AddUsings(
                UsingDirective(ParseName("Microsoft.CodeAnalysis")),
                UsingDirective(ParseName("Microsoft.CodeAnalysis.CSharp")),
                UsingDirective(ParseName("Microsoft.CodeAnalysis.CSharp.Syntax")))
            .WithLeadingTrivia(Trivia(NullableDirectiveTrivia(Token(SyntaxKind.EnableKeyword), isActive: true)));

        var sourceText = SyntaxFormatter.Format(compilationUnit.NormalizeWhitespace()).GetText(System.Text.Encoding.UTF8).ToString();

        var fileName = $"{info.Target.ToDisplayString(TargetNameFormat)}.{info.PatternName}.g.cs";
        return (fileName, sourceText);
    }
}
```

> `ParseStatement`/`ParseMemberDeclaration` keep the emitter compact and avoid hand-building factory soup; `SymbolDisplay.FormatLiteral` makes token-text comparisons escape-safe. The matcher uses `SyntaxKind.{kind}` and `.IsKind(...)`/`.AsToken()` from `Microsoft.CodeAnalysis.CSharp` (imported in the generated file). `LiteralOne` has no captures, so the record is `record LiteralOneMatch();` and the matcher returns `new LiteralOneMatch()`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/Synto.Test --filter "FullyQualifiedName~MatchRoundTripTests.LiteralOne_MatchesOne_RejectsTwo"`
Expected: PASS.
Run: `dotnet test test/Synto.Test --filter "FullyQualifiedName~MatchDiagnosticsTests.Generator_IsIncremental_OnUnrelatedEdit"`
Expected: PASS (the `Result` step now produces a cached output on the unrelated edit).

- [ ] **Step 5: Add + accept the first output snapshot, then commit**

`test/Synto.Test/Match/MatchSnapshotTests.cs`:

```csharp
using Microsoft.CodeAnalysis;

namespace Synto.Test.Match;

public class MatchSnapshotTests
{
    private static Task VerifyMatcher(string source) =>
        Verify(MatchTestHarness.Run(source)).UseDirectory("snapshots");

    [Fact]
    public Task ExpressionSingle_Literal() => VerifyMatcher(
        """
        using Synto.Matching;
        partial class M { }
        static class Patterns {
            [Match<M>(MatchOption.Single)]
            static object LiteralOne() => 1;
        }
        """);
}
```

Run the snapshot test, review the `*.received.cs` (confirm: `#nullable enable`, the three Roslyn usings, `public sealed record LiteralOneMatch();`, and `partial class M { public static LiteralOneMatch? LiteralOne(SyntaxNode node) { ... return new LiteralOneMatch(); } }` with structural guards for a numeric-literal `1`), then accept it.

```bash
git add src/Synto.SourceGenerator/Matching/MatchMarkers.cs src/Synto.SourceGenerator/Matching/MatchEmitter.cs \
        test/Synto.Test/Match/MatchRoundTripTests.cs test/Synto.Test/Match/MatchSnapshotTests.cs \
        test/Synto.Test/Match/snapshots/MatchSnapshotTests.ExpressionSingle_Literal#M.LiteralOne.g.verified.cs
git commit -m "feat(matching): generic structural walk + expression-Single literal matcher emission"
```

---

### Task 6: Emitter — expression captures (bare identifier → `ExpressionSyntax`)

**Files:**
- Modify: `src/Synto.SourceGenerator/Matching/MatchEmitter.cs` (add a hole check at the top of `EmitNodeMatch`)
- Test: `MatchRoundTripTests.cs`, `MatchSnapshotTests.cs`

**Interfaces:**
- Consumes: `MatchMarkers.TryGetCapture`. Produces: capture extraction for plain `[Capture]` params (member type `ExpressionSyntax`).

- [ ] **Step 1: Write the failing round-trip test** — add to `MatchRoundTripTests`:

```csharp
    [Match<M>(MatchOption.Single)]
    private static object Sum([Capture] int a, [Capture] int b) => a + b;

    [Fact]
    public void Sum_CapturesBothOperands()
    {
        var node = SyntaxFactory.ParseExpression("foo() + 42");
        var m = M.Sum(node);
        Assert.NotNull(m);
        Assert.Equal("foo()", m!.A.ToString());
        Assert.Equal("42", m.B.ToString());

        Assert.Null(M.Sum(SyntaxFactory.ParseExpression("foo() - 42"))); // wrong operator
        Assert.Null(M.Sum(SyntaxFactory.ParseExpression("foo()")));      // not a binary expr
    }

    // The [Capture<TNode>] NARROWING behavior is added together with EmitCapture in THIS task (EmitCapture
    // reads ctx.Markers.Narrows), so its behavioral lock lives here as a red->green step — not as a
    // green-on-arrival test in Task 7. Authored as a local function per the round-trip convention.
    [Fact]
    public void Narrowed_OnlyMatchesInvocation_AndTypesTheMember()
    {
        [Match<M>(MatchOption.Single)]
        static object Narrowed([Capture<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>] object call) => call;

        var m = M.Narrowed(SyntaxFactory.ParseExpression("foo(1)"));
        Assert.NotNull(m);
        Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax typed = m!.Call; // must compile AS the narrow type
        Assert.Equal("foo(1)", typed.ToString());

        Assert.Null(M.Narrowed(SyntaxFactory.ParseExpression("1 + 1"))); // not an invocation
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/Synto.Test --filter "FullyQualifiedName~MatchRoundTripTests.Sum_CapturesBothOperands|FullyQualifiedName~MatchRoundTripTests.Narrowed_OnlyMatchesInvocation_AndTypesTheMember"`
Expected: FAIL — `M.Sum`/`M.Narrowed` exist but do not capture/narrow (compile error on `m.A` / `m.Call` typed as `ExpressionSyntax`, or the walk treats `a`/`b` as literal identifiers and over-constrains). The narrowing test fails red until `EmitCapture` reads `ctx.Markers.Narrows` in Step 3.

- [ ] **Step 3: Write minimal implementation** — at the very top of `EmitNodeMatch`, before the literal-node handling, add the capture-hole check:

```csharp
        // Expression capture hole: a bare identifier binding to a [Capture] parameter (spec §3.1).
        if (pattern is IdentifierNameSyntax id
            && ctx.Markers.TryGetCapture(ctx.Info.SemanticModel, id, out var captureParam))
        {
            EmitCapture(body, accessor, captureParam, ctx);
            return;
        }
```

Add the `EmitCapture` helper to `MatchEmitter`:

```csharp
    private static void EmitCapture(List<StatementSyntax> body, string accessor, IParameterSymbol param, MatchContext ctx)
    {
        var local = "cap_" + param.Name;
        var member = char.ToUpperInvariant(param.Name[0]) + param.Name.Substring(1);

        // [Capture<TNode>] narrows the kind + member type; plain [Capture] captures any ExpressionSyntax.
        var narrow = ctx.Markers.Narrows.TryGetValue(param, out var t) ? t : null;
        var memberType = narrow is null ? "ExpressionSyntax" : narrow.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        if (ctx.BoundCaptureLocals.Contains(local))
        {
            // Non-linear reuse — handled in a later task. For now (single placement) this branch is unused.
            body.Add(ParseStatement($"if (!{accessor}.IsEquivalentTo({local})) return null;"));
            return;
        }

        body.Add(ParseStatement($"if ({accessor} is not {memberType} {local}) return null;"));
        ctx.BoundCaptureLocals.Add(local);
        ctx.Captures.Add(new Capture { LocalName = local, MemberName = member, MemberType = memberType, Ordinal = param.Ordinal });
    }
```

> The captures are added in *walk* order. To freeze the record member order to *signature* order (spec shows `GuardedMatch(Cond, Guard, Rest)`), each `Capture` carries the parameter's `Ordinal`, so `Compose` sorts by it in **O(n log n)** — no per-key `ToList()`/`FindIndex` rescan of `ctx.Markers.Captures` (which would be O(n²)). Update `Compose`'s capture enumerations to use the signature-ordered list:

```csharp
        // at the top of Compose, replace direct ctx.Captures use with:
        var orderedCaptures = ctx.Captures.OrderBy(c => c.Ordinal).ToList();
```

and use `orderedCaptures` for both the `return new ...(args)` and the record-parameter list. (Task 10 hoists this into the shared `OrderCaptures(MatchContext)` — `static List<Capture> OrderCaptures(MatchContext ctx) => ctx.Captures.OrderBy(c => c.Ordinal).ToList();` — used by every emit branch.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/Synto.Test --filter "FullyQualifiedName~MatchRoundTripTests.Sum_CapturesBothOperands|FullyQualifiedName~MatchRoundTripTests.Narrowed_OnlyMatchesInvocation_AndTypesTheMember"`
Expected: PASS (capture + narrowing).

- [ ] **Step 5: Add + accept snapshot, then commit** — add to `MatchSnapshotTests`:

```csharp
    [Fact]
    public Task ExpressionSingle_Captures() => VerifyMatcher(
        """
        using Synto.Matching;
        partial class M { }
        static class Patterns {
            [Match<M>(MatchOption.Single)]
            static object Sum([Capture] int a, [Capture] int b) => a + b;
        }
        """);
```

Review/accept the snapshot (record `SumMatch(ExpressionSyntax A, ExpressionSyntax B)`; the `+` operator token compared by kind+text; `a`/`b` positions captured via `is not ExpressionSyntax cap_a`).

```bash
git add src/Synto.SourceGenerator/Matching/MatchEmitter.cs test/Synto.Test/Match/MatchRoundTripTests.cs \
        test/Synto.Test/Match/MatchSnapshotTests.cs \
        test/Synto.Test/Match/snapshots/MatchSnapshotTests.ExpressionSingle_Captures#M.Sum.g.verified.cs
git commit -m "feat(matching): expression captures (bare identifier -> ExpressionSyntax)"
```

---

### Task 7: Emitter — `[Capture<TNode>]` narrowing — **snapshot lock**

> **Scope (round-1 testability medium).** The narrowing *behavior* is round-trip-covered as a **red→green** step in **Task 6** (`Narrowed_OnlyMatchesInvocation_AndTypesTheMember`, green only once `EmitCapture` reads `ctx.Markers.Narrows`), and `MatchMarkers.Create` already resolves the `` CaptureAttribute`1 `` symbol by identity (Task 5). So this task adds **no behavioral test and no emitter code** — it is a deliberate **snapshot lock** of the narrowed *output shape* (the emitted member type and the narrowed `is`-guard), nothing more.

**Files:**
- (No emitter change.) `MatchSnapshotTests.cs` only.
- Test: `MatchSnapshotTests.cs`

- [ ] **Step 1: Add the narrowed-output snapshot** — add to `MatchSnapshotTests`:

```csharp
    [Fact]
    public Task ExpressionSingle_Narrowed() => VerifyMatcher(
        """
        using Microsoft.CodeAnalysis.CSharp.Syntax;
        using Synto.Matching;
        partial class M { }
        static class Patterns {
            [Match<M>(MatchOption.Single)]
            static object Narrowed([Capture<InvocationExpressionSyntax>] object call) => call;
        }
        """);
```

- [ ] **Step 2: Run, review, accept the golden** — run the snapshot test; review the `*.received.cs` and confirm the captured member is the **fully-qualified narrow** `global::Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax` and the guard is `if (node is not global::…InvocationExpressionSyntax cap_call) return null;`; then accept it. (Red first — new received file — then green on accept.)

- [ ] **Step 3: Commit** (local commit on `experimental/matching` — no push)

```bash
git add test/Synto.Test/Match/MatchSnapshotTests.cs \
        test/Synto.Test/Match/snapshots/MatchSnapshotTests.ExpressionSingle_Narrowed#M.Narrowed.g.verified.cs
git commit -m "test(matching): snapshot-lock [Capture<TNode>] narrowed member type"
```

---

### Task 8: Emitter — expression wildcard `Expr.Any<T>()`

**Files:**
- Modify: `src/Synto.SourceGenerator/Matching/MatchMarkers.cs` (add wildcard detection)
- Modify: `src/Synto.SourceGenerator/Matching/MatchEmitter.cs` (handle the wildcard hole)
- Test: `MatchRoundTripTests.cs`, `MatchSnapshotTests.cs`

**Interfaces:**
- Produces: `MatchMarkers.IsExpressionWildcard(SemanticModel, InvocationExpressionSyntax)` (binds to `Synto.Matching.Expr.Any<T>()`).

- [ ] **Step 1: Write the failing round-trip test** — add to `MatchRoundTripTests`:

```csharp
    [Match<M>(MatchOption.Single)]
    private static object EqualsAnything([Capture] object lhs) => lhs == Expr.Any<object>();

    [Fact]
    public void Wildcard_MatchesAnyRightOperand_WithoutCapturing()
    {
        var m = M.EqualsAnything(SyntaxFactory.ParseExpression("x == foo(1, 2)"));
        Assert.NotNull(m);
        Assert.Equal("x", m!.Lhs.ToString());

        Assert.Null(M.EqualsAnything(SyntaxFactory.ParseExpression("x + y"))); // not '=='
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Synto.Test --filter "FullyQualifiedName~MatchRoundTripTests.Wildcard_MatchesAnyRightOperand_WithoutCapturing"`
Expected: FAIL — the `Expr.Any<object>()` invocation is walked as a literal node and over-constrains the right operand (no match), or fails to compile/emit.

- [ ] **Step 3: Write minimal implementation**

Add to `MatchMarkers`:

```csharp
    /// <summary>Does <paramref name="inv"/> bind to <c>Synto.Matching.Expr.Any&lt;T&gt;()</c>?</summary>
    public bool IsExpressionWildcard(SemanticModel model, InvocationExpressionSyntax inv)
    {
        return model.GetSymbolInfo(inv).Symbol is IMethodSymbol m
            && m.Name == "Any"
            && SymbolEqualityComparer.Default.Equals(m.ContainingType, ExprMarker);   // resolved symbol, not ToDisplayString()
    }
```

At the top of `EmitNodeMatch` (after the capture-hole check), add:

```csharp
        // Expression wildcard hole: Expr.Any<T>() matches any expression, captures nothing (spec §3.5).
        if (pattern is InvocationExpressionSyntax invWild
            && ctx.Markers.IsExpressionWildcard(ctx.Info.SemanticModel, invWild))
        {
            body.Add(ParseStatement($"if ({accessor} is not ExpressionSyntax) return null;"));
            return;
        }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Synto.Test --filter "FullyQualifiedName~MatchRoundTripTests.Wildcard_MatchesAnyRightOperand_WithoutCapturing"`
Expected: PASS.

- [ ] **Step 5: Add + accept snapshot, then commit** — add `ExpressionSingle_Wildcard` to `MatchSnapshotTests`, accept (right operand asserted only `is ExpressionSyntax`, no capture).

```bash
git add src/Synto.SourceGenerator/Matching/MatchMarkers.cs src/Synto.SourceGenerator/Matching/MatchEmitter.cs \
        test/Synto.Test/Match/MatchRoundTripTests.cs test/Synto.Test/Match/MatchSnapshotTests.cs \
        test/Synto.Test/Match/snapshots/MatchSnapshotTests.ExpressionSingle_Wildcard#M.EqualsAnything.g.verified.cs
git commit -m "feat(matching): expression wildcard Expr.Any<T>()"
```

---

### Task 9: Emitter — non-linear equality (reused capture → `IsEquivalentTo`)

**Files:**
- Modify: `src/Synto.SourceGenerator/Matching/MatchEmitter.cs` (the reuse branch in `EmitCapture` is already present; this task proves + snapshots it and fixes the member-once rule)
- Test: `MatchRoundTripTests.cs`, `MatchSnapshotTests.cs`

**Interfaces:**
- Produces: a reused `[Capture]` adds **one** record member (first occurrence) and an `IsEquivalentTo` guard on each subsequent occurrence (spec §3.6).

- [ ] **Step 1: Write the failing round-trip test** — add to `MatchRoundTripTests`:

```csharp
    [Match<M>(MatchOption.None)]
    private static void SelfCompare([Capture] object x) { _ = x == x; }

    [Fact]
    public void SelfCompare_RequiresBothSidesSyntacticallyEqual()
    {
        // None matches a method/local-function declaration whose body is `_ = <e> == <e>;`
        var decl = SyntaxFactory.ParseMemberDeclaration("void F() { _ = a.b == a.b; }")!;
        var m = M.SelfCompare(decl);
        Assert.NotNull(m);
        Assert.Equal("a.b", m!.X.ToString());

        var mismatch = SyntaxFactory.ParseMemberDeclaration("void F() { _ = a.b == a.c; }")!;
        Assert.Null(M.SelfCompare(mismatch)); // two sides differ -> IsEquivalentTo fails
    }
```

> This test depends on `MatchOption.None` (Task 13). If implementing strictly in order, write the non-linear assertion against an **expression-Single** pattern first (e.g. `[Match<M>(MatchOption.Single)] static object SelfEq([Capture] object x) => x == x;`) and move the `None` flavor to Task 13. Use the Single form here to keep tasks independent:

```csharp
    [Match<M>(MatchOption.Single)]
    private static object SelfEq([Capture] object x) => x == x;

    [Fact]
    public void SelfEq_RequiresBothSidesSyntacticallyEqual()
    {
        Assert.NotNull(M.SelfEq(SyntaxFactory.ParseExpression("a.b == a.b")));
        Assert.Null(M.SelfEq(SyntaxFactory.ParseExpression("a.b == a.c")));
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Synto.Test --filter "FullyQualifiedName~MatchRoundTripTests.SelfEq_RequiresBothSidesSyntacticallyEqual"`
Expected: FAIL — the second `x` re-binds `cap_x` (duplicate local declaration) → generated code does not compile, or both sides are captured as two members.

- [ ] **Step 3: Write minimal implementation** — the reuse branch already emits `IsEquivalentTo`, but it must **not** add a second `Capture` member and the guard's left operand must navigate to a node. Confirm `EmitCapture`:
  - first occurrence: declares `cap_x`, adds the member, records the local in `BoundCaptureLocals`;
  - subsequent occurrence: emits `if ({accessor} is not {{ }} _re || !_re.IsEquivalentTo({local})) return null;` — i.e. require the candidate is non-null and equivalent. Replace the reuse branch with:

```csharp
        if (ctx.BoundCaptureLocals.Contains(local))
        {
            // Non-linear reuse: the second+ site must be syntactically equal to the first (one IsEquivalentTo).
            body.Add(ParseStatement($"if ({accessor} is not {{ }} _re_{local} || !_re_{local}.IsEquivalentTo({local})) return null;"));
            return;
        }
```

(No new member is added on reuse, so the record keeps a single `X` member.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Synto.Test --filter "FullyQualifiedName~MatchRoundTripTests.SelfEq_RequiresBothSidesSyntacticallyEqual"`
Expected: PASS.

- [ ] **Step 5: Add + accept snapshot, then commit** — add `ExpressionSingle_NonLinear` (`=> x == x`) to `MatchSnapshotTests`, accept (one `X` member; an `IsEquivalentTo(cap_x)` guard on the second operand).

```bash
git add src/Synto.SourceGenerator/Matching/MatchEmitter.cs test/Synto.Test/Match/MatchRoundTripTests.cs \
        test/Synto.Test/Match/MatchSnapshotTests.cs \
        test/Synto.Test/Match/snapshots/MatchSnapshotTests.ExpressionSingle_NonLinear#M.SelfEq.g.verified.cs
git commit -m "feat(matching): non-linear equality via single IsEquivalentTo"
```

---

### Task 10: Emitter — statement-`Single` (block-rooted, single direct statement, leftmost)

**Files:**
- Modify: `src/Synto.SourceGenerator/Matching/MatchEmitter.cs` (statement-Single branch + a block scan)
- Test: `MatchRoundTripTests.cs`, `MatchSnapshotTests.cs`

**Interfaces:**
- Produces: for `MatchOption.Single` with a **block body containing exactly one statement** (no anchors, no quantifier), a matcher rooted on a candidate `BlockSyntax` that finds the **leftmost direct statement** matching the pattern statement (spec §4 statement-Single).

> Matcher shape: `if (node is not BlockSyntax b) return null; for (int i = 0; i < b.Statements.Count; i++) { if (TryAt(b.Statements[i])) return new ...; } return null;` — emitted as a local function `TryAt` wrapping the `EmitNodeMatch` guards, or inline with a labeled scan. Implementation below uses a per-offset local function returning the match (or null) and the outer loop commits to the leftmost.

- [ ] **Step 1: Write the failing round-trip test** — add to `MatchRoundTripTests`:

```csharp
    [Fact]
    public void StatementSingle_FindsLeftmostReturn_InABlock()
    {
        // object return type (per §3.9) — a `void` pattern with `return result;` is CS0127.
        [Match<M>(MatchOption.Single)]
        static object ReturnCapture([Capture] object result) { return result; }

        var block = (Microsoft.CodeAnalysis.CSharp.Syntax.BlockSyntax)
            SyntaxFactory.ParseStatement("{ Foo(); return x + 1; return y; }");
        var m = M.ReturnCapture(block);
        Assert.NotNull(m);
        Assert.Equal("x + 1", m!.Result.ToString()); // leftmost return

        var noReturn = (Microsoft.CodeAnalysis.CSharp.Syntax.BlockSyntax)
            SyntaxFactory.ParseStatement("{ Foo(); Bar(); }");
        Assert.Null(M.ReturnCapture(noReturn));
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Synto.Test --filter "FullyQualifiedName~MatchRoundTripTests.StatementSingle_FindsLeftmostReturn_InABlock"`
Expected: FAIL — a block-bodied `Single` currently hits the `default` arm of `Emit` and emits nothing → `M.ReturnCapture` does not exist.

- [ ] **Step 3: Write minimal implementation** — in `Emit`, add a statement-Single branch:

```csharp
            case MatchOption.Single when TryGetSingleStatementBody(info, out var stmt):
                EmitStatementSingle(body, stmt, ctx);
                break;
```

Add the helpers:

```csharp
    private static bool TryGetSingleStatementBody(MatchInfo info, out StatementSyntax stmt)
    {
        BlockSyntax? block = info.PatternSyntax switch
        {
            MethodDeclarationSyntax { Body: { } b } => b,
            LocalFunctionStatementSyntax { Body: { } b } => b,
            _ => null,
        };

        // Exactly one statement, no anchors/quantifiers (those are handled by Bare/anchor tasks).
        if (block is { Statements.Count: 1 })
        {
            stmt = block.Statements[0];
            return true;
        }

        stmt = null!;
        return false;
    }

    private static void EmitStatementSingle(List<StatementSyntax> body, StatementSyntax pattern, MatchContext ctx)
    {
        // Root on the candidate block; scan its DIRECT statements; commit to the leftmost that matches.
        body.Add(ParseStatement("if (node is not BlockSyntax _blk) return null;"));

        var matchTypeName = ctx.Info.PatternName + "Match";

        // Per-offset attempt as a local function so each scan position has its own capture scope.
        var attempt = new List<StatementSyntax>();
        EmitNodeMatch(attempt, "_blk.Statements[_i]", pattern, ctx);
        var orderedArgs = string.Join(", ", OrderCaptures(ctx).Select(c => c.LocalName));
        attempt.Add(ParseStatement($"return new {matchTypeName}({orderedArgs});"));

        var local = LocalFunctionStatement(
                ParseTypeName($"{matchTypeName}?"),
                Identifier("_TryAt"))
            .WithParameterList(ParseParameterList("(int _i)"))
            .WithBody(Block(attempt));

        body.Add(local);
        body.Add(ParseStatement(
            "for (int _i = 0; _i < _blk.Statements.Count; _i++) { var _r = _TryAt(_i); if (_r is not null) return _r; }"));
        body.Add(ParseStatement("return null;"));
    }
```

Refactor the signature-order capture sort out of `Compose` into a shared `OrderCaptures(MatchContext)` so both `Compose` and `EmitStatementSingle` use it. Also: when `Emit` returns through the statement-Single branch, `Compose` must **not** append its own `return new ...;`/wrap a second body — guard `Compose` so it only adds the trailing `return new ...` for the expression-Single path. Simplest: have each branch produce the full body (including returns) and make `Compose` purely wrap class/record/usings. Move the expression-Single trailing return into the `MatchOption.Single when TryGetExpressionBody` arm:

```csharp
            case MatchOption.Single when TryGetExpressionBody(info, out var expr):
                EmitNodeMatch(body, "node", expr, ctx);
                body.Add(ParseStatement($"return new {info.PatternName}Match({string.Join(", ", OrderCaptures(ctx).Select(c => c.LocalName))});"));
                break;
```

and delete the trailing-return line from `Compose`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Synto.Test --filter "FullyQualifiedName~MatchRoundTripTests.StatementSingle_FindsLeftmostReturn_InABlock"`
Expected: PASS. Re-run the whole `MatchRoundTripTests` + `MatchSnapshotTests` to confirm the `Compose` refactor didn't regress expression-Single.

- [ ] **Step 5: Add + accept snapshot, then commit** — add `StatementSingle_Return` to `MatchSnapshotTests`, accept (block-root + leftmost scan + `_TryAt`).

```bash
git add src/Synto.SourceGenerator/Matching/MatchEmitter.cs test/Synto.Test/Match/MatchRoundTripTests.cs \
        test/Synto.Test/Match/MatchSnapshotTests.cs \
        test/Synto.Test/Match/snapshots/MatchSnapshotTests.StatementSingle_Return#M.ReturnCapture.g.verified.cs
git commit -m "feat(matching): statement-Single (block-rooted, leftmost direct statement)"
```

---

### Task 11: Emitter — `Bare` run with fixed-arity elements (literal statements, `One()`, `Exactly(n)`)

**Files:**
- Modify: `src/Synto.SourceGenerator/Matching/MatchMarkers.cs` (classify statement-hole quantifiers)
- Modify: `src/Synto.SourceGenerator/Matching/MatchEmitter.cs` (Bare run alignment, fixed-arity only)
- Test: `MatchRoundTripTests.cs`, `MatchSnapshotTests.cs`

**Interfaces:**
- Produces:
  - `MatchMarkers.TryGetStatementHole(SemanticModel, StatementSyntax, out StatementHole)` — classifies an `ExpressionStatementSyntax` as a statement hole: `{ Kind: Capture|Wildcard, Quantifier: One|Opt|Some|All|Exactly, Count?, CaptureParam? }`, else not a hole.
  - `MatchEmitter` Bare branch: a candidate `BlockSyntax`, the pattern run = ordered list of `RunElement` (literal statement | fixed-arity hole). v1 fixed-arity = `One()` (1) and `Exactly(n)` (n). Match the run **contained** in the block at the **leftmost** offset.

> v1 fixed-arity alignment: the run consumes a known number of statements `K = sum(element widths)`. Scan offsets `o = 0..Count-K`; at each offset, each element aligns to a fixed sub-slice. Commit to the leftmost offset where all elements match.

- [ ] **Step 1: Write the failing round-trip test** — add to `MatchRoundTripTests`:

```csharp
    [Match<M>(MatchOption.Bare)]
    private static void OneGuard([Capture] bool cond, [Capture] Stmt only)
    {
        if (cond)
            only.One();
    }

    [Fact]
    public void Bare_FixedArity_MatchesContainedIfWithOneStatement()
    {
        var block = (Microsoft.CodeAnalysis.CSharp.Syntax.BlockSyntax)
            SyntaxFactory.ParseStatement("{ Pre(); if (ready) Go(); Post(); }");
        var m = M.OneGuard(block);
        Assert.NotNull(m);
        Assert.Equal("ready", m!.Cond.ToString());
        Assert.Equal("Go();", m.Only.ToString());

        var noIf = (Microsoft.CodeAnalysis.CSharp.Syntax.BlockSyntax)
            SyntaxFactory.ParseStatement("{ Pre(); Post(); }");
        Assert.Null(M.OneGuard(noIf));
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Synto.Test --filter "FullyQualifiedName~MatchRoundTripTests.Bare_FixedArity_MatchesContainedIfWithOneStatement"`
Expected: FAIL — `MatchOption.Bare` hits the `default` arm; `M.OneGuard` not emitted.

- [ ] **Step 3: Write minimal implementation**

Add to `MatchMarkers` a statement-hole classifier:

```csharp
    public enum HoleKind { Capture, Wildcard }
    public enum Quant { One, Opt, Some, All, Exactly }

    public readonly struct StatementHole
    {
        public StatementHole(HoleKind kind, Quant quant, int count, IParameterSymbol? capture)
        { Kind = kind; Quantifier = quant; Count = count; Capture = capture; }
        public HoleKind Kind { get; }
        public Quant Quantifier { get; }
        public int Count { get; }              // Exactly(n) only
        public IParameterSymbol? Capture { get; }
        public bool IsVariableLength => Quantifier is Quant.Some or Quant.All or Quant.Opt;
    }

    public bool TryGetStatementHole(SemanticModel model, StatementSyntax stmt, out StatementHole hole)
    {
        hole = default;
        if (stmt is not ExpressionStatementSyntax { Expression: InvocationExpressionSyntax inv }) return false;
        if (inv.Expression is not MemberAccessExpressionSyntax ma) return false;
        if (model.GetSymbolInfo(inv).Symbol is not IMethodSymbol method) return false;

        var verb = method.Name switch
        {
            "One" => Quant.One, "Opt" => Quant.Opt, "Some" => Quant.Some,
            "All" => Quant.All, "Exactly" => Quant.Exactly, _ => (Quant?)null,
        };
        if (verb is null) return false;

        int count = verb == Quant.Exactly && inv.ArgumentList.Arguments.Count == 1
            && inv.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax lit
            && int.TryParse(lit.Token.ValueText, out var n) ? n : (verb == Quant.One ? 1 : 0);

        var receiver = model.GetSymbolInfo(ma.Expression).Symbol;
        if (receiver is IParameterSymbol p && Captures.Any(c => SymbolEqualityComparer.Default.Equals(c, p)))
        {
            hole = new StatementHole(HoleKind.Capture, verb.Value, count, p);
            return true;
        }
        if (SymbolEqualityComparer.Default.Equals(method.ContainingType, StatementMarker))   // resolved symbol, not ToDisplayString()
        {
            hole = new StatementHole(HoleKind.Wildcard, verb.Value, count, null);
            return true;
        }
        return false;
    }
```

Add the Bare branch to `Emit`:

```csharp
            case MatchOption.Bare when TryGetBareRun(info, out var statements):
                EmitBareRun(body, statements, ctx);
                break;
```

Add the run model + fixed-arity alignment (variable-length added in Task 12; here reject/none):

```csharp
    private abstract class RunElement { }
    private sealed class LiteralElement : RunElement { public required StatementSyntax Statement; }
    private sealed class HoleElement : RunElement { public required MatchMarkers.StatementHole Hole; }

    private static bool TryGetBareRun(MatchInfo info, out IReadOnlyList<StatementSyntax> statements)
    {
        BlockSyntax? block = info.PatternSyntax switch
        {
            MethodDeclarationSyntax { Body: { } b } => b,
            LocalFunctionStatementSyntax { Body: { } b } => b,
            _ => null,
        };
        statements = block?.Statements.ToList() ?? (IReadOnlyList<StatementSyntax>)System.Array.Empty<StatementSyntax>();
        return block is not null;
    }

    private static List<RunElement> BuildRun(MatchContext ctx, IReadOnlyList<StatementSyntax> statements)
    {
        var run = new List<RunElement>();
        foreach (var s in statements)
        {
            if (ctx.Markers.TryGetStatementHole(ctx.Info.SemanticModel, s, out var hole))
                run.Add(new HoleElement { Hole = hole });
            else
                run.Add(new LiteralElement { Statement = s });
        }
        return run;
    }

    private static void EmitBareRun(List<StatementSyntax> body, IReadOnlyList<StatementSyntax> statements, MatchContext ctx)
    {
        var run = BuildRun(ctx, statements);

        // v1 fixed-arity: every element has a known width. (Variable-length Some/All/Opt -> Task 12.)
        int width = run.Sum(e => e switch
        {
            LiteralElement => 1,
            HoleElement h => h.Hole.Quantifier == MatchMarkers.Quant.Exactly ? h.Hole.Count : 1, // One=1
            _ => 1,
        });

        body.Add(ParseStatement("if (node is not BlockSyntax _blk) return null;"));

        var matchTypeName = ctx.Info.PatternName + "Match";
        var attempt = new List<StatementSyntax>();
        int slot = 0;
        foreach (var element in run)
        {
            switch (element)
            {
                case LiteralElement lit:
                    EmitNodeMatch(attempt, $"_blk.Statements[_o + {slot}]", lit.Statement, ctx);
                    slot += 1;
                    break;
                case HoleElement { Hole.Kind: MatchMarkers.HoleKind.Capture, Hole.Quantifier: MatchMarkers.Quant.One } one:
                    EmitStatementCapture(attempt, $"_blk.Statements[_o + {slot}]", one.Hole.Capture!, "StatementSyntax", ctx);
                    slot += 1;
                    break;
                case HoleElement { Hole.Quantifier: MatchMarkers.Quant.One }:        // wildcard One -> match, no capture
                    slot += 1;
                    break;
                case HoleElement { Hole.Quantifier: MatchMarkers.Quant.Exactly } ex:
                    if (ex.Hole.Kind == MatchMarkers.HoleKind.Capture)
                        EmitStatementListCapture(attempt, "_blk", "_o", slot, ex.Hole.Count, ex.Hole.Capture!, ctx);
                    slot += ex.Hole.Count;
                    break;
            }
        }
        var orderedArgs = string.Join(", ", OrderCaptures(ctx).Select(c => c.LocalName));
        attempt.Add(ParseStatement($"return new {matchTypeName}({orderedArgs});"));

        var attemptFn = LocalFunctionStatement(ParseTypeName($"{matchTypeName}?"), Identifier("_TryAt"))
            .WithParameterList(ParseParameterList("(int _o)"))
            .WithBody(Block(attempt));
        body.Add(attemptFn);
        body.Add(ParseStatement(
            $"for (int _o = 0; _o + {width} <= _blk.Statements.Count; _o++) {{ var _r = _TryAt(_o); if (_r is not null) return _r; }}"));
        body.Add(ParseStatement("return null;"));
    }

    // accessor is EITHER a SyntaxNode (a node-child boundary, e.g. the embedded statement of `if (cond)
    // guard.One();`, projected via `…AsNode()`) OR a StatementSyntax (the `_blk.Statements[..]` indexer). The
    // `is not {memberType} {local}` narrows AT the hole regardless of which (mirrors EmitCapture): it binds a
    // {memberType}-typed local so the record member binds with NO CS0266, and rejects a non-statement at the
    // node-child site. Do NOT bind with `var {local} = {accessor};` — that is the round-1 CS0266. See the
    // "Accessor type contract" design note; this method has two call sites (direct indexer vs `.AsNode()`
    // child) and the narrow makes BOTH correct (a redundant-but-harmless true-narrow on the indexer site).
    private static void EmitStatementCapture(List<StatementSyntax> body, string accessor, IParameterSymbol param, string memberType, MatchContext ctx)
    {
        var local = "cap_" + param.Name;
        var member = char.ToUpperInvariant(param.Name[0]) + param.Name.Substring(1);
        body.Add(ParseStatement($"if ({accessor} is not {memberType} {local}) return null;"));
        if (!ctx.BoundCaptureLocals.Contains(local))
        {
            ctx.BoundCaptureLocals.Add(local);
            ctx.Captures.Add(new Capture { LocalName = local, MemberName = member, MemberType = memberType, Ordinal = param.Ordinal });
        }
    }

    private static void EmitStatementListCapture(List<StatementSyntax> body, string blk, string offsetVar, int slot, int count, IParameterSymbol param, MatchContext ctx)
    {
        var local = "cap_" + param.Name;
        var member = char.ToUpperInvariant(param.Name[0]) + param.Name.Substring(1);
        body.Add(ParseStatement(
            $"var {local} = new SyntaxList<StatementSyntax>({blk}.Statements.Skip({offsetVar} + {slot}).Take({count}));"));
        if (!ctx.BoundCaptureLocals.Contains(local))
        {
            ctx.BoundCaptureLocals.Add(local);
            ctx.Captures.Add(new Capture { LocalName = local, MemberName = member, MemberType = "SyntaxList<StatementSyntax>", Ordinal = param.Ordinal });
        }
    }
```

> Note the `cond` capture: `if (cond) only.One();` — the `if` is a **literal** `IfStatementSyntax` whose embedded statement is the `only.One()` hole. So the literal-statement walk for the `if` must itself descend and hit the statement hole at the embedded-statement child. Extend `EmitNodeMatch` to treat an embedded `ExpressionStatementSyntax` child that classifies as a statement hole the same way (capture the embedded statement). Add, near the top of `EmitNodeMatch`, after the expression-hole checks:

```csharp
        // Embedded statement hole (e.g. the body of `if (cond) guard.One();`): a [Capture] Stmt invocation
        // sitting as a single embedded statement captures that one statement node. `accessor` here is what the
        // PARENT recursion passed for this node-child position — already `{listVar}[{i}].AsNode()` (a
        // SyntaxNode). Pass it UNCHANGED: NO second `.AsNode()` (the round-1 high: `accessor + ".AsNode()"`
        // produced `…AsNode().AsNode()`, CS1061). EmitStatementCapture type-narrows `is not StatementSyntax
        // {local}` at the hole, so the SyntaxNode binds to a StatementSyntax member with no CS0266.
        if (pattern is StatementSyntax embedded
            && ctx.Markers.TryGetStatementHole(ctx.Info.SemanticModel, embedded, out var embeddedHole)
            && embeddedHole is { Kind: MatchMarkers.HoleKind.Capture, Quantifier: MatchMarkers.Quant.One })
        {
            EmitStatementCapture(body, accessor, embeddedHole.Capture!, "StatementSyntax", ctx);
            return;
        }
```

Add `using System.Linq;` is already present; ensure `SyntaxList<StatementSyntax>` and `.Skip/.Take` (LINQ over `SyntaxList`) compile in the *generated* file — `SyntaxList<T>` is enumerable and has a constructor taking `IEnumerable<T>`; the generated file imports `Microsoft.CodeAnalysis` (for `SyntaxList<>`) and needs `System.Linq` for `Skip/Take`. Add `using System.Linq;` to the generated compilation unit's usings in `Compose`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Synto.Test --filter "FullyQualifiedName~MatchRoundTripTests.Bare_FixedArity_MatchesContainedIfWithOneStatement"`
Expected: PASS.

- [ ] **Step 5: Add + accept snapshot, then commit** — add `Bare_OneGuard` to `MatchSnapshotTests`, accept (block-root, `_TryAt(int _o)`, leftmost scan over `width`).

```bash
git add src/Synto.SourceGenerator/Matching/MatchMarkers.cs src/Synto.SourceGenerator/Matching/MatchEmitter.cs \
        test/Synto.Test/Match/MatchRoundTripTests.cs test/Synto.Test/Match/MatchSnapshotTests.cs \
        test/Synto.Test/Match/snapshots/MatchSnapshotTests.Bare_OneGuard#M.OneGuard.g.verified.cs
git commit -m "feat(matching): Bare run alignment for fixed-arity elements (literal/One/Exactly)"
```

---

### Task 12: Emitter — `Bare` single variable-length quantifier (`Some`/`All`/`Opt`) + statement wildcards

**Files:**
- Modify: `src/Synto.SourceGenerator/Matching/MatchEmitter.cs` (single greedy split)
- Test: `MatchRoundTripTests.cs`, `MatchSnapshotTests.cs`

**Interfaces:**
- Produces: a run with **at most one** variable-length element. The fixed elements before it consume from the offset; the fixed elements after it consume from the block end; the variable element absorbs the middle slice (`All`=0+, `Some`=1+, `Opt`=0/1). Single deterministic greedy split (spec §3.4/§4).

- [ ] **Step 1: Write the failing round-trip test** — add to `MatchRoundTripTests`:

```csharp
    [Match<M>(MatchOption.Bare)]
    private static void GuardThenRest([Capture] bool cond, [Capture] Stmt guard, [Capture] Stmt rest)
    {
        if (cond)
            guard.One();
        rest.All();
    }

    [Fact]
    public void Bare_OneVariableLength_SplitsDeterministically()
    {
        var block = (Microsoft.CodeAnalysis.CSharp.Syntax.BlockSyntax)
            SyntaxFactory.ParseStatement("{ if (ok) Go(); A(); B(); C(); }");
        var m = M.GuardThenRest(block);
        Assert.NotNull(m);
        Assert.Equal("ok", m!.Cond.ToString());
        Assert.Equal("Go();", m.Guard.ToString());
        Assert.Equal(3, m.Rest.Count);          // A(); B(); C();
        Assert.Equal("A();", m.Rest[0].ToString());

        var noRest = (Microsoft.CodeAnalysis.CSharp.Syntax.BlockSyntax)
            SyntaxFactory.ParseStatement("{ if (ok) Go(); }");
        var m2 = M.GuardThenRest(noRest);
        Assert.NotNull(m2);
        Assert.Equal(0, m2!.Rest.Count);        // All() = 0+
    }

    [Fact]
    public void Bare_FixedElementAfterVariable_IndexesTail()
    {
        // A LITERAL statement AFTER the variable-length quantifier (`return;` is literal — it binds to no
        // marker), so after = 1 and the `_o + before + _var`-relative tail indexing actually runs (the
        // GuardThenRest case puts the variable LAST, after = 0, and never reaches it).
        [Match<M>(MatchOption.Bare)]
        static void RunThenReturn([Capture] Stmt body) { body.All(); return; }

        var ok = (Microsoft.CodeAnalysis.CSharp.Syntax.BlockSyntax)
            SyntaxFactory.ParseStatement("{ A(); B(); return; }");
        var m = M.RunThenReturn(ok);
        Assert.NotNull(m);
        Assert.Equal(2, m!.Body.Count);          // A(); B();  — the trailing `return;` is the fixed tail, not captured
        Assert.Equal("A();", m.Body[0].ToString());

        // a trailing non-`return` statement breaks the match — proves the tail element is checked at the _var-relative slot
        var bad = (Microsoft.CodeAnalysis.CSharp.Syntax.BlockSyntax)
            SyntaxFactory.ParseStatement("{ A(); B(); C(); }");
        Assert.Null(M.RunThenReturn(bad));
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/Synto.Test --filter "FullyQualifiedName~MatchRoundTripTests.Bare_OneVariableLength_SplitsDeterministically|FullyQualifiedName~MatchRoundTripTests.Bare_FixedElementAfterVariable_IndexesTail"`
Expected: FAIL — the `rest.All()`/`body.All()` element has no fixed width; the Task 11 `width` sum mis-handles it (over-/under-counts) so no match, wrong slice, or no tail check.

- [ ] **Step 3: Write minimal implementation** — generalize `EmitBareRun` to a single greedy split. Compute `before` = fixed width of elements before the variable element, `after` = fixed width of elements after it, and `varElement` (0 or 1). When there is no variable element, fall back to Task 11's fixed path. When there is one:

```csharp
        var varIndex = run.FindIndex(e => e is HoleElement h && h.Hole.IsVariableLength);
        if (varIndex < 0) { /* existing fixed-arity path */ }
        else
        {
            int before = run.Take(varIndex).Sum(WidthOf);
            int after = run.Skip(varIndex + 1).Sum(WidthOf);
            var varHole = ((HoleElement)run[varIndex]).Hole;

            // attempt body, parameterized by offset _o:
            //   match the `before` fixed elements at _o.._o+before
            //   let varCount = (blkCount - _o - before - after)  (the greedy middle)
            //   if varCount < min(varHole) return null;  (Some=1, All=0, Opt: 0..1)
            //   if (Opt && varCount > 1) return null;
            //   capture/wildcard the middle slice [_o+before .. +varCount]
            //   match the `after` fixed elements at the tail
        }
```

Concretely emit (inside `_TryAt(int _o)`):

```csharp
        // before-elements at fixed slots 0.._before
        // (emit literal/One/Exactly via EmitNodeMatch/EmitStatementCapture as in Task 11, slot from 0)
        attempt.Add(ParseStatement($"int _var = _blk.Statements.Count - _o - {before} - {after};"));
        var min = varHole.Quantifier == MatchMarkers.Quant.Some ? 1 : 0;
        attempt.Add(ParseStatement($"if (_var < {min}) return null;"));
        if (varHole.Quantifier == MatchMarkers.Quant.Opt)
            attempt.Add(ParseStatement("if (_var > 1) return null;"));
        if (varHole.Kind == MatchMarkers.HoleKind.Capture)
        {
            if (varHole.Quantifier == MatchMarkers.Quant.Opt)
                EmitOptionalStatementCapture(attempt, "_blk", "_o", before, varHole.Capture!, ctx);
            else
                EmitVariableStatementListCapture(attempt, "_blk", "_o", before, varHole.Capture!, ctx);
        }

        // after-elements: the fixed run AFTER the variable element. Element j (0-based among the after-run)
        // sits at the RUNTIME index `_o + before + _var + <afterSlot>` — i.e. relative to the computed `_var`,
        // not a generation-time constant. This is the index path the GuardThenRest test (rest.All() LAST,
        // after = 0) never exercises; the Bare_FixedElementAfterVariable round-trip below pins it.
        int afterSlot = 0;
        foreach (var element in run.Skip(varIndex + 1))
        {
            string at = $"_blk.Statements[_o + {before} + _var + {afterSlot}]";
            switch (element)
            {
                case LiteralElement lit:
                    EmitNodeMatch(attempt, at, lit.Statement, ctx);
                    afterSlot += 1;
                    break;
                case HoleElement { Hole.Kind: MatchMarkers.HoleKind.Capture, Hole.Quantifier: MatchMarkers.Quant.One } one:
                    EmitStatementCapture(attempt, at, one.Hole.Capture!, "StatementSyntax", ctx);
                    afterSlot += 1;
                    break;
                case HoleElement { Hole.Quantifier: MatchMarkers.Quant.One }:                       // wildcard One
                    afterSlot += 1;
                    break;
                case HoleElement { Hole.Quantifier: MatchMarkers.Quant.Exactly } ex:
                    if (ex.Hole.Kind == MatchMarkers.HoleKind.Capture)
                        EmitStatementListCapture(attempt, "_blk", $"_o + {before} + _var", afterSlot, ex.Hole.Count, ex.Hole.Capture!, ctx);
                    afterSlot += ex.Hole.Count;
                    break;
            }
        }
        // (a second variable-length element in the after-run is impossible here — >1 variable-length is
        // rejected by SY1204, Task 16 — so the after-run is all fixed-arity, indices are deterministic.)
```

Add the variable-length list/optional capture helpers:

```csharp
    private static void EmitVariableStatementListCapture(List<StatementSyntax> body, string blk, string offsetVar, int before, IParameterSymbol param, MatchContext ctx)
    {
        var local = "cap_" + param.Name;
        var member = char.ToUpperInvariant(param.Name[0]) + param.Name.Substring(1);
        body.Add(ParseStatement($"var {local} = new SyntaxList<StatementSyntax>({blk}.Statements.Skip({offsetVar} + {before}).Take(_var));"));
        if (ctx.BoundCaptureLocals.Add(local))
            ctx.Captures.Add(new Capture { LocalName = local, MemberName = member, MemberType = "SyntaxList<StatementSyntax>" });
    }

    private static void EmitOptionalStatementCapture(List<StatementSyntax> body, string blk, string offsetVar, int before, IParameterSymbol param, MatchContext ctx)
    {
        var local = "cap_" + param.Name;
        var member = char.ToUpperInvariant(param.Name[0]) + param.Name.Substring(1);
        body.Add(ParseStatement($"StatementSyntax? {local} = _var == 1 ? {blk}.Statements[{offsetVar} + {before}] : null;"));
        if (ctx.BoundCaptureLocals.Add(local))
            ctx.Captures.Add(new Capture { LocalName = local, MemberName = member, MemberType = "StatementSyntax?" });
    }
```

> `ctx.BoundCaptureLocals` is a `HashSet<string>`; use `.Add(local)` (returns false if present) to avoid duplicate members. The outer scan loop bound becomes `_o + {before} + {after} <= _blk.Statements.Count`. Wildcard variable-length (`Statement.All()` etc.) consumes the middle without capturing (skip the capture emit).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Synto.Test --filter "FullyQualifiedName~MatchRoundTripTests.Bare_OneVariableLength_SplitsDeterministically|FullyQualifiedName~MatchRoundTripTests.Bare_FixedElementAfterVariable_IndexesTail"`
Expected: PASS. Re-run all of `MatchRoundTripTests` to confirm fixed-arity Bare (Task 11) still passes.

- [ ] **Step 5: Add + accept snapshots, then commit** — add `Bare_GuardThenRest`, a wildcard variant (`Statement.All()`), and **`Bare_FixedAfterVariable`** (the `body.All(); return;` shape, so the `_var`-relative tail index is snapshot-pinned too) to `MatchSnapshotTests`, accept.

```bash
git add src/Synto.SourceGenerator/Matching/MatchEmitter.cs test/Synto.Test/Match/MatchRoundTripTests.cs \
        test/Synto.Test/Match/MatchSnapshotTests.cs test/Synto.Test/Match/snapshots/
git commit -m "feat(matching): Bare single variable-length quantifier (Some/All/Opt) greedy split"
```

---

### Task 13: Emitter — `None` (declaration-rooted, full body)

**Files:**
- Modify: `src/Synto.SourceGenerator/Matching/MatchEmitter.cs` (None branch)
- Test: `MatchRoundTripTests.cs`, `MatchSnapshotTests.cs`

**Interfaces:**
- Produces: for `MatchOption.None`, a matcher rooted on a candidate **declaration** (`MethodDeclarationSyntax` or `LocalFunctionStatementSyntax`), matching the candidate's **body block** against the pattern body **fully** (both ends anchored — no slack). The candidate's name/parameters are not constrained (the pattern's params are captures). Equivalent to Bare with implicit `Block.Start()` + `Block.End()` (spec §3.2/§3.9).

- [ ] **Step 1: Write the failing round-trip test** — add to `MatchRoundTripTests`:

```csharp
    [Match<M>(MatchOption.None)]
    private static void SingleDiscard([Capture] object x) { _ = x; }

    [Fact]
    public void None_MatchesDeclarationWhoseBodyIsExactlyTheShape()
    {
        var exact = SyntaxFactory.ParseMemberDeclaration("void F() { _ = y.z; }")!;
        var m = M.SingleDiscard(exact);
        Assert.NotNull(m);
        Assert.Equal("y.z", m!.X.ToString());

        var extra = SyntaxFactory.ParseMemberDeclaration("void F() { _ = y.z; More(); }")!;
        Assert.Null(M.SingleDiscard(extra));   // None is fully bounded: trailing statement breaks the match
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Synto.Test --filter "FullyQualifiedName~MatchRoundTripTests.None_MatchesDeclarationWhoseBodyIsExactlyTheShape"`
Expected: FAIL — `MatchOption.None` hits the `default` arm; `M.SingleDiscard` not emitted.

- [ ] **Step 3: Write minimal implementation** — add the None branch to `Emit`:

```csharp
            case MatchOption.None when TryGetBareRun(info, out var stmts):
                EmitDeclarationFullBody(body, stmts, ctx);
                break;
```

```csharp
    private static void EmitDeclarationFullBody(List<StatementSyntax> body, IReadOnlyList<StatementSyntax> statements, MatchContext ctx)
    {
        // Root on the candidate declaration; pull its body block; match the run fully (anchored both ends).
        body.Add(ParseStatement(
            "BlockSyntax? _blk = node switch { MethodDeclarationSyntax md => md.Body, LocalFunctionStatementSyntax lf => lf.Body, _ => null };"));
        body.Add(ParseStatement("if (_blk is null) return null;"));

        var run = BuildRun(ctx, statements);
        // Fully bounded: the block must contain EXACTLY the run (offset fixed at 0, no leftover).
        // Reuse the Bare alignment but with a single offset _o = 0 and an exact-length check.
        EmitAnchoredRun(body, run, ctx, anchorStart: true, anchorEnd: true);
    }
```

Refactor the Task 11/12 scan into a shared `EmitAnchoredRun(body, run, ctx, anchorStart, anchorEnd)`:
- `anchorStart && anchorEnd` (None, or Bare with both anchors): fix `_o = 0` and require the run consumes the **entire** block — emit `if (_blk.Statements.Count != <exactWidthOrLowerBoundForVar>) ...`; for a single variable element, `_var = Count - before - after` and no scan loop.
- otherwise scan offsets as in Tasks 11/12.

Have `EmitBareRun` (Tasks 11/12) call `EmitAnchoredRun(..., anchorStart:false, anchorEnd:false)` so all three share one alignment core. (Anchors `Block.Start()/End()` in Bare set the flags in Task 14.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Synto.Test --filter "FullyQualifiedName~MatchRoundTripTests.None_MatchesDeclarationWhoseBodyIsExactlyTheShape"`
Expected: PASS. Re-run all round-trips to confirm the `EmitAnchoredRun` refactor preserved Bare/Single behavior.

- [ ] **Step 4b: Re-review + intentionally re-accept the Task 10–12 goldens (refactor changes the emitted shape).** Routing statement-`Single`/`Bare` through the new shared `EmitAnchoredRun` **changes the generated matcher text** for patterns whose `*.verified.cs` goldens were accepted in Tasks 10–12 (`StatementSingle_Return`, `Bare_OneGuard`, `Bare_GuardThenRest`, `Bare_FixedAfterVariable`, the wildcard variant). The round-trips re-run green (behavior preserved) but **do not cover snapshot shape** — so run the snapshot suite, expect those goldens to show as changed (`*.received.cs`), **review each diff deliberately** (confirm it is the expected restructure into the shared core, not a regression), and re-accept. This is an *intentional, reviewed* re-accept — not a blind mass-accept (architecture.md). Then run `SnapshotOrphanGuardTest` and confirm it stays green (no orphaned/renamed snapshot files left behind by the reshape).

Run: `dotnet test test/Synto.Test --filter "FullyQualifiedName~MatchSnapshotTests"` → review changed goldens → accept.
Run: `dotnet test test/Synto.Test --filter "FullyQualifiedName~SnapshotOrphanGuardTest"` → expect PASS.

- [ ] **Step 5: Add + accept snapshot, then commit** — add `None_SingleDiscard` (and re-add the `None` `SelfCompare` from Task 9 if deferred), accept. Stage the **re-accepted** Task 10–12 goldens alongside the new one (they are part of this commit).

```bash
git add src/Synto.SourceGenerator/Matching/MatchEmitter.cs test/Synto.Test/Match/MatchRoundTripTests.cs \
        test/Synto.Test/Match/MatchSnapshotTests.cs test/Synto.Test/Match/snapshots/
git commit -m "feat(matching): None (declaration-rooted, fully-bounded body match)"
```

---

### Task 14: Anchors `Block.Start()` / `Block.End()` + SY1201

**Files:**
- Create: `src/Synto.SourceGenerator/Matching/MatchDiagnostics.cs`
- Modify: `src/Synto.SourceGenerator/Matching/MatchMarkers.cs` (anchor detection)
- Modify: `src/Synto.SourceGenerator/Matching/MatchEmitter.cs` (consume anchors in Bare/Single; reject in None)
- Test: `MatchRoundTripTests.cs`, `MatchDiagnosticsTests.cs`, `MatchSnapshotTests.cs`

**Interfaces:**
- Produces:
  - `MatchMarkers.TryGetAnchor(SemanticModel, StatementSyntax, out bool isStart)` — `Block.Start()`/`Block.End()`.
  - `MatchDiagnostics.AnchorNotAllowed(LocationInfo?)` → **SY1201**.
  - Emitter: in Bare/statement-Single, a leading `Block.Start()` sets `anchorStart`, a trailing `Block.End()` sets `anchorEnd` (the anchor statements are removed from the run). In None, any anchor → SY1201 on the marker call, emit nothing.

- [ ] **Step 1: Write the failing tests**

Round-trip (`MatchRoundTripTests`):

```csharp
    [Fact]
    public void Anchor_End_PinsToLastStatement()
    {
        // object return (per §3.9) — `void` + `return result;` is CS0127. The trailing `Block.End();` is
        // unreachable after the return (CS0162, a harmless warning suppressed file-wide); it is the anchor,
        // not executable code (the body is phantom).
        [Match<M>(MatchOption.Single)]
        static object TrailingReturn([Capture] object result) { return result; Block.End(); }

        var endsWithReturn = (Microsoft.CodeAnalysis.CSharp.Syntax.BlockSyntax)
            SyntaxFactory.ParseStatement("{ Foo(); return v; }");
        Assert.NotNull(M.TrailingReturn(endsWithReturn));

        var returnNotLast = (Microsoft.CodeAnalysis.CSharp.Syntax.BlockSyntax)
            SyntaxFactory.ParseStatement("{ return v; Foo(); }");
        Assert.Null(M.TrailingReturn(returnNotLast)); // return is not the LAST statement
    }
```

Diagnostic (`MatchDiagnosticsTests`):

```csharp
    [Fact]
    public void AnchorInNonePattern_ReportsSY1201()
    {
        var diagnostics = MatchTestHarness.Diagnostics(
            """
            using Synto.Matching;
            partial class M { }
            static class Patterns {
                [Match<M>(MatchOption.None)]
                static void P([Capture] object x) { _ = x; Block.End(); }
            }
            """);

        var diag = Assert.Single(diagnostics, d => d.Id == "SY1201");
        Assert.NotEqual(Location.None, diag.Location);
        Assert.False(diag.Location.SourceSpan.IsEmpty);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/Synto.Test --filter "FullyQualifiedName~Anchor_End_PinsToLastStatement|FullyQualifiedName~AnchorInNonePattern_ReportsSY1201"`
Expected: FAIL — anchors are currently walked as literal statements (`Block.End()` over-constrains / no SY1201).

- [ ] **Step 3: Write minimal implementation**

`src/Synto.SourceGenerator/Matching/MatchDiagnostics.cs`:

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Synto.Matching;

/// <summary>The Matching feature's pattern-specific diagnostics (SY12xx range; see AnalyzerReleases.Unshipped.md).</summary>
internal static class MatchDiagnostics
{
    private const string Cat = "Synto.Matching";

    private static readonly DiagnosticDescriptor _anchorNotAllowed = new("SY1201",
        "Anchor not allowed",
        "Block.Start()/Block.End() are not allowed in a None pattern — its braces already bound the match",
        Cat, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static DiagnosticInfo AnchorNotAllowed(LocationInfo? location) =>
        new(_anchorNotAllowed, location, new EquatableArray<string>(ImmutableArray<string>.Empty));
}
```

Add anchor detection to `MatchMarkers`:

```csharp
    public bool TryGetAnchor(SemanticModel model, StatementSyntax stmt, out bool isStart)
    {
        isStart = false;
        if (stmt is not ExpressionStatementSyntax { Expression: InvocationExpressionSyntax inv }) return false;
        if (model.GetSymbolInfo(inv).Symbol is not IMethodSymbol m) return false;
        if (!SymbolEqualityComparer.Default.Equals(m.ContainingType, BlockMarker)) return false;   // resolved symbol, not ToDisplayString()
        isStart = m.Name == "Start";
        return m.Name is "Start" or "End";
    }
```

In `MatchEmitter`, before building the run for None/Bare/Single, split anchors off:

```csharp
    private static (bool anchorStart, bool anchorEnd, List<StatementSyntax> core, List<StatementSyntax> anchors)
        ExtractAnchors(MatchContext ctx, IReadOnlyList<StatementSyntax> statements)
    {
        bool start = false, end = false;
        var core = new List<StatementSyntax>();
        var anchors = new List<StatementSyntax>();
        for (int i = 0; i < statements.Count; i++)
        {
            if (ctx.Markers.TryGetAnchor(ctx.Info.SemanticModel, statements[i], out var isStart))
            {
                anchors.Add(statements[i]);
                if (isStart) start = true; else end = true;
            }
            else core.Add(statements[i]);
        }
        return (start, end, core, anchors);
    }
```

- None branch: if `anchors.Count > 0`, add `MatchDiagnostics.AnchorNotAllowed(LocationInfo.CreateFrom(anchors[0].GetLocation()))` for each anchor and `return null` from `Emit`.
- Bare/statement-Single branches: pass `anchorStart`/`anchorEnd` into `EmitAnchoredRun`; emit the run from `core`.

Make `Emit` able to signal "diagnostic, no output": have the branch set a `bool failed` and return `null` when an anchor was rejected (diagnostics already in the list — the generator then emits diagnostics-only, like Templating's converter-error bail).

For statement-`Single` (Task 10), `Block.End()` after the single statement now sets `anchorEnd`, so instead of "leftmost any offset" the scan requires the matched statement to be the **last** (`_o == _blk.Statements.Count - 1`). Thread the anchors into `EmitStatementSingle` (route it through `EmitAnchoredRun` with a single fixed element).

**Register SY1201 now** — append to `src/Synto.SourceGenerator/AnalyzerReleases.Unshipped.md` (under `### New Rules`, after `SY1009`) the single line `SY1201 | Synto.Matching | Error | MatchDiagnostics`, so this task's green-gate / Release build is RS2008-clean (do not defer registration to Task 18).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/Synto.Test --filter "FullyQualifiedName~Anchor_End_PinsToLastStatement|FullyQualifiedName~AnchorInNonePattern_ReportsSY1201"`
Expected: PASS.

- [ ] **Step 4b: Re-review + re-accept the statement-`Single` golden (anchor rerouting changes its shape).** Rerouting statement-`Single` (Task 10) through `EmitAnchoredRun` with the anchor flags changes the **emitted matcher text** for the Task-10 `StatementSingle_Return` golden (now an anchored single-element run, not a bespoke leftmost scan). Run the snapshot suite, review that golden's diff deliberately (expected restructure, not a regression), re-accept it, and confirm `SnapshotOrphanGuardTest` stays green.

Run: `dotnet test test/Synto.Test --filter "FullyQualifiedName~MatchSnapshotTests"` → review changed goldens → accept; then `--filter "FullyQualifiedName~SnapshotOrphanGuardTest"` → expect PASS.

- [ ] **Step 5: Add + accept snapshot, then commit** — add `Single_TrailingReturnAnchored` to `MatchSnapshotTests`, accept; stage the re-accepted `StatementSingle_Return` golden with it.

```bash
git add src/Synto.SourceGenerator/Matching/MatchDiagnostics.cs src/Synto.SourceGenerator/Matching/MatchMarkers.cs \
        src/Synto.SourceGenerator/Matching/MatchEmitter.cs src/Synto.SourceGenerator/AnalyzerReleases.Unshipped.md \
        test/Synto.Test/Match/MatchRoundTripTests.cs \
        test/Synto.Test/Match/MatchDiagnosticsTests.cs test/Synto.Test/Match/MatchSnapshotTests.cs \
        test/Synto.Test/Match/snapshots/
git commit -m "feat(matching): Block.Start/End anchors + SY1201 anchor-in-None diagnostic (+register SY1201)"
```

---

### Task 15: SY1203 — phantom `foreach` over a `[Capture]` (deferred-`foreach`)

**Files:**
- Modify: `src/Synto.SourceGenerator/Matching/MatchDiagnostics.cs` (SY1203)
- Modify: `src/Synto.SourceGenerator/Matching/MatchEmitter.cs` (pre-scan the pattern; reject)
- Test: `MatchDiagnosticsTests.cs`

**Interfaces:**
- Produces: `MatchDiagnostics.ForeachRepetitionNotSupported(LocationInfo?)` → **SY1203**. The emitter pre-scans the pattern body for a `ForEachStatementSyntax` whose `Expression` binds to a `[Capture]` parameter; if found, report SY1203 on the `foreach` and emit nothing.

- [ ] **Step 1: Write the failing test** — add to `MatchDiagnosticsTests`:

```csharp
    [Fact]
    public void ForeachOverCapture_ReportsSY1203()
    {
        var diagnostics = MatchTestHarness.Diagnostics(
            """
            using System.Text;
            using System.Collections.Generic;
            using Synto.Matching;
            partial class M { }
            static class Patterns {
                [Match<M>(MatchOption.Bare)]
                static void Concat([Capture] StringBuilder sb, [Capture] List<object> parts)
                {
                    foreach (var part in parts)
                        sb.Append(part);
                }
            }
            """);

        var diag = Assert.Single(diagnostics, d => d.Id == "SY1203");
        Assert.NotEqual(Location.None, diag.Location);
        Assert.False(diag.Location.SourceSpan.IsEmpty);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Synto.Test --filter "FullyQualifiedName~ForeachOverCapture_ReportsSY1203"`
Expected: FAIL — the `foreach` is walked literally (or the emitter throws → SY0000), no SY1203.

- [ ] **Step 3: Write minimal implementation**

Add to `MatchDiagnostics`:

```csharp
    private static readonly DiagnosticDescriptor _foreachNotSupported = new("SY1203",
        "foreach repetition not yet supported",
        "A foreach over a [Capture] parameter (pattern repetition) is not supported in v1; its backtracking lowering is deferred",
        Cat, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static DiagnosticInfo ForeachRepetitionNotSupported(LocationInfo? location) =>
        new(_foreachNotSupported, location, new EquatableArray<string>(ImmutableArray<string>.Empty));
```

In `MatchEmitter.Emit`, before dispatching on `info.Option`, pre-scan and bail — reuse the already-built `ctx.Markers` (do NOT rebuild `MatchMarkers.Create` per `foreach`):

```csharp
        if (TryFindDeferredForeach(info, ctx.Markers, out var foreachLocation))
        {
            diagnostics.Add(MatchDiagnostics.ForeachRepetitionNotSupported(LocationInfo.CreateFrom(foreachLocation)));
            return null;
        }
```

```csharp
    private static bool TryFindDeferredForeach(MatchInfo info, MatchMarkers markers, out Location? location)
    {
        foreach (var fe in info.PatternSyntax.DescendantNodes().OfType<ForEachStatementSyntax>())
        {
            if (fe.Expression is IdentifierNameSyntax id
                && info.SemanticModel.GetSymbolInfo(id).Symbol is IParameterSymbol p
                && markers.Captures.Any(c => SymbolEqualityComparer.Default.Equals(c, p)))   // reuse markers, not Create()-per-node
            {
                location = fe.GetLocation();
                return true;
            }
        }
        location = null;
        return false;
    }
```

**Register SY1203 now** — append `SY1203 | Synto.Matching | Error | MatchDiagnostics` to `src/Synto.SourceGenerator/AnalyzerReleases.Unshipped.md` (this task's green-gate stays RS2008-clean).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Synto.Test --filter "FullyQualifiedName~ForeachOverCapture_ReportsSY1203"`
Expected: PASS.

- [ ] **Step 5: Commit** (local commit on `experimental/matching` — no push)

```bash
git add src/Synto.SourceGenerator/Matching/MatchDiagnostics.cs src/Synto.SourceGenerator/Matching/MatchEmitter.cs \
        src/Synto.SourceGenerator/AnalyzerReleases.Unshipped.md test/Synto.Test/Match/MatchDiagnosticsTests.cs
git commit -m "feat(matching): SY1203 reject phantom foreach over [Capture] (deferred lowering, +register SY1203)"
```

---

### Task 16: SY1204 — quantifier placement needs backtracking (two variable-length / ambiguous)

**Files:**
- Modify: `src/Synto.SourceGenerator/Matching/MatchDiagnostics.cs` (SY1204)
- Modify: `src/Synto.SourceGenerator/Matching/MatchEmitter.cs` (reject runs with >1 variable-length element)
- Test: `MatchDiagnosticsTests.cs`

**Interfaces:**
- Produces: `MatchDiagnostics.QuantifierNeedsBacktracking(LocationInfo?)` → **SY1204**. In the run alignment (Tasks 11–13), if a run (after anchor extraction) contains **more than one** variable-length element (`Some`/`All`/`Opt`), report SY1204 on the second variable-length hole and emit nothing.

- [ ] **Step 1: Write the failing test** — add to `MatchDiagnosticsTests`:

```csharp
    [Fact]
    public void TwoVariableLengthQuantifiers_ReportsSY1204()
    {
        var diagnostics = MatchTestHarness.Diagnostics(
            """
            using Synto.Matching;
            partial class M { }
            static class Patterns {
                [Match<M>(MatchOption.Bare)]
                static void P([Capture] Stmt head, [Capture] Stmt tail)
                {
                    head.Some();
                    tail.All();
                }
            }
            """);

        var diag = Assert.Single(diagnostics, d => d.Id == "SY1204");
        Assert.NotEqual(Location.None, diag.Location);
        Assert.False(diag.Location.SourceSpan.IsEmpty);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Synto.Test --filter "FullyQualifiedName~TwoVariableLengthQuantifiers_ReportsSY1204"`
Expected: FAIL — the alignment picks the first variable element and mis-handles the second (wrong match or SY0000), no SY1204.

- [ ] **Step 3: Write minimal implementation**

Add to `MatchDiagnostics`:

```csharp
    private static readonly DiagnosticDescriptor _quantifierNeedsBacktracking = new("SY1204",
        "Quantifier placement needs backtracking",
        "A statement run with more than one variable-length quantifier (Some/All/Opt) needs backtracking, not supported in v1",
        Cat, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static DiagnosticInfo QuantifierNeedsBacktracking(LocationInfo? location) =>
        new(_quantifierNeedsBacktracking, location, new EquatableArray<string>(ImmutableArray<string>.Empty));
```

In `EmitAnchoredRun` (the shared alignment core), before splitting, count variable-length elements; if `> 1`, locate the second one's source statement and bail:

```csharp
        var variableElements = run.Where(e => e is HoleElement h && h.Hole.IsVariableLength).ToList();
        if (variableElements.Count > 1)
        {
            // Locate the second variable-length hole's statement for the squiggle.
            var holeStatements = statements.Where(s => ctx.Markers.TryGetStatementHole(ctx.Info.SemanticModel, s, out var h) && h.IsVariableLength).ToList();
            var loc = holeStatements.Count > 1 ? holeStatements[1].GetLocation() : holeStatements.FirstOrDefault()?.GetLocation();
            ctx.Diagnostics.Add(MatchDiagnostics.QuantifierNeedsBacktracking(LocationInfo.CreateFrom(loc)));
            ctx.Aborted = true;
            return;
        }
```

`ctx.Diagnostics`/`ctx.Aborted` already exist on `MatchContext` (pinned in its final shape at Task 5) and `EmitAnchoredRun` already receives the raw `statements` (pinned signature, Task 11) — so this task only **uses** them, it does not re-plumb the contract. Wire `Emit` to copy `ctx.Diagnostics` into the outer `diagnostics` list and, after each option branch, `if (ctx.Aborted) return null;` so the generator emits diagnostics-only (like Templating's converter-error bail).

**Register SY1204 now** — append `SY1204 | Synto.Matching | Error | MatchDiagnostics` to `src/Synto.SourceGenerator/AnalyzerReleases.Unshipped.md`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Synto.Test --filter "FullyQualifiedName~TwoVariableLengthQuantifiers_ReportsSY1204"`
Expected: PASS. Re-run all round-trips (single variable-length still emits).

- [ ] **Step 5: Commit** (local commit on `experimental/matching` — no push)

```bash
git add src/Synto.SourceGenerator/Matching/MatchDiagnostics.cs src/Synto.SourceGenerator/Matching/MatchEmitter.cs \
        src/Synto.SourceGenerator/AnalyzerReleases.Unshipped.md test/Synto.Test/Match/MatchDiagnosticsTests.cs
git commit -m "feat(matching): SY1204 reject >1 variable-length quantifier per run (+register SY1204)"
```

---

### Task 17: SY1202 — provable unsatisfiable (anchor-contradiction subset)

**Files:**
- Modify: `src/Synto.SourceGenerator/Matching/MatchDiagnostics.cs` (SY1202)
- Modify: `src/Synto.SourceGenerator/Matching/MatchEmitter.cs` (detect content before `Block.Start()` / after `Block.End()`)
- Test: `MatchDiagnosticsTests.cs`

**Interfaces:**
- Produces: `MatchDiagnostics.PatternUnsatisfiable(LocationInfo?, string reason)` → **SY1202**. v1 emits it **only** for the provable anchor contradiction: a matched statement appears **before** a `Block.Start()` anchor, or **after** a `Block.End()` anchor (spec §11, conservative). Detected during anchor extraction.

- [ ] **Step 1: Write the failing test** — add to `MatchDiagnosticsTests`:

```csharp
    [Fact]
    public void ContentBeforeBlockStart_ReportsSY1202()
    {
        var diagnostics = MatchTestHarness.Diagnostics(
            """
            using Synto.Matching;
            partial class M { }
            static class Patterns {
                [Match<M>(MatchOption.Bare)]
                static void P([Capture] Stmt body)
                {
                    body.Some();
                    Block.Start();   // statements required BEFORE the block begins -> impossible
                }
            }
            """);

        var diag = Assert.Single(diagnostics, d => d.Id == "SY1202");
        Assert.NotEqual(Location.None, diag.Location);
        Assert.False(diag.Location.SourceSpan.IsEmpty);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Synto.Test --filter "FullyQualifiedName~ContentBeforeBlockStart_ReportsSY1202"`
Expected: FAIL — no SY1202 (the anchor is treated as just setting `anchorStart`, ignoring its position).

- [ ] **Step 3: Write minimal implementation**

Add to `MatchDiagnostics`:

```csharp
    private static readonly DiagnosticDescriptor _unsatisfiable = new("SY1202",
        "Pattern can never match",
        "Pattern can never match: {0}",
        Cat, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static DiagnosticInfo PatternUnsatisfiable(LocationInfo? location, string reason) =>
        new(_unsatisfiable, location, new EquatableArray<string>(ImmutableArray.Create(reason)));
```

In `ExtractAnchors`, track positions: a `Block.Start()` must precede all core statements; a `Block.End()` must follow all core statements. If a core statement index is less than a `Block.End()` index... concretely, walk in order and flag:
- a core (non-anchor) statement seen **after** a `Block.End()` anchor, or
- a core statement seen **before** a `Block.Start()` anchor.

```csharp
        bool sawEnd = false;
        Location? contradiction = null;
        Location? startAnchorLoc = null;
        bool sawCoreAfterStart = false;
        for (int i = 0; i < statements.Count; i++)
        {
            if (ctx.Markers.TryGetAnchor(ctx.Info.SemanticModel, statements[i], out var isStart))
            {
                if (isStart) { startAnchorLoc = statements[i].GetLocation(); if (sawCoreAfterStart) contradiction ??= startAnchorLoc; }
                else { sawEnd = true; }
                // ... existing start/end flag + anchors.Add ...
            }
            else
            {
                if (sawEnd) contradiction ??= statements[i].GetLocation();
                sawCoreAfterStart = true;
                // ... existing core.Add ...
            }
        }
        if (contradiction is not null)
        {
            ctx.Diagnostics.Add(MatchDiagnostics.PatternUnsatisfiable(LocationInfo.CreateFrom(contradiction),
                "content is required before Block.Start() or after Block.End()"));
            ctx.Aborted = true;
        }
```

(For the test, `body.Some()` precedes `Block.Start()`, so `sawCoreAfterStart` is true when the start anchor is seen → contradiction on the start anchor.) Keep this strictly provable-only — emit nothing on merely-suspicious patterns.

**Register SY1202 now** — append `SY1202 | Synto.Matching | Error | MatchDiagnostics` to `src/Synto.SourceGenerator/AnalyzerReleases.Unshipped.md` (all four SY12xx are now registered in the tasks that add them; Task 18 only verifies).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Synto.Test --filter "FullyQualifiedName~ContentBeforeBlockStart_ReportsSY1202"`
Expected: PASS. Re-run the Task 14 anchor round-trip to confirm well-formed anchors still emit.

- [ ] **Step 5: Commit** (local commit on `experimental/matching` — no push)

```bash
git add src/Synto.SourceGenerator/Matching/MatchDiagnostics.cs src/Synto.SourceGenerator/Matching/MatchEmitter.cs \
        src/Synto.SourceGenerator/AnalyzerReleases.Unshipped.md test/Synto.Test/Match/MatchDiagnosticsTests.cs
git commit -m "feat(matching): SY1202 provable anchor-contradiction (+register SY1202)"
```

---

### Task 18: SY0000 catch-all regression-lock + SY12xx registration audit + final gate

> **Not a red→green task — explicitly a regression-lock + whole-feature gate.** SY1201–SY1204 are already **registered in Tasks 14–17** (the task that adds each descriptor), so there is nothing to append here. The two tests below are **regression-locks** (they assert behavior implemented in earlier tasks and pass on arrival); their value is to fail loudly if a later edit *removes* a registration or the catch-all. Plus a forced end-to-end SY0000 to actually exercise the catch-convert-report path.

**Files:**
- Test: `MatchDiagnosticsTests.cs` (SY0000 unit lock + end-to-end SY0000 + a descriptor-registration audit)
- (No source change — `AnalyzerReleases.Unshipped.md` already carries all four from Tasks 14–17.)

**Interfaces:**
- Produces: a regression-lock that any internal failure → SY0000 (the catch-all `Diagnostics.InternalError`), an end-to-end proof the generator's `try/catch` reports SY0000 rather than throwing, and an audit that all four SY12xx IDs are release-tracked.

- [ ] **Step 1: Write the tests** — add to `MatchDiagnosticsTests`:

```csharp
    [Fact]   // REGRESSION-LOCK (green on arrival): the catch-all factory shape, locked against drift
    public void InternalError_MapsToSY0000()
    {
        var ex = new System.InvalidOperationException("boom");
        var diag = Diagnostics.InternalError(ex).ToDiagnostic();

        Assert.Equal("SY0000", diag.Id);
        Assert.Equal(Location.None, diag.Location);
        Assert.Contains("boom", diag.GetMessage(), System.StringComparison.Ordinal);
    }

    [Fact]   // END-TO-END: a generation-time throw must surface as SY0000 via GenerateMatcher's try/catch,
             // NOT escape the generator. Forced with an internal test-only hook (see note) so the catch arm
             // is actually executed by the pipeline, not just asserted on the factory in isolation.
    public void EmitterThrow_IsReportedAsSY0000_NotThrown()
    {
        MatchEmitter.ThrowForTesting = true;
        try
        {
            var diagnostics = MatchTestHarness.Diagnostics(
                """
                using Synto.Matching;
                partial class M { }
                static class Patterns {
                    [Match<M>(MatchOption.Single)] static object P([Capture] int a) => a;
                }
                """);
            Assert.Single(diagnostics, d => d.Id == "SY0000");   // reported, generator did not crash
        }
        finally { MatchEmitter.ThrowForTesting = false; }
    }

    [Fact]   // REGRESSION-LOCK: all four SY12xx are release-tracked (RS2008 source of truth)
    public void Sy12xxDescriptors_AreRegistered_InUnshippedReleases()
    {
        var path = System.IO.Path.Combine(RepoRoot(), "src", "Synto.SourceGenerator", "AnalyzerReleases.Unshipped.md");
        var text = System.IO.File.ReadAllText(path);
        foreach (var id in new[] { "SY1201", "SY1202", "SY1203", "SY1204" })
            Assert.Contains(id, text, System.StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "Synto.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new System.IO.DirectoryNotFoundException("repo root (Synto.sln) not found");
    }
```

> **The `ThrowForTesting` hook.** Add a single internal `internal static bool ThrowForTesting;` to `MatchEmitter`, checked at the top of `Emit` (`if (ThrowForTesting) throw new InvalidOperationException("forced");`). It is `internal` (test-visible via `InternalsVisibleTo`, already configured for `Synto.Test`), defaults `false`, and exists only so the end-to-end catch-convert-report path (Task 4's `try/catch`) is executed by a test rather than left green-on-arrival. If introducing a test-only field is judged not worth it, drop `EmitterThrow_IsReportedAsSY0000_NotThrown` and keep the unit lock — but the hook is cheap and makes the SY0000 guarantee a *tested* path, not an asserted-in-isolation one.

- [ ] **Step 2: Run tests** — they are regression-locks/end-to-end, so:

Run: `dotnet test test/Synto.Test --filter "FullyQualifiedName~MatchDiagnosticsTests.InternalError_MapsToSY0000|FullyQualifiedName~MatchDiagnosticsTests.EmitterThrow_IsReportedAsSY0000_NotThrown|FullyQualifiedName~MatchDiagnosticsTests.Sy12xxDescriptors_AreRegistered_InUnshippedReleases"`
Expected: PASS (the unit lock + the registration audit are green from earlier tasks; the forced-throw test goes red→green with the `ThrowForTesting` hook added in this task). If `Synto.sln` is not the solution file name, adjust the sentinel in `RepoRoot()` (e.g. `global.json`).

- [ ] **Step 3: (no registration step)** — SY1201–SY1204 are already in `AnalyzerReleases.Unshipped.md` (Tasks 14–17). Add only the `ThrowForTesting` hook to `MatchEmitter` for the forced-throw test.

- [ ] **Step 4: Run the full Matching suite + a Release build to verify the whole feature**

Run: `dotnet test test/Synto.Test --filter "FullyQualifiedName~Synto.Test.Match"`
Expected: PASS (all round-trip, snapshot, surface, and diagnostic tests).
Run: `dotnet build -c Release src/Synto.SourceGenerator`
Expected: PASS with **no RS2008** (every reported descriptor is release-tracked).

- [ ] **Step 5: Commit** (local commit on `experimental/matching` — no push)

```bash
git add src/Synto.SourceGenerator/Matching/MatchEmitter.cs test/Synto.Test/Match/MatchDiagnosticsTests.cs
git commit -m "test(matching): SY0000 catch-all regression-lock + end-to-end throw + SY12xx registration audit"
```

---

## Self-Review

**Spec coverage:**
- §3.2 attribute + `MatchOption` (None/Bare/Single, non-flags) → Task 1; generic-attribute discovery **spike-verified on the 5.0 floor** (evidence recorded in Global Constraints; no fallback) → Tasks 1, 4 (positive type-arg readback test).
- §3.3 expression captures + slot-typing + `[Capture<TNode>]` narrowing → Task 6 (capture + narrowing behavior, red→green); Task 7 (narrowed-output **snapshot lock**).
- §3.4 statement quantifiers One/Opt/Some/All/Exactly + single-variable-length line → Tasks 11, 12; over-the-line → Task 16.
- §3.5 wildcards (static verbs + `Expr.Any<T>()`) → Tasks 3, 8, 11, 12.
- §3.6 non-linear equality → Task 9.
- §3.7 phantom `foreach` (deferred) → SY1203 Task 15.
- §3.9 anchors + SY1201 → Task 14.
- §4 matching semantics (None decl-rooted / Bare contains-leftmost / Single statement & expression) → Tasks 5, 10, 11, 12, 13.
- §5 bespoke straight-line emission → the generic walk, all emitter tasks.
- §6 equatable pipeline / no captured `Compilation`/`ISymbol`/`SyntaxNode` → Task 4 (`MatchGenerationResult`, transform-local `MatchInfo`) + cacheability test.
- §7 sibling folder/namespace, injected `internal` surface, deferred markers NOT injected → Tasks 1–3 (only v1 markers embedded).
- §8/§11 diagnostics SY1201/SY1202/SY1203/SY1204 + SY0000 → Tasks 14–18.
- §12 snapshots + round-trips + cacheability + per-arm diagnostic tests → throughout.
- §13 frozen output shape / per-option input contract → snapshot tasks pin it.

**Deferred-by-spec (correctly absent):** `Deep`/`Either`/`Many<T>` markers (injected only with their lowering), `foreach` lowering, multi-variable-length lowering, semantic constraints, token/identifier capture, negation, `AtLeast`/`Between`. Each reachable deferred path degrades to SY1203/SY1204 (Tasks 15, 16), never a literal mis-match or an unimplemented-arm throw.

**Placeholder scan:** No `TODO`/`TBD`. Task 7 is a deliberate **snapshot lock** (the narrowing *behavior* is red→green in Task 6) and Task 18 is a **regression-lock + whole-feature gate** (registration done in Tasks 14–17, plus a forced end-to-end SY0000) — neither is a green-on-arrival behavioral test masquerading as red→green. Emitter tasks 10–13 share one `EmitAnchoredRun` core (signature pinned at Task 11, `MatchContext` pinned at Task 5), refactored in place (not duplicated); the Tasks 13/14 reshapes explicitly re-review and re-accept the earlier goldens.

**Type consistency:** `MatchGenerationResult` / `MatchTrackingNames.{Transform,Result}` / `MatchInfo` / `MatchMarkers` / `MatchEmitter.{Emit,EmitNodeMatch,Compose,EmitAnchoredRun}` / `MatchDiagnostics.{AnchorNotAllowed,PatternUnsatisfiable,ForeachRepetitionNotSupported,QuantifierNeedsBacktracking}` names are used consistently across tasks. Capture local `cap_{name}` and member `{Name}` conventions are uniform. Generated usings (`Microsoft.CodeAnalysis`, `.CSharp`, `.CSharp.Syntax`, plus `System.Linq` from Task 11) are consistent.

**Marker identity (resolved, not a deferred risk):** `MatchMarkers.Create` resolves every marker via `GetTypeByMetadataName(typeof(global::Synto.Matching.*).FullName!)` and compares with `SymbolEqualityComparer.Default` (the unbound `CaptureAttribute<>` via `ConstructUnboundGenericType()`), exactly as `SyntaxParameterFinder.cs:79`/`InlinedParameterFinder.cs` do — so there is **no `ToDisplayString()` `"…CaptureAttribute<>"` display-string fragility** to validate at implementation time (the round-1 medium); the comparison is format-independent. The generic-attribute *target* read (`AttributeClass.TypeArguments[0]`, Task 4) is the spike-verified path (Global Constraints) and is independent.

## Execution Handoff

**Branch isolation governs execution — see "Execution Model & Branch Isolation" at the top.** All work targets **`experimental/matching`** (base and push target), **never `main`**; the standard `ready→implement` per-green ff-push-to-main is **disabled** (this project does not practice full CI yet, and the new shipped-package surface must not reach `main` until the feature is deliberately merged).

Implement via **`superpowers:subagent-driven-development` in a throwaway worktree branched from `experimental/matching`** (a fresh subagent per task + two-stage review). **Do NOT use `implement-plan` (incl. `{mode:"dry-run"}`)** — it hardcodes its worktree base to `origin/main` (`.claude/workflows/implement-plan.js`), which lacks this feature's stack, so the Matching code won't compile there; `dry-run` relaxes only the push, not the base.

Do **not** route this plan through the main-pushing flow. **Final integration** is a single deliberate `git push origin HEAD:experimental/matching` after the whole plan is green (not per-commit); merging to `main` is a later human decision outside this plan. Snapshot-accept steps require human review of each new/changed `*.received.cs` before it becomes a golden — do not blind-accept (this includes the intentional re-accepts in Tasks 13/14).

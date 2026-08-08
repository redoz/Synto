# Matching DSL (v1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **Tracking issue:** #92
> **Spec:** docs/superpowers/specs/2026-06-20-matching-dsl.md

**Goal:** Add `Synto.Matching` — the inverse of Templating — as a sibling feature inside the one umbrella generator: a `[Match<M>]` pattern method is compiled into a bespoke straight-line Roslyn matcher (`M.Pattern(SyntaxNode) -> PatternMatch?`) that structurally recognizes a tree and returns typed captures.

**Architecture:** A new `src/Synto.SourceGenerator/Matching/` folder holds a second `IIncrementalGenerator` (`MatchFactorySourceGenerator`) that mirrors `TemplateFactorySourceGenerator`: all semantic work runs inside a `ForAttributeWithMetadataName` (FAWMN) transform keyed on the generic attribute `Synto.Matching.MatchAttribute`1`, flowing out an equatable `MatchGenerationResult` (generated text + `EquatableArray<DiagnosticInfo>`). The consumer marker surface is authored once in `src/Synto/Matching/`, embedded as `Synto.Runtime.*` resources, and injected `internal` by the existing `SurfaceInjectionGenerator` (no new generator, no new package). The emitter (`MatchEmitter`) lowers a pattern via a **generic structural walk** over `ChildNodesAndTokens()` (no per-`SyntaxKind` switch), stopping at holes and capturing them — producing readable, self-contained Roslyn that depends only on `Microsoft.CodeAnalysis`.

**Tech Stack:** C# / .NET 10 SDK, `netstandard2.0`, Roslyn (`Microsoft.CodeAnalysis.CSharp`) 5.0 floor, generic attributes (C# 11) on the consumer side, Verify snapshot tests + `CSharpGeneratorDriver`, xUnit v3 / MTP v2.

---

## How to read this plan (re-leveled — contracts over code)

This plan deliberately pins **the durable design** — the consumer surface, the generated-output shape, the pipeline/cacheability contract, the diagnostics, and the v1 scope — and then describes each task by the **behavior it delivers and the test that proves it**, *not* by a line-by-line script of method bodies and exact `ParseStatement("…")` strings.

- **What is fixed and reviewable up front** lives in **§ Contracts** (the one-way doors consumers and snapshots bind to) and **§ Emitter design** (the algorithm as approach + invariants + helper *signatures and responsibilities*). Review should bless these; if they are right, the implementation can be tweaked freely.
- **What the implementer writes** is the emitter code and the exact test code, via strict red→green TDD. Each task states the contract/behavior, the test *intent* with its **key (executable) assertions**, and the acceptance criteria — concrete enough to execute, not so prescriptive that a typo in the plan becomes a defect. Per-task spec-compliance + code-quality reviews and the green-gate catch line-level bugs.
- **Sequencing rule (load-bearing):** every task's test asserts **only** behavior that *that task* implements. A red→green step must be greenable within its own task — never assert emission/caching/diagnostics that a later task first produces.

## Global Constraints

- **`netstandard2.0`** for the generator and the runtime markers; **Roslyn 5.0 floor**; **no file/network/process/environment work at generation time** — inputs come only from the compilation.
- **Pipeline values are equatable value types.** Never capture `Compilation`, `ISymbol`, `SemanticModel`, or `SyntaxNode` into pipeline state. Do all semantic work inside the FAWMN transform and flow out only an equatable `MatchGenerationResult` (§ C4).
- **Failures become a `DiagnosticInfo` (SY-series), never a thrown exception.** Wrap generation in a `try/catch` that converts any escaping exception to `Diagnostics.InternalError` (**SY0000**).
- **Matching is a sibling folder** under the one umbrella generator (`src/Synto.SourceGenerator/Matching/`); **consumer markers** in namespace `Synto.Matching` (in `Synto.Core`), **generator-internal** types in `namespace Synto` (mirroring `TemplateInfo`; see File structure). **No new package.** Consumer markers are injected `internal`; any emitted helper is `file`-scoped (the `IsExternalInit` polyfill is the one exception — a single per-assembly `internal` type, § C3). Generated output is **self-contained** (references only Roslyn — no Synto runtime dependency).
- **Generic-attribute discovery (spike-verified on the Roslyn 5.0 floor — evidence below):** key FAWMN on the arity-suffixed metadata name `` Synto.Matching.MatchAttribute`1 `` and read the matcher target type off `ctx.Attributes[0].AttributeClass!.TypeArguments[0]`. Do **not** add a `[Match(typeof(M))]` fallback — it is unnecessary (see evidence).

  > **Spike evidence (the spec's one must-verify-before-freeze item, §10) — already done, recorded here.** A throwaway spike against **`Microsoft.CodeAnalysis.CSharp` 5.0.0** confirmed end-to-end:
  > - `ForAttributeWithMetadataName("Synto.Matching.MatchAttribute`1", …)` **fires** on a `[Match<M>]`-annotated method. The **backtick-`1` arity suffix is REQUIRED** — the bare name matches **nothing** (a generic attribute's metadata name carries its arity), so the constant must keep the `` `1 `` suffix.
  > - `ctx.Attributes[0].AttributeClass.TypeArguments[0]` resolves to a **real `NamedType`** (for `[Match<int>]`, `int` with `IsErrorType == false`) — so the matcher target is read directly off the attribute; no `typeof` round-trip.
  > - **No generator diagnostics** were produced.
  >
  > Conclusion: the generic `[Match<M>]` form works on the 5.0 floor; the `[Match(typeof(M))]` fallback is intentionally omitted. The *ongoing executable* guard is Task 4's positive validation test plus every round-trip — all fail red if generic-attribute discovery ever stops firing on the host floor. The throwaway probe is not retained; if a reviewer wants a re-runnable artifact, re-create the one-file probe and attach its output before Task 1 (cheap to reproduce).
- **`IsKind` on the 5.0 floor (spike-confirmed):** `CSharpExtensions.IsKind` is defined for **both** `SyntaxNode?` and `SyntaxNodeOrToken` on the 5.0.0 reference assembly, so both node-position and token-branch kind guards compile in generated output without an `.AsToken()` workaround.
- **Output shape is pinned by Verify snapshots.** Every new emission path gets a snapshot test; an unexplained snapshot change is a finding, not a rubber stamp.
- **Diagnostics ID ranges:** Matching's pattern-specific diagnostics live in the reserved **`SY12xx`** range (SY1201–SY1205; see § C5). Do **not** reuse `SY1008`/`SY1009` (taken by the `[Runtime]` converter checks). Each descriptor is registered in `AnalyzerReleases.Unshipped.md` **in the same task that adds it**, so each intermediate green-gate stays RS2008-clean.

---

## Execution Model & Branch Isolation (read before running any task)

**This feature does NOT land on `main`.** Issue #92 mandates the entire Matching feature land on the long-lived **`experimental/matching`** branch (the branch this plan, spec, and the unpushed stack already live on). The shipped `Synto` generator package's injected marker surface (the 7 new consumer-facing types — `MatchAttribute<TMatcher>`, `MatchOption`, `CaptureAttribute`, `CaptureAttribute<TNode>`, `Stmt`, `Statement`/`Expr`/`Block`) and the new `SY1201`–`SY1205` diagnostics are a **one-way door** the moment any consumer compiles against a published package (`consequences.md`, `project-phase.md` one-way-doors). They MUST NOT reach `main` until the feature is *deliberately* merged as a whole — an experimental branch exists precisely to hold that surface off the published contract while it is still free to change.

**Branch contract (binds every task):**
- **Integration target = the `experimental/matching` bookmark, never `main`.** Every task commit lands on `experimental/matching` (locally, via jj); nothing is pushed by default. The marker surface and the SY12xx diagnostics stay off `main` — and off any published package — until a deliberate, whole-feature merge, which is a separate human decision outside this plan.
- **No remote CI fires, and nothing publishes.** `.github/workflows/build.yml` triggers only on `main` (push/PR) and publishes NuGet packages; it never runs on `experimental/matching`. Integrating here therefore cannot trip the one-way door. The gate each task must pass is the **local** green-gate (`dotnet build` + `dotnet test` + `dotnet format --verify-no-changes`).
- **How to execute — the `implement-plan` workflow (jj-native), in LOCAL mode.** Run `implement-plan {plan:"docs/superpowers/plans/2026-06-20-matching-dsl.md"}` with `SYNTO_FLOW_INTEGRATE` unset or `local`. It creates an isolated jj **workspace** rooted at the integration bookmark B (resolved by `.claude/scripts/base-branch.sh`), implements each `### Task` via strict red-green TDD with a per-task spec-compliance review and a code-quality review (bounded fix loops), and after each green task rebases the stack onto the latest tip of B, re-runs the full green-gate, and advances bookmark B forward-only. In LOCAL mode it pushes **nothing**; the operator working copy follows B via jj auto-rebase.
  - **Pin B explicitly** so bookmark resolution is unambiguous: `export SYNTO_FLOW_BASE=experimental/matching` before the run. (`base-branch.sh` otherwise auto-detects the nearest ancestor bookmark of `@`.)
  - **`{mode:"dry-run"}`** implements + gates every task in the workspace but does **not** advance B or push — use it to validate the whole plan end-to-end before committing to the per-task bookmark advance. (Note: a dry-run still implements *all* tasks; it is a full local build, not a quick smoke test.)
  - The earlier git-worktree version of this workflow hardcoded `origin/main` as its base and could not compile this feature's stack; that version is gone — the current workflow is jj-workspace-native and roots at bookmark B (commit `4769d80`).
- **Final integration / merge `experimental/matching → main`** is a separate, later, human decision **outside this plan**. If you later choose to push the branch, set `SYNTO_FLOW_INTEGRATE=push`.

---

## File structure

**New runtime markers (authored once, `public`, in `src/Synto/Matching/`; embedded as `Synto.Runtime.*` resources):**
`MatchOption.cs`, `MatchAttribute.cs`, `CaptureAttribute.cs` (both `CaptureAttribute` + `CaptureAttribute<TNode>`), `Markers.cs` (`Stmt`/`Statement`/`Expr`/`Block`).

**New generator (`src/Synto.SourceGenerator/Matching/`, namespace `Synto` — generator-internal, mirroring Templating's `Synto.TemplateInfo`):**
`MatchTrackingNames.cs`, `MatchGenerationResult.cs`, `MatchInfo.cs` (transform-local), `MatchFactorySourceGenerator.cs`, `MatchMarkers.cs`, `MatchEmitter.cs`, `MatchDiagnostics.cs`.

**Modified:** `Synto.SourceGenerator.csproj` (add `Synto.Runtime.*` `<EmbeddedResource>` items); `Diagnostics.cs` (add `LocationInfo`-based overloads of the four target-validation factories — same descriptors, no new IDs); `AnalyzerReleases.Unshipped.md` (register each SY12xx in the task that adds it).

**New tests (`test/Synto.Test/Match/`):** `MatchTestHarness.cs`, `MatchSnapshotTests.cs`, `MatchRoundTripTests.cs`, `MatchDiagnosticsTests.cs`, `MatchSurfaceTests.cs`; plus a `MatchGenerationResult` case added to the existing `PipelineEquatabilityTests.cs`.

> Generator-internal Matching types live in **`namespace Synto`** — mirroring Templating, whose generator-internal pipeline types (`TemplateInfo`, etc.) are in `namespace Synto` while only its *consumer surface* is feature-namespaced (`Synto.Templating`). Reserving **`Synto.Matching`** for the injected consumer markers (in `Synto.Core`) keeps the generator-internal / consumer-surface divide crisp (plan-review round-4 maintainability). No collision: the generator-internal `Synto.*` types live in the *generator* assembly; the `Synto.Matching.*` markers live in the referenced `Synto.Core` assembly, and the names differ regardless (`Synto.MatchEmitter` vs `Synto.Matching.MatchAttribute`). `Diagnostics.cs` stays in `namespace Synto` (shared with Templating).

---

## Contracts (the pinned one-way doors)

These are frozen by snapshots and bound by consumer code. Review them as the durable design; the implementation that satisfies them may be tweaked, but **these shapes may not drift** without a deliberate contract change.

### C1. Consumer marker surface

Authored `public` in `src/Synto/Matching/`, injected `internal` by `SurfaceInjectionGenerator`. Exact names, namespace, accessibility, and member shapes are the contract (what consumers write); the phantom bodies (`=> null!` etc.) are trivial and the implementer's to fill.

```csharp
namespace Synto.Matching;

// plain, NON-[Flags] enum — None/Bare/Single are mutually-exclusive cardinalities, not bit flags.
public enum MatchOption { None = 0, Bare = 1, Single = 2 }

// generic attribute; metadata name "Synto.Matching.MatchAttribute`1".
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class MatchAttribute<TMatcher> : Attribute
{
    public MatchAttribute(MatchOption option = MatchOption.None);  // Option get-only property
    public MatchOption Option { get; }
}

[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class CaptureAttribute : Attribute { }                 // "Synto.Matching.CaptureAttribute"

[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class CaptureAttribute<TNode> : Attribute { }          // "Synto.Matching.CaptureAttribute`1"

// statement-capture quantifier holder (instance verbs; return types document the captured shape).
public sealed class Stmt
{
    public StatementSyntax One();             public StatementSyntax? Opt();
    public SyntaxList<StatementSyntax> Some(); public SyntaxList<StatementSyntax> All();
    public SyntaxList<StatementSyntax> Exactly(int n);
}

public static class Statement   // statement WILDCARD: same verbs, static, void
{ public static void One(); public static void Opt(); public static void Some(); public static void All(); public static void Exactly(int n); }

public static class Expr  { public static T Any<T>(); }   // expression wildcard — the SOLE wildcard spelling (no bare-default)
public static class Block { public static void Start();  public static void End(); }   // Bare/Single anchors
```

- `CaptureAttribute.cs` declares **both** the non-generic and generic attribute (one file → both rewritten `internal`; injected hint `CaptureAttribute.g.cs`).
- `Markers.cs` references `Microsoft.CodeAnalysis` / `…CSharp.Syntax` types, so it (and the injected copy) carries those usings — consumers already reference Roslyn.
- Deferred markers (`Deep`, `Either`, `Many<T>`) are **not** injected in v1 — a consumer cannot name them, so they need no diagnostic.

### C2. Generated-output shape

What consumer code binds to; snapshot-pinned.

- **File name:** `{Target.ToDisplayString(NameAndContainingTypesAndNamespaces)}.{PatternName}.g.cs` (mirrors Templating).
- **Matcher method:** `public static {PatternName}Match? {PatternName}(SyntaxNode node)`, emitted into `partial class {TargetName}` wrapped in the target's full ancestry (`WithAncestryFrom`), under `#nullable enable`.
- **Result record — `public sealed record {PatternName}Match(<members>)`, NESTED inside the partial target type.**
  > **Naming/nesting decision (resolves plan-review round-3 high #1 — namespace collision).** The record is **nested in the partial target** (`partial class M { public sealed record {PatternName}Match(...); public static {PatternName}Match? {PatternName}(SyntaxNode node) {...} }`), **not** a top-level sibling in the target namespace. Nesting scopes the record to its target, so two distinct targets in one namespace sharing a pattern name (`class Parser` + `class Rewriter`, each `[Match<…>] Call(...)`) emit `Parser.CallMatch` and `Rewriter.CallMatch` — **no CS0101**. This is a deliberate, minimal deviation from the spec §3.11 illustration (which draws `GuardedMatch` as a top-level record): it is transparent to the spec's `M.Guarded(node) is { } m` consumer usage and to positional deconstruction (`var (cond, guard, rest) = m;`); only an explicit type reference changes from `{Pattern}Match` to `{Target}.{Pattern}Match`. The qualify-at-namespace alternative (`{TargetName}{PatternName}Match`) was rejected — it uglifies the name and still pollutes the namespace. Pattern-name uniqueness **per target** remains a real constraint: two same-named patterns on the **same** target collide (host `CS0111` on the matcher method) — an expected v1 author error, like two same-named partial methods.
- **Record members** = the `[Capture]` parameters **in signature order** (carry each capture's `Ordinal = param.Ordinal` and sort by it — record member order is signature order regardless of walk order). Member naming: PascalCase of the parameter name; capture local naming: `cap_{paramName}`. Positional record (free `Deconstruct`). Member types by placement:
  | placement | member type |
  |---|---|
  | plain `[Capture]` expression hole | `ExpressionSyntax` |
  | `[Capture<TNode>]` narrowed | the fully-qualified `TNode` |
  | `[Capture] Stmt` `.One()` | `StatementSyntax` |
  | `[Capture] Stmt` `.Opt()` | `StatementSyntax?` |
  | `[Capture] Stmt` `.Some()` / `.All()` / `.Exactly(n)` | `SyntaxList<StatementSyntax>` (**not** `IReadOnlyList<T>` — anchored across the quantifiers and the deferred `foreach`, spec §10) |
- **Usings:** `Microsoft.CodeAnalysis`, `…CSharp`, `…CSharp.Syntax`; **`System.Linq` only when the body uses `Skip`/`Take`** (set a `NeedsLinq` flag in the slice-emitting helpers — do **not** add it unconditionally, which would churn the capture-less expression-Single goldens; plan-review round-3 medium). Output normalized with `NormalizeWhitespace()` then `SyntaxFormatter.Format(...)`.
- **Namespace & polyfill (resolves the round-4 C2/C3 conflict):** the matcher file keeps `WithAncestryFrom`'s **file-scoped** namespace as-is — **no block-namespace conversion**. The `init`-modreq `IsExternalInit` polyfill is **not** in the matcher file; it is a single per-assembly post-init output (§ C3, `Synto.Matching.IsExternalInit.g.cs`). A matcher snapshot therefore shows a plain file-scoped namespace with **no** embedded polyfill block. (Global-namespace targets emit no namespace wrapper, exactly as Templating's `WithAncestryFrom` already does.)

### C3. Self-containment on `netstandard2.0`

A Synto consumer is a Roslyn generator author, compiling against **`netstandard2.0`**. The positional result record lowers its members to `{ get; init; }`, and `init` requires `System.Runtime.CompilerServices.IsExternalInit`, **absent on `netstandard2.0`** → a real consumer build fails **CS0518** (the `init` modreq is unresolved) without a polyfill — contradicting "self-contained, no runtime dependency."

> **Mechanism re-derived from a spike (plan-review round-4 critical — the round-3 per-file `file`-local polyfill was *unbuildable*; now verified).** This is the second load-bearing compiler assumption, spiked exactly as generic-attribute discovery was before freezing. On a real `netstandard2.0` build:
> - `file static class IsExternalInit` → **CS0518**, *stays red, never green*. A `file` type carries a **mangled** metadata name, so the compiler's well-known-type lookup for the `init` modreq (which resolves the **canonical** `System.Runtime.CompilerServices.IsExternalInit`) never finds it. This differs from `ToSyntax`/`ToTypeSyntax`, which `file`-scope resolves by member-access *name* (not by canonical metadata lookup) — the round-3 design wrongly conflated the two paths under "the FileLocalHelpers ethos."
> - Two matcher files each emitting an `internal static class IsExternalInit` → **CS0101** (duplicate definition in one assembly).
> - **One** `internal static class IsExternalInit` (canonical, non-mangled name) → **build succeeds**; the `init` modreq resolves.
>
> Conclusion: the polyfill must be emitted **once per consumer assembly**, by canonical (non-`file`) name.

- **Mechanism (pinned):** emit the polyfill **exactly once per consumer assembly** as a single, non-`file` **`internal static class IsExternalInit`** in `namespace System.Runtime.CompilerServices`, via **`RegisterPostInitializationOutput`** under a **fixed hint name** (`Synto.Matching.IsExternalInit.g.cs`). Post-init output is a deterministic constant emitted once regardless of how many `[Match]` methods exist — the canonical metadata name satisfies the `init` modreq, and the single copy means **no CS0101** across matcher files. Registered by **exactly one** generator (`MatchFactorySourceGenerator`) so two post-init generators can't re-introduce the duplicate; `SurfaceInjectionGenerator` does **not** also emit it. **NOT `file`-scoped.**
- **No block-namespace.** Because the polyfill is no longer per-file, each matcher keeps its `WithAncestryFrom` **file-scoped** namespace unchanged — the round-3 **block-namespace conversion is deleted** (it existed only to host the per-file polyfill; this dissolves the round-4 C2/C3 conflict).
- **BCL-present (net5.0+) consumer:** a consumer whose target framework's corlib already defines `IsExternalInit` gets our injected source copy *plus* the BCL copy → at most **CS0436** (a *warning*; the source copy wins, the `init` modreq still resolves). Acceptable — "self-contained, compiles everywhere" holds for both the ns2.0 (polyfill load-bearing) and net5.0+ (polyfill redundant-but-harmless) consumer.
- **Record vs. hand-written class — decision: keep the positional record + single polyfill.** The alternative — emit the result as a `sealed class` with ctor + get-only `{ get; }` props (set in the ctor, so **no `init`, no polyfill at all**) + a hand-written `Deconstruct(out …)` — was weighed and **rejected**: it eliminates the polyfill but (a) loses the record's free structural `Equals`/`GetHashCode`/`ToString`/`Deconstruct` and deviates from spec §3.11's drawn `record` shape, (b) requires emitting and snapshot-pinning a hand-written `Deconstruct` per arity, and (c) the single-`internal` polyfill is now spike-proven to build and is one deduped constant file — the cost it avoids is small. The record preserves the spec's positional-deconstruction consumer usage (`var (cond, guard, rest) = m;`) directly.
- **Unconditional emission accepted.** Post-init can't see compilation content, so the polyfill is emitted even when no capturing matcher (or a zero-capture record with no `init` members) exists — an unused `internal` class, harmless. The `CompilationProvider`-gated alternative (emit only when `GetTypeByMetadataName("…IsExternalInit") is null`, which would also skip the BCL/consumer-own-copy cases) flows an equatable `bool` and is cache-safe, but is a **deferred refinement**; v1 takes the simpler deterministic constant path. (The one residual CS0101 case — a consumer that hand-ships its *own* `IsExternalInit` in the same assembly — is rare and self-resolves by deleting the redundant copy; the gated variant would remove it.)
- **Proof (required test; the SOLE executable guard of this one-way door — closure pinned, RED witnessed):** authored at **Task 6** (the first **capturing** matcher — a zero-capture record has no `init` member and needs no polyfill, so the proof can only go red once captures exist; sequencing the proof at Task 5 would be ineffective). A `netstandard2.0`-only harness helper compiles a *generated capturing* matcher against a pinned reference closure and asserts **zero error diagnostics** — **RED (CS0518)** with the polyfill suppressed, **GREEN** with it.
  - **Closure (pinned, load-bearing):** the **`NETStandard.Library.Ref`** ref-pack / netstandard2.0 *reference* assemblies **+ Roslyn only**. Explicitly **NOT** `typeof(object).Assembly.Location` (the running net10 corlib) and **NOT** `Assembly.Load("netstandard")` (the runtime `netstandard.dll` facade, which forwards to a corlib that *defines* `IsExternalInit`) — either would make the proof pass green even with a broken/absent polyfill.
  - **Closure self-check `[Fact]`:** assert `compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.IsExternalInit") is null` over that closure, so a future ref-set change can't silently restore the type and pass the proof with a broken polyfill.
  - **BCL-present coexistence `[Fact]`:** compile the same capturing matcher against a net5.0+/net10 closure (corlib *defines* `IsExternalInit`) and assert **zero error diagnostics** (at most CS0436 warning) — the other direction of "compiles everywhere."
  - **Acceptance criterion:** **RED must be witnessed** (suppress the polyfill emission, observe CS0518) before wiring the polyfill green — a green-on-arrival proof is rejected.

### C4. Equatable pipeline & cacheability invariant

- **Discovery:** `MatchFactorySourceGenerator` keys `ForAttributeWithMetadataName` on `` Synto.Matching.MatchAttribute`1 ``; the transform reads the matcher target off `ctx.Attributes[0].AttributeClass.TypeArguments[0]` and the option off the ctor arg.
- **Pipeline value (equatable, flows out):** `internal record struct MatchGenerationResult(string? FileName, string? Source, EquatableArray<DiagnosticInfo> Diagnostics)`. A `MatchGenerationResult` structural-equality case is added to `PipelineEquatabilityTests`.
- **Transform-local model (never flows):** `MatchInfo` holds the `SemanticModel`, the attribute syntax, the target `INamedTypeSymbol`, the pattern `IMethodSymbol`, the pattern syntax, the name, and the `MatchOption`, via a static `MatchInfo? Create(GeneratorAttributeSyntaxContext)`. It lives only inside the transform.
- **HARD invariant:** all semantic work happens inside the FAWMN transform; **never** capture `Compilation`/`ISymbol`/`SemanticModel`/`SyntaxNode` into pipeline state. (Cacheability is correctness *and* performance — full severity regardless of phase.)
- **Tracking names:** `MatchTrackingNames.Transform` / `.Result`.
- **Required cacheability behaviors:** (a) an **unrelated** edit leaves every Match pipeline step `Cached`/`Unchanged`; (b) with two patterns on one target, editing **one** re-runs that pattern's `Transform`/`Result` (`Modified`) while the other stays `Cached`/`Unchanged`. (b) belongs at the first body-dependent-emission task — assert it as ">=1 `Modified` for the edited pattern AND >=1 `Cached`/`Unchanged`" (host-Roslyn-robust), never on a stub emitter.

### C5. Diagnostics

- **Target validation reuses Templating's four existing descriptors** via new `LocationInfo`-based overloads added to `Diagnostics.cs` (Matching has only the attribute `Location`, not a `TargetType`). The four-arm order — each reachable misuse to its precise descriptor, never a malformed `partial` cascade — is:
  1. **not declared in source** (a metadata/referenced-assembly type) → **SY1003** `TargetNotDeclaredInSource`.
  2. **not a (non-record) class** (struct / interface / `record class`) → **SY1002** `TargetNotClass`. (A `record class` parses as `RecordDeclarationSyntax`; mirror Templating's `ClassDeclarationSyntax`-only contract.)
  3. **target not `partial`** → **SY1001** `TargetNotPartial`.
  4. **any ancestor class not `partial`** → **SY1004** `TargetAncestorNotPartial` (report each).
  > Edge accepted + noted (plan-review low): a class nested **directly in a non-class** (`struct Outer { partial class M {} }`) passes arm 4 (it walks only `ClassDeclarationSyntax` ancestors) and then `WithAncestryFrom`'s cast throws → surfaced as **SY0000**, not a precise SY1xxx. Inherited from Templating's identical `WithAncestryFrom`; out of scope for v1 (exotic target); still a diagnostic, never a crash.
- **Matching's `SY12xx` family** (category `Synto.Matching`, `Error`, registered in the task that adds each — IDs, severity, message *intent*, and the triggering input/location are the contract; the message wording is the implementer's):
  | ID | name | triggers (located on) |
  |---|---|---|
  | **SY1201** | `AnchorNotAllowed` | `Block.Start()`/`Block.End()` in a `None` pattern (braces already bound it) — the anchor call. |
  | **SY1202** | `PatternUnsatisfiable` | **provable** contradiction only: a core statement **before** `Block.Start()` or **after** `Block.End()` — the offending statement/anchor. Conservative: silent on merely-suspicious patterns. |
  | **SY1203** | `ForeachRepetitionNotSupported` | a phantom `foreach` over a `[Capture]` param (deferred repetition) — the `foreach`. |
  | **SY1204** | `QuantifierPlacementUnsupported` | the **quantifier-placement** family (message `{0}`-reason-parameterized): a run (post-anchor) with **>1 variable-length** element (`Some`/`All`/`Opt`) — *located on the second variable-length hole*; **or** a **variable-length** quantifier in an **embedded single-statement slot** (`if (cond) rest.All();`, which can't expand to one slot) — *located on the embedded hole*. Both are placements the straight-line matcher can't satisfy. |
  | **SY1205** | `MalformedPatternBody` | the **option×body-shape** family (message `{0}`-reason-parameterized): `Single` on a multi-statement (post-anchor) core; **or** `None`/`Bare` on an expression body — *located on the attribute*. (The embedded variable-length case moved to SY1204's quantifier family — round-4 medium.) |
  > **SY1205 is a deliberate addition** beyond the spec's enumerated SY1201–SY1204 (justified by correctness.md's "every reachable misuse maps to a located diagnostic, never a silent default arm" — without it an option×body-shape misuse emits nothing and the consumer sees only an "M.P does not exist" error at their call site). **Round-4 split (maintainability — the three-misuse grab-bag):** resolved by (i) `{0}`-reason-parameterizing **both** SY1204 and SY1205 so each arm carries a precise message, and (ii) **reclassifying the embedded variable-length case into SY1204's quantifier-placement family** (it is a cardinality/placement error, not an option×body-shape error). Net IDs remain **five — SY1201–SY1205** — with SY1204 carrying two quantifier-placement arms and SY1205 the two option×body-shape arms.
- **SY0000** `Diagnostics.InternalError` is the catch-all: any exception escaping generation is converted, never thrown.
- **No partial matcher beside a diagnostic.** Every SY12xx diagnostic test asserts **both** the diagnostic *and* `Assert.Empty(result.GeneratedTrees)` — a diagnostic-only emit (the abort/`body.Count == 0` bail, § Emitter design), so a forgotten abort-check can never ship a malformed tree.

### C6. v1 scope

**Supported (v1):**
- `MatchOption.None` / `Bare` / `Single` with the spec §4 semantics (None decl-rooted & fully bounded; Bare contains-leftmost in a block; statement-Single block-rooted leftmost; expression-Single rooted on the handed expression node).
- Expression captures (`[Capture] T x` → `ExpressionSyntax`; `[Capture<TNode>]` narrowing) and the expression wildcard `Expr.Any<T>()`.
- Statement captures via `One`/`Opt`/`Some`/`All`/`Exactly`, and their static wildcard forms, **with at most one variable-length element (`Some`/`All`/`Opt`) per run** (single greedy split — straight-line).
- **Non-linear** equality (reused `[Capture]` → one member + one `IsEquivalentTo` per reuse site).
- **Anchors** `Block.Start()`/`Block.End()`, **including in combination with a single variable-length element** (the decision below).
- Diagnostics SY1201–SY1205 + SY0000.

> **Anchored + single-variable-length-element decision: SUPPORTED in v1 (resolves plan-review round-3 high #2).** Per spec §8, `None`/`Bare`/`Single` are all v1 and "at most one variable-length quantifier per run" is v1; their **intersection** — e.g. a `None` pattern `{ first.One(); rest.All(); }`, or a `Bare` pattern with `Block.End()` + a trailing variable element — is a natural v1 use (matching "one statement then a run", fully bounded), not an exotic edge. Deferring it would leave a surprising hole in the flagship statement-run feature. **Consequence for the emitter (§ run-alignment core):** the anchored drivers must bound on **`before + after`** (the fixed widths flanking the one variable element), *never* a single fixed total `width` that miscounts the variable element as `1`; the variable element absorbs `Count − offset − before − after`. The fully-anchored (`None`) case requires `Count >= before + after` (not `Count == width`). Round-trip + snapshot coverage for `None`+variable and `anchorEnd`+variable is required (Tasks 12/13).

**Deferred (designed-for, not built) — each reachable path degrades to a clean located diagnostic, never a literal mis-match or a throw:**
- phantom `foreach` over a `[Capture]` (repetition) → **SY1203**.
- a run with **>1** variable-length element, a variable element abutting content it could also consume, **or a variable-length quantifier in an embedded single-statement slot** → **SY1204**.
- `Deep`/`Either`/`Many<T>`, semantic constraints, token/identifier capture, negation, `AtLeast`/`Between` — markers **not injected**, so unnameable; no diagnostic needed.

---

## Emitter design (behavior + invariants + helper responsibilities — not prescribed bodies)

The emitter is the hard part; this section fixes its **approach and invariants** and the **signatures/responsibilities** of its helpers. The implementer writes the bodies and the exact emitted strings via TDD; the round-trips (behavior) and snapshots (shape) are the executable spec.

### The generic structural walk

> **Two trees are structurally equal** (trivia-insensitive, like `IsEquivalentTo(other, topLevel:false)`) **iff** they have the same `RawKind`, the same `ChildNodesAndTokens()` count, equal token texts (kind + text), and structurally-equal child nodes. At a **hole** position the emitter **captures** instead of comparing.

The emitter unrolls this over the *known* pattern tree at generation time, emitting `if (<negation>) return null;` guards against the runtime candidate, recursing child-by-child via the `ChildSyntaxList` indexer (`SyntaxNodeOrToken`) — **no per-`SyntaxKind` switch**.

- **Helper:** `EmitNodeMatch(List<StatementSyntax> body, string accessor, SyntaxNode pattern, MatchContext ctx)` — emit the guards for one node position and recurse. It first checks whether `pattern` is a hole (expression capture, expression wildcard, embedded statement hole) and dispatches; otherwise it emits the literal-node guards and walks children.
- **Kind guard is load-bearing (plan-review round-1 medium):** at each node position guard **both** the .NET type (binds a temp for child navigation) **and** the `RawKind` (`IsKind(SyntaxKind.{pattern.Kind()})`). The .NET type alone over-accepts kinds sharing a type — `ArrayInitializerExpression` vs `CollectionInitializerExpression` (both `InitializerExpressionSyntax`), the several `BinaryExpressionSyntax` kinds, etc. Token children compare **kind + text** (escape-safe via `SymbolDisplay.FormatLiteral`).
- **Child-count guard is load-bearing (plan-review round-4 medium — superset false-positive):** at each literal node position **also** emit `{accessor}.ChildNodesAndTokens().Count == {pattern.ChildNodesAndTokens().Count}` — the "same child count" clause of the structural-equality definition, which also bounds the child indices. Without it, a pattern with an **optional** child omitted (an `if` with **no** `else` → 5 children) false-positive-matches a **superset** candidate (an `if` **with** an `else` → 6 children), because the walk only compares the pattern's children and silently ignores the extra. The if-with/without-`else` near-miss round-trip (Task 10) pins this.

### Accessor type contract (the walk's one invariant — plan-review round-1 high)

Every `accessor` string threaded through the walk has a static type of **exactly one of two**: `SyntaxNode` (the `node` parameter and every node-child boundary, projected via `…ChildNodesAndTokens()[i].AsNode()`) or `StatementSyntax` (a `_blk.Statements[i]` indexer). Never a raw `SyntaxNodeOrToken`; never double-projected.

- **Node-child recursion passes the `.AsNode()` accessor unchanged** — a hole branch reached through it must **not** append a second `.AsNode()` (the round-1 `…AsNode().AsNode()` CS1061).
- **Holes type-narrow *at* the hole**, whatever the accessor's static type: emit `if ({accessor} is not {MemberType} {local}) return null;` — this both rejects a non-matching node **and** binds a `{MemberType}`-typed local, so a captured statement binds to its record member with **no CS0266** even when `{accessor}` is statically `SyntaxNode`. Never bind with `var {local} = {accessor};` into a typed member.

### Per-`MatchOption` strategy

- **expression-`Single`** (arrow body): root on the handed node — `EmitNodeMatch(body, "node", expr, ctx)`; return the record. Input contract: hand it the expression node (consumer's `DescendantNodes()` loop). Not block-scoped; anchors don't apply.
- **statement-`Single`** (block body, one *core* statement): root on the candidate `BlockSyntax`, scan its **direct** statements, commit to the **leftmost** match. Lowered as a **one-element run** through the shared run-alignment core.
- **`Bare`** (block body, a run): root on the candidate `BlockSyntax`; align the pattern run **contained** in the block at the **leftmost** offset; ≤1 variable-length element (single greedy split). Anchors pin the run to the block's first/last edge.
- **`None`** (block body, fully bounded): root on the candidate **declaration** (`MethodDeclarationSyntax`/`LocalFunctionStatementSyntax`), derive its body block, and align the run **fully bounded** (both anchors implicit — exact coverage, but a single variable element absorbs the slack).
- **expression captures:** a bare identifier binding to a `[Capture]` param → capture (`ExpressionSyntax`, or the narrowed `TNode`). A **reused** capture → one member (first site) + an `IsEquivalentTo` guard on each later site, with a **unique temp per reuse site** (a fixed name re-declares on 3+ reuse → CS0128).
- **expression wildcard:** `Expr.Any<T>()` → assert `is ExpressionSyntax`, capture nothing.
- **statement holes:** an `ExpressionStatementSyntax` wrapping a quantifier invocation on a `[Capture] Stmt` param (capture) or the static `Statement` holder (wildcard). The verb (`One`/`Opt`/`Some`/`All`/`Exactly(n)`) selects the member type and the run cardinality.
- **embedded statement hole:** a `[Capture] Stmt .One()` or `Statement.One()` sitting as a single embedded statement (e.g. the body of `if (cond) guard.One();`) captures / matches that one statement. A **variable-length** quantifier embedded in a single-statement slot is meaningless → **SY1204** (the quantifier-placement family — round-4 reclassification).
- **anchors:** an `ExpressionStatementSyntax` wrapping `Block.Start()`/`Block.End()`.

> **Hole detection is by *binding*, never by type/shape (spec §3.1 — the leak-free invariant).** A node is a hole only when its symbol binds to a `[Capture]` parameter or a `Synto.Matching` marker. Literal `foreach`/`default`/`StatementSyntax` locals in matched code never collide. The discard glue `_ = <expr>;` binds to **no** marker, so it is matched **literally** (intended v1 semantics) — it merely hosts an expression hole in statement position.

### Marker resolution

**`MatchMarkers.Create(MatchInfo)`** resolves every marker `INamedTypeSymbol` **once** via `Compilation.GetTypeByMetadataName(typeof(global::Synto.Matching.X).FullName!)` and classifies holes by **`SymbolEqualityComparer.Default`** — never `ToDisplayString()` string-matching (plan-review round-1 medium). The unbound `CaptureAttribute<>` is resolved via `…GetTypeByMetadataName(typeof(CaptureAttribute<>).FullName!).ConstructUnboundGenericType()` and compared against an attribute's `AttributeClass.ConstructUnboundGenericType()` — exactly as `SyntaxParameterFinder.cs:79` / `InlinedParameterFinder.cs` already do. It exposes the capture parameters (with their `[Capture<T>]` narrow types) and predicates: `TryGetCapture`, `IsExpressionWildcard`, `TryGetStatementHole`, `TryGetAnchor`. It is cheap (built once per pattern); node walks reuse `ctx.Markers` rather than rebuilding it per node.

### `MatchContext` (mutable per-pattern emit state — final shape, pinned at introduction)

```
MatchContext(MatchInfo info, MatchMarkers markers)   // Info / Markers get-only
  List<Capture> Captures               // each carries Ordinal = param.Ordinal (record member order = signature order)
  HashSet<string> BoundCaptureLocals   // first-vs-reuse-site distinction
  List<DiagnosticInfo> Diagnostics     // emitter-raised SY1202/SY1204/SY1205
  bool Aborted                         // a branch set this + returned -> Emit emits diagnostics-only
  string NextTmp()                     // unique temp names
```

- **No `required` members** anywhere in the emitter (`MatchContext`, the `RunElement` hierarchy): `netstandard2.0` lacks `RequiredMemberAttribute`/`CompilerFeatureRequiredAttribute` (CS9035/CS0656). Use ctors + get-only props, mirroring `MatchInfo`/`TemplateInfo`.
- **The abort/merge is wired where `MatchContext` is introduced**, not deferred: `MatchEmitter.Emit` always does `diagnostics.AddRange(ctx.Diagnostics); if (ctx.Aborted || body.Count == 0) return null;` (a diagnostics-only emit, like Templating's converter-error bail). Later tasks only **set** `ctx.Diagnostics`/`ctx.Aborted`.

### The shared run-alignment core

**`EmitAnchoredRun(List<StatementSyntax> body, IReadOnlyList<StatementSyntax> coreStatements, List<RunElement> run, MatchContext ctx, bool anchorStart, bool anchorEnd)`** — the single alignment core that `Bare`, statement-`Single`, and `None` all flow through, so behavior is added once and never re-plumbed.

- **Signature is FINAL at its introduction (Task 9).** Later tasks fill in *behavior* (the variable-length split, the anchored drivers, the SY1204 check) using the parameters already present — they add **no** field or parameter. `coreStatements` is the raw post-anchor statement list (kept for diagnostic `Location`s, e.g. SY1204).
- **Precondition:** the **caller** establishes the candidate block local `_blk` (a `BlockSyntax`) and its null guard before calling — so `None` can root `_blk` on a declaration body while `Bare`/`Single` root it on `node`. `EmitAnchoredRun` never re-derives `_blk`.
- **`run`** is an ordered list of `RunElement` — a `LiteralElement` (a literal statement to match structurally) or a `HoleElement` (a classified statement hole). `RunElement`/`HoleElement` carry their data via ctors (no `required`); **`HoleElement` carries its source statement `Location`** so the SY1204 check (§ C5) reports on the offending hole without re-querying `TryGetStatementHole` (plan-review round-4 perf-low).
- **Structure:** emit a per-offset attempt as a local function `_TryAt(int _o)` (matches the run's elements at that offset, captures, returns the record or null), then a **driver** selected by the anchor flags.
- **The bound invariant (load-bearing — resolves round-3 high #2):** a run has **at most one** variable-length element (else SY1204). Compute `before` = fixed widths before it, `after` = fixed widths after it; the variable element absorbs `Count − _o − before − after`. **Drivers bound on `before + after`, never a single fixed total `width`** (a fixed-only run is the special case where there is no variable element and `before + after` is the exact width):
  - **no anchors** (Bare contains): `for` scan over offsets `0 .. Count − (before+after)`, commit to the leftmost match.
  - **anchorStart only:** pin `_o = 0`, attempt once.
  - **anchorEnd only:** pin `_o` so the run ends at the last statement, attempt once.
  - **anchorStart && anchorEnd** (None / fully bounded): `int _o = 0;` (no scan), require `Count >= before + after`; the variable element covers the remainder.
  - The fully-anchored / no-scan branch **always declares `int _o = 0;`** so the attempt's `_blk.Statements[_o + …]` accessors bind even with no loop (plan-review round-2 medium).
- **Fixed elements *after* the variable element** index at the runtime offset `_o + before + _var + <afterSlot>` (relative to the computed `_var`, not a generation-time constant). This tail path is exercised only when something fixed follows the variable element (a literal statement / `One()` / `Exactly(n)` after an `All()`), so it needs explicit round-trip + snapshot coverage (Task 11).

> **Generated body style (plan-review low, decided).** The matcher indexes `Statements[..]` / `ChildNodesAndTokens()[i]` directly and uses `Skip`/`Take` for slices — Shinn-style "reads like hand-written" code (spec §5), linear and bounded by the candidate's size. Only the **record shape** and the **matcher method signature** are the snapshot-pinned one-way door; the matcher **body** (indexing/scan style) is **non-binding and snapshot-reversible**, so any later micro-optimization stays open without a contract break.

### `ExtractAnchors` ordering (plan-review round-2 medium)

Anchor extraction runs **before** any count-based shape check, so statement-`Single`/`None` count only the **core** (post-anchor) statements. So `{ return result; Block.End(); }` (2 raw statements) extracts to `core = [return result;]` + `anchorEnd:true` and dispatches into the **anchored statement-`Single`** path, never a silent `default` arm. Apply this ordering wherever a count guard precedes anchor handling.

---

## Tasks

> Each task: **Delivers** (the contract/behavior), **Files**, **Behavior & contract** (what to build), **Test** (intent + the key/executable assertions), **Red → Green** (why the test is red until this task's code lands), and lean `- [ ]` steps. Author round-trip patterns as **per-`[Fact]` local functions** (Templating's `RoundTripTests` model — keeps each pattern's name/scope local to its `[Fact]`; the shared `private static partial class M { }` stays at class scope). This aids *readability*, **not** compile isolation — `Synto.Test` compiles all-or-nothing, so a non-compiling emitted matcher fails the whole build (true per-matcher isolation would need a separate target class per pattern, which v1 does not do). Diagnostic/snapshot fixtures go through `MatchTestHarness.Run`, which **asserts the consumer source compiles before generation** (mirrors `SimpleTemplateTest.VerifyTemplate`).

> **Two cross-task harness conventions, pinned once here (plan-review round-4):**
> - **Capture-text normalization.** Every `m.A == "foo()"`-style round-trip assertion routes through a single `MatchTestHarness` helper — `AssertCapture(expected, captured)` ≡ `Assert.Equal(expected, captured.NormalizeWhitespace().ToString().Trim())`, CRLF/LF normalized — mirroring Templating's `AssertGenerated`, because a captured node carries the parsed input's trivia and a raw `.ToString()` `==` is trivia-fragile. Used from Task 5 on; do **not** hand-roll a raw `==` on node text.
> - **RED often manifests as a build failure, not a failing assertion.** For round-trip tasks the RED state is frequently a `Synto.Test` **compile error** (CS0128 re-declared local, CS0246/CS1061 on a mis-typed/mis-ordered member) from the emitted matcher — the subagent driver must read a build failure at the listed RED point as the **intended RED**, not a harness defect.

### Task 1: Marker surface — `MatchAttribute<TMatcher>` + `MatchOption`

**Delivers:** the `MatchAttribute<TMatcher>` / `MatchOption` consumer surface (C1), injected `internal`.
**Files:** create `src/Synto/Matching/MatchOption.cs`, `MatchAttribute.cs`; modify `Synto.SourceGenerator.csproj` (two `<EmbeddedResource>` items in the existing `Synto.Runtime.*` group); test `test/Synto.Test/Match/MatchSurfaceTests.cs`.

**Behavior & contract:** author the two types exactly per C1. `SurfaceInjectionGenerator` auto-injects every `Synto.Runtime.*` resource, rewriting `public`→`internal`.

**Test (intent + key assertions):** `MatchSurfaceTests` compiles a consumer snippet that uses `[Match<M>(MatchOption.Bare)]` against the **public `Synto.Core`** markers (references `SyntoCoreAssembly`, runs **no** generator) and asserts **no error diagnostics** — it validates the public marker *shape* binds. (Comment it as such: the injected-**internal** gate is the existing `SurfaceInjectionTest` golden in Step 5 plus the round-trips, **not** this test — it references the public Core copy.)

**Red → Green:** RED — `MatchOption`/`Match<>` don't exist (CS0246). GREEN once the two markers are authored + embedded.

- [ ] Write `MatchSurfaceTests.InjectedMatchAttributeAndOptionBind` (public-shape bind) → run → RED (CS0246).
- [ ] Author `MatchOption.cs` + `MatchAttribute.cs` per C1; add the two `<EmbeddedResource>` items → run → GREEN.
- [ ] Run `SurfaceInjectionTest` (review the two new `*.received.cs` — confirm `internal enum MatchOption`, `internal sealed class MatchAttribute<TMatcher>`; accept as goldens) **and `InjectedSurfaceCompletenessTest`** (the injected generic-attribute markers must compile in the Roslyn-only closure — this also covers Tasks 2–3; an unexpected red there is intended signal, not noise).
- [ ] Commit (local on `experimental/matching`): `feat(matching): inject Match<TMatcher> attribute + MatchOption marker surface`.

### Task 2: Marker surface — `CaptureAttribute` + `CaptureAttribute<TNode>`

**Delivers:** the two `[Capture]` hole markers (C1).
**Files:** create `src/Synto/Matching/CaptureAttribute.cs` (both types in one file); modify the csproj; add a `[Fact]` to `MatchSurfaceTests`.

**Behavior & contract:** per C1. `PublicToInternalRewriter` rewrites both top-level types; injected hint `CaptureAttribute.g.cs`.

**Test:** a consumer snippet using `[Capture] object x, [Capture<BinaryExpressionSyntax>] object y` binds with no errors (public-shape bind, references Core + Roslyn).

**Red → Green:** RED — `Capture`/`Capture<>` don't exist. GREEN once authored + embedded.

- [ ] Write `InjectedCaptureAttributesBind` → RED.
- [ ] Author `CaptureAttribute.cs`; add the `<EmbeddedResource>` → GREEN.
- [ ] Run `SurfaceInjectionTest`; review/accept `…#CaptureAttribute.g.verified.cs` (both `internal sealed class CaptureAttribute` and `…<TNode>`).
- [ ] Commit: `feat(matching): inject Capture / Capture<TNode> hole markers`.

### Task 3: Marker surface — `Stmt`, `Statement`, `Expr`, `Block`

**Delivers:** the quantifier/wildcard/anchor markers (C1).
**Files:** create `src/Synto/Matching/Markers.cs`; modify the csproj; add a `[Fact]` to `MatchSurfaceTests`.

**Behavior & contract:** per C1; `Markers.cs` carries `using Microsoft.CodeAnalysis;` / `…CSharp.Syntax;` (the return types are Roslyn types).

**Test:** a consumer snippet exercising every marker form in body position — `if (cond) guard.One();`, `Statement.One();`, `rest.All();`, `_ = Expr.Any<bool>();`, `Block.End();` — binds with no errors.

**Red → Green:** RED — markers don't exist. GREEN once authored + embedded.

- [ ] Write `InjectedQuantifierAndWildcardMarkersBind` → RED.
- [ ] Author `Markers.cs`; add the `<EmbeddedResource>` → GREEN.
- [ ] Run `SurfaceInjectionTest`; review/accept `…#Markers.g.verified.cs` (all four marker types `internal`).
- [ ] Commit: `feat(matching): inject Stmt/Statement/Expr/Block quantifier + anchor markers`.

### Task 4: Pipeline scaffold + four-arm target validation (SY1001–SY1004)

**Delivers:** the equatable pipeline (C4) end-to-end with a **stub** emitter, the four-arm target validation (C5), and the unrelated-edit cacheability behavior.
**Files:** create `MatchTrackingNames.cs`, `MatchGenerationResult.cs`, `MatchInfo.cs`, `MatchFactorySourceGenerator.cs`, and a stub `MatchEmitter.Emit` (returns null); modify `Diagnostics.cs` (the four `LocationInfo` overloads); create `MatchTestHarness.cs`, `MatchDiagnosticsTests.cs`; add a `MatchGenerationResult` case to `PipelineEquatabilityTests.cs`.

**Behavior & contract:**
- `MatchInfo.Create` reads the target off `AttributeClass.TypeArguments[0]` and the option off the ctor arg (C4).
- `GenerateMatcher` runs `ValidateTarget` then the (stub) emitter inside a `try/catch`→SY0000, flowing out a `MatchGenerationResult`. `WithTrackingName` on the Transform and the post-`Where(not null)` Result.
- `ValidateTarget` is the four-arm check (C5), reusing the new `LocationInfo` overloads.
- `MatchTestHarness` mirrors `SimpleTemplateTest`: in-memory compilation against the public Core markers + Roslyn, runs only `MatchFactorySourceGenerator`, and **asserts the consumer source compiles before generation**. (Negative target-validation fixtures deliberately feed source that compiles as plain C# — the misuse is semantic, caught by `ValidateTarget`.)

**Test (intent + key assertions):**
- `TargetNotDeclaredInSource_ReportsSY1003` (arm 1 — the **gating** arm, previously untested): a **metadata** `TMatcher` — `[Match<int>](Single)` (`int` lives in corlib, not source) → exactly one `SY1003` with a real (non-empty) `Location` (the attribute's). Guards the freshly-authored `LocationInfo` overload + the four-arm order, so a metadata target can't mis-route to a later arm or to `WithAncestryFrom`'s throwing cast → SY0000 (plan-review round-4 medium).
- `TargetNotPartial_ReportsSY1001`: a non-`partial` class target → exactly one `SY1001` with a real (non-empty) `Location`.
- `TargetNotClass_ReportsSY1002` (`[Theory]` over `struct` / `record struct` / `interface`): exactly one `SY1002`, and **no** `SY1003` (the target IS in source).
- `TargetAncestorNotPartial_ReportsSY1004`: a matcher nested under a non-`partial` outer class → exactly one `SY1004`.
- `WellFormedMatch_ReadsTypeArg_PassesValidation`: a well-formed `[Match<M>(Single)] static object One() => 1;` produces **no diagnostics** (type-arg read + four-arm validation passed). **Do NOT assert on `GeneratedTrees`** — the stub emits nothing; the emission proof is Task 5 (plan-review round-2/round-3 high: asserting emptiness here flips RED at Task 5).
- `Generator_IsIncremental_OnUnrelatedEdit`: with tracking enabled, adding an unrelated syntax tree leaves every `Transform`/`Result` output `Cached`/`Unchanged` (the `Result` step caches a null-`Source` `MatchGenerationResult`).
- `PipelineEquatabilityTests`: equal-content `MatchGenerationResult`s are equal with equal hashes; a differing `Source` is unequal.

**Red → Green:** RED — `MatchFactorySourceGenerator`/`MatchTrackingNames` don't exist (compile error). GREEN once the scaffold + validation + stub emitter land. (The cross-pattern editing-one cacheability test is **deferred to Task 6**, the first body-dependent emission — it cannot go green on a stub that emits no body-dependent output.)

- [ ] Write the four-arm validation (incl. the **SY1003** metadata-target arm) + positive + unrelated-edit cacheability + equatability tests → RED.
- [ ] Author the tracking names, result struct, `MatchInfo`, the generator, the stub `MatchEmitter.Emit`, and the four `Diagnostics` `LocationInfo` overloads → GREEN.
- [ ] Commit: `feat(matching): pipeline scaffold + four-arm matcher-target validation (SY1001-SY1004)`.

### Task 5: Emitter core — expression-`Single` literal + generic structural walk + `Compose`

**Delivers:** the generic structural walk and the full output composition (C2/C3) for a zero-capture expression-`Single`.
**Files:** create `MatchMarkers.cs` (capture-parameter resolution + `IsExpressionBodied`); replace the stub `MatchEmitter.Emit`; register the once-per-assembly `IsExternalInit` post-init output (C3) in `MatchFactorySourceGenerator`; create `MatchRoundTripTests.cs`, `MatchSnapshotTests.cs` + the capture-normalization helper in `MatchTestHarness` (§ Tasks intro).

**Behavior & contract:**
- For `MatchOption.Single` + an **expression body** with zero captures, emit the matcher rooted on `node` via `EmitNodeMatch` and the (empty-member) record, composed per C2 (nested record, **file-scoped** namespace via `WithAncestryFrom`, three usings, `#nullable enable`, formatted). The `IsExternalInit` polyfill is **not** in this file — register it once per assembly as a post-init output (C3, fixed hint `Synto.Matching.IsExternalInit.g.cs`).
- Introduce `EmitNodeMatch` (the generic walk: kind+type guard, **child-count guard**, child-by-child, token kind+text) and `MatchContext` (final shape, § Emitter design) with the abort/merge wired in `Emit`.
- `Compose` nests the record in the partial target under the file-scoped ancestry namespace (**no block conversion**); the `IsExternalInit` polyfill is a separate per-assembly post-init output, **not** part of `Compose` (C2/C3).

**Test (intent + key assertions):**
- Round-trip `LiteralOne_MatchesOne_RejectsTwo`: `[Match<M>(Single)] static object LiteralOne() => 1;` → `M.LiteralOne(ParseExpression("1"))` non-null; `"2"` and `"1 + 1"` null.
- `WellFormedMatch_EmitsNamedMatcher` (the emission assertion moved off Task 4): the well-formed `One() => 1` now yields exactly one generated tree containing `partial class M` and `One(SyntaxNode node)`, with no diagnostics.
- Snapshot `ExpressionSingle_Literal`: golden pins `#nullable enable`, the three usings, the **nested** `public sealed record LiteralOneMatch();`, the matcher with structural guards for literal `1`, and the **file-scoped** namespace — with **no** embedded `IsExternalInit` block (the polyfill is a separate per-assembly post-init file; pin it as its own one-line golden `Matcher_IsExternalInit_Polyfill` if a snapshot of the post-init output is wanted).

> **netstandard2.0 self-containment proof is deferred to Task 6** (the first **capturing** matcher) — a zero-capture record here has no `init` member, so the polyfill is dead weight and the proof can't reach RED at Task 5 (plan-review round-4 high). Task 5 only **emits** the polyfill (the post-init registration above); Task 6 proves it load-bearing.

**Red → Green:** RED — `M.LiteralOne` doesn't exist (stub emits nothing). GREEN once the walk + `Compose` land.

- [ ] Write the round-trip + emission tests → RED (the netstandard2.0 self-containment proof lands at Task 6).
- [ ] Author `MatchMarkers` (capture resolution), `EmitNodeMatch`, `MatchContext`, `Compose` (nested record under the file-scoped ancestry namespace), and register the once-per-assembly `IsExternalInit` post-init output, replacing the stub → GREEN.
- [ ] Add + review + accept `ExpressionSingle_Literal` snapshot (file-scoped namespace, no embedded polyfill).
- [ ] Commit: `feat(matching): generic structural walk + expression-Single literal matcher emission`.

### Task 6: Emitter — expression captures + `[Capture<TNode>]` narrowing + cross-pattern cacheability

**Delivers:** plain-`[Capture]` (→ `ExpressionSyntax`) and `[Capture<TNode>]` (→ narrowed type + narrowed guard) capture extraction; the cross-pattern cacheability behavior.
**Files:** modify `MatchEmitter.cs` (`EmitCapture` + the hole check atop `EmitNodeMatch`); modify `MatchMarkers.cs` if needed for narrow types; extend `MatchTestHarness` with the pinned **netstandard2.0 reference closure** + the BCL-present closure + the `GetTypeByMetadataName` closure self-check (C3); tests in `MatchRoundTripTests`/`MatchSnapshotTests`/`MatchDiagnosticsTests`.

**Behavior & contract:**
- A bare identifier binding to a `[Capture]` param emits `if ({accessor} is not {MemberType} {local}) return null;`, records a `Capture` with `Ordinal = param.Ordinal`, and adds the local to `BoundCaptureLocals`. Member type is `ExpressionSyntax` (plain) or the fully-qualified narrow `TNode` (`[Capture<TNode>]`).
- **Deliberately NO reuse branch yet** — a second occurrence re-declares the local (CS0128). That is the RED state Task 8 fixes; adding a working reuse branch now would make Task 8 green-on-arrival (plan-review round-1 testability medium).
- Record-member order is signature order via `Ordinal` (sort in `Compose`/`OrderCaptures`, O(n log n), not a per-key rescan).

**Test (intent + key assertions):**
- `Sum_CapturesBothOperands`: `Sum([Capture] int a, [Capture] int b) => a + b` over `"foo() + 42"` → `m.A == "foo()"`, `m.B == "42"`; `"foo() - 42"` (wrong op) and `"foo()"` (not binary) null.
- `Narrowed_OnlyMatchesInvocation_AndTypesTheMember` (narrowing as a real red→green here, **not** a separate green-on-arrival task): `[Capture<InvocationExpressionSyntax>] object call` → `m.Call` **must compile as** `InvocationExpressionSyntax`; matches `"foo(1)"`, rejects `"1 + 1"`.
- `Generator_IsIncremental_AcrossPatterns_OnEditingOne` (moved here, C4): two `[Match]` patterns on one target; editing **one** body → ">=1 `Modified` for the edited pattern AND >=1 `Cached`/`Unchanged`" on `Transform`/`Result`.
- `GeneratedMatcher_CompilesOn_NetStandard20` (the C3 self-containment proof — first lands here, the first **capturing** matcher; a zero-capture record needs no `init`/polyfill so it could not go red at Task 5): the generated `Sum` matcher compiles against the **pinned** netstandard2.0 reference closure (`NETStandard.Library.Ref` ref set + Roslyn **only** — explicitly NOT `typeof(object).Assembly.Location`, NOT `Assembly.Load("netstandard")`) with **zero error diagnostics**. **Acceptance: RED must be witnessed** — suppress the Task-5 post-init polyfill, observe **CS0518** (the `init` modreq unresolved; *not* CS0656), restore it → GREEN.
- `NetStandard20Closure_LacksIsExternalInit` (closure self-check): `compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.IsExternalInit") is null` over that closure — so a future ref-set change can't silently restore the type and pass the proof with a broken polyfill.
- `GeneratedMatcher_CompilesOn_NetWithBcl` (BCL-present coexistence, C3): the same capturing matcher compiles against a net5.0+/net10 closure whose corlib **already defines** `IsExternalInit` → **zero error diagnostics** (the injected `internal` copy yields at most **CS0436**, a *warning*; source wins, the modreq still resolves) — so "self-contained, compiles everywhere" holds on both sides.
- Snapshots `ExpressionSingle_Captures` (record `SumMatch(ExpressionSyntax A, ExpressionSyntax B)`) and `ExpressionSingle_Narrowed` (member typed the fully-qualified narrow; narrowed `is`-guard).

> **netstandard2.0 reference closure (pinned, CPM-correct):** the C3 proof needs a netstandard2.0 *reference* set that **lacks** `IsExternalInit`. Use **`NETStandard.Library.Ref`** (the ref-pack) added under **central package management** — a `PackageVersion` in `Directory.Packages.props` plus a version-less `PackageReference` in `test/Synto.Test/Synto.Test.csproj` (a bare versioned `PackageReference` fails NU1008). Confirm a netstandard2.0 ref set isn't already on hand first. Do **not** use the running net10 corlib (`typeof(object).Assembly.Location`) or the runtime `netstandard.dll` facade (`Assembly.Load("netstandard")`) — both forward to a corlib that *defines* `IsExternalInit` and would make the proof pass green with a broken polyfill.

**Red → Green:** RED — `M.Sum`/`M.Narrowed` exist but don't capture/narrow (CS-error on `m.A`/`m.Call` typed, or over-constrained match); the C3 proof is red (CS0518) until the polyfill is wired. GREEN once `EmitCapture` reads `ctx.Markers.Narrows` and the polyfill emits.

- [ ] Write the capture + narrowing + cross-pattern cacheability tests, and the C3 proof trio (`GeneratedMatcher_CompilesOn_NetStandard20`, `NetStandard20Closure_LacksIsExternalInit`, `GeneratedMatcher_CompilesOn_NetWithBcl`) → RED.
- [ ] Add `EmitCapture` + the capture-hole dispatch + the `Ordinal` sort; extend `MatchTestHarness` with the pinned ns2.0 + BCL closures → GREEN. **Witness the C3 RED** (suppress the polyfill → CS0518) before accepting the proof as green.
- [ ] Add + accept `ExpressionSingle_Captures` and `ExpressionSingle_Narrowed` snapshots.
- [ ] Commit: `feat(matching): expression captures + [Capture<TNode>] narrowing + netstandard2.0 self-containment proof`.

### Task 7: Emitter — expression wildcard `Expr.Any<T>()`

**Delivers:** the expression wildcard (match any expression, capture nothing).
**Files:** modify `MatchMarkers.cs` (`IsExpressionWildcard`) + `MatchEmitter.cs` (wildcard dispatch atop `EmitNodeMatch`); tests in `MatchRoundTripTests`/`MatchSnapshotTests`.

**Behavior & contract:** an `InvocationExpressionSyntax` binding to `Synto.Matching.Expr.Any<T>()` (by resolved symbol, not `ToDisplayString`) emits `if ({accessor} is not ExpressionSyntax) return null;` and returns — no capture.

**Test:** `EqualsAnything([Capture] object lhs) => lhs == Expr.Any<object>()` over `"x == foo(1, 2)"` → `m.Lhs == "x"`; `"x + y"` (not `==`) null. Snapshot `ExpressionSingle_Wildcard` (right operand asserted only `is ExpressionSyntax`, no capture member).

**Red → Green:** RED — the `Any<>()` invocation is walked literally and over-constrains the operand. GREEN once the wildcard dispatch lands.

- [ ] Write `Wildcard_MatchesAnyRightOperand_WithoutCapturing` → RED.
- [ ] Add `IsExpressionWildcard` + the dispatch → GREEN; add + accept snapshot.
- [ ] Commit: `feat(matching): expression wildcard Expr.Any<T>()`.

### Task 8: Emitter — non-linear equality (reused capture → `IsEquivalentTo`)

**Delivers:** a reused `[Capture]` adds **one** member (first site) + an `IsEquivalentTo` guard per later site, with a **unique temp per reuse site**.
**Files:** modify `MatchEmitter.cs` (the reuse branch atop `EmitCapture`); tests in `MatchRoundTripTests`/`MatchSnapshotTests`.

**Behavior & contract:** introduce the reuse branch (Task 6 deliberately omitted it). At a site whose local is already in `BoundCaptureLocals`, emit a uniquely-named temp (`ctx.NextTmp()`) and an `IsEquivalentTo({local})` guard — no new member. A fixed temp name would re-declare on 3+ reuse (CS0128).

**Test:**
- `SelfEq_RequiresBothSidesSyntacticallyEqual`: `SelfEq([Capture] object x) => x == x` matches `"a.b == a.b"`, rejects `"a.b == a.c"`.
- `SelfEq3_RequiresAllThreeSidesEqual` (3-site reuse — locks the unique-per-site temp): `=> x + x + x` matches `"a.b + a.b + a.b"`, rejects `"a.b + a.b + a.c"` (proves no CS0128 from two reuse sites in one scope).
- Snapshot `ExpressionSingle_NonLinear` (one `X` member; an `IsEquivalentTo(cap_x)` guard with a `NextTmp()` temp).

**Red → Green:** RED — without the reuse branch the 2nd `x` re-declares `cap_x` (CS0128) → the matcher doesn't compile → `M.SelfEq`/`M.SelfEq3` fail to bind. GREEN once the reuse branch lands.

- [ ] Write the 2-site + 3-site reuse round-trips → RED (CS0128).
- [ ] Add the reuse branch (unique temp per site) → GREEN; add + accept snapshot.
- [ ] Commit: `feat(matching): non-linear equality via single IsEquivalentTo`.

### Task 9: Emitter — statement-`Single` + introduce the shared run-alignment core

**Delivers:** statement-`Single` (block-rooted, leftmost direct statement), lowered through the **shared run-alignment core `EmitAnchoredRun`** introduced here in its **final signature**.
**Files:** modify `MatchEmitter.cs` (dispatch on a one-statement block body; introduce `EmitAnchoredRun` + the `RunElement` hierarchy + `EmitStatementCapture`); tests in `MatchRoundTripTests`/`MatchSnapshotTests`.

**Behavior & contract:**
- For `MatchOption.Single` + a block body with exactly one statement, root on the candidate `BlockSyntax`, build a **one-element run**, and align it via `EmitAnchoredRun(..., anchorStart:false, anchorEnd:false)` — the no-anchor scan driver (leftmost over offsets). The caller establishes `_blk` + its null guard (the core's precondition).
- Introduce `EmitAnchoredRun` with its **final 6-arg signature** (§ run-alignment core). Task 9 fills the **no-anchor** driver only; the anchored drivers and the variable-length split are added by Tasks 11–13 **without changing the signature**.
- `EmitStatementCapture` type-narrows at the hole (accessor-type contract) — correct at both its call sites.

**Test:** `StatementSingle_FindsLeftmostReturn_InABlock`: `ReturnCapture([Capture] object result) { return result; }` (`object` return per §3.9 — `void` + `return result;` is CS0127) over `"{ Foo(); return x + 1; return y; }"` → `m.Result == "x + 1"` (leftmost return); a no-return block → null. Snapshot `StatementSingle_Return` (block-root, `_TryAt(int _o)`, leftmost scan).

**Red → Green:** RED — a block-bodied `Single` hits the `default` arm → `M.ReturnCapture` not emitted. GREEN once the dispatch + the core's no-anchor driver land.

- [ ] Write the statement-`Single` round-trip → RED.
- [ ] Introduce `EmitAnchoredRun` (final signature, no-anchor driver), `RunElement`, `EmitStatementCapture`, and the one-element-run dispatch → GREEN.
- [ ] Add + accept `StatementSingle_Return` snapshot.
- [ ] Commit: `feat(matching): statement-Single (block-rooted, leftmost) on the shared run-alignment core`.

### Task 10: Emitter — `Bare` fixed-arity runs + embedded statement holes

**Delivers:** `Bare` alignment for **fixed-arity** elements (literal statements, `One()`, `Exactly(n)`), and the embedded-statement-hole path.
**Files:** modify `MatchMarkers.cs` (`TryGetStatementHole`) + `MatchEmitter.cs` (the `Bare` dispatch + multi-element fixed attempt body + the embedded-hole branch in `EmitNodeMatch`); tests in `MatchRoundTripTests`/`MatchSnapshotTests`.

**Behavior & contract:**
- `TryGetStatementHole` classifies an `ExpressionStatementSyntax` invocation as a `{ Kind: Capture|Wildcard, Quantifier: One|Opt|Some|All|Exactly, Count?, Capture? }` (by resolved symbol), exposing `IsVariableLength` for `Some`/`All`/`Opt`.
- The `Bare` dispatch builds the run from the block statements and aligns via `EmitAnchoredRun(..., false, false)`. For this task the attempt body handles **fixed-arity** elements only (literal → `EmitNodeMatch`; capture `One()` → `EmitStatementCapture`; wildcard `One()` → match, no capture; `Exactly(n)` capture → a `SyntaxList` slice). The driver bounds on the fixed width (the `before+after` formula with no variable element).
- **Embedded statement hole** (e.g. the body of `if (cond) guard.One();`): when `EmitNodeMatch` reaches a statement child that classifies as a hole, a `Capture`/`One` captures the one embedded statement (accessor passed **unchanged**, narrowed at the hole), a `Wildcard`/`One` matches any one statement (no capture); a **variable-length** embedded quantifier is left for **Task 15's SY1204** (until then it falls through to a dead literal guard — never a crash).

**Test:**
- `Bare_FixedArity_MatchesContainedIfWithOneStatement`: `OneGuard([Capture] bool cond, [Capture] Stmt only) { if (cond) only.One(); }` over `"{ Pre(); if (ready) Go(); Post(); }"` → `m.Cond == "ready"`, `m.Only == "Go();"`; a block with no `if` → null.
- `Bare_EmbeddedWildcardOne_MatchesIfWithAnyBody`: `IfAny([Capture] bool cond) { if (cond) Statement.One(); }` matches an `if` with any single-statement body, capturing only `cond`.
- `Bare_IfWithoutElse_RejectsIfWithElse` (the **child-count guard** near-miss — § Emitter design / plan-review round-4 medium): the same `OneGuard` pattern (`if (cond) only.One();`, an `if` with **no** `else`) over a candidate containing `if (ready) Go(); else Stop();` (an `if` **with** an `else`) → **null** — the candidate `if` has 6 children vs the pattern's 5, so the child-count guard rejects it. Without the guard the extra `else` is silently ignored and it false-positive-matches.
- Snapshot `Bare_OneGuard` (block-root, `_TryAt(int _o)`, leftmost scan over the fixed width).

**Red → Green:** RED — `Bare` hits the `default` arm → `M.OneGuard` not emitted. GREEN once the `Bare` dispatch + fixed-arity attempt body + embedded-hole branch land.

- [ ] Write the fixed-arity + embedded-wildcard-One round-trips → RED.
- [ ] Add `TryGetStatementHole`, the `Bare` dispatch, the fixed attempt body, and the embedded-hole branch → GREEN.
- [ ] Add + accept `Bare_OneGuard` snapshot.
- [ ] Commit: `feat(matching): Bare fixed-arity run alignment (literal/One/Exactly) + embedded statement holes`.

### Task 11: Emitter — `Bare` single variable-length element + statement wildcards + signature-order lock

**Delivers:** a run with **at most one** variable-length element (`Some`=1+, `All`=0+, `Opt`=0–1) via a single greedy split; statement wildcards; the fixed-tail-after-variable index path; and the signature-order (positional) record-member lock.
**Files:** modify `MatchEmitter.cs` (the variable split in the shared attempt body; `EmitVariableStatementListCapture`/`EmitOptionalStatementCapture`); tests in `MatchRoundTripTests`/`MatchSnapshotTests`.

**Behavior & contract:**
- Generalize the **shared** attempt body: compute `before`/`after` (fixed widths flanking the one variable element); `_var = Count − _o − before − after`; guard `_var >= min` (`Some`→1, `All`/`Opt`→0) and `_var <= 1` for `Opt`; capture/wildcard the middle slice; then match the fixed **after**-elements at `_o + before + _var + <afterSlot>` (the `_var`-relative tail). The driver bound swaps the fixed width for `before + after`.
- The variable-length capture helpers set `MemberType` (`SyntaxList<StatementSyntax>` for `Some`/`All`, `StatementSyntax?` for `Opt`) **and `Ordinal = param.Ordinal`** — load-bearing for signature-order members (plan-review round-2 high).

**Test (intent + key assertions):**
- `Bare_OneVariableLength_SplitsDeterministically`: `GuardThenRest([Capture] bool cond, [Capture] Stmt guard, [Capture] Stmt rest) { if (cond) guard.One(); rest.All(); }` over `"{ if (ok) Go(); A(); B(); C(); }"` → `m.Cond == "ok"`, `m.Guard == "Go();"`, `m.Rest.Count == 3`; a block with no rest → `m.Rest.Count == 0` (`All` = 0+).
- **Signature-order positional lock** (the test that *actually* fails red without `Ordinal` — round-3's `GuardThenRest` fixture was **illusory**: its walk order `(cond, guard, rest)` *equalled* its signature order, so the sort was a no-op and the test passed with **or** without the bug; plan-review round-4 medium, recurring). Use a fixture whose **walk order genuinely differs from signature order** with a **type-discriminating** member: signature **`P([Capture] Stmt rest, [Capture] bool cond) { if (cond) X(); rest.All(); }`** — the walk hits `cond` first (inside the `if`), then `rest` (the `All()` run), so **walk order `(cond, rest)` ≠ signature order `(rest, cond)`**. Assert via **deconstruction**: `var (a, b) = m; _ = a.Count;`. Correct (`Ordinal = param.Ordinal`): member 0 = `rest` (`SyntaxList<StatementSyntax>`, has `.Count`) → **compiles + passes**. Buggy (members in walk order): member 0 = `cond` (`ExpressionSyntax`, **no** `.Count`) → **CS1061**, build fails. **The implementer MUST verify it fails RED (CS1061) without the `Ordinal` sort** — a green-with-or-without-the-sort fixture is rejected. (`X();` binds to no marker, so it is matched literally.)
- `Bare_FixedElementAfterVariable_IndexesTail`: a **literal** statement after the variable element (`RunThenReturn([Capture] Stmt body) { body.All(); return; }`) over `"{ A(); B(); return; }"` → `m.Body.Count == 2`; `"{ A(); B(); C(); }"` (no trailing `return`) → null (proves the `_var`-relative tail is checked).
- Snapshots `Bare_GuardThenRest`, a wildcard variant (`Statement.All()`), `Bare_FixedAfterVariable`, and `Bare_ReversedSignatureOrder` (pins the signature-order record members `(SyntaxList<StatementSyntax> Rest, ExpressionSyntax Cond)` against the reversed walk order).

**Red → Green:** RED — the variable element has no fixed width; Task 10's width sum mis-counts it → no/ wrong match, and the positional lock fails to compile without `Ordinal`. GREEN once the split + `Ordinal`-setting helpers land.

- [ ] Write the variable-split + positional-order + fixed-tail round-trips → RED.
- [ ] Add the single greedy split to the shared attempt body + the two variable capture helpers (both setting `Ordinal`) → GREEN; re-run Task 10 fixed-arity round-trips.
- [ ] Add + accept the three snapshots.
- [ ] Commit: `feat(matching): Bare single variable-length quantifier (Some/All/Opt) + signature-order lock`.

### Task 12: Emitter — `None` (declaration-rooted, fully bounded; anchored + variable supported)

**Delivers:** `MatchOption.None` — root on a candidate declaration, derive its body block, align the run **fully bounded** (both anchors), correctly supporting a **single variable-length element** under full bounding (C6 decision).
**Files:** modify `MatchEmitter.cs` (the `None` dispatch + `EmitDeclarationFullBody` + the fully-anchored driver branch in `EmitAnchoredRun`); tests in `MatchRoundTripTests`/`MatchSnapshotTests`.

**Behavior & contract:**
- The `None` dispatch derives `_blk` from the candidate `MethodDeclarationSyntax`/`LocalFunctionStatementSyntax` body, then calls `EmitAnchoredRun(..., anchorStart:true, anchorEnd:true)` — a **third** 6-arg caller, no re-plumb.
- Fill the **fully-anchored driver branch**: `int _o = 0;` (no scan), require `Count >= before + after` (**not** `Count == width`), let the variable element absorb the remainder. This is the C6 anchored+variable fix — the branch must **not** gate on a single fixed `width` that miscounts a variable element as 1.

**Test (intent + key assertions):**
- `None_MatchesDeclarationWhoseBodyIsExactlyTheShape`: `SingleDiscard([Capture] object x) { _ = x; }` over `"void F() { _ = y.z; }"` → `m.X == "y.z"`; a body with an extra trailing statement → null (fully bounded).
- **`None` + variable element** (the C6 case the round-3 review flagged as wrong-output): a `None` pattern like `{ first.One(); rest.All(); }` matches a declaration whose body is "one statement then a run" — assert it matches a 1-statement body **and** a 3-statement body (the `Count == width` bug would match only the exact count). Add the matching round-trip + snapshot.
- Snapshots `None_SingleDiscard` and the `None`+variable case.

**Red → Green:** RED — `None` hits the `default` arm; and (for the variable case) a `Count == width` gate would reject valid trees. GREEN once `EmitDeclarationFullBody` + the corrected fully-anchored driver land.

- [ ] Write the `None` exact + `None`+variable round-trips → RED.
- [ ] Add the `None` dispatch, `EmitDeclarationFullBody`, and the fully-anchored driver (`before+after` lower-bound, no `Count==width`) → GREEN.
- [ ] **Mid-plan full-suite gate** (branch isolation — no CI-to-main): run the entire `Synto.Test.Match` suite (Task 12 is a high cross-task regression point — every prior emit path now flows through the shared core) → expect PASS.
- [ ] **Re-review + intentionally re-accept** the Task 9–11 goldens whose emitted shape changed by routing through the shared core (review each diff deliberately — the expected restructure, not a regression); confirm `SnapshotOrphanGuardTest` stays green.
- [ ] Add + accept the `None` snapshots; stage the re-accepted goldens.
- [ ] Commit: `feat(matching): None (declaration-rooted, fully bounded) + anchored variable-length element`.

### Task 13: Anchors `Block.Start()`/`Block.End()` + SY1201 + SY1205 + anchor-before-count dispatch

**Delivers:** explicit anchors (`Block.Start()`/`Block.End()`) wired into `Bare`/statement-`Single`; SY1201 (anchor in `None`); SY1205 (the **option×body-shape** misuse family, `{0}`-reason-parameterized); and the **anchor-extraction-before-count** dispatch.
**Files:** create `MatchDiagnostics.cs` (SY1201, SY1205); modify `MatchMarkers.cs` (`TryGetAnchor`) + `MatchEmitter.cs` (restructure the block-bodied dispatch around `ExtractAnchors`; thread `anchorStart`/`anchorEnd` into `Bare`/statement-`Single`; the SY1205 arms); register SY1201/SY1205; tests in `MatchRoundTripTests`/`MatchDiagnosticsTests`/`MatchSnapshotTests`.

**Behavior & contract:**
- `TryGetAnchor` recognizes `Block.Start()`/`Block.End()` (by resolved symbol).
- **Restructure the dispatch** (§ ExtractAnchors ordering): one block-bodied branch extracts anchors **first**, then dispatches on the **core** (post-anchor) count — `None` (anchors → SY1201 each, no output; else `EmitDeclarationFullBody`), `Single when core.Count == 1` (→ statement-`Single` with the anchor flags), `Bare` (→ `EmitBareRun` with the flags), `default` (multi-statement `Single` → SY1205). An expression body with a non-`Single` option → SY1205. `Bare`/statement-`Single` now pass the anchor flags into `EmitAnchoredRun`, which selects the anchorStart-only / anchorEnd-only / no-anchor driver.
- (The variable-length **embedded** quantifier case — `if (cond) rest.All();` — is **not** handled here; it is a quantifier-placement error reclassified to **SY1204** and lands at Task 15, extending Task 10's embedded-hole branch — plan-review round-4 medium.)
- The abort/`body.Count==0` bail (wired in Task 5's `Emit`) yields **diagnostics-only, no tree** for every SY1201/SY1205 path.
- Register `SY1201`/`SY1205` in `AnalyzerReleases.Unshipped.md` **now** (RS2008-clean green-gate).

**Test (intent + key assertions):**
- `Anchor_End_PinsToLastStatement`: `TrailingReturn([Capture] object result) { return result; Block.End(); }` over `"{ Foo(); return v; }"` → non-null; `"{ return v; Foo(); }"` (return not last) → null. (Suppress CS0162/CS0127 file-wide; the body is phantom.)
- `AnchorInNonePattern_ReportsSY1201_AndEmitsNoTree`: a `None` pattern with `Block.End()` → one `SY1201` with a real `Location` **and** `Assert.Empty(GeneratedTrees)`.
- `OptionBodyShapeMisuse_ReportsSY1205_AndEmitsNoTree` (`[Theory]`): `None`/`Bare` on an expression body; `Single` on a 2-statement core → one `SY1205` (the matching `{0}` reason) + empty trees.
- Snapshot `Single_TrailingReturnAnchored`.

**Red → Green:** RED — anchors are walked literally (`Block.End()` over-constrains; the anchored-`Single` body falls to `default` → no SY1201); the option×body-shape misuse cases fall through silently → no SY1205. GREEN once `TryGetAnchor`, the anchor-first dispatch, the anchor-flag threading, and the SY1205 arms land.

- [ ] Write the anchor round-trip + the SY1201/SY1205 diagnostic tests → RED.
- [ ] Add `MatchDiagnostics` (SY1201/SY1205), `TryGetAnchor`, `ExtractAnchors`, the restructured dispatch, the anchor-flag threading, the SY1205 arms; register both IDs → GREEN.
- [ ] **Second full-suite gate** (`Synto.Test.Match`) — Task 13 restructures the block-bodied dispatch (anchor-extraction-first) and threads anchor flags through the shared core (second-highest regression point) → expect PASS.
- [ ] **Confirm the statement-`Single` golden is UNCHANGED** — it already routes through `EmitAnchoredRun` (Task 9) with `anchorStart/End:false`, so threading the (false/false) anchor flags must not alter the no-anchor output; an unexpected diff signals a dispatch **regression** (do not blind-accept). Confirm `SnapshotOrphanGuardTest` green.
- [ ] Add + accept `Single_TrailingReturnAnchored`; stage re-accepted goldens.
- [ ] Commit: `feat(matching): Block.Start/End anchors + SY1201 + SY1205 + anchor-before-count dispatch (+register SY1201/SY1205)`.

### Task 14: SY1203 — phantom `foreach` over a `[Capture]` (deferred repetition)

**Delivers:** SY1203 for a phantom `foreach` iterating a `[Capture]` param (the deferred repetition path), emitting no tree.
**Files:** modify `MatchDiagnostics.cs` (SY1203) + `MatchEmitter.cs` (a pre-scan, reusing `ctx.Markers`); register SY1203; test in `MatchDiagnosticsTests`.

**Behavior & contract:** before dispatching on the option, pre-scan the pattern body for a `ForEachStatementSyntax` whose `Expression` binds to a `[Capture]` param (reuse the already-built `ctx.Markers`, not a per-`foreach` rebuild); if found, add SY1203 on the `foreach` and return null. Register SY1203 now.

**Test:** `ForeachOverCapture_ReportsSY1203`: the §3.7 `Concat` pattern (`foreach (var part in parts) sb.Append(part);` with `[Capture] List<object> parts`) → one `SY1203` with a real `Location`.

**Red → Green:** RED — the `foreach` is walked literally (or throws → SY0000); no SY1203. GREEN once the pre-scan lands.

- [ ] Write `ForeachOverCapture_ReportsSY1203` → RED.
- [ ] Add SY1203 + the pre-scan; register SY1203 → GREEN.
- [ ] Commit: `feat(matching): SY1203 reject phantom foreach over [Capture] (+register SY1203)`.

### Task 15: SY1204 — quantifier placement unsupported (>1 variable-length per run + embedded variable-length)

**Delivers:** SY1204 (the quantifier-placement family, `{0}`-reason-parameterized) — (i) a run (post-anchor) with **>1** variable-length element, and (ii) a **variable-length quantifier in an embedded single-statement slot** (reclassified from SY1205, round-4 medium) — each emitting no tree.
**Files:** modify `MatchDiagnostics.cs` (SY1204, `{0}`-parameterized) + `MatchEmitter.cs` (the `>1` count check in `EmitAnchoredRun` before splitting **and** the variable-length arm of `EmitNodeMatch`'s embedded-hole branch, extending Task 10); register SY1204; tests in `MatchDiagnosticsTests`.

**Behavior & contract:** two arms, both SY1204 (`{0}`-reason-parameterized). (i) In the shared alignment core, before splitting, count the run's variable-length elements; if `> 1`, add SY1204 on the **second** variable-length hole's `Location` (carried on `HoleElement`, § run-alignment core — **no** `TryGetStatementHole` re-query), set `ctx.Aborted`, and return. (ii) In `EmitNodeMatch`'s embedded-hole branch (Task 10), a **variable-length** embedded quantifier (`if (cond) rest.All();` — meaningless in a single-statement slot) adds SY1204 on the embedded hole + `ctx.Aborted`. The Task-5 merge/bail emits diagnostics-only for both. Register SY1204 now.

**Test:** `TwoVariableLengthQuantifiers_ReportsSY1204_AndEmitsNoTree`: `head.Some(); tail.All();` → one `SY1204` with a real `Location` **and** `Assert.Empty(GeneratedTrees)` (locks the abort path). `VariableLengthEmbeddedHole_ReportsSY1204` (reclassified from Task 13): `if (cond) rest.All();` → one `SY1204` on the embedded hole + `Assert.Empty(GeneratedTrees)`. Re-run a single-variable-length round-trip (still emits).

**Red → Green:** RED — the alignment picks the first variable element and mis-handles the second (wrong match or SY0000), and the embedded variable-length quantifier falls through to a dead literal guard; no SY1204. GREEN once the `>1` count check + the embedded-arm check land.

- [ ] Write `TwoVariableLengthQuantifiers_…` + `VariableLengthEmbeddedHole_ReportsSY1204` → RED.
- [ ] Add SY1204 (`{0}`-parameterized) + the `> 1` check in the core + the embedded-arm check in `EmitNodeMatch`; register SY1204 → GREEN.
- [ ] Commit: `feat(matching): SY1204 reject unsupported quantifier placement (>1 per run + embedded variable-length) (+register SY1204)`.

### Task 16: SY1202 — provable unsatisfiable (anchor-contradiction subset)

**Delivers:** SY1202 for the **provable** anchor contradiction only — a core statement before `Block.Start()` or after `Block.End()`.
**Files:** modify `MatchDiagnostics.cs` (SY1202) + `MatchEmitter.cs` (`ExtractAnchors` tracks positions); register SY1202; test in `MatchDiagnosticsTests`.

**Behavior & contract:** during anchor extraction, flag a core (non-anchor) statement seen **after** a `Block.End()` anchor, or a core statement seen **before** a `Block.Start()` anchor; on a contradiction, add SY1202 (on the offending statement/anchor) + `ctx.Aborted`. Strictly provable-only — silent on merely-suspicious patterns (conservatism is load-bearing — a false positive rejects a valid matcher). Register SY1202 now (all five SY12xx then registered in the tasks that add them).

**Test:** `ContentBeforeBlockStart_ReportsSY1202`: `body.Some(); Block.Start();` → one `SY1202` with a real `Location`. Re-run the Task-13 anchor round-trip (well-formed anchors still emit).

**Red → Green:** RED — the anchor only sets a flag, ignoring its position; no SY1202. GREEN once position tracking lands.

- [ ] Write `ContentBeforeBlockStart_ReportsSY1202` → RED.
- [ ] Add SY1202 + the position tracking in `ExtractAnchors`; register SY1202 → GREEN.
- [ ] Commit: `feat(matching): SY1202 provable anchor-contradiction (+register SY1202)`.

### Task 17: SY0000 regression-lock + SY12xx registration audit + final gate

**Delivers:** regression-locks for the SY0000 catch-all and the five-ID registration, plus the whole-feature gate. **Not a red→green task** — both tests assert behavior from earlier tasks (green on arrival); their value is to fail loudly if a later edit removes a registration or the catch-all.
**Files:** test `MatchDiagnosticsTests` (a SY0000 unit lock + a registration audit). No source change — all five SY12xx are already registered in Tasks 13–16.

**Behavior & contract:**
- `InternalError_MapsToSY0000`: `Diagnostics.InternalError(ex).ToDiagnostic()` has `Id == "SY0000"`, `Location.None`, and the message contains the exception text.
- `Sy12xxDescriptors_AreRegistered_InUnshippedReleases`: `AnalyzerReleases.Unshipped.md` contains all of `SY1201`–`SY1205`.
- **No forced-throw end-to-end test** (plan-review round-2 medium): do **not** add a process-global `ThrowForTesting` hook — under xUnit cross-class parallelism it could corrupt an unrelated generator-invoking test. The SY0000 path stays covered by the unit lock + the generator's `try/catch`.

- [ ] Write the SY0000 unit lock + the registration audit → both green on arrival.
- [ ] Run the full `Synto.Test.Match` suite → PASS; run `dotnet build -c Release src/Synto.SourceGenerator` → PASS with **no RS2008**.
- [ ] Commit: `test(matching): SY0000 catch-all regression-lock + SY12xx (SY1201-SY1205) registration audit`.

---

## Self-Review

**Spec coverage:**
- §3.2 attribute + `MatchOption` (None/Bare/Single, non-flags), generic-attribute discovery (spike-verified, no fallback) → Tasks 1, 4 (positive **validation** readback — no emission assert against the stub; emission proof in Task 5).
- §3.3 expression captures + slot-typing + `[Capture<TNode>]` narrowing → Task 6 (capture + narrowing, both red→green).
- §3.4 statement quantifiers + single-variable-length line → Tasks 10, 11; over-the-line (>1 per run) **and** embedded variable-length → SY1204 Task 15.
- §3.5 wildcards (static verbs + `Expr.Any<T>()`) → Tasks 3, 7, 10, 11.
- §3.6 non-linear equality → Task 8.
- §3.7 phantom `foreach` (deferred) → SY1203 Task 14.
- §3.9 anchors + SY1201 → Task 13 (`ExtractAnchors` before any count gate). Option×body-shape misuse → SY1205 Task 13.
- §4 matching semantics (None decl-rooted / Bare contains-leftmost / Single statement & expression) → Tasks 5, 9, 10, 11, 12.
- §5 bespoke straight-line emission → the generic walk, all emitter tasks.
- §6 equatable pipeline / no captured `Compilation`/`ISymbol`/`SyntaxNode` → Task 4 + cacheability tests (4, 6).
- §7 sibling folder/namespace, injected `internal` surface, deferred markers NOT injected → Tasks 1–3.
- §8/§11 diagnostics SY1201–SY1205 + SY0000 → Tasks 13–17; every reachable misuse → a located diagnostic, the SY12xx tests assert **no tree** beside it.
- §12 snapshots + round-trips + cacheability + per-arm diagnostics → throughout; §13 frozen output shape / per-option input contract → C2 + snapshot tasks.

**Contract decisions recorded:**
- **Result record nesting (C2, round-3 high):** nested in the partial target (`{Target}.{Pattern}Match`) — collision-safe across distinct targets sharing a pattern name; a deliberate, transparent deviation from spec §3.11's top-level illustration.
- **Anchored + single-variable-length element (C6, round-3 high):** **supported** in v1 per spec §8 (intersection of two v1 features); the anchored drivers bound on `before + after`, never a fixed total width — the round-3 high #2 fix, covered by Tasks 11/12.
- **netstandard2.0 `IsExternalInit` (C3, round-4 critical):** the round-3 per-file `file`-local polyfill was **unbuildable** (spike: `file` → CS0518, two `internal` copies → CS0101). Re-derived to a **single per-assembly `internal static class IsExternalInit`** via `RegisterPostInitializationOutput` (canonical name satisfies the `init` modreq; one copy ⇒ no CS0101; BCL-present ⇒ at most CS0436 warning). The **block-namespace conversion is deleted** — matchers keep their file-scoped namespace (resolves the C2/C3 conflict). The positional **record** is kept over a hand-written `sealed class`+`Deconstruct` (which would need no polyfill) because the record is spec-faithful (§3.11) and the polyfill is now one deduped constant file. The proof moves to **Task 6** (first capturing matcher), against a **pinned** ns2.0 ref closure (no `IsExternalInit`), with **witnessed RED (CS0518)** as an acceptance criterion + a closure self-check + a BCL-present coexistence fact.
- **SY1204/SY1205 split (C5, round-4 medium):** SY1205's three-misuse grab-bag is split — the embedded variable-length case moves to SY1204's quantifier-placement family; both descriptors are `{0}`-reason-parameterized; net IDs stay **SY1201–SY1205** (a deliberate fifth ID beyond the spec's four, justified by correctness.md's "every reachable misuse maps to a located diagnostic").
- **Generator-internal namespace (round-4 medium):** generator-internal Matching types live in `namespace Synto` (mirroring `TemplateInfo`); `Synto.Matching` is reserved for the injected consumer markers.

**Deferred-by-spec (correctly absent):** `Deep`/`Either`/`Many<T>` markers (not injected), `foreach` lowering, multi-variable-length lowering, semantic constraints, token/identifier capture, negation, `AtLeast`/`Between`. Each reachable deferred path degrades to SY1203/SY1204, never a literal mis-match or a throw.

**Sequencing self-check (the recurring round-2/3/4 defect class):** Task 4's positive test asserts **only** validation-passed readback (no `GeneratedTrees` against the stub) and now also covers the **SY1003** metadata-target arm; emission asserts live at Task 5; the cross-pattern cacheability test **and the netstandard2.0 self-containment proof** live at Task 6 (the first body-dependent / first **capturing** emission — a zero-capture record at Task 5 has no `init`/polyfill so the proof can't go red there; the asserted RED diagnostic is **CS0518**, witnessed); Task 8's reuse branch is a genuine red→green (Task 6 omits it); the signature-order positional lock lives at Task 11 and uses a **reversed signature-vs-walk-order** fixture that fails RED (CS1061) without the `Ordinal` sort (the round-3 `GuardThenRest` fixture was illusory). The shared `EmitAnchoredRun` is introduced at Task 9 in its **final 6-arg signature** and only grows (no re-plumb); Tasks 12/13 explicitly re-review earlier goldens, with full-suite gates after both.

**Cacheability / no-leak:** all semantic work in the FAWMN transform; only the equatable `MatchGenerationResult` flows; `MatchInfo` (holding `SemanticModel`/symbols) stays transform-local; a `MatchGenerationResult` equatability case guards it.

## Execution Handoff

**Branch isolation governs execution — see "Execution Model & Branch Isolation" at the top.** All work targets **`experimental/matching`** (base and push target), **never `main`**; the standard `ready→implement` per-green ff-push-to-main is **disabled**.

Implement via **`superpowers:subagent-driven-development` in a throwaway worktree branched from `experimental/matching`** (a fresh subagent per task + the two-stage spec/quality review). **Do NOT use `implement-plan` (incl. `{mode:"dry-run"}`)** — it bases its worktree on `origin/main`, which lacks this feature's stack, so the Matching code won't compile there.

Do **not** route this plan through the main-pushing flow. **Final integration** is a single deliberate `git push origin HEAD:experimental/matching` after the whole plan is green (not per-commit); merging to `main` is a later human decision outside this plan. Snapshot-accept steps require human review of each new/changed `*.received.cs` before it becomes a golden — do not blind-accept (including the intentional re-accepts in Tasks 12/13).

# Middleware Quoting Engine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `TemplateSyntaxQuoter` feature-agnostic — replace its 5 hard-coded templating features with a pluggable, generic-`TContext`, 7-seam **middleware quoting engine**, so the quoter "knows nothing about the step it performs."

**Architecture:** A new `internal` engine `MiddlewareSyntaxQuoter<TContext>` (Core, subclass of the untouched `internal CSharpSyntaxQuoter`) owns only seam dispatch + middleware composition. Synto's 5 features become `internal` middleware in the source generator. The 4 quoter-construction sites become the composition root. **Public-SHAPED but `internal` now** — the generic `TContext` is threaded everywhere so a future GA flip to `public` is a pure accessibility change; the public-API door is NOT opened by this plan.

**Tech Stack:** Roslyn incremental source generators (netstandard2.0), xUnit v3 + Verify snapshot tests, SDK 10 MTP.

## Global Constraints

- **Branch:** experimental feature work — land on an `experimental/*` branch, never `main` (memory `experimental-features-own-branch`).
- **Everything new is `internal`.** No `public` types. `CSharpSyntaxQuoter` / `CSharpSyntaxQuoterBase` stay `internal` and **untouched** — do NOT flip their accessibility, do NOT edit `CSharpSyntaxQuoter.Generated.cs` (self-host generated half) or `src/Synto.Bootstrap/CSharpSyntaxQuoter*.cs` (bootstrap self-host INPUT). Breaking either breaks the self-host chain.
- **Pure refactor — byte-identical snapshots are the oracle.** Every existing snapshot MUST stay byte-identical. `Synto.Test` 257/257, `Synto.Example.ObjectReader.Tests` 10/10. A moved snapshot (`.received.*` file appearing) means behavior changed — STOP and reconcile, never accept.
- **Cacheability:** no `ISymbol` / `SemanticModel` / `Compilation` / `SyntaxNode` may enter cached pipeline state. Middleware are built at **emission** (inside the `GenerateTemplate` transform), capturing only the channel dictionaries that already flow there today. The two incremental guard tests must stay green: `SpliceMemberGeneratorTest.IsIncrementalOnUnrelatedEdit`, `ObjectReaderIncrementalTests.Transform_IsCachedOnUnrelatedEdit`.
- **Precedence preserved EXACTLY:** a replaced node is NEVER decorated; a trimmed node is dropped before quoting. Encode via middleware registration order (Node seam: Replacement → Trim → Decoration). This mirrors today's `Visit(SyntaxNode?)`: replacement-pre, trim-pre, base, decoration-post.
- **List-seam order mirrors today's check sequence:** today `Visit<TNode>(SyntaxList<TNode>)` tests interpolation-fold FIRST, then member-segment. Register `InterpolationFold` before `MemberSegment` so the refactor is behavior-preserving by inspection (they target disjoint list shapes, so order is behaviorally inert, but mirror it anyway).
- **netstandard2.0:** no default interface methods. A middleware implements only the seam interfaces it handles.
- **Test runner:** `dotnet test --project <csproj>` (raw MTP; do not let rtk rewrite args — memory `dotnet-test-mtp-invocation`; filter with `--filter-method` / `--filter-class`, never `--filter`). For the full green gate use the built test exes directly (MTP misreports under `dotnet test` aggregate runs).
- **Contracts over syntax:** tasks pin behaviors / interfaces / signatures; the implementer chooses exact code (memory `plans-pin-contracts-not-syntax`).

## Reference — what exists today (do not re-derive)

- `src/Synto.SourceGenerator/Templating/TemplateSyntaxQuoter.cs` — the type being dissolved. Two overrides: `Visit(SyntaxNode?)` (lines 54-74: replacement-pre, trim-pre, base, decoration-post) and `Visit<TNode>(SyntaxList<TNode>)` (lines 84-133: interpolation-fold check, then member-segment `BuildList<TNode>` run). Field `_semanticModel` (line 13) is assigned but never read — **dead, drop it**.
- `src/Synto/CSharpSyntaxQuoter.cs` — the `internal` base. `CSharpSyntaxQuoterBase` (line 21) has 7 virtual seams: `Visit(SyntaxNode?)` (from `CSharpSyntaxVisitor`), `Visit<TNode>(SyntaxList<TNode>)` (58), `Visit<TNode>(SeparatedSyntaxList<TNode>)` (74), `Visit(SyntaxKind)` (107), `Visit(SyntaxToken)` (112), `Visit(SyntaxTriviaList)` (183), `Visit(SyntaxTokenList)` (189). `ToArrayLiteral` (36) is `protected static`. `CSharpSyntaxQuoter` (204) is the concrete instantiable partial (`new CSharpSyntaxQuoter(bool includeTrivia)`).
- `src/Synto.SourceGenerator/Templating/InterpolationFold.cs` — already middleware-shaped: ctor takes `(stringStagedRoots, Func<SyntaxNode?,ExpressionSyntax?> visit, Func<IEnumerable<ExpressionSyntax>,TypeSyntax,ExpressionSyntax> toArrayLiteral)`; method `bool TryFoldInterpolatedContents(SyntaxList<InterpolatedStringContentSyntax>, out ExpressionSyntax)`. **Reused unchanged.**
- `src/Synto.SourceGenerator/Templating/DecorationFinder.cs:15` — `internal readonly struct AppliedDecoration { string HelperMethodName; ImmutableArray<ExpressionSyntax> Arguments; }`.
- The 4 construction sites: `StagedRegionEmitter.cs:67`, `StagedScaffoldBuilder.cs:165`, `TemplateFactoryBuilder.cs:779`, `TemplateFactoryBuilder.cs:898` (`QuoteAndAssemble` — the full one: replacements + prunableNodes (trim) + memberSegments + stringStagedRoots + decorationHooks).
- `src/Synto.SourceGenerator/Templating/TemplateSyntaxQuoterInvoker.cs` — holds a quoter + template, drives `TryQuote`. Must be retargeted to the engine type.
- `test/Synto.Test/Synto.Test.csproj:89` — references Core via `Aliases="SyntoCore"` extern alias; `test/Synto.Test/SyntoCoreAssembly.cs` bridges it. New Core-internal-type tests reference `SyntoCore::Synto.<Type>`.
- `src/Synto/Synto.csproj:64-65` — `InternalsVisibleTo` to `Synto.SourceGenerator` and `Synto.Diagnostics` (NOT `Synto.Test` yet — Task 1 adds it).

## File Structure

- **Create (Core, `internal`, public-shaped)** — next to `src/Synto/CSharpSyntaxQuoter.cs`:
  - `src/Synto/Quoting/QuoteContext.cs` — abstract cooperation surface.
  - `src/Synto/Quoting/QuoteMiddleware.cs` — `IQuoteMiddleware` marker + the 7 seam interfaces + the 7 `…Next` delegates (one file; small interfaces).
  - `src/Synto/Quoting/MiddlewareSyntaxQuoter.cs` — the engine.
- **Create (SourceGenerator, `internal`)** — `src/Synto.SourceGenerator/Templating/Middleware/`:
  - `ReplacementMiddleware.cs`, `TrimMiddleware.cs`, `DecorationMiddleware.cs`, `MemberSegmentMiddleware.cs`, `InterpolationFoldMiddleware.cs`.
- **Modify:** the 4 construction sites; `TemplateSyntaxQuoterInvoker.cs`; `src/Synto/Synto.csproj` (add IVT).
- **Delete:** `src/Synto.SourceGenerator/Templating/TemplateSyntaxQuoter.cs`.
- **Test (Synto.Test):**
  - `test/Synto.Test/Quoting/MiddlewareSyntaxQuoterTest.cs` — engine seam-dispatch smoke tests (the genuinely-new behavior).
  - `test/Synto.Test/Templating/QuoteMiddlewareTest.cs` — per-middleware behavior tests.

---

## Task 1: Engine + seam interfaces + `QuoteContext` (Core, internal, public-shaped)

**Files:**
- Create: `src/Synto/Quoting/QuoteContext.cs`, `src/Synto/Quoting/QuoteMiddleware.cs`, `src/Synto/Quoting/MiddlewareSyntaxQuoter.cs`
- Modify: `src/Synto/Synto.csproj` (add `<InternalsVisibleTo Include="Synto.Test" />`)
- Test: `test/Synto.Test/Quoting/MiddlewareSyntaxQuoterTest.cs`

**Interfaces:**
- Consumes: `internal CSharpSyntaxQuoter : CSharpSyntaxQuoterBase` (Core, untouched) and its 7 virtual seams + `protected static ExpressionSyntax ToArrayLiteral(IEnumerable<ExpressionSyntax>, TypeSyntax)`.
- Produces (all `internal`, in namespace `Synto`):
  - `abstract class QuoteContext` with: `ExpressionSyntax? Quote(SyntaxNode? node)`, `ExpressionSyntax QuoteList<TNode>(SyntaxList<TNode> list) where TNode : SyntaxNode`, `ExpressionSyntax ToArrayLiteral(IEnumerable<ExpressionSyntax> nodes, TypeSyntax elementType)`.
  - `interface IQuoteMiddleware {}` (empty marker).
  - 7 seam interfaces, each `: IQuoteMiddleware`, each with one method (shapes below).
  - 7 `…Next` delegates (the per-seam continuation).
  - `class MiddlewareSyntaxQuoter<TContext> : CSharpSyntaxQuoter where TContext : QuoteContext`, ctor `(IReadOnlyList<IQuoteMiddleware> middleware, bool includeTrivia)`.

**Seam contracts (pin shapes, not exact code):**

```csharp
internal delegate ExpressionSyntax? NodeQuoteNext(SyntaxNode? node);
internal interface INodeQuoteMiddleware<TContext> : IQuoteMiddleware where TContext : QuoteContext
{ ExpressionSyntax? Quote(SyntaxNode? node, TContext ctx, NodeQuoteNext next); }

internal delegate ExpressionSyntax ListQuoteNext<TNode>(SyntaxList<TNode> list) where TNode : SyntaxNode;
internal interface IListQuoteMiddleware<TContext> : IQuoteMiddleware where TContext : QuoteContext
{ ExpressionSyntax Quote<TNode>(SyntaxList<TNode> list, TContext ctx, ListQuoteNext<TNode> next) where TNode : SyntaxNode; }

// …and identically for SeparatedList, Token, TriviaList, TokenList, Kind:
//   ISeparatedListQuoteMiddleware<TContext>.Quote<TNode>(SeparatedSyntaxList<TNode>, TContext, SeparatedListQuoteNext<TNode>)
//   ITokenQuoteMiddleware<TContext>.Quote(SyntaxToken, TContext, TokenQuoteNext)
//   ITriviaListQuoteMiddleware<TContext>.Quote(SyntaxTriviaList, TContext, TriviaListQuoteNext)
//   ITokenListQuoteMiddleware<TContext>.Quote(SyntaxTokenList, TContext, TokenListQuoteNext)
//   IKindQuoteMiddleware<TContext>.Quote(SyntaxKind, TContext, KindQuoteNext)
```

**Engine contract (pin this, not exact code):**
1. Ctor partitions `middleware` once via `OfType<I…QuoteMiddleware<TContext>>()` into 7 ordered arrays. A middleware implementing several seam interfaces lands in multiple partitions; registration order preserved within each.
2. The **node** chain (non-generic) is pre-composed once in the ctor into a single `NodeQuoteNext` whose terminal calls `base.Visit(node)`. The **generic** list/separated-list seams are composed **per-call** inside their override (the chain closes over `TNode`).
3. **All 7 base seams are overridden.** Each override: if that seam's partition is empty → call `base.<Visit>` directly (zero-overhead fast path — exactly today's behavior, where only Node and List ever have middleware). Otherwise run the chain; the chain's terminal `next` is `base.<Visit>`.
4. The engine owns one `TContext` bound at construction (one quoter == one transform); `QuoteContext.Quote` ⇒ `this.Visit(node)` (re-enters the node pipeline), `QuoteContext.QuoteList<TNode>` ⇒ `this.Visit(list)`, `QuoteContext.ToArrayLiteral` ⇒ the base `protected static ToArrayLiteral`. For `TContext = QuoteContext`, supply a trivial concrete nested `DefaultQuoteContext : QuoteContext` bound to the engine (no extra state).
5. No `SemanticModel`/`ISymbol`/`Compilation`/live `SyntaxNode` is captured into the engine or context beyond the transform-local channel data the middleware already hold.

- [ ] **Step 1: Add Core IVT for tests** — in `src/Synto/Synto.csproj`, next to the existing `<InternalsVisibleTo>` items (lines 64-65), add:

```xml
<InternalsVisibleTo Include="Synto.Test" />
```

- [ ] **Step 2: Write the failing seam-dispatch test** — `test/Synto.Test/Quoting/MiddlewareSyntaxQuoterTest.cs`. Use the `SyntoCore::` extern alias (see top of file: `extern alias SyntoCore;` then `using SyntoCore::Synto;`). Three tests:

```csharp
// (a) No middleware ⇒ identical to a plain CSharpSyntaxQuoter on a representative node.
[Fact]
public void NoMiddleware_QuotesIdenticallyToBase()
{
    var node = SyntaxFactory.ParseExpression("a + b * c");
    var baseQuote = new SyntoCore::Synto.CSharpSyntaxQuoter(includeTrivia: false).Visit(node);
    var engineQuote = new SyntoCore::Synto.MiddlewareSyntaxQuoter<SyntoCore::Synto.QuoteContext>(
        System.Array.Empty<SyntoCore::Synto.IQuoteMiddleware>(), includeTrivia: false).Visit(node);
    Assert.Equal(baseQuote!.NormalizeWhitespace().ToFullString(), engineQuote!.NormalizeWhitespace().ToFullString());
}

// (b) A recording Token middleware IS invoked (proves a non-Node/List seam dispatches through the chain).
[Fact]
public void TokenMiddleware_IsInvoked()
{
    var mw = new RecordingTokenMiddleware();
    var node = SyntaxFactory.ParseExpression("x");          // contains an identifier token
    _ = new SyntoCore::Synto.MiddlewareSyntaxQuoter<SyntoCore::Synto.QuoteContext>(
        new SyntoCore::Synto.IQuoteMiddleware[] { mw }, includeTrivia: false).Visit(node);
    Assert.True(mw.Invoked);
}

// (c) Node-chain precedence: a short-circuiting Node middleware wins over a later one (proves ordered chain + short-circuit).
[Fact]
public void NodeMiddleware_ShortCircuits_LaterMiddlewareNotCalled() { /* register [ShortCircuit, Recording]; assert Recording.Invoked == false */ }
```

`RecordingTokenMiddleware` implements `SyntoCore::Synto.ITokenQuoteMiddleware<SyntoCore::Synto.QuoteContext>`, sets `Invoked = true`, returns `next(token)`. Define the recording/short-circuit helpers in the test file.

- [ ] **Step 3: Run it, confirm it fails** — `dotnet test --project test/Synto.Test/Synto.Test.csproj --filter-class MiddlewareSyntaxQuoterTest`. Expected: FAIL to COMPILE (engine/interfaces/context don't exist yet).

- [ ] **Step 4: Implement** — create `QuoteContext.cs`, `QuoteMiddleware.cs` (marker + 7 seam interfaces + 7 `…Next` delegates), `MiddlewareSyntaxQuoter.cs` (the engine per the contract above). All `internal`, namespace `Synto`, in `src/Synto/Quoting/`.

- [ ] **Step 5: Run it, confirm it passes** — same filter as Step 3. Expected: PASS (3/3).

- [ ] **Step 6: Confirm zero snapshot movement** — `dotnet build Synto.slnx -c Debug` then run `./test/Synto.Test/bin/Debug/net10.0/Synto.Test.exe`. Expected: all green, no `.received.*` files (engine is not wired into any generator path yet, so all 257 existing snapshots are untouched).

- [ ] **Step 7: Commit** — `feat(quoting): add internal middleware quoting engine + 7 seam interfaces + QuoteContext (public-shaped, unused)`.

## Task 2: The 5 internal feature middleware (SourceGenerator)

**Files:**
- Create: `src/Synto.SourceGenerator/Templating/Middleware/{Replacement,Trim,Decoration,MemberSegment,InterpolationFold}Middleware.cs`
- Test: `test/Synto.Test/Templating/QuoteMiddlewareTest.cs`

**Interfaces:**
- Consumes: the seam interfaces + `QuoteContext` from Task 1 (Core-internal, visible to SourceGenerator via existing IVT); `InterpolationFold`, `AppliedDecoration`, `CollectionSyntaxExtensions.BuildList<TNode>`.
- Produces (all `internal`, namespace `Synto`, default `TContext = QuoteContext`):
  - `ReplacementMiddleware(IReadOnlyDictionary<SyntaxNode,ExpressionSyntax> replacements)` : `INodeQuoteMiddleware<QuoteContext>`.
  - `TrimMiddleware(HashSet<SyntaxNode> trimNodes)` : `INodeQuoteMiddleware<QuoteContext>`.
  - `DecorationMiddleware(IReadOnlyDictionary<SyntaxNode,ImmutableArray<AppliedDecoration>> hooks)` : `INodeQuoteMiddleware<QuoteContext>`.
  - `MemberSegmentMiddleware(IReadOnlyDictionary<SyntaxNode,ExpressionSyntax> memberSegments)` : `IListQuoteMiddleware<QuoteContext>`.
  - `InterpolationFoldMiddleware(IReadOnlyDictionary<SyntaxNode,ExpressionSyntax> stringStagedRoots)` : `IListQuoteMiddleware<QuoteContext>`.

**Behavior contracts (each reproduces today's `TemplateSyntaxQuoter` branch EXACTLY):**
- **ReplacementMiddleware** (Node, pre, short-circuit): `if node ≠ null && replacements.TryGetValue(node, out var r) → return r;` else `return next(node);`. Does NOT call `next` on a hit.
- **TrimMiddleware** (Node, pre, drop): `if node ≠ null && trimNodes.Contains(node) → return null;` else `return next(node);`.
- **DecorationMiddleware** (Node, post): `var result = next(node); if node ≠ null && result ≠ null && hooks.TryGetValue(node, out var list) → fold each AppliedDecoration onto result as a chained `result.<HelperMethodName>(args…)` invocation, in order; return result;`. (Copy the exact fold loop from `TemplateSyntaxQuoter.Visit` lines 63-72.)
- **MemberSegmentMiddleware** (List): `if no child of list is a key in memberSegments → return next(list);` else build the `CollectionSyntaxExtensions.BuildList<TNode>(…)` run exactly as `TemplateSyntaxQuoter.Visit<TNode>` lines 98-132 (each fixed member quoted via `ctx.Quote`, each segment substituted at its declaration position; element type `ParseTypeName(typeof(TNode).Name)`).
- **InterpolationFoldMiddleware** (List): owns an `InterpolationFold` built from `ctx.Quote` + `ctx.ToArrayLiteral` (build it lazily on first call from `ctx`, or per-call — cheap). `if typeof(TNode) == typeof(InterpolatedStringContentSyntax) && fold.TryFoldInterpolatedContents((SyntaxList<InterpolatedStringContentSyntax>)(object)list, out var folded) → return folded;` else `return next(list);`. The `TNode == InterpolatedStringContentSyntax` guard now lives INSIDE this middleware (moved out of the quoter).

**Note:** `InterpolationFold` itself stays in `Templating/` unchanged and is already covered by `InterpolationFoldTest.cs`. `InterpolationFoldMiddleware` is a thin wrapper — its test below only proves the wrapper delegates + the `TNode` guard.

- [ ] **Step 1: Write failing per-middleware tests** — `test/Synto.Test/Templating/QuoteMiddlewareTest.cs`. One focused test per middleware, each constructing the Core engine (`SyntoCore::` alias) with exactly that one middleware and asserting output on a minimal parsed input. Examples (pin behavior, fill exact asserts during impl):

```csharp
// Replacement short-circuits to the supplied expression.
[Fact] public void Replacement_ReturnsReplacement_ForKeyedNode() { /* map{node→IdentifierName("REPLACED")}; quote → contains "REPLACED", no quoting of node */ }
// Trim drops the keyed node (Visit returns null for it).
[Fact] public void Trim_ReturnsNull_ForKeyedNode() { /* set{node}; engine.Visit(node) == null */ }
// Decoration wraps the base quote in a chained .Apply call.
[Fact] public void Decoration_WrapsBaseQuote_InChainedInvocation() { /* hooks{node→[AppliedDecoration("ApplyX", args)]}; output text contains ".ApplyX(" applied to the base quote */ }
// MemberSegment emits a BuildList<TNode> run when a child has a segment.
[Fact] public void MemberSegment_EmitsBuildList_WhenChildHasSegment() { /* member list w/ one segment; output contains "CollectionSyntaxExtensions.BuildList<" */ }
// InterpolationFoldMiddleware defers when TNode is not InterpolatedStringContentSyntax.
[Fact] public void InterpolationFold_DefersToNext_ForNonInterpolatedList() { /* a plain SyntaxList<StatementSyntax> → identical to base list quoting */ }
```

- [ ] **Step 2: Run them, confirm they fail** — `dotnet test --project test/Synto.Test/Synto.Test.csproj --filter-class QuoteMiddlewareTest`. Expected: FAIL to COMPILE (middleware don't exist).

- [ ] **Step 3: Implement the 5 middleware** per the behavior contracts above, lifting the exact logic from `TemplateSyntaxQuoter`. Keep all work transform-local (constructors capture only the channel dictionaries).

- [ ] **Step 4: Run them, confirm they pass** — same filter. Expected: PASS. Also run `--filter-class InterpolationFoldTest` to confirm `InterpolationFold` is untouched/green.

- [ ] **Step 5: Confirm zero snapshot movement** — `./test/Synto.Test/bin/Debug/net10.0/Synto.Test.exe`. Expected: 257 green, no `.received.*` (middleware exist but are not yet wired into any construction site).

- [ ] **Step 6: Commit** — `feat(quoting): add 5 internal feature middleware (replacement/trim/decoration/member-segment/interpolation-fold)`.

## Task 3: Flip the composition root, delete `TemplateSyntaxQuoter`

**Files:**
- Modify: `src/Synto.SourceGenerator/Templating/StagedRegionEmitter.cs:67`, `StagedScaffoldBuilder.cs:165`, `TemplateFactoryBuilder.cs:779`, `TemplateFactoryBuilder.cs:898`
- Modify: `src/Synto.SourceGenerator/Templating/TemplateSyntaxQuoterInvoker.cs` (retarget quoter type)
- Delete: `src/Synto.SourceGenerator/Templating/TemplateSyntaxQuoter.cs`

**Interfaces:**
- Consumes: the engine (Task 1) + the 5 middleware (Task 2).
- Produces: 4 construction sites that build an ordered `IReadOnlyList<IQuoteMiddleware>` from the same channel data they pass today, then `new MiddlewareSyntaxQuoter<QuoteContext>(middleware, includeTrivia)`. `TemplateSyntaxQuoter` no longer exists. Dead `_semanticModel` gone.

**Construction contract (pin this, not exact code):**
- Introduce one small helper (e.g. `static IReadOnlyList<IQuoteMiddleware> TemplateQuoterMiddleware.Build(replacements, trimNodes, memberSegments, stringStagedRoots, decorationHooks)`) so the 4 sites share one composition. The helper appends, in this EXACT order (precedence-bearing):
  1. `ReplacementMiddleware` (if `replacements` non-empty)
  2. `TrimMiddleware` (if `trimNodes` non-empty)
  3. `InterpolationFoldMiddleware` (if `stringStagedRoots` non-empty)
  4. `MemberSegmentMiddleware` (if `memberSegments` non-empty)
  5. `DecorationMiddleware` (if `decorationHooks` non-empty)
  Empty-channel middleware are omitted (so the engine's per-seam partitions stay empty and hit the zero-overhead base path). Node-seam order among the registered ones is Replacement → Trim → … → Decoration; the engine partitions by seam, so Decoration (Node, post) composes after Replacement/Trim (Node, pre) regardless of the List middleware interleaved in the list. Verify the resulting **node** partition order is exactly `[Replacement?, Trim?, Decoration?]` and the **list** partition order is exactly `[InterpolationFold?, MemberSegment?]`.
- Each site passes only the channels it uses today (e.g. `StagedRegionEmitter` → replacements + stringStagedRoots; `QuoteAndAssemble` → all five).
- `TemplateSyntaxQuoterInvoker`: change the held/typed quoter from `TemplateSyntaxQuoter` to `MiddlewareSyntaxQuoter<QuoteContext>` (or the base `CSharpSyntaxQuoter` if only base quoting is needed — confirm by reading the invoker; it likely only needs `Visit`/`TryQuote` surface). Keep the file name or rename to `TemplateQuoterInvoker` (implementer's choice; if renamed, update references).

- [ ] **Step 1: Build the composition helper + flip site `QuoteAndAssemble` (`TemplateFactoryBuilder.cs:898`)** — replace the `new TemplateSyntaxQuoter(...)` (all 5 channels) with the helper + `new MiddlewareSyntaxQuoter<QuoteContext>(...)`. This site alone exercises ALL 5 middleware.

- [ ] **Step 2: Build + run the full snapshot suite** — `dotnet build Synto.slnx -c Debug` then `./test/Synto.Test/bin/Debug/net10.0/Synto.Test.exe`. Expected: 257 green, **zero** `.received.*`. If any snapshot moves, STOP — the flip changed behavior; diff the `.received` vs `.verified` and reconcile the middleware/order before continuing.

- [ ] **Step 3: Flip the 3 staged sites** — `StagedRegionEmitter.cs:67`, `StagedScaffoldBuilder.cs:165`, `TemplateFactoryBuilder.cs:779` (each: replacements/liftMap + stringStagedRoots). Use the same helper.

- [ ] **Step 4: Retarget the invoker + delete `TemplateSyntaxQuoter.cs`** — update `TemplateSyntaxQuoterInvoker` to the engine type; delete `TemplateSyntaxQuoter.cs`; drop the dead `_semanticModel` (it lived only on the deleted type). Build — expect 0 errors, no remaining references to `TemplateSyntaxQuoter`.

- [ ] **Step 5: Full green gate + incremental guards** — run:

```bash
dotnet build Synto.slnx -c Debug
./test/Synto.Test/bin/Debug/net10.0/Synto.Test.exe                                                                          # expect 257, no .received
./examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Tests/bin/Debug/net10.0/Synto.Example.ObjectReader.Tests.exe   # expect 10
```

Confirm the two incremental guards are in the green set: `SpliceMemberGeneratorTest.IsIncrementalOnUnrelatedEdit` and `ObjectReaderIncrementalTests.Transform_IsCachedOnUnrelatedEdit`. Expected: all PASS, zero `.received.*`.

- [ ] **Step 6: Commit** — `refactor(quoting): replace TemplateSyntaxQuoter with the middleware engine at all 4 construction sites; delete the monolithic quoter`.

## Task 4: Integration green-gate + record the deferred decisions

**Files:**
- Modify (docs only): `docs/kb/` map entry for the quoter (via the `refresh-kb` skill if path:line refs moved), and the memory note for the deferred GA flip.

- [ ] **Step 1: Full solution build + all suites** — `dotnet build Synto.slnx -c Debug`; run `Synto.Test.exe` (257), `Synto.Example.ObjectReader.Tests.exe` (10), and `dotnet test --project test/Synto.Diagnostics.Test/Synto.Diagnostics.Test.csproj` (diagnostics — unaffected, confirm green). Expected: all PASS, zero `.received.*`.

- [ ] **Step 2: Self-host / bootstrap unaffected** — confirm `src/Synto.Bootstrap` and `Synto.Bootstrap.Test` build green and `CSharpSyntaxQuoterGeneratorTest.VerifySnapshot` is byte-identical (we never touched the base or bootstrap). Run its build + test.

- [ ] **Step 3: Format gate** — `dotnet format whitespace` (scoped per memory `no-git-pre-commit-hooks`). Expected: clean (or apply + re-run the green gate).

- [ ] **Step 4: Record deferred GA flip** — write a memory note (`memory/middleware-quoter-engine-internal.md` + MEMORY.md pointer): the engine + seams + `QuoteContext` shipped `internal`/public-SHAPED; the GA flip to `public` (which forces the whole `CSharpSyntaxQuoter` base public — ~100 `VisitXxx` methods — via the generator's `.WithModifiers(targetClass.Modifiers)`) is deferred to GA. Cross-link `[[post-quote-decorations-ga-gate]]`.

- [ ] **Step 5: Commit** — only docs/memory changed: `docs(quoting): record middleware engine internal/public-shaped + deferred GA flip`.

---

## Self-Review

- **Spec coverage:**
  - Engine (seam dispatch, partition, compose, empty-partition fast path) → Task 1.
  - 7 seams / `QuoteContext` / generic `TContext` / marker + per-seam interfaces → Task 1.
  - 5 features as middleware (Replacement, Trim, Decoration, MemberSegment, InterpolationFold) → Task 2.
  - Composition root at the 4 sites + delete `TemplateSyntaxQuoter` + drop dead `_semanticModel` + retarget invoker → Task 3.
  - Precedence (replaced-never-decorated) via order `[Replacement, Trim, …, Decoration]` → Task 3 Step 1 + Global Constraints.
  - List-seam order (interpolation-fold before member-segment) → Task 3 Step 1 + Global Constraints.
  - "Internal now, public-shaped"; base untouched; no public door → Global Constraints + Task 1 (`internal`) + Task 4 Step 4 (deferred-flip note).
  - Open-question #4 (prove the 5 unconsumed seams dispatch) → Task 1 Step 2 (`TokenMiddleware_IsInvoked`).
  - Cacheability (no live symbols; build at emission; 2 incremental guards) → Global Constraints + Task 3 Step 5.
  - Byte-identical oracle → every task's "zero `.received.*`" step.
  - Self-host untouched → Task 4 Step 2.
- **Type consistency:** `MiddlewareSyntaxQuoter<TContext>`, `QuoteContext`, `IQuoteMiddleware`, `I{Node,List,SeparatedList,Token,TriviaList,TokenList,Kind}QuoteMiddleware<TContext>`, `{Node,List,…}QuoteNext` delegates, the 5 `*Middleware` ctors, and `AppliedDecoration{HelperMethodName,Arguments}` are named identically across Tasks 1-3. The 4 site line numbers (67/165/779/898) and the invoker match the Reference section.
- **No placeholders:** test bodies in Tasks 1-2 give the concrete assertion intent + the exact construction (engine + `SyntoCore::` alias); the implementer fills the final `Assert` text against real quoted output — flagged inline, not hidden. Behavior contracts cite the exact source lines to lift from (`TemplateSyntaxQuoter` 54-74, 84-133; the decoration loop 63-72).
- **Risk — precedence inversion:** Task 3 Step 1 explicitly verifies the node partition is `[Replacement?, Trim?, Decoration?]`; Task 1 Step 2 test (c) proves short-circuit ordering at the engine level.
- **Risk — snapshot drift:** guarded after every task by "zero `.received.*`"; Task 3 Step 2 stops the flip at the first moved snapshot.

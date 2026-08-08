# Quote-time semantic type qualification — self-contained generated output by construction

**Status:** Design (brainstormed 2026-07-10, accepted shape). Resolves evaluation finding **C1**
(2026-07-10 eval / `issues.md`): a de-qualified `[Template]` body produced non-compiling output for
`ImplicitUsings`-disabled consumers.

**Origin:** the ObjectReader dog-food. The switch-expression-typed-returns branch cosmetically
de-qualified the reader template body (`global::System.X` → bare `X`, 24 lines). The body compiles in
the carrier only because the Generator project enables SDK `ImplicitUsings`; the produced reader,
re-hosted into `ObjectReader.g.cs` (which carries no using context), then fails with CS0246 for any
consumer whose implicit usings are off — the compiler default. An interim band-aid (two hardcoded
`using`s in `ObjectReaderGenerator.Emit()`) plus a new compile gate (T1) currently hold the example
green; this feature is the product fix that retires the band-aid.

## 1. Problem

A template body names framework types by **simple name**, relying on the carrier file's using context
(explicit `using`s **and** SDK-injected implicit usings). The quoter reproduces those names **lexically**
— `IdentifierName("IEnumerator")`, `IdentifierName("Type")` — so both the generated factory source and
the syntax the factory produces at runtime carry bare names. Bare names only resolve where the hosting
document supplies the right usings. Two facts make that fragile:

1. **Implicit usings are invisible to syntax.** `ReaderTemplate.cs` has no `using System;`; `Type`,
   `DBNull`, `InvalidCastException` resolve only through the project's implicit usings. Anything that
   collects usings off the carrier *syntax* (as `TransplantCarrierUsings` does — `carrierUnit.Usings`)
   cannot see them, so it cannot reproduce them downstream.
2. **Re-hosting consumers own their output document.** A generator like ObjectReader calls the factory
   for a `ClassDeclarationSyntax` and splices it into a document it composes itself. A
   `ClassDeclarationSyntax` cannot carry compilation-unit `using`s, so a bare-name node landing in a
   using-less document is unresolvable — and the quoter has no visibility into that document.

### Root cause

The quoter emits names as authored, but the guarantee generated code needs is **self-containedness**:
the output must resolve regardless of the hosting document's using context. Authoring ergonomics (bare
names) and output self-containedness are in tension, and today output self-containedness is an accident
of consumer csproj settings rather than a property of the emit.

## 2. Approach — resolve it once, bake it in

At quote time the transform holds the carrier `SemanticModel`, which knows the **exact** symbol behind
every name (implicit usings included). Resolve each type/namespace-bound name to its `global::`-rooted
fully-qualified form and emit **that**. `global::`-rooted names resolve with zero using context and
cannot be captured by an outer scope, so output is self-contained **by construction** — for standard
factory-document templates and re-hosting consumers alike, with **no change to any factory return type
or consumer call site**.

Readability cost (verbose `global::` names) is accepted; a future per-document "using-tidy" post-pass
(§8) may collapse them back to bare names + a collision-safe using block where the whole output document
is visible. That pass is explicitly **out of scope** here.

### Why not bare-name + collected usings

Reducing to a simple name + an emitted `using` is not unconditionally safe: same-simple-name ambiguity
(CS0104), nested types (a `using` imports a namespace, not a containing type), and output-context
collisions the quoter cannot see. It also cannot deliver usings to re-hosting consumers without a
factory API change. Full qualification sidesteps all of it; the tidy pass (§8), running where the
document is complete, is the right place to recover readability safely.

## 3. Mechanism — finder + per-name middleware

Mirrors Synto's established finder→step split (`FacadeCallFinder`→builder step, `DecorationFinder`,
staging). The quoter pipeline is deliberately **SemanticModel-free** (`QuoteContext`: steps "never hold
`SemanticModel`/`ISymbol`/`Compilation` state, so nothing leaks into cached pipeline state"); this design
preserves that — the SemanticModel is used only in the finder, and only value data reaches the quoter.

### 3.1 `QualificationFinder` (SemanticModel pass, runs during plan assembly)

- Walks the carrier template body. For every name node that **binds to a named type or namespace symbol**
  reached *as a type/namespace reference*, resolves the `global::`-rooted fully-qualified `NameSyntax` and
  produces a `QualifyStep` closing over it, paired with the original node.
- Its output is per-node `(SyntaxNode, QualifyStep)` entries — the same hand-off shape `FacadeCallFinder`
  gives `PlanAssembler`, **not** a separate persisted map. There is no `node → NameSyntax` dictionary as a
  runtime structure; the qualified name lives on the step. No `SemanticModel`, `ISymbol`, or `Compilation`
  is retained — the step closes over syntax only, so the result folds into the plan without breaking
  cacheability.
- Runs inside the `ForAttributeWithMetadataName` transform, alongside the other finders.

### 3.2 `QualifyStep : INodeQuoteMiddleware` (SemanticModel-free)

- **Closes over its own substitute `NameSyntax`** — that is the whole state it needs. Counterpart of
  `ReplaceStep`, but instead of a precomputed quoted literal it re-enters the quoter with the substitute:
  `ctx.Quote(qualifiedName)`. The qualified name quotes through the normal path, so the emitted factory
  produces the `global::` form and composes with everything around it.
- **One instance per qualifying name occurrence**, registered against that node — the existing per-node
  step model (`nodeSteps: SyntaxNode → steps`); no new dispatch machinery and no second lookup structure.

### 3.3 `PlanAssembler` fold

`PlanAssembler` folds the finder's per-node `QualifyStep` entries into the existing `nodeSteps` map,
alongside every other feature's per-node steps, and **owns precedence** (§5). The `nodeSteps` map is the
only node-keyed structure; each `QualifyStep` carries its own qualified name. Feature composition stays in
`PlanAssembler`; the quoter factory remains feature-blind.

## 4. Scope predicate — what gets qualified

Qualify a name node iff the SemanticModel binds it to an `INamedTypeSymbol` or `INamespaceSymbol` reached
as a **type or namespace reference**. Emit the `global::`-rooted fully-qualified form.

**Skip (left exactly as authored):**

- `[Splice]` / ordinary **type parameters** (`T`, `TRet`) — no namespace, must stay the parameter name.
- **Predefined-type keywords** (`int`, `string`, `object`, `void`, …) — already self-contained; never
  rewrite to `global::System.Int32` (semantics identical, readability worse, and it would churn every
  snapshot needlessly).
- **Builder facade names** (`Member`, `Switch`, `Case`, `Throw`, …) — bind to a builder or are unbound;
  they are not type references. Their **type arguments** are subject to the predicate.
- **Locals, parameters, labels, member names** — not type/namespace references.
- **`var`** — inferred; never expand to the concrete type (changes readability/semantics of intent).
- Names **already `global::`-rooted**, and the **Synto surface** (`Synto` / `Synto.*`) consistent with
  the existing `TransplantCarrierUsings` exclusion.

## 5. Interactions & precedence (owned by `PlanAssembler`)

- **Facade calls.** The builder name is never qualified; type arguments are
  (`Throw<TRet>(new InvalidCastException(...))` → `global::System.InvalidCastException`; `TRet` skipped).
- **Staged / live / replaced regions.** Qualification applies to the **quoted skeleton**. A name inside a
  region that is replaced wholesale (a staged region quoted verbatim, a live control region, a builder
  island) is that region's responsibility; precedence ensures a name already consumed by staging or a
  facade is not also qualified. Verbatim carrier code that runs in the factory keeps relying on
  `TransplantCarrierUsings` (unchanged).
- **Decorations.** Decoration type arguments are **already** qualified today (`Implements` →
  `global::System.Data.IDataReader`). Share one symbol→`global::NameSyntax` helper between decoration
  resolution and `QualificationFinder`.

## 6. The qualification helper (and the M12 fix)

A single primitive: `INamedTypeSymbol`/`INamespaceSymbol` → `global::`-rooted `NameSyntax`, preserving
generic type arguments and nested-type containment (`Outer<T>.Inner` →
`global::Ns.Outer<...>.Inner`). Evaluation finding **M12** (a `GetQualifiedNameSyntax`-style helper drops
type arguments on generic containing types) is **in scope**: qualification will routinely exercise
generic and nested types, so the helper must render them correctly. Decoration resolution adopts the same
helper.

## 7. Error handling

A name that fails to bind (error symbol / missing type) is **left as authored** — no invented
qualification. Such a name was already going to fail downstream; the existing post-generation compile
(and the ObjectReader T1 gate) surface it. No new diagnostics: qualification is a best-effort correctness
improvement, not a validation gate.

## 8. Out of scope — future using-tidy post-pass

A later, independent post-generation pass may run over a **complete output document**, collapse
`global::`-rooted names back to bare names, and emit a collision-safe `using` block (safe precisely
because the whole document — every name and every declaration — is visible at that point, unlike the
quoter). Designed-for but **not built here**; nothing in this feature precludes it.

## 9. Testing & acceptance

- **Oracle.** The pre-de-qualification ObjectReader `global::` snapshot is the acceptance target:
  bare-authored `ReaderTemplate.cs` + feature ON must reproduce the fully-qualified output. On landing,
  delete the interim hardcoded `using`s from `ObjectReaderGenerator.Emit()`; the T1 compile gate
  (`Generated_Reader_Compiles_Without_ImplicitUsings`) stays green.
- **Suite churn.** Every `Synto.Test` snapshot that names a framework type churns bare→`global::`;
  re-accept, and each must still **post-generation compile** (the existing `VerifySwitchTemplate` /
  harness compile gates enforce this).
- **Focused unit tests.** Nested type `Outer.Inner` qualifies correctly (M12); generic type arguments
  qualify; type parameter skipped; predefined keyword untouched; already-`global::` untouched; builder
  facade name untouched; a name in a `[Splice]`/staged region handled by precedence, not double-qualified.
- **Cacheability.** The `CacheabilityAssert.AllStepsCachedOrUnchanged` guard stays green — the
  `QualifyStep` closes over a `NameSyntax` value only; no `SemanticModel`/`ISymbol`/`Compilation` enters
  pipeline state.

## 10. Migration / rollout

- Land on an `experimental/*` branch (project norm; never `main` directly).
- Remove the interim hardcoded `using`s in `ObjectReaderGenerator.Emit()` as part of the change; keep the
  T1 compile gate.
- Re-accept churned snapshots in the same change; the post-gen compile gates are the correctness backstop
  for the re-accepts.

## Non-goals

- The using-tidy post-pass (§8).
- Any change to factory return types, consumer call sites, or the `[Template]` authoring surface.
- New diagnostics.
- Qualifying non-type references (members, locals, `var`) or predefined keywords.

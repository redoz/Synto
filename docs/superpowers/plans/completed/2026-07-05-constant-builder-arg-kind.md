# `[Constant]` builder-arg binding-time kind — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `[Constant]` as the 4th `[SyntaxBuilder]` parameter binding-time kind (alongside `[Quoted]`, `[TypeArg]`, and unmarked): a `[Constant]`-marked builder parameter evaluates its call argument at factory-build time and **lifts** it to a constant `ExpressionSyntax` via the shared `ValueLift`, then adopt it on `Case`'s `label` so `Case(c.Ordinal, …)` replaces the hand-built `LiteralExpression(NumericLiteralExpression, Literal(c.Ordinal))`. The value-lift is made **batteries-included** so it emits every reasonable compile-time constant — built-in literals **and enums** (`Color.Red` → `global::Ns.Color.Red`) — not just literals.

**Architecture:** `[Constant]` reuses the single-source value-lift (`ValueLift.TryEmitValueLift` → `value.ToSyntax()`), which we extend once to cover enum values (emitting a qualified enum-member access, cast fallback for unnamed/flags values). Because the lift is single-source, that extension lands uniformly for `[Quote]`, `Quote()`, `[Unquote]`, and `[Constant]`. The `[Constant]` lift is applied at the **builder-argument binding seam** (`FacadeArgumentBinder.Bind`, which already holds the `SemanticModel` and the diagnostics sink), the pre-lifted node is carried on the binding, and `BuilderStep` splices it verbatim. No standalone `Constant<T>()` facade is added (superseded — see spec §3.2).

**Tech Stack:** C# Roslyn incremental source generators; `dotnet test` (SDK 10 / MTP); Verify snapshot tests.

## Global Constraints

- **Base = `main` tip** (the current newest line; contains §3.1 `Unquote` rename + `Member`-on-`[SyntaxBuilder]` + switch-builders). Do NOT base on `experimental/constant-facade` (stale — its §3.1 work is already in `main`). Work commits go on an **`experimental/*` branch, never `main`**; commit only when the user asks (the implement-plan workflow commits inside its own workspace, which is fine).
- **Single-source the lift:** all value→syntax lifting flows through `ValueLift.TryEmitValueLift` (`src/Synto.Generators/Templating/Lifting/ValueLift.cs:70`) → `LiteralSyntaxExtensions.ToSyntax` (`src/Synto.Core/Syntax/EmitHelpers/LiteralSyntaxExtensions.cs`). NEVER duplicate the literal/enum switch. Enum support is added to that ONE place.
- **Batteries-included constant coverage:** built-in literal types (already), `null` (already), **enums** (this plan). Concrete custom types still require a `[Runtime]` `ToSyntax(this T)` converter; anything past that is the author's escape hatch (hand-build the node / don't use the builder sugar).
- **Diagnostics are SY1008 / SY1009** — `TemplateDiagnostics.NoRuntimeConverter` (no `[Runtime]` converter for a concrete non-built-in, non-enum type) and `TemplateDiagnostics.AmbiguousRuntimeConverter` (more than one). The spec/handoff text saying "SY1011/SY1012" is **stale**; reference the descriptor methods, not the IDs. After the enum extension, enums NO LONGER hit SY1008.
- **Byte-identical dog-food output:** the ObjectReader **final generated reader** snapshot (`examples/…/ObjectReaderSnapshotTests…#ObjectReader.g.verified.cs`) MUST stay byte-identical after switching `Case` labels to `[Constant]`. Generator *factory-source* snapshots (e.g. `SwitchBuilderTest.*.verified.cs`) legitimately change (`LiteralExpression(…)` → `(…).ToSyntax()`) and are benign re-accepts.
- **Tests (SDK 10 / MTP):** run raw `dotnet test --project <csproj>`; filter with `--filter-method` / `--filter-class`, never `--filter`.
- `[Constant]` is a **value-axis** kind: it consumes a value argument (like `[Quoted]`/unmarked), not a type argument (unlike `[TypeArg]`).
- **Value-lift vs quote boundary (design invariant to preserve):** `[Constant]` emits a *value's* faithful constant syntax (a literal, or an enum-member access). It does NOT preserve an output-world *identifier* — a `const int MaxRetries = 3` lifts to `3` (the compiler already folded the name), and emitting a name that must exist in the output world is `[Quoted]`'s job, not `[Constant]`'s.

---

## File Structure

**New:**
- `src/Synto.Core/Templating/Builders/Surface/ConstantAttribute.cs` — the new parameter attribute (authored `public`, injected `internal` by `SurfaceInjectionGenerator`). Mirrors `QuotedAttribute`.
- `test/Synto.Test/…/LiteralSyntaxExtensionsTest.cs` — direct unit tests for the runtime `ToSyntax` helper's new enum branch (no such test file exists today).

**Modified — the shared value-lift (batteries-included):**
- `src/Synto.Core/Syntax/EmitHelpers/LiteralSyntaxExtensions.cs:102-124` — add an enum branch to the generic `ToSyntax<T>` fallback (currently throws `NotImplementedException` for enums).
- `src/Synto.Generators/Templating/Lifting/ValueLift.cs:81` — accept `TypeKind.Enum` on the `.ToSyntax()` path (so enums stop routing to the `[Runtime]` converter path).

**Modified — generator recognition/binding/emit:**
- `src/Synto.Generators/Templating/Builders/BuilderCallModel.cs:13-18` — add `BuilderArgKind.Constant`; document that a `Constant` binding carries its **pre-lifted** `ExpressionSyntax` in `ValueArgument`.
- `src/Synto.Generators/Templating/Builders/FacadeShape.cs:118-182` — recognize `[Constant]`, enforce mutual-exclusion with `[Quoted]`/`[TypeArg]`, reject `[Constant] params`, emit a facade value param typed `object` + `FacadeParam(..., BuilderArgKind.Constant, ...)`.
- `src/Synto.Generators/Templating/Builders/FacadeArgumentBinder.cs:17-81` — apply `ValueLift` to a `[Constant]` arg at bind time; store the lifted node on the binding; surface SY1008/SY1009.
- `src/Synto.Generators/Templating/Builders/FacadeCallFinder.cs` — construct/thread a `ValueLift` into `FacadeArgumentBinder.Bind` (it already resolves builder/marker symbols from the compilation).
- `src/Synto.Generators/Templating/Builders/BuilderStep.cs:39-50` — add a `BuilderArgKind.Constant` case that emits the pre-lifted `ValueArgument` verbatim.
- `src/Synto.Generators/Templating/Builders/SyntaxBuilderFacadeGenerator.cs` — verify a `[Constant]` param emits as a value param and does NOT count as "builder is its own facade".

**Modified — surface consumer + dog-food:**
- `src/Synto.Core/Templating/Builders/InjectedOnly/SyntoBuilders.cs:63` — `Case(ExpressionSyntax label, …)` → `Case([Constant] ExpressionSyntax label, …)`.
- `examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Generator/ReaderTemplate.cs:125` — `Case(LiteralExpression(NumericLiteralExpression, Literal(c.Ordinal)), …)` → `Case(c.Ordinal, …)`.

**Modified — tests + snapshots:**
- `test/Synto.Test/Templating/SyntaxBuilderTest.cs` — facade-derivation + lift generation tests for `[Constant]` (incl. an enum arg).
- `test/Synto.Test/Templating/RuntimeConverterTest.cs` — SY1008/SY1009 over a `[Constant]` param; flip any existing enum→SY1008 assertion to success.
- `test/Synto.Test/Templating/SwitchBuilderTest.cs:199,225,254` — migrate hand-built `Case` labels to raw values; re-accept the changed factory snapshots.
- `test/Synto.Test/Templating/InjectedSurfaceCompletenessTest.cs` — bind the new `ConstantAttribute`.
- `test/Synto.Test/snapshots/SurfaceInjectionTest.VerifyInjectedSurface#*.g.verified.cs` — re-accept (new attribute injected; `Case` shows `[Constant]`).
- `examples/…/Synto.Example.ObjectReader.Tests/snapshots/…#ObjectReader.g.verified.cs` — MUST stay byte-identical (verification, not edit).

---

## Task 1: `[Constant]` attribute surface + injection

**Files:**
- Create: `src/Synto.Core/Templating/Builders/Surface/ConstantAttribute.cs`
- Modify: `test/Synto.Test/Templating/InjectedSurfaceCompletenessTest.cs`
- Re-accept: `test/Synto.Test/snapshots/SurfaceInjectionTest.VerifyInjectedSurface#*.g.verified.cs`

**Interfaces:**
- Produces: `public sealed class Synto.Templating.ConstantAttribute : Attribute`, `[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]`, **no properties** (facade value param is fixed to `object`; an `As`-style override is deferred/YAGNI). Later tasks recognize it by `typeof(global::Synto.Templating.ConstantAttribute).FullName!`.

**Contract to satisfy:**
- Authored `public` in the Surface folder and injected `internal` into the consumer generator compilation by `SurfaceInjectionGenerator` with **no registry change** (a new type in the already-scanned Surface assembly — confirm the `<Compile Remove>` glob / injection type-list picks it up, exactly as `QuotedAttribute`/`TypeArgAttribute` are). XML doc states: marks a `[SyntaxBuilder]` parameter whose call argument is a factory-time **value** lifted to a constant `ExpressionSyntax` (the value-lift binding-time kind — the builder-arg counterpart of `[Quote]`), contrasted with `[Quoted]` (island quote) and unmarked (verbatim value).

- [ ] **Step 1: Confirm the injection surface** — read `SurfaceInjectionGenerator` and the Synto.Core `.csproj` `<Compile Remove>` for `Templating/Builders/Surface/**`; confirm a new attribute type there is injected automatically (like `TypeArgAttribute`). Note the mechanism in your commit message.

- [ ] **Step 2: Write the failing completeness assertion** — in `InjectedSurfaceCompletenessTest.cs`, add `ConstantAttribute` to whatever authoritative set the test binds/enumerates (mirror the `QuotedAttribute` entry). Run it and watch it fail.

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj --filter-class Synto.Test.Templating.InjectedSurfaceCompletenessTest`
Expected: FAIL (type not found).

- [ ] **Step 3: Create `ConstantAttribute.cs`** mirroring `QuotedAttribute.cs` (namespace `Synto.Templating`, same `AttributeUsage`, no properties, doc per the contract).

- [ ] **Step 4: Re-run completeness + accept the injected-surface snapshot** — verify the snapshot diff is ONLY the new injected attribute, then accept.

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj --filter-class Synto.Test.Templating.InjectedSurfaceCompletenessTest`
Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj --filter-class Synto.Test.Templating.SurfaceInjectionTest`
Expected: PASS after snapshot accept.

- [ ] **Step 5: Commit** — `feat(templating): add [Constant] builder-arg attribute to injected surface`.

---

## Task 2: `BuilderArgKind.Constant` + `FacadeShape` recognition

**Files:**
- Modify: `src/Synto.Generators/Templating/Builders/BuilderCallModel.cs:13-18` (enum) and `:20-39` (binding doc)
- Modify: `src/Synto.Generators/Templating/Builders/FacadeShape.cs:118-182` (per-parameter branch)
- Modify: `src/Synto.Generators/Templating/Builders/SyntaxBuilderFacadeGenerator.cs` (`FacadeMatchesBuilder` "own facade" check + value-param emission)
- Test: `test/Synto.Test/Templating/SyntaxBuilderTest.cs`

**Interfaces:**
- Consumes: `ConstantAttribute` (Task 1).
- Produces: `BuilderArgKind.Constant` enum member. `FacadeShape.Derive` maps a `[Constant]` builder parameter to `FacadeParam(p.Name, BuilderArgKind.Constant, genericParameterName: null, valueTypeDisplay: "object")`. The synthesized facade exposes that parameter as an `object`-typed value parameter, and a builder bearing a `[Constant]` param is NOT "its own facade" (a facade IS synthesized, exactly as for `[Quoted]`).

**Contract to satisfy:**
- Add `Constant` to `BuilderArgKind` (`BuilderCallModel.cs`), documented as: value-axis; the resolved binding carries the **already-lifted** `ExpressionSyntax` in `ValueArgument`.
- In `FacadeShape.Derive`, add a `[Constant]` attribute lookup next to the `[Quoted]`/`[TypeArg]` lookups (`:120-122`), enforce that `[Constant]` is **mutually exclusive** with both `[Quoted]` and `[TypeArg]` (extend the guard at `:124-136`), reject **`[Constant] params`** (mirror the `[Quoted] params` rejection at `:168-172`), and add the recognition branch (between the `quoted` branch `:163` and the unmarked fallthrough `:180`) appending `FacadeParam(p.Name, BuilderArgKind.Constant, null, "object")`.
- `SyntaxBuilderFacadeGenerator`: confirm the `[Constant]` value param emits as `object <name>` in the synthesized facade, and that `FacadeMatchesBuilder` (`:168-178`) does NOT treat a `[Constant]`-bearing builder as its own facade (it only short-circuits when ALL params are `Staged`).

- [ ] **Step 1: Write the failing facade-derivation test** — in `SyntaxBuilderTest.cs`, a probe `[SyntaxBuilder]` method with a `[Constant]` value parameter; assert the synthesized facade exposes that parameter typed `object` and the builder is facade-synthesized (not its-own-facade). Mirror an existing facade-shape/snapshot test's style.

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj --filter-class Synto.Test.Templating.SyntaxBuilderTest`
Expected: FAIL (`[Constant]` currently falls through to `Staged`, so the facade param is typed as the builder param type, not `object`).

- [ ] **Step 2: Implement** the enum member + `FacadeShape` recognition + mutual-exclusion/params guards + `SyntaxBuilderFacadeGenerator` verification.

- [ ] **Step 3: Run the test to green.**
Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj --filter-class Synto.Test.Templating.SyntaxBuilderTest`
Expected: PASS.

- [ ] **Step 4: Add + run the guard tests** — `[Constant]`+`[Quoted]` on one param → annotation error; `[Constant] params` → annotation error (mirror existing `[TypeArg]`+`[Quoted]` / `[Quoted] params` negative tests).
Expected: PASS.

- [ ] **Step 5: Commit** — `feat(templating): recognize [Constant] as a builder-arg binding-time kind`.

---

## Task 3: batteries-included value-lift — enum support (shared single source)

**Files:**
- Modify: `src/Synto.Core/Syntax/EmitHelpers/LiteralSyntaxExtensions.cs:102-124`
- Modify: `src/Synto.Generators/Templating/Lifting/ValueLift.cs:81`
- Create: `test/Synto.Test/…/LiteralSyntaxExtensionsTest.cs`
- Modify (if present): `test/Synto.Test/Templating/RuntimeConverterTest.cs` (flip enum→SY1008 to success)

**Interfaces:**
- Produces: `LiteralSyntaxExtensions.ToSyntax<T>` handles enum values → a qualified enum-member-access `ExpressionSyntax`. `ValueLift.TryEmitValueLift` accepts `TypeKind.Enum` on the `.ToSyntax()` path (returns `true`, no diagnostic). This is used by ALL lift sites (`[Quote]`, `Quote()`, `[Unquote]`, `[Constant]`).

**Contract to satisfy:**
- **Runtime helper** (`LiteralSyntaxExtensions.ToSyntax<T>`): before the `NotImplementedException`, add an enum branch. For an enum value:
  - If the value maps to a single named member (`Enum.GetName(value.GetType(), value) is { } name`), emit member-access `global::<FullyQualifiedEnumType>.<name>` (build the type name from `value.GetType().FullName`, `global::`-prefixed, `+`→`.` for nested types).
  - Otherwise (undefined value or flags combination), emit a cast of the underlying integral literal: `(global::<FullyQualifiedEnumType>)<underlyingValue.ToSyntax()>` (get the underlying value via `Convert.ChangeType(value, Enum.GetUnderlyingType(t))`, reuse the existing built-in `ToSyntax`).
  - The emitted node is a valid constant expression (usable as a `case` label).
- **Generation-time acceptance** (`ValueLift.TryEmitValueLift`, `:81`): broaden the fast-path condition from `valueType is ITypeParameterSymbol || IsBuiltInLiteralType(valueType)` to also include `valueType.TypeKind == TypeKind.Enum`. An enum then emits `valueAccess.ToSyntax()` (which binds to the generic `ToSyntax<T>` → the new enum branch at runtime) instead of routing to the `[Runtime]` converter path (which would wrongly report SY1008). Generic type parameters already flow through the generic `ToSyntax<T>`, so an enum bound to a `T` gets enum support for free.

- [ ] **Step 1: Write failing runtime-helper unit tests** — new `LiteralSyntaxExtensionsTest.cs`: `SampleEnum.Red.ToSyntax()` normalizes to the source text `global::<ns>.SampleEnum.Red`; a nested enum type resolves with `.` (not `+`); an undefined value `((SampleEnum)99).ToSyntax()` normalizes to `(global::<ns>.SampleEnum)99`; a `[Flags]` combination `(A | B).ToSyntax()` normalizes to the cast form. (Assert on `.NormalizeWhitespace().ToFullString()`.)

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj --filter-class <ns>.LiteralSyntaxExtensionsTest`
Expected: FAIL (`NotImplementedException` for enums).

- [ ] **Step 2: Implement the enum branch** in `ToSyntax<T>` per the contract. Run the unit tests to green.
Expected: PASS.

- [ ] **Step 3: Write a failing generator test** proving an enum flows through a lift site without SY1008 — e.g. a `[Template]` using `Quote(SampleEnum.Red)` (or `[Quote] SampleEnum` param) emits `SampleEnum.Red.ToSyntax()` in the factory (and the helper is injected), with NO diagnostic. (Pick the lift site with the least ceremony in the existing tests.)

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj --filter-class Synto.Test.Templating.RuntimeConverterTest`
Expected: FAIL (enum currently → SY1008).

- [ ] **Step 4: Implement the `ValueLift` acceptance** (`:81`). Run the generator test to green. Flip any pre-existing "enum → SY1008" assertion in `RuntimeConverterTest` to assert success.
Expected: PASS.

- [ ] **Step 5: Commit** — `feat(templating): batteries-include the value-lift with enum support`.

---

## Task 4: value-lift at the `[Constant]` binding seam

**Files:**
- Modify: `src/Synto.Generators/Templating/Builders/FacadeArgumentBinder.cs:17-81`
- Modify: `src/Synto.Generators/Templating/Builders/FacadeCallFinder.cs` (thread a `ValueLift` into `Bind`)
- Modify: `src/Synto.Generators/Templating/Builders/BuilderStep.cs:39-50` (emit the pre-lifted node)
- Test: `test/Synto.Test/Templating/SyntaxBuilderTest.cs`

**Interfaces:**
- Consumes: `BuilderArgKind.Constant` (Task 2); the batteries-included `ValueLift` (Task 3); `ValueLift.TryEmitValueLift(ITypeSymbol valueType, ExpressionSyntax valueAccess, Location? loc, out ExpressionSyntax lifted, out DiagnosticInfo? diag)`; `ValueLift` ctor `(SemanticModel, INamedTypeSymbol? runtimeAttribute, INamedTypeSymbol? expressionSyntaxSymbol)`.
- Produces: for a `[Constant]` parameter, `BuilderArgBinding.ValueArgument` holds the **lifted** expression (`value.ToSyntax()`, an enum-member access, or a fully-qualified `[Runtime]` converter call), so `BuilderStep` emits it directly.

**Contract to satisfy:**
- `FacadeArgumentBinder.Bind` gains access to a `ValueLift` (constructed by `FacadeCallFinder` from the compilation the same way `TemplateFactoryBuilder.InitValueLift` builds it — gathering the `RuntimeAttribute` + `ExpressionSyntax` symbols). Pass it into `Bind` (new parameter).
- When binding a `[Constant]` parameter's value argument, `Bind`:
  1. resolves the argument expression's type via `semanticModel.GetTypeInfo(argExpr).Type` (the **expression's own** type — `int` for `c.Ordinal`, `SampleEnum` for `SampleEnum.Red` — independent of the `object` facade param);
  2. calls `ValueLift.TryEmitValueLift(valueType, argExpr, argExpr.GetLocation(), out lifted, out diag)`;
  3. on success records `new BuilderArgBinding(BuilderArgKind.Constant, p.ParameterName, lifted, null)`;
  4. on failure adds `diag` to `diagnostics` and returns `null` (bail, consistent with the other binding-error returns).
- `BuilderStep.Quote` (`:39-50`) gains `case BuilderArgKind.Constant => binding.ValueArgument!` — emit the pre-lifted node **verbatim** (no `ctx.Quote`, no `NestedBuilderCallRewriter`).
- The `LiteralSyntaxExtensions` helper is injected into the generated document by the existing referenced-helper scan (because `.ToSyntax()` now appears in the emitted factory) — confirm it appears.

- [ ] **Step 1: Write the failing generation test** — a `[Template]` calling a `[Constant]`-param builder with (a) an `int` literal, (b) a `string`, (c) a `bool`, (d) an **enum** value, (e) a value of an inlined generic type parameter `T`. Assert the generated factory passes `(<value>).ToSyntax()` for that argument (the enum case rides the same `.ToSyntax()` shape) and the `ToSyntax` helper is injected.

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj --filter-class Synto.Test.Templating.SyntaxBuilderTest`
Expected: FAIL (today a `Constant` binding is emitted verbatim, so the factory embeds the raw value, not `(<value>).ToSyntax()`).

- [ ] **Step 2: Implement** the `ValueLift` threading (`FacadeCallFinder` → `Bind`), the lift in `Bind`, and the `BuilderStep` `Constant` case.

- [ ] **Step 3: Run the test to green.** Accept any new/changed factory snapshot after confirming the only change is `(<value>).ToSyntax()`.
Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj --filter-class Synto.Test.Templating.SyntaxBuilderTest`
Expected: PASS.

- [ ] **Step 4: Commit** — `feat(templating): lift [Constant] builder args via the shared value-lift`.

---

## Task 5: diagnostics SY1008 / SY1009 for `[Constant]`

**Files:**
- Test: `test/Synto.Test/Templating/RuntimeConverterTest.cs`

**Interfaces:**
- Consumes: the Task 4 bind-time lift; `TemplateDiagnostics.NoRuntimeConverter` (SY1008), `TemplateDiagnostics.AmbiguousRuntimeConverter` (SY1009).

**Contract to satisfy:**
- A `[Constant]` argument whose value is a **concrete non-built-in, non-enum** type with **no** `[Runtime]` `ToSyntax(this T)` converter reports **SY1008** at the argument location; with **more than one**, **SY1009**. Same descriptors, policy, and shape as the existing `[Quote]`/`Quote` cases — no new diagnostic. (Falls out of Task 4's reuse of `ValueLift`; this task pins it with tests.) Enums do NOT reach here (Task 3).

- [ ] **Step 1: Write the failing/locking diagnostic tests** — mirror the existing `Quote` SY1008/SY1009 cases in `RuntimeConverterTest.cs`, driven through a `[Constant]` builder argument over a custom **struct** (not enum) with zero converters (→ SY1008) and two converters (→ SY1009). Assert exact id + location.

Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj --filter-class Synto.Test.Templating.RuntimeConverterTest`
Expected: PASS if Task 4 wired `ValueLift` correctly. If FAIL, the lift isn't surfacing diagnostics through the binder's `diagnostics` list — fix Task 4's bail path.

- [ ] **Step 2: Commit** — `test(templating): pin SY1008/SY1009 for [Constant] builder args`.

---

## Task 6: adopt `[Constant]` on `Case`, migrate callers, verify byte-identical dog-food

**Files:**
- Modify: `src/Synto.Core/Templating/Builders/InjectedOnly/SyntoBuilders.cs:63`
- Modify: `examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Generator/ReaderTemplate.cs:125`
- Modify: `test/Synto.Test/Templating/SwitchBuilderTest.cs:199,225,254`
- Re-accept: `test/Synto.Test/Templating/snapshots/SwitchBuilderTest.*.verified.cs` (factory source — benign change)
- Re-accept: `test/Synto.Test/snapshots/SurfaceInjectionTest.VerifyInjectedSurface#SyntoBuilders.g.verified.cs` (`Case` now shows `[Constant]`)
- Verify byte-identical: `examples/…/Synto.Example.ObjectReader.Tests/snapshots/…#ObjectReader.g.verified.cs`

**Interfaces:**
- Consumes: everything from Tasks 1–5.

**Contract to satisfy:**
- `Case`'s `label` parameter becomes `[Constant] ExpressionSyntax label`. Its synthesized facade value param is `object`, so authors pass a factory-time value (`c.Ordinal`, or an enum) directly; a hand-built `ExpressionSyntax` passed as a value would hit SY1008. So **all** `Case` label call sites must migrate to raw values. This narrows `Case` to value-liftable labels — literals and enums (the spec's binding-time note §3.2: a builder wanting a staged/`[Quoted]` label form too would expose two overloads — out of scope here).
- Every current `Case` label is a numeric literal (int) → all migrate cleanly:
  - `SwitchBuilderTest.cs:199` `Case(LiteralExpression(NumericLiteralExpression, Literal(0)), …)` → `Case(0, …)`
  - `SwitchBuilderTest.cs:225,254` `Case(LiteralExpression(NumericLiteralExpression, Literal(c.Ordinal)), …)` → `Case(c.Ordinal, …)`
  - `ReaderTemplate.cs:125` `Case(LiteralExpression(NumericLiteralExpression, Literal(c.Ordinal)), …)` → `Case(c.Ordinal, …)`
- **The ObjectReader final reader output is byte-identical**: `c.Ordinal.ToSyntax()` produces the same `LiteralExpression(NumericLiteralExpression, Literal(<ordinal>))` node at factory runtime the hand form built. `#ObjectReader.g.verified.cs` MUST NOT change. The `SwitchBuilderTest.*` factory-source snapshots DO change (`LiteralExpression(…)` → `(…).ToSyntax()`) — benign re-accepts.

- [ ] **Step 1: Mark the builder** — edit `SyntoBuilders.cs:63` to `Case([Constant] ExpressionSyntax label, params IEnumerable<StatementSyntax> body)`. Update the design comment block (`:43-49`) about `Case`'s label from "already-built constant `ExpressionSyntax` … marking them `[Quoted]` would quote the projection variable out of output scope" to reflect that `label` is now `[Constant]` (value lifted to a constant; a live projection var referenced inside stays liveness-shielded).

- [ ] **Step 2: Migrate the three test call sites** (`SwitchBuilderTest.cs:199,225,254`) to raw values.

- [ ] **Step 3: Run switch-builder tests; accept the factory-source snapshot deltas** after confirming each delta is only `LiteralExpression(NumericLiteralExpression, Literal(x))` → `(x).ToSyntax()`. Re-accept the `SyntoBuilders.g.verified.cs` injected-surface snapshot; run `SurfaceInjectionTest` green.
Run: `dotnet test --project test/Synto.Test/Synto.Test.csproj --filter-class Synto.Test.Templating.SwitchBuilderTest`
Expected: PASS after accept.

- [ ] **Step 4: Migrate the dog-food** — edit `ReaderTemplate.cs:125` to `Case(c.Ordinal, …)`.

- [ ] **Step 5: Verify byte-identical ObjectReader output + full dog-food green.** Confirm `#ObjectReader.g.verified.cs` is UNCHANGED (no snapshot diff). If it changed, the lift is not producing the identical node — stop and diagnose (do not accept).
Run: `dotnet test --project examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Tests/Synto.Example.ObjectReader.Tests.csproj`
Expected: PASS, `#ObjectReader.g.verified.cs` untouched.

- [ ] **Step 6: Full green gate** — build + full test + format.
Run: `dotnet build` then `dotnet test` (solution) then `dotnet format whitespace --verify-no-changes`
Expected: all green.

- [ ] **Step 7: Commit** — `feat(templating): adopt [Constant] on Case; migrate ObjectReader TypedGetter labels`.

---

## Self-Review

**Spec coverage (§3.2 + superseded facade spec's testing list + batteries-included ruling):**
- `[Constant]` as 4th builder-arg kind → Tasks 1, 2, 4. ✔
- Shared `ValueLift`, `value.ToSyntax()` / `[Runtime]` converter → Task 4 (Global Constraint: single-source). ✔
- **Batteries-included: enums** (`Color.Red` → `global::Ns.Color.Red`), single-source, all lift sites → Task 3. ✔
- SY1008/SY1009 (stale "SY1011/SY1012" corrected; enums excluded) → Task 5. ✔
- No standalone `Constant<T>()` facade → not built (design note Task 1). ✔
- First consumer `Case([Constant] …)` + ObjectReader `Case(c.Ordinal, …)` byte-identical → Task 6. ✔
- Injected-surface completeness + snapshot → Tasks 1 & 6. ✔
- Coverage: int/string/bool/generic-param/enum in the `[Constant]` lift test → Task 4 Step 1. ✔

**Type consistency:** `BuilderArgKind.Constant` (Task 2) used identically in `FacadeShape` (Task 2), `FacadeArgumentBinder` (Task 4), `BuilderStep` (Task 4). `BuilderArgBinding.ValueArgument` carries the lifted node for `Constant`. Facade value type `"object"` consistent (Tasks 2, 6). `ValueLift.TryEmitValueLift` signature consistent (Tasks 3, 4).

**Open verification the implementer must confirm (checkpoint, not gap):** that lifting in `FacadeArgumentBinder.Bind` and storing on the binding causes `BuilderStep` to emit the lifted node in a **staged builder-arg position** nested in `Switch` (the spec's "one open seam"). Task 4's generation test + Task 6's byte-identical ObjectReader check together lock this. Fallback if Task 4 Step 1 fails to show `(x).ToSyntax()` at the `Case` label: thread the `ValueLift` into `BuilderStep` and lift at quote time (needs `SemanticModel` + `Diagnostics` on `QuoteContext` — heavier; prefer the bind-time seam).

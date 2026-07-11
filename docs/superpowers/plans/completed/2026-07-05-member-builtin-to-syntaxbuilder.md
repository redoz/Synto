# Move the built-in `Member` facade onto the standard `[SyntaxBuilder]` path (retire half the hardwired built-in machinery)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Delete the hand-authored `Template.Member` facade + its hardwired recognition arm and let `Member`
ride the same `[SyntaxBuilder]`-discovery + facade-synthesis path every new builder uses. `TypeOf` stays on the
hardwired path **deliberately** (it cannot ride the standard path — see below); this plan replaces its
"built-in / legacy" framing with a load-bearing rationale comment so the remaining special case is understood,
not mistaken for debt.

**Architecture:** Today two built-ins (`Member`, `TypeOf`) are recognized by *binding* to inert facades on
`Template` and rewritten via a hardwired `switch` in `FacadeCallFinder.TryBuiltIn`. Every *new* builder instead
carries `[SyntaxBuilder]`, is discovered by `SyntaxBuilderRegistry`, gets an inert facade synthesized by
`SyntaxBuilderFacadeGenerator`, and is recognized *structurally* (unbound simple name) by the same
`FacadeCallFinder`. `Member` can move to the structural path unchanged; `TypeOf` cannot (proven below).

**Tech Stack:** Roslyn incremental source generators; C#; Verify snapshot tests; jj + `integrate.sh` green-gate.

## Why `Member` migrates but `TypeOf` cannot (the design fact this plan encodes)

Both built-in facades exist because a built-in builder returns `ExpressionSyntax`/`TypeSyntax` (a syntax node)
while the *authoring* call site needs a different carrier type. The escape hatch differs per builder:

- **`Member`** — builder is `Member([Quoted] ExpressionSyntax instance, string name)`. The `[Quoted]` first
  parameter is the shield: the author passes a **real value** (`_e.Current`, an `object`), which does **not**
  bind to a builder whose first parameter is `ExpressionSyntax`. So the call is UNBOUND → the structural path
  claims it → it binds the synthesized generic facade `TResult Member<TResult>(object instance, string name)`.
  The generic `TResult` is genuinely the accessed member's carrier type (`Member<object>(...)`), so the
  synthesized facade is equivalent to the hand-authored `Member<TValue>(object, string)`. **Clean migration.**

- **`TypeOf`** — builder is `TypeOf(string name) → ExpressionSyntax`, used in **value position** requiring
  `System.Type` (`System.Type GetFieldType() => … return TypeOf(c.ColumnTypeName);`). It has **no shield**: the
  author passes a `string`, which binds directly to the `string`-parameter builder (returns `ExpressionSyntax`,
  breaks the `System.Type` return). Pinning a non-generic `System.Type TypeOf(string)` facade would collide with
  the builder by **return-type-only difference (CS0111)**; the synthesized generic `TResult TypeOf<TResult>(string)`
  is out-competed by the exact non-generic builder overload. The hand-authored `Template.TypeOf` dodges both by
  (a) pinning the `System.Type` return and (b) living on `Template`, a *different type* than the `SyntoBuilders`
  builder. The type name is a **live/staged value** (`c.ColumnTypeName` from column metadata), so `[TypeArg]` is
  also impossible. `TypeOf` therefore stays hardwired; fully retiring it needs a separate feature (facade
  return-type pin + alternate facade host) that is **out of scope here**.

## The contract (what "done" means)

- `SyntoBuilders.Member` carries `[SyntaxBuilder]` and is discovered/synthesized/recognized like any user builder.
- `Template.Member<TValue>` is **deleted**; `FacadeCallFinder` no longer special-cases `Member`.
- Every existing `Member<T>(...)` authoring site (ObjectReader + tests) resolves against the **synthesized**
  `SyntoBuilders.Member<TResult>` facade and produces **the same generated output** (the emit target is still the
  fully-qualified `global::Synto.Templating.SyntoBuilders.Member(...)`, so `Factory.*.g` snapshots stay
  byte-identical; only the injected-surface `Template.g` / `SyntoBuilders.g` snapshots move).
- All `Member` behaviors are preserved through the structural path: member-access emit, the named-argument
  diagnostic (SY1029 `NamedArgumentOnFacade`), and the injected-internal zero-collision property.
- `TypeOf` behavior is **unchanged**; only its comments change (rationale, not "legacy").
- `FacadeCallFinder.TryBuiltIn` / `IsBuiltInFacadeName` / the built-in branch retain **only** the `TypeOf` arm.

## Global Constraints

- **jj-native, off `main`.** All work lands on bookmark `experimental/member-syntaxbuilder`. Never move `main`
  ([[experimental-features-own-branch]]).
- **No git hooks.** Format via `dotnet format whitespace`; CI/green-gate is the enforcement ([[no-git-pre-commit-hooks]]).
- **Test invocation (MTP / SDK 10):** raw `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug`.
  Not via rtk. Filter (if needed) via `--filter-method` / `--filter-class`, NOT `--filter` ([[dotnet-test-mtp-invocation]]).
- **`Member` emit is unchanged.** The synthesized facade and the hardwired arm both emit the same
  fully-qualified `SyntoBuilders.Member(...)` call, so every `Factory.*.g.verified.cs` stays byte-identical. If
  one moves, treat it as a regression to diagnose, not a snapshot to re-accept. The only *expected* snapshot
  moves are the two injected-surface files (`Template.g`, `SyntoBuilders.g`).
- **Recognition parity.** The structural path must claim exactly the calls the hardwired arm claimed — no more
  (do not hijack an unrelated `Member(...)` that binds to a real user method) and no less.

## Orientation (read before starting)

Surface (runtime, `src/Synto.Core/Templating/`):
- `Builders/InjectedOnly/SyntoBuilders.cs:20` — `Member([Quoted] ExpressionSyntax instance, string name)` (add
  `[SyntaxBuilder]`). `:24` — `TypeOf(string name)` (unchanged). The class doc `:8-16` calls both "hardwired" —
  update to say `Member` is now standard, `TypeOf` alone is hardwired (+ why).
- `Staging/Surface/Template.cs:61-71` — the `Member<TValue>(object instance, string name)` inert facade
  (**delete**). `:73-80` — the `TypeOf(string name)` inert facade (**keep**; refresh its doc). Class doc `:3-9`.

Generator recognition (`src/Synto.Generators/Templating/Builders/FacadeCallFinder.cs`):
- `:24` — `BuiltInBuilderType` const (still needed by the `TypeOf` arm; keep).
- `:61-126` — `VisitInvocationExpression`. `:64-83` is the built-in (bound-to-`Template`) branch; it stays but
  now only matches `TypeOf`. `:90-123` is the structural user-builder branch that will claim `Member`.
- `:128-150` — `TryBuiltIn`: **remove the `Member` arm (`:133-139`)**; keep the `TypeOf` arm (`:141-145`).
- `:182-184` — `IsBuiltInFacadeName`: **narrow to `TypeOf` only** (drop the `Member` disjunct).
- Discovery/synthesis that pick `Member` up once it has `[SyntaxBuilder]`:
  `Builders/SyntaxBuilderRegistry.cs:18` (`FindBuilders`), `Builders/SyntaxBuilderFacadeGenerator.cs:61` (`Build`)
  → synthesizes `internal static TResult Member<TResult>(object instance, string name) => default!;`,
  `Builders/FacadeArgumentBinder.cs:17` (`Bind`) → resolves `builderType = SyntoBuilders` (`:79`, same emit
  target as the hardwired arm), `Builders/FacadeShape.cs` (`[Quoted]` → `object` param; `ExpressionSyntax`
  return → fresh `TResult`).

Consumer dog-food (`examples/Synto.Example.ObjectReader/…/ReaderTemplate.cs`):
- `:97` — `(object?)Member<object>(_e.Current, c.Name) ?? DBNull.Value`. `:126` — `Member<TRet>(_e.Current, c.Name)`.
  Both are `using static SyntoBuilders` bare `Member<...>` calls (see the note at `:82`); after `Template.Member`
  is deleted they must resolve against the synthesized `SyntoBuilders.Member<TResult>` facade. No source change
  expected — verify the example still carrier-compiles and its snapshot is unchanged.

Tests (`test/Synto.Test/Templating/SyntaxBuilderTest.cs`):
- `:101,111` — `Member_WithConstantName_EmitsMemberAccess` (`Member<object>(instance, "Name")`), snapshot
  `snapshots/SyntaxBuilderTest.Member_WithConstantName_EmitsMemberAccess#Factory.Build.g.verified.cs` (expect
  byte-identical). `:488` — zero-collision test. `:558` — named-argument SY1029 test (`Member<object>(instance:
  …, name: …)`). `:757` — hint-name / `StartsWith("Synto")` test. Comments at `:9,474,545,740` describe `Member`
  as "built-in"/"`Template.Member`" — update the wording where it now flows the structural path.
- `test/Synto.Test/Templating/InjectedSurfaceCompletenessTest.cs` — compiles consumer templates against the
  injected surface; the mechanical proof the moved `Member` still binds.
- Surface snapshots to re-accept: `test/Synto.Test/snapshots/SurfaceInjectionTest.VerifyInjectedSurface#Template.g.verified.cs`
  (loses `Member`) and `#SyntoBuilders.g.verified.cs` (gains `[SyntaxBuilder]` on `Member`).

KB to refresh afterward (naming only, via the `refresh-kb` skill — not a plan task):
`docs/kb/subsystems/templating.md:138,142` (Member listed as a `Template` inline facade), `docs/kb/glossary.md`.

---

## Task 1 — Move `Member` onto the `[SyntaxBuilder]` path (behavior-preserving; green)

Add the marker, delete the hand-authored facade, and remove the hardwired `Member` recognition — in one unit,
because leaving any two of the three in place either double-processes the call or leaves dead code.

- [ ] **Test first.** In `SyntaxBuilderTest`, assert `Member` now flows the *structural* path: keep
  `Member_WithConstantName_EmitsMemberAccess` green (member-access emit unchanged) and keep the named-argument
  case (`Member<object>(instance: …, name: …)`) reporting **SY1029** (`NamedArgumentOnFacade`) — it must fire
  from the structural branch (`FacadeCallFinder:104`), not the deleted built-in branch. Run the relevant tests
  red against the not-yet-changed code only where a new assertion is added; otherwise this task is proven by the
  existing suite staying green after the change.
- [ ] In `SyntoBuilders.cs`, add `[SyntaxBuilder]` to `Member` (`:20`). Update the class doc (`:8-16`) so it says
  `Member` is discovered like a user builder and **only** `TypeOf` remains hardwired.
- [ ] In `Template.cs`, **delete** the `Member<TValue>` facade (`:61-71`). Leave `TypeOf` (`:73-80`) untouched.
  If the class doc names `Member` as built-in, drop that mention.
- [ ] In `FacadeCallFinder.cs`, remove the `Member` arm from `TryBuiltIn` (`:133-139`) and narrow
  `IsBuiltInFacadeName` (`:182-184`) to `TypeOf` only. Leave the `TypeOf` arm, `BuiltInBuilderType`, and the
  built-in branch structure intact. Confirm the structural branch (`:90-123`) claims `Member`: it is registered
  in `_buildersByName` (now that it has `[SyntaxBuilder]`), the authoring calls are UNBOUND (`Template.Member`
  gone, synthesized facade absent from the analyzed compilation), so the unbound-name rule admits them.
- [ ] **Gate:** `dotnet test --project test/Synto.Test/Synto.Test.csproj -c Debug` green; ObjectReader example
  builds and its snapshot test is green (no source change to `ReaderTemplate.cs`). Confirm every `Factory.*.g`
  snapshot — including `SyntaxBuilderTest.…Member…#Factory.Build.g` — is **byte-identical**. Re-accept exactly
  the two injected-surface snapshots (`Template.g` loses `Member`; `SyntoBuilders.g` gains the attribute).
  `dotnet format whitespace --verify-no-changes` clean. Commit.

## Task 2 — Re-document `TypeOf`'s deliberate special status (doc-only; green)

`TypeOf` stays hardwired; make the code say **why**, so the remaining special case is not read as leftover debt.

- [ ] In `SyntoBuilders.cs` (`TypeOf`, `:23-24`), `Template.cs` (`TypeOf` facade, `:73-80`), and
  `FacadeCallFinder.cs` (the `TypeOf` arm + built-in branch), replace any "built-in"/"legacy"/"hand-authored
  counterpart" phrasing that implies temporariness with the rationale from the "Why … cannot" section: `TypeOf`
  must return `System.Type` in value position while its builder returns `ExpressionSyntax`; a same-signature
  `System.Type` facade on `SyntoBuilders` is CS0111 against the builder; the type name is a live value so
  `[TypeArg]` is impossible; hence the `System.Type`-pinned facade hosted on `Template`. Reference `Member` as
  the migrated contrast (its `[Quoted]` shield is what let it move).
- [ ] **Gate:** build clean; full `Synto.Test` green; **no** snapshot moves (comments only);
  `dotnet format whitespace --verify-no-changes` clean. Commit.

---

## Verification (whole change)

- `SurfaceInjectionTest.VerifyInjectedSurface#Template.g.verified.cs` no longer contains `Member`;
  `#SyntoBuilders.g.verified.cs` shows `[SyntaxBuilder]` on `Member`.
- `InjectedSurfaceCompletenessTest` compiles the consumer templates against the injected surface — proves the
  moved `Member` binds structurally.
- Full `Synto.Test` green; **all** `Factory.*.g` snapshots byte-identical (Member emit target unchanged);
  ObjectReader example green with an unchanged snapshot.
- `FacadeCallFinder` special-cases only `TypeOf` (grep: no `Member` disjunct in `IsBuiltInFacadeName`, no
  `Member` arm in `TryBuiltIn`); `Template` has no `Member` facade member.
- `TypeOf` untouched behaviorally; its comments now state the load-bearing rationale.
- `dotnet format whitespace --verify-no-changes` clean.
- Follow-up (not gated by this plan): run `refresh-kb` to drop `Member` from the `Template`-inline-facade list in
  `docs/kb/subsystems/templating.md` and the glossary.

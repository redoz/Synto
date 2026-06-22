# Synto generator-author utilities — injected cacheability toolkit + interceptor helper (design)

- **Date:** 2026-06-22
- **Status:** Approved — design. To be implemented on `experimental/object-reader` (the dog-food line where
  the friction was found and the validating example lives). Not on main.
- **Builds on:** the **surface-injection** mechanism (`SurfaceInjectionGenerator`, which auto-injects every
  embedded `Synto.Runtime.*` resource as `internal`) and the **shapes** of Synto's existing internal
  cacheability types (`src/Synto.SourceGenerator/EquatableArray.cs`, `DiagnosticInfo.cs`), which the
  injectable copies mirror. Validated by the **ObjectReader example** (`examples/Synto.Example.ObjectReader/`).
- **Tracking:** First items off the ObjectReader friction log
  (`docs/superpowers/notes/2026-06-22-objectreader-synto-friction.md`) — findings **#2** (cacheability
  toolkit) and **#5** (interceptor helper). Findings **#1/#3/#4** (list/repeater hole, shell parameters,
  template-hole marker) and the **`Synto.Diagnostics` cacheable-support** question are explicitly **separate
  future specs** (see §8).

## 1. Motivation

Building the ObjectReader generator surfaced that Synto **hides its own generator-author primitives**. A
cacheable incremental generator must flow value-equatable state (the C-5 discipline), so every author
re-rolls the same types. The example hand-rolled `EquatableArray<T>` (~60 lines), `LocationInfo`, and a
`DiagnosticInfo` carrier — with comments literally pointing at the friction — and hand-emitted the
`InterceptsLocationAttribute` definition the SDK doesn't ship. Synto *already owns* the first three
internally (in `Synto.SourceGenerator`); they are simply not injected to consumers.

This delivers two small, additive, **injected** generator-author utilities and proves them by deleting the
example's hand-rolled copies (the close-the-loop validation, §7). It changes **no** templating/matching
semantics and touches **no** consumer output shape.

## 2. Background — the injection mechanism (load-bearing)

`SurfaceInjectionGenerator` (`src/Synto.SourceGenerator`) reads **every** embedded manifest resource named
`Synto.Runtime.*`, parses it, rewrites top-level **`public`→`internal`** (`PublicToInternalRewriter`), and
emits it at `RegisterPostInitializationOutput` into the consumer's compilation. New injectable surface =
author a file + add one `<EmbeddedResource … LogicalName="Synto.Runtime.X.cs">` to
`Synto.SourceGenerator.csproj`; it is then picked up automatically. The rewriter only flips `public`; an
**already-`internal`** type injects verbatim — so the existing internal cacheability types can be injected
as-is.

Synto's existing internal primitives — the canonical **shape** the injectable copies mirror (we author fresh injectable copies rather than relocate these, because relocating collides in `Synto.Test`; see D1):

```csharp
// src/Synto.SourceGenerator/EquatableArray.cs  (namespace Synto)
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
    where T : IEquatable<T> { /* ImmutableArray-backed; Empty; structural Equals/GetHashCode */ }

// src/Synto.SourceGenerator/DiagnosticInfo.cs  (namespace Synto)
internal record struct LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
{ public Location ToLocation(); public static LocationInfo? CreateFrom(Location?); }

internal record struct DiagnosticInfo(DiagnosticDescriptor Descriptor, LocationInfo? Location, EquatableArray<string> MessageArgs)
{ public Diagnostic ToDiagnostic(); }   // descriptor in → Diagnostic out; self-contained
```

## 3. Design decisions

- **D1 — Single-sourced injectable surface, authored in `src/Synto`.** Author the injectable
  `EquatableArray<T>` / `LocationInfo` / `DiagnosticInfo` once in `src/Synto/Generators/` (namespace
  `Synto.Generators`), `<Compile Remove>`d from `Synto.Core` and embedded — the **same single-source pattern
  every other injected marker uses** (markers live once in `src/Synto`, embedded). `Synto.SourceGenerator`'s
  **internal** copies (namespace `Synto`) are **left untouched**: relocating + injecting *them* collides in
  `Synto.Test`, which both *consumes* the injected surface **and** unit-tests internal pipeline types
  (`TemplateGenerationResult` / `MatchGenerationResult`) that carry `EquatableArray<DiagnosticInfo>` — making
  two same-named types from different assemblies that won't bridge (a hard compile error). The **injected**
  surface is single-sourced; we deliberately do **not** also de-dupe Synto's private pipeline copy (that is
  the architecture doc's separate deferred smell). The injectable copy mirrors the canonical internal shape
  (§2/§4).
- **D2 — Dedicated namespace, unconditional injection.** The injected toolkit lives in a **new namespace
  `Synto.Generators`** and is injected unconditionally as `internal` (like the markers — no
  `SurfaceInjectionGenerator` change). A fresh namespace means it only enters name resolution when an
  author writes `using Synto.Generators;`, so a templating-only consumer never sees it and the
  most-commonly-rolled name (`EquatableArray<T>`) does not clash by default. Accepted cost: the source's
  namespace moves `Synto` → `Synto.Generators`, so `Synto.SourceGenerator`'s own references update (a
  mechanical rename the build verifies — D6).
- **D3 — Interceptor helper is the attribute definition only.** A new `Synto.Generators.Interceptors`
  helper owns the canonical, BCL-only `System.Runtime.CompilerServices.InterceptsLocationAttribute`
  definition (which the SDK does not ship and the version-stamped `[InterceptsLocation]` binds to), exposed
  as a one-call `AddDefinition(...)`. The interceptor **method body** — the generic-arity shell and the
  `(object)` cast that bridge `Create<T>` to a concrete reader — stay **bespoke** (they construct the
  author's specialized type); generalizing them is a non-goal (§8).
- **D4 — `Synto.Diagnostics` is untouched (no back door).** `DiagnosticInfo` takes **any**
  `DiagnosticDescriptor`; it does not depend on `Synto.Diagnostics`. We do **not** expose
  `Synto.Diagnostics`' private generated descriptor. Whether `Synto.Diagnostics` should natively support the
  cacheable/incremental path is a deliberate **future** design, not a field-accessibility flip slipped in
  here (§8).
- **D5 — Prove by deletion (close the loop).** The ObjectReader example deletes its hand-rolled
  `EquatableArray<T>` + `LocationInfo` and its `InterceptsLocationAttribute` const, and consumes
  `Synto.Generators.*`. Its enum-keyed diagnostic carrier is **renamed** off `DiagnosticInfo` (to
  `PendingDiagnostic`, avoiding a clash with the injected `Synto.Generators.DiagnosticInfo`) and **kept** as
  a stopgap with a pointer to the deferred `Synto.Diagnostics` work. Validation = the example still builds
  green and all its tests pass with the boilerplate gone.
- **D6 — Internal duplicates all left as-is.** No internal copy is relocated. This adds one dedicated
  **injectable** copy in `src/Synto/Generators`; `Synto.SourceGenerator` / `Synto.Diagnostics` /
  `Synto.Bootstrap` keep their own internal copies. Fully collapsing the internal duplication is the
  architecture doc's separately-deferred smell, **out of scope** here.

## 4. Surface — what gets injected (namespace `Synto.Generators`, `internal` in consumers)

```csharp
namespace Synto.Generators;

// new in src/Synto/Generators/EquatableArray.cs (mirrors the internal shape), embedded Synto.Runtime.EquatableArray.cs
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
    where T : IEquatable<T> { public static readonly EquatableArray<T> Empty; /* …mirror of the internal copy… */ }

// new in src/Synto/Generators/Diagnostics.cs (mirrors the internal shapes), embedded Synto.Runtime.Diagnostics.cs
internal record struct LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
{ public Location ToLocation(); public static LocationInfo? CreateFrom(Location? location); }

internal record struct DiagnosticInfo(DiagnosticDescriptor Descriptor, LocationInfo? Location, EquatableArray<string> MessageArgs)
{ public Diagnostic ToDiagnostic(); }

// new in src/Synto/Generators/Interceptors.cs, embedded Synto.Runtime.Interceptors.cs
internal static class Interceptors
{
    public const string AttributeHintName = "InterceptsLocationAttribute.g.cs";
    public const string AttributeSource;                 // the canonical BCL-only InterceptsLocationAttribute definition
    public static void AddDefinition(SourceProductionContext context);              // context.AddSource(AttributeHintName, AttributeSource)
    public static void AddDefinition(IncrementalGeneratorPostInitializationContext context); // post-init overload (the natural home for a constant)
}
```

(These are authored `public` where new / left `internal` where relocated; the rewriter normalizes everything
to `internal` on injection. `Interceptors` references `SourceProductionContext` /
`IncrementalGeneratorPostInitializationContext` — available in any consumer that references the Roslyn
analyzer surface.)

## 5. The mechanism (mirrors the marker pattern)

- **Author** new files in `src/Synto/Generators/` (namespace `Synto.Generators`), authored `public`:
  `EquatableArray.cs`, `Diagnostics.cs` (holding `LocationInfo` + `DiagnosticInfo`), `Interceptors.cs`. The
  injectable `EquatableArray` / `LocationInfo` / `DiagnosticInfo` mirror the canonical internal shapes (§2);
  `Interceptors` is new.
- **`<Compile Remove>`** the three files from `src/Synto/Synto.csproj` (embed-only — injected surface, not
  `Synto.Core` runtime API; mirrors the existing `MatchReplaceExtensions.cs` treatment). They are
  type-checked when injected, proven by the self-containment test (§9).
- **Embed** all three as `<EmbeddedResource Include="Generators\…"
  LogicalName="Synto.Runtime.{EquatableArray,Diagnostics,Interceptors}.cs" />` in
  `src/Synto.SourceGenerator/Synto.SourceGenerator.csproj` (next to the existing `Synto.Runtime.*` markers;
  the Include path reaches `..\Synto\Generators\…`). `SurfaceInjectionGenerator` discovers and injects them
  automatically, rewriting `public`→`internal`.
- **`Synto.SourceGenerator`'s internal `EquatableArray` / `DiagnosticInfo` are not touched** (no relocation).

## 6. Contracts / invariants

- **C-1 — Single-sourced injected surface.** Exactly one authored copy of each *injectable* type, in
  `src/Synto/Generators`, embed-only + embedded for injection (D1). (Synto's internal pipeline copies are a
  separate, pre-existing concern, untouched.)
- **C-2 — Self-contained on `netstandard2.0`.** The injected toolkit compiles in a consumer's ns2.0
  generator with no `Synto.*` runtime-package reference — only BCL + the Roslyn surface the consumer already
  references. `record struct` `init` requires `IsExternalInit`, which the injected surface already provides
  (confirm; add to the injected surface if missing). Proven by a self-containment test mirroring the
  existing `CompileInjected*OnNetStandard20` proofs.
- **C-3 — Collision-safe by default.** Injected only into `Synto.Generators`; a consumer that does not
  `using Synto.Generators;` is unaffected. A clash is possible only if an author opts in **and** declares a
  same-named type — their choice to resolve (CS0104), never a default break.
- **C-4 — No semantic / output-shape change.** No templating/matching behavior changes; no consumer
  *generated output* changes (the toolkit is generator-side state, never emitted into consumer code). The
  injected-surface snapshots gain new goldens; no existing golden's meaning changes.
- **C-5 — `Synto.Diagnostics` untouched.** No change to the `[Diagnostic]` generator or its output (D4).
- **C-6 — Example proves removal.** The example builds green and all its tests pass with its hand-rolled
  `EquatableArray`/`LocationInfo`/`InterceptsLocationAttribute` deleted (D5).

## 7. Close the loop — ObjectReader example refactor

- `Model.cs`: delete the hand-rolled `EquatableArray<T>` (~60 lines) and `LocationInfo`; add
  `using Synto.Generators;`. Keep `ColumnInfo`, `DiagnosticKind`, `ObjectReaderModel`. Rename the enum-keyed
  carrier `DiagnosticInfo` → `PendingDiagnostic` (avoids clash with the injected `Synto.Generators.DiagnosticInfo`),
  with a `// TODO` pointing at the deferred `Synto.Diagnostics` cacheable-support spec.
- `ObjectReaderGenerator.cs`: delete the `InterceptsLocationAttributeSource` const (~16 lines) and replace
  the `spc.AddSource("InterceptsLocationAttribute.g.cs", …)` with `Synto.Generators.Interceptors.AddDefinition(spc)`.
- **Unchanged (honest non-goals this round):** the `DiagnosticKind`→factory `switch` (deferred
  `Synto.Diagnostics` work), the interceptor body's `(object)` double-cast, and the list-driven switch-arms
  in raw `SyntaxFactory` (friction #1). Net ≈ **90 lines of plumbing** leave the example.
- The example's snapshot goldens must be **unchanged** (the refactor changes how the generator is *built*,
  not what it *emits*) — an unchanged snapshot is part of the proof.

## 8. Scope / non-goals

- **In scope:** inject `EquatableArray<T>` + `LocationInfo` + `DiagnosticInfo` (relocated single source) and
  a new `Interceptors` attribute-definition helper, all into `Synto.Generators`; refactor the ObjectReader
  example onto them; self-containment + injected-surface snapshot tests.
- **Non-goals (separate future specs):**
  - **`Synto.Diagnostics` cacheable support** — native incremental/equatable diagnostics (e.g. exposing a
    descriptor by design, or emitting an equatable carrier). The deferred decision behind D4; the example's
    `PendingDiagnostic` enum carrier stays until then.
  - **Interceptor method/body generation** — the generic-arity shell + `(object)` cast (friction #5 tail).
  - **List/repeater template hole** (friction #1), **emitted-shell parameters** (#3), **template-hole
    marker** (#4).
  - **Collapsing the other internal duplicates** (`Synto.Diagnostics` / `Synto.Bootstrap` copies — D6).
  - Any new **public** `Synto.Core` runtime API (the toolkit is injected `internal`, not runtime surface).

## 9. Testing

- **Self-containment (C-2):** a Synto.Test proof compiling the injected `EquatableArray` + `DiagnosticInfo`
  (+ `LocationInfo`) + `Interceptors` on the faithful `netstandard2.0` closure with zero errors (mirror
  `CompileInjected*OnNetStandard20`).
- **Injected-surface snapshots (C-4):** new goldens `SurfaceInjectionTest…#EquatableArray.g.verified.cs`,
  `#DiagnosticInfo.g.verified.cs`, `#Interceptors.g.verified.cs` — each the `internal`-rewritten authored
  source. Confirm no existing injected golden changes meaning.
- **Synto core suite stays green (the collision check):** no internal relocation, so the injectable copy
  (namespace `Synto.Generators`) does **not** collide with the internal copy (namespace `Synto`) in
  `Synto.Test` — `Synto.Test` resolves `EquatableArray` via its enclosing `Synto` namespace and the injected
  `Synto.Generators` copy stays dormant. `PipelineEquatabilityTests` (which bridges `EquatableArray` with the
  internal `TemplateGenerationResult`/`MatchGenerationResult`) must still build and pass unchanged.
- **Example (C-6):** after the §7 refactor, `dotnet build` 0 errors and all example tests pass; the example
  generator output **snapshot is unchanged**.
- **Green gate:** `dotnet build -c Debug` (0 errors) · `dotnet test --no-build -c Debug` (all green; **no**
  `--nologo`) · `dotnet format whitespace --verify-no-changes`.

## 10. Branch / VCS

New work on **`experimental/object-reader`** (current tip; the example lives there and is refactored in the
same change). **jj-native**, never main, no `jj git push`; the operator advances the bookmark. Conventional
Commits, no AI footer.

## 11. Resolved decisions

- **Single source (resolved, revised).** Author the injectable `EquatableArray`/`LocationInfo`/
  `DiagnosticInfo` once in `src/Synto/Generators` (embed-only), mirroring the canonical internal shapes;
  **leave Synto's internal copies untouched** — relocating them collides in `Synto.Test` (D1). The injected
  surface is single-sourced; de-duping the internal copy is explicitly out of scope (D6).
- **Namespace (resolved).** Dedicated `Synto.Generators`, unconditional `internal` injection, accepting the
  mechanical internal rename (D2/D6).
- **`Synto.Diagnostics` (resolved).** Untouched — no descriptor back door; native cacheable support is a
  separate future spec (D4/§8).
- **Interceptor helper (resolved).** Attribute-definition helper only; method/body stays bespoke (D3/§8).
- **Validation (resolved).** Prove by deleting the example's hand-rolled copies; example stays green and its
  output snapshot is unchanged (D5/C-6).

# Synto Architecture — Domain Context for Reviewers

Shared domain context for every reviewer, planner, and evaluator agent. Read this
before reading source files. It is the single source of truth for "how Synto is
built"; the workflow scripts and skills reference this file instead of duplicating it.

Synto ("**SYN**tax **TO**olkit") is a set of **Roslyn incremental source generators**
that quote and template C# syntax trees — you write ordinary C#, a generator parses it
and emits the `SyntaxFactory` calls that rebuild it, so you can compose syntax in your
own generators without hand-writing the factory soup. It is **experimental** and mid
packaging-redesign. The shape is deliberately small: a **build-time library plus
generators** — there is **no runtime service, no database, no network, no process**. It
runs inside the C# compiler.

**Templating** is the first capability and the only one that ships today; **Matching**
(match on syntax) is next, and lands as a sibling feature folder/namespace inside the
same umbrella generator, not a new package.

The paradigm:

- **`IIncrementalGenerator`** is the execution model. The load-bearing concern is
  **incremental cacheability**: everything that flows through a pipeline stage must be an
  **equatable value type**, and the pipeline must **never capture `Compilation`,
  `ISymbol`, or `SyntaxNode`** into cached state — doing so re-roots the compilation in
  memory and makes the generator re-run on every keystroke. `TemplateFactorySourceGenerator`
  does *all* semantic work inside the `ForAttributeWithMetadataName` transform and flows
  out an equatable `TemplateGenerationResult` (generated text + an `EquatableArray<DiagnosticInfo>`).
- **Roslyn** (`Microsoft.CodeAnalysis.CSharp`, **5.0 floor**) is the only real
  dependency. Quoting is done by `CSharpSyntaxQuoter`; output is normalized
  (`NormalizeWhitespace`) and run through `SyntaxFormatter` before emission.
- **Packaging is source-injection.** The shipped `Synto` generator package carries the
  generator + `Synto.dll` under `analyzers/dotnet/cs` (analysis-time only — Roslyn does
  not load an analyzer's NuGet deps into the analyzer load context). It injects the small
  consumer-facing **marker surface** as `internal` source via
  `SurfaceInjectionGenerator.RegisterPostInitializationOutput`, and emits the runtime
  **helpers** that actually appear in generated code as `file static class` copies into
  the one generated file that uses them (`FileLocalHelpers`). Net result: a consumer's
  generated code is **self-contained with no runtime package dependency**.
- **Verify snapshot tests + `CSharpGeneratorDriver`** verify generated-code correctness.
  Each scenario builds an in-memory `CSharpCompilation`, runs the generator through a
  `CSharpGeneratorDriver`, and asserts the emitted `*.g.cs` against a golden
  `*.verified.cs`. These snapshots are the executable spec of the output shape.
- **Self-host bootstrap.** `src/Synto` consumes `src/Synto.Bootstrap` as an analyzer
  (`OutputItemType="Analyzer"`) to generate part of its own quoter — Synto is built with a
  generator of itself. A clean build must produce Bootstrap before `Synto`.
- Tech baseline: **.NET 10 SDK** (`global.json` pins `10.0.300`, `rollForward: disable`),
  **netstandard2.0** for every generator and the runtime, **Roslyn 5.0** baseline (a
  generator compiled against 5.0 loads on any host Roslyn ≥ 5.0), **central package
  management** (`Directory.Packages.props`), shared packaging logic in
  `Directory.Build.targets` gated on `<IsSyntoGeneratorPackage>`, **MTP v2** test platform,
  and **jj (Jujutsu)** over the git repo (git commands still work; the repo may sit on a
  detached HEAD).

## Project layering (load-bearing)

Four projects under `src/`; only **three ship** (`Synto.Core`, `Synto`, `Synto.Diagnostics`).
The boundary that matters is **runtime vs generator** and **consumer-facing surface vs
generator-internal helpers**:

- **`src/Synto`** → ships as **`Synto.Core`** (`netstandard2.0`, `IsPackable=true`). The
  runtime/toolkit library: the **marker surface** (`TemplateAttribute`, `InlineAttribute`,
  `RuntimeAttribute`, `TemplateOption`, `Syntax`/`Syntax<T>`), the emitted **helpers**
  (`ToSyntax`/`LiteralSyntaxExtensions`, `ToTypeSyntax`/`RuntimeTypeExtensions`,
  `OrNullLiteralExpression`/`QuoteSyntaxExtensions`), and **generator-internal** machinery
  the quoter uses (`CSharpSyntaxQuoter`, `Formatting/SyntaxFormatter`, `UsingDirectiveSet`,
  `SymbolExtensions`, the list extensions). It is the authoritative source for the markers
  and helpers; the generator package embeds those files as resources, never re-declares them.
- **`src/Synto.SourceGenerator`** → ships as **`Synto`**. The **umbrella generator**, all
  features as folders/namespaces (`Templating/` today, `Matching/` next). Holds
  `SurfaceInjectionGenerator` (injects the markers as `internal`), `TemplateFactorySourceGenerator`
  + `FileLocalHelpers` (emits helpers as `file`-scoped copies), the equatable pipeline
  models (`TemplateInfo`, `TemplateGenerationResult`, `DiagnosticInfo`, `EquatableArray`),
  and the hand-written `Diagnostics` descriptors (`SY0000`, `SY1001`–`SY1009`).
- **`src/Synto.Bootstrap`** → **not shipped**. The self-host generator that emits part of
  `src/Synto`'s quoter (`CSharpSyntaxQuoter.Generated.cs`).
- **`src/Synto.Diagnostics`** → ships as the separate, **dev-only** package
  **`Synto.Diagnostics`** — a general-purpose `[Diagnostic]`-to-`DiagnosticDescriptor`
  generator for *any* Roslyn author. Its generated output is self-contained (it injects its
  own `DiagnosticAttribute` and calls no Synto runtime helper), so it has no runtime dependency.

The injected surface is `internal` (not `public`) so it can't pollute a consumer's public
API or collide when several Synto-consuming assemblies are referenced together; the emitted
helpers are `file`-scoped so a private copy can never clash with `Synto.Core`'s public copies
(the CS0121 ambiguity an injected `internal` copy would cause). The single source of truth is
the embedded-resource pipeline: marker/helper `.cs` files are authored once in `src/Synto`,
embedded into the generator (`Synto.Runtime.*` / `Synto.Helper.*` resources), parsed, and
rewritten (`public`→`internal` for markers, `public`→`file` for helpers) at inject time.

## Evaluate within this paradigm

Don't flag idiomatic `IIncrementalGenerator` / Roslyn / `SyntaxFactory` usage as issues,
but **do** flag misuse of the platform or violations of the layering above:

- **breaking incremental caching** — capturing `Compilation`, `ISymbol`, `SemanticModel`,
  or `SyntaxNode` into pipeline state, or flowing a non-equatable type through a provider
  (the cache compares by value; a captured symbol roots the compilation and re-runs the
  generator on every edit);
- **generator-internal types leaking into the injected consumer surface or generated
  output** — `CSharpSyntaxQuoter`, `SyntaxFormatter`, `UsingDirectiveSet`, `SymbolExtensions`,
  list extensions must stay 0-occurrence in consumer output; only the five markers and the
  three file-local helpers belong there;
- **drifting the single source of truth** — re-declaring a marker/helper in the generator
  instead of embedding the `src/Synto` file, or the two **duplicated quoter/helper sources**
  still linked between `src/Synto` and `src/Synto.Bootstrap` drifting apart (a known,
  deferred smell — prefer a shared/linked file so they can't diverge);
- **exceptions escaping the generator on a reachable path** — generation failures must be
  converted to a `DiagnosticInfo` (e.g. `Diagnostics.InternalError`), not thrown; an
  unguarded `!`, `.Result`, or `First()` on input-dependent data is the equivalent of a
  raw `.unwrap`;
- **widening the surface** — flipping the injected markers from `internal` to `public`,
  making a file-local helper non-`file`-scoped, or adding a forced **runtime package
  dependency** to the `Synto` generator package (consumers must stay dependency-free);
- **package-identity / output-shape drift** — renaming `Synto` / `Synto.Core` /
  `Synto.Diagnostics`, or changing the generated factory's name/signature/namespace that a
  consumer's code binds to (the snapshots pin this — an unexplained snapshot change is a
  finding, not a rubber stamp);
- secrets (`NUGET_API_KEY`, `GITHUB_TOKEN`, any `*_TOKEN` / `*_SECRET` / `*_KEY` /
  bearer token) committed to source or emitted into generated code or logs.

## Codebase navigation

Read the relevant context before reading source:

1. **`README.md`** (repo root) — what Synto is and the quickest example.
2. **The doc-comments are the design.** `SurfaceInjectionGenerator`, `FileLocalHelpers`,
   and `TemplateFactorySourceGenerator` carry long `<remarks>` explaining the injection
   model, the `internal`-vs-`file` choice, and the cacheability discipline. Read them first.
3. **The snapshot tests** under `test/Synto.Test/Templating/snapshots/` (and the
   `SurfaceInjectionTest` / `InjectedSurfaceCompletenessTest` / `ZeroCollisionTest`
   snapshots) are the authoritative spec of the generated output shape.
4. **Build wiring** — `Directory.Build.targets` (analyzer packaging, gated on
   `IsSyntoGeneratorPackage`), `Directory.Build.props`, `Directory.Packages.props`,
   `global.json`, and each `*.csproj`'s `<EmbeddedResource>` items.

Design decisions live in the doc-comments, and the implementation — together with the
verified snapshots — is expected to match them.

## Evidence requirement

For every **critical** and **high** finding, include the relevant code snippet
(3–10 lines). A finding without quoted code is unverifiable and will be rejected
during verification.

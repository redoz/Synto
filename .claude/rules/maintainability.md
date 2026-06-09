# Maintainability

Evaluate whether the code is easy to understand, modify, and extend.

## Checklist

### Abstractions & Boundaries
- Is the **layering intact**? This is the primary maintainability contract. The
  consumer-facing surface (markers `TemplateAttribute`/`InlineAttribute`/`RuntimeAttribute`,
  `TemplateOption`, `Syntax`/`Syntax<T>`, and the two emitted helpers
  `OrNullLiteralExpression`/`ToTypeSyntax`) must stay separate from **generator-internal**
  machinery (`CSharpSyntaxQuoter`, `SyntaxFormatter`, `UsingDirectiveSet`,
  `SymbolExtensions`, the list extensions). Internals must never leak into
  consumer-generated output (0 occurrences is the bar).
- Are the **runtime vs generator** boundaries respected? `src/Synto` (→ `Synto.Core`, the
  runtime/toolkit surface) holds no generator pipeline logic; `src/Synto.SourceGenerator`
  (→ `Synto`, the umbrella generator) holds the pipeline; `src/Synto.Bootstrap` is
  self-host-only and never ships. No project reaches into another's internals.
- Are there leaky abstractions where generator implementation details bleed through? (a
  `Compilation`/`ISymbol`/`SyntaxNode` escaping into a pipeline model — which is also a
  caching bug; a quoter-internal type appearing in the injected surface)
- Does the folder/namespace structure match the toolkit's feature decomposition?
  (`Templating/` today, `Matching/` next, each a self-contained feature area inside the one
  umbrella generator)

### Placement & Structure
- Is logic placed where it belongs (templating logic under `Templating/`, formatting in
  `Formatting/`, the marker/helper surface in `src/Synto`), or where it was convenient?
- Are there types or sources that **duplicate the same concept**? Known smell: the
  quoter/helper sources linked into both `src/Synto` and `src/Synto.Bootstrap`
  (`LiteralSyntaxExtensions.cs`, `UsingDirectiveSet.cs`, `Formatting/SyntaxFormatter.cs`) —
  two physical copies that can drift from what the quoter emits. Injected helpers should
  have a **single source of truth**.
- Are surface types defined in the right project/namespace? (consumer markers in
  `src/Synto/Templating`, not buried in the generator)
- Is the project layout appropriate for "one umbrella generator, features as folders"
  rather than a package-per-feature sprawl?

### Design Quality
- Are the domain types well-designed? (`TemplateOption` granularity —
  `None`/`Single`/`Bare`; the equatable pipeline shapes
  `TemplateInfo`/`DiagnosticInfo`/`EquatableArray<T>`/`LocationInfo`; markers carrying just
  enough to drive emission)
- Is naming clear and consistent? (`Synto` the generator package vs `Synto.Core` the
  runtime vs `Synto.Diagnostics`; `*Attribute` markers; feature namespaces
  `Synto.Templating.*` / `Synto.Matching.*`)
- Is there dead code, commented-out code, or obsolete modules? (large blocks of
  commented-out template tests, abandoned `[Unquote]`-era experiments)

### Change Resistance
- Does the code resist change? (a `SyntaxKind` assumption hardcoded in one emission path;
  magic strings for attribute metadata names that would need updating in several places;
  the injected surface duplicated rather than sourced once)
- Could a new requirement be added without rewriting? Would a **second feature —
  `Matching`** — fit cleanly as a new folder/namespace with its own injected
  markers/helpers behind the same `RegisterPostInitializationOutput` wiring, or would it
  force templating-specific assumptions into shared code? Would **dogfooding
  `Synto.Diagnostics`** (replacing the hand-written `SY*` descriptors with `[Diagnostic]`
  partials) slot in via an analyzer reference, or ripple across the build?
- Are there parts everyone is afraid to touch? Why? (the quoter; the bootstrap chain)

## Codebase Patterns

- **Layering is the contract**: runtime surface (`src/Synto` → `Synto.Core`) vs umbrella
  generator (`src/Synto.SourceGenerator` → `Synto`) vs self-host bootstrap
  (`src/Synto.Bootstrap`). Most smells are violations of this split, or of the
  consumer-surface / generator-internal divide.
- **Umbrella generator, features as folders**: all syntax capabilities live in one
  generator project as folders/namespaces (`Templating/`, `Matching/` next). Consumers add
  **one** package; new features do **not** become new packages.
- **Packaging = source-injection**: the generator injects the small consumer surface as
  `internal`, file-scoped-namespace **source** via `RegisterPostInitializationOutput` (the
  same pattern `DiagnosticsGenerator` already uses), so consumer output has **no runtime
  package dependency**. Generator-internal helpers never appear in that surface.
- **Single source of truth for injected helpers**: the emitted helpers
  (`OrNullLiteralExpression`, `ToTypeSyntax`) and the quoter that emits calls to them
  should share one definition, not drift across linked copies.
- **Self-host bootstrap**: `src/Synto` consumes `Synto.Bootstrap` as an analyzer to
  generate its own quoter — the toolkit builds itself.

## Scope Guidance

- **Full evaluation**: Review the whole project graph (runtime / generator / bootstrap /
  diagnostics), the feature-folder decomposition inside the generator, the consumer-surface
  vs generator-internal divide, and the injected-surface single-source-of-truth.
- **Change review**: Focus on whether the changes respect the runtime/generator and
  surface/internal boundaries, introduce new coupling or a second copy of a concept, or
  place code in the wrong layer.

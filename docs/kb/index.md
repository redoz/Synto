# Synto knowledge base

A navigation map of Synto's internals, authored as an [OKF](/SPEC.md) bundle.
This is the *where/what* layer — start here, drill into a concept, follow its
links. The *why/how-to-evaluate* layer is `.claude/playbook/`.

New here? Read [conventions](/conventions.md) (the type registry) and the
[glossary](/glossary.md) first, then [architecture/layering](/architecture/layering.md).

# Meta

* [Conventions & type registry](/conventions.md) - the fixed types + frontmatter contract for this bundle.
* [Glossary](/glossary.md) - Quote / Unquote / Splice / Staged / … → their canonical defining file.
* [OKF spec](/SPEC.md) - the format this bundle conforms to.

# Architecture

* [Layering](/architecture/layering.md) - runtime vs generator, and which way dependencies point.
* [Bootstrap loop](/architecture/bootstrap.md) - the self-hosting quoter that generates itself.
* [Incremental pipeline](/architecture/incremental-pipeline.md) - tracked steps and the cacheability contract.

# Projects

* [Synto (runtime)](/projects/synto-runtime.md) - consumer surface + quoter machinery.
* [Synto.SourceGenerator](/projects/synto-sourcegenerator.md) - the primary generators (templates, matching, builders).
* [Synto.Bootstrap](/projects/synto-bootstrap.md) - build-time generator of the runtime quoter.
* [Synto.Diagnostics](/projects/synto-diagnostics.md) - `[Diagnostic]` descriptor generator.

# Subsystems

* [Quoting](/subsystems/quoting.md) - turn a syntax tree into the code that rebuilds it.
* [Templating](/subsystems/templating.md) - `[Template]` → factory, the staged-binding pipeline.
* [Matching](/subsystems/matching.md) - pattern-match / capture / replace over syntax.
* [Decorations](/subsystems/decorations.md) - post-quote declaration markers (Identifier/Visibility/Sealed/…).

# Examples

* [ObjectReader](/examples/objectreader.md) - zero-reflection `IDataReader` generation; the dog-food friction probe.

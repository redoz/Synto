# Projects

The four buildable assemblies under `src/`. They look alike on disk but occupy
distinct [layers](/architecture/layering.md) — open the right one on the first try.

* [Synto (runtime)](/projects/synto-runtime.md) - `runtime` - consumer markers, extensions, and the quoter.
* [Synto.SourceGenerator](/projects/synto-sourcegenerator.md) - `generator` - templates, matching, syntax-builder facades, surface injection.
* [Synto.Bootstrap](/projects/synto-bootstrap.md) - `bootstrap` - build-time generator of the runtime quoter.
* [Synto.Diagnostics](/projects/synto-diagnostics.md) - `diagnostics` - `[Diagnostic]` descriptor generator.

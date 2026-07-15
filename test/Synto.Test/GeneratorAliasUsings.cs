extern alias SyntoGenerators;
extern alias SyntoIncremental;

// The generator assembly is referenced through the `SyntoGenerators` extern alias (see Synto.Test.csproj)
// so its metadata copies of the pipeline value types stay out of the default scope and can never collide
// with the internal source copies SurfaceInjectionGenerator injects into this compilation under the same
// `Synto.Generators` namespace. Re-expose the generator's internal namespaces globally VIA the alias so
// test code binds the SHIPPED (compiled) types — replacing the per-file `using Synto.Generators.*;`
// directives, which would otherwise fail (the metadata namespaces are no longer in the global alias).
global using SyntoGenerators::Synto.Generators;
// The cacheable trio (EquatableArray/DiagnosticInfo/LocationInfo) authored under `namespace
// Synto.Generators` now lives in Synto.Incremental.dll (Task 4); expose it via the SyntoIncremental
// alias so test code binds the SHIPPED (compiled) trio. The two assemblies' `Synto.Generators` type
// sets are disjoint, so re-exposing both aliases' namespace is unambiguous.
global using SyntoIncremental::Synto.Generators;
global using SyntoIncremental::Synto.Generators.Incremental;
global using SyntoGenerators::Synto.Generators.Symbols;
global using SyntoGenerators::Synto.Generators.Matching;
global using SyntoGenerators::Synto.Generators.Matching.Discovery;
global using SyntoGenerators::Synto.Generators.Matching.Emit;
global using SyntoGenerators::Synto.Generators.Matching.Diagnostics;
global using SyntoGenerators::Synto.Generators.Templating;
global using SyntoGenerators::Synto.Generators.Templating.Discovery;
global using SyntoGenerators::Synto.Generators.Templating.BindingTime;
global using SyntoGenerators::Synto.Generators.Templating.Quoting;
global using SyntoGenerators::Synto.Generators.Templating.Quoting.Passes;
global using SyntoGenerators::Synto.Generators.Templating.Staging;
global using SyntoGenerators::Synto.Generators.Templating.Lifting;
global using SyntoGenerators::Synto.Generators.Templating.Builders;
global using SyntoGenerators::Synto.Generators.Templating.Decorations;
global using SyntoGenerators::Synto.Generators.Templating.Emit;
global using SyntoGenerators::Synto.Generators.Templating.Prelude;
global using SyntoGenerators::Synto.Generators.Templating.Diagnostics;

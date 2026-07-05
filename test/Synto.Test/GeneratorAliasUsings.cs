extern alias SyntoGenerators;

// The generator assembly is referenced through the `SyntoGenerators` extern alias (see Synto.Test.csproj)
// so its metadata copies of the pipeline value types stay out of the default scope and can never collide
// with the internal source copies SurfaceInjectionGenerator injects into this compilation under the same
// `Synto.Generators` namespace. Re-expose the generator's internal namespaces globally VIA the alias so
// test code binds the SHIPPED (compiled) types — replacing the per-file `using Synto.Generators.*;`
// directives, which would otherwise fail (the metadata namespaces are no longer in the global alias).
global using SyntoGenerators::Synto.Generators;
global using SyntoGenerators::Synto.Generators.Matching;
global using SyntoGenerators::Synto.Generators.Templating;

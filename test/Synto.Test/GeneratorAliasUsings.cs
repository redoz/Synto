extern alias SyntoIncremental;
extern alias SyntoTemplate;
extern alias SyntoMatch;

// The umbrella (Synto.Generators.dll) is reference-only after Task 10 — its former SurfaceInjectionGenerator
// (the sole type in its `Synto.Generators` namespace) was deleted, so there is nothing to re-expose from it;
// every pipeline type now lives in a feature DLL, aliased below.
// The cacheable trio (EquatableArray/DiagnosticInfo/LocationInfo) authored under `namespace
// Synto.Generators` now lives in Synto.Incremental.dll (Task 4); expose it via the SyntoIncremental
// alias so test code binds the SHIPPED (compiled) trio. The two assemblies' `Synto.Generators` type
// sets are disjoint, so re-exposing both aliases' namespace is unambiguous.
global using SyntoIncremental::Synto.Generators;
global using SyntoIncremental::Synto.Generators.Incremental;
// Synto.Generators.Symbols + the whole Synto.Generators.Templating.* tree moved into Synto.Template.dll
// (Task 7); reach them through the SyntoTemplate alias so test code binds the SHIPPED (compiled) types.
global using SyntoTemplate::Synto.Generators.Symbols;
// Synto.Generators.Matching.* moved into Synto.Match.dll (Task 8); reach it through the SyntoMatch alias so
// test code binds the SHIPPED (compiled) types (incl. MatchSurfaceInjector).
global using SyntoMatch::Synto.Generators.Matching;
global using SyntoMatch::Synto.Generators.Matching.Discovery;
global using SyntoMatch::Synto.Generators.Matching.Emit;
global using SyntoMatch::Synto.Generators.Matching.Diagnostics;
global using SyntoTemplate::Synto.Generators.Templating;
global using SyntoTemplate::Synto.Generators.Templating.Discovery;
global using SyntoTemplate::Synto.Generators.Templating.BindingTime;
global using SyntoTemplate::Synto.Generators.Templating.Quoting;
global using SyntoTemplate::Synto.Generators.Templating.Quoting.Passes;
global using SyntoTemplate::Synto.Generators.Templating.Staging;
global using SyntoTemplate::Synto.Generators.Templating.Lifting;
global using SyntoTemplate::Synto.Generators.Templating.Builders;
global using SyntoTemplate::Synto.Generators.Templating.Decorations;
global using SyntoTemplate::Synto.Generators.Templating.Emit;
global using SyntoTemplate::Synto.Generators.Templating.Prelude;
global using SyntoTemplate::Synto.Generators.Templating.Diagnostics;

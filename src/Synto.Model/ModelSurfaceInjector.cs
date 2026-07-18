using Microsoft.CodeAnalysis;
using Synto.GenKit;

namespace Synto.Generators.Model;

/// <summary>
/// Injects the Model feature's consumer-facing surface embedded in <c>Synto.Model.dll</c> (the
/// <c>Synto.Runtime.*</c> resources: <c>SyntoModelAttribute</c>, <c>SyntoModel&lt;T&gt;</c>, <c>Project</c>)
/// as <c>internal</c> source into the consumer's compilation. It injects ONLY the Model surface — disjoint
/// from the cacheable trio (owned by <c>IncrementalSurfaceInjector</c>) and from the Template / Match /
/// Diagnostics surface sets — so N feature injectors stay collision-free (CS0101) in one consumer
/// compilation. The mechanism lives in <see cref="SurfaceInjectorBase"/>; this concrete subclass only scans
/// this assembly's embedded surface.
/// </summary>
[Generator(LanguageNames.CSharp)]
internal sealed class ModelSurfaceInjector : SurfaceInjectorBase
{
}

using Microsoft.CodeAnalysis;
using Synto.GenKit;

namespace Synto.Generators.Templating;

/// <summary>
/// Injects the Template feature's consumer-facing marker surface embedded in <c>Synto.Template.dll</c>
/// (the <c>Synto.Runtime.*</c> resources) as <c>internal</c> source into the consumer's compilation. It
/// injects ONLY the Template markers — disjoint from the cacheable trio (owned by
/// <c>IncrementalSurfaceInjector</c>) and from the Match / Diagnostics marker sets — so N feature injectors
/// stay collision-free (CS0101) in one consumer compilation. The mechanism lives in
/// <see cref="SurfaceInjectorBase"/>; this concrete subclass only scans this assembly's embedded surface.
/// </summary>
[Generator(LanguageNames.CSharp)]
internal sealed class TemplateSurfaceInjector : SurfaceInjectorBase
{
}

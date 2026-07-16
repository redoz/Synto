using Microsoft.CodeAnalysis;
using Synto.GenKit;

namespace Synto.Generators.Matching;

/// <summary>
/// Injects the Match feature's consumer-facing marker surface embedded in <c>Synto.Match.dll</c>
/// (the <c>Synto.Runtime.*</c> resources) as <c>internal</c> source into the consumer's compilation. It
/// injects ONLY the Match markers — disjoint from the cacheable trio (owned by
/// <c>IncrementalSurfaceInjector</c>) and from the Template / Diagnostics marker sets — so N feature
/// injectors stay collision-free (CS0101) in one consumer compilation. The mechanism lives in
/// <see cref="SurfaceInjectorBase"/>; this concrete subclass only scans this assembly's embedded surface.
/// </summary>
[Generator(LanguageNames.CSharp)]
internal sealed class MatchSurfaceInjector : SurfaceInjectorBase
{
}

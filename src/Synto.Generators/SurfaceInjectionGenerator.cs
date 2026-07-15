using Microsoft.CodeAnalysis;
using Synto.GenKit;

namespace Synto.Generators;

/// <summary>
/// Injects the consumer-facing Synto marker surface embedded in <c>Synto.Generators.dll</c> (the
/// <c>Synto.Runtime.*</c> resources) as <c>internal</c> source into the consumer's compilation. The
/// mechanism lives in <see cref="SurfaceInjectorBase"/>; this concrete subclass exists only to scan
/// this assembly's embedded surface and to carry the <c>[Generator]</c> attribute.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class SurfaceInjectionGenerator : SurfaceInjectorBase
{
}

using Microsoft.CodeAnalysis;
using Synto.GenKit;

namespace Synto.Generators.Incremental;

/// <summary>
/// Injects the cacheable trio (<c>EquatableArray&lt;T&gt;</c> / <c>DiagnosticInfo</c> / <c>LocationInfo</c>)
/// into consumer compilations. This is the SINGLE injector of the trio across the whole Synto package — no
/// feature DLL embeds or injects it — which is what keeps N feature-marker injectors collision-free (CS0101)
/// in one consumer compilation. It scans its own assembly (Synto.Incremental) for the embedded
/// <c>Synto.Runtime.*</c> trio resources via <see cref="SurfaceInjectorBase"/>.
/// </summary>
[Generator]
public sealed class IncrementalSurfaceInjector : SurfaceInjectorBase { }

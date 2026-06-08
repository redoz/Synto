extern alias SyntoCore;

using System.Reflection;

namespace Synto.Test;

/// <summary>
/// Provides access to the public <c>Synto.Core</c> runtime assembly, which is referenced through the
/// <c>SyntoCore</c> extern alias (see Synto.Test.csproj). <c>SurfaceInjectionGenerator</c> injects an internal
/// copy of the Synto surface into this compilation, so the unqualified <c>Synto.*</c> names resolve to
/// those injected types; the in-memory generator tests, however, need the PUBLIC, referenceable
/// surface as a metadata reference for the compilations they build. This helper bridges the alias so
/// callers do not each need their own <c>extern alias</c> directive.
/// </summary>
internal static class SyntoCoreAssembly
{
    /// <summary>The public Synto.Core assembly (the one that declares the public marker types).</summary>
    public static Assembly Assembly { get; } = typeof(SyntoCore::Synto.TemplateAttribute).Assembly;

    /// <summary>On-disk location of <see cref="Assembly"/>.</summary>
    public static string Location { get; } = Assembly.Location;
}

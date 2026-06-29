using Microsoft.CodeAnalysis;
using Synto.Templating;

namespace Synto;

/// <summary>
/// Resolves the built-in decoration marker symbols ONCE per template (via
/// <see cref="Compilation.GetTypeByMetadataName(string)"/>) so <see cref="DecorationFinder"/> can classify a
/// declaration's attributes by <see cref="SymbolEqualityComparer.Default"/> — never by string-matching. Mirrors
/// <c>Matching/MatchMarkers.cs</c>: the bound built-ins are looked up directly; the generic
/// <c>[Implements&lt;T&gt;]</c> / <c>[Inherits&lt;T&gt;]</c> are looked up via their unbound metadata name
/// (<c>typeof(X&lt;&gt;).FullName</c>) and reduced with <see cref="INamedTypeSymbol.ConstructUnboundGenericType"/>
/// so a closed usage matches by comparing its own <c>ConstructUnboundGenericType()</c>.
/// <para>
/// <see cref="Resolve"/> returns <c>null</c> when the decoration surface isn't referenced by the compilation
/// (no decorations are possible), letting the transform skip all decoration work with zero behavior change.
/// Pure symbol resolution done INSIDE the <c>GenerateTemplate</c> transform — nothing is captured into cached
/// pipeline state.
/// </para>
/// </summary>
internal sealed class DecorationMarkers
{
    private DecorationMarkers(
        INamedTypeSymbol identifier,
        INamedTypeSymbol visibility,
        INamedTypeSymbol @sealed,
        INamedTypeSymbol implementsUnbound,
        INamedTypeSymbol inheritsUnbound,
        INamedTypeSymbol accessEnum)
    {
        Identifier = identifier;
        Visibility = visibility;
        Sealed = @sealed;
        ImplementsUnbound = implementsUnbound;
        InheritsUnbound = inheritsUnbound;
        AccessEnum = accessEnum;
    }

    /// <summary><c>Synto.Templating.IdentifierAttribute</c> (no-arg).</summary>
    public INamedTypeSymbol Identifier { get; }

    /// <summary><c>Synto.Templating.VisibilityAttribute</c> (<c>Access</c> ctor arg).</summary>
    public INamedTypeSymbol Visibility { get; }

    /// <summary><c>Synto.Templating.SealedAttribute</c> (no-arg).</summary>
    public INamedTypeSymbol Sealed { get; }

    /// <summary>The UNBOUND <c>Synto.Templating.ImplementsAttribute&lt;&gt;</c>.</summary>
    public INamedTypeSymbol ImplementsUnbound { get; }

    /// <summary>The UNBOUND <c>Synto.Templating.InheritsAttribute&lt;&gt;</c>.</summary>
    public INamedTypeSymbol InheritsUnbound { get; }

    /// <summary>The <c>Synto.Templating.Access</c> enum (for rendering <c>[Visibility]</c>'s ctor arg).</summary>
    public INamedTypeSymbol AccessEnum { get; }

    public static DecorationMarkers? Resolve(Compilation compilation)
    {
        var identifier = compilation.GetTypeByMetadataName(typeof(IdentifierAttribute).FullName!);
        var visibility = compilation.GetTypeByMetadataName(typeof(VisibilityAttribute).FullName!);
        var @sealed = compilation.GetTypeByMetadataName(typeof(SealedAttribute).FullName!);
        var implementsUnbound = compilation
            .GetTypeByMetadataName(typeof(ImplementsAttribute<>).FullName!)?
            .ConstructUnboundGenericType();
        var inheritsUnbound = compilation
            .GetTypeByMetadataName(typeof(InheritsAttribute<>).FullName!)?
            .ConstructUnboundGenericType();
        var accessEnum = compilation.GetTypeByMetadataName(typeof(Access).FullName!);

        // The surface ships as one unit, so any single missing symbol means the decoration surface isn't
        // referenced — return null and let the transform skip decoration discovery entirely.
        if (identifier is null
            || visibility is null
            || @sealed is null
            || implementsUnbound is null
            || inheritsUnbound is null
            || accessEnum is null)
        {
            return null;
        }

        return new DecorationMarkers(identifier, visibility, @sealed, implementsUnbound, inheritsUnbound, accessEnum);
    }
}

using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Synto;

/// <summary>
/// Shared symbol-metadata reads (attribute lookup + named-argument extraction) used by the
/// generator's discovery passes. Internal-only generator helper — never injected into output.
/// </summary>
internal static class SymbolMetadataExtensions
{
    public static bool HasAttribute(ISymbol symbol, INamedTypeSymbol attribute)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(attr.AttributeClass, attribute))
                return true;
        }

        return false;
    }

    public static AttributeData? FindAttribute(ISymbol symbol, string attributeFullName)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == attributeFullName)
                return attr;
        }

        return null;
    }

    public static bool GetNamedBool(AttributeData attr, string name)
    {
        foreach (var kv in attr.NamedArguments)
        {
            if (kv.Key == name && kv.Value.Value is bool b)
                return b;
        }

        return false;
    }

    public static string? GetNamedTypeDisplay(AttributeData attr, string name)
    {
        foreach (var kv in attr.NamedArguments)
        {
            if (kv.Key == name && kv.Value.Value is ITypeSymbol t)
                return t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        return null;
    }

    /// <summary>
    /// All source-declared top-level named types in <paramref name="ns"/> and its descendant namespaces.
    /// Nested types are not descended into; callers that need them expand each result themselves. Used by the
    /// generator's compilation-wide discovery passes to walk the current source assembly's global namespace.
    /// </summary>
    public static IEnumerable<INamedTypeSymbol> EnumerateAssemblyTypes(INamespaceSymbol ns)
    {
        foreach (var type in ns.GetTypeMembers())
            yield return type;

        foreach (var child in ns.GetNamespaceMembers())
        {
            foreach (var type in EnumerateAssemblyTypes(child))
                yield return type;
        }
    }
}

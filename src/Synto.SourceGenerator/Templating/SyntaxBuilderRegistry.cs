using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Synto;

/// <summary>
/// Compilation-wide discovery of source-declared <c>[SyntaxBuilder]</c> methods. Shared by the facade-synthesis
/// generator and by facade-call matching. Runs inside the generator transform; nothing captured into pipeline state.
/// </summary>
internal static class SyntaxBuilderRegistry
{
    /// <summary>
    /// Enumerates the source-declared <c>public static</c> methods marked <c>[SyntaxBuilder]</c>, in a
    /// deterministic order (used by the facade-synthesis generator and by facade-call matching).
    /// </summary>
    public static ImmutableArray<IMethodSymbol> FindBuilders(Compilation compilation, INamedTypeSymbol? syntaxBuilderAttribute)
    {
        if (syntaxBuilderAttribute is null)
            return ImmutableArray<IMethodSymbol>.Empty;

        var builder = ImmutableArray.CreateBuilder<IMethodSymbol>();
        foreach (var type in EnumerateTypes(compilation.Assembly.GlobalNamespace))
        {
            foreach (var member in type.GetMembers())
            {
                if (member is IMethodSymbol method
                    && method.IsStatic
                    && SymbolMetadataExtensions.HasAttribute(method, syntaxBuilderAttribute))
                {
                    builder.Add(method);
                }
            }
        }

        builder.Sort(static (a, b) => string.CompareOrdinal(
            a.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            b.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));

        return builder.ToImmutable();
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceSymbol ns)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            yield return type;
            foreach (var nested in EnumerateNested(type))
                yield return nested;
        }

        foreach (var child in ns.GetNamespaceMembers())
        {
            foreach (var type in EnumerateTypes(child))
                yield return type;
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateNested(INamedTypeSymbol type)
    {
        foreach (var nested in type.GetTypeMembers())
        {
            yield return nested;
            foreach (var deeper in EnumerateNested(nested))
                yield return deeper;
        }
    }
}

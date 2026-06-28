using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Synto;

/// <summary>
/// Discovers user-authored value-to-syntax converters for an <c>[Unquote]</c> parameter whose declared type is
/// not one of the built-in literal types <see cref="LiteralSyntaxExtensions"/> handles. A converter is a
/// <c>static class</c> in the current compilation marked with <c>[Runtime]</c> that exposes an extension
/// method <c>ExpressionSyntax ToSyntax(this T)</c> for the unquoted type <c>T</c>.
/// </summary>
/// <remarks>
/// All discovery runs inside the <c>ForAttributeWithMetadataName</c> transform of
/// <see cref="TemplateFactorySourceGenerator"/> and only ever flows out equatable values (generated text +
/// diagnostics), so it never captures a <see cref="Compilation"/>, <see cref="ISymbol"/> or
/// <see cref="SyntaxNode"/> into pipeline state. The walk is scoped to the current source assembly
/// (<see cref="IAssemblySymbol.GlobalNamespace"/>) so it is bounded by the user's own project, and results
/// are returned in a deterministic order so the emitted source is stable.
/// </remarks>
internal static class RuntimeConverterFinder
{
    /// <summary>
    /// Enumerates the source-declared <c>static class</c>es marked with <paramref name="runtimeAttribute"/>,
    /// in a deterministic (fully-qualified-name) order.
    /// </summary>
    public static ImmutableArray<INamedTypeSymbol> FindRuntimeClasses(Compilation compilation, INamedTypeSymbol? runtimeAttribute)
    {
        if (runtimeAttribute is null)
            return ImmutableArray<INamedTypeSymbol>.Empty;

        var builder = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
        foreach (var type in EnumerateTypes(compilation.Assembly.GlobalNamespace))
        {
            if (type.TypeKind == TypeKind.Class && type.IsStatic && SymbolMetadataExtensions.HasAttribute(type, runtimeAttribute))
                builder.Add(type);
        }

        builder.Sort(static (a, b) => string.CompareOrdinal(
            a.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            b.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));

        return builder.ToImmutable();
    }

    /// <summary>
    /// Of <paramref name="runtimeClasses"/>, those exposing an applicable extension method
    /// <c>ToSyntax(this <paramref name="targetType"/>)</c> returning <paramref name="expressionSyntax"/>
    /// (or a subtype). 0 = no converter, 1 = the converter to inject, &gt;1 = ambiguous.
    /// </summary>
    public static ImmutableArray<INamedTypeSymbol> FindConvertersFor(
        ImmutableArray<INamedTypeSymbol> runtimeClasses,
        ITypeSymbol targetType,
        INamedTypeSymbol? expressionSyntax)
    {
        var builder = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
        foreach (var cls in runtimeClasses)
        {
            if (HasConverter(cls, targetType, expressionSyntax))
                builder.Add(cls);
        }

        return builder.ToImmutable();
    }

    private static bool HasConverter(INamedTypeSymbol cls, ITypeSymbol targetType, INamedTypeSymbol? expressionSyntax)
    {
        foreach (var member in cls.GetMembers("ToSyntax"))
        {
            if (member is not IMethodSymbol method)
                continue;

            if (!method.IsExtensionMethod || method.Parameters.Length != 1)
                continue;

            if (!SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, targetType))
                continue;

            // When ExpressionSyntax is resolvable, require the converter to actually return it (or a subtype),
            // so the generated `ExpressionSyntax x = value.ToSyntax();` assignment compiles.
            if (expressionSyntax is not null && !IsAssignableTo(method.ReturnType, expressionSyntax))
                continue;

            return true;
        }

        return false;
    }

    private static bool IsAssignableTo(ITypeSymbol type, INamedTypeSymbol target)
    {
        for (ITypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, target))
                return true;
        }

        return false;
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceSymbol ns)
    {
        foreach (var type in ns.GetTypeMembers())
            yield return type;

        foreach (var child in ns.GetNamespaceMembers())
        {
            foreach (var type in EnumerateTypes(child))
                yield return type;
        }
    }
}

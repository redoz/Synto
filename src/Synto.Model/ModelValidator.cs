using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Synto.Generators.Model;

/// <summary>
/// Cache-safety validation for a <c>[SyntoModel]</c> spec's projected fields. A generated record field must be an
/// equatable value shape — a C# primitive, <c>string</c>, an <c>enum</c>, an <c>EquatableArray&lt;…&gt;</c>, or
/// another <c>[SyntoModel]</c> record — so the emitted record has value equality and rides the incremental cache
/// by construction. A field that would leak a Roslyn <c>ISymbol</c>/<c>Compilation</c>/<c>Location</c> (or is
/// otherwise non-equatable) is dropped and reported (accumulate-and-continue, mirroring <c>TemplateValidator</c>).
/// All checks are mechanical symbol facts — no heuristic/LLM judgement.
/// </summary>
internal static class ModelValidator
{
    private const string SymbolInterfaceFullName = "global::Microsoft.CodeAnalysis.ISymbol";
    private const string CompilationFullName = "global::Microsoft.CodeAnalysis.Compilation";
    private const string LocationFullName = "global::Microsoft.CodeAnalysis.Location";
    private const string SyntoModelAttributeName = "Synto.Model.SyntoModelAttribute";
    private const string EquatableArrayName = "EquatableArray";
    private const string EquatableArrayNamespace = "Synto.Generators";

    /// <summary>
    /// Returns the projected fields that pass cache-safety validation, dropping each offender and appending the
    /// matching diagnostic to <paramref name="diagnostics"/> (accumulate-and-continue). A field whose DECLARED type
    /// is symbol-like reports <c>SY1036</c> (with the symbol-leak tail); a benign but opaque declared type
    /// (<c>object</c>/<c>dynamic</c>) whose LIFTED getter RHS returns a symbol reports <c>SY1037</c>; any other
    /// non-cache-safe declared type reports <c>SY1036</c> (no tail). <c>Project.Each/One</c> fields are synthesized
    /// to cache-safe record types and are always kept.
    /// </summary>
    public static IReadOnlyList<ProjectedField> Validate(List<DiagnosticInfo> diagnostics, ModelInfo info)
    {
        var survivors = new List<ProjectedField>(info.Fields.Count);

        foreach (var field in info.Fields)
        {
            // Project.Each/One fields become EquatableArray<Record> / Record? — cache-safe by construction.
            if (field.Kind != ProjectionKind.None)
            {
                survivors.Add(field);
                continue;
            }

            var declaredType = info.SemanticModel.GetTypeInfo(field.DeclaredType).Type;

            // A declared field type that IS a Roslyn symbol/compilation/location can never be cached.
            if (declaredType is not null && IsSymbolLike(declaredType))
            {
                diagnostics.Add(ModelDiagnostics.NonEquatableFieldType(
                    field.DeclaredType.GetLocation(), declaredType.ToDisplayString(), isSymbolLike: true));
                continue;
            }

            // An opaque declared type (object/dynamic) is not itself a leak, but the LIFTED getter RHS may still
            // return a symbol — catch that precisely with SY1037 rather than a blanket SY1036.
            if (declaredType is null || IsOpaque(declaredType))
            {
                var rhsType = info.SemanticModel.GetTypeInfo(field.Rhs).Type;
                if (rhsType is not null && IsSymbolLike(rhsType))
                {
                    diagnostics.Add(ModelDiagnostics.SymbolLeakInProjection(
                        field.Rhs.GetLocation(), rhsType.ToDisplayString()));
                    continue;
                }

                survivors.Add(field);
                continue;
            }

            // Any other declared type must be a cache-safe value shape.
            if (!IsCacheSafe(declaredType))
            {
                diagnostics.Add(ModelDiagnostics.NonEquatableFieldType(
                    field.DeclaredType.GetLocation(), declaredType.ToDisplayString(), isSymbolLike: false));
                continue;
            }

            survivors.Add(field);
        }

        return survivors;
    }

    private static bool IsOpaque(ITypeSymbol type) =>
        type.SpecialType == SpecialType.System_Object || type.TypeKind == TypeKind.Dynamic;

    /// <summary>
    /// Whether <paramref name="type"/> derives from / implements a Roslyn <c>ISymbol</c>, <c>Compilation</c>, or
    /// <c>Location</c> — the shapes that must never be cached into a model. <c>ISymbol</c> is an interface, so a
    /// concrete symbol type (e.g. <c>INamedTypeSymbol</c>) is caught via its implemented interfaces; <c>Compilation</c>
    /// and <c>Location</c> are abstract base classes, caught by walking the base-type chain.
    /// </summary>
    private static bool IsSymbolLike(ITypeSymbol type)
    {
        if (FullNameMatches(type, SymbolInterfaceFullName))
            return true;

        foreach (var iface in type.AllInterfaces)
        {
            if (FullNameMatches(iface, SymbolInterfaceFullName))
                return true;
        }

        for (ITypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (FullNameMatches(current, CompilationFullName) || FullNameMatches(current, LocationFullName))
                return true;
        }

        return false;
    }

    private static bool IsCacheSafe(ITypeSymbol type)
    {
        // Unwrap Nullable<T> — a nullable primitive/enum is still a cache-safe value.
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments.Length: 1 } nullable)
            type = nullable.TypeArguments[0];

        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean:
            case SpecialType.System_Char:
            case SpecialType.System_SByte:
            case SpecialType.System_Byte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Decimal:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_String:
                return true;
        }

        if (type.TypeKind == TypeKind.Enum)
            return true;

        if (type is INamedTypeSymbol named)
        {
            if (named.Name == EquatableArrayName
                && named.ContainingNamespace?.ToDisplayString() == EquatableArrayNamespace)
                return true;

            foreach (var attr in named.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString() == SyntoModelAttributeName)
                    return true;
            }
        }

        return false;
    }

    private static bool FullNameMatches(ITypeSymbol type, string fullyQualifiedName) =>
        type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == fullyQualifiedName;
}

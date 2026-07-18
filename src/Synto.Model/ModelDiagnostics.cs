using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Synto.Generators.Model;

/// <summary>
/// The Model feature's cache-safety diagnostics (the reserved <c>SY1036</c>–<c>SY1039</c> range, category
/// <c>Synto.Usage</c>). Kept in this feature DLL so its IDs never collide with Templating (<c>SY1005</c>–<c>SY1035</c>)
/// or Matching (<c>SY12xx</c>). Each descriptor is registered in <c>AnalyzerReleases.Unshipped.md</c> in the same
/// task that adds it (RS2008-clean), audited by <c>ModelDiagnosticsTests</c>. Every factory returns an equatable
/// <see cref="DiagnosticInfo"/> so violations ride the incremental pipeline as cacheable values.
/// </summary>
internal static class ModelDiagnostics
{
    private const string Category = "Synto.Usage";

    // SY1036 — a projected field's DECLARED type is not cache-safe (not a primitive/string/enum/EquatableArray/
    // nested [SyntoModel] record). When the type derives from ISymbol/Compilation/Location the message appends the
    // symbol-leak tail steering the author to project it to a value.
    private const string SymbolLeakTail =
        " would leak symbol into cached model; project it to a value (e.g. `.Name`, `.ToDisplayString()`).";

    private static readonly DiagnosticDescriptor _nonEquatableFieldType = new("SY1036",
        "Non-Equatable Model Field",
        "'{0}' is not a cache-safe model field type; use a primitive, string, enum, EquatableArray<…>, or another [SyntoModel] record.{1}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticInfo NonEquatableFieldType(Location? location, string typeName, bool isSymbolLike) =>
        new(_nonEquatableFieldType, LocationInfo.CreateFrom(location),
            new EquatableArray<string>(ImmutableArray.Create(typeName, isSymbolLike ? SymbolLeakTail : string.Empty)));

    // SY1037 — the LIFTED getter RHS result type is symbol-like even though the declared field type looked benign
    // (e.g. `object Leak => Symbol;`). Catches the leak the declared-type check alone would miss.
    private static readonly DiagnosticDescriptor _symbolLeakInProjection = new("SY1037",
        "Symbol Leak In Projection",
        "The projection result type '{0}' would leak a symbol into the cached model; project it to a value (e.g. `.Name`, `.ToDisplayString()`).",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticInfo SymbolLeakInProjection(Location? location, string typeName) =>
        new(_symbolLeakInProjection, LocationInfo.CreateFrom(location),
            new EquatableArray<string>(ImmutableArray.Create(typeName)));

    // SY1038 — a Project.Each/One type argument is not a [SyntoModel] spec class in the same compilation, so its
    // nested `From` projection cannot be resolved.
    private static readonly DiagnosticDescriptor _projectElementNotSyntoModel = new("SY1038",
        "Project Element Not A Model",
        "'{0}' is not a [SyntoModel] spec class; Project.Each/One can only project into a [SyntoModel] record",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticInfo ProjectElementNotSyntoModel(Location? location, string typeArg) =>
        new(_projectElementNotSyntoModel, LocationInfo.CreateFrom(location),
            new EquatableArray<string>(ImmutableArray.Create(typeArg)));

    // SY1039 — the spec class name lacks the trailing `Shape` suffix and no [SyntoModel(Name=…)] override was
    // given, so no record name can be derived.
    private static readonly DiagnosticDescriptor _nameSuffixMissing = new("SY1039",
        "Model Name Cannot Be Derived",
        "Cannot derive a record name for '{0}'; end the spec class name with 'Shape' or set [SyntoModel(Name=…)]",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticInfo NameSuffixMissing(Location? location, string specName) =>
        new(_nameSuffixMissing, LocationInfo.CreateFrom(location),
            new EquatableArray<string>(ImmutableArray.Create(specName)));
}

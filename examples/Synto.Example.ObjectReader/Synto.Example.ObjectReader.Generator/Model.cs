using Microsoft.CodeAnalysis;
using Synto.Generators;

namespace Synto.Example.ObjectReader.Generator;

/// <summary>One resolved column: the member name (also the direct accessor on <c>_e.Current</c>) and its
/// fully-qualified type name (used for <c>GetFieldType</c>). Pure value type — safe to cache (C-5).</summary>
internal readonly record struct ColumnInfo(string Name, string ColumnTypeName);

/// <summary>Which diagnostic an equatable <see cref="PendingDiagnostic"/> carries; mapped back to a real
/// <c>Synto.Diagnostics</c>-generated factory call in the output stage.</summary>
internal enum DiagnosticKind
{
    /// <summary>SOR0000 — an unexpected generator exception, converted to a diagnostic (never thrown — C-5).</summary>
    InternalError,

    /// <summary>SOR0001 — a named member was not found on the target type; the column is skipped (C-2).</summary>
    MemberNotFound,

    /// <summary>SOR0002 — the member list was not compile-time-constant; the call is not intercepted.</summary>
    MembersNotConstant,
}

/// <summary>
/// A value-equatable description of a diagnostic carried out of the transform. The real
/// <see cref="Diagnostic"/> is materialized only in the output stage (via the <c>Synto.Diagnostics</c>-
/// generated factory) so the cached pipeline value stays free of non-cacheable Roslyn objects (C-5).
/// </summary>
// stopgap until Synto.Diagnostics supports the cacheable path (its own future spec)
internal readonly record struct PendingDiagnostic(DiagnosticKind Kind, LocationInfo? Location, EquatableArray<string> Arguments);

/// <summary>
/// The equatable per-call-site model the syntax/semantic transform flows out. Carries NO Roslyn objects
/// (no Compilation/ISymbol/SemanticModel/SyntaxNode) so the pipeline stays cacheable (C-5); emission in
/// <c>RegisterSourceOutput</c> runs from this value alone. <paramref name="Intercept"/> is <c>false</c> for a
/// diagnostics-only model (e.g. a non-constant member list — SOR0002): its diagnostics are replayed but no
/// reader/interceptor is emitted for it.
/// </summary>
internal readonly record struct ObjectReaderModel(
    string TargetTypeQualifiedName,
    string TargetTypeShortName,
    EquatableArray<ColumnInfo> Columns,
    EquatableArray<PendingDiagnostic> Diagnostics,
    string InterceptsLocationAttribute,
    bool Intercept);

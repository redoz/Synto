namespace Synto.Diagnostics;

/// <summary>
/// The value-equatable output of processing a single <c>[Diagnostic]</c> method. Carrying only the generated
/// text and diagnostic data (never the <see cref="Microsoft.CodeAnalysis.SemanticModel"/>, symbols or syntax
/// nodes used to produce it) lets the incremental pipeline cache results and avoids rooting the compilation
/// in memory across edits.
/// </summary>
internal record struct DiagnosticGenerationResult(string? FileName, string? Source, EquatableArray<DiagnosticInfo> Diagnostics);

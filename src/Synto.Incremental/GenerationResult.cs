namespace Synto.Generators;

/// <summary>
/// The value-equatable output of processing a single generator target. Carrying only the generated text and
/// diagnostic data (never the <see cref="Microsoft.CodeAnalysis.SemanticModel"/>, symbols or syntax nodes
/// used to produce it) lets the incremental pipeline cache results and avoids rooting the compilation. Shared
/// by the Template, Match and Diagnostics generators, whose source-output stages are identical.
/// </summary>
internal record struct GenerationResult(string? FileName, string? Source, EquatableArray<DiagnosticInfo> Diagnostics);

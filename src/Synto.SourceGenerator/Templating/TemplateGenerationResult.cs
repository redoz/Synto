namespace Synto;

/// <summary>
/// The value-equatable output of processing a single <c>[Template]</c>. Carrying only the generated text and
/// diagnostic data (never the <see cref="Microsoft.CodeAnalysis.SemanticModel"/>, symbols or syntax nodes
/// used to produce it) lets the incremental pipeline cache results and avoids rooting the compilation.
/// </summary>
internal record struct TemplateGenerationResult(string? FileName, string? Source, EquatableArray<DiagnosticInfo> Diagnostics);

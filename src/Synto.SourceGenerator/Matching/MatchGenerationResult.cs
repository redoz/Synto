namespace Synto;

/// <summary>
/// The value-equatable output of processing a single <c>[Match&lt;TMatcher&gt;]</c> pattern. Carrying only the
/// generated text and diagnostic data (never the <see cref="Microsoft.CodeAnalysis.SemanticModel"/>, symbols
/// or syntax nodes used to produce it) lets the incremental pipeline cache results and avoids rooting the
/// compilation. Mirrors <see cref="TemplateGenerationResult"/>.
/// </summary>
internal record struct MatchGenerationResult(string? FileName, string? Source, EquatableArray<DiagnosticInfo> Diagnostics);

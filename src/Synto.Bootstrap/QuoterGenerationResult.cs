namespace Synto.Bootstrap;

/// <summary>
/// The value-equatable output of processing the <c>CSharpSyntaxQuoter</c> target. Carrying only the generated
/// text and diagnostic data (never the <see cref="Microsoft.CodeAnalysis.SemanticModel"/>, symbols or syntax
/// nodes used to produce it) lets the incremental pipeline cache results and avoids rooting the compilation
/// in memory across edits.
/// </summary>
internal record struct QuoterGenerationResult(string? FileName, string? Source, EquatableArray<DiagnosticInfo> Diagnostics);

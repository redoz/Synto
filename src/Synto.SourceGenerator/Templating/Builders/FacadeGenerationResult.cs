namespace Synto.Generators.Templating;

/// <summary>
/// The value-equatable output of processing a single <c>[SyntaxBuilder]</c> method for facade synthesis.
/// Mirrors <see cref="TemplateGenerationResult"/>: it carries only the generated facade text and located
/// diagnostic data (never the <see cref="Microsoft.CodeAnalysis.ISymbol"/>/syntax nodes used to produce it),
/// so the incremental pipeline can cache results without rooting the compilation. A declaration-site misuse
/// yields diagnostics with no source; a valid builder yields source with no diagnostics.
/// </summary>
internal record struct FacadeGenerationResult(string? HintName, string? Source, EquatableArray<DiagnosticInfo> Diagnostics);

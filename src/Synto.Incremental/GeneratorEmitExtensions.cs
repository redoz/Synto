using System;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Synto.Generators;

/// <summary>
/// The single source-output stage shared by the Template, Match and Diagnostics generators: report every
/// carried diagnostic, then (if generation produced a file) add the source, degrading an <c>AddSource</c>
/// failure to a diagnostic rather than crashing the generator.
/// </summary>
internal static class GeneratorEmitExtensions
{
    public static void Emit(this SourceProductionContext context, in GenerationResult result)
    {
        foreach (var diagnostic in result.Diagnostics)
            context.ReportDiagnostic(diagnostic.ToDiagnostic());

        if (result.FileName is not null && result.Source is not null)
        {
            try
            {
                context.AddSource(result.FileName, SourceText.From(result.Source, Encoding.UTF8));
            }
#pragma warning disable CA1031 // degrade an AddSource failure (e.g. a duplicate/invalid hint) to a diagnostic instead of crashing the generator
            catch (ArgumentException ex)
#pragma warning restore CA1031
            {
                context.ReportDiagnostic(Incremental.Diagnostics.InternalError(ex).ToDiagnostic());
            }
        }
    }
}

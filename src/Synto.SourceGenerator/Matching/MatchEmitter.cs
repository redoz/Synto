using System.Collections.Generic;

namespace Synto;

/// <summary>
/// Lowers a validated <see cref="MatchInfo"/> into the generated matcher source.
/// </summary>
/// <remarks>
/// STUB (Task 4): real emission — the generic structural walk, captures, the run-alignment core — lands in
/// Task 5 onward. Returning <see langword="null"/> means "nothing emitted"; the pipeline then flows only the
/// (possibly empty) diagnostics, exactly like Templating's diagnostics-only bail. The signature mirrors
/// Templating's <c>ProcessTemplate</c> so the call site in <see cref="MatchFactorySourceGenerator"/> is
/// stable when the body is filled in.
/// </remarks>
internal static class MatchEmitter
{
    public static (string FileName, string Source)? Emit(List<DiagnosticInfo> diagnostics, MatchInfo info)
    {
        return null;
    }
}

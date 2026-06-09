using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Synto.Bootstrap;

/// <summary>
/// A value-equatable description of a diagnostic. The real <see cref="Diagnostic"/> is materialized only in
/// the source-output stage via <see cref="ToDiagnostic"/>, keeping the cached pipeline value free of
/// non-cacheable Roslyn objects (a live <see cref="Location"/> / <see cref="SemanticModel"/> roots the
/// syntax tree / compilation in memory across edits).
/// </summary>
/// <remarks>
/// The bootstrap generator's only failure mode is an unhandled exception during quoting, which it reports as
/// the internal-error <c>SY0000</c> with no location, so this carries just the descriptor and message
/// arguments. The shared descriptor is reused (not re-allocated per run) so equal failures compare equal.
/// </remarks>
internal record struct DiagnosticInfo(DiagnosticDescriptor Descriptor, EquatableArray<string> MessageArgs)
{
    private static readonly DiagnosticDescriptor InternalErrorDescriptor = new(
        "SY0000",
        "Failed to create CSharpSyntaxQuoter",
        "Exception: {0}",
        "Synto.Dev",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticInfo InternalError(Exception exception) =>
        new(InternalErrorDescriptor, new EquatableArray<string>(ImmutableArray.Create(exception.ToString())));

    public readonly Diagnostic ToDiagnostic()
    {
        var args = new object?[MessageArgs.Count];
        for (int i = 0; i < MessageArgs.Count; i++)
            args[i] = MessageArgs[i];

        return Diagnostic.Create(Descriptor, location: null, args);
    }
}

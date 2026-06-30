using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Synto;

/// <summary>
/// A value-equatable representation of a <see cref="Location"/> so diagnostics can be carried through the
/// incremental pipeline without rooting a <see cref="SyntaxTree"/> / <see cref="Compilation"/> in memory.
/// </summary>
internal record struct LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
{
    public Location ToLocation() => Location.Create(FilePath, TextSpan, LineSpan);

    public static LocationInfo? CreateFrom(Location? location)
    {
        if (location?.SourceTree is null)
            return null;

        return new LocationInfo(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);
    }
}

/// <summary>
/// A value-equatable description of a diagnostic. The real <see cref="Diagnostic"/> is materialized only in
/// the source-output stage via <see cref="ToDiagnostic"/>, keeping the cached pipeline value free of
/// non-cacheable Roslyn objects.
/// </summary>
internal record struct DiagnosticInfo(DiagnosticDescriptor Descriptor, LocationInfo? Location, EquatableArray<string> MessageArgs)
{
    public Diagnostic ToDiagnostic()
    {
        var args = new object?[MessageArgs.Count];
        for (int i = 0; i < MessageArgs.Count; i++)
            args[i] = MessageArgs[i];

        return Diagnostic.Create(Descriptor, Location?.ToLocation(), args);
    }
}

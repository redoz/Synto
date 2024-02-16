//HintName: X.Y.Z.Diagnostics.InternalError.g.cs
#nullable enable
using Microsoft.CodeAnalysis;

namespace X.Y.Z;
internal static partial class Diagnostics
{
    private static DiagnosticDescriptor _InternalError = new("TST0000", "Internal Error", "Unhandled exception {0} was thrown: {1}", "Synto.Internal", DiagnosticSeverity.Error, true);
    public static partial Diagnostic InternalError(Location location, string exceptionType, string exceptionMessage)
    {
        return Diagnostic.Create(
                   _InternalError, 
                   location, 
                   null, 
                   null, 
                   null, 
                   exceptionType, 
                   exceptionMessage);
    }
}

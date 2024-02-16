using Microsoft.CodeAnalysis;
using Synto.Diagnostics;

namespace Synto.Example.ObjectReader;

internal static partial class Diagnostics
{
    private const string IdPrefix = "SOR";

    public static Diagnostic InternalError(Exception exception)
    {
        return InternalError(null, exception.GetType().FullName!, exception.ToString());
    }

    [Diagnostic(IdPrefix + "0000", "Internal Error", "Unhandled exception {0} was thrown: {1}", "Synto.Internal", DiagnosticSeverity.Error, true)]
    public static partial Diagnostic InternalError(Location? location, string exceptionType, string exceptionMessage);
}
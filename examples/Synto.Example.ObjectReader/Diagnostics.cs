using Microsoft.CodeAnalysis;
using Synto.Diagnostics;

namespace Synto.Example.ObjectReader;

internal static partial class Diagnostics
{
    private const string IdPrefix = "SOR";

    private static readonly DiagnosticDescriptor _InternalError = new(IdPrefix + "0000",
        "Internal Error",
        "Unhandled exception {0} was thrown: {1}",
        "Synto.Internal",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);


    public static Diagnostic InternalError(Exception exception)
    {
        return Diagnostic.Create(_InternalError,
            location: null,
            exception.GetType().FullName,
            exception.ToString().Replace("\r", "").Replace("\n", " "));
    }

    [Diagnostic(IdPrefix + "0000", "Internal Error", "Unhandled exception {0} was thrown: {1}", "Synto.Internal", DiagnosticSeverity.Error, true)]
    public static partial Diagnostic UnsuppoertedWhatever(Location location, string exceptionType, string exceptionMessage);
}
using System;
using Microsoft.CodeAnalysis;

namespace Synto.Diagnostics;

internal static class Diagnostics
{
    private const string IdPrefix = "SDG";

    private static readonly DiagnosticDescriptor _InternalError = new(
        IdPrefix + "0000",
        "Internal Error",
        "Unhandled exception {0} was thrown: {1}",
        "Synto.Diagnostics.Internal",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);


    public static Diagnostic InternalError(Exception exception)
    {
        return Diagnostic.Create(_InternalError,
            location: null,
            exception.GetType().FullName,
            exception.ToString().Replace("\r", "").Replace("\n", " "));
    }

    private static readonly DiagnosticDescriptor _TargetAncestorNotPartial = new(
        IdPrefix + "1002",
        "Invalid Target",
        "Target method '{0}' ancestor '{1}' must be declared partial",
        "Synto.Diagnostics.Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static Diagnostic TargetAncestorNotPartial(Location location, string methodName, string ancestorName)
    {
        return Diagnostic.Create(_TargetAncestorNotPartial,
            location,
            methodName,
            ancestorName);
    }

    private static readonly DiagnosticDescriptor _TargetNotPartial = new(
        IdPrefix + "1001",
        "Invalid Target",
        "Target method '{0}' must be declared as partial",
        "Synto.Diagnostics.Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static Diagnostic TargetNotPartial(Location location, string methodName)
    {
        return Diagnostic.Create(_TargetNotPartial,
            null,
            location,
            methodName);
    }

}
using System;
using System.Collections.Immutable;
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

    public static DiagnosticInfo InternalError(Exception exception)
    {
        return new DiagnosticInfo(_InternalError,
            Location: null,
            new EquatableArray<string>(ImmutableArray.Create(
                exception.GetType().FullName!,
                exception.ToString().Replace("\r", "").Replace("\n", " "))));
    }

    private static readonly DiagnosticDescriptor _TargetNotPartial = new(
        IdPrefix + "1001",
        "Invalid Target",
        "Target method '{0}' must be declared as partial",
        "Synto.Diagnostics.Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticInfo TargetNotPartial(LocationInfo? location, string methodName)
    {
        return new DiagnosticInfo(_TargetNotPartial,
            location,
            new EquatableArray<string>(ImmutableArray.Create(methodName)));
    }

    private static readonly DiagnosticDescriptor _TargetAncestorNotPartial = new(
        IdPrefix + "1002",
        "Invalid Target",
        "Target method '{0}' ancestor '{1}' must be declared partial",
        "Synto.Diagnostics.Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticInfo TargetAncestorNotPartial(LocationInfo? location, string methodName, string ancestorName)
    {
        return new DiagnosticInfo(_TargetAncestorNotPartial,
            location,
            new EquatableArray<string>(ImmutableArray.Create(methodName, ancestorName)));
    }

    private static readonly DiagnosticDescriptor _TargetNotClass = new(
        IdPrefix + "1003",
        "Invalid Target",
        "Target method '{0}' must be declared in a partial class",
        "Synto.Diagnostics.Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticInfo TargetNotClass(LocationInfo? location, string methodName)
    {
        return new DiagnosticInfo(_TargetNotClass,
            location,
            new EquatableArray<string>(ImmutableArray.Create(methodName)));
    }
}

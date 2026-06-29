using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Synto;

internal static class Diagnostics
{


    internal const string IdPrefix = "SY";

    private static readonly DiagnosticDescriptor _internalError = new(IdPrefix + "0000",
                                                                    "Internal Error",
                                                                    "Unhandled exception {0} was thrown: {1}",
                                                                    "Synto.Internal",
                                                                    DiagnosticSeverity.Error,
                                                                    isEnabledByDefault: true);

    public static DiagnosticInfo InternalError(Exception exception)
    {
        return new DiagnosticInfo(_internalError,
                                    Location: null,
                                    new EquatableArray<string>(ImmutableArray.Create(
                                        exception.GetType().FullName!,
                                        exception.ToString().Replace("\r", "").Replace("\n", " "))));
    }

    private static readonly DiagnosticDescriptor _targetNotPartial = new(IdPrefix + "1001",
                                                                            "Invalid Target",
                                                                            "Target type '{0}' must be declared as a partial class",
                                                                            "Synto.Usage",
                                                                            DiagnosticSeverity.Error,
                                                                            isEnabledByDefault: true);

    public static DiagnosticInfo TargetNotPartial(TargetType target)
    {
        return TargetNotPartial(LocationInfo.CreateFrom(target.GetReferenceLocation()), target.FullName);
    }

    // LocationInfo-based overload (C5): Matching has only the attribute Location (no typeof argument), so it
    // supplies the located target name directly. Same descriptor / ID as the TargetType overload above.
    public static DiagnosticInfo TargetNotPartial(LocationInfo? location, string targetName)
    {
        return new DiagnosticInfo(_targetNotPartial,
                                    location,
                                    new EquatableArray<string>(ImmutableArray.Create(targetName)));
    }

    private static readonly DiagnosticDescriptor _targetNotClass = new(IdPrefix + "1002",
                                                                        "Invalid Target",
                                                                        "Target type '{0}' must be declared as a class",
                                                                        "Synto.Usage",
                                                                        DiagnosticSeverity.Error,
                                                                        isEnabledByDefault: true);

    public static DiagnosticInfo TargetNotClass(TargetType target)
    {
        return TargetNotClass(LocationInfo.CreateFrom(target.GetReferenceLocation()), target.FullName);
    }

    public static DiagnosticInfo TargetNotClass(LocationInfo? location, string targetName)
    {
        return new DiagnosticInfo(_targetNotClass,
                                    location,
                                    new EquatableArray<string>(ImmutableArray.Create(targetName)));
    }

    private static readonly DiagnosticDescriptor _targetNotDeclaredInSource = new(IdPrefix + "1003",
                                                                                    "Invalid Target",
                                                                                    "Target type '{0}' must be declared in project '{1}'",
                                                                                    "Synto.Usage",
                                                                                    DiagnosticSeverity.Error,
                                                                                    isEnabledByDefault: true);

    public static DiagnosticInfo TargetNotDeclaredInSource(TargetType target, string? projectName)
    {
        return TargetNotDeclaredInSource(LocationInfo.CreateFrom(target.GetReferenceLocation()), target.FullName, projectName);
    }

    public static DiagnosticInfo TargetNotDeclaredInSource(LocationInfo? location, string targetName, string? projectName)
    {
        return new DiagnosticInfo(_targetNotDeclaredInSource,
                                    location,
                                    new EquatableArray<string>(ImmutableArray.Create(targetName, projectName ?? "<unknown>")));
    }

    private static readonly DiagnosticDescriptor _targetAncestorNotPartial = new(IdPrefix + "1004",
                                                                                    "Invalid Target",
                                                                                    "Target type '{0}' ancestor '{1}' must be declared partial",
                                                                                    "Synto.Usage",
                                                                                    DiagnosticSeverity.Error,
                                                                                    isEnabledByDefault: true);

    public static DiagnosticInfo TargetAncestorNotPartial(TargetType target, string ancestorName)
    {
        return TargetAncestorNotPartial(LocationInfo.CreateFrom(target.GetReferenceLocation()), target.FullName, ancestorName);
    }

    public static DiagnosticInfo TargetAncestorNotPartial(LocationInfo? location, string targetName, string ancestorName)
    {
        return new DiagnosticInfo(_targetAncestorNotPartial,
                                    location,
                                    new EquatableArray<string>(ImmutableArray.Create(targetName, ancestorName)));
    }

}

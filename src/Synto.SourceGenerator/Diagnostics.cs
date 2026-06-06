using System;
using Microsoft.CodeAnalysis;

namespace Synto;

internal static class Diagnostics
{


    private const string IdPrefix = "SY";

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
                                    new EquatableArray<string>(new[]
                                    {
                                        exception.GetType().FullName!,
                                        exception.ToString().Replace("\r", "").Replace("\n", " ")
                                    }));
    }

    private static readonly DiagnosticDescriptor _targetNotPartial = new(IdPrefix + "1001",
                                                                            "Invalid Target",
                                                                            "Target type '{0}' must be declared as a partial class",
                                                                            "Synto.Usage",
                                                                            DiagnosticSeverity.Error,
                                                                            isEnabledByDefault: true);

    public static DiagnosticInfo TargetNotPartial(TargetType target)
    {
        return new DiagnosticInfo(_targetNotPartial,
                                    LocationInfo.CreateFrom(target.GetReferenceLocation()),
                                    new EquatableArray<string>(new[] { target.FullName }));
    }

    private static readonly DiagnosticDescriptor _targetNotClass = new(IdPrefix + "1002",
                                                                        "Invalid Target",
                                                                        "Target type '{0}' must be declared as a class",
                                                                        "Synto.Usage",
                                                                        DiagnosticSeverity.Error,
                                                                        isEnabledByDefault: true);

    public static DiagnosticInfo TargetNotClass(TargetType target)
    {
        return new DiagnosticInfo(_targetNotClass,
                                    LocationInfo.CreateFrom(target.GetReferenceLocation()),
                                    new EquatableArray<string>(new[] { target.FullName }));
    }

    private static readonly DiagnosticDescriptor _targetNotDeclaredInSource = new(IdPrefix + "1003",
                                                                                    "Invalid Target",
                                                                                    "Target type '{0}' must be declared in project '{1}'",
                                                                                    "Synto.Usage",
                                                                                    DiagnosticSeverity.Error,
                                                                                    isEnabledByDefault: true);

    public static DiagnosticInfo TargetNotDeclaredInSource(TargetType target, string? projectName)
    {
        return new DiagnosticInfo(_targetNotDeclaredInSource,
                                    LocationInfo.CreateFrom(target.GetReferenceLocation()),
                                    new EquatableArray<string>(new[] { target.FullName, projectName ?? "<unknown>" }));
    }

    private static readonly DiagnosticDescriptor _targetAncestorNotPartial = new(IdPrefix + "1004",
                                                                                    "Invalid Target",
                                                                                    "Target type '{0}' ancestor '{1}' must be declared partial",
                                                                                    "Synto.Usage",
                                                                                    DiagnosticSeverity.Error,
                                                                                    isEnabledByDefault: true);

    public static DiagnosticInfo TargetAncestorNotPartial(TargetType target, string ancestorName)
    {
        return new DiagnosticInfo(_targetAncestorNotPartial,
                                    LocationInfo.CreateFrom(target.GetReferenceLocation()),
                                    new EquatableArray<string>(new[] { target.FullName, ancestorName }));
    }

    private static readonly DiagnosticDescriptor _bareSourceCannotBeEmpty = new(IdPrefix + "1005",
                                                                            "Invalid Source",
                                                                            "Source '{0}' can not be empty when Bare is specified",
                                                                            "Synto.Usage",
                                                                            DiagnosticSeverity.Error,
                                                                            isEnabledByDefault: true);

    public static DiagnosticInfo BareSourceCannotBeEmpty(Source source)
    {
        return new DiagnosticInfo(_bareSourceCannotBeEmpty,
                                    LocationInfo.CreateFrom(source.Syntax.GetLocation()),
                                    new EquatableArray<string>(new[] { source.Identifier }));
    }

    private static readonly DiagnosticDescriptor _multipleStatementsNotAllowed = new(IdPrefix + "1006",
        "Invalid Source",
        "Source function '{0}' can not have multiple statement when Single is specified",
        "Synto.Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticInfo MultipleStatementsNotAllowed(Source source)
    {
        return new DiagnosticInfo(_multipleStatementsNotAllowed,
            LocationInfo.CreateFrom(source.Syntax.GetLocation()),
            new EquatableArray<string>(new[] { source.Identifier }));
    }


    private static readonly DiagnosticDescriptor _multipleMembersNotAllowed = new(IdPrefix + "1007",
        "Invalid Source",
        "Source type '{0}' can not have multiple members when Single is specified",
        "Synto.Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticInfo MultipleMembersNotAllowed(Source source)
    {
        return new DiagnosticInfo(_multipleMembersNotAllowed,
            LocationInfo.CreateFrom(source.Syntax.GetLocation()),
            new EquatableArray<string>(new[] { source.Identifier }));
    }
}

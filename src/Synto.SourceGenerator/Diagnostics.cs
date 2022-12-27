using System;
using Microsoft.CodeAnalysis;

namespace Synto;

internal static class Diagnostics
{


    private const string IdPrefix = "SY";

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

    private static readonly DiagnosticDescriptor _TargetNotPartial = new(IdPrefix + "1001",
                                                                            "Invalid Target",
                                                                            "Target type '{0}' must be declared as a partial class",
                                                                            "Synto.Usage",
                                                                            DiagnosticSeverity.Error,
                                                                            isEnabledByDefault: true);

    public static Diagnostic TargetNotPartial(TargetType target)
    {
        return Diagnostic.Create(_TargetNotPartial,
                                    target.Reference.GetLocation(),
                                    target.FullName);
    }

    private static readonly DiagnosticDescriptor _TargetNotClass = new(IdPrefix + "1002",
                                                                        "Invalid Target",
                                                                        "Target type '{0}' must be declared as a partial class",
                                                                        "Synto.Usage",
                                                                        DiagnosticSeverity.Error,
                                                                        isEnabledByDefault: true);

    public static Diagnostic TargetNotClass(TargetType target)
    {
        return Diagnostic.Create(_TargetNotClass,
                                    target.Reference.GetLocation(),
                                    target.FullName);
    }

    private static readonly DiagnosticDescriptor _TargetNotDeclaredInSource = new(IdPrefix + "1003",
                                                                                    "Invalid Target",
                                                                                    "Target type '{0}' must be declared in project '{1}'",
                                                                                    "Synto.Usage",
                                                                                    DiagnosticSeverity.Error,
                                                                                    isEnabledByDefault: true);

    public static Diagnostic TargetNotDeclaredInSource(TargetType target, string? projectName)
    {
        return Diagnostic.Create(_TargetNotDeclaredInSource,
                                    target.Reference.GetLocation(),
                                    target.FullName, projectName ?? "<unknown>");
    }

    private static readonly DiagnosticDescriptor _TargetAncestorNotPartial = new(IdPrefix + "1004",
                                                                                    "Invalid Target",
                                                                                    "Target type '{0}' ancestor '{1}' must be declared partial",
                                                                                    "Synto.Usage",
                                                                                    DiagnosticSeverity.Error,
                                                                                    isEnabledByDefault: true);

    public static Diagnostic TargetAncestorNotPartial(TargetType target, string ancestorName)
    {
        return Diagnostic.Create(_TargetAncestorNotPartial,
                                    target.Reference.GetLocation(),
                                    target.FullName, ancestorName);
    }

    private static readonly DiagnosticDescriptor _BareSourceCannotBeEmpty = new(IdPrefix + "1005",
                                                                            "Invalid Source",
                                                                            "Source function '{0}' can not be empty when Bare is specified",
                                                                            "Synto.Usage",
                                                                            DiagnosticSeverity.Error,
                                                                            isEnabledByDefault: true);

    public static Diagnostic BareSourceCannotBeEmpty(SourceFunction source)
    {
        return Diagnostic.Create(_BareSourceCannotBeEmpty,
                                    source.Body?.GetLocation() ?? source.Identifier.GetLocation(),
                                    source.Identifier.ValueText);
    }

    private static readonly DiagnosticDescriptor _MultipleStatementsNotAllowed = new(IdPrefix + "1006",
        "Invalid Source",
        "Source function '{0}' can not have multiple statement when Single is specified",
        "Synto.Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static Diagnostic MultipleStatementsNotAllowed(SourceFunction source)
    {
        return Diagnostic.Create(_MultipleStatementsNotAllowed,
            source.Body?.GetLocation() ?? source.Identifier.GetLocation(),
            source.Identifier.ValueText);
    }
}


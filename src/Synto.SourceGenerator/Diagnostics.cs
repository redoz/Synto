using System;
using Microsoft.CodeAnalysis;
using Synto;

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
                                    target.GetReferenceLocation(),
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
                                    target.GetReferenceLocation(),
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
                                    target.GetReferenceLocation(),
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
                                    target.GetReferenceLocation(),
                                    target.FullName, ancestorName);
    }

    private static readonly DiagnosticDescriptor _BareSourceCannotBeEmpty = new(IdPrefix + "1005",
                                                                            "Invalid Source",
                                                                            "Source '{0}' can not be empty when Bare is specified",
                                                                            "Synto.Usage",
                                                                            DiagnosticSeverity.Error,
                                                                            isEnabledByDefault: true);

    public static Diagnostic BareSourceCannotBeEmpty(Source source)
    {
        return Diagnostic.Create(_BareSourceCannotBeEmpty,
                                    source.Syntax.GetLocation(),
                                    source.Identifier);
    }

    private static readonly DiagnosticDescriptor _MultipleStatementsNotAllowed = new(IdPrefix + "1006",
        "Invalid Source",
        "Source function '{0}' can not have multiple statement when Single is specified",
        "Synto.Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static Diagnostic MultipleStatementsNotAllowed(Source source)
    {
        return Diagnostic.Create(_MultipleStatementsNotAllowed,
            source.Syntax.GetLocation(),
            source.Identifier);
    }


    private static readonly DiagnosticDescriptor _MultipleMembersNotAllowed = new(IdPrefix + "1007",
        "Invalid Source",
        "Source type '{0}' can not have multiple members when Single is specified",
        "Synto.Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static Diagnostic MultipleMembersNotAllowed(Source source)
    {
        return Diagnostic.Create(_MultipleMembersNotAllowed,
            source.Syntax.GetLocation() ,
            source.Identifier);
    }
}


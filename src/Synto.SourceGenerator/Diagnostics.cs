using System;
using System.Collections.Immutable;
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
                                    new EquatableArray<string>(ImmutableArray.Create(source.Identifier)));
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
            new EquatableArray<string>(ImmutableArray.Create(source.Identifier)));
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
            new EquatableArray<string>(ImmutableArray.Create(source.Identifier)));
    }

    private static readonly DiagnosticDescriptor _noRuntimeConverter = new(IdPrefix + "1008",
        "Missing Converter",
        "No value-to-syntax converter found for inlined type '{0}'; mark a static class with [Runtime] and give it an extension method 'ExpressionSyntax ToSyntax(this {0} value)'",
        "Synto.Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticInfo NoRuntimeConverter(Location? location, string typeName)
    {
        return new DiagnosticInfo(_noRuntimeConverter,
            LocationInfo.CreateFrom(location),
            new EquatableArray<string>(ImmutableArray.Create(typeName)));
    }

    private static readonly DiagnosticDescriptor _ambiguousRuntimeConverter = new(IdPrefix + "1009",
        "Ambiguous Converter",
        "Multiple [Runtime] converters define 'ToSyntax(this {0})' ({1} found); keep exactly one",
        "Synto.Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticInfo AmbiguousRuntimeConverter(Location? location, string typeName, int count)
    {
        return new DiagnosticInfo(_ambiguousRuntimeConverter,
            LocationInfo.CreateFrom(location),
            new EquatableArray<string>(ImmutableArray.Create(
                typeName,
                count.ToString(System.Globalization.CultureInfo.InvariantCulture))));
    }

    private static readonly DiagnosticDescriptor _liveParameterMissingName = new(IdPrefix + "1010",
        "Missing Parameter Name",
        "Parameter<T>() used outside a variable declaration must specify an explicit parameterName",
        "Synto.Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticInfo LiveParameterMissingName(Location? location)
    {
        return new DiagnosticInfo(_liveParameterMissingName,
            LocationInfo.CreateFrom(location),
            new EquatableArray<string>(ImmutableArray<string>.Empty));
    }

    private static readonly DiagnosticDescriptor _liveParameterNameCollision = new(IdPrefix + "1011",
        "Live Parameter Name Collision",
        "Two Parameter<T>() sites declare the same explicit name '{0}'; reference one declared parameter instead of naming it twice",
        "Synto.Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticInfo LiveParameterNameCollision(Location? location, string name)
    {
        return new DiagnosticInfo(_liveParameterNameCollision,
            LocationInfo.CreateFrom(location),
            new EquatableArray<string>(ImmutableArray.Create(name)));
    }

    private static readonly DiagnosticDescriptor _liveParameterTypeConflict = new(IdPrefix + "1012",
        "Conflicting Live Parameter Type",
        "Parameter name '{0}' is declared with conflicting types '{1}' and '{2}'",
        "Synto.Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticInfo LiveParameterTypeConflict(Location? location, string name, string firstType, string secondType)
    {
        return new DiagnosticInfo(_liveParameterTypeConflict,
            LocationInfo.CreateFrom(location),
            new EquatableArray<string>(ImmutableArray.Create(name, firstType, secondType)));
    }

    private static readonly DiagnosticDescriptor _unsupportedLiveShape = new(IdPrefix + "1014",
        "Unsupported Live Shape",
        "Live control-flow shape is not supported by the staging emitter: {0}",
        "Synto.Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticInfo UnsupportedLiveShape(Location? location, string reason)
    {
        return new DiagnosticInfo(_unsupportedLiveShape,
            LocationInfo.CreateFrom(location),
            new EquatableArray<string>(ImmutableArray.Create(reason)));
    }

    private static readonly DiagnosticDescriptor _facadeSynthesisError = new(IdPrefix + "1015",
        "Invalid Syntax Builder",
        "[SyntaxBuilder] method '{0}' cannot synthesize a valid facade: {1}",
        "Synto.Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticInfo FacadeSynthesisError(Location? location, string builderName, string reason)
    {
        return new DiagnosticInfo(_facadeSynthesisError,
            LocationInfo.CreateFrom(location),
            new EquatableArray<string>(ImmutableArray.Create(builderName, reason)));
    }

    private static readonly DiagnosticDescriptor _builderArgBindingMismatch = new(IdPrefix + "1016",
        "Syntax Builder Argument Mismatch",
        "Argument for syntax-builder parameter '{0}' cannot satisfy its binding: {1}",
        "Synto.Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticInfo BuilderArgBindingMismatch(Location? location, string parameterName, string reason)
    {
        return new DiagnosticInfo(_builderArgBindingMismatch,
            LocationInfo.CreateFrom(location),
            new EquatableArray<string>(ImmutableArray.Create(parameterName, reason)));
    }

    private static readonly DiagnosticDescriptor _builderBadReturnShape = new(IdPrefix + "1017",
        "Invalid Syntax Builder Return",
        "[SyntaxBuilder] method '{0}' returns '{1}', which is not a supported builder return shape (expected ExpressionSyntax or TypeSyntax)",
        "Synto.Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticInfo BuilderBadReturnShape(Location? location, string builderName, string returnType)
    {
        return new DiagnosticInfo(_builderBadReturnShape,
            LocationInfo.CreateFrom(location),
            new EquatableArray<string>(ImmutableArray.Create(builderName, returnType)));
    }

    private static readonly DiagnosticDescriptor _ambiguousBuilder = new(IdPrefix + "1018",
        "Ambiguous Syntax Builder",
        "Two [SyntaxBuilder] methods synthesize colliding facade '{0}'; keep exactly one",
        "Synto.Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticInfo AmbiguousBuilder(Location? location, string facadeName)
    {
        return new DiagnosticInfo(_ambiguousBuilder,
            LocationInfo.CreateFrom(location),
            new EquatableArray<string>(ImmutableArray.Create(facadeName)));
    }
}

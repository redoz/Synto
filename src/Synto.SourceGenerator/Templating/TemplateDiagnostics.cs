using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Synto;

internal static class TemplateDiagnostics
{
    private const string IdPrefix = "SY";

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

    private static readonly DiagnosticDescriptor _unquoteParameterMissingName = new(IdPrefix + "1010",
        "Missing Parameter Name",
        "Parameter<T>() used outside a variable declaration must specify an explicit parameterName",
        "Synto.Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticInfo UnquoteParameterMissingName(Location? location)
    {
        return new DiagnosticInfo(_unquoteParameterMissingName,
            LocationInfo.CreateFrom(location),
            new EquatableArray<string>(ImmutableArray<string>.Empty));
    }

    private static readonly DiagnosticDescriptor _unquoteParameterNameCollision = new(IdPrefix + "1011",
        "Unquote Parameter Name Collision",
        "Two Parameter<T>() sites declare the same explicit name '{0}'; reference one declared parameter instead of naming it twice",
        "Synto.Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticInfo UnquoteParameterNameCollision(Location? location, string name)
    {
        return new DiagnosticInfo(_unquoteParameterNameCollision,
            LocationInfo.CreateFrom(location),
            new EquatableArray<string>(ImmutableArray.Create(name)));
    }

    private static readonly DiagnosticDescriptor _unquoteParameterTypeConflict = new(IdPrefix + "1012",
        "Conflicting Unquote Parameter Type",
        "Parameter name '{0}' is declared with conflicting types '{1}' and '{2}'",
        "Synto.Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticInfo UnquoteParameterTypeConflict(Location? location, string name, string firstType, string secondType)
    {
        return new DiagnosticInfo(_unquoteParameterTypeConflict,
            LocationInfo.CreateFrom(location),
            new EquatableArray<string>(ImmutableArray.Create(name, firstType, secondType)));
    }

    private static readonly DiagnosticDescriptor _impossibleCut = new(IdPrefix + "1013",
        "Impossible Cut",
        "Staged binding cannot run at factory time: {0}",
        "Synto.Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticInfo ImpossibleCut(Location? location, string reason)
    {
        return new DiagnosticInfo(_impossibleCut,
            LocationInfo.CreateFrom(location),
            new EquatableArray<string>(ImmutableArray.Create(reason)));
    }

    private static readonly DiagnosticDescriptor _unsupportedStagedShape = new(IdPrefix + "1014",
        "Unsupported Staged Shape",
        "Staged control-flow shape is not supported by the staging emitter: {0}",
        "Synto.Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticInfo UnsupportedStagedShape(Location? location, string reason)
    {
        return new DiagnosticInfo(_unsupportedStagedShape,
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

    private static readonly DiagnosticDescriptor _spliceMethodMustBeStatic = new(IdPrefix + "1019",
        "Invalid Splice Member Generator",
        "[Splice] member generator '{0}' must be static (it runs at factory-build time; an instance method could reach output-world members).",
        "Synto.Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticInfo SpliceMethodMustBeStatic(Location? location, string methodName)
    {
        return new DiagnosticInfo(_spliceMethodMustBeStatic,
            LocationInfo.CreateFrom(location),
            new EquatableArray<string>(ImmutableArray.Create(methodName)));
    }

    private static readonly DiagnosticDescriptor _spliceMethodBadReturnType = new(IdPrefix + "1020",
        "Invalid Splice Member Generator",
        "[Splice] member generator '{0}' returns '{1}', which is not a supported shape (expected MemberDeclarationSyntax or IEnumerable<MemberDeclarationSyntax>).",
        "Synto.Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticInfo SpliceMethodBadReturnType(Location? location, string methodName, string returnType)
    {
        return new DiagnosticInfo(_spliceMethodBadReturnType,
            LocationInfo.CreateFrom(location),
            new EquatableArray<string>(ImmutableArray.Create(methodName, returnType)));
    }

    private static readonly DiagnosticDescriptor _spliceMethodHasParameters = new(IdPrefix + "1021",
        "Invalid Splice Member Generator",
        "[Splice] member generator '{0}' must be parameterless (inputs are supplied via Parameter<T>(); the generator is auto-invoked with no caller).",
        "Synto.Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticInfo SpliceMethodHasParameters(Location? location, string methodName)
    {
        return new DiagnosticInfo(_spliceMethodHasParameters,
            LocationInfo.CreateFrom(location),
            new EquatableArray<string>(ImmutableArray.Create(methodName)));
    }
}

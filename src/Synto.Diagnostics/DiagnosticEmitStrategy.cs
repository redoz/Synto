using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto;
using Synto.Generators;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto.Diagnostics;

/// <summary>The classification of a <c>[Diagnostic]</c> factory parameter, used by <see cref="DiagnosticEmitStrategy.ValidateParameter"/>.</summary>
internal enum ParameterRole
{
    Severity,
    MessageArg
}

/// <summary>
/// The body-emission seam for the <c>[Diagnostic]</c> factory. The live and cacheable return shapes are
/// peers behind this strategy (no <c>if (isCacheable)</c> thread): each recognizes its own location-parameter
/// type and builds its own <c>return</c> statement. <see cref="ForReturnType"/> selects the strategy from the
/// factory's declared return type.
/// </summary>
internal abstract class DiagnosticEmitStrategy
{
    /// <summary>Recognizes this strategy's location-parameter type (live: <c>Location</c>).</summary>
    public abstract bool IsLocationParameter(IParameterSymbol parameter, Compilation compilation);

    /// <summary>
    /// Validates a parameter that has already been classified as <see cref="ParameterRole.Severity"/> or
    /// <see cref="ParameterRole.MessageArg"/>, returning an SDG1005 <see cref="DiagnosticInfo"/> when this
    /// strategy's shape cannot represent it, or <c>null</c> when the parameter is fine as-is. The live strategy
    /// (whose <c>Diagnostic.Create</c> body can carry any severity/message-arg type via <c>string.Format</c>)
    /// never rejects; the cacheable strategy overrides this to reject non-<c>string</c> message args and any
    /// severity parameter (both would otherwise be silently dropped or emit uncompilable code).
    /// </summary>
    public virtual DiagnosticInfo? ValidateParameter(
        IParameterSymbol parameter,
        ParameterRole role,
        Compilation compilation,
        LocationInfo? location) => null;

    /// <summary>Builds the factory's <c>return</c> statement for this strategy's shape.</summary>
    public abstract StatementSyntax BuildReturn(
        IdentifierNameSyntax descriptorName,
        ExpressionSyntax? locationExpr,
        ExpressionSyntax? severityExpr,
        IReadOnlyList<ExpressionSyntax> messageArgExprs,
        UsingDirectiveSet usings);

    /// <summary>
    /// Selects the emit strategy for the factory's declared return type: <see cref="LiveDiagnosticEmitStrategy"/>
    /// for <c>Microsoft.CodeAnalysis.Diagnostic</c>, <c>null</c> otherwise. (Task 5 adds the cacheable arm for
    /// <c>Synto.Generators.DiagnosticInfo</c>; Task 6 turns the remaining <c>null</c> into a diagnostic.)
    /// </summary>
    public static DiagnosticEmitStrategy? ForReturnType(ITypeSymbol returnType, Compilation compilation)
    {
        var diagnosticType = compilation.GetTypeByMetadataName(typeof(Diagnostic).FullName!);
        if (SymbolEqualityComparer.Default.Equals(returnType, diagnosticType))
            return new LiveDiagnosticEmitStrategy();

        var diagnosticInfoType = compilation.GetTypeByMetadataName("Synto.Generators.DiagnosticInfo");
        if (SymbolEqualityComparer.Default.Equals(returnType, diagnosticInfoType))
            return new CacheableDiagnosticEmitStrategy();

        return null;
    }
}

/// <summary>
/// Emits a live <c>Diagnostic.Create(descriptor, location, severity?, additionalLocations, properties, args...)</c>
/// return — the historical <c>[Diagnostic]</c> body, unchanged. Location parameter type: <c>Location</c>.
/// </summary>
internal sealed class LiveDiagnosticEmitStrategy : DiagnosticEmitStrategy
{
    public override bool IsLocationParameter(IParameterSymbol parameter, Compilation compilation)
    {
        var locationType = compilation.GetTypeByMetadataName(typeof(Location).FullName!);
        return SymbolEqualityComparer.Default.Equals(parameter.Type, locationType);
    }

    public override StatementSyntax BuildReturn(
        IdentifierNameSyntax descriptorName,
        ExpressionSyntax? locationExpr,
        ExpressionSyntax? severityExpr,
        IReadOnlyList<ExpressionSyntax> messageArgExprs,
        UsingDirectiveSet usings)
    {
        ExpressionSyntax location = locationExpr ?? LiteralExpression(SyntaxKind.NullLiteralExpression);
        ExpressionSyntax severity = severityExpr ?? LiteralExpression(SyntaxKind.NullLiteralExpression);
        ExpressionSyntax additionalLocations = LiteralExpression(SyntaxKind.NullLiteralExpression);
        ExpressionSyntax properties = LiteralExpression(SyntaxKind.NullLiteralExpression);

        return ReturnStatement(
            InvocationExpression(
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    usings.GetTypeName(
                        ParseName(typeof(Diagnostic).FullName!)),
                    IdentifierName("Create")),
                ArgumentList(
                    SeparatedList([
                        Argument(descriptorName),
                        Argument(location),
                        Argument(severity),
                        Argument(additionalLocations),
                        Argument(properties),
                        .. messageArgExprs.Select(Argument)
                    ]))));
    }
}

/// <summary>
/// Emits a cacheable <c>new Synto.Generators.DiagnosticInfo(descriptor, location, new EquatableArray&lt;string&gt;(...))</c>
/// return — a value-equatable diagnostic that rides the incremental cache and materializes a live
/// <c>Diagnostic</c> only at source-output time via <c>ToDiagnostic()</c>. Location parameter type:
/// <c>Synto.Generators.LocationInfo</c> (matched through its nullable-underlying type, so <c>LocationInfo?</c> matches).
/// Severity is baked into the descriptor, so no separate severity argument is emitted.
/// </summary>
internal sealed class CacheableDiagnosticEmitStrategy : DiagnosticEmitStrategy
{
    public override bool IsLocationParameter(IParameterSymbol parameter, Compilation compilation)
    {
        var locationInfoType = compilation.GetTypeByMetadataName("Synto.Generators.LocationInfo");

        // A cacheable factory takes LocationInfo? (Nullable<LocationInfo>); compare the nullable-underlying type.
        var paramType = parameter.Type;
        if (paramType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            paramType = nullable.TypeArguments[0];

        return SymbolEqualityComparer.Default.Equals(paramType, locationInfoType);
    }

    public override DiagnosticInfo? ValidateParameter(
        IParameterSymbol parameter,
        ParameterRole role,
        Compilation compilation,
        LocationInfo? location)
    {
        switch (role)
        {
            // DiagnosticInfo has no severity field — severity is fixed by the descriptor. A severity parameter
            // would be captured and then silently dropped by BuildReturn.
            case ParameterRole.Severity:
                return Diagnostics.UnsupportedCacheableParameter(
                    location,
                    parameter.Name,
                    parameter.Type.ToDisplayString(),
                    "severity is fixed by the descriptor on a cacheable [Diagnostic] factory; remove this parameter");

            // DiagnosticInfo.MessageArgs is EquatableArray<string> — a non-string message arg would emit
            // uncompilable code (CS1503) rather than being formatted, unlike the live path's string.Format.
            case ParameterRole.MessageArg when parameter.Type.SpecialType != SpecialType.System_String:
                return Diagnostics.UnsupportedCacheableParameter(
                    location,
                    parameter.Name,
                    parameter.Type.ToDisplayString(),
                    "message arguments must be of type 'string' on a cacheable [Diagnostic] factory");

            default:
                return null;
        }
    }

    public override StatementSyntax BuildReturn(
        IdentifierNameSyntax descriptorName,
        ExpressionSyntax? locationExpr,
        ExpressionSyntax? severityExpr,
        IReadOnlyList<ExpressionSyntax> messageArgExprs,
        UsingDirectiveSet usings)
    {
        ExpressionSyntax location = locationExpr ?? LiteralExpression(SyntaxKind.NullLiteralExpression);

        // new EquatableArray<string>(ImmutableArray.Create(args...))  — or ImmutableArray<string>.Empty when no args.
        ExpressionSyntax messageArgs = messageArgExprs.Count == 0
            ? MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                usings.GetTypeName(ParseName("System.Collections.Immutable.ImmutableArray<string>")),
                IdentifierName("Empty"))
            : InvocationExpression(
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    usings.GetTypeName(ParseName("System.Collections.Immutable.ImmutableArray")),
                    IdentifierName("Create")),
                ArgumentList(SeparatedList(messageArgExprs.Select(Argument))));

        ExpressionSyntax equatableArray = ObjectCreationExpression(
            usings.GetTypeName(ParseName("Synto.Generators.EquatableArray<string>")))
            .WithArgumentList(ArgumentList(SeparatedList([Argument(messageArgs)])));

        return ReturnStatement(
            ObjectCreationExpression(
                usings.GetTypeName(ParseName("Synto.Generators.DiagnosticInfo")))
                .WithArgumentList(
                    ArgumentList(
                        SeparatedList([
                            Argument(descriptorName),
                            Argument(location),
                            Argument(equatableArray)
                        ]))));
    }
}

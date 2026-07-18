using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto.Diagnostics;

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

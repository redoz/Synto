using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto;

/// <summary>
/// Builds the staged-region CALL expressions over the Synto.Core runtime helpers (BuildList / ListSegment.Run /
/// ToSyntax). Emits invocations only — it never re-declares a helper; the helper NAMES are bound via
/// <c>nameof</c> so a Synto.Core rename fails at generator compile time instead of silently producing a
/// non-compiling factory.
/// </summary>
internal static class StagedHelperCallFactory
{
    private const string CollectionHelper = nameof(CollectionSyntaxExtensions);
    private const string BuildListMethod = nameof(CollectionSyntaxExtensions.BuildList);
    private const string ListSegmentType = nameof(CollectionSyntaxExtensions.ListSegment<StatementSyntax>);
    private const string RunMethod = nameof(CollectionSyntaxExtensions.ListSegment<StatementSyntax>.Run);
    private const string ToSyntaxMethod = nameof(LiteralSyntaxExtensions.ToSyntax);

    /// <summary><c>CollectionSyntaxExtensions.ListSegment&lt;StatementSyntax&gt;.Run(runName)</c>.</summary>
    public static ExpressionSyntax RunSegment(string runName) =>
        InvocationExpression(
            MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName(CollectionHelper),
                    GenericName(Identifier(ListSegmentType))
                        .WithTypeArgumentList(TypeArgumentList(SingletonSeparatedList<TypeSyntax>(IdentifierName("StatementSyntax"))))),
                IdentifierName(RunMethod)))
            .AddArgumentListArguments(Argument(IdentifierName(runName)));

    /// <summary><c>Block(CollectionSyntaxExtensions.BuildList&lt;StatementSyntax&gt;(seg0, seg1, ...))</c>.</summary>
    public static ExpressionSyntax BlockReplacement(IReadOnlyList<ExpressionSyntax> segments)
    {
        var buildList = InvocationExpression(
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName(CollectionHelper),
                    GenericName(Identifier(BuildListMethod))
                        .WithTypeArgumentList(TypeArgumentList(SingletonSeparatedList<TypeSyntax>(IdentifierName("StatementSyntax"))))))
            .WithArgumentList(ArgumentList(SeparatedList(segments.Select(Argument))));

        return InvocationExpression(IdentifierName("Block"))
            .AddArgumentListArguments(Argument(buildList));
    }

    public static ExpressionSyntax ToSyntaxCall(ExpressionSyntax expression)
    {
        ExpressionSyntax target = NeedsParentheses(expression)
            ? ParenthesizedExpression(expression.WithoutTrivia())
            : expression.WithoutTrivia();

        return InvocationExpression(
            MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                target,
                IdentifierName(ToSyntaxMethod)));
    }

    private static bool NeedsParentheses(ExpressionSyntax expression) =>
        expression is not (IdentifierNameSyntax or MemberAccessExpressionSyntax or InvocationExpressionSyntax
            or ElementAccessExpressionSyntax or ParenthesizedExpressionSyntax or LiteralExpressionSyntax);
}

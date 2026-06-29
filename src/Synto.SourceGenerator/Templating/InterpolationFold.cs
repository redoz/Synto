using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto;

/// <summary>
/// Interpolation staged-fold subsystem (spec 2026-06-28). Folds bare staged-string interpolation holes in a
/// regular <c>$"…"</c> string's contents list into their flanking literal text, so a foldable hole's escaped value
/// (<c>label.ToInterpolatedText()</c>) is fused into the surrounding literal token instead of being re-emitted as a
/// runtime hole. Driven by <see cref="TemplateSyntaxQuoter"/>, which passes its own <c>Visit</c> (synchronous
/// re-entry) and array-literal builder as callbacks — no captured closure outlives the transform. Pure
/// transform-internal helper; nothing captured into pipeline state.
/// </summary>
internal sealed class InterpolationFold
{
    private readonly IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> _stringStagedRoots;
    private readonly Func<SyntaxNode?, ExpressionSyntax?> _visit;
    private readonly Func<IEnumerable<ExpressionSyntax>, TypeSyntax, ExpressionSyntax> _toArrayLiteral;

    public InterpolationFold(
        IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> stringStagedRoots,
        Func<SyntaxNode?, ExpressionSyntax?> visit,
        Func<IEnumerable<ExpressionSyntax>, TypeSyntax, ExpressionSyntax> toArrayLiteral)
    {
        _stringStagedRoots = stringStagedRoots;
        _visit = visit;
        _toArrayLiteral = toArrayLiteral;
    }

    /// <summary>
    /// Folds bare staged-string interpolation holes in a regular <c>$"…"</c> string's contents list into their
    /// flanking literal text. Returns <see langword="false"/> (defer to base) when there is nothing to fold:
    /// no string staged roots, a verbatim/raw interpolated string, or no foldable hole present. v1 scope:
    /// only a hole whose expression is a string staged root with NO alignment and NO format clause inside a
    /// regular interpolated string.
    /// </summary>
    public bool TryFoldInterpolatedContents(SyntaxList<InterpolatedStringContentSyntax> contents, out ExpressionSyntax result)
    {
        result = null!;

        if (_stringStagedRoots.Count == 0 || contents.Count == 0)
            return false;

        // Regular `$"…"` only — defer verbatim (`$@"…"`) and raw (`$"""…"""`) interpolated strings to base.
        if (contents[0].Parent is not InterpolatedStringExpressionSyntax owner
            || !owner.StringStartToken.IsKind(SyntaxKind.InterpolatedStringStartToken))
            return false;

        bool anyFold = false;
        foreach (var content in contents)
        {
            if (content is InterpolationSyntax interpolation && IsFoldableHole(interpolation))
            {
                anyFold = true;
                break;
            }
        }

        if (!anyFold)
            return false;

        // Rebuild the contents: a maximal run of literal text + foldable holes (bounded by runtime/non-foldable
        // holes or the string ends) fuses into ONE InterpolatedStringText token; runtime holes break the run and
        // are emitted unchanged via the base per-node quoting.
        var output = new List<ExpressionSyntax>();
        var group = new List<InterpolatedStringContentSyntax>();
        bool groupHasFold = false;

        void Flush()
        {
            if (group.Count == 0)
                return;

            if (groupHasFold)
            {
                output.Add(BuildFusedText(group));
            }
            else
            {
                foreach (var node in group)
                    if (_visit(node) is { } quoted)
                        output.Add(quoted);
            }

            group.Clear();
            groupHasFold = false;
        }

        foreach (var content in contents)
        {
            if (content is InterpolationSyntax interpolation && IsFoldableHole(interpolation))
            {
                group.Add(content);
                groupHasFold = true;
            }
            else if (content is InterpolatedStringTextSyntax)
            {
                group.Add(content);
            }
            else
            {
                // A runtime / non-foldable interpolation: flush the pending run, then emit it unchanged.
                Flush();
                if (_visit(content) is { } quoted)
                    output.Add(quoted);
            }
        }

        Flush();

        // Mirror the base SyntaxList quoting wrapper exactly: new List<InterpolatedStringContentSyntax>(new[] { … }).
        TypeSyntax elementType = ParseTypeName(typeof(InterpolatedStringContentSyntax).Name);
        result = InvocationExpression(
            GenericName(Identifier(nameof(List)), TypeArgumentList(SingletonSeparatedList(elementType))),
            ArgumentList(SingletonSeparatedList(Argument(_toArrayLiteral(output, elementType)))));
        return true;
    }

    private bool IsFoldableHole(InterpolationSyntax interpolation)
        => interpolation.AlignmentClause is null
           && interpolation.FormatClause is null
           && _stringStagedRoots.ContainsKey(interpolation.Expression);

    /// <summary>
    /// Builds the single fused <c>InterpolatedStringText(Token(TriviaList(), InterpolatedStringTextToken, …, …,
    /// TriviaList()))</c> for a run of literal-text and foldable-hole content. The token's TEXT is the escaped
    /// concatenation (literal runs verbatim, each hole as <c>accessor.ToInterpolatedText()</c>); its VALUE-TEXT is
    /// the decoded concatenation (literal value-text, each hole as the raw <c>accessor</c>).
    /// </summary>
    private ExpressionSyntax BuildFusedText(List<InterpolatedStringContentSyntax> group)
    {
        ExpressionSyntax? textExpr = null;
        ExpressionSyntax? valueExpr = null;

        static void Append(ref ExpressionSyntax? accumulator, ExpressionSyntax piece)
            => accumulator = accumulator is null
                ? piece
                : BinaryExpression(SyntaxKind.AddExpression, accumulator, piece);

        foreach (var item in group)
        {
            if (item is InterpolatedStringTextSyntax text)
            {
                Append(ref textExpr, LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(text.TextToken.Text)));
                Append(ref valueExpr, LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(text.TextToken.ValueText)));
            }
            else if (item is InterpolationSyntax interpolation)
            {
                ExpressionSyntax accessor = _stringStagedRoots[interpolation.Expression];
                Append(ref textExpr, InvocationExpression(
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        accessor,
                        IdentifierName(nameof(InterpolationSyntaxExtensions.ToInterpolatedText)))));
                Append(ref valueExpr, accessor);
            }
        }

        textExpr ??= LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(string.Empty));
        valueExpr ??= LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(string.Empty));

        return InvocationExpression(
            IdentifierName(nameof(InterpolatedStringText)),
            ArgumentList(SingletonSeparatedList(Argument(
                InvocationExpression(
                    IdentifierName(nameof(Token)),
                    ArgumentList(SeparatedList(new[]
                    {
                        Argument(InvocationExpression(IdentifierName(nameof(TriviaList)))),
                        Argument(IdentifierName(nameof(SyntaxKind.InterpolatedStringTextToken))),
                        Argument(textExpr),
                        Argument(valueExpr),
                        Argument(InvocationExpression(IdentifierName(nameof(TriviaList)))),
                    })))))));
    }
}

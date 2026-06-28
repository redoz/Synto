using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;


namespace Synto;

internal sealed class TemplateSyntaxQuoter : CSharpSyntaxQuoter
{
    private readonly SemanticModel _semanticModel;
    private readonly IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> _unquotedReplacements;
    private readonly HashSet<SyntaxNode> _trimNodes;
    private readonly IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> _memberSegments;

    // Interpolation staged-fold channel (spec 2026-06-28): the string-typed staged-root REFERENCE nodes that
    // may appear as bare interpolation holes, mapped to their factory-time raw value accessor (e.g. the factory
    // parameter / hoisted local `label`). A foldable hole's escaped value (`label.ToInterpolatedText()`) is fused
    // into the surrounding literal text instead of being re-emitted as a runtime hole. Built at EMISSION (no
    // ITypeSymbol/SemanticModel leaks into cached pipeline state); empty when no template-body string holes occur.
    private readonly IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> _stringStagedRoots;


    public TemplateSyntaxQuoter(
        SemanticModel semanticModel,
        IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> unquotedReplacements,
        HashSet<SyntaxNode> trimNodes,
        bool includeTrivia,
        IReadOnlyDictionary<SyntaxNode, ExpressionSyntax>? memberSegments = null,
        IReadOnlyDictionary<SyntaxNode, ExpressionSyntax>? stringStagedRoots = null) : base(includeTrivia)
    {
        _semanticModel = semanticModel;
        _unquotedReplacements = unquotedReplacements;
        _trimNodes = trimNodes;
        _memberSegments = memberSegments ?? new Dictionary<SyntaxNode, ExpressionSyntax>();
        _stringStagedRoots = stringStagedRoots ?? new Dictionary<SyntaxNode, ExpressionSyntax>();
    }

    public override ExpressionSyntax? Visit(SyntaxNode? node)
    {
        if (node is not null && _unquotedReplacements.TryGetValue(node, out var replacement))
            return replacement;

        if (node is not null && _trimNodes.Contains(node))
            return null;

        return base.Visit(node);
    }

    /// <summary>
    /// A member list that contains one or more <c>[Splice]</c> member generators is emitted as a
    /// <c>CollectionSyntaxExtensions.BuildList&lt;TNode&gt;(…)</c> run (spec §4, member axis): each fixed member is
    /// a single-node segment quoted in place, and each generator contributes its precomputed segment
    /// (<c>ListSegment&lt;TNode&gt;.Run(…)</c> for an enumerable shape, or the generator call directly for a single
    /// member) at its DECLARATION position among the siblings — so declaration order is preserved. Any other
    /// SyntaxList (no generator present) falls through to the base list quoting unchanged.
    /// </summary>
    public override ExpressionSyntax Visit<TNode>(SyntaxList<TNode> nodeList)
    {
        // List-level interpolation staged-fold (spec 2026-06-28). This is a plain virtual override of the
        // generated base list-quoting — exactly the same mechanism as the [Splice] member BuildList path below
        // and Visit(SyntaxNode?) above — NOT suppression. The fold must live at the contents-list level (not a
        // VisitInterpolation override) because fusing a foldable hole with its FLANKING InterpolatedStringText
        // runs requires seeing the sibling text nodes, which a per-hole override never receives. Every
        // non-foldable list (and every interpolated string with no foldable hole) defers to the base behavior.
        if (typeof(TNode) == typeof(InterpolatedStringContentSyntax)
            && TryFoldInterpolatedContents((SyntaxList<InterpolatedStringContentSyntax>)(object)nodeList, out var foldedContents))
        {
            return foldedContents;
        }

        bool hasSegment = false;
        foreach (var node in nodeList)
        {
            if (_memberSegments.ContainsKey(node))
            {
                hasSegment = true;
                break;
            }
        }

        if (!hasSegment)
            return base.Visit(nodeList);

        TypeSyntax elementType = ParseTypeName(typeof(TNode).Name);

        var arguments = new List<ArgumentSyntax>();
        foreach (var node in nodeList)
        {
            if (_memberSegments.TryGetValue(node, out var segment))
            {
                arguments.Add(Argument(segment));
            }
            else if (Visit(node) is { } quoted)
            {
                arguments.Add(Argument(quoted));
            }
        }

        return InvocationExpression(
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName(nameof(CollectionSyntaxExtensions)),
                    GenericName(Identifier(nameof(CollectionSyntaxExtensions.BuildList)))
                        .WithTypeArgumentList(TypeArgumentList(SingletonSeparatedList(elementType)))))
            .WithArgumentList(ArgumentList(SeparatedList(arguments)));
    }

    /// <summary>
    /// Folds bare staged-string interpolation holes in a regular <c>$"…"</c> string's contents list into their
    /// flanking literal text. Returns <see langword="false"/> (defer to base) when there is nothing to fold:
    /// no string staged roots, a verbatim/raw interpolated string, or no foldable hole present. v1 scope:
    /// only a hole whose expression is a string staged root with NO alignment and NO format clause inside a
    /// regular interpolated string.
    /// </summary>
    private bool TryFoldInterpolatedContents(SyntaxList<InterpolatedStringContentSyntax> contents, out ExpressionSyntax result)
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
                    if (Visit(node) is { } quoted)
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
                if (Visit(content) is { } quoted)
                    output.Add(quoted);
            }
        }

        Flush();

        // Mirror the base SyntaxList quoting wrapper exactly: new List<InterpolatedStringContentSyntax>(new[] { … }).
        TypeSyntax elementType = ParseTypeName(typeof(InterpolatedStringContentSyntax).Name);
        result = InvocationExpression(
            GenericName(Identifier(nameof(List)), TypeArgumentList(SingletonSeparatedList(elementType))),
            ArgumentList(SingletonSeparatedList(Argument(ToArrayLiteral(output, elementType)))));
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

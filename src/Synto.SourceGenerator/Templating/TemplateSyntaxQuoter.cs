using System.Collections.Generic;
using System.Collections.Immutable;
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

    // Post-quote hook channel (Task 3): decoration hooks applied after a node's base quote is produced.
    // Each entry maps a source node to an ordered sequence of AppliedDecoration values; the quoter folds
    // them in order onto the base quote as chained method-invocation expressions. Inert when empty.
    private readonly IReadOnlyDictionary<SyntaxNode, ImmutableArray<AppliedDecoration>> _postQuoteHooks;

    // Interpolation staged-fold subsystem (spec 2026-06-28). Constructed with this quoter's own Visit (synchronous
    // re-entry) and the base array-literal builder as callbacks — a local delegate, never a captured closure
    // outliving the transform.
    private readonly InterpolationFold _interpolationFold;


    public TemplateSyntaxQuoter(
        SemanticModel semanticModel,
        IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> unquotedReplacements,
        HashSet<SyntaxNode> trimNodes,
        bool includeTrivia,
        IReadOnlyDictionary<SyntaxNode, ExpressionSyntax>? memberSegments = null,
        IReadOnlyDictionary<SyntaxNode, ExpressionSyntax>? stringStagedRoots = null,
        IReadOnlyDictionary<SyntaxNode, ImmutableArray<AppliedDecoration>>? postQuoteHooks = null) : base(includeTrivia)
    {
        _semanticModel = semanticModel;
        _unquotedReplacements = unquotedReplacements;
        _trimNodes = trimNodes;
        _memberSegments = memberSegments ?? new Dictionary<SyntaxNode, ExpressionSyntax>();
        _stringStagedRoots = stringStagedRoots ?? new Dictionary<SyntaxNode, ExpressionSyntax>();
        _postQuoteHooks = postQuoteHooks ?? new Dictionary<SyntaxNode, ImmutableArray<AppliedDecoration>>();
        _interpolationFold = new InterpolationFold(_stringStagedRoots, Visit, ToArrayLiteral);
    }

    public override ExpressionSyntax? Visit(SyntaxNode? node)
    {
        if (node is not null && _unquotedReplacements.TryGetValue(node, out var replacement))
            return replacement;

        if (node is not null && _trimNodes.Contains(node))
            return null;

        var result = base.Visit(node);
        if (node is not null && result is not null && _postQuoteHooks.TryGetValue(node, out var hooks))
            foreach (var hook in hooks)
            {
                var arguments = new List<ArgumentSyntax>(hook.Arguments.Length);
                foreach (var arg in hook.Arguments)
                    arguments.Add(Argument(arg));
                result = InvocationExpression(
                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, result, IdentifierName(hook.HelperMethodName)))
                    .WithArgumentList(ArgumentList(SeparatedList(arguments)));
            }
        return result;
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
            && _interpolationFold.TryFoldInterpolatedContents((SyntaxList<InterpolatedStringContentSyntax>)(object)nodeList, out var foldedContents))
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
}

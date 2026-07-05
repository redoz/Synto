using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto.Generators.Templating.Quoting.Passes;

/// <summary>
/// Owner-keyed List-pipeline step; dispatched by <see cref="PerNodePipelineQuoter"/>. For an interpolated
/// string's contents list (<c>SyntaxList&lt;InterpolatedStringContentSyntax&gt;</c>), fold bare staged-string
/// interpolation holes into their flanking literal text via the <see cref="InterpolationFold"/> subsystem;
/// every other list shape — and every interpolated string with no foldable hole — defers to <c>next</c>. The
/// <c>TNode == InterpolatedStringContentSyntax</c> guard and this self-deferral are load-bearing: they resolve
/// the owner-key collision residual (a non-target list reaching this step falls through). Per-owner counterpart
/// of the dissolved InterpolationFoldMiddleware.
/// </summary>
internal sealed class InterpolationFoldStep : IListQuoteMiddleware
{
    private readonly IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> _stringStagedRoots;
    private InterpolationFold? _fold;

    public InterpolationFoldStep(IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> stringStagedRoots)
        => _stringStagedRoots = stringStagedRoots;

    // The quoter only dispatches this step for interpolated-string content lists (central element-type guard),
    // so the body no longer re-checks TNode.
    public Type ElementType => typeof(InterpolatedStringContentSyntax);

    public ExpressionSyntax Quote<TNode>(SyntaxList<TNode> list, QuoteContext ctx, ListQuoteNext<TNode> next)
        where TNode : SyntaxNode
    {
        // Build the fold lazily from the engine's cooperation surface (same context across all calls).
        _fold ??= new InterpolationFold(_stringStagedRoots, ctx.Quote, ctx.ToArrayLiteral);

        if (_fold.TryFoldInterpolatedContents(
                (SyntaxList<InterpolatedStringContentSyntax>)(object)list, out var folded))
        {
            return folded;
        }

        return next(list);
    }
}

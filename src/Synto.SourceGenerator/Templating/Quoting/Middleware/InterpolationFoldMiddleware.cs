using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto.Generators.Templating;

/// <summary>
/// List seam: for an interpolated string's contents list (<c>SyntaxList&lt;InterpolatedStringContentSyntax&gt;</c>),
/// fold bare staged-string interpolation holes into their flanking literal text via the
/// <see cref="InterpolationFold"/> subsystem; every other list shape — and every interpolated string with no
/// foldable hole — defers to base list quoting. The <c>TNode == InterpolatedStringContentSyntax</c> guard now
/// lives inside this middleware (moved out of the quoter). Reproduces the interpolation-fold branch of the
/// dissolved <c>TemplateSyntaxQuoter.Visit&lt;TNode&gt;(SyntaxList&lt;TNode&gt;)</c>.
/// </summary>
internal sealed class InterpolationFoldMiddleware : IListQuoteMiddleware<QuoteContext>
{
    private readonly IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> _stringStagedRoots;
    private InterpolationFold? _fold;

    public InterpolationFoldMiddleware(IReadOnlyDictionary<SyntaxNode, ExpressionSyntax> stringStagedRoots)
        => _stringStagedRoots = stringStagedRoots;

    public ExpressionSyntax Quote<TNode>(SyntaxList<TNode> list, QuoteContext ctx, ListQuoteNext<TNode> next)
        where TNode : SyntaxNode
    {
        if (typeof(TNode) == typeof(InterpolatedStringContentSyntax))
        {
            // Build the fold lazily from the engine's cooperation surface (same context across all calls).
            _fold ??= new InterpolationFold(_stringStagedRoots, ctx.Quote, ctx.ToArrayLiteral);

            if (_fold.TryFoldInterpolatedContents(
                    (SyntaxList<InterpolatedStringContentSyntax>)(object)list, out var folded))
            {
                return folded;
            }
        }

        return next(list);
    }
}

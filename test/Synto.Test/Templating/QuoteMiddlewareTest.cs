extern alias SyntoCore;

using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;
using Core = SyntoCore::Synto;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

using Synto.Generators.Templating;
namespace Synto.Test.Templating;

/// <summary>
/// Per-middleware behavior tests for the 5 templating feature middleware. Each test builds the Core
/// <see cref="SyntoCore::Synto.MiddlewareSyntaxQuoter{TContext}"/> engine (reached via the <c>SyntoCore</c>
/// alias) with EXACTLY one feature middleware and asserts its effect on a minimal parsed input — proving each
/// middleware reproduces the corresponding branch of the dissolved <c>TemplateSyntaxQuoter</c>. The
/// middleware themselves live in <c>Synto.SourceGenerator</c> (global <c>Synto</c> namespace, internal).
/// </summary>
public class QuoteMiddlewareTest
{
    private static Core.MiddlewareSyntaxQuoter<Core.QuoteContext> Engine(params Core.IQuoteMiddleware[] middleware)
        => new(middleware, includeTrivia: false);

    // Replacement short-circuits to the supplied expression (the keyed node is NOT quoted).
    [Fact]
    public void Replacement_ReturnsReplacement_ForKeyedNode()
    {
        var node = ParseExpression("a + b");
        var replacements = new Dictionary<SyntaxNode, ExpressionSyntax> { [node] = IdentifierName("REPLACED") };

        var result = Engine(new ReplacementMiddleware(replacements)).Visit(node);

        Assert.Equal("REPLACED", result!.NormalizeWhitespace().ToFullString());
    }

    // Trim drops the keyed node (Visit returns null for it).
    [Fact]
    public void Trim_ReturnsNull_ForKeyedNode()
    {
        var node = ParseExpression("a");
        var trimNodes = new HashSet<SyntaxNode> { node };

        var result = Engine(new TrimMiddleware(trimNodes)).Visit(node);

        Assert.Null(result);
    }

    // Decoration wraps the base quote in a chained `.ApplyX(...)` invocation.
    [Fact]
    public void Decoration_WrapsBaseQuote_InChainedInvocation()
    {
        var node = ParseExpression("a");
        var hooks = new Dictionary<SyntaxNode, ImmutableArray<AppliedDecoration>>
        {
            [node] = ImmutableArray.Create(new AppliedDecoration("ApplyX", ImmutableArray<ExpressionSyntax>.Empty)),
        };

        var result = Engine(new DecorationMiddleware(hooks)).Visit(node);

        Assert.Contains(".ApplyX(", result!.NormalizeWhitespace().ToFullString());
    }

    // MemberSegment emits a CollectionSyntaxExtensions.BuildList<TNode> run when a child has a segment.
    [Fact]
    public void MemberSegment_EmitsBuildList_WhenChildHasSegment()
    {
        var statement = ParseStatement("a();");
        var list = SingletonList(statement);
        var memberSegments = new Dictionary<SyntaxNode, ExpressionSyntax> { [statement] = IdentifierName("SEGMENT") };

        var result = Engine(new MemberSegmentMiddleware(memberSegments)).Visit(list);

        Assert.Contains("CollectionSyntaxExtensions.BuildList<", result.NormalizeWhitespace().ToFullString());
    }

    // InterpolationFoldMiddleware defers when TNode is not InterpolatedStringContentSyntax: identical to base.
    [Fact]
    public void InterpolationFold_DefersToNext_ForNonInterpolatedList()
    {
        var statement = ParseStatement("a();");
        var list = SingletonList(statement);
        var stringStagedRoots = new Dictionary<SyntaxNode, ExpressionSyntax>();

        var engineQuote = Engine(new InterpolationFoldMiddleware(stringStagedRoots)).Visit(list);
        var baseQuote = new Core.CSharpSyntaxQuoter(includeTrivia: false).Visit(list);

        Assert.Equal(
            baseQuote.NormalizeWhitespace().ToFullString(),
            engineQuote.NormalizeWhitespace().ToFullString());
    }
}

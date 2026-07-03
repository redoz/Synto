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
/// Per-feature behavior tests for the templating quote steps, each routed through its pipeline middleware. Each
/// test builds the Core <see cref="SyntoCore::Synto.MiddlewareSyntaxQuoter{TContext}"/> engine (reached via the
/// <c>SyntoCore</c> alias) with EXACTLY one pipeline middleware (<see cref="NodePipelineMiddleware"/> or
/// <see cref="ListPipelineMiddleware"/>) whose plan map attaches a single step to the target key, asserting that
/// step's effect on a minimal parsed input. This exercises both per-key dispatch (the pipeline middleware finds —
/// or defers on — the visited node/owner) and the per-feature step behavior it dissolved from. The steps and
/// pipeline middleware live in <c>Synto.SourceGenerator</c> (global <c>Synto</c> namespace, internal).
/// </summary>
public class QuoteMiddlewareTest
{
    private static Core.MiddlewareSyntaxQuoter<Core.QuoteContext> Engine(params Core.IQuoteMiddleware[] middleware)
        => new(middleware, includeTrivia: false);

    private static Dictionary<SyntaxNode, ImmutableArray<Core.INodeQuoteMiddleware<Core.QuoteContext>>> NodeMap(
        SyntaxNode key, params Core.INodeQuoteMiddleware<Core.QuoteContext>[] steps)
        => new() { [key] = ImmutableArray.Create(steps) };

    private static Dictionary<SyntaxNode, ImmutableArray<Core.IListQuoteMiddleware<Core.QuoteContext>>> ListMap(
        SyntaxNode key, params Core.IListQuoteMiddleware<Core.QuoteContext>[] steps)
        => new() { [key] = ImmutableArray.Create(steps) };

    // ReplaceStep short-circuits to the supplied expression (the keyed node is NOT quoted); the node pipeline
    // dispatches it for the keyed node.
    [Fact]
    public void Replacement_ReturnsReplacement_ForKeyedNode()
    {
        var node = ParseExpression("a + b");
        var steps = NodeMap(node, new ReplaceStep(IdentifierName("REPLACED")));

        var result = Engine(new NodePipelineMiddleware(steps)).Visit(node);

        Assert.Equal("REPLACED", result!.NormalizeWhitespace().ToFullString());
    }

    // TrimStep drops the keyed node (the node pipeline returns null for it).
    [Fact]
    public void Trim_ReturnsNull_ForKeyedNode()
    {
        var node = ParseExpression("a");
        var steps = NodeMap(node, TrimStep.Instance);

        var result = Engine(new NodePipelineMiddleware(steps)).Visit(node);

        Assert.Null(result);
    }

    // DecorateStep wraps the base quote in a chained `.ApplyX(...)` invocation, dispatched for the keyed node.
    [Fact]
    public void Decoration_WrapsBaseQuote_InChainedInvocation()
    {
        var node = ParseExpression("a");
        var hooks = ImmutableArray.Create(new AppliedDecoration("ApplyX", ImmutableArray<ExpressionSyntax>.Empty));
        var steps = NodeMap(node, new DecorateStep(hooks));

        var result = Engine(new NodePipelineMiddleware(steps)).Visit(node);

        Assert.Contains(".ApplyX(", result!.NormalizeWhitespace().ToFullString());
    }

    // MemberSegmentStep emits a CollectionSyntaxExtensions.BuildList<TNode> run when a keyed generator child is in
    // the list; the list pipeline keys on the list's owner (the enclosing type declaration).
    [Fact]
    public void MemberSegment_EmitsBuildList_WhenChildHasSegment()
    {
        var classDecl = (ClassDeclarationSyntax)ParseMemberDeclaration("class C { void M() { } }")!;
        var method = classDecl.Members[0];
        var memberSegments = new Dictionary<SyntaxNode, ExpressionSyntax> { [method] = IdentifierName("SEGMENT") };
        var steps = ListMap(classDecl, new MemberSegmentStep(memberSegments));

        var result = Engine(new ListPipelineMiddleware(steps)).Visit(classDecl.Members);

        Assert.Contains("CollectionSyntaxExtensions.BuildList<", result.NormalizeWhitespace().ToFullString());
    }

    // InterpolationFoldStep defers when the visited list has no keyed owner: identical to the base quote. The
    // parentless statement list has a null owner, so the list pipeline finds nothing and falls through to next.
    [Fact]
    public void InterpolationFold_DefersToNext_ForNonInterpolatedList()
    {
        var statement = ParseStatement("a();");
        var list = SingletonList(statement);
        var stringStagedRoots = new Dictionary<SyntaxNode, ExpressionSyntax>();
        var steps = ListMap(ParseExpression("unrelated"), new InterpolationFoldStep(stringStagedRoots));

        var engineQuote = Engine(new ListPipelineMiddleware(steps)).Visit(list);
        var baseQuote = new Core.CSharpSyntaxQuoter(includeTrivia: false).Visit(list);

        Assert.Equal(
            baseQuote.NormalizeWhitespace().ToFullString(),
            engineQuote.NormalizeWhitespace().ToFullString());
    }
}

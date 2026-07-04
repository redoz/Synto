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
/// Per-feature behavior tests for the templating quote steps, dispatched through
/// <see cref="PerNodePipelineQuoter"/>. Each test constructs the quoter with a single step attached to the
/// target node/owner key (the other seam's map left empty), asserting that step's effect on a minimal parsed
/// input. This exercises both the quoter's per-key dispatch (it finds — or defers on — the visited node/owner)
/// and the per-feature step behavior. A final parity test pins that empty step maps quote identically to the
/// plain base quoter. The steps and the step-model interfaces both live in <c>Synto.SourceGenerator</c>
/// (namespace <c>Synto.Generators.Templating</c>, internal); the parity test's base quoter is the
/// <c>Synto</c> runtime <c>CSharpSyntaxQuoter</c> (reached via the <c>SyntoCore</c> alias).
/// </summary>
public class QuoteMiddlewareTest
{
    private static readonly Dictionary<SyntaxNode, ImmutableArray<INodeQuoteMiddleware>> EmptyNodeMap = new();
    private static readonly Dictionary<SyntaxNode, ImmutableArray<IListQuoteMiddleware>> EmptyListMap = new();

    private static PerNodePipelineQuoter NodeQuoter(SyntaxNode key, params INodeQuoteMiddleware[] steps)
        => new(
            new Dictionary<SyntaxNode, ImmutableArray<INodeQuoteMiddleware>> { [key] = ImmutableArray.Create(steps) },
            EmptyListMap,
            includeTrivia: false);

    private static PerNodePipelineQuoter ListQuoter(SyntaxNode key, params IListQuoteMiddleware[] steps)
        => new(
            EmptyNodeMap,
            new Dictionary<SyntaxNode, ImmutableArray<IListQuoteMiddleware>> { [key] = ImmutableArray.Create(steps) },
            includeTrivia: false);

    // ReplaceStep short-circuits to the supplied expression (the keyed node is NOT quoted); the quoter
    // dispatches it for the keyed node.
    [Fact]
    public void Replacement_ReturnsReplacement_ForKeyedNode()
    {
        var node = ParseExpression("a + b");
        var quoter = NodeQuoter(node, new ReplaceStep(IdentifierName("REPLACED")));

        var result = quoter.Visit(node);

        Assert.Equal("REPLACED", result!.NormalizeWhitespace().ToFullString());
    }

    // TrimStep drops the keyed node (the quoter returns null for it).
    [Fact]
    public void Trim_ReturnsNull_ForKeyedNode()
    {
        var node = ParseExpression("a");
        var quoter = NodeQuoter(node, TrimStep.Instance);

        var result = quoter.Visit(node);

        Assert.Null(result);
    }

    // DecorateStep wraps the base quote in a chained `.ApplyX(...)` invocation, dispatched for the keyed node.
    [Fact]
    public void Decoration_WrapsBaseQuote_InChainedInvocation()
    {
        var node = ParseExpression("a");
        var hooks = ImmutableArray.Create(new AppliedDecoration("ApplyX", ImmutableArray<ExpressionSyntax>.Empty));
        var quoter = NodeQuoter(node, new DecorateStep(hooks));

        var result = quoter.Visit(node);

        Assert.Contains(".ApplyX(", result!.NormalizeWhitespace().ToFullString(), System.StringComparison.Ordinal);
    }

    // MemberSegmentStep emits a CollectionSyntaxExtensions.BuildList<TNode> run when a keyed generator child is in
    // the list; the quoter keys the list on its owner (the enclosing type declaration).
    [Fact]
    public void MemberSegment_EmitsBuildList_WhenChildHasSegment()
    {
        var classDecl = (ClassDeclarationSyntax)ParseMemberDeclaration("class C { void M() { } }")!;
        var method = classDecl.Members[0];
        var memberSegments = new Dictionary<SyntaxNode, ExpressionSyntax> { [method] = IdentifierName("SEGMENT") };
        var quoter = ListQuoter(classDecl, new MemberSegmentStep(memberSegments));

        var result = quoter.Visit(classDecl.Members);

        Assert.Contains("CollectionSyntaxExtensions.BuildList<", result.NormalizeWhitespace().ToFullString(), System.StringComparison.Ordinal);
    }

    // InterpolationFoldStep defers when the visited list has no keyed owner: identical to the base quote. The
    // parentless statement list has a null owner, so the quoter finds nothing and falls through to base.
    [Fact]
    public void InterpolationFold_DefersToNext_ForNonInterpolatedList()
    {
        var statement = ParseStatement("a();");
        var list = SingletonList(statement);
        var stringStagedRoots = new Dictionary<SyntaxNode, ExpressionSyntax>();
        var quoter = ListQuoter(ParseExpression("unrelated"), new InterpolationFoldStep(stringStagedRoots));

        var pipelineQuote = quoter.Visit(list);
        var baseQuote = new Core.CSharpSyntaxQuoter(includeTrivia: false).Visit(list);

        Assert.Equal(
            baseQuote.NormalizeWhitespace().ToFullString(),
            pipelineQuote.NormalizeWhitespace().ToFullString());
    }

    // Parity (ported from the deleted engine smoke test): empty step maps quote a representative node identically
    // to a plain base quoter — the quoter's zero-step fast path is genuinely a pass-through.
    [Fact]
    public void EmptyStepMaps_QuoteIdenticallyToBase()
    {
        var node = ParseExpression("a + b * c");
        var baseQuote = new Core.CSharpSyntaxQuoter(includeTrivia: false).Visit(node);
        var pipelineQuote = new PerNodePipelineQuoter(EmptyNodeMap, EmptyListMap, includeTrivia: false).Visit(node);

        Assert.Equal(
            baseQuote!.NormalizeWhitespace().ToFullString(),
            pipelineQuote!.NormalizeWhitespace().ToFullString());
    }
}

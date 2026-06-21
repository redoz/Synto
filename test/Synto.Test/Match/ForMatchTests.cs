using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.Matching;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto.Test.Match;

/// <summary>
/// Functional + incremental + self-containment tests for the <c>ForMatch</c> incremental-provider helper:
/// the injected <c>Matched&lt;T&gt;</c> / <c>MatchPattern&lt;T&gt;</c> data surface, the generated
/// per-pattern <c>{Pattern}CouldMatch</c> predicate and <c>{Pattern}Pattern</c> descriptor, and the two
/// <c>ForMatch</c> overloads (thin + projecting). The shared <c>private partial class M</c> hosts the
/// generated matcher members; the patterns are authored once at class scope and exercised across facts.
/// </summary>
public partial class ForMatchTests
{
    // Shared matcher target — the generator emits {Pattern}/{Pattern}CouldMatch/{Pattern}Pattern into it.
    private partial class M { }

    // The shared `<a> + <b>` pattern, authored once so M.Sum / M.SumCouldMatch / M.SumPattern are available
    // class-wide. It is a phantom matcher (never invoked); the generator recognizes it structurally.
    [Match<M>(MatchOption.Single)]
    private static object Sum([Capture] int a, [Capture] int b) => a + b;

    [Fact]
    public void MatchPattern_BundlesPredicateAndMatcher()
    {
        // MatchPattern<T> is injected into THIS test assembly (internal), so we can use it directly.
        var pattern = new MatchPattern<string>(
            couldMatch: n => n is LiteralExpressionSyntax,
            match: n => n is LiteralExpressionSyntax ? n.ToString() : null);

        var lit = ParseExpression("42");
        var id = ParseExpression("x");

        Assert.True(pattern.IsMatch(lit));
        Assert.False(pattern.IsMatch(id));
        Assert.Equal("42", pattern.Match(lit));
        Assert.Null(pattern.Match(id));
    }

    [Fact]
    public void InjectedForMatchSurface_CompilesOn_NetStandard20()
    {
        // C-FM3: the injected helper surface compiles self-contained on the netstandard2.0 closure (with the
        // IsExternalInit polyfill the matching DSL injects) — no Synto runtime-package dependency.
        var diagnostics = MatchTestHarness.CompileInjectedForMatchSurfaceOnNetStandard20();

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void CouldMatch_AcceptsMatchRoot_RejectsWrongKind()
    {
        // The cheap companion predicate is the matcher's top-level type + RawKind gate only.
        Assert.True(M.SumCouldMatch(ParseExpression("x + y")));    // AddExpression
        Assert.False(M.SumCouldMatch(ParseExpression("x * y")));   // wrong kind (MultiplyExpression)
        Assert.False(M.SumCouldMatch(ParseExpression("x")));       // wrong type (not a binary expression)
    }

    [Fact]
    public void CouldMatch_IsSupersetOf_Match() // C-FM1
    {
        // CouldMatch must accept every node Match accepts — it is the matcher's own root gate.
        var root = ParseCompilationUnit("class C { object F() => (1 + 2) + foo(3 + 4); }");
        foreach (var node in root.DescendantNodesAndSelf())
            if (M.Sum(node) is not null)
                Assert.True(M.SumCouldMatch(node), $"CouldMatch must accept every node Match accepts: {node}");
    }
}

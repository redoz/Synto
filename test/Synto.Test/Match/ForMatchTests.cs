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
        // C-FM3 (data surface): the injected Matched<T> / MatchPattern<T> helper surface compiles
        // self-contained on the netstandard2.0 closure (with the IsExternalInit polyfill the matching DSL
        // injects) — no Synto runtime-package dependency.
        var diagnostics = MatchTestHarness.CompileInjectedForMatchSurfaceOnNetStandard20();

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void InjectedForMatchExtensions_CompileOn_NetStandard20()
    {
        // C-FM3 (extension surface): the injected SyntoMatchProviderExtensions wrappers over the Roslyn
        // incremental-provider API ALSO compile self-contained on the FAITHFUL netstandard2.0 closure (the
        // ns2.0 Roslyn build + its deps), alongside the data surface they reference and the polyfill. This is
        // the coverage the faithful closure unlocks: the loaded net Roslyn build threw CS1705 binding the
        // provider API against the ns2.x BCL refs, so the extensions could not previously be proven.
        var diagnostics = MatchTestHarness.CompileInjectedForMatchExtensionsOnNetStandard20();

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

    [Fact]
    public void Pattern_BundlesGeneratedPredicateAndMatcher()
    {
        // The generated {Pattern}Pattern descriptor pairs {Pattern}CouldMatch with {Pattern}.
        var add = ParseExpression("1 + 2");
        Assert.True(M.SumPattern.IsMatch(add));

        var m = M.SumPattern.Match(add);
        Assert.NotNull(m);
        MatchTestHarness.AssertCapture("1", m!.A);
        MatchTestHarness.AssertCapture("2", m.B);
    }

    [Fact]
    public void ForMatch_Thin_YieldsOneResultPerMatchedNode() // C-FM4
    {
        // The thin ForMatch yields one Matched<T> per node where the matcher succeeds. Source has two
        // add-expressions, so the consumer generator emits exactly two outputs.
        var outputs = MatchTestHarness.RunConsumerGenerator(
            new ThinConsumerGenerator(),
            "class C { int F(int a, int b) => a + b; int G(int c, int d) => c + d; }");

        Assert.Equal(2, outputs.Count);
    }

    /// <summary>Consumer generator that hooks the thin <c>ForMatch</c> overload, emitting one source per match.</summary>
    private sealed class ThinConsumerGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var matches = context.SyntaxProvider.ForMatch(M.SumPattern);
            context.RegisterSourceOutput(matches, static (productionContext, matched) =>
                productionContext.AddSource($"thin_{matched.Node.SpanStart}.g.cs", "// " + matched.Captures.A));
        }
    }

    [Fact]
    public void ForMatch_Projecting_IsCacheable_OnUnrelatedEdit() // C-FM2
    {
        // The projecting overload's only pipeline output is the equatable TResult (string). An unrelated edit
        // leaves the source tree byte-identical, so every consumer step result must come from cache.
        var (_, second) = MatchTestHarness.RunConsumerGeneratorTwice(
            new ProjectingConsumerGenerator(),
            "class C { int F(int a, int b) => a + b; int G(int c, int d) => c + d; }",
            "namespace Other { internal sealed class Unrelated { } }");

        var result = second.Results.Single();
        CacheabilityAssert.AllStepsCachedOrUnchanged(result, new[] { "consumer" });
    }

    /// <summary>Consumer generator that hooks the PROJECTING <c>ForMatch</c> overload, projecting to an equatable string.</summary>
    private sealed class ProjectingConsumerGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var projected = context.SyntaxProvider
                .ForMatch(M.SumPattern, static (matched, _) => matched.Captures.A.ToString())
                .WithTrackingName("consumer");

            context.RegisterSourceOutput(projected, static (productionContext, value) =>
                productionContext.AddSource($"proj_{value}.g.cs", "// " + value));
        }
    }

    [Fact]
    public void ForMatch_EndToEnd_GeneratesPerMatch()
    {
        // Full path: M.SumPattern -> projecting ForMatch -> RegisterSourceOutput. The projection carries both
        // capture text AND the matched node (its text + location line), proving the node is available downstream.
        var outputs = MatchTestHarness.RunConsumerGenerator(
            new EndToEndConsumerGenerator(),
            "class C\n{\n    int Sum1() => 1 + 2;\n    int Sum2() => 3 + 4;\n}");

        Assert.Equal(2, outputs.Count);
        Assert.Contains(outputs, output => output.Contains("a=1 b=2 node=1 + 2 line=2", StringComparison.Ordinal));
        Assert.Contains(outputs, output => output.Contains("a=3 b=4 node=3 + 4 line=3", StringComparison.Ordinal));
    }

    /// <summary>Realistic consumer generator: projects each Sum match to its capture text + matched-node text/location.</summary>
    private sealed class EndToEndConsumerGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var projected = context.SyntaxProvider.ForMatch(M.SumPattern, static (matched, _) =>
                new SumProjection(
                    matched.Captures.A.ToString(),
                    matched.Captures.B.ToString(),
                    matched.Node.ToString(),
                    matched.Node.GetLocation().GetLineSpan().StartLinePosition.Line));

            context.RegisterSourceOutput(projected, static (productionContext, projection) =>
                productionContext.AddSource(
                    $"sum_{projection.A}_{projection.B}.g.cs",
                    $"// a={projection.A} b={projection.B} node={projection.Node} line={projection.Line}"));
        }
    }

    /// <summary>Equatable projection of a Sum match — only equatable data crosses the pipeline boundary (C-FM2).</summary>
    private readonly record struct SumProjection(string A, string B, string Node, int Line);
}

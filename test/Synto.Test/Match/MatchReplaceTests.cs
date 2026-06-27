using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.Matching;

namespace Synto.Test.Match;

/// <summary>
/// Functional + self-containment tests for <c>MatchPattern&lt;T&gt;.Replace</c> — the consumer-facing,
/// Regex-parallel match-driven rewriter. The shared <c>private partial class M</c> hosts the generated
/// matcher members (<c>M.Sum</c> / <c>M.SumPattern</c> / <c>M.SumMatch</c>); the <c>[Match&lt;M&gt;]</c>
/// pattern is authored once at class scope (mirroring <c>ForMatchTests</c>) and exercised across facts.
/// </summary>
public partial class MatchReplaceTests
{
    // Shared matcher target — the generator emits M.Sum / M.SumCouldMatch / M.SumPattern / M.SumMatch into it.
    private partial class M { }

    // Template factory target (mirrors RoundTripTests / examples/Synto.Examples): the generator emits the
    // Factory.{Name} static factory methods into this partial.
    private static partial class Factory { }

    // Sum matches `a + b` and captures both operands (A, B : ExpressionSyntax).
    [global::Synto.Matching.Match<M>(global::Synto.Matching.MatchOption.Single)]
    private static object Sum([global::Synto.Matching.Capture] int a, [global::Synto.Matching.Capture] int b) => a + b;

    private static ExpressionSyntax Mul(global::Synto.Matching.Matched<M.SumMatch> m) =>
        SyntaxFactory.BinaryExpression(SyntaxKind.MultiplyExpression, m.Captures.A, m.Captures.B);

    [Fact]
    public void Replace_All_RewritesEveryNonNestedMatch() // C-R1 (All)
    {
        var root = SyntaxFactory.ParseExpression("f(1 + 2, 3 + 4)"); // two sibling Sum matches
        var rewritten = M.SumPattern.Replace(root, static m => Mul(m));
        Assert.Equal("f(1 * 2, 3 * 4)", rewritten.NormalizeWhitespace().ToString());
    }

    [Fact]
    public void Replace_All_OutermostWins_DoesNotRewriteInsideAReplacedSubtree() // C-R1
    {
        // The outer `(1 + 2) + (3 + 4)` matches Sum; its inner `1 + 2` / `3 + 4` are inside the
        // replaced subtree and must NOT be separately rewritten.
        var root = SyntaxFactory.ParseExpression("(1 + 2) + (3 + 4)");
        var rewritten = M.SumPattern.Replace((ExpressionSyntax)root, static m => Mul(m));
        Assert.Equal("(1 + 2) * (3 + 4)", rewritten.NormalizeWhitespace().ToString());
    }

    [Fact]
    public void Replace_NoMatch_ReturnsRootUnchanged()
    {
        var root = SyntaxFactory.ParseExpression("f(1 - 2)"); // subtraction: no Sum match
        var rewritten = M.SumPattern.Replace(root, static m => Mul(m));
        Assert.Same(root, rewritten); // C-R1/C-R2: no needless rewriting, same instance
    }

    [Fact]
    public void Replace_RootItselfMatches_ReturnsReplacementAsTRoot() // C-R2
    {
        var root = SyntaxFactory.ParseExpression("1 + 2"); // the root IS the match
        ExpressionSyntax rewritten = M.SumPattern.Replace((ExpressionSyntax)root, static m => Mul(m));
        Assert.Equal("1 * 2", rewritten.NormalizeWhitespace().ToString());
    }

    [Fact]
    public void Replace_First_RewritesOnlyTheFirstMatch() // C-R5
    {
        var root = SyntaxFactory.ParseExpression("f(1 + 2, 3 + 4)");
        var rewritten = M.SumPattern.Replace(root, static m => Mul(m), global::Synto.Matching.ReplaceOption.First);
        Assert.Equal("f(1 * 2, 3 + 4)", rewritten.NormalizeWhitespace().ToString()); // only the first Sum rewritten
    }

    [Fact]
    public void Replace_First_ShortCircuits_EvaluatorInvokedExactlyOnce() // C-R5
    {
        var root = SyntaxFactory.ParseExpression("f(1 + 2, 3 + 4)");
        var calls = 0;
        M.SumPattern.Replace(root, m => { calls++; return Mul(m); }, global::Synto.Matching.ReplaceOption.First);
        Assert.Equal(1, calls); // does not rewrite-all-then-take-first
    }

    [Fact]
    public void Replace_OverCompilationUnit_UsesNodeAndCaptures() // realistic end-to-end
    {
        // Two sibling Sum matches as invocation arguments — neither nested in the other, and the enclosing
        // invocation is not itself a Sum — so All rewrites both, in leftmost-first document order, using each
        // match's captures. (The plan's original `g(1+2) + h(3+4)` input was self-inconsistent: its top-level
        // `+` is itself a Sum, which outermost-wins replaces wholesale — see Replace_All_OutermostWins.)
        var root = SyntaxFactory.ParseCompilationUnit("class C { int F() => g(1 + 2, 3 + 4); }");
        var rewritten = M.SumPattern.Replace(root, static m => Mul(m));
        Assert.Equal("class C { int F() => g(1 * 2, 3 * 4); }",
            rewritten.NormalizeWhitespace(indentation: "", eol: " ").ToString().Replace("  ", " ").Trim());
    }

    [Fact]
    public void Replace_WithTemplateBuiltReplacement_Composes() // Matching + Templating capstone
    {
        // The replacement is built by a Synto [Template] factory rather than raw SyntaxFactory — proving the
        // open Func<Matched<T>, SyntaxNode> lets Templating compose with Matching for free.
        [global::Synto.Templating.Template(typeof(Factory), Options = global::Synto.Templating.TemplateOption.Single)]
        static void MulT([global::Synto.Templating.Splice] int a, [global::Synto.Templating.Splice] int b)
        { _ = a * b; }

        var root = SyntaxFactory.ParseExpression("g(1 + 2)");
        // Factory.MulT(...) returns the `_ = a * b;`-shaped statement; reach its right-hand multiply expression.
        var rewritten = M.SumPattern.Replace(root, static m =>
            ((AssignmentExpressionSyntax)((ExpressionStatementSyntax)Factory.MulT(m.Captures.A, m.Captures.B)).Expression).Right);

        Assert.Equal("g(1 * 2)", rewritten.NormalizeWhitespace().ToString());
    }

    [Fact]
    public void InjectedMatchReplaceSurface_CompilesOn_NetStandard20() // C-R4
    {
        var diagnostics = MatchTestHarness.CompileInjectedMatchReplaceSurfaceOnNetStandard20();
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }
}

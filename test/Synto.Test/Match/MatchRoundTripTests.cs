using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.Matching;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

// The pattern methods are phantom local functions — recognized structurally by the generator, never invoked
// — so the compiler's "unused local function" warning is expected and suppressed file-wide.
#pragma warning disable CS8321

namespace Synto.Test.Match;

/// <summary>
/// Behavioral round-trips for the matching generator: each <c>[Fact]</c> declares a phantom pattern as a
/// local function decorated with <c>[Match&lt;M&gt;]</c>, the generator lowers it into <c>M.{Pattern}</c>
/// during THIS assembly's build, and the test exercises the emitted matcher against real parsed syntax.
/// Mirrors Templating's <c>RoundTripTests</c> model — the shared <c>private static partial class M</c> stays
/// at class scope while each pattern's name/scope is local to its <c>[Fact]</c>.
/// </summary>
public partial class MatchRoundTripTests
{
    // The shared matcher target. The generator emits the matcher method + its nested result record into this
    // partial as M.{Pattern}(SyntaxNode) -> {Pattern}Match?. NON-static: a static type cannot be a generic
    // type argument (CS0718), and [Match<M>] takes M as a type argument.
    private partial class M { }

    [Fact]
    public void LiteralOne_MatchesOne_RejectsTwo()
    {
        [Match<M>(MatchOption.Single)]
        static object LiteralOne() => 1;

        // The expression-Single matcher is rooted on the handed node and structurally recognizes literal `1`.
        Assert.NotNull(M.LiteralOne(ParseExpression("1")));
        Assert.Null(M.LiteralOne(ParseExpression("2")));        // wrong literal text
        Assert.Null(M.LiteralOne(ParseExpression("1 + 1")));    // not a literal at all
    }

    [Fact]
    public void Sum_CapturesBothOperands()
    {
        [Match<M>(MatchOption.Single)]
        static object Sum([Capture] int a, [Capture] int b) => a + b;

        // Structural match of `<a> + <b>` captures BOTH operands as ExpressionSyntax, whatever their shape.
        var m = M.Sum(ParseExpression("foo() + 42"));
        Assert.NotNull(m);
        MatchTestHarness.AssertCapture("foo()", m!.A);
        MatchTestHarness.AssertCapture("42", m.B);

        Assert.Null(M.Sum(ParseExpression("foo() - 42")));   // wrong operator kind (SubtractExpression)
        Assert.Null(M.Sum(ParseExpression("foo()")));        // not a binary expression at all
    }

    [Fact]
    public void Narrowed_OnlyMatchesInvocation_AndTypesTheMember()
    {
        [Match<M>(MatchOption.Single)]
        static object Narrowed([Capture<InvocationExpressionSyntax>] object call) => call;

        var m = M.Narrowed(ParseExpression("foo(1)"));
        Assert.NotNull(m);

        // The narrowed member MUST compile as InvocationExpressionSyntax — assigning to that exact type and
        // reaching an invocation-only member proves the static type, not just the runtime value.
        InvocationExpressionSyntax call = m!.Call;
        Assert.Single(call.ArgumentList.Arguments);

        Assert.Null(M.Narrowed(ParseExpression("1 + 1")));   // not an invocation -> narrowed guard rejects it
    }

    [Fact]
    public void Wildcard_MatchesAnyRightOperand_WithoutCapturing()
    {
        [Match<M>(MatchOption.Single)]
        static object EqualsAnything([Capture] object lhs) => lhs == Expr.Any<object>();

        // Expr.Any<T>() matches ANY expression in the right slot and captures nothing; only `lhs` surfaces.
        var m = M.EqualsAnything(ParseExpression("x == foo(1, 2)"));
        Assert.NotNull(m);
        MatchTestHarness.AssertCapture("x", m!.Lhs);

        Assert.Null(M.EqualsAnything(ParseExpression("x + y")));   // not an == expression
    }

    [Fact]
    public void SelfEq_RequiresBothSidesSyntacticallyEqual()
    {
        [Match<M>(MatchOption.Single)]
        static object SelfEq([Capture] object x) => x == x;

        // The reused `x` collapses to one member + one IsEquivalentTo: both operands must be structurally equal.
        Assert.NotNull(M.SelfEq(ParseExpression("a.b == a.b")));
        Assert.Null(M.SelfEq(ParseExpression("a.b == a.c")));   // sides differ
    }

    [Fact]
    public void SelfEq3_RequiresAllThreeSidesEqual()
    {
        // Three sites of one capture -> two reuse sites in one scope. A fixed reuse-temp name would re-declare
        // (CS0128); a unique temp per site is what lets this compile. (`int` glue so `+` binds — the glue type
        // does not affect matching; the member is still ExpressionSyntax.)
        [Match<M>(MatchOption.Single)]
        static object SelfEq3([Capture] int x) => x + x + x;

        Assert.NotNull(M.SelfEq3(ParseExpression("a.b + a.b + a.b")));
        Assert.Null(M.SelfEq3(ParseExpression("a.b + a.b + a.c")));   // last side differs
    }
}

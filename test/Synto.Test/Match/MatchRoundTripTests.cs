using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.Matching;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

// The pattern methods are phantom local functions — recognized structurally by the generator, never invoked
// — so the compiler's "unused local function" warning is expected and suppressed file-wide.
#pragma warning disable CS8321
// `x == x` / `x + x` non-linear fixtures deliberately compare a capture to itself.
#pragma warning disable CS1718
// Anchored fixtures put a phantom Block.End() after a `return` — unreachable in the phantom body.
#pragma warning disable CS0162

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

    [Fact]
    public void StatementSingle_FindsLeftmostReturn_InABlock()
    {
        // A block body with one core statement -> statement-Single: root on the candidate block, scan its
        // direct statements, commit to the LEFTMOST match. (`object` return so `return result;` binds, §3.9.)
        [Match<M>(MatchOption.Single)]
        static object ReturnCapture([Capture] object result)
        { return result; }

        var m = M.ReturnCapture(ParseStatement("{ Foo(); return x + 1; return y; }"));
        Assert.NotNull(m);
        MatchTestHarness.AssertCapture("x + 1", m!.Result);   // leftmost return wins

        Assert.Null(M.ReturnCapture(ParseStatement("{ Foo(); Bar(); }")));   // no return at all
    }

    [Fact]
    public void Bare_FixedArity_MatchesContainedIfWithOneStatement()
    {
        // Bare "contains" a literal `if` whose condition is a [Capture] and whose single embedded statement is
        // a [Capture] Stmt .One(). The run is the one `if` (leftmost contained); cond + only surface.
        [Match<M>(MatchOption.Bare)]
        static void OneGuard([Capture] bool cond, [Capture] Stmt only)
        { if (cond) only.One(); }

        var m = M.OneGuard(ParseStatement("{ Pre(); if (ready) Go(); Post(); }"));
        Assert.NotNull(m);
        MatchTestHarness.AssertCapture("ready", m!.Cond);
        MatchTestHarness.AssertCapture("Go();", m.Only);

        Assert.Null(M.OneGuard(ParseStatement("{ Pre(); Post(); }")));   // no `if` to contain
    }

    [Fact]
    public void Bare_EmbeddedWildcardOne_MatchesIfWithAnyBody()
    {
        // Statement.One() is the embedded WILDCARD: it matches any single-statement `if` body, capturing only cond.
        [Match<M>(MatchOption.Bare)]
        static void IfAny([Capture] bool cond)
        { if (cond) Statement.One(); }

        var m = M.IfAny(ParseStatement("{ if (go) DoThing(); }"));
        Assert.NotNull(m);
        MatchTestHarness.AssertCapture("go", m!.Cond);

        Assert.NotNull(M.IfAny(ParseStatement("{ if (go) return; }")));   // any single embedded statement
    }

    [Fact]
    public void Bare_IfWithoutElse_RejectsIfWithElse()
    {
        // Child-count guard near-miss: the pattern `if (cond) only.One();` (no else, 5 children) must NOT match
        // an `if ... else ...` (6 children). Without the count guard the extra else is silently ignored.
        // (Distinct pattern name from Bare_FixedArity's OneGuard — same-named patterns on one target collide.)
        [Match<M>(MatchOption.Bare)]
        static void OneGuardNoElse([Capture] bool cond, [Capture] Stmt only)
        { if (cond) only.One(); }

        Assert.Null(M.OneGuardNoElse(ParseStatement("{ if (ready) Go(); else Stop(); }")));
    }

    [Fact]
    public void Bare_OneVariableLength_SplitsDeterministically()
    {
        // One variable-length element (rest.All()) after a fixed literal `if`: the single greedy split is
        // deterministic — the `if` pins the boundary, rest.All() absorbs the remainder (0+).
        [Match<M>(MatchOption.Bare)]
        static void GuardThenRest([Capture] bool cond, [Capture] Stmt guard, [Capture] Stmt rest)
        { if (cond) guard.One(); rest.All(); }

        var m = M.GuardThenRest(ParseStatement("{ if (ok) Go(); A(); B(); C(); }"));
        Assert.NotNull(m);
        MatchTestHarness.AssertCapture("ok", m!.Cond);
        MatchTestHarness.AssertCapture("Go();", m.Guard);
        Assert.Equal(3, m.Rest.Count);

        var none = M.GuardThenRest(ParseStatement("{ if (ok) Go(); }"));
        Assert.NotNull(none);
        Assert.Equal(0, none!.Rest.Count);   // All = 0+
    }

    [Fact]
    public void Bare_ReversedSignatureOrder_LocksMemberOrder()
    {
        // Walk order (cond first, in the `if` condition; rest last, the trailing run) DIFFERS from signature
        // order (rest, cond). Deconstruction proves member 0 is `rest` (a SyntaxList with .Count) — without the
        // Ordinal sort the members would be in walk order (cond first, an ExpressionSyntax) and `a.Count` is CS1061.
        [Match<M>(MatchOption.Bare)]
        static void Reversed([Capture] Stmt rest, [Capture] bool cond)
        { if (cond) { } rest.All(); }

        var m = M.Reversed(ParseStatement("{ if (go) { } A(); B(); }"));
        Assert.NotNull(m);

        var (a, b) = m!;
        Assert.Equal(2, a.Count);                 // member 0 = rest (SyntaxList<StatementSyntax>)
        MatchTestHarness.AssertCapture("go", b);   // member 1 = cond (ExpressionSyntax)
    }

    [Fact]
    public void Bare_FixedElementAfterVariable_IndexesTail()
    {
        // A fixed literal AFTER the variable element exercises the _var-relative tail index.
        [Match<M>(MatchOption.Bare)]
        static void RunThenReturn([Capture] Stmt body)
        { body.All(); return; }

        var m = M.RunThenReturn(ParseStatement("{ A(); B(); return; }"));
        Assert.NotNull(m);
        Assert.Equal(2, m!.Body.Count);

        Assert.Null(M.RunThenReturn(ParseStatement("{ A(); B(); C(); }")));   // no trailing return
    }

    [Fact]
    public void Bare_Some_RequiresAtLeastOne()
    {
        // Some = 1+: a leading fixed `if` then body.Some() requires at least one trailing statement.
        [Match<M>(MatchOption.Bare)]
        static void GuardThenSome([Capture] bool cond, [Capture] Stmt body)
        { if (cond) Statement.One(); body.Some(); }

        var m = M.GuardThenSome(ParseStatement("{ if (ok) Go(); A(); }"));
        Assert.NotNull(m);
        Assert.Equal(1, m!.Body.Count);

        Assert.Null(M.GuardThenSome(ParseStatement("{ if (ok) Go(); }")));   // Some needs >= 1
    }

    [Fact]
    public void Bare_Opt_MatchesZeroOrOne()
    {
        // Opt = 0–1: the captured member is nullable; present -> the statement, absent -> null.
        [Match<M>(MatchOption.Bare)]
        static void HeadThenOpt([Capture] bool cond, [Capture] Stmt tail)
        { if (cond) Statement.One(); tail.Opt(); }

        var present = M.HeadThenOpt(ParseStatement("{ if (ok) Go(); A(); }"));
        Assert.NotNull(present);
        Assert.NotNull(present!.Tail);
        MatchTestHarness.AssertCapture("A();", present.Tail!);

        var absent = M.HeadThenOpt(ParseStatement("{ if (ok) Go(); }"));
        Assert.NotNull(absent);
        Assert.Null(absent!.Tail);

        Assert.Null(M.HeadThenOpt(ParseStatement("{ if (ok) Go(); A(); B(); }")));   // Opt <= 1
    }

    [Fact]
    public void Bare_Exactly_CapturesFixedSlice()
    {
        // Exactly(n) is fixed-arity: it captures exactly n consecutive statements as a SyntaxList.
        [Match<M>(MatchOption.Bare)]
        static void Pair([Capture] Stmt pair)
        { pair.Exactly(2); }

        var m = M.Pair(ParseStatement("{ A(); B(); C(); }"));
        Assert.NotNull(m);
        Assert.Equal(2, m!.Pair.Count);   // leftmost two

        Assert.Null(M.Pair(ParseStatement("{ A(); }")));   // fewer than 2
    }

    [Fact]
    public void Bare_WildcardAll_MatchesAnyBlock()
    {
        // Statement.All() is the variable WILDCARD: it matches any run (0+) and captures nothing.
        [Match<M>(MatchOption.Bare)]
        static void WildAll()
        { Statement.All(); }

        Assert.NotNull(M.WildAll(ParseStatement("{ A(); B(); }")));
        Assert.NotNull(M.WildAll(ParseStatement("{ }")));   // All = 0+
    }

    [Fact]
    public void None_MatchesDeclarationWhoseBodyIsExactlyTheShape()
    {
        // None (default): root on the candidate DECLARATION, derive its body, match FULLY BOUNDED. An extra
        // trailing statement means the body is not exactly the shape -> no match.
        [Match<M>]
        static void SingleDiscard([Capture] object x)
        { _ = x; }

        var m = M.SingleDiscard(ParseMemberDeclaration("void F() { _ = y.z; }")!);
        Assert.NotNull(m);
        MatchTestHarness.AssertCapture("y.z", m!.X);

        Assert.Null(M.SingleDiscard(ParseMemberDeclaration("void F() { _ = y.z; Extra(); }")!));   // not fully bounded
    }

    [Fact]
    public void None_WithVariableElement_MatchesOneAndThreeStatementBodies()
    {
        // None + a single variable-length element: fully bounded, but the variable element absorbs the slack —
        // so it matches a 1-statement body AND a 3-statement body (a `Count == width` gate would reject both
        // but the exact one).
        [Match<M>]
        static void FirstThenRest([Capture] Stmt first, [Capture] Stmt rest)
        { first.One(); rest.All(); }

        var one = M.FirstThenRest(ParseMemberDeclaration("void F() { A(); }")!);
        Assert.NotNull(one);
        Assert.Equal(0, one!.Rest.Count);

        var three = M.FirstThenRest(ParseMemberDeclaration("void F() { A(); B(); C(); }")!);
        Assert.NotNull(three);
        MatchTestHarness.AssertCapture("A();", three!.First);
        Assert.Equal(2, three.Rest.Count);
    }

    [Fact]
    public void Anchor_End_PinsToLastStatement()
    {
        // Block.End() after the core return pins it to the LAST statement of the candidate block.
        // `Block` is qualified because `using static SyntaxFactory;` also defines a `Block` member.
        [Match<M>(MatchOption.Single)]
        static object TrailingReturn([Capture] object result)
        { return result; Synto.Matching.Block.End(); }

        Assert.NotNull(M.TrailingReturn(ParseStatement("{ Foo(); return v; }")));   // return is last
        Assert.Null(M.TrailingReturn(ParseStatement("{ return v; Foo(); }")));      // return not last
    }

    [Fact]
    public void Anchor_End_WithVariableElement_AbsorbsLead()
    {
        // anchorEnd + a leading variable element: rest.All() absorbs everything up to the trailing return, and
        // Block.End() requires that return to be the block's last statement (the C6 anchorEnd+variable case).
        [Match<M>(MatchOption.Bare)]
        static void RunThenReturnAnchored([Capture] Stmt rest)
        { rest.All(); return; Synto.Matching.Block.End(); }

        var m = M.RunThenReturnAnchored(ParseStatement("{ A(); B(); return; }"));
        Assert.NotNull(m);
        Assert.Equal(2, m!.Rest.Count);

        Assert.Null(M.RunThenReturnAnchored(ParseStatement("{ A(); return; B(); }")));   // return not last
    }
}

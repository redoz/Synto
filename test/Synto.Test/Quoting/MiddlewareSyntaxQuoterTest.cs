extern alias SyntoCore;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SyntoCore::Synto;
using Xunit;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto.Test.Quoting;

/// <summary>
/// Seam-dispatch smoke tests for the generic middleware quoting engine
/// (<see cref="SyntoCore::Synto.MiddlewareSyntaxQuoter{TContext}"/>). These prove the genuinely-new
/// behavior the engine introduces: an empty middleware list quotes identically to the plain base quoter,
/// a non-Node/List seam (Token) dispatches through the chain, and the Node chain runs in registration
/// order with short-circuit. Reached through the public Synto.Core copies via the <c>SyntoCore</c> alias.
/// </summary>
public class MiddlewareSyntaxQuoterTest
{
    // (a) No middleware => identical to a plain CSharpSyntaxQuoter on a representative node.
    [Fact]
    public void NoMiddleware_QuotesIdenticallyToBase()
    {
        var node = ParseExpression("a + b * c");
        var baseQuote = new CSharpSyntaxQuoter(includeTrivia: false).Visit(node);
        var engineQuote = new MiddlewareSyntaxQuoter<QuoteContext>(
            System.Array.Empty<IQuoteMiddleware>(), includeTrivia: false).Visit(node);

        Assert.Equal(
            baseQuote!.NormalizeWhitespace().ToFullString(),
            engineQuote!.NormalizeWhitespace().ToFullString());
    }

    // (b) A recording Token middleware IS invoked (proves a non-Node/List seam dispatches through the chain).
    [Fact]
    public void TokenMiddleware_IsInvoked()
    {
        var mw = new RecordingTokenMiddleware();
        var node = ParseExpression("a + b"); // the operator token is quoted via Visit(SyntaxToken)
        _ = new MiddlewareSyntaxQuoter<QuoteContext>(
            new IQuoteMiddleware[] { mw }, includeTrivia: false).Visit(node);

        Assert.True(mw.Invoked);
    }

    // (c) Node-chain precedence: a short-circuiting Node middleware wins over a later one
    // (proves an ordered chain + short-circuit: the later middleware never runs).
    [Fact]
    public void NodeMiddleware_ShortCircuits_LaterMiddlewareNotCalled()
    {
        var shortCircuit = new ShortCircuitNodeMiddleware(IdentifierName("SHORT"));
        var recording = new RecordingNodeMiddleware();
        var node = ParseExpression("x");

        var result = new MiddlewareSyntaxQuoter<QuoteContext>(
            new IQuoteMiddleware[] { shortCircuit, recording }, includeTrivia: false).Visit(node);

        Assert.False(recording.Invoked);
        Assert.Equal("SHORT", result!.NormalizeWhitespace().ToFullString());
    }

    private sealed class RecordingTokenMiddleware : ITokenQuoteMiddleware<QuoteContext>
    {
        public bool Invoked { get; private set; }

        public ExpressionSyntax Quote(SyntaxToken token, QuoteContext ctx, TokenQuoteNext next)
        {
            Invoked = true;
            return next(token);
        }
    }

    private sealed class RecordingNodeMiddleware : INodeQuoteMiddleware<QuoteContext>
    {
        public bool Invoked { get; private set; }

        public ExpressionSyntax? Quote(SyntaxNode? node, QuoteContext ctx, NodeQuoteNext next)
        {
            Invoked = true;
            return next(node);
        }
    }

    private sealed class ShortCircuitNodeMiddleware : INodeQuoteMiddleware<QuoteContext>
    {
        private readonly ExpressionSyntax _replacement;

        public ShortCircuitNodeMiddleware(ExpressionSyntax replacement) => _replacement = replacement;

        public ExpressionSyntax? Quote(SyntaxNode? node, QuoteContext ctx, NodeQuoteNext next)
            => _replacement;
    }
}

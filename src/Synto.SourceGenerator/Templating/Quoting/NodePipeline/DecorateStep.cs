using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto.Generators.Templating;

/// <summary>
/// Post (wrapping) Node-pipeline step: quote the node via <c>next</c>, then fold this node's decoration hooks
/// onto the result in order as a chained <c>result.&lt;HelperMethodName&gt;(args…)</c> invocation. Per-node
/// counterpart of the dissolved DecorationMiddleware. A replaced node never reaches here (a ReplaceStep earlier
/// in the node's step list short-circuits).
/// </summary>
internal sealed class DecorateStep : INodeQuoteMiddleware
{
    private readonly ImmutableArray<AppliedDecoration> _hooks;

    public DecorateStep(ImmutableArray<AppliedDecoration> hooks) => _hooks = hooks;

    public ExpressionSyntax? Quote(SyntaxNode? node, QuoteContext ctx, NodeQuoteNext next)
    {
        var result = next(node);

        if (result is null)
            return null;

        foreach (var hook in _hooks)
        {
            var arguments = new List<ArgumentSyntax>(hook.Arguments.Length);
            foreach (var arg in hook.Arguments)
                arguments.Add(Argument(arg));
            result = InvocationExpression(
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, result, IdentifierName(hook.HelperMethodName)))
                .WithArgumentList(ArgumentList(SeparatedList(arguments)));
        }

        return result;
    }
}

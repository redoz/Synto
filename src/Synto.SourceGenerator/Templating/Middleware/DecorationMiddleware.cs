using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto;

/// <summary>
/// Node seam (post): quote the node via <c>next</c>, then — if the node carries decoration hooks — fold each
/// <see cref="AppliedDecoration"/> onto the base quote in order as a chained <c>result.&lt;HelperMethodName&gt;(args…)</c>
/// invocation. Reproduces the post-quote decoration branch of the dissolved
/// <c>TemplateSyntaxQuoter.Visit(SyntaxNode?)</c>. A replaced node never reaches here (the Replacement
/// middleware short-circuits earlier in the Node chain).
/// </summary>
internal sealed class DecorationMiddleware : INodeQuoteMiddleware<QuoteContext>
{
    private readonly IReadOnlyDictionary<SyntaxNode, ImmutableArray<AppliedDecoration>> _hooks;

    public DecorationMiddleware(IReadOnlyDictionary<SyntaxNode, ImmutableArray<AppliedDecoration>> hooks)
        => _hooks = hooks;

    public ExpressionSyntax? Quote(SyntaxNode? node, QuoteContext ctx, NodeQuoteNext next)
    {
        var result = next(node);

        if (node is not null && result is not null && _hooks.TryGetValue(node, out var hooks))
        {
            foreach (var hook in hooks)
            {
                var arguments = new List<ArgumentSyntax>(hook.Arguments.Length);
                foreach (var arg in hook.Arguments)
                    arguments.Add(Argument(arg));
                result = InvocationExpression(
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, result, IdentifierName(hook.HelperMethodName)))
                    .WithArgumentList(ArgumentList(SeparatedList(arguments)));
            }
        }

        return result;
    }
}

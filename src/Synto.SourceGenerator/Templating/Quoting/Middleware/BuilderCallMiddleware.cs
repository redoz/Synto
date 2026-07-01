using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto.Generators.Templating;

/// <summary>
/// Node seam (pre, short-circuit): if the visited node is a recognized facade-call invocation, assemble the
/// fully-qualified static builder invocation over its processed arguments and return it WITHOUT quoting the
/// node (does not call <c>next</c>). Each <c>[Quoted]</c>/<c>[TypeArg]</c> island is quoted by RE-ENTERING the
/// full pipeline via <see cref="QuoteContext.Quote"/>, so nested builder calls expand and splice injections
/// inside a builder argument compose — replacing the plan-time partial-plan island sub-quoter that could not
/// see later-registered replacements (splice calls, sibling/nested builders). An unmarked parameter receives
/// the live/computed value verbatim. No copy of the builder is injected: it is called fully-qualified.
/// </summary>
internal sealed class BuilderCallMiddleware : INodeQuoteMiddleware<QuoteContext>
{
    private readonly IReadOnlyDictionary<SyntaxNode, BuilderCall> _calls;

    public BuilderCallMiddleware(IReadOnlyDictionary<SyntaxNode, BuilderCall> calls)
        => _calls = calls;

    public ExpressionSyntax? Quote(SyntaxNode? node, QuoteContext ctx, NodeQuoteNext next)
    {
        if (node is null || !_calls.TryGetValue(node, out var call))
            return next(node);

        var args = new List<ArgumentSyntax>(call.Args.Count);
        foreach (var binding in call.Args)
        {
            ExpressionSyntax argExpr = binding.Kind switch
            {
                BuilderArgKind.Quoted => ctx.Quote(binding.ValueArgument!)!,
                BuilderArgKind.QuotedTypeArg => ctx.Quote(binding.TypeArgument!)!,
                _ => binding.ValueArgument!.WithoutTrivia(),
            };
            args.Add(Argument(argExpr));
        }

        return InvocationExpression(
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    ParseName(call.BuilderFullyQualifiedTypeName),
                    IdentifierName(call.BuilderMethodName)))
            .WithArgumentList(ArgumentList(SeparatedList(args)));
    }
}

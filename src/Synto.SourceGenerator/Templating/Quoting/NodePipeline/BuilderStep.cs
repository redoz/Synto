using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto.Generators.Templating;

/// <summary>
/// Terminal (pre, short-circuit) Node-pipeline step: assemble the fully-qualified static builder invocation
/// over this node's processed arguments and return it WITHOUT quoting the node (never calls <c>next</c>).
/// Per-node counterpart of the dissolved BuilderCallMiddleware — same assembly semantics, minus the dictionary
/// lookup (the plan already resolved the single <see cref="BuilderCall"/> for this node).
/// Each <c>[Quoted]</c>/<c>[TypeArg]</c> island argument is quoted by RE-ENTERING the full pipeline via
/// <see cref="QuoteContext.Quote"/>, so nested builder calls expand and splice injections inside a builder
/// argument compose (this is what keeps bug #1/#2 fixed). Re-entry is on argument subtrees, never on the
/// invocation node itself, so there is no self-recursion. An unmarked parameter receives the live/computed
/// value verbatim.
/// </summary>
internal sealed class BuilderStep : INodeQuoteMiddleware
{
    private readonly BuilderCall _call;

    public BuilderStep(BuilderCall call) => _call = call;

    public ExpressionSyntax? Quote(SyntaxNode? node, QuoteContext ctx, NodeQuoteNext next)
    {
        var args = new List<ArgumentSyntax>(_call.Args.Count);
        foreach (var binding in _call.Args)
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
                    ParseName(_call.BuilderFullyQualifiedTypeName),
                    IdentifierName(_call.BuilderMethodName)))
            .WithArgumentList(ArgumentList(SeparatedList(args)));
    }
}

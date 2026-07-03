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
    private readonly IReadOnlyDictionary<SyntaxNode, BuilderCall> _builderCalls;

    public BuilderStep(BuilderCall call, IReadOnlyDictionary<SyntaxNode, BuilderCall> builderCalls)
    {
        _call = call;
        _builderCalls = builderCalls;
    }

    public ExpressionSyntax? Quote(SyntaxNode? node, QuoteContext ctx, NodeQuoteNext next)
    {
        var args = new List<ArgumentSyntax>(_call.Args.Count);
        foreach (var binding in _call.Args)
        {
            ExpressionSyntax argExpr = binding.Kind switch
            {
                BuilderArgKind.Quoted => ctx.Quote(binding.ValueArgument!)!,
                BuilderArgKind.QuotedTypeArg => ctx.Quote(binding.TypeArgument!)!,
                // A staged (unmarked) argument is emitted as verbatim build-time code, but any recognized builder
                // facade call NESTED inside it (e.g. `Case(...)` / `Arm(...)` inside a `.Select(c => ...)` projection
                // feeding a host builder's staged list) must still be rewritten to its real fully-qualified builder
                // invocation — otherwise the factory would reference the inert facade. Each nested call re-enters
                // the pipeline (which expands its own [Quoted] islands + deeper nested builders), leaving the
                // surrounding staged LINQ/lambda code untouched.
                _ => (ExpressionSyntax)new NestedBuilderCallRewriter(ctx, _builderCalls).Visit(binding.ValueArgument!)!.WithoutTrivia(),
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

    /// <summary>
    /// Rewrites each recognized builder-facade invocation nested inside a staged argument to its real builder call
    /// by re-entering the node pipeline (<see cref="QuoteContext.Quote"/>) on that invocation — leaving all other
    /// (LINQ / lambda / literal) staged code verbatim. Re-entry is on the nested invocation subtree, so the outer
    /// staged expression is never quoted-as-syntax.
    /// </summary>
    private sealed class NestedBuilderCallRewriter : CSharpSyntaxRewriter
    {
        private readonly QuoteContext _ctx;
        private readonly IReadOnlyDictionary<SyntaxNode, BuilderCall> _builderCalls;

        public NestedBuilderCallRewriter(QuoteContext ctx, IReadOnlyDictionary<SyntaxNode, BuilderCall> builderCalls)
        {
            _ctx = ctx;
            _builderCalls = builderCalls;
        }

        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (_builderCalls.ContainsKey(node))
                return _ctx.Quote(node);

            return base.VisitInvocationExpression(node);
        }
    }
}

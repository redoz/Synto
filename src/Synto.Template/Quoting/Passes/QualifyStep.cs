using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto.Generators.Templating.Quoting.Passes;

/// <summary>
/// Node-pipeline step that emits the authored type name in its <c>global::</c>-rooted fully-qualified form while
/// PRESERVING any nested per-node steps (e.g. a <c>[Splice]</c> type argument's <see cref="ReplaceStep"/>). Rather
/// than re-quoting the baked substitute wholesale — which would discard the authored subtree and its splices — it
/// re-quotes only the authored FINAL SIMPLE NAME through the pipeline continuation <paramref name="next"/> (so any
/// nested holes still fire), then re-parents that quote under the substitute's holes-free qualifier prefix. Sound
/// because <paramref name="next"/> is the continuation PAST this step (no self-recursion) and the base quoter
/// descends into children via the overridden Visit, re-dispatching their steps. Holds only a
/// <see cref="NameSyntax"/> value, so it is cacheability-safe.
/// </summary>
internal sealed class QualifyStep : INodeQuoteMiddleware
{
    private readonly NameSyntax _qualified;   // global::-rooted fully-qualified substitute (QualifiedNameFactory.ForSymbol)

    public QualifyStep(NameSyntax qualified) => _qualified = qualified;

    public ExpressionSyntax? Quote(SyntaxNode? node, QuoteContext ctx, NodeQuoteNext next)
    {
        // The authored node's final simple name carries the type arguments (and thus any [Splice] holes):
        // the node itself when it is a simple name, else the right-hand simple name of a written-out qualified name.
        SimpleNameSyntax authoredSimple = node as SimpleNameSyntax ?? ((QualifiedNameSyntax)node!).Right;

        // Quote it through the continuation so nested per-node steps still fire. Do NOT re-enter via ctx.Quote on
        // `node` — that re-dispatches this very step (recursion). `next` runs the chain PAST QualifyStep.
        ExpressionSyntax inner = next(authoredSimple)!;

        // Re-parent `inner` under the substitute's holes-free qualifier so the namespace / global:: prefix is
        // injected without baking over the authored (spliceable) simple name. The qualifier (`qn.Left` / alias) is
        // taken from the SYMBOL-baked substitute — holes-free for a namespace prefix, which is the overwhelmingly
        // common case. KNOWN LIMITATION (deep-review finding #2, deferred pre-1.0): if the qualifier is itself a
        // GENERIC containing type with a spliced type argument (e.g. `Outer<[Splice] X>.Inner`), that splice is
        // baked to its literal name here rather than fired — an extremely narrow, currently-unexercised shape.
        return _qualified switch
        {
            QualifiedNameSyntax qn => Invoke(nameof(QualifiedName), ctx.Quote(qn.Left)!, inner),
            AliasQualifiedNameSyntax aqn => Invoke(nameof(AliasQualifiedName), ctx.Quote(aqn.Alias)!, inner),
            _ => ctx.Quote(_qualified),   // defensive: no qualifier to inject (bare/global-namespace simple name)
        };
    }

    private static InvocationExpressionSyntax Invoke(string factoryMethod, params ExpressionSyntax[] args)
        => InvocationExpression(IdentifierName(factoryMethod))
            .WithArgumentList(ArgumentList(SeparatedList(args.Select(Argument))));
}

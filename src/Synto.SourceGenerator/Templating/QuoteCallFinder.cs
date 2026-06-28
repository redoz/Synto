using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto;

/// <summary>
/// A recognized <c>Template.Quote(value)</c> body call: the inert facade that lifts the factory-time
/// <see cref="ValueArgument"/> to syntax AT the call position (the inline counterpart to a <c>[Quote]</c>
/// parameter) and is an output-world liveness boundary — a live argument inside it does not make the enclosing
/// construct staged. The <see cref="Invocation"/> is the replacement key; <see cref="ValueArgument"/> is the
/// value lifted in its place.
/// </summary>
internal sealed class QuoteCall(InvocationExpressionSyntax invocation, ExpressionSyntax valueArgument)
{
    public InvocationExpressionSyntax Invocation { get; } = invocation;
    public ExpressionSyntax ValueArgument { get; } = valueArgument;
}

/// <summary>
/// Discovers <c>Template.Quote(value)</c> calls in a <c>[Template]</c> body (recognized by binding, mirroring how
/// <c>Template.Unquote</c> / <c>Splice</c> / <c>Member</c> / <c>TypeOf</c> are recognized). Nothing is captured
/// into pipeline state. The discovered invocation nodes are also handed to <see cref="BindingTimeClassifier"/> so
/// it can shield their arguments from liveness propagation.
/// </summary>
internal sealed class QuoteCallFinder : TemplateScopedWalker
{
    public static IReadOnlyList<QuoteCall> FindQuoteCalls(SemanticModel semanticModel, SyntaxNode node, TemplateScope scope)
    {
        var finder = new QuoteCallFinder(semanticModel, scope);
        finder.Visit(node);
        return finder._calls;
    }

    private readonly SemanticModel _semanticModel;
    private readonly INamedTypeSymbol? _templateSymbol;
    private readonly List<QuoteCall> _calls = new();

    private QuoteCallFinder(SemanticModel semanticModel, TemplateScope scope)
        : base(scope)
    {
        _semanticModel = semanticModel;
        _templateSymbol = semanticModel.Compilation.GetTypeByMetadataName(typeof(global::Synto.Templating.Template).FullName!);
    }

    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        if (_templateSymbol is not null
            && node.ArgumentList.Arguments.Count == 1
            && _semanticModel.GetSymbolInfo(node).Symbol is IMethodSymbol method
            && method.Name == nameof(global::Synto.Templating.Template.Quote)
            && SymbolEqualityComparer.Default.Equals(method.ContainingType, _templateSymbol))
        {
            _calls.Add(new QuoteCall(node, node.ArgumentList.Arguments[0].Expression));
        }

        base.VisitInvocationExpression(node);
    }
}

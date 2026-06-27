using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto;

/// <summary>
/// A recognized <c>Template.Splice(node)</c> body call: the inert facade that splices a pre-built
/// <c>ExpressionSyntax</c> verbatim into the produced syntax at the call position (the inline counterpart to a
/// <c>[Splice]</c> parameter). The <see cref="Invocation"/> is the replacement key; <see cref="Argument"/> is
/// the expression spliced in its place.
/// </summary>
internal sealed class SpliceCall(InvocationExpressionSyntax invocation, ExpressionSyntax argument)
{
    public InvocationExpressionSyntax Invocation { get; } = invocation;
    public ExpressionSyntax Argument { get; } = argument;
}

/// <summary>
/// Discovers <c>Template.Splice(node)</c> calls in a <c>[Template]</c> body (recognized by binding, mirroring
/// how <c>Template.Unquote</c> / <c>Member</c> / <c>TypeOf</c> are recognized). Nothing is captured into
/// pipeline state.
/// </summary>
internal sealed class SpliceCallFinder : CSharpSyntaxWalker
{
    public static IReadOnlyList<SpliceCall> FindSpliceCalls(SemanticModel semanticModel, SyntaxNode node)
    {
        var finder = new SpliceCallFinder(semanticModel);
        finder.Visit(node);
        return finder._calls;
    }

    private readonly SemanticModel _semanticModel;
    private readonly INamedTypeSymbol? _templateSymbol;
    private readonly List<SpliceCall> _calls = new();

    private SpliceCallFinder(SemanticModel semanticModel)
    {
        _semanticModel = semanticModel;
        _templateSymbol = semanticModel.Compilation.GetTypeByMetadataName(typeof(global::Synto.Templating.Template).FullName!);
    }

    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        if (_templateSymbol is not null
            && node.ArgumentList.Arguments.Count == 1
            && _semanticModel.GetSymbolInfo(node).Symbol is IMethodSymbol method
            && method.Name == nameof(global::Synto.Templating.Template.Splice)
            && SymbolEqualityComparer.Default.Equals(method.ContainingType, _templateSymbol))
        {
            _calls.Add(new SpliceCall(node, node.ArgumentList.Arguments[0].Expression));
        }

        base.VisitInvocationExpression(node);
    }
}

using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto;

// TODO implement these
public sealed class TokenSimplifier : CSharpSyntaxRewriter
{

}

public sealed class TriviaRemover : CSharpSyntaxRewriter
{
    private readonly SemanticModel _semanticModel;
    public bool RemoveNonEmptyTrivia { get; set; }

    public TriviaRemover(SemanticModel semanticModel, bool removeNoneEmpty = false)
    {
        this._semanticModel = semanticModel ?? throw new ArgumentNullException(nameof(semanticModel));

        if (removeNoneEmpty == false)
            throw new NotImplementedException("No support for keeping non-empty trivia.");

        this.RemoveNonEmptyTrivia = removeNoneEmpty;
    }

    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        var symbol = _semanticModel.GetSymbolInfo(node);
        
        // if there's an overload 
        return base.VisitInvocationExpression(node);
    }

}
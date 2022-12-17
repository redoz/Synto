using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SF = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto;


internal class TemplateSyntaxQuoter : CSharpSyntaxQuoter
{
    private readonly SourceFunction? _source;
    private readonly SemanticModel _semanticModel;
    private readonly IParameterSymbol[] _parameterSymbols;

    public TemplateSyntaxQuoter(SourceFunction source, SemanticModel semanticModel) : base()
    {
        this._source = source;
        this._semanticModel = semanticModel;

        // resolve the parameter symbols (we kind of assume this won't fail) 🤞
        this._parameterSymbols = source.ParameterListSyntax.Parameters.Select(paramSyntax => semanticModel.GetDeclaredSymbol(paramSyntax)!).ToArray();

    }

    public override ExpressionSyntax? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        var exprSymbol = _semanticModel.GetSymbolInfo(node.Expression);

        if (exprSymbol.Symbol is IParameterSymbol parameterSymbol && Array.FindIndex(_parameterSymbols, item => SymbolEqualityComparer.Default.Equals(item, parameterSymbol)) >= 0)
        {
            return SF.IdentifierName(parameterSymbol.Name);
        }

        return base.VisitInvocationExpression(node);
    }

    public override ExpressionSyntax? VisitIdentifierName(IdentifierNameSyntax node)
    {
        //Debugger.Launch();
        var identifierSymbol = _semanticModel.GetSymbolInfo(node);
        if (identifierSymbol.Symbol is IParameterSymbol parameterSymbol && _parameterSymbols.Contains(parameterSymbol, SymbolEqualityComparer.Default))
        {
            return SF.InvocationExpression(SF.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, node, SF.IdentifierName("ToLiteral")));
        }

        return base.VisitIdentifierName(node);
    }
}


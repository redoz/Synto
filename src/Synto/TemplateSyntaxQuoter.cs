using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SF = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto;

sealed class UnquoteFinder : CSharpSyntaxWalker
{
    private readonly SemanticModel _semanticModel;
    private List<IParameterSymbol> _unquotedIdentifiers;
    private bool _results;

    public UnquoteFinder(SemanticModel semanticModel, IEnumerable<IParameterSymbol> unquotedParameters) : base()
    {
        _semanticModel = semanticModel;
        _unquotedIdentifiers = new List<IParameterSymbol>(unquotedParameters);
    }

    public override void VisitIdentifierName(IdentifierNameSyntax node)
    {
        var symbolInfo = _semanticModel.GetSymbolInfo(node);
        if (symbolInfo.Symbol is IParameterSymbol paramSymbol && _unquotedIdentifiers.Contains(paramSymbol, SymbolEqualityComparer.Default))
            _results = true;
    }

    //public IReadOnlyList<IdentifierNameSyntax> UnquotedIdentifiers => _unquotedIdentifiers;

    public bool ContainsUnquotedParameter(SyntaxNode? node)
    {
        if (node is null)
            return false;

        this._results = false;
        this.Visit(node);
        return this._results;
    }
}

internal class TemplateSyntaxQuoter : CSharpSyntaxQuoter
{
    private readonly SourceFunction? _source;
    private readonly SemanticModel _semanticModel;
    private readonly ParameterSyntax[] _templateParams;
    private readonly IParameterSymbol[] _parameterSymbols;
    private readonly UnquoteFinder _unquoteFinder;

    public TemplateSyntaxQuoter(SourceFunction source, ParameterSyntax[] templateParams, SemanticModel semanticModel) : base()
    {
        this._source = source;
        this._semanticModel = semanticModel;
        this._templateParams = templateParams;

        // resolve the parameter symbols (we kind of assume this won't fail) 🤞
        this._parameterSymbols = source.ParameterListSyntax.Parameters.Select(paramSyntax => semanticModel.GetDeclaredSymbol(paramSyntax)!).ToArray();

        //Debugger.Launch();
        var unquotedParameters = new List<IParameterSymbol>();
        foreach (var symbol in this._parameterSymbols)
        {
            var attrs = symbol.GetAttributes();
            if (attrs.Any(attr => attr.AttributeClass?.Name == nameof(UnquoteAttribute))) // this is a bit of a dirty hack for now
            {
                unquotedParameters.Add(symbol);
            }
        }
        this._unquoteFinder = new UnquoteFinder(semanticModel, unquotedParameters);
    }

    //public override ExpressionSyntax? VisitForStatement(ForStatementSyntax node)
    //{
    //    if (_unquoteFinder.ContainsUnquotedParameter(node.Declaration) || node.Incrementors.Any(inc => _unquoteFinder.ContainsUnquotedParameter(inc)) || _unquoteFinder.ContainsUnquotedParameter(node.Condition))
    //    {
    //        //return SyntaxFactory.ExpressionStatement(node);
    //    }
    //    return base.VisitForStatement(node);
    //}

    //public override ExpressionSyntax? VisitExpressionStatement(ExpressionStatementSyntax node)
    //{
    //    if (_unquoteFinder.ContainsUnquotedParameter(node))
    //    {
            
    //    }
    //    return base.VisitExpressionStatement(node);
    //}

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
                //return SyntaxFactoryInvocation(
                //    nameof(SF.LiteralExpression),
                //    (predefined.Kind() switch
                //    {
                //        _ => SyntaxKind.StringLiteralExpression
                //    }).QuoteSyntaxKind(),
                //    SyntaxFactoryInvocation(nameof(SF.Literal), SF.IdentifierName(node.Identifier)));
            
        }

        return base.VisitIdentifierName(node);
    }
}


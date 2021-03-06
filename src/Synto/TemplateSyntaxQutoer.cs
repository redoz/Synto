using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SF = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto;

internal class TemplateSyntaxQutoer : CSharpSyntaxQuoter
{
    private readonly SourceFunction? _source;
    private readonly SemanticModel _semanticModel;
    private readonly ParameterSyntax[] _templateParams;
    private readonly IParameterSymbol[] _parameterSymbols;

    public TemplateSyntaxQutoer(SourceFunction source, ParameterSyntax[] templateParams, SemanticModel semanticModel) : base()
    {
        this._source = source;
        this._semanticModel = semanticModel;
        this._templateParams = templateParams;

        // resolve the parameter symbols (we kind of assume this won't fail) 🤞
        this._parameterSymbols = source.ParameterListSyntax.Parameters.Select(paramSyntax => semanticModel.GetDeclaredSymbol(paramSyntax)!).ToArray();

    }

    
    public override ExpressionSyntax? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        var exprSymbol = _semanticModel.GetSymbolInfo(node.Expression);

        if (exprSymbol.Symbol is IParameterSymbol parameterSymbol && Array.FindIndex(_parameterSymbols, item => SymbolEqualityComparer.Default.Equals(item, parameterSymbol)) is int index && index >= 0)
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
        // TODO prety sure i need the semantic model to check that these symbols match

        //if (parameters?.SingleOrDefault(p => p.Identifier.IsEquivalentTo(node.Identifier)) is ParameterSyntax paramSyntax && paramSyntax.Type is not null)
        //{

        //    if (paramSyntax.Type is PredefinedTypeSyntax predefined)
        //    {
        //        return SyntaxFactoryInvocation(
        //            nameof(SF.LiteralExpression),
        //            (predefined.Kind() switch
        //            {
        //                _ => SyntaxKind.StringLiteralExpression
        //            }).QuoteSyntaxKind(),
        //            SyntaxFactoryInvocation(nameof(SF.Literal), SF.IdentifierName(node.Identifier)));
        //    }
        //}

        return base.VisitIdentifierName(node);
    }
}


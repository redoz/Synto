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
    private readonly INamedTypeSymbol _templateAttributeSymbol;

    public TemplateSyntaxQuoter(SourceFunction source, SemanticModel semanticModel) : base()
    {
        this._source = source;
        this._semanticModel = semanticModel; 
        this._templateAttributeSymbol = semanticModel.Compilation.GetTypeByMetadataName(typeof(Synto.TemplateAttribute).FullName)!;

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
        var identifierSymbol = _semanticModel.GetSymbolInfo(node);
        if (identifierSymbol.Symbol is IParameterSymbol parameterSymbol && _parameterSymbols.Contains(parameterSymbol, SymbolEqualityComparer.Default))
        {
            return SF.InvocationExpression(SF.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, node, SF.IdentifierName("ToLiteral")));
        }

        return base.VisitIdentifierName(node);
    }

    public override ExpressionSyntax? VisitAttribute(AttributeSyntax node)
    {
        var symbolInfo = this._semanticModel.GetSymbolInfo(node);
        if (SymbolEqualityComparer.Default.Equals(symbolInfo.Symbol?.ContainingType, _templateAttributeSymbol))
            return null;

        return base.VisitAttribute(node);
    }

    public override ExpressionSyntax? VisitAttributeList(AttributeListSyntax node)
    {
        // strip out the Template attribute, if there are more attributes in this list the null returned from VisitAttribute will get filtered out by
        // Visit(SeparatedSyntaxList<TNode>)
        // this is a bit of a dirty hack and if we end up having to mutate the tree for other reasons we should reconsider this approach
        // ideally we could just manipulate the tree before passing it down, but that will invalidate our SemanticModel
        if (node.Attributes.Count == 1)
        {
            foreach (var attributeSyntax in node.Attributes)
            {
                var symbolInfo = this._semanticModel.GetSymbolInfo(attributeSyntax.Name);
                if (SymbolEqualityComparer.Default.Equals(symbolInfo.Symbol?.ContainingType, _templateAttributeSymbol))
                {
                    return null;
                }
            }
        }

        return base.VisitAttributeList(node);
    }
}


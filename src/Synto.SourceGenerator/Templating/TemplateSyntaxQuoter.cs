using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;


namespace Synto.Templating;

internal class TemplateSyntaxQuoter : CSharpSyntaxQuoter
{
    public new static IEnumerable<UsingDirectiveSyntax> RequiredUsings()
    {
        return CSharpSyntaxQuoter.RequiredUsings()
            .Union(new[]
            {
                UsingDirective(IdentifierName("Synto"))
            });
    }

    private readonly SemanticModel _semanticModel;
    private readonly IParameterSymbol[] _syntaxParameterSymbols;
    private readonly IParameterSymbol[] _literalParameterSymbols;
    private readonly INamedTypeSymbol _templateAttributeSymbol;

    public TemplateSyntaxQuoter(
        SemanticModel semanticModel, 
        IEnumerable<ParameterSyntax> syntaxParameterSymbols,
        IEnumerable<ParameterSyntax> literalParameterSymbols, 
        bool includeTrivia) : base(includeTrivia)
    {
        _semanticModel = semanticModel;
        _syntaxParameterSymbols = syntaxParameterSymbols.Select(paramSyntax => semanticModel.GetDeclaredSymbol(paramSyntax)!).ToArray();
        _literalParameterSymbols = literalParameterSymbols.Select(paramSyntax => semanticModel.GetDeclaredSymbol(paramSyntax)!).ToArray();
        _templateAttributeSymbol = semanticModel.Compilation.GetTypeByMetadataName(typeof(TemplateAttribute).FullName)!;
    }
    // this identifies our Syntax() and Syntax<T>() delegates SyntaxNode based parameters
    public override ExpressionSyntax? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        var exprSymbol = _semanticModel.GetSymbolInfo(node.Expression);

        if (exprSymbol.Symbol is IParameterSymbol parameterSymbol && _syntaxParameterSymbols.Contains(parameterSymbol, SymbolEqualityComparer.Default))
        {
            // since the parameter definition is already of the right type of SyntaxNode we just need to swap in the parameter name
            return IdentifierName(parameterSymbol.Name);
        }

        return base.VisitInvocationExpression(node);
    }

    // this identifies literal parameters that needs to converted to a SyntaxNode
    public override ExpressionSyntax? VisitIdentifierName(IdentifierNameSyntax node)
    {
        var identifierSymbol = _semanticModel.GetSymbolInfo(node);
        if (identifierSymbol.Symbol is IParameterSymbol parameterSymbol && _literalParameterSymbols.Contains(parameterSymbol, SymbolEqualityComparer.Default))
        {
            // since this is a literal vale we need to convert it to a SyntaxNode using our helper function ToSyntax()
            return InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, node, IdentifierName("ToSyntax")));
        }

        return base.VisitIdentifierName(node);
    }

    public override ExpressionSyntax? VisitAttribute(AttributeSyntax node)
    {
        var typeInfo = _semanticModel.GetTypeInfo(node);
        if (SymbolEqualityComparer.Default.Equals(typeInfo.Type, _templateAttributeSymbol))
            return null;

        return base.VisitAttribute(node);
    }

    public override ExpressionSyntax? VisitForEachStatement(ForEachStatementSyntax node)
    {
        if (node.Expression.DescendantNodesAndSelf(n => _syntaxParameterSymbols.Contains(_semanticModel.GetSymbolInfo(n).Symbol as IParameterSymbol, SymbolEqualityComparer.Default)).SingleOrDefault() is ParameterSyntax syntaxParam)
        {
            InvocationExpression(IdentifierName(syntaxParam.Identifier));
        }
        return base.VisitForEachStatement(node);
    }

    public override ExpressionSyntax? VisitAttributeList(AttributeListSyntax node)
    {
        // strip out the Template attribute, if there are more attributes in this list the null returned from VisitAttribute will get filtered out by
        // Visit(SeparatedSyntaxList<TNode>)
        // this is a bit of a dirty hack and if we end up having to mutate the tree for other reasons we should reconsider this approach
        // ideally we could just manipulate the tree before passing it down, but that will invalidate our SemanticModel
        //var knownAttrType = _semanticModel.Compilation.GetTypeByMetadataName(typeof(TemplateAttribute).FullName);
        if (node.Attributes.Count == 1)
        {
            foreach (var attributeSyntax in node.Attributes)
            {
                var typeInfo = _semanticModel.GetTypeInfo(attributeSyntax);
                if (SymbolEqualityComparer.Default.Equals(typeInfo.Type, _templateAttributeSymbol))
                {
                    return null;
                }
            }
        }

        return base.VisitAttributeList(node);
    }
}


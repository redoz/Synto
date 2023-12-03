using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto.Templating;

internal sealed class SyntaxParameter(ParameterSyntax parameter, IReadOnlyList<InvocationExpressionSyntax> references)
{
    public ParameterSyntax Parameter { get; } = parameter;
    public IReadOnlyList<InvocationExpressionSyntax> References { get; } = references;
}

internal class SyntaxParameterFinder : CSharpSyntaxWalker
{
    public static IEnumerable<SyntaxParameter> FindSyntaxParameters(SemanticModel semanticModel, SyntaxNode node)
    {
        // since the usage might be before the discovery this is a two phase operation
        var finder = new SyntaxParameterFinder(semanticModel);
        finder.Visit(node);

        foreach (var typeParameterBySymbol in finder._parameterBySymbol)
        {
            if (finder._replacementsBySymbol.TryGetValue(typeParameterBySymbol.Key, out var replacements))
            {
                yield return new(typeParameterBySymbol.Value, replacements);
            }
        }
    }

    private readonly SemanticModel _semanticModel;
    private readonly INamedTypeSymbol _syntaxDelegateSymbol;
    private readonly INamedTypeSymbol _syntaxOfTDelegateSymbol;

    private readonly Dictionary<ISymbol, ParameterSyntax> _parameterBySymbol;
    private readonly Dictionary<ISymbol, List<InvocationExpressionSyntax>> _replacementsBySymbol;

    protected SyntaxParameterFinder(SemanticModel semanticModel)
    {
        _semanticModel = semanticModel;
        var syntaxDelegateSymbol = semanticModel.Compilation.GetTypeByMetadataName(typeof(Syntax).FullName!)!;
        var syntaxOfTDelegateSymbol = semanticModel.Compilation.GetTypeByMetadataName(typeof(Syntax<>).FullName!)!;

        Debug.Assert(syntaxDelegateSymbol is not null);
        Debug.Assert(syntaxOfTDelegateSymbol is not null);

        _syntaxDelegateSymbol = syntaxDelegateSymbol!;
        _syntaxOfTDelegateSymbol = syntaxOfTDelegateSymbol!.ConstructUnboundGenericType();

        _replacementsBySymbol = new Dictionary<ISymbol, List<InvocationExpressionSyntax>>(SymbolEqualityComparer.Default);
        _parameterBySymbol = new Dictionary<ISymbol, ParameterSyntax>(SymbolEqualityComparer.Default);
    }

    public override void DefaultVisit(SyntaxNode node)
    {
        if (_parameterBySymbol.Count > 0 && node is InvocationExpressionSyntax invocationExpressionSyntax)
        {
            var symbolInfo = _semanticModel.GetSymbolInfo(invocationExpressionSyntax.Expression);
            if (symbolInfo.Symbol is { } symbol && _replacementsBySymbol.TryGetValue(symbol, out var replacementList))
            {
                replacementList.Add(invocationExpressionSyntax);
                return;
            }
        }

        base.DefaultVisit(node);
    }

    public override void VisitParameter(ParameterSyntax node)
    {
        var typeSymbol = _semanticModel.GetDeclaredSymbol(node);
        if (typeSymbol is null)
            return;

        if (typeSymbol.Type is INamedTypeSymbol {IsGenericType: true} namedTypeSymbol && SymbolEqualityComparer.Default.Equals(namedTypeSymbol.ConstructUnboundGenericType(), _syntaxOfTDelegateSymbol))
        {
            _parameterBySymbol.Add(typeSymbol, node);
            _replacementsBySymbol.Add(typeSymbol, []);
        }
        else if (SymbolEqualityComparer.Default.Equals(typeSymbol.Type, _syntaxDelegateSymbol))
        {
            _parameterBySymbol.Add(typeSymbol, node);
            _replacementsBySymbol.Add(typeSymbol, []);
        }
    }
}
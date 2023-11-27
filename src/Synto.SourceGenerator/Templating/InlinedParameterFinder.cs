using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto.Templating;

internal sealed class InlinedParameter(ParameterSyntax parameter, IReadOnlyList<TypeSyntax> references, bool asSyntax)
{
    public ParameterSyntax Parameter { get; } = parameter;
    public IReadOnlyList<TypeSyntax> References { get; } = references;
    public bool AsSyntax { get; } = asSyntax;
}


internal class InlinedParameterFinder : CSharpSyntaxWalker
{
    private sealed class InlinedParameterInfo(ParameterSyntax parameter, bool asSyntax)
    {
        public ParameterSyntax Parameter { get; } = parameter;
        public bool AsSyntax { get; } = asSyntax;
    }

    public static IEnumerable<InlinedParameter> FindInlinedParameters(SemanticModel semanticModel, SyntaxNode node)
    {
        // since the usage might be before the discovery this is a two phase operation
        var finder = new InlinedParameterFinder(semanticModel);
        finder.Visit(node);

        foreach (var typeParameterBySymbol in finder._parameterBySymbol)
        {
            if (finder._replacementsBySymbol.TryGetValue(typeParameterBySymbol.Key, out var replacements))
            {
                yield return new(typeParameterBySymbol.Value.Parameter, replacements, typeParameterBySymbol.Value.AsSyntax);
            }
        }
    }

    private readonly SemanticModel _semanticModel;
    private readonly INamedTypeSymbol _inlineAttributeSymbol;

    private readonly Dictionary<ISymbol, InlinedParameterInfo> _parameterBySymbol;
    private readonly Dictionary<ISymbol, List<IdentifierNameSyntax>> _replacementsBySymbol;

    protected InlinedParameterFinder(SemanticModel semanticModel)
    {
        _semanticModel = semanticModel;
        var attributeSymbol = semanticModel.Compilation.GetTypeByMetadataName(typeof(InlineAttribute).FullName!)!;
        Debug.Assert(attributeSymbol is not null);

        _inlineAttributeSymbol = attributeSymbol!;
        _replacementsBySymbol = new Dictionary<ISymbol, List<IdentifierNameSyntax>>(SymbolEqualityComparer.Default);
        _parameterBySymbol = new Dictionary<ISymbol, InlinedParameterInfo>(SymbolEqualityComparer.Default);
    }

    public override void DefaultVisit(SyntaxNode node)
    {
        if (_parameterBySymbol.Count > 0 && node is IdentifierNameSyntax identifierNameSyntax)
        {
            //Debugger.Launch();
            var symbolInfo = _semanticModel.GetSymbolInfo(identifierNameSyntax);
            if (symbolInfo.Symbol is { } symbol && _replacementsBySymbol.TryGetValue(symbol, out var replacementList))
            {
                replacementList.Add(identifierNameSyntax);
                return;
            }
        }

        base.DefaultVisit(node);
    }

    public override void VisitParameter(ParameterSyntax node)
    {
        foreach (var attributeList in node.AttributeLists)
        {
            foreach (var attributeSyntax in attributeList.Attributes)
            {
                var typeInfo = _semanticModel.GetTypeInfo(attributeSyntax);
                if (!SymbolEqualityComparer.Default.Equals(typeInfo.Type, _inlineAttributeSymbol))
                    continue;

                var typeSymbol = _semanticModel.GetDeclaredSymbol(node);

                if (typeSymbol is null)
                    continue;

                var asSyntax = attributeSyntax.GetNamedArgumentOrDefault<bool>(nameof(InlineAttribute.AsSyntax), _semanticModel, false);

                _parameterBySymbol.Add(typeSymbol, new InlinedParameterInfo(node, asSyntax));
                _replacementsBySymbol.Add(typeSymbol, new List<IdentifierNameSyntax>());
            }
        }

        base.VisitParameter(node);
    }
}
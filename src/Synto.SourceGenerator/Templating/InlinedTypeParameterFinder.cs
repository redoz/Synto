using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto;

internal sealed class InlinedTypeParameter(ITypeParameterSymbol typeParameterSymbol, TypeParameterSyntax typeParameterSyntax, IReadOnlyList<TypeSyntax> references, bool asSyntax)
{
    public ITypeParameterSymbol TypeParameterSymbol { get; } = typeParameterSymbol;
    public TypeParameterSyntax TypeParameterSyntax { get; } = typeParameterSyntax;
    public IReadOnlyList<TypeSyntax> References { get; } = references;
    public bool AsSyntax { get; } = asSyntax;
}

internal sealed class InlinedTypeParameterFinder : CSharpSyntaxWalker
{
    private sealed class InlinedTypeParameterInfo(TypeParameterSyntax typeParameter, bool asSyntax)
    {
        public TypeParameterSyntax TypeParameter { get; } = typeParameter;
        public bool AsSyntax { get; } = asSyntax;
    }

    private enum Phase
    {
        IdentifyTypeParameters,
        FindTypeParameterReferences
    }

    public static IEnumerable<InlinedTypeParameter> FindInlinedTypeParameters(SemanticModel semanticModel, SyntaxNode node)
    {
        // since the usage might be before the discovery this is a two phase operation
        var finder = new InlinedTypeParameterFinder(semanticModel);
        finder.IdentifyInlineTypeParameters(node);
        finder.FindTypeParameterReferences(node);

        foreach (var typeParameterBySymbol in finder._typeParameterBySymbol)
        {
            //typeParameterBySymbol.Value.AttributeLists.SelectMany(al => al.Attributes).Single(attr => )
            if (finder._replacementsBySymbol.TryGetValue(typeParameterBySymbol.Key, out var replacements))
            {
                yield return new (typeParameterBySymbol.Key, typeParameterBySymbol.Value.TypeParameter, replacements, typeParameterBySymbol.Value.AsSyntax);
            }
        }
    }

    private void FindTypeParameterReferences(SyntaxNode node)
    {
        _phase = Phase.FindTypeParameterReferences;
        Visit(node);
    }

    private void IdentifyInlineTypeParameters(SyntaxNode node)
    {
        _phase = Phase.IdentifyTypeParameters;
        Visit(node);
    }

    private readonly SemanticModel _semanticModel;
    private readonly INamedTypeSymbol _inlineAttributeSymbol;

    private readonly Dictionary<ITypeParameterSymbol, InlinedTypeParameterInfo> _typeParameterBySymbol;
    private readonly Dictionary<ITypeParameterSymbol, List<TypeSyntax>> _replacementsBySymbol;

    private Phase _phase;


    private InlinedTypeParameterFinder(SemanticModel semanticModel)
    {
        _semanticModel = semanticModel;
        var attributeSymbol = semanticModel.Compilation.GetTypeByMetadataName(typeof(InlineAttribute).FullName!)!;
        Debug.Assert(attributeSymbol is not null);

        _inlineAttributeSymbol = attributeSymbol!;
        _replacementsBySymbol = new Dictionary<ITypeParameterSymbol, List<TypeSyntax>>(SymbolEqualityComparer.Default);
        _typeParameterBySymbol = new Dictionary<ITypeParameterSymbol, InlinedTypeParameterInfo>(SymbolEqualityComparer.Default);
    }

    public override void DefaultVisit(SyntaxNode node)
    {
        if (_phase == Phase.FindTypeParameterReferences && node is TypeSyntax typeSyntax)
        {
            //Debugger.Launch();
            var symbolInfo = _semanticModel.GetSymbolInfo(typeSyntax);
            if (symbolInfo.Symbol is ITypeParameterSymbol symbol && _replacementsBySymbol.TryGetValue(symbol, out var replacementList))
            {
                replacementList.Add(typeSyntax);
                return;
            }
        }

        base.DefaultVisit(node);
    }

    public override void VisitTypeParameter(TypeParameterSyntax node)
    {
        if (_phase == Phase.IdentifyTypeParameters)
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

                    var attrData = typeSymbol.GetAttributes().Single(attr => SymbolEqualityComparer.Default.Equals(_inlineAttributeSymbol, attr.AttributeClass));

                    bool asSyntax = false;

                    foreach (var kvp in attrData.NamedArguments)
                    {
                        if (StringComparer.Ordinal.Equals(nameof(InlineAttribute.AsSyntax), kvp.Key))
                        {
                            asSyntax = (bool)kvp.Value.Value!;
                        }
                    }

                    _typeParameterBySymbol.Add(typeSymbol, new InlinedTypeParameterInfo(node, asSyntax));
                    _replacementsBySymbol.Add(typeSymbol, new List<TypeSyntax>());
                }
            }
        }

        base.VisitTypeParameter(node);
    }
}
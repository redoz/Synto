using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.Templating;

namespace Synto;

/// <summary>
/// A resolved staged generic type parameter: either an <c>[Unquote]</c> type (value-lift — the supplied
/// <c>System.Type</c> is converted via <c>typeof(T).ToTypeSyntax()</c> at factory time) or a <c>[Splice]</c>
/// type (verbatim — a pre-built <c>TypeSyntax</c> is supplied to the factory and spliced as-is). Every
/// <see cref="References"/> use is in TYPE position, so a splice lands as <c>TypeSyntax</c> (the fix for the
/// type-axis miscompile where a spliced type was previously emitted as <c>ExpressionSyntax</c>).
/// </summary>
internal sealed class StagedTypeParameter(ITypeParameterSymbol typeParameterSymbol, TypeParameterSyntax typeParameterSyntax, IReadOnlyList<TypeSyntax> references, bool isSplice)
{
    public ITypeParameterSymbol TypeParameterSymbol { get; } = typeParameterSymbol;
    public TypeParameterSyntax TypeParameterSyntax { get; } = typeParameterSyntax;
    public IReadOnlyList<TypeSyntax> References { get; } = references;

    /// <summary><c>true</c> for <c>[Splice]</c> (verbatim TypeSyntax); <c>false</c> for <c>[Unquote]</c> (value lift).</summary>
    public bool IsSplice { get; } = isSplice;
}

/// <summary>
/// Discovers <c>[Unquote]</c> / <c>[Splice]</c> generic type parameters in a <c>[Template]</c> body and their
/// type-position use-sites. Two-phase (identify type parameters, then find references) because a use can
/// precede the declaration during the walk; nothing is captured into pipeline state.
/// </summary>
internal sealed class StagedTypeParameterFinder : TemplateScopedWalker
{
    private sealed class Info(TypeParameterSyntax typeParameter, bool isSplice)
    {
        public TypeParameterSyntax TypeParameter { get; } = typeParameter;
        public bool IsSplice { get; } = isSplice;
    }

    private enum Phase
    {
        IdentifyTypeParameters,
        FindTypeParameterReferences,
    }

    public static IEnumerable<StagedTypeParameter> FindStagedTypeParameters(SemanticModel semanticModel, SyntaxNode node, TemplateScope scope)
    {
        var finder = new StagedTypeParameterFinder(semanticModel, scope);
        finder._phase = Phase.IdentifyTypeParameters;
        finder.Visit(node);
        finder._phase = Phase.FindTypeParameterReferences;
        finder.Visit(node);

        foreach (var typeParameterBySymbol in finder._typeParameterBySymbol)
        {
            if (finder._replacementsBySymbol.TryGetValue(typeParameterBySymbol.Key, out var replacements))
                yield return new(typeParameterBySymbol.Key, typeParameterBySymbol.Value.TypeParameter, replacements, typeParameterBySymbol.Value.IsSplice);
        }
    }

    private readonly SemanticModel _semanticModel;
    private readonly INamedTypeSymbol? _unquoteAttributeSymbol;
    private readonly INamedTypeSymbol? _spliceAttributeSymbol;

    private readonly Dictionary<ITypeParameterSymbol, Info> _typeParameterBySymbol;
    private readonly Dictionary<ITypeParameterSymbol, List<TypeSyntax>> _replacementsBySymbol;

    private Phase _phase;

    private StagedTypeParameterFinder(SemanticModel semanticModel, TemplateScope scope)
        : base(scope)
    {
        _semanticModel = semanticModel;
        _unquoteAttributeSymbol = semanticModel.Compilation.GetTypeByMetadataName(typeof(UnquoteAttribute).FullName!);
        _spliceAttributeSymbol = semanticModel.Compilation.GetTypeByMetadataName(typeof(SpliceAttribute).FullName!);

        _replacementsBySymbol = new Dictionary<ITypeParameterSymbol, List<TypeSyntax>>(SymbolEqualityComparer.Default);
        _typeParameterBySymbol = new Dictionary<ITypeParameterSymbol, Info>(SymbolEqualityComparer.Default);
    }

    public override void DefaultVisit(SyntaxNode node)
    {
        if (_phase == Phase.FindTypeParameterReferences && node is TypeSyntax typeSyntax)
        {
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
                    var attributeType = _semanticModel.GetTypeInfo(attributeSyntax).Type;

                    bool isSplice;
                    if (_spliceAttributeSymbol is not null && SymbolEqualityComparer.Default.Equals(attributeType, _spliceAttributeSymbol))
                        isSplice = true;
                    else if (_unquoteAttributeSymbol is not null && SymbolEqualityComparer.Default.Equals(attributeType, _unquoteAttributeSymbol))
                        isSplice = false;
                    else
                        continue;

                    if (_semanticModel.GetDeclaredSymbol(node) is not { } typeSymbol || _typeParameterBySymbol.ContainsKey(typeSymbol))
                        continue;

                    _typeParameterBySymbol.Add(typeSymbol, new Info(node, isSplice));
                    _replacementsBySymbol.Add(typeSymbol, new List<TypeSyntax>());
                }
            }
        }

        base.VisitTypeParameter(node);
    }
}

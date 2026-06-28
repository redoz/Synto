using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.Templating;

namespace Synto;

/// <summary>
/// A resolved <c>[Splice]</c> value parameter: a pre-built <c>ExpressionSyntax</c> supplied to the factory at
/// invocation time and spliced into the output VERBATIM (no evaluation, no value lift). The parameter becomes
/// a factory parameter typed <c>ExpressionSyntax</c> and every <see cref="References"/> use is replaced as-is.
/// </summary>
internal sealed class SpliceParameter(ParameterSyntax parameter, IReadOnlyList<IdentifierNameSyntax> references)
{
    public ParameterSyntax Parameter { get; } = parameter;
    public IReadOnlyList<IdentifierNameSyntax> References { get; } = references;
}

/// <summary>
/// Discovers <c>[Splice]</c> value parameters in a <c>[Template]</c> body and their use-sites. Two-phase
/// (find-then-replace) because a use can precede the parameter discovery during the single tree walk; nothing
/// is captured into pipeline state.
/// </summary>
internal sealed class SpliceParameterFinder : TemplateScopedWalker
{
    public static IEnumerable<SpliceParameter> FindSpliceParameters(SemanticModel semanticModel, SyntaxNode node, TemplateScope scope)
    {
        var finder = new SpliceParameterFinder(semanticModel, scope);
        finder.Visit(node);

        foreach (var parameterBySymbol in finder._parameterBySymbol)
        {
            if (finder._replacementsBySymbol.TryGetValue(parameterBySymbol.Key, out var replacements))
                yield return new(parameterBySymbol.Value, replacements);
        }
    }

    private readonly SemanticModel _semanticModel;
    private readonly INamedTypeSymbol? _spliceAttributeSymbol;

    private readonly Dictionary<ISymbol, ParameterSyntax> _parameterBySymbol;
    private readonly Dictionary<ISymbol, List<IdentifierNameSyntax>> _replacementsBySymbol;

    private SpliceParameterFinder(SemanticModel semanticModel, TemplateScope scope)
        : base(scope)
    {
        _semanticModel = semanticModel;
        _spliceAttributeSymbol = semanticModel.Compilation.GetTypeByMetadataName(typeof(SpliceAttribute).FullName!);
        Debug.Assert(_spliceAttributeSymbol is not null);

        _replacementsBySymbol = new Dictionary<ISymbol, List<IdentifierNameSyntax>>(SymbolEqualityComparer.Default);
        _parameterBySymbol = new Dictionary<ISymbol, ParameterSyntax>(SymbolEqualityComparer.Default);
    }

    public override void DefaultVisit(SyntaxNode node)
    {
        if (_parameterBySymbol.Count > 0 && node is IdentifierNameSyntax identifierNameSyntax)
        {
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
        if (_spliceAttributeSymbol is not null)
        {
            foreach (var attributeList in node.AttributeLists)
            {
                foreach (var attributeSyntax in attributeList.Attributes)
                {
                    if (!SymbolEqualityComparer.Default.Equals(_semanticModel.GetTypeInfo(attributeSyntax).Type, _spliceAttributeSymbol))
                        continue;

                    if (_semanticModel.GetDeclaredSymbol(node) is not { } parameterSymbol || _parameterBySymbol.ContainsKey(parameterSymbol))
                        continue;

                    _parameterBySymbol.Add(parameterSymbol, node);
                    _replacementsBySymbol.Add(parameterSymbol, new List<IdentifierNameSyntax>());
                }
            }
        }

        base.VisitParameter(node);
    }
}

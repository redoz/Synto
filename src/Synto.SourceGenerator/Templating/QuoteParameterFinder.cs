using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.Templating;

namespace Synto;

/// <summary>
/// A resolved <c>[Quote]</c> value parameter: a factory-time value supplied to the factory at invocation time
/// and <em>lifted</em> into the output (via the shared value→syntax helper) exactly like an <c>[Unquote]</c>
/// value, but <b>never</b> a staging root — control flow driven only by it stays a runtime construct. The
/// parameter becomes a factory parameter typed <typeparamref name="T"/> and every <see cref="References"/> use is
/// replaced by the lifted syntax.
/// </summary>
internal sealed class QuoteParameter(ParameterSyntax parameter, IReadOnlyList<IdentifierNameSyntax> references)
{
    public ParameterSyntax Parameter { get; } = parameter;
    public IReadOnlyList<IdentifierNameSyntax> References { get; } = references;
}

/// <summary>
/// Discovers <c>[Quote]</c> value parameters in a <c>[Template]</c> body and their use-sites. Two-phase
/// (find-then-replace) because a use can precede the parameter discovery during the single tree walk; nothing is
/// captured into pipeline state. Mirrors <see cref="SpliceParameterFinder"/> but keyed off
/// <see cref="QuoteAttribute"/>; crucially the discovered symbols are NEVER seeded into
/// <see cref="BindingTimeClassifier"/>, so a control construct referencing only a quoted value stays
/// <c>Quoted</c> (no unroll).
/// </summary>
internal sealed class QuoteParameterFinder : CSharpSyntaxWalker
{
    public static IEnumerable<QuoteParameter> FindQuoteParameters(SemanticModel semanticModel, SyntaxNode node)
    {
        var finder = new QuoteParameterFinder(semanticModel);
        finder.Visit(node);

        foreach (var parameterBySymbol in finder._parameterBySymbol)
        {
            if (finder._replacementsBySymbol.TryGetValue(parameterBySymbol.Key, out var replacements))
                yield return new(parameterBySymbol.Value, replacements);
        }
    }

    private readonly SemanticModel _semanticModel;
    private readonly INamedTypeSymbol? _quoteAttributeSymbol;

    private readonly Dictionary<ISymbol, ParameterSyntax> _parameterBySymbol;
    private readonly Dictionary<ISymbol, List<IdentifierNameSyntax>> _replacementsBySymbol;

    private QuoteParameterFinder(SemanticModel semanticModel)
    {
        _semanticModel = semanticModel;
        _quoteAttributeSymbol = semanticModel.Compilation.GetTypeByMetadataName(typeof(QuoteAttribute).FullName!);
        Debug.Assert(_quoteAttributeSymbol is not null);

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
        if (_quoteAttributeSymbol is not null)
        {
            foreach (var attributeList in node.AttributeLists)
            {
                foreach (var attributeSyntax in attributeList.Attributes)
                {
                    if (!SymbolEqualityComparer.Default.Equals(_semanticModel.GetTypeInfo(attributeSyntax).Type, _quoteAttributeSymbol))
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

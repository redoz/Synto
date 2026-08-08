using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto.GenKit;

/// <summary>
/// Template-agnostic rewriter that rebinds every reference to a mapped root symbol into a target identifier name
/// (identity when the symbol is not mapped). The right-hand member of a <c>a.Member</c> access is left untouched
/// so only the accessed root — never a member name — is renamed. Shared by the template staging path
/// (root → factory-parameter rename) and the model path (<c>SyntoModel&lt;T&gt;.Symbol</c> → <c>From</c> parameter).
/// </summary>
internal class SymbolReferenceRenamer : CSharpSyntaxRewriter
{
    private readonly SemanticModel _semanticModel;
    private readonly IReadOnlyDictionary<ISymbol, string> _rootNames;

    public SymbolReferenceRenamer(SemanticModel semanticModel, IReadOnlyDictionary<ISymbol, string> rootNames)
    {
        _semanticModel = semanticModel;
        _rootNames = rootNames;
    }

    public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
    {
        if (node.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == node)
            return base.VisitIdentifierName(node);

        var symbol = _semanticModel.GetSymbolInfo(node).Symbol;
        if (symbol is not null && _rootNames.TryGetValue(symbol, out var name))
            return IdentifierName(name);

        return base.VisitIdentifierName(node);
    }
}

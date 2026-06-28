using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto;

/// <summary>Rewrites live-root identifier references to their factory parameter names (identity when unchanged).</summary>
internal sealed class RootRenameRewriter : CSharpSyntaxRewriter
{
    private readonly SemanticModel _semanticModel;
    private readonly IReadOnlyDictionary<ISymbol, string> _rootNames;

    public RootRenameRewriter(SemanticModel semanticModel, IReadOnlyDictionary<ISymbol, string> rootNames)
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

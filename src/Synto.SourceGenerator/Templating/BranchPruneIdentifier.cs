using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto.Templating;

internal class BranchPruneIdentifier : CSharpSyntaxWalker
{
    public static HashSet<SyntaxNode> FindPrunableNodes(IEnumerable<SyntaxNode> prunableNodes, SyntaxNode node)
    {
        BranchPruneIdentifier self = new BranchPruneIdentifier(prunableNodes);
        self.Visit(node);
        return self._prunableNodes;
    }
    
    private readonly HashSet<SyntaxNode> _prunableNodes;

    protected BranchPruneIdentifier(IEnumerable<SyntaxNode> prunableNodes)
    {
        _prunableNodes = new HashSet<SyntaxNode>(prunableNodes);
    }

    public override void DefaultVisit(SyntaxNode node)
    {
        base.DefaultVisit(node);

        bool all = false;
        foreach (var childNode in node.ChildNodes())
        {
            all = _prunableNodes.Contains(childNode);
            if (!all)
                break;
        }

        if (all)
        {
            _prunableNodes.Add(node);
            // not strictly necessary, but made it it a bit easier to debug
            foreach (var childNode in node.ChildNodes()) 
                _prunableNodes.Remove(childNode);
        }
    }

    public override void VisitParameterList(ParameterListSyntax node)
    {
        // we can't prune parameter lists, they can be empty, but they're not optional
        return;
    }
}
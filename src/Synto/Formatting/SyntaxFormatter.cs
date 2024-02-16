using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto.Formatting;

public class SyntaxFormatter : CSharpSyntaxRewriter
{
    private int _offset;
    private int _depth;
    private int _indentation;
    private bool _indentOn;
    private readonly Stack<int> _listCount = new();

    public static TSyntax Format<TSyntax>(TSyntax node) where TSyntax : SyntaxNode
    {
        var formatter = new SyntaxFormatter();
        return (TSyntax)formatter.Visit(node)!;
    }

    public override SyntaxNode? VisitReturnStatement(ReturnStatementSyntax node)
    {
        if (node is null) throw new ArgumentNullException(nameof(node));

        // omegahack
        if (_indentation == 0)
            _indentation = node.SyntaxTree.GetLineSpan(node.Span).StartLinePosition.Character / 2;

        return base.VisitReturnStatement(node);
    }

    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        if (node is null) throw new ArgumentNullException(nameof(node));

        bool disableIndent = false;
        if (_indentOn == false)
        {
            disableIndent = _indentOn = true;
            _offset = node.SyntaxTree.GetLineSpan(node.Span).StartLinePosition.Character;
            _depth = 0;
        }

        var ret = base.VisitInvocationExpression(node);

        if (disableIndent)
        {
            _indentOn = false;
        }
        return ret;
    }
    private SyntaxTriviaList GetIndentTrivia()
    {
        return SyntaxFactory.ParseLeadingTrivia('\n' + new string(' ', _offset + _indentation * _depth));
    }

    public override TNode? VisitListElement<TNode>(TNode? node) where TNode : class
    {
        var ret = base.VisitListElement(node);

        if (_indentOn
            && ret is not null
            && (_listCount.Peek() > 1
                || ret.DescendantTrivia(descendIntoTrivia: true).Any(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia))))
        {
            ret = ret.WithLeadingTrivia(GetIndentTrivia());
        }

        return ret;
    }
    public override SeparatedSyntaxList<TNode> VisitList<TNode>(SeparatedSyntaxList<TNode> list)
    {
        _depth++;
        _listCount.Push(list.Count);
        var ret = base.VisitList(list);
        _listCount.Pop();
        _depth--;
        return ret;
    }
}
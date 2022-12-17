using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// ReSharper disable once CheckNamespace
namespace Synto.Utils;

internal class SyntaxFormatter : CSharpSyntaxRewriter
{
    private int _offset;
    private int _depth;
    private int _indentation;
    private bool _indentOn;

    public static TSyntax Format<TSyntax>(TSyntax node) where TSyntax : SyntaxNode
    {
        var formatter = new SyntaxFormatter();
        return (TSyntax)formatter.Visit(node)!;
    }

    public override SyntaxNode? VisitReturnStatement(ReturnStatementSyntax node)
    {
        // omegahack
        if (_indentation == 0) 
            _indentation = node.GetLeadingTrivia().Last().Span.Length / 2;

        return base.VisitReturnStatement(node);
    }

    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        bool disableIndent = false;
        if (this._indentOn == false)
        {
            disableIndent = this._indentOn = true;
            this._offset = node.SyntaxTree.GetLineSpan(node.Span).StartLinePosition.Character;
            this._depth = 0;
        }

        var ret = base.VisitInvocationExpression(node);

        if (disableIndent)
        {
            this._indentOn = false;
        }
        return ret;
    }

    public override SyntaxNode? VisitArgument(ArgumentSyntax node)
    {
        var ret = base.VisitArgument(node);
        if (this._indentOn && ret is not null && (node.Parent is ArgumentListSyntax { Arguments.Count: > 1 } || ret.DescendantTrivia(descendIntoTrivia: true).Any(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia))))
        {
            ret = ret.WithLeadingTrivia(GetIndentTrivia());
        }

        return ret;
    }

    private SyntaxTriviaList GetIndentTrivia()
    {
        return SyntaxFactory.ParseLeadingTrivia(Environment.NewLine + new string(' ', this._offset + this._indentation * this._depth));
    }

    public override TNode? VisitListElement<TNode> (TNode? node) where TNode : class
    {
        var ret = base.VisitListElement(node);
        if (this._indentOn && ret is not null && (node!.Parent is InitializerExpressionSyntax { Expressions: { Count: > 1} } || ret.DescendantTrivia(descendIntoTrivia: true).Any(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia))))
            ret = ret?.WithLeadingTrivia(GetIndentTrivia());
        return ret;
    }

    public override SeparatedSyntaxList<TNode> VisitList<TNode>(SeparatedSyntaxList<TNode> list)
    {
        this._depth++;
        var ret = base.VisitList(list);
        this._depth--;
        return ret;
    }
}
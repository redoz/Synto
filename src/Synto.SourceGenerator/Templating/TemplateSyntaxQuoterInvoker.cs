using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Diagnostics;
using Synto.Templating;

namespace Synto;

internal sealed class TemplateSyntaxQuoterInvoker : CSharpSyntaxWalker
{
    private readonly TemplateSyntaxQuoter _quoter;
    private readonly TemplateInfo _template;

    private TemplateSyntaxQuoterInvoker(TemplateSyntaxQuoter quoter, TemplateInfo template) : base(SyntaxWalkerDepth.Node)
    {
        _quoter = quoter;
        _template = template;
    }

    public static bool TryQuote(TemplateSyntaxQuoter quoter, TemplateInfo template, out ExpressionSyntax? expressionSyntax, out TypeSyntax? returnType, out DiagnosticInfo? error)
    {
        var selector = new TemplateSyntaxQuoterInvoker(quoter, template);
        selector.Visit(template.Source!.Syntax);

        expressionSyntax = selector.Expression;
        returnType = selector.ReturnType;
        error = selector.Error;

        return expressionSyntax is not null;
    }

    public ExpressionSyntax? Expression { get; private set; }
    public TypeSyntax? ReturnType { get; private set; }
    public DiagnosticInfo? Error { get; private set; }

    public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        => VisitMethodLike(node, node.Body, node.ExpressionBody);

    public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        => VisitMethodLike(node, node.Body, node.ExpressionBody);

    /// <summary>
    /// Shared selection for the two method-shaped carriers (a <c>[Template]</c> method and a local function):
    /// in <c>Bare</c> mode quote the block body (or the expression body's expression); otherwise quote the whole
    /// declaration node.
    /// </summary>
    private void VisitMethodLike(SyntaxNode node, BlockSyntax? body, ArrowExpressionClauseSyntax? expressionBody)
    {
        if (_template.Options.HasFlag(TemplateOption.Bare))
        {
            if (body is not null)
                VisitMethodBody(body);
            else if (expressionBody is not null)
            {
                Expression = _quoter.Visit(expressionBody.Expression);
                ReturnType = TypeNameOf(expressionBody.Expression);
            }
        }
        else
        {
            Expression = _quoter.Visit(node);
            ReturnType = TypeNameOf(node);
        }
    }

    /// <summary>The <c>ParseTypeName(node.GetType().FullName!)</c> idiom: the fully-qualified Roslyn type of a node.</summary>
    private static TypeSyntax TypeNameOf(SyntaxNode node)
        => SyntaxFactory.ParseTypeName(node.GetType().FullName!);

    private void VisitMethodBody(BlockSyntax body)
    {
        Debug.Assert(_template.Options.HasFlag(TemplateOption.Bare));

        if (body!.Statements.Count == 0)
        {
            Error = TemplateDiagnostics.BareSourceCannotBeEmpty(_template.Source!);
        }
        else if ((_template.Options & TemplateOption.Single) == TemplateOption.Single)
        {
            if (body.Statements.Count == 1)
            {
                Expression = _quoter.Visit(body.Statements[0]);
                ReturnType = TypeNameOf(body.Statements[0]);
            }
            else
            {
                Error = TemplateDiagnostics.MultipleStatementsNotAllowed(_template.Source!);
            }
        }
        else
        {
            Expression = _quoter.Visit(body);
            ReturnType = SyntaxFactory.ParseTypeName(typeof(BlockSyntax).FullName!);
        }
    }


    public override void VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        VisitTypeDeclaration(node);
    }

    public override void VisitStructDeclaration(StructDeclarationSyntax node)
    {
        VisitTypeDeclaration(node);
    }

    public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
    {
        VisitTypeDeclaration(node);
    }

    private void VisitTypeDeclaration(TypeDeclarationSyntax node)
    {
        if (_template.Options.HasFlag(TemplateOption.Bare))
        {
            if (node.Members.Count == 0)
            {
                Error = TemplateDiagnostics.BareSourceCannotBeEmpty(_template.Source!);
            }
            else if ((_template.Options & TemplateOption.Single) == TemplateOption.Single)
            {
                if (node.Members.Count == 1)
                {
                    Expression = _quoter.Visit(node.Members[0]);
                    ReturnType = TypeNameOf(node.Members[0]);
                }
                else
                {
                    Error = TemplateDiagnostics.MultipleMembersNotAllowed(_template.Source!);
                }
            }
            else
            {
                Expression = _quoter.Visit(node.Members);

                ReturnType = SyntaxFactory.ParseTypeName("Microsoft.CodeAnalysis.SyntaxList<Microsoft.CodeAnalysis.CSharp.Syntax.MemberDeclarationSyntax>");
            }
        }
        else
        {
            Expression = _quoter.Visit(node);
            ReturnType = TypeNameOf(node);
        }
    }


}

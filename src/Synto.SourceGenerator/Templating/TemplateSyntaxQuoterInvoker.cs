using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Diagnostics;

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

    public static bool TryQuote(TemplateSyntaxQuoter quoter, TemplateInfo template, out ExpressionSyntax? expressionSyntax, out TypeSyntax? returnType, out Diagnostic? error)
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
    public Diagnostic? Error { get; private set; }

    public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        if (_template.Options.HasFlag(TemplateOption.Bare))
        {
            if (node.Body is not null)
                VisitMethodBody(node.Body);
            else if (node.ExpressionBody is not null)
            {
                Expression = _quoter.Visit(node.ExpressionBody.Expression);
                ReturnType = SyntaxFactory.ParseTypeName(node.ExpressionBody.Expression.GetType().FullName!);
            }
        }
        else
        {
            Expression = _quoter.Visit(node);
            ReturnType = SyntaxFactory.ParseTypeName(node.GetType().FullName!);
        }
    }

    public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
    {
        if (_template.Options.HasFlag(TemplateOption.Bare))
        {
            if (node.Body is not null)
                VisitMethodBody(node.Body);
            else if (node.ExpressionBody is not null)
            {
                Expression = _quoter.Visit(node.ExpressionBody.Expression);
                ReturnType = SyntaxFactory.ParseTypeName(node.ExpressionBody.Expression.GetType().FullName!);
            }
        }
        else
        {
            Expression = _quoter.Visit(node);
            ReturnType = SyntaxFactory.ParseTypeName(node.GetType().FullName!);
        }
    }

    private void VisitMethodBody(BlockSyntax body)
    {
        Debug.Assert(_template.Options.HasFlag(TemplateOption.Bare));

        if (body!.Statements.Count == 0)
        {
            Error = Diagnostics.BareSourceCannotBeEmpty(_template.Source!);
        }
        else if ((_template.Options & TemplateOption.Single) == TemplateOption.Single)
        {
            if (body.Statements.Count == 1)
            {
                Expression = _quoter.Visit(body.Statements[0]);
                ReturnType = SyntaxFactory.ParseTypeName(body.Statements[0].GetType().FullName!);
            }
            else
            {
                Error = Diagnostics.MultipleStatementsNotAllowed(_template.Source!);
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
                Error = Diagnostics.BareSourceCannotBeEmpty(_template.Source!);
            }
            else if ((_template.Options & TemplateOption.Single) == TemplateOption.Single)
            {
                if (node.Members.Count == 1)
                {
                    Expression = _quoter.Visit(node.Members[0]);
                    ReturnType = SyntaxFactory.ParseTypeName(node.Members[0].GetType().FullName!);
                }
                else
                {
                    Error = Diagnostics.MultipleMembersNotAllowed(_template.Source!);
                }
            }
            else
            {
                Debugger.Launch();
                Expression = _quoter.Visit(node.Members);
                
                ReturnType = SyntaxFactory.ParseTypeName("Microsoft.CodeAnalysis.SyntaxList<Microsoft.CodeAnalysis.CSharp.Syntax.MemberDeclarationSyntax>");
            }
        }
        else
        {
            Expression = _quoter.Visit(node);
            ReturnType = SyntaxFactory.ParseTypeName(node.GetType().FullName!);
        }
    }


}
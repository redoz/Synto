using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Diagnostics;
using Synto.Templating;
using Synto.Generators.Templating.Emit;
using Synto.Generators.Templating.Diagnostics;

namespace Synto.Generators.Templating.Quoting;

internal sealed class TemplateSyntaxQuoterInvoker : CSharpSyntaxWalker
{
    private readonly CSharpSyntaxQuoter _quoter;
    private readonly TemplateInfo _template;

    private TemplateSyntaxQuoterInvoker(CSharpSyntaxQuoter quoter, TemplateInfo template) : base(SyntaxWalkerDepth.Node)
    {
        _quoter = quoter;
        _template = template;
    }

    /// <summary>
    /// Quotes the template carrier into a factory-body expression. Contract: returning <c>false</c>
    /// (no quoted <paramref name="expressionSyntax"/>) ALWAYS yields a non-null <paramref name="error"/>
    /// — every failure path is explained by a diagnostic, never a silent no-output. Callers therefore
    /// report <paramref name="error"/> unconditionally when this returns <c>false</c>.
    /// </summary>
    public static bool TryQuote(CSharpSyntaxQuoter quoter, TemplateInfo template, out ExpressionSyntax? expressionSyntax, out TypeSyntax? returnType, out DiagnosticInfo? error)
    {
        var selector = new TemplateSyntaxQuoterInvoker(quoter, template);
        selector.Visit(template.Source!.Syntax);

        expressionSyntax = selector.Expression;
        returnType = selector.ReturnType;
        error = selector.Error;

        // Contract guard: no quoted expression must be accompanied by a diagnostic (no silent failure).
        Debug.Assert(expressionSyntax is not null || error is not null,
            "TryQuote returned false without a diagnostic — a silent no-output path was reached.");

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
            else
            {
                // Bodyless carrier (partial-definition / extern / abstract): there is nothing to quote in
                // Bare mode. Surface it as the SY1005 "empty Bare source" diagnostic rather than falling
                // through with both Expression and Error null (which the caller reads as a silent no-output).
                Error = TemplateDiagnostics.BareSourceCannotBeEmpty(_template.Source!);
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

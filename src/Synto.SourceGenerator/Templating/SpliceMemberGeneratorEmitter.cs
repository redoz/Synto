using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto;

/// <summary>
/// Emits a valid <c>[Splice]</c> member generator as factory-time code (member axis, spec §4): a non-static local
/// function carrying the generator's body VERBATIM, appended to the factory preamble, plus the member segment it
/// contributes. Runs inside the generator transform; nothing captured into pipeline state.
/// </summary>
internal static class SpliceMemberGeneratorEmitter
{
    /// <summary>
    /// Emits a valid <c>[Splice]</c> member generator as factory-time code: a non-static local function carrying
    /// the generator's body VERBATIM (its <c>Parameter&lt;&gt;()</c> declarations dropped, since those values fold
    /// into the factory parameters and are captured in scope here), appended to the factory <paramref name="preamble"/>.
    /// Returns the member segment the generator contributes to its enclosing type's member list: a
    /// <c>ListSegment&lt;MemberDeclarationSyntax&gt;.Run(localFn())</c> for an enumerable shape, or the local-function
    /// call directly (implicitly a single-node segment) for a single member.
    /// </summary>
    public static ExpressionSyntax Emit(
        SpliceMemberGenerator generator,
        SemanticModel semanticModel,
        List<StatementSyntax> preamble,
        ref int counter)
    {
        var methodSymbol = semanticModel.GetDeclaredSymbol(generator.Method);
        var templateSymbol = semanticModel.Compilation.GetTypeByMetadataName(typeof(global::Synto.Templating.Template).FullName!);
        var rewriter = new SpliceGeneratorBodyRewriter(semanticModel, templateSymbol);

        string localName = "__spliceGenerator_" + generator.Method.Identifier.Text + "_" + counter++;

        // Fully-qualified return type so it resolves in the generated factory regardless of its usings.
        TypeSyntax returnType = methodSymbol?.ReturnType is { } returnTypeSymbol
            ? ParseTypeName(returnTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            : generator.Method.ReturnType;

        BlockSyntax body;
        if (generator.Method.Body is { } block)
            body = (BlockSyntax)rewriter.Visit(block)!;
        else if (generator.Method.ExpressionBody is { } arrow)
            body = Block(ReturnStatement((ExpressionSyntax)rewriter.Visit(arrow.Expression)!.WithoutTrivia()));
        else
            body = Block();

        // Non-static local function so it captures the folded factory parameters (a `static` local function could
        // not reach them).
        var localFunction = LocalFunctionStatement(returnType, Identifier(localName))
            .WithParameterList(ParameterList())
            .WithBody(body);

        preamble.Add(localFunction);

        ExpressionSyntax call = InvocationExpression(IdentifierName(localName));

        if (generator.ReturnShape == SpliceMemberReturnShape.Enumerable)
        {
            // CollectionSyntaxExtensions.ListSegment<MemberDeclarationSyntax>.Run(localFn())
            return InvocationExpression(
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            IdentifierName(nameof(CollectionSyntaxExtensions)),
                            GenericName(Identifier(nameof(CollectionSyntaxExtensions.ListSegment<MemberDeclarationSyntax>)))
                                .WithTypeArgumentList(TypeArgumentList(SingletonSeparatedList<TypeSyntax>(IdentifierName(nameof(MemberDeclarationSyntax)))))),
                        IdentifierName(nameof(CollectionSyntaxExtensions.ListSegment<MemberDeclarationSyntax>.Run))))
                .AddArgumentListArguments(Argument(call));
        }

        // Single shape: the call returns a MemberDeclarationSyntax → implicit ListSegment conversion.
        return call;
    }

    /// <summary>
    /// Rewrites a <c>[Splice]</c> member generator body into factory-time code: drops each
    /// <c>var x = Parameter&lt;T&gt;();</c> declaration (the value folds into a captured factory parameter of the
    /// same name) and replaces any inline <c>Parameter&lt;T&gt;("name")</c> call with its resolved parameter
    /// identifier. Everything else is preserved verbatim — the generator's <c>foreach</c> stays a real loop.
    /// </summary>
    private sealed class SpliceGeneratorBodyRewriter : CSharpSyntaxRewriter
    {
        private readonly SemanticModel _semanticModel;
        private readonly INamedTypeSymbol? _templateSymbol;

        public SpliceGeneratorBodyRewriter(SemanticModel semanticModel, INamedTypeSymbol? templateSymbol)
        {
            _semanticModel = semanticModel;
            _templateSymbol = templateSymbol;
        }

        public override SyntaxNode? VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
        {
            if (node.Declaration.Variables.Count == 1
                && node.Declaration.Variables[0].Initializer?.Value is InvocationExpressionSyntax invocation
                && IsParameterCall(invocation))
            {
                return null;
            }

            return base.VisitLocalDeclarationStatement(node);
        }

        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (IsParameterCall(node)
                && node.ArgumentList.Arguments.Count == 1
                && _semanticModel.GetConstantValue(node.ArgumentList.Arguments[0].Expression) is { HasValue: true, Value: string name })
            {
                return IdentifierName(name);
            }

            return base.VisitInvocationExpression(node);
        }

        private bool IsParameterCall(InvocationExpressionSyntax node)
        {
            return _templateSymbol is not null
                && _semanticModel.GetSymbolInfo(node).Symbol is IMethodSymbol method
                && method.Name == nameof(global::Synto.Templating.Template.Parameter)
                && method.TypeArguments.Length == 1
                && SymbolEqualityComparer.Default.Equals(method.ContainingType, _templateSymbol);
        }
    }
}

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using Synto.Formatting;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto.Diagnostics;

[Generator(LanguageNames.CSharp)]
public sealed class DiagnosticsGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(InjectAttribute);

        var syntaxProvider = context.SyntaxProvider.ForAttributeWithMetadataName(
                "Synto.Diagnostics.DiagnosticAttribute",
                static (node, _) => true,
                static (syntaxContext, cancellationToken) => TargetInfo.Create(syntaxContext, cancellationToken))
            .Where(templateInfo => templateInfo is not null);

        var assemblyName = context.CompilationProvider.Select(static (c, _) => c.AssemblyName);

        var providerWithCompilation = syntaxProvider.Combine(assemblyName);

        context.RegisterImplementationSourceOutput(providerWithCompilation, Execute);
    }

    private void InjectAttribute(IncrementalGeneratorPostInitializationContext context)
    {
        context.AddSource(
            "Synto.Diagnostics.DiagnosticsAttribute.g.cs",
            """
            #nullable enable
            using System;
            using System.Collections.Generic;
            using Microsoft.CodeAnalysis;
            
            namespace Synto.Diagnostics;
            
            [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
            public sealed class DiagnosticAttribute(
                string id,
                string title,
                string messageFormat,
                string category,
                DiagnosticSeverity defaultSeverity,
                bool isEnabledByDefault,
                string? description = null,
                string? helpLinkUri = null,
                params string[] customTags)
                : Attribute
            {
                public string Id { get; } = id;
                public string Title { get; } = title;
                public string MessageFormat { get; } = messageFormat;
                public string Category { get; } = category;
                public DiagnosticSeverity DefaultSeverity { get; } = defaultSeverity;
                public bool IsEnabledByDefault { get; } = isEnabledByDefault;
                public string? Description { get; } = description;
                public string? HelpLinkUri { get; } = helpLinkUri;
                public IReadOnlyList<string> CustomTags { get; } = customTags;
            }
            """
            );
    }

    private void Execute(SourceProductionContext context, (TargetInfo? TemplateInfo, string? AssemblyName) value)
    {
        if (value.TemplateInfo is null || value.AssemblyName is null)
            return;

        try
        {
            if (ValidateTarget(context, value.AssemblyName, value.TemplateInfo))
                ProcessTarget(context, value.TemplateInfo);
        }
#pragma warning disable CA1031 // we're explicitly catching _any_ exception and converting it to a diagnostic message
        catch (Exception ex)
#pragma warning restore CA1031
        {
            context.ReportDiagnostic(Diagnostics.InternalError(ex));
        }
    }

    private static bool ValidateTarget(SourceProductionContext context, string assemblyName, TargetInfo targetInfo)
    {

        if (!targetInfo.Target.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            context.ReportDiagnostic(Diagnostics.TargetNotPartial(targetInfo.Target.GetLocation(), targetInfo.Target.Identifier.Text));
            return false;
        }

        bool EnsureAncestryIsPartial(MethodDeclarationSyntax methodDeclarationSyntax)
        {
            bool ret = true;
            var parent = methodDeclarationSyntax.Parent;
            while (parent is ClassDeclarationSyntax parentClass)
            {
                if (!parentClass.Modifiers.Any(SyntaxKind.PartialKeyword))
                {
                    context.ReportDiagnostic(Diagnostics.TargetAncestorNotPartial(parentClass.GetLocation(), targetInfo.Target.Identifier.Text, parentClass.Identifier.Text));
                    ret = false;
                }

                parent = parentClass.Parent;
            }

            return ret;
        }

        if (!EnsureAncestryIsPartial(targetInfo.Target))
            return false;

        return true;
    }

    private static void ProcessTarget(SourceProductionContext context, TargetInfo targetInfo)
    {
        CancellationToken cancellationToken = context.CancellationToken;

        var targetMethod = targetInfo.Target;
        var semanticModel = targetInfo.SemanticModel;

        var parent = (ClassDeclarationSyntax)targetMethod.Parent!;

        var methodSymbol = semanticModel.GetDeclaredSymbol(targetMethod);

        Debug.Assert(methodSymbol is not null);

        var parentSymbol = semanticModel.GetDeclaredSymbol(parent);

        Debug.Assert(parentSymbol is not null);

        UsingDirectiveSet additionalUsings = new UsingDirectiveSet([]);


        // there's gotta be a better way to get a typename than parsing the typeof().FullName?
        List<MemberDeclarationSyntax> members = new();

        IEnumerable<ArgumentSyntax> arguments = targetInfo
            .AttributeSyntax
            .ArgumentList!
            .Arguments
            .Select(aas =>
            {
                switch (aas.Expression)
                {
                    case LiteralExpressionSyntax literal:
                        return Argument(literal);

                    // this case only handles enums, we could probably also add a branch to handle constants
                    // that would make the generated code a bit closer to what was originally typed
                    // but, we'd have to identity the constant field and route the type name through the AdditionalUsingsSet
                    // then rebuild the MemberAccessExpressionSyntax
                    case MemberAccessExpressionSyntax
                    {
                        Expression: var maybeEnumExpr
                    }
                        when semanticModel.GetSymbolInfo(maybeEnumExpr, cancellationToken)
                            is
                        {
                            Symbol: INamedTypeSymbol
                            {
                                EnumUnderlyingType: not null
                            }
                        }:
                        // in theory, we should probably pull get the full name from this type symbol and rebuild this expression via the AdditionalUsingsSet
                        // we get away with not doing this because the only enum arg in our Attribute is defined in a known namespace
                        return Argument(aas.Expression);
                    default:
                        {
                            var optionalConst = semanticModel.GetConstantValue(aas.Expression, cancellationToken);
                            Debug.Assert(optionalConst.HasValue);
                            return Argument(optionalConst.Value.ToSyntax());
                        }
                }
            });
        var descriptorName = Identifier("_" + targetMethod.Identifier);
        members.Add(
            FieldDeclaration(
                new SyntaxList<AttributeListSyntax>(),
                TokenList(
                    Token(SyntaxKind.PrivateKeyword),
                    Token(SyntaxKind.StaticKeyword)),
                VariableDeclaration(
                    additionalUsings.GetTypeName(
                        ParseName(typeof(DiagnosticDescriptor).FullName!)),
                    SeparatedList(
                        [
                            VariableDeclarator(
                                descriptorName,
                                null,
                                EqualsValueClause(
                                    ImplicitObjectCreationExpression(
                                        ArgumentList(SeparatedList(arguments)),
                                        initializer: null)))
                        ]
                    ))));

        ExpressionSyntax? locationExpr = null;
        ExpressionSyntax? severityExpr = null;
        ExpressionSyntax additionalLocationsExpr = LiteralExpression(SyntaxKind.NullLiteralExpression);
        ExpressionSyntax propertiesExpr = LiteralExpression(SyntaxKind.NullLiteralExpression);

        List<ExpressionSyntax> messageArgExprs = [];

        var locationTypeSymbol = semanticModel.Compilation.GetTypeByMetadataName(typeof(Location).FullName!);
        var diagnosticSeverityTypeSymbol = semanticModel.Compilation.GetTypeByMetadataName(typeof(DiagnosticSeverity).FullName!);

        foreach (ParameterSyntax parameter in targetMethod.ParameterList.Parameters)
        {
            var paramSymbol = semanticModel.GetDeclaredSymbol(parameter, cancellationToken);
            Debug.Assert(paramSymbol is not null);

            if (SymbolEqualityComparer.Default.Equals(paramSymbol!.Type, locationTypeSymbol))
            {
                locationExpr = IdentifierName(parameter.Identifier);
            }
            else if (SymbolEqualityComparer.Default.Equals(paramSymbol!.Type, diagnosticSeverityTypeSymbol))
            {
                severityExpr = IdentifierName(parameter.Identifier);
            }
            else
            {
                messageArgExprs.Add(IdentifierName(parameter.Identifier));
            }
        }

        locationExpr ??= LiteralExpression(SyntaxKind.NullLiteralExpression);
        severityExpr ??= LiteralExpression(SyntaxKind.NullLiteralExpression);

        members.Add(targetMethod
            .WithSemicolonToken(Token(SyntaxKind.None))
            .WithAttributeLists([]) // clear the attribute
            .WithBody(
                Block(
                    ReturnStatement(
                        InvocationExpression(
                            MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                additionalUsings.GetTypeName(
                                    ParseName(typeof(Diagnostic).FullName!)
                                ),
                                IdentifierName("Create")),
                            ArgumentList(
                                SeparatedList([
                                    Argument(IdentifierName(descriptorName)),
                                    Argument(locationExpr),
                                    Argument(severityExpr),
                                    Argument(additionalLocationsExpr),
                                    Argument(propertiesExpr),
                                    .. messageArgExprs.Select(Argument)
                                ]))))
                )));


        // clone the parent but with the new member
        var classDecl = parent.WithMembers(List(members));

        var targetSyntax = classDecl.WithAncestryFrom(parentSymbol!);

        var compilationUnit = CompilationUnit()
            .AddMembers(targetSyntax)
            .WithUsings(List(additionalUsings.Union(
                [
                    //UsingDirective(ParseName(typeof(Diagnostic).Namespace!))
                ]
            )))
            .WithLeadingTrivia(
                TriviaList(
                    Trivia(
                        NullableDirectiveTrivia(
                            Token(SyntaxKind.EnableKeyword),
                            true))));

        var sourceText = SyntaxFormatter.Format(compilationUnit.NormalizeWhitespace()).GetText(Encoding.UTF8);

        context.AddSource(methodSymbol!.ToGeneratedFilename(), sourceText);
    }

}
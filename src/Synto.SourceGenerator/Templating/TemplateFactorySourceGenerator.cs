﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.Formatting;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto.Templating;


[Generator(LanguageNames.CSharp)]
public class TemplateFactorySourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var syntaxProvider = context.SyntaxProvider.ForAttributeWithMetadataName(
                typeof(TemplateAttribute).FullName!,
                static (node, cancellationToken) => true,
                static (syntaxContext, cancellationToken) => TemplateInfo.Create(syntaxContext))
            .Where(templateInfo => templateInfo is not null);

        var assemblyName = context.CompilationProvider.Select(static (c, _) => c.AssemblyName);

        var providerWithCompilation = syntaxProvider.Combine(assemblyName);

        context.RegisterSourceOutput(providerWithCompilation, Execute);
    }

    private void Execute(SourceProductionContext context, (TemplateInfo TemplateInfo, string AssemblyName) value)
    {
        
        try
        {
            // var semanticModel = context.Compilation.GetSemanticModel(template.AttributeSyntax.SyntaxTree);
            //var runtimeKey = template.AttributeSyntax.GetNamedArgument<string>(nameof(TemplateAttribute.Runtime), semanticModel) is { HasValue: true } optional ? optional.Value : RuntimeAttribute.Default;
            //if (!runtimeTypes.TryGetValue(runtimeKey, out var runtimeTypeList))
            //    runtimeTypeList = new List<TypeDeclarationSyntax>();
            if (ValidateTemplate(context, value.AssemblyName, value.TemplateInfo))
                ProcessTemplate(context, value.TemplateInfo);
        }
        catch (Exception ex)
        {
            context.ReportDiagnostic(Diagnostics.InternalError(ex));
        }
    }


    private static bool ValidateTemplate(SourceProductionContext context, string assemblyName, TemplateInfo template)
    {
        if (template.Target.Type.DeclaringSyntaxReferences.FirstOrDefault() is not { } syntaxRef)
        {
            context.ReportDiagnostic(Diagnostics.TargetNotDeclaredInSource(template.Target, assemblyName));
            return false;
        }

        if (syntaxRef.GetSyntax() is not ClassDeclarationSyntax classSyntax)
        {
            context.ReportDiagnostic(Diagnostics.TargetNotClass(template.Target));
            return false;
        }

        if (!classSyntax.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            context.ReportDiagnostic(Diagnostics.TargetNotPartial(template.Target));
            return false;
        }

        bool EnsureAncestryIsPartial(ClassDeclarationSyntax classDeclarationSyntax)
        {
            bool ret = true;
            var parent = classDeclarationSyntax.Parent;
            while (parent is ClassDeclarationSyntax parentClass)
            {
                if (!parentClass.Modifiers.Any(SyntaxKind.PartialKeyword))
                {
                    context.ReportDiagnostic(Diagnostics.TargetAncestorNotPartial(template.Target, parentClass.Identifier.Text));
                    ret = false;
                }

                parent = parentClass.Parent;
            }

            return ret;
        }

        if (!EnsureAncestryIsPartial(classSyntax))
            return false;

        return true;
    }

    private static void ProcessTemplate(SourceProductionContext context, TemplateInfo template)
    {
        // we use this to collect additional usings that are required throughout the source-generation process
        UsingDirectiveSet additionalUsings = new UsingDirectiveSet(CSharpSyntaxQuoter.RequiredUsings());

        MethodDeclarationSyntax? syntaxFactoryMethod = CreateSyntaxFactoryMethod(context, template.SemanticModel, additionalUsings, template, template.Options);

        // this is null if the template processing failed, but then diagnostics should have been added to the context, so we just exit
        if (syntaxFactoryMethod is null)
            return;

        var targetClassDecl = (ClassDeclarationSyntax)template.Target.Type!.DeclaringSyntaxReferences[0].GetSyntax();

        MemberDeclarationSyntax targetSyntax = ClassDeclaration(targetClassDecl.Identifier)
            .WithModifiers(targetClassDecl.Modifiers)
            .AddMembers(syntaxFactoryMethod);

        ISymbol current = template.Target.Type.ContainingSymbol;
        while (current is ITypeSymbol)
        {
            var classDecls = (ClassDeclarationSyntax)current.DeclaringSyntaxReferences[0].GetSyntax();

            targetSyntax = ClassDeclaration(current.Name)
                .WithModifiers(classDecls.Modifiers)
                .AddMembers(targetSyntax);

            current = current.ContainingSymbol;
        }

        // if the template is defined in the global namespace this will return null
        var namespaceName = current.GetNamespaceName();
        if (namespaceName is not null)
        {
            targetSyntax = FileScopedNamespaceDeclaration(namespaceName)
                .AddMembers(targetSyntax);
        }

        var compilationUnit = CompilationUnit()
            //.AddMembers(runtimeTypeList.ToArray())
            .AddMembers(targetSyntax);

        compilationUnit = compilationUnit
            .AddUsings(
                TemplateSyntaxQuoter.RequiredUsings()
                    .Union(additionalUsings)
                    .ToArray());


        var sourceText = SyntaxFormatter.Format(compilationUnit.NormalizeWhitespace()).GetText(Encoding.UTF8);

        context.AddSource($"{template.Target.FullName}.{template.Source!.Identifier}.g.cs", sourceText);
    }

    private static MethodDeclarationSyntax? CreateSyntaxFactoryMethod(
        SourceProductionContext context,
        SemanticModel semanticModel,
        UsingDirectiveSet additionalUsings,
        TemplateInfo templateInfo,
        TemplateOption options)
    {


        var unquotedReplacements = new Dictionary<SyntaxNode, ExpressionSyntax>();
        var trimNodes = new HashSet<SyntaxNode>();

        // trim the template attribute
        trimNodes.Add(templateInfo.AttributeSyntax);

        var preamble = new List<StatementSyntax>();

        // we use this to ensure we generate a unique type name
        var inlinedTypeParamNames = new HashSet<string>(StringComparer.Ordinal);
        List<TypeParameterSyntax> inlinedTypeParams = new List<TypeParameterSyntax>();
        foreach (var replacements in InlinedTypeParameterFinder.FindInlinedTypeParameters(semanticModel, templateInfo.Source!.Syntax))
        {
            string typeParamName = replacements.TypeParameter.Identifier.Text;
            while (!inlinedTypeParamNames.Add(typeParamName))
                typeParamName += '_';

            inlinedTypeParams.Add(TypeParameter(typeParamName));

            var syntaxForTypeParamIdentifier = Identifier("syntaxForTypeParam_" + typeParamName);
            TypeSyntax typeSyntaxForTypeParam = IdentifierName(syntaxForTypeParamIdentifier);
            preamble.Add(
                LocalDeclarationStatement(
                    VariableDeclaration(
                        additionalUsings.GetTypeName(ParseTypeName(typeof(TypeSyntax).FullName!)),
                        SingletonSeparatedList(
                            VariableDeclarator(
                                syntaxForTypeParamIdentifier,
                                null,
                                EqualsValueClause(
                                    InvocationExpression(
                                        IdentifierName(nameof(ParseTypeName)),
                                        ArgumentList(
                                            SingletonSeparatedList(
                                                Argument(
                                                    PostfixUnaryExpression(
                                                        SyntaxKind.SuppressNullableWarningExpression,
                                                        MemberAccessExpression(
                                                            SyntaxKind.SimpleMemberAccessExpression,
                                                            TypeOfExpression(IdentifierName(typeParamName)),
                                                            IdentifierName(nameof(Type.FullName))))))))))))));

            foreach (var typeSyntax in replacements.References)
                unquotedReplacements.Add(typeSyntax, typeSyntaxForTypeParam);

            trimNodes.Add(replacements.TypeParameter);
        }

        HashSet<string> paramNames = new HashSet<string>(StringComparer.Ordinal);

        List<ParameterSyntax> parameters = new List<ParameterSyntax>();

        foreach (var replacements in InlinedParameterFinder.FindInlinedParameters(semanticModel, templateInfo.Source.Syntax))
        {
            //Debugger.Launch();
            string paramName = replacements.Parameter.Identifier.Text;
            while (!paramNames.Add(paramName))
                paramName += '_';

            // TODO check if there is a static <type>.ToSyntax() to call, if so call it, otherwise convert this parameter type to ExpressionSyntax
            //Debugger.Launch();
            //var syntax = semanticModel.LookupStaticMembers(replacements.Parameter.SpanStart);
            //var methods = syntax.Where(symbol => symbol.IsStatic && symbol is IMethodSymbol method).ToList();
            //context.Compilation.

            //semanticModel.TryGetSpeculativeSemanticModel(0, InvocationExpression())

            TypeSyntax parameterType;

            IdentifierNameSyntax identifierSyntaxForParam;

            if (replacements.AsSyntax)
            {
                parameterType = additionalUsings.GetTypeName(ParseTypeName(typeof(ExpressionSyntax).FullName!));
                identifierSyntaxForParam = IdentifierName(paramName);
            }
            else
            {
                parameterType = replacements.Parameter.Type!;

                var syntaxForTypeParamIdentifier = Identifier("syntaxForParam_" + paramName);
                identifierSyntaxForParam = IdentifierName(syntaxForTypeParamIdentifier);
                preamble.Add(
                    LocalDeclarationStatement(
                        VariableDeclaration(
                            additionalUsings.GetTypeName(ParseTypeName(typeof(ExpressionSyntax).FullName!)),
                            SingletonSeparatedList(
                                VariableDeclarator(
                                    syntaxForTypeParamIdentifier,
                                    null,
                                    EqualsValueClause(
                                        InvocationExpression(
                                            MemberAccessExpression(
                                                SyntaxKind.SimpleMemberAccessExpression,
                                                IdentifierName(paramName),
                                                IdentifierName("ToSyntax")))))))));
            }



            parameters.Add(Parameter(Identifier(paramName)).WithType(parameterType));

            foreach (var identifierNameSyntax in replacements.References)
                unquotedReplacements.Add(identifierNameSyntax, identifierSyntaxForParam);

            trimNodes.Add(replacements.Parameter);
        }


        foreach (var replacements in SyntaxParameterFinder.FindSyntaxParameters(semanticModel, templateInfo.Source.Syntax))
        {
            string paramName = replacements.Parameter.Identifier.Text;
            while (!paramNames.Add(paramName))
                paramName += '_';

            var parameterType = additionalUsings.GetTypeName(ParseTypeName(typeof(ExpressionSyntax).FullName!));

            parameters.Add(Parameter(Identifier(paramName)).WithType(parameterType));

            foreach (var identifierNameSyntax in replacements.References)
                unquotedReplacements.Add(identifierNameSyntax, IdentifierName(paramName));
        }


        //Debugger.Launch();
        var prunableNodes = BranchPruneIdentifier.FindPrunableNodes(trimNodes, templateInfo.Source.Syntax);

        //Debugger.Launch();

        TemplateSyntaxQuoter quoter = new(
            semanticModel,
            unquotedReplacements,
            prunableNodes,
            includeTrivia: options.HasFlag(TemplateOption.PreserveTrivia));


        if (!TemplateSyntaxQuoterInvoker.TryQuote(
                quoter, 
                templateInfo, 
                out ExpressionSyntax? syntaxTreeExpr, 
                out TypeSyntax? returnType,
                out Diagnostic? error))
        {
            context.ReportDiagnostic(error!);
            return null;
        }


        var syntaxFactoryMethod = MethodDeclaration(additionalUsings.GetTypeName(returnType!), templateInfo.Source.Identifier)
            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword))
            .WithBody(Block()
                .AddStatements(preamble.ToArray())
                .AddStatements(ReturnStatement(syntaxTreeExpr)));

        if (inlinedTypeParams.Count > 0)
            syntaxFactoryMethod = syntaxFactoryMethod.WithTypeParameterList(TypeParameterList(SeparatedList(inlinedTypeParams)));

        if (parameters.Count > 0)
            syntaxFactoryMethod = syntaxFactoryMethod.WithParameterList(ParameterList(SeparatedList(parameters)));

        return syntaxFactoryMethod;
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.Formatting;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto.Templating;

[Generator]
public class TemplateFactorySourceGenerator : ISourceGenerator
{
    public void Execute(GeneratorExecutionContext context)
    {
        //Debugger.Launch();
        if (context.SyntaxContextReceiver is not CompositeSyntaxContextReceiver syntaxReceivers
            || syntaxReceivers.OfType<AttributeSyntaxLocator<TemplateAttribute, CSharpSyntaxNode>>() is not { } syntaxReceiver
            || syntaxReceivers.OfType<AttributeSyntaxLocator<RuntimeAttribute, MemberDeclarationSyntax>>() is not { } runtimeLocator)
        {
            return;
        }
        try
        {
            // lookup runtime types to include in output
            var runtimeTypes = GetRuntimeTypes(context, runtimeLocator.Locations);

            var templates = GetTemplates(context, syntaxReceiver.Locations);

            foreach (var template in templates)
            {
                var semanticModel = context.Compilation.GetSemanticModel(template.Attribute.SyntaxTree);
                var runtimeKey = template.Attribute.GetNamedArgument<string>(nameof(TemplateAttribute.Runtime), semanticModel) is { HasValue: true } optional ? optional.Value : RuntimeAttribute.Default;
                if (!runtimeTypes.TryGetValue(runtimeKey, out var runtimeTypeList))
                    runtimeTypeList = new List<TypeDeclarationSyntax>();

                ProcessTemplate(context, template, runtimeTypeList);
            }
        }
        catch (Exception ex)
        {
            context.ReportDiagnostic(Diagnostics.InternalError(ex));
        }
    }

    private static Dictionary<string, List<TypeDeclarationSyntax>> GetRuntimeTypes(GeneratorExecutionContext context, IEnumerable<SyntaxLocation<MemberDeclarationSyntax>> locations)
    {
        var runtimeTypes = new Dictionary<string, List<TypeDeclarationSyntax>>();

        // collect all runtime inclusions
        foreach (var runtimeLocation in locations)
        {
            var runtimeSemanticModel = context.Compilation.GetSemanticModel(runtimeLocation.Attribute.SyntaxTree);
            string runtimeKey = runtimeLocation.Attribute.GetConstructorArguments<string?>(runtimeSemanticModel) ?? RuntimeAttribute.Default;
            var cleanTarget = AttributeSyntaxRemover.Remove(runtimeLocation.Target, runtimeLocation.Attribute);

            var modifiers = cleanTarget.Modifiers;
            for (int i = 0; i < modifiers.Count;)
            {
                if (SyntaxFacts.IsAccessibilityModifier(modifiers[i].Kind()))
                    modifiers = modifiers.RemoveAt(i);
                else
                    i++;
            }

            modifiers.Insert(0, Token(SyntaxKind.FileKeyword));

            if (!runtimeTypes.TryGetValue(runtimeKey, out var typeList))
                runtimeTypes.Add(runtimeKey, typeList = new List<TypeDeclarationSyntax>());

            typeList.Add((TypeDeclarationSyntax)cleanTarget.WithModifiers(modifiers));
        }

        return runtimeTypes;
    }

    private static IEnumerable<TemplateInfo> GetTemplates(GeneratorExecutionContext context, IEnumerable<SyntaxLocation<CSharpSyntaxNode>> locations)
    {
        foreach (var syntaxLocation in locations)
        {
            var targetArg = syntaxLocation.Attribute.ArgumentList?.Arguments.FirstOrDefault();
            if (targetArg?.Expression is TypeOfExpressionSyntax typeOfExpr)
            {
                // capture target info
                var target = typeOfExpr.Type;
                var targetType = context.Compilation.GetSemanticModel(syntaxLocation.Target.SyntaxTree).GetTypeInfo(target);

                // and source info
                Source? source;

                if (syntaxLocation.Target is LocalFunctionStatementSyntax localFunctionSyntax)
                    source = new SourceFunction(syntaxLocation.Target, localFunctionSyntax.Identifier, localFunctionSyntax.ParameterList, localFunctionSyntax.Body);
                else if (syntaxLocation.Target is MethodDeclarationSyntax methodSyntax)
                    source = new SourceFunction(syntaxLocation.Target, methodSyntax.Identifier, methodSyntax.ParameterList, methodSyntax.Body!);
                else if (syntaxLocation.Target is ClassDeclarationSyntax classDeclarationSyntax)
                    source = new SourceType(syntaxLocation.Target, classDeclarationSyntax.Identifier, classDeclarationSyntax);
                else
                    source = null;

                yield return new TemplateInfo(syntaxLocation.Attribute, new TargetType(target, targetType.Type), source);
            }
        }
    }

    private static bool ValidateTemplate(GeneratorExecutionContext context, TemplateInfo template)
    {
        if (template.Target.Type is null)
        {
            context.ReportDiagnostic(Diagnostics.TargetNotDeclaredInSource(template.Target, "NULL TYPE" + context.Compilation.AssemblyName));
            return false;
        }

        if (template.Target.Type?.DeclaringSyntaxReferences.FirstOrDefault() is not { } syntaxRef)
        {
            context.ReportDiagnostic(Diagnostics.TargetNotDeclaredInSource(template.Target, context.Compilation.AssemblyName));
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


        if (template.Source is null)
            return false;
        return true;
    }

    private static MethodDeclarationSyntax? ProcessTemplate(
        GeneratorExecutionContext context,
        SemanticModel semanticModel,
        UsingDirectiveSet additionalUsings,
        TemplateInfo templateInfo,
        TemplateOption options)
    {


        Dictionary<SyntaxNode, ExpressionSyntax> unquotedReplacements = new Dictionary<SyntaxNode, ExpressionSyntax>();
        HashSet<SyntaxNode> trimNodes = new HashSet<SyntaxNode>();

        // trim the template attribute
        trimNodes.Add(templateInfo.Attribute);

        List<StatementSyntax> preamble = new List<StatementSyntax>();

        // we use this to ensure we generate a unique type name
        HashSet<string> inlinedTypeParamNames = new HashSet<string>(StringComparer.Ordinal);
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

        //Debugger.Launch();

        TypeSyntax? returnType;
        SyntaxNode? syntax;
        switch (templateInfo.Source)
        {
            case SourceFunction sourceFunction:
                if (!TrySelectSyntax(context, sourceFunction, options, out returnType, out syntax))
                    return null;
                break;
            case SourceType sourceType:
                if (!TrySelectSyntax(context, sourceType, options, out returnType, out syntax))
                    return null;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        var syntaxTreeExpr = quoter.Visit(syntax);

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

    private static bool TrySelectSyntax(GeneratorExecutionContext context, SourceType source, TemplateOption options, out TypeSyntax? returnType, out SyntaxNode? syntax)
    {
        if (options.HasFlag(TemplateOption.Bare))
        {
            if (source.Declaration.Members.Count == 0)
            {
                context.ReportDiagnostic(Diagnostics.BareSourceCannotBeEmpty(source));
                returnType = null;
                syntax = null;
                return false;
            }

            if ((options & TemplateOption.Single) == TemplateOption.Single)
            {
                if (source.Declaration.Members.Count == 1)
                {
                    syntax = source.Declaration.Members[0];
                    returnType = ParseTypeName(source.Declaration.Members[0].GetType().FullName!);
                }
                else
                {
                    context.ReportDiagnostic(Diagnostics.MultipleMembersNotAllowed(source));
                    returnType = null;
                    syntax = null;
                    return false;
                }
            }
            else
            {
                syntax = source.Declaration;
                returnType = ParseTypeName(typeof(SyntaxList<MemberDeclarationSyntax>).FullName!);
            }
        }
        else
        {
            syntax = source.Declaration;
            returnType = ParseTypeName(source.Declaration.GetType().FullName!);
        }

        return true;
    }


    private static bool TrySelectSyntax(GeneratorExecutionContext context, SourceFunction source, TemplateOption options, out TypeSyntax? returnType, out SyntaxNode? syntax)
    {
        if (options.HasFlag(TemplateOption.Bare))
        {
            if (source.Body!.Statements.Count == 0)
            {
                context.ReportDiagnostic(Diagnostics.BareSourceCannotBeEmpty(source));
                returnType = null;
                syntax = null;
                return false;
            }

            if ((options & TemplateOption.Single) == TemplateOption.Single)
            {
                if (source.Body.Statements.Count == 1)
                {
                    syntax = source.Body.Statements[0];
                    returnType = ParseTypeName(source.Body.Statements[0].GetType().FullName!);
                }
                else
                {
                    context.ReportDiagnostic(Diagnostics.MultipleStatementsNotAllowed(source));
                    returnType = null;
                    syntax = null;
                    return false;
                }
            }
            else
            {
                syntax = source.Body;
                returnType = ParseTypeName(typeof(BlockSyntax).FullName!);
            }
        }
        else
        {
            syntax = source.Syntax;
            returnType = ParseTypeName(source.Syntax.GetType().FullName!);
        }

        return true;
    }

    private static void ProcessTemplate(GeneratorExecutionContext context, TemplateInfo template, List<TypeDeclarationSyntax> runtimeTypeList)
    {
        // first we do some checking to ensure we were given valid inputs
        if (!ValidateTemplate(context, template))
            return;

        var source = template.Source!;

        var semanticModel = context.Compilation.GetSemanticModel(source.Syntax.SyntaxTree);

        //Debugger.Launch();
        TemplateOption options = template.Attribute.GetNamedArgument<int>(nameof(TemplateAttribute.Options), semanticModel) is
        { HasValue: true, Value: int rawOptions }
            ? (TemplateOption)rawOptions
            : TemplateOption.Default;

        // we use this to collection additional usings that are required through out the source-generation process
        UsingDirectiveSet additionalUsings = new UsingDirectiveSet(CSharpSyntaxQuoter.RequiredUsings());

        MethodDeclarationSyntax? syntaxFactoryMethod = null;

        syntaxFactoryMethod = ProcessTemplate(context, semanticModel, additionalUsings, template, options);

        // this is null if the template processing failed, but then diagnostics should have been added to the context so we just exit
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
            .AddMembers(runtimeTypeList.ToArray())
            .AddMembers(targetSyntax);

        compilationUnit = compilationUnit
            .AddUsings(
                TemplateSyntaxQuoter.RequiredUsings()
                    .Union(additionalUsings)
                    .ToArray());


        var sourceText = SyntaxFormatter.Format(compilationUnit.NormalizeWhitespace()).GetText(Encoding.UTF8);

        context.AddSource($"{template.Target.FullName}.{template.Source!.Identifier}.cs", sourceText);
    }




    public void Initialize(GeneratorInitializationContext context)
    {
#if DEBUG
        if (!Debugger.IsAttached)
        {
            //Debugger.Launch();
        }
#endif
        context.RegisterForSyntaxNotifications(
            () =>
                new CompositeSyntaxContextReceiver(
                    new AttributeSyntaxLocator<TemplateAttribute, CSharpSyntaxNode>(),
                    new AttributeSyntaxLocator<RuntimeAttribute, MemberDeclarationSyntax>()));
    }
}


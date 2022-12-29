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
public class TemplateFactoryGenerator : ISourceGenerator
{
    public void Execute(GeneratorExecutionContext context)
    {
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
            for (int i = 0; i < cleanTarget.Modifiers.Count;)
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
                SourceFunction? source;

                if (syntaxLocation.Target is LocalFunctionStatementSyntax localFunctionSyntax)
                    source = new SourceFunction(syntaxLocation.Target, localFunctionSyntax.Identifier, localFunctionSyntax.ParameterList, localFunctionSyntax.Body);
                else if (syntaxLocation.Target is MethodDeclarationSyntax methodSyntax)
                    source = new SourceFunction(syntaxLocation.Target, methodSyntax.Identifier, methodSyntax.ParameterList, methodSyntax.Body!);
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


        if (template.Source?.Body is null)
            return false;
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
            {HasValue: true, Value: int rawOptions}
            ? (TemplateOption) rawOptions
            : TemplateOption.Default;

        // rewrite Syntax/Syntax<T> parameters
        var targetParams = new ParameterSyntax[source.ParameterListSyntax.Parameters.Count];
        List<ParameterSyntax> literalParameters = new();
        List<ParameterSyntax> syntaxParameters = new();

        var syntaxDelegateSymbol = context.Compilation.GetTypeByMetadataName("Synto.Templating.Syntax");
        Debug.Assert(syntaxDelegateSymbol != null);
        var syntaxOfTDelegateSymbol = context.Compilation.GetTypeByMetadataName("Synto.Templating.Syntax`1");
        Debug.Assert(syntaxOfTDelegateSymbol != null);

        // we use this to collection additional usings that are required through out the source-generation process
        UsingDirectiveSet additionalUsings = new UsingDirectiveSet(CSharpSyntaxQuoter.RequiredUsings());

        //Debugger.Launch();
        for (int i = 0; i < source.ParameterListSyntax.Parameters.Count; i++)
        {
            var sourceParam = source.ParameterListSyntax.Parameters[i];
            var paramSymbol = semanticModel.GetDeclaredSymbol(sourceParam); // 🤞
            if (SymbolEqualityComparer.Default.Equals(paramSymbol?.Type, syntaxDelegateSymbol))
            {

                syntaxParameters.Add(sourceParam);
                targetParams[i] = sourceParam.WithType(additionalUsings.GetTypeName(ParseTypeName(typeof(ExpressionSyntax).FullName)));
            }
            else if (paramSymbol?.Type is INamedTypeSymbol { IsGenericType: true } namedTypeSymbol &&
                     SymbolEqualityComparer.Default.Equals(namedTypeSymbol.OriginalDefinition, syntaxOfTDelegateSymbol))
            {
                syntaxParameters.Add(sourceParam);
                targetParams[i] = sourceParam.WithType(additionalUsings.GetTypeName(ParseTypeName(typeof(ExpressionSyntax).FullName)));
            }
            else if (paramSymbol?.Type is IArrayTypeSymbol { ElementType: INamedTypeSymbol { IsGenericType: false } nonGenericElementTypeSymbol } &&
                     SymbolEqualityComparer.Default.Equals(nonGenericElementTypeSymbol.OriginalDefinition, syntaxDelegateSymbol))
            {
                syntaxParameters.Add(sourceParam);
                targetParams[i] = sourceParam.WithType(ArrayType(additionalUsings.GetTypeName(ParseTypeName(typeof(ExpressionSyntax).FullName))));
            }
            else if (paramSymbol?.Type is IArrayTypeSymbol { ElementType: INamedTypeSymbol { IsGenericType: true } elementTypeSymbol } &&
                     SymbolEqualityComparer.Default.Equals(elementTypeSymbol.OriginalDefinition, syntaxOfTDelegateSymbol))
            {
                syntaxParameters.Add(sourceParam);
                targetParams[i] = sourceParam.WithType(ArrayType(additionalUsings.GetTypeName(ParseTypeName(typeof(ExpressionSyntax).FullName))));
            }
            else
                literalParameters.Add(targetParams[i] = sourceParam);
        }

        TemplateSyntaxQuoter quoter = new(
            semanticModel,
            source,
            syntaxParameters,
            literalParameters,
            includeTrivia: options.HasFlag(TemplateOption.PreserveTrivia));

        ExpressionSyntax? syntaxTreeExpr;
        TypeSyntax? returnType;

        //Debugger.Launch();

        if (options.HasFlag(TemplateOption.Bare))
        {
            if (source.Body!.Statements.Count == 0)
            {
                context.ReportDiagnostic(Diagnostics.BareSourceCannotBeEmpty(source));
                return;
            }

            if ((options & TemplateOption.Single) == TemplateOption.Single)
            {
                if (source.Body.Statements.Count == 1)
                {
                    syntaxTreeExpr = quoter.Visit(source.Body.Statements[0]);
                    returnType = ParseTypeName(source.Body.Statements[0].GetType().FullName);
                }
                else
                {
                    context.ReportDiagnostic(Diagnostics.MultipleStatementsNotAllowed(source));
                    return;
                }
            }
            else
            {
                syntaxTreeExpr = quoter.Visit(source.Body);
                returnType = ParseTypeName(typeof(BlockSyntax).FullName);
            }
        }
        else
        {
            syntaxTreeExpr = quoter.Visit(source.Syntax);
            returnType = ParseTypeName(source.Syntax.GetType().FullName);
        }


        var syntaxFactoryMethod = MethodDeclaration(additionalUsings.GetTypeName(returnType), source.Identifier)
            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword))
            .WithParameterList(ParameterList().AddParameters(targetParams))
            .WithBody(Block().AddStatements(ReturnStatement(syntaxTreeExpr)));


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


        var sourceText = SyntaxFormatter.Format(compilationUnit.NormalizeWhitespace(eol: Environment.NewLine)).GetText(Encoding.UTF8);

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


using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.CodeAnalysis;
using Synto.Formatting;
using Synto.Templating;
using Synto;
using SF = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto;

// when the Synto.SyntaxContextReceiver has been converted to AttributeSyntaxLocator<TemplateAttribute> we should consider changing
// the AttributeSyntaxLocator to support multiple attributes so that we can rid of this helper class

[Generator]
public class TemplateFactoryGenerator : ISourceGenerator
{
    public void Execute(GeneratorExecutionContext context)
    {
        if (context.SyntaxContextReceiver is not SyntaxContextReceiverMultiplexer syntaxReceivers
            || syntaxReceivers.OfType<SyntaxContextReceiver>() is not { } syntaxReceiver
            || syntaxReceivers.OfType<AttributeSyntaxLocator<RuntimeAttribute>>() is not { } runtimeLocator)
        {
            return;
        }

        var runtimeTypes = new Dictionary<string, List<TypeDeclarationSyntax>>();

        //runtimeTypes.Add(RuntimeAttribute.Default, builtInRuntime);

        // collect all runtime inclusions
        foreach (var runtimeLocation in runtimeLocator.Locations)
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

            modifiers.Insert(0, SF.Token(SyntaxKind.FileKeyword));

            if (!runtimeTypes.TryGetValue(runtimeKey, out var typeList))
                runtimeTypes.Add(runtimeKey, typeList = new List<TypeDeclarationSyntax>());

            typeList.Add((TypeDeclarationSyntax)cleanTarget.WithModifiers(modifiers));
        }

        try
        {
            foreach (var template in syntaxReceiver.ProjectionAttributes)
            {
                var semanticModel = context.Compilation.GetSemanticModel(template.ProjectionAttribute.SyntaxTree);
                var runtimeKey = template.ProjectionAttribute.GetNamedArgument<string>(nameof(TemplateAttribute.Runtime), semanticModel) is { HasValue: true } optional ? optional.Value : RuntimeAttribute.Default;
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

    private static void ProcessTemplate(GeneratorExecutionContext context, TemplateInfo template, List<TypeDeclarationSyntax> runtimeTypeList)
    {
        // first we do some checking to ensure we were given valid inputs

        if (template.Target.Type is null)
        {
            context.ReportDiagnostic(Diagnostics.TargetNotDeclaredInSource(template.Target, "NULL TYPE" + context.Compilation.AssemblyName));
            return;
        }

        if (template.Target.Type?.DeclaringSyntaxReferences.FirstOrDefault() is not { } syntaxRef)
        {
            context.ReportDiagnostic(Diagnostics.TargetNotDeclaredInSource(template.Target, context.Compilation.AssemblyName));
            return;
        }

        if (syntaxRef.GetSyntax() is not ClassDeclarationSyntax classSyntax)
        {
            context.ReportDiagnostic(Diagnostics.TargetNotClass(template.Target));
            return;
        }

        if (!classSyntax.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            context.ReportDiagnostic(Diagnostics.TargetNotPartial(template.Target));
            return;
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
            return;

        // now we need to generate a file containing a namespace + the partial class decls from the Target

        var source = template.Source;

        if (source?.Body is null)
            return;

        var semanticModel = context.Compilation.GetSemanticModel(source.Syntax.SyntaxTree);

        //Debugger.Launch();

        var optionsArg =
            template.ProjectionAttribute
                .ArgumentList!
                .Arguments.SingleOrDefault(arg => arg.NameEquals is { } nameEquals &&
                                                  StringComparer.Ordinal.Equals(nameEquals.Name.Identifier.Value,
                                                      nameof(TemplateAttribute.Options))); // &&

        TemplateOption options =
            optionsArg is not null && semanticModel.GetConstantValue(optionsArg.Expression) is
            { HasValue: true, Value: int rawValue } // this comes out as int, so unbox it
                ? (TemplateOption)rawValue // then cast it
                : TemplateOption.Default;

        // rewrite Syntax/Syntax<T> parameters
        var targetParams = new ParameterSyntax[source.ParameterListSyntax.Parameters.Count];
        List<ParameterSyntax> literalParameters = new();
        List<ParameterSyntax> syntaxParameters = new();

        var syntaxDelegateSymbol = context.Compilation.GetTypeByMetadataName("Synto.Syntax");
        Debug.Assert(syntaxDelegateSymbol != null);
        var syntaxOfTDelegateSymbol = context.Compilation.GetTypeByMetadataName("Synto.Syntax`1");
        Debug.Assert(syntaxOfTDelegateSymbol != null);

        UsingDirectiveSet additionalUsings = new UsingDirectiveSet(CSharpSyntaxQuoter.RequiredUsings());

        //Debugger.Launch();
        for (int i = 0; i < source.ParameterListSyntax.Parameters.Count; i++)
        {
            var sourceParam = source.ParameterListSyntax.Parameters[i];
            var paramSymbol = semanticModel.GetDeclaredSymbol(sourceParam); // 🤞
            if (SymbolEqualityComparer.Default.Equals(paramSymbol?.Type, syntaxDelegateSymbol))
            {
                syntaxParameters.Add(sourceParam);
                targetParams[i] = sourceParam.WithType(additionalUsings.GetTypeName(SF.ParseTypeName(typeof(ExpressionSyntax).FullName)));
            }
            else if (paramSymbol?.Type is INamedTypeSymbol { IsGenericType: true } namedTypeSymbol &&
                     SymbolEqualityComparer.Default.Equals(namedTypeSymbol.OriginalDefinition, syntaxOfTDelegateSymbol))
            {
                syntaxParameters.Add(sourceParam);
                targetParams[i] = sourceParam.WithType(additionalUsings.GetTypeName(SF.ParseTypeName(typeof(ExpressionSyntax).FullName)));
            }
            else if (paramSymbol?.Type is IArrayTypeSymbol { ElementType: INamedTypeSymbol { IsGenericType: true } elementTypeSymbol } &&
                     SymbolEqualityComparer.Default.Equals(elementTypeSymbol.OriginalDefinition, syntaxOfTDelegateSymbol))
            {
                syntaxParameters.Add(sourceParam);
                targetParams[i] = sourceParam.WithType(additionalUsings.GetTypeName(SF.ParseTypeName(typeof(ExpressionSyntax).FullName)));
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
            if (source.Body.Statements.Count == 0)
            {
                context.ReportDiagnostic(Diagnostics.BareSourceCannotBeEmpty(source));
                return;
            }

            if ((options & TemplateOption.Single) == TemplateOption.Single)
            {
                if (source.Body.Statements.Count == 1)
                {
                    syntaxTreeExpr = source.Body.Statements[0].Accept(quoter);
                    returnType = SF.ParseTypeName(source.Body.Statements[0].GetType().FullName);
                }
                else
                {
                    context.ReportDiagnostic(Diagnostics.MultipleStatementsNotAllowed(source));
                    return;
                }
            }
            else
            {
                syntaxTreeExpr = source.Body.Accept(quoter);
                returnType = SF.ParseTypeName(typeof(BlockSyntax).FullName);
            }
        }
        else
        {
            syntaxTreeExpr = quoter.Visit(source.Syntax);
            returnType = SF.ParseTypeName(source.Syntax.GetType().FullName);
        }


        var syntaxFactoryMethod = SF.MethodDeclaration(additionalUsings.GetTypeName(returnType), source.Identifier)
            .AddModifiers(SF.Token(SyntaxKind.PublicKeyword), SF.Token(SyntaxKind.StaticKeyword))
            .WithParameterList(SF.ParameterList().AddParameters(targetParams))
            .WithBody(SF.Block().AddStatements(SF.ReturnStatement(syntaxTreeExpr)));


        var targetClassDecl = (ClassDeclarationSyntax)template.Target.Type.DeclaringSyntaxReferences[0].GetSyntax();

        MemberDeclarationSyntax targetSyntax = SF.ClassDeclaration(targetClassDecl.Identifier)
            .WithModifiers(targetClassDecl.Modifiers)
            .AddMembers(syntaxFactoryMethod);

        ISymbol current = template.Target.Type.ContainingSymbol;
        while (current is ITypeSymbol)
        {
            var classDecls = (ClassDeclarationSyntax)current.DeclaringSyntaxReferences[0].GetSyntax();

            targetSyntax = SF.ClassDeclaration(current.Name)
                .WithModifiers(classDecls.Modifiers)
                .AddMembers(targetSyntax);

            current = current.ContainingSymbol;
        }

        // if the template is defined in the global namespace this will return null
        var namespaceName = current.GetNamespaceName();
        if (namespaceName is not null)
        {
            targetSyntax = SF.FileScopedNamespaceDeclaration(namespaceName)
                .AddMembers(targetSyntax);
        }

        var compilationUnit = SF.CompilationUnit()
            .AddMembers(runtimeTypeList.ToArray())
            .AddMembers(targetSyntax);

        compilationUnit = compilationUnit
            .AddUsings(
                CSharpSyntaxQuoter.RequiredUsings()
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
        context.RegisterForSyntaxNotifications(() => new SyntaxContextReceiverMultiplexer(new SyntaxContextReceiver(), new AttributeSyntaxLocator<RuntimeAttribute>()));
    }
}


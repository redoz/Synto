using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.Utils;
using SF = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto;


[Generator]
public class SyntaxFactoryGenerator : ISourceGenerator
{
    public void Execute(GeneratorExecutionContext context)
    {
        if (context.SyntaxContextReceiver is not SyntaxContextReceiver syntaxReceiver)
            return;

        try
        {
            foreach (var template in syntaxReceiver.ProjectionAttributes)
            {
                // first we do some checking to ensure we were given valid inputs

                if (template.Target.Type is null)
                {
                    context.ReportDiagnostic(Diagnostics.TargetNotDeclaredInSource(template.Target, "NULL TYPE" + context.Compilation.AssemblyName));
                    continue;
                }

                if (template.Target.Type?.DeclaringSyntaxReferences.FirstOrDefault() is not { } syntaxRef)
                {
                    context.ReportDiagnostic(Diagnostics.TargetNotDeclaredInSource(template.Target, context.Compilation.AssemblyName));
                    continue;
                }

                if (syntaxRef.GetSyntax() is not ClassDeclarationSyntax classSyntax)
                {
                    context.ReportDiagnostic(Diagnostics.TargetNotClass(template.Target));
                    continue;
                }

                if (!classSyntax.Modifiers.Any(SyntaxKind.PartialKeyword))
                {
                    context.ReportDiagnostic(Diagnostics.TargetNotPartial(template.Target));
                    continue;
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
                    continue;

                // now we need to generate a file containing a namespace + the partial class decls from the Target

                var source = template.Source;

                if (source?.Body is null)
                    continue;

                var semanticModel = context.Compilation.GetSemanticModel(source.Syntax.SyntaxTree);

                //Debugger.Launch();

                var optionsArg =
                    template.ProjectionAttribute
                    .ArgumentList!
                    .Arguments.SingleOrDefault(arg => arg.NameEquals is { } nameEquals &&
                                          StringComparer.Ordinal.Equals(nameEquals.Name.Identifier.Value, nameof(TemplateAttribute.Options))) ;// &&

                TemplateOption options =
                    optionsArg is not null && semanticModel.GetConstantValue(optionsArg.Expression) is
                        {HasValue: true, Value: int rawValue} // this comes out as int, so unbox it
                        ? (TemplateOption)rawValue // then cast it
                        : TemplateOption.Default;

                // rewrite Syntax/Syntax<T> parameters
                var targetParams = new ParameterSyntax[source.ParameterListSyntax.Parameters.Count];

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
                        targetParams[i] = sourceParam.WithType(additionalUsings.GetTypeName(SF.ParseTypeName(typeof(ExpressionSyntax).FullName)));
                    }
                    else if (paramSymbol?.Type is INamedTypeSymbol {IsGenericType: true} namedTypeSymbol &&
                             SymbolEqualityComparer.Default.Equals(namedTypeSymbol.OriginalDefinition, syntaxOfTDelegateSymbol))
                    {
                        targetParams[i] = sourceParam.WithType(additionalUsings.GetTypeName(SF.ParseTypeName(typeof(ExpressionSyntax).FullName)));
                    }
                    else
                        targetParams[i] = sourceParam;
                }

                
                TemplateSyntaxQuoter quoter = new(source, semanticModel);

                ExpressionSyntax? syntaxTreeExpr;
                TypeSyntax? returnType;

                //Debugger.Launch();

                if (options.HasFlag(TemplateOption.Bare))
                {
                    if (source.Body.Statements.Count == 0)
                    {
                        context.ReportDiagnostic(Diagnostics.BareSourceCannotBeEmpty(source));
                        continue;
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
                            continue;
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


                targetSyntax = SF.FileScopedNamespaceDeclaration(current.GetNamespaceName())
                                    .AddMembers(targetSyntax);

               var compilationUnit = SF.CompilationUnit().AddMembers(targetSyntax);
               
                compilationUnit = compilationUnit
                    .AddUsings(
                        CSharpSyntaxQuoter.RequiredUsings()
                        .Union(additionalUsings)
                        .ToArray());


               var sourceText = SyntaxFormatter.Format(compilationUnit.NormalizeWhitespace(eol: Environment.NewLine)).GetText(Encoding.UTF8);

               context.AddSource($"{template.Target.FullName}.{template.Source!.Identifier}.cs", sourceText);
            }

        }
        catch (Exception ex)
        {
            context.ReportDiagnostic(Diagnostics.InternalError(ex));
        }
    }


    public void Initialize(GeneratorInitializationContext context)
    {
#if DEBUG
        if (!Debugger.IsAttached)
        {
            //Debugger.Launch();
        }
#endif
        context.RegisterForSyntaxNotifications(() => new SyntaxContextReceiver());
    }
}


using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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

                if (template.Target.Type?.DeclaringSyntaxReferences.FirstOrDefault() is not SyntaxReference syntaxRef)
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

                bool bare = template.ProjectionAttribute
                                    .ArgumentList!
                                    .Arguments.Any(arg => arg.NameEquals is NameEqualsSyntax nameEquals &&
                                                   StringComparer.Ordinal.Equals(nameEquals.Name.Identifier.Value,
                                                                                 nameof(TemplateAttribute.Bare)) &&
                                                   semanticModel.GetConstantValue(arg.Expression) is { HasValue: true, Value: true});

                // rewrite Syntax/Syntax<T> parameters
                var targetParams = new ParameterSyntax[source.ParameterListSyntax.Parameters.Count];

                var syntaxDelegateSymbol = context.Compilation.GetTypeByMetadataName("Synto.Syntax");
                Debug.Assert(syntaxDelegateSymbol != null);
                var syntaxOfTDelegateSymbol = context.Compilation.GetTypeByMetadataName("Synto.Syntax`1");
                Debug.Assert(syntaxOfTDelegateSymbol != null);

                
                //Debugger.Launch();
                for (int i = 0; i < source.ParameterListSyntax.Parameters.Count; i++)
                {
                    var sourceParam = source.ParameterListSyntax.Parameters[i];
                    var paramSymbol = semanticModel.GetDeclaredSymbol(sourceParam)!; // 🤞
                    if (SymbolEqualityComparer.Default.Equals(paramSymbol?.Type, syntaxDelegateSymbol))
                    {
                        targetParams[i] = sourceParam.WithType(SF.ParseTypeName(typeof(ExpressionSyntax).FullName));
                    }
                    else if (paramSymbol?.Type is INamedTypeSymbol namedTypeSymbol &&
                            namedTypeSymbol.IsGenericType &&
                            SymbolEqualityComparer.Default.Equals(namedTypeSymbol.OriginalDefinition, syntaxOfTDelegateSymbol))
                    {
                        targetParams[i] = sourceParam.WithType(SF.ParseTypeName(typeof(ExpressionSyntax).FullName));
                    }
                    else
                        targetParams[i] = sourceParam;
                }

                
                TemplateSyntaxQutoer quoter = new(source, targetParams, semanticModel);

                ExpressionSyntax? syntaxTreeExpr;
                TypeSyntax? returnType;
                if (bare)
                {
                    if (source.Body.Statements.Count == 0)
                    {
                        context.ReportDiagnostic(Diagnostics.BareSourceCannotBeEmpty(source));
                        continue;
                    }
                    else if (source.Body.Statements.Count == 1)
                    {
                        syntaxTreeExpr = source.Body.Statements[0].Accept(quoter);
                        returnType = SF.ParseTypeName(source.Body.Statements[0].GetType().FullName);
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

        


                var syntaxFactoryMethod = SF.MethodDeclaration(returnType, source.Identifier)
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


                targetSyntax = SF.NamespaceDeclaration(current.GetNamespaceName())
                                    .AddMembers(targetSyntax);

                var compilationUnit = SF.CompilationUnit()
                    .AddMembers(targetSyntax);

                // not entirely sure why this is needed, but otherwise things fail when we build from test explorer with a "Inconsistent syntax tree features" error
                // could be that this is masking something dumb happening elsewhere
                compilationUnit = compilationUnit.SyntaxTree.WithRootAndOptions(compilationUnit,
                                                                                context.Compilation.SyntaxTrees.First().Options)
                                                            .GetCompilationUnitRoot();


                // this compilation is created so that we can get a semantic model containing the existing syntax tree as well as the one we just generated
                Compilation compilation = context.Compilation.AddSyntaxTrees(compilationUnit.SyntaxTree);
                NamespaceRewriter namespaceRewriter = new(compilation.GetSemanticModel(compilationUnit.SyntaxTree));
                compilationUnit = (CompilationUnitSyntax)compilationUnit.Accept(namespaceRewriter)!;

                var sourceText = compilationUnit.NormalizeWhitespace().GetText(Encoding.UTF8);

                context.AddSource($"{template.Target.FullName}.{template.Source.Identifier}.cs", sourceText);
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


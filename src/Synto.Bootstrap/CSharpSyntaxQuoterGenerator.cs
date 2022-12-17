using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SF = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using Synto.Utils;

namespace Synto.Bootstrap;

internal class UsingDirectiveSet : IEnumerable<UsingDirectiveSyntax>
{
    private readonly UsingDirectiveSyntax[] _predefined;
    private readonly List<UsingDirectiveSyntax> _usings;

    public UsingDirectiveSet(IEnumerable<UsingDirectiveSyntax> predefined)
    {
        // we don't support static or alias usings (we could at least support the alias, but we don't)
        this._predefined = predefined.Where(usingSyntax => usingSyntax.StaticKeyword.IsKind(SyntaxKind.None) && usingSyntax.Alias is null).ToArray();
        this._usings = new List<UsingDirectiveSyntax>();
    }

    public IEnumerator<UsingDirectiveSyntax> GetEnumerator()
    {
        return this._usings.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable) this._usings).GetEnumerator();
    }

    public void AddNamespace(NameSyntax namespaceName)
    {
        
        if (!this._usings.Any(usingSyntax => usingSyntax.Name.IsEquivalentTo(namespaceName, topLevel: true))
            && !this._predefined.Any(usingSyntax => usingSyntax.Name.IsEquivalentTo(namespaceName, topLevel: true)))
        {
            this._usings.Add(SF.UsingDirective(namespaceName));
        }
    }

    public NameSyntax GetTypeName(NameSyntax fullyQualifiedName)
    {
        switch (fullyQualifiedName)
        {
            case IdentifierNameSyntax identifierName:
                return identifierName;
            case QualifiedNameSyntax qualifiedName:
            {
                NameSyntax namespaceName = qualifiedName.Left;
                AddNamespace(namespaceName);
                return qualifiedName.Right;
            }
            default:
                throw new NotSupportedException();
        }
    }
}

[Generator]
public class CSharpSyntaxQuoterGenerator : ISourceGenerator
{
    public void Execute(GeneratorExecutionContext context)
    {
        if (context.SyntaxContextReceiver is not TargetLocator locator || locator.TargetNode is null)
            return;

        try
        {
            ExecuteInternal(context, locator.TargetNode);
        }
        catch (Exception ex)
        {
            context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor("SY0000", "Failed to create CSharpSyntaxQuoter", "Exception: {0}", "Synto.Dev", DiagnosticSeverity.Error, true), null, ex.ToString()));
        }
    }

    private void ExecuteInternal(GeneratorExecutionContext context, ClassDeclarationSyntax targetClass)
    {
        //System.Diagnostics.Debugger.Launch();

        var semanticModel = context.Compilation.GetSemanticModel(targetClass.SyntaxTree);
        var typeSymbol = semanticModel.GetDeclaredSymbol(targetClass)!;

        // this is a bit weird, but it finds the type we need
        INamedTypeSymbol baseType = typeSymbol.BaseType!;
        while (baseType.Name != "CSharpSyntaxVisitor" && baseType.BaseType is not null)
            baseType = baseType.BaseType;

        var allMembers = baseType.GetMembers();

        List<MemberDeclarationSyntax> members = new();

        UsingDirectiveSet additionalUsings = new UsingDirectiveSet(CSharpSyntaxQuoter.RequiredUsings());

        var filteredMembers = allMembers.OfType<IMethodSymbol>().Where(member => member.Name.StartsWith("Visit") && member.Name.Length > "Visit".Length);


        //Debugger.Launch();

        foreach (var item in filteredMembers)
        {
            // skip things already defined in target
            if (targetClass.Members.OfType<MethodDeclarationSyntax>().Any(member => member.Identifier.ValueText == item.Name))
                continue;

            var paramSymbol = item.Parameters.Single();


            //var parameterSyntax = SF.Parameter(SF.Identifier(paramSymbol.Name))
            //    .WithType(SF.IdentifierName(paramSymbol.Type.Name));
            var parameterSyntax = SF.Parameter(SF.Identifier(paramSymbol.Name))
                .WithType(additionalUsings.GetTypeName(paramSymbol.Type.GetQualifiedNameSyntax()));


            // identify SyntaxFactory method we should call (this isn't very nice, or robust)
            var syntaxFactorySymbol = context.Compilation.GetTypeByMetadataName(typeof(SF).FullName /* this is technically not correct, but will work for this type */)!;
            var candidateMethods = syntaxFactorySymbol.GetMembers()
                .OfType<IMethodSymbol>()
                .Where(method => StringComparer.OrdinalIgnoreCase.Equals(method.Name, item.Name.Substring("Visit".Length)) && method.Parameters.Length > 0);
            var factoryMethods = candidateMethods.OrderByDescending(method => method.Parameters.Length).ToArray();
            IMethodSymbol factoryMethod;

                
            BlockSyntax? body = null;
            // used to disambiguate factory methods
            if (factoryMethods.Length >= 2 && factoryMethods[0].Parameters.Length == factoryMethods[1].Parameters.Length)
            {
                factoryMethod = factoryMethods[0].ReturnType switch
                {
                    { MetadataName: nameof(XmlTextSyntax) } =>
                        factoryMethods.Single(method => method.Parameters[0].Name == "textTokens" && method.Parameters[0].Type.MetadataName == "SyntaxTokenList"),
                    { MetadataName: nameof(AnonymousMethodExpressionSyntax) } =>
                        factoryMethods.Single(method => method.Parameters[0].Name == "modifiers"),
                    { MetadataName: nameof(SubpatternSyntax) } =>
                        factoryMethods.Single(method => method.Parameters[0].Name == "nameColon"),
                    var other => throw new NotImplementedException($"Item is: {item.Name}, Not yet implemented: {other.MetadataName}")
                };
            }
            else
                factoryMethod = factoryMethods[0];

            var nameOfExpr = SF.InvocationExpression(
                SF.IdentifierName(
                    SF.Identifier(
                        SF.TriviaList(),
                        SyntaxKind.NameOfKeyword,
                        SyntaxFacts.GetText(SyntaxKind.NameOfKeyword),
                        SyntaxFacts.GetText(SyntaxKind.NameOfKeyword),
                        SF.TriviaList())),
                SF.ArgumentList(
                    SF.SingletonSeparatedList(
                        SF.Argument(SF.IdentifierName(factoryMethod.Name)))));

            var identifierOfNameOfExpr = SF.InvocationExpression(
                SF.IdentifierName(nameof(SF.IdentifierName)),
                SF.ArgumentList(
                    SF.SingletonSeparatedList(
                        SF.Argument(nameOfExpr))));
            
            var expr = SF.InvocationExpression(identifierOfNameOfExpr);
            //var expr = SF.InvocationExpression(SF.IdentifierName(factoryMethod.Name));

            var arguments = SF.ArgumentList();

            List<ExpressionSyntax> unquoted = new(factoryMethod.Parameters.Length + 1)
            {
                identifierOfNameOfExpr // don't quote the nameof() expr
            };

            foreach (var parameter in factoryMethod.Parameters)
            {
                // find the member on the node type
                var sourceMemberName = char.ToUpperInvariant(parameter.Name[0]) + parameter.Name.Substring(1);
                ITypeSymbol nodeTypeSymbol = paramSymbol.Type;
                ISymbol? sourceMemberSymbol;
                do
                {
                    sourceMemberSymbol = nodeTypeSymbol.GetMembers(sourceMemberName).SingleOrDefault();
                } while (sourceMemberSymbol is null && (nodeTypeSymbol = nodeTypeSymbol!.BaseType!) is not null);

                if (sourceMemberSymbol is null)
                {
                    throw new Exception($"Unable to find SyntaxNode member {sourceMemberName} on type {paramSymbol.Type.ToDisplayString()}");
                }


                ExpressionSyntax argExpr = SF.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    additionalUsings.GetTypeName(paramSymbol.GetQualifiedNameSyntax()),
                    SF.IdentifierName(sourceMemberName));
                    
                ITypeSymbol argType = parameter.Type;
                if (sourceMemberSymbol is IMethodSymbol)
                {
                    argExpr = SF.InvocationExpression(argExpr);
                }
                else if (sourceMemberSymbol is not IPropertySymbol)
                {
                    throw new Exception($"Was not expecting SyntaxNode member {sourceMemberName} to be of type {sourceMemberSymbol.GetType().FullName}");
                }

                if (!argType.IsPrimitive())
                {
                    argExpr = SF.InvocationExpression(SF.IdentifierName("Visit")).AddArgumentListArguments(SF.Argument(argExpr));

                    if (parameter.NullableAnnotation == NullableAnnotation.Annotated)
                    {
                        argExpr = SF.InvocationExpression(
                            SF.MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                argExpr,
                                SF.IdentifierName("OrQuotedNullLiteral")));
                    }
                    else
                    {
                        argExpr = SF.PostfixUnaryExpression(SyntaxKind.SuppressNullableWarningExpression, argExpr);
                    }

                    //unquoted.Add(argExpr);
                }
                else
                {
                    argExpr = SF.InvocationExpression(
                        SF.MemberAccessExpression(SyntaxKind
                                .SimpleMemberAccessExpression, 
                            argExpr,
                            SF.IdentifierName("ToLiteral")));
                }
                unquoted.Add(argExpr);

                arguments = arguments.AddArguments(SF.Argument(argExpr));

            }
            //var arg = SF.ArgumentList(SF.SeparatedList(arguments.Select(arg => SF.Argument(arg))));

            expr = expr.WithArgumentList(arguments);

            //var quotedArg = CSharpSyntaxQuoter.Quote(arg, exclude: arguments);

            // expr = expr.AddArgumentListArguments(SF.Argument(quotedArg));

            var commentText = expr.WithExpression(SF.IdentifierName(factoryMethod.Name)).NormalizeWhitespace().GetText(Encoding.UTF8);

            //var quotedExpr = CSharpSyntaxQuoter.Quote(expr, exclude: factoryTypeNameExpr);

            var quotedExpr = CSharpSyntaxQuoter.Quote(expr, exclude: unquoted);

            body = SF.Block().AddStatements(SF.ReturnStatement(quotedExpr).WithLeadingTrivia(SF.Comment("// " + commentText)));

            var returnTypeSyntax = SF.NullableType(additionalUsings.GetTypeName(SF.ParseName(typeof(ExpressionSyntax).FullName)));

            var method = SF.MethodDeclaration(returnTypeSyntax, item.Name)
                .AddModifiers(SF.Token(SyntaxKind.PublicKeyword), SF.Token(SyntaxKind.OverrideKeyword))
                .WithParameterList(SF.ParameterList(SF.SingletonSeparatedList(parameterSyntax)))
                .WithBody(body);

            members.Add(method);
        }

        var classDeclSyntax = SF.ClassDeclaration(targetClass.Identifier)
            .WithModifiers(targetClass.Modifiers)
            .WithMembers(SF.List(members));

        var compilationUnit = SF.CompilationUnit()
            .AddUsings(CSharpSyntaxQuoter.RequiredUsings()
                .Union(additionalUsings).ToArray())
            .AddMembers(SF.FileScopedNamespaceDeclaration(typeSymbol.ContainingNamespace.GetQualifiedNameSyntax()))
            .AddMembers(classDeclSyntax);

        // try to make it a bit more readable
        compilationUnit = SyntaxFormatter.Format(compilationUnit.NormalizeWhitespace(eol: Environment.NewLine));
        
        var sourceText = compilationUnit.GetText(Encoding.UTF8);

        context.AddSource($"{targetClass.Identifier.Text}.cs", sourceText);
    }

    public void Initialize(GeneratorInitializationContext context)
    {
        //Debugger.Launch();
        context.RegisterForSyntaxNotifications(() => new TargetLocator());
    }

}
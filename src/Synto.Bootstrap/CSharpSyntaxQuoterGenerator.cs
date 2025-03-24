using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Synto.Formatting;

namespace Synto.Bootstrap;

[Generator(LanguageNames.CSharp)]
public class CSharpSyntaxQuoterGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var syntaxProvider = context.SyntaxProvider.CreateSyntaxProvider((node, _) =>
                node is ClassDeclarationSyntax cdl &&
                StringComparer.Ordinal.Equals("CSharpSyntaxQuoter", cdl.Identifier.Text),
            (syntaxContext, _) =>
                ((ClassDeclarationSyntax)syntaxContext.Node, syntaxContext.SemanticModel));

        context.RegisterSourceOutput(syntaxProvider, Execute);
    }

    private void Execute(SourceProductionContext context, (ClassDeclarationSyntax TargetNode, SemanticModel SemanticModel) target)
    {

        try
        {
            ExecuteInternal(context, target.TargetNode, target.SemanticModel);
        }
#pragma warning disable CA1031 // we're explicitly catching _any_ exception and converting it to a diagnostic message
        catch (Exception ex)
#pragma warning restore CA1031
        {
            context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor("SY0000", "Failed to create CSharpSyntaxQuoter", "Exception: {0}", "Synto.Dev", DiagnosticSeverity.Error, true), null, ex.ToString()));
        }
    }

    private void ExecuteInternal(SourceProductionContext context, ClassDeclarationSyntax targetClass,
        SemanticModel semanticModel)
    {
        //System.Diagnostics.Debugger.Launch();

        var typeSymbol = semanticModel.GetDeclaredSymbol(targetClass)!;

        // this is a bit weird, but it finds the type we need
        INamedTypeSymbol baseType = typeSymbol.BaseType!;
        while (baseType.Name != "CSharpSyntaxVisitor" && baseType.BaseType is not null)
            baseType = baseType.BaseType;

        var allMembers = baseType.GetMembers();

        List<MemberDeclarationSyntax> members = new();

        UsingDirectiveSet additionalUsings = new UsingDirectiveSet(CSharpSyntaxQuoter.RequiredUsings());

        var filteredMembers = allMembers.OfType<IMethodSymbol>().Where(member => member.Name.StartsWith("Visit", StringComparison.InvariantCultureIgnoreCase) && member.Name.Length > "Visit".Length);


        //Debugger.Launch();

        foreach (var item in filteredMembers)
        {
            // skip things already defined in target
            if (targetClass.Members.OfType<MethodDeclarationSyntax>().Any(member => member.Identifier.ValueText == item.Name))
                continue;

            var paramSymbol = item.Parameters.Single();


            //var parameterSyntax = SF.Parameter(SF.Identifier(paramSymbol.Name))
            //    .WithType(SF.IdentifierName(paramSymbol.Type.Name));
            var parameterSyntax = Parameter(Identifier(paramSymbol.Name))
                .WithType(additionalUsings.GetTypeName(paramSymbol.Type.GetQualifiedNameSyntax()));

            
            // identify SyntaxFactory method we should call (this isn't very nice, or robust)
            var syntaxFactorySymbol = semanticModel.Compilation.GetTypeByMetadataName(typeof(SyntaxFactory).FullName /* this is technically not correct, but will work for this type */)!;
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

            var nameOfExpr = InvocationExpression(
                IdentifierName(
                    Identifier(
                        TriviaList(),
                        SyntaxKind.NameOfKeyword,
                        SyntaxFacts.GetText(SyntaxKind.NameOfKeyword),
                        SyntaxFacts.GetText(SyntaxKind.NameOfKeyword),
                        TriviaList())),
                ArgumentList(
                    SingletonSeparatedList(
                        Argument(IdentifierName(factoryMethod.Name)))));

            var identifierOfNameOfExpr = InvocationExpression(
                IdentifierName(nameof(IdentifierName)),
                ArgumentList(
                    SingletonSeparatedList(
                        Argument(nameOfExpr))));
            
            var expr = InvocationExpression(identifierOfNameOfExpr);
            //var expr = SF.InvocationExpression(SF.IdentifierName(factoryMethod.Name));

            var arguments = ArgumentList();

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
#pragma warning disable CA2201 // we don't really care in the bootstrap project
                    throw new Exception($"Unable to find SyntaxNode member {sourceMemberName} on type {paramSymbol.Type.ToDisplayString()}");
#pragma warning restore CA2201
                }


                ExpressionSyntax argExpr = MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    additionalUsings.GetTypeName(paramSymbol.GetQualifiedNameSyntax()),
                    IdentifierName(sourceMemberName));
                    
                ITypeSymbol argType = parameter.Type;
                if (sourceMemberSymbol is IMethodSymbol)
                {
                    argExpr = InvocationExpression(argExpr);
                }
                else if (sourceMemberSymbol is not IPropertySymbol)
                {
#pragma warning disable CA2201 // we don't really care in the bootstrap project
                    throw new Exception($"Was not expecting SyntaxNode member {sourceMemberName} to be of type {sourceMemberSymbol.GetType().FullName}");
#pragma warning restore CA2201
                }

                if (!argType.IsPrimitive())
                {
                    argExpr = InvocationExpression(IdentifierName("Visit")).AddArgumentListArguments(Argument(argExpr));

                    if (parameter.NullableAnnotation == NullableAnnotation.Annotated)
                    {
                        argExpr = InvocationExpression(
                            MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                argExpr,
                                IdentifierName("OrNullLiteralExpression")));
                    }
                    else
                    {
                        argExpr = PostfixUnaryExpression(SyntaxKind.SuppressNullableWarningExpression, argExpr);
                    }

                }
                else
                {
                    argExpr = InvocationExpression(
                        MemberAccessExpression(SyntaxKind
                                .SimpleMemberAccessExpression, 
                            argExpr,
                            IdentifierName("ToSyntax")));
                }
                unquoted.Add(argExpr);

                arguments = arguments.AddArguments(Argument(argExpr));

            }

            expr = expr.WithArgumentList(arguments);

            // the Expression of expr is now a nameof() construction, so we replace it with an Identifier to make th comment more readable
            var commentText = expr.WithExpression(IdentifierName(factoryMethod.Name)).NormalizeWhitespace().GetText(Encoding.UTF8);

            var quotedExpr = CSharpSyntaxQuoter.Quote(expr, exclude: unquoted);

            var nullGuard = IfStatement(
                IsPatternExpression(
                    IdentifierName(parameterSyntax.Identifier),
                    ConstantPattern(
                        LiteralExpression(
                            SyntaxKind.NullLiteralExpression))),
                ThrowStatement(
                    ObjectCreationExpression(
                        additionalUsings.GetTypeName(ParseName(typeof(ArgumentNullException).FullName!)),
                        ArgumentList(
                            SingletonSeparatedList(
                                Argument(
                                    InvocationExpression(
                                        IdentifierName(
                                            Identifier(
                                                TriviaList(),
                                                SyntaxKind.NameOfKeyword,
                                                SyntaxFacts.GetText(SyntaxKind.NameOfKeyword),
                                                SyntaxFacts.GetText(SyntaxKind.NameOfKeyword),
                                                TriviaList())),
                                        ArgumentList(
                                            SingletonSeparatedList(
                                                Argument(
                                                    IdentifierName(
                                                        parameterSyntax.Identifier)))))))),
                        initializer: null)));



            body = Block().AddStatements(
                nullGuard,
                ReturnStatement(quotedExpr)
                    .WithLeadingTrivia(Comment("// " + commentText)));

            var returnTypeSyntax = NullableType(additionalUsings.GetTypeName(ParseName(typeof(ExpressionSyntax).FullName)));

            var method = MethodDeclaration(returnTypeSyntax, item.Name)
                .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.OverrideKeyword))
                .WithParameterList(ParameterList(SingletonSeparatedList(parameterSyntax)))
                .WithBody(body);

            members.Add(method);
        }


        var classDeclSyntax = ClassDeclaration(targetClass.Identifier)
            .WithModifiers(targetClass.Modifiers)
            .WithMembers(List(members));

        var compilationUnit = CompilationUnit()
            .AddUsings(CSharpSyntaxQuoter.RequiredUsings()
                .Union(additionalUsings).ToArray())
            .AddMembers(FileScopedNamespaceDeclaration(typeSymbol.ContainingNamespace.GetQualifiedNameSyntax()))
            .AddMembers(classDeclSyntax);

        // try to make it a bit more readable
        compilationUnit = SyntaxFormatter.Format(compilationUnit.NormalizeWhitespace());
        
        var sourceText = compilationUnit.GetText(Encoding.UTF8);

        context.AddSource($"{targetClass.Identifier.Text}.cs", sourceText);
    }



}
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SF = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Synto.Bootstrap
{
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
            //Debugger.Launch();

            var semanticModel = context.Compilation.GetSemanticModel(targetClass.SyntaxTree);
            var typeSymbol = semanticModel.GetDeclaredSymbol(targetClass)!;

            // this is a bit weird, but it finds the type we need
            INamedTypeSymbol baseType = typeSymbol.BaseType!;
            while (baseType.Name != "CSharpSyntaxVisitor" && baseType.BaseType is not null)
                baseType = baseType.BaseType;

            var allMembers = baseType.GetMembers();

            List<MemberDeclarationSyntax> members = new();

            var returnTypeSyntax = SF.NullableType(SF.ParseTypeName(typeof(ExpressionSyntax).FullName));

            var filteredMembers = allMembers.OfType<IMethodSymbol>().Where(member => member.Name.StartsWith("Visit") && member.Name.Length > "Visit".Length);

            var syntaxFactoryExpr = SF.ParseName(typeof(SF).FullName);


            //const string syntaxFactoryTypeName = "SyntaxFactoryType";
            //members.Add(SF.FieldDeclaration(
            //    SF.VariableDeclaration(
            //        SF.ParseTypeName(typeof(TypeSyntax).FullName),
            //        SF.SingletonSeparatedList(
            //            SF.VariableDeclarator(
            //                SF.Identifier(syntaxFactoryTypeName),
            //                null,
            //                SF.EqualsValueClause(
            //                    SF.InvocationExpression(
            //                        SF.MemberAccessExpression(
            //                            SyntaxKind.SimpleMemberAccessExpression,
            //                            SF.ParseTypeName(typeof(SyntaxFactory).FullName),
            //                            SF.IdentifierName(nameof(SyntaxFactory.ParseTypeName))),
            //                        SF.ArgumentList(
            //                            SF.SingletonSeparatedList(
            //                                SF.Argument(
            //                                    SF.LiteralExpression(
            //                                        SyntaxKind.StringLiteralExpression,
            //                                        SF.Literal(typeof(SyntaxFactory).FullName)))))))))))
            //    .AddModifiers(SF.Token(SyntaxKind.PrivateKeyword), SF.Token(SyntaxKind.ReadOnlyKeyword)));

            //var factoryTypeNameExpr = SF.IdentifierName(syntaxFactoryTypeName);

            //   Debugger.Launch();

            foreach (var item in filteredMembers)
            {
                // skip things already defined in target
                if (targetClass.Members.OfType<MethodDeclarationSyntax>().Any(member => member.Identifier.ValueText == item.Name))
                    continue;

                var paramSymbol = item.Parameters.Single();

                var parameterSyntax = SF.Parameter(SF.Identifier(paramSymbol.Name))
                                                   .WithType(paramSymbol.Type.GetQualifiedNameSyntax());

                // identify SyntaxFactory method we should call (this isn't very nice, or robust)
                var syntaxFactorySymbol = context.Compilation.GetTypeByMetadataName(typeof(SyntaxFactory).FullName /* this is technically not correct, but will work for this type */)!;
                var candidateMethods = syntaxFactorySymbol.GetMembers()
                    .OfType<IMethodSymbol>()
                    .Where(method => StringComparer.OrdinalIgnoreCase.Equals(method.Name, item.Name.Substring("Visit".Length)) && method.Parameters.Length > 0);
                var factoryMethods = candidateMethods.OrderByDescending(method => method.Parameters.Length).ToArray();
                IMethodSymbol factoryMethod;

                
                BlockSyntax? body = null;
                if (factoryMethods.Length >= 2 && factoryMethods[0].Parameters.Length == factoryMethods[1].Parameters.Length)
                {
                    factoryMethod = factoryMethods[0].ReturnType switch
                    {
                        //{ MetadataName: nameof(IdentifierNameSyntax) } =>
//                                null,//factoryMethods.Single(method => method.Parameters[0].Name == "name"),
                        { MetadataName: nameof(XmlTextSyntax) } =>
                                factoryMethods.Single(method => method.Parameters[0].Name == "textTokens" && method.Parameters[0].Type.MetadataName == "SyntaxTokenList"),
                        { MetadataName: nameof(AnonymousMethodExpressionSyntax) } =>
                                factoryMethods.Single(method => method.Parameters[0].Name == "modifiers"),
                        var other => throw new NotImplementedException($"Not yet implemented: {other.MetadataName}")
                    };
                }
                else
                    factoryMethod = factoryMethods[0];


                var expr = SF.InvocationExpression(
                    SF.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SF.IdentifierName("SyntaxFactory"),
                        SF.IdentifierName(factoryMethod.Name)));


                //expr = (InvocationExpressionSyntax)CSharpSyntaxQuoter.Quote(expr, exclude: factoryTypeNameExpr);

                //SF.InvocationExpression(SF.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, syntaxFactoryExpr,  ))
                var arguments = SF.ArgumentList();

                ///List<ExpressionSyntax> arguments = new(factoryMethod.Parameters.Length);
                List<ExpressionSyntax> unquoted = new(factoryMethod.Parameters.Length);

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

                    ExpressionSyntax argExpr = SF.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, SF.IdentifierName(paramSymbol.Name), SF.IdentifierName(sourceMemberName));
                    
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

                        unquoted.Add(argExpr);
                    }

                    arguments = arguments.AddArguments(SF.Argument(argExpr));

                }
                //var arg = SF.ArgumentList(SF.SeparatedList(arguments.Select(arg => SF.Argument(arg))));

                expr = expr.WithArgumentList(arguments);

                //var quotedArg = CSharpSyntaxQuoter.Quote(arg, exclude: arguments);

                // expr = expr.AddArgumentListArguments(SF.Argument(quotedArg));

                var commentText = expr.NormalizeWhitespace().GetText(Encoding.UTF8);

                //var quotedExpr = CSharpSyntaxQuoter.Quote(expr, exclude: factoryTypeNameExpr);

                var quotedExpr = CSharpSyntaxQuoter.Quote(expr, exclude: unquoted);

                body = SF.Block().AddStatements(SF.ReturnStatement(quotedExpr).WithLeadingTrivia(SF.Comment("// " + commentText)));



                var method = SF.MethodDeclaration(returnTypeSyntax, item.Name)
                               .AddModifiers(SF.Token(SyntaxKind.PublicKeyword), SF.Token(SyntaxKind.OverrideKeyword))
                               .WithParameterList(SF.ParameterList(SF.SingletonSeparatedList(parameterSyntax)));

                if (body is not null)
                    method = method.WithBody(body);

                members.Add(method);
            }

            var classDeclSyntax = SF.ClassDeclaration(targetClass.Identifier)
                                       .WithModifiers(targetClass.Modifiers)
                                       .WithMembers(SF.List(members));

            var namespaceSyntax = SF.NamespaceDeclaration(typeSymbol.ContainingNamespace.GetQualifiedNameSyntax())
                                       .AddMembers(classDeclSyntax);

            var compilationUnit = SF.CompilationUnit()
                                       .AddMembers(namespaceSyntax);

            var sourceText = compilationUnit.NormalizeWhitespace().GetText(Encoding.UTF8);

            context.AddSource($"{targetClass.Identifier.Text}.cs", sourceText);
        }

        public void Initialize(GeneratorInitializationContext context)
        {
            //Debugger.Launch();
            context.RegisterForSyntaxNotifications(() => new TargetLocator());
        }

    }
}

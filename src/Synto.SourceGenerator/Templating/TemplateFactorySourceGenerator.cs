using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Synto.Formatting;
using Synto.Templating;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto;


[Generator(LanguageNames.CSharp)]
public class TemplateFactorySourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // The whole template is processed inside the transform so the value flowing through the pipeline is
        // an equatable TemplateGenerationResult (just generated text + diagnostic data). This keeps the
        // SemanticModel / symbols / syntax nodes out of the cached pipeline state, which restores
        // incrementality and avoids rooting the compilation in memory across edits.
        var results = context.SyntaxProvider.ForAttributeWithMetadataName(
                typeof(TemplateAttribute).FullName!,
                static (node, cancellationToken) => true,
                static (syntaxContext, cancellationToken) => GenerateTemplate(syntaxContext))
            .WithTrackingName(TemplateTrackingNames.Transform)
            .Where(static result => result is not null)
            .WithTrackingName(TemplateTrackingNames.Result);

        context.RegisterSourceOutput(results, static (context, result) => Emit(context, result!.Value));
    }

    private static TemplateGenerationResult? GenerateTemplate(GeneratorAttributeSyntaxContext syntaxContext)
    {
        var templateInfo = TemplateInfo.Create(syntaxContext);
        if (templateInfo is null)
            return null;

        var assemblyName = syntaxContext.SemanticModel.Compilation.AssemblyName;
        if (assemblyName is null)
            return null;

        var diagnostics = new List<DiagnosticInfo>();

        string? fileName = null;
        string? source = null;

        try
        {
            if (ValidateTemplate(diagnostics, assemblyName, templateInfo)
                && ProcessTemplate(diagnostics, templateInfo) is { } generated)
            {
                fileName = generated.FileName;
                source = generated.Source;
            }
        }
#pragma warning disable CA1031 // we're explicitly catching _any_ exception and converting it to a diagnostic message
        catch (Exception ex)
#pragma warning restore CA1031
        {
            diagnostics.Add(Diagnostics.InternalError(ex));
        }

        return new TemplateGenerationResult(fileName, source, new EquatableArray<DiagnosticInfo>(diagnostics.ToImmutableArray()));
    }

    private static void Emit(SourceProductionContext context, TemplateGenerationResult result)
    {
        foreach (var diagnostic in result.Diagnostics)
            context.ReportDiagnostic(diagnostic.ToDiagnostic());

        if (result.FileName is not null && result.Source is not null)
            context.AddSource(result.FileName, SourceText.From(result.Source, Encoding.UTF8));
    }


    private static bool ValidateTemplate(List<DiagnosticInfo> diagnostics, string assemblyName, TemplateInfo template)
    {
        if (template.Target.Type.DeclaringSyntaxReferences.FirstOrDefault() is not { } syntaxRef)
        {
            diagnostics.Add(Diagnostics.TargetNotDeclaredInSource(template.Target, assemblyName));
            return false;
        }

        if (syntaxRef.GetSyntax() is not ClassDeclarationSyntax classSyntax)
        {
            diagnostics.Add(Diagnostics.TargetNotClass(template.Target));
            return false;
        }

        if (!classSyntax.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            diagnostics.Add(Diagnostics.TargetNotPartial(template.Target));
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
                    diagnostics.Add(Diagnostics.TargetAncestorNotPartial(template.Target, parentClass.Identifier.Text));
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

    private static (string FileName, string Source)? ProcessTemplate(List<DiagnosticInfo> diagnostics, TemplateInfo template)
    {
        // we use this to collect additional usings that are required throughout the source-generation process
        UsingDirectiveSet additionalUsings = new UsingDirectiveSet(CSharpSyntaxQuoter.RequiredUsings());

        // this is null if the template processing failed, but then diagnostics should have been added, so we just exit
        if (CreateSyntaxFactoryMethod(diagnostics, template.SemanticModel, additionalUsings, template, template.Options) is not { } syntaxFactoryMethod)
            return null;

        var targetClassDecl = (ClassDeclarationSyntax)template.Target.Type!.DeclaringSyntaxReferences[0].GetSyntax();

        MemberDeclarationSyntax targetSyntax = ClassDeclaration(targetClassDecl.Identifier)
            .WithModifiers(targetClassDecl.Modifiers)
            .AddMembers(syntaxFactoryMethod);

        targetSyntax = targetSyntax.WithAncestryFrom(template.Target.Type);

        // The factory body calls the emitted helpers (value.ToSyntax() / typeof(T).ToTypeSyntax() /
        // expr.OrNullLiteralExpression()) as extension methods. Rather than relying on an injected
        // internal copy in `namespace Synto` (which would collide with Synto.Core's public copies under
        // CS0121), emit each used helper as a `file static class` into THIS compilation unit, in the SAME
        // namespace scope as the factory. A `file` type is invisible to other files, so it can never
        // collide, and an extension method in the enclosing/global namespace resolves with no `using` at
        // all — which is why generated files no longer carry `using Synto;`.
        //
        // WHICH helpers to emit is decided by SCANNING the just-built factory syntax for real calls to a
        // known helper method, rather than by flags the factory-builder happens to set. This is robust by
        // construction: any helper a template's output references is detected and injected, so the
        // injected surface is complete for whatever the generator emits.
        var helpers = FindReferencedHelpers(syntaxFactoryMethod);

        // Place the helper classes in the same scope as the factory: inside its (file-scoped or block)
        // namespace if it has one, otherwise directly in the global compilation unit.
        if (helpers.Count > 0)
        {
            var helperDeclarations = helpers.Select(h => (MemberDeclarationSyntax)h.Declaration).ToArray();

            targetSyntax = targetSyntax switch
            {
                FileScopedNamespaceDeclarationSyntax fileNs => fileNs.AddMembers(helperDeclarations),
                NamespaceDeclarationSyntax ns => ns.AddMembers(helperDeclarations),
                _ => targetSyntax, // global namespace: helpers are added to the compilation unit below
            };
        }

        var compilationUnit = CompilationUnit()
            .AddMembers(targetSyntax);

        // In the global-namespace case the helpers weren't folded into a namespace member above, so add
        // them as top-level members of the compilation unit alongside the factory.
        if (helpers.Count > 0 && targetSyntax is not (FileScopedNamespaceDeclarationSyntax or NamespaceDeclarationSyntax))
            compilationUnit = compilationUnit.AddMembers(helpers.Select(h => (MemberDeclarationSyntax)h.Declaration).ToArray());

        // The compilation unit's usings: the quoter's required usings + any collected during processing +
        // the usings the emitted helpers themselves need (e.g. `using SF = ...SyntaxFactory;`). C# requires
        // usings before any declaration, so the helper usings are merged in here (deduped).
        var usings = CSharpSyntaxQuoter.RequiredUsings()
            .Union(additionalUsings)
            .ToList();

        foreach (var helper in helpers)
            MergeUsings(usings, helper.Usings);

        compilationUnit = compilationUnit.AddUsings(usings.ToArray());

        // Emit inside an explicit `#nullable enable` context. The generated factory carries nullable
        // annotations (`ExpressionSyntax?` parameters, `node.X!` suppressions), so without this directive
        // a consumer compiling the auto-generated file gets CS8669 ("annotation should only be used in a
        // '#nullable' context"). Prepending the directive matches how SurfaceInjectionGenerator and
        // DiagnosticsGenerator emit their output.
        compilationUnit = compilationUnit.WithLeadingTrivia(
            Trivia(
                NullableDirectiveTrivia(
                    Token(SyntaxKind.EnableKeyword),
                    isActive: true)));

        var sourceText = SyntaxFormatter.Format(compilationUnit.NormalizeWhitespace()).GetText(Encoding.UTF8).ToString();

        return ($"{template.Target.FullName}.{template.Source!.Identifier}.g.cs", sourceText);
    }

    private static void MergeUsings(List<UsingDirectiveSyntax> target, IEnumerable<UsingDirectiveSyntax> additions)
    {
        foreach (var addition in additions)
        {
            if (!target.Any(existing => existing.IsEquivalentTo(addition, topLevel: false)))
                target.Add(addition);
        }
    }

    /// <summary>
    /// Scans a generated factory method for real calls to a known runtime helper and returns the
    /// <c>file static class</c> for each referenced helper (deduplicated, at most one per helper).
    /// </summary>
    /// <remarks>
    /// Detection matches <see cref="MemberAccessExpressionSyntax"/> whose <c>.Name</c> identifier text is
    /// a helper method name — i.e. an actual emitted call such as <c>value.ToSyntax()</c> or
    /// <c>typeof(T).ToTypeSyntax()</c>. Crucially this does NOT match QUOTED template content: when a
    /// template's own body references such a name, the quoter emits it as a string literal argument
    /// (<c>IdentifierName("ToSyntax")</c>), not as a member-access identifier, so quoted content never
    /// triggers a spurious injection (and a template literally calling <c>.ToSyntax()</c> in its body is
    /// not double-injected).
    /// </remarks>
    private static List<FileLocalHelpers.Helper> FindReferencedHelpers(MethodDeclarationSyntax factoryMethod)
    {
        var referencedMethodNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var memberAccess in factoryMethod.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
            referencedMethodNames.Add(memberAccess.Name.Identifier.ValueText);

        var helpers = new List<FileLocalHelpers.Helper>();
        foreach (var entry in FileLocalHelpers.Entries)
        {
            if (referencedMethodNames.Contains(entry.MethodName))
                helpers.Add(entry.Helper);
        }

        return helpers;
    }

    /// <summary>
    /// The built-in literal types <see cref="LiteralSyntaxExtensions"/> handles directly. An inlined parameter
    /// of one of these binds to a specific <c>ToSyntax(this T)</c> overload (injected via the method-name
    /// scan); any other concrete type needs a user-provided <c>[Runtime]</c> converter.
    /// </summary>
    private static bool IsBuiltInLiteralType(ITypeSymbol type) =>
        type.SpecialType switch
        {
            SpecialType.System_String
                or SpecialType.System_Boolean
                or SpecialType.System_Char
                or SpecialType.System_SByte
                or SpecialType.System_Byte
                or SpecialType.System_Int16
                or SpecialType.System_UInt16
                or SpecialType.System_Int32
                or SpecialType.System_UInt32
                or SpecialType.System_Int64
                or SpecialType.System_UInt64
                or SpecialType.System_Decimal
                or SpecialType.System_Single
                or SpecialType.System_Double => true,
            _ => false,
        };

    private static MethodDeclarationSyntax? CreateSyntaxFactoryMethod(
        List<DiagnosticInfo> diagnostics,
        SemanticModel semanticModel,
        UsingDirectiveSet additionalUsings,
        TemplateInfo templateInfo,
        TemplateOption options)
    {
        // Which file-local helper(s) the caller emits is decided by SCANNING the finished factory syntax
        // (see FindReferencedHelpers), so the builder no longer needs to track usage flags.
        var unquotedReplacements = new Dictionary<SyntaxNode, ExpressionSyntax>();
        var trimNodes = new HashSet<SyntaxNode>();

        // trim the template attribute
        trimNodes.Add(templateInfo.AttributeSyntax);

        var preamble = new List<StatementSyntax>();

        // we use these to ensure we generate a unique type name
        var paramNames = new HashSet<string>(StringComparer.Ordinal);
        var inlinedTypeParamNames = new HashSet<string>(StringComparer.Ordinal);

        List<ParameterSyntax> parameters = new List<ParameterSyntax>();
        List<TypeParameterSyntax> typeParams = new List<TypeParameterSyntax>();

        HashSet<ITypeParameterSymbol> inlinedTypeParams = new HashSet<ITypeParameterSymbol>(SymbolEqualityComparer.Default);
        foreach (var replacements in InlinedTypeParameterFinder.FindInlinedTypeParameters(semanticModel, templateInfo.Source!.Syntax))
        {
            string typeParamName = replacements.TypeParameterSyntax.Identifier.Text;

            ExpressionSyntax typeSyntaxForTypeParam;
            if (replacements.AsSyntax)
            {
                // TODO make a little utility for this
                while (!paramNames.Add(typeParamName))
                    typeParamName += '_';

                typeSyntaxForTypeParam = IdentifierName(typeParamName);

                parameters.Add(Parameter(Identifier(typeParamName)).WithType(additionalUsings.GetTypeName(ParseTypeName(typeof(ExpressionSyntax).FullName!))));
            }
            else
            {
                inlinedTypeParams.Add(replacements.TypeParameterSymbol);

                // TODO make a little utility for this
                while (!inlinedTypeParamNames.Add(typeParamName))
                    typeParamName += '_';

                typeParams.Add(TypeParameter(typeParamName));

                var syntaxForTypeParamIdentifier = Identifier("syntaxForTypeParam_" + typeParamName);

                typeSyntaxForTypeParam = IdentifierName(syntaxForTypeParamIdentifier);

                // typeof(T).ToTypeSyntax() — converts the inlined type argument into a TypeSyntax at
                // runtime using the Synto helper, which (unlike ParseTypeName(typeof(T).FullName))
                // correctly handles closed generic types, arrays and nested types.
                var typeSyntaxInitializer = InvocationExpression(
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        TypeOfExpression(IdentifierName(typeParamName)),
                        IdentifierName(nameof(RuntimeTypeExtensions.ToTypeSyntax))));

                preamble.Add(
                    LocalDeclarationStatement(
                        VariableDeclaration(
                            additionalUsings.GetTypeName(ParseTypeName(typeof(TypeSyntax).FullName!)),
                            SingletonSeparatedList(
                                VariableDeclarator(
                                    syntaxForTypeParamIdentifier,
                                    null,
                                    EqualsValueClause(typeSyntaxInitializer))))));
            }

            foreach (var typeSyntax in replacements.References)
                unquotedReplacements.Add(typeSyntax, typeSyntaxForTypeParam);

            trimNodes.Add(replacements.TypeParameterSyntax);
        }

        // Lazily-resolved state for [Runtime] converter discovery — only walked when an inlined parameter of
        // a concrete, non-built-in type is actually encountered. All of this is semantic work done INSIDE the
        // transform; nothing here is captured into pipeline state (only the resulting source text flows out).
        var runtimeAttribute = semanticModel.Compilation.GetTypeByMetadataName(typeof(RuntimeAttribute).FullName!);
        var expressionSyntaxSymbol = semanticModel.Compilation.GetTypeByMetadataName(typeof(ExpressionSyntax).FullName!);
        ImmutableArray<INamedTypeSymbol>? runtimeClasses = null;
        bool converterError = false;

        foreach (var replacements in InlinedParameterFinder.FindInlinedParameters(semanticModel, templateInfo.Source.Syntax))
        {
            string paramName = replacements.Parameter.Identifier.Text;
            while (!paramNames.Add(paramName))
                paramName += '_';

            // TODO check if there is a static <type>.ToSyntax() to call, if so call it, otherwise convert this parameter type to ExpressionSyntax
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

                var paramTypeSymbolInfo = semanticModel.GetTypeInfo(parameterType);

                // How `value` is converted to an ExpressionSyntax. By default `value.ToSyntax()`, which binds
                // to the file-local LiteralSyntaxExtensions (for built-in types) or to the generic ToSyntax<T>
                // fallback (for inlined generic type parameters, resolved at the author's runtime). A custom
                // type instead binds to a discovered [Runtime] converter, called directly below.
                ExpressionSyntax conversion = InvocationExpression(
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName(paramName),
                        IdentifierName("ToSyntax")));

                // if this is a generic type reference we need to include it in our factory method declaration if it's not already been inlined
                if (paramTypeSymbolInfo.Type is ITypeParameterSymbol typeParam && !inlinedTypeParams.Contains(typeParam))
                {
                    string typeParamName = typeParam.Name;

                    while (!inlinedTypeParamNames.Add(typeParamName))
                        typeParamName += '_';

                    parameterType = IdentifierName(typeParamName);

                    typeParams.Add(TypeParameter(typeParamName));
                }
                else if (paramTypeSymbolInfo.Type is { } paramType
                         && paramType is not ITypeParameterSymbol
                         && !IsBuiltInLiteralType(paramType))
                {
                    // A concrete, non-built-in inlined type needs a user-provided [Runtime] converter exposing
                    // ToSyntax(this ThatType). It is discovered from the TYPE (not by scanning emitted call
                    // names); with no converter (or more than one) a diagnostic is emitted at generation time,
                    // rather than letting the generic ToSyntax<T> fallback throw NotImplementedException at the
                    // author's runtime.
                    runtimeClasses ??= RuntimeConverterFinder.FindRuntimeClasses(semanticModel.Compilation, runtimeAttribute);
                    var converters = RuntimeConverterFinder.FindConvertersFor(runtimeClasses.Value, paramType, expressionSyntaxSymbol);

                    if (converters.Length == 0)
                    {
                        diagnostics.Add(Diagnostics.NoRuntimeConverter(replacements.Parameter.Type!.GetLocation(), paramType.ToDisplayString()));
                        converterError = true;
                    }
                    else if (converters.Length > 1)
                    {
                        diagnostics.Add(Diagnostics.AmbiguousRuntimeConverter(replacements.Parameter.Type!.GetLocation(), paramType.ToDisplayString(), converters.Length));
                        converterError = true;
                    }
                    else
                    {
                        // Call the user's converter DIRECTLY by its fully-qualified name as a static method —
                        // `global::Ns.Converter.ToSyntax(value)`. We deliberately do NOT inject a file-local
                        // COPY of the converter (as is done for Synto's own embedded helpers): the user's
                        // converter already lives in this compilation, so a copy would be a second
                        // `ToSyntax(this ThatType)` in scope and collide with the original (CS0121). A
                        // fully-qualified static call binds to exactly the discovered converter, needs no
                        // `using`, and keeps the generated output free of any Synto runtime dependency.
                        var converter = converters[0];
                        conversion = InvocationExpression(
                                MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    ParseName(converter.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)),
                                    IdentifierName("ToSyntax")))
                            .AddArgumentListArguments(Argument(IdentifierName(paramName)));

                        // Emit the parameter with its fully-qualified declared type so it resolves in the
                        // generated file regardless of which usings the template's file happened to carry.
                        parameterType = ParseTypeName(paramType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                    }
                }

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
                                    EqualsValueClause(conversion))))));
            }



            parameters.Add(Parameter(Identifier(paramName)).WithType(parameterType));

            foreach (var identifierNameSyntax in replacements.References)
                unquotedReplacements.Add(identifierNameSyntax, identifierSyntaxForParam);

            trimNodes.Add(replacements.Parameter);
        }

        // A missing/ambiguous converter is a usage error: bail with the diagnostic(s) already reported rather
        // than emit a factory that would fail to compile or throw at the author's runtime.
        if (converterError)
            return null;


        foreach (var replacements in SyntaxParameterFinder.FindSyntaxParameters(semanticModel, templateInfo.Source.Syntax))
        {
            string paramName = replacements.Parameter.Identifier.Text;
            while (!paramNames.Add(paramName))
                paramName += '_';

            var parameterType = additionalUsings.GetTypeName(ParseTypeName(typeof(ExpressionSyntax).FullName!));

            parameters.Add(Parameter(Identifier(paramName)).WithType(parameterType));

            foreach (var identifierNameSyntax in replacements.References)
                unquotedReplacements.Add(identifierNameSyntax, IdentifierName(paramName));

            trimNodes.Add(replacements.Parameter);
        }


        var prunableNodes = BranchPruneIdentifier.FindPrunableNodes(trimNodes, templateInfo.Source.Syntax);

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
                out DiagnosticInfo? error))
        {
            if (error is { } errorInfo)
                diagnostics.Add(errorInfo);
            return null;
        }


        var syntaxFactoryMethod = MethodDeclaration(additionalUsings.GetTypeName(returnType!), templateInfo.Source.Identifier)
            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword))
            .WithBody(Block()
                .AddStatements(preamble.ToArray())
                .AddStatements(ReturnStatement(syntaxTreeExpr)));

        if (typeParams.Count > 0)
            syntaxFactoryMethod = syntaxFactoryMethod.WithTypeParameterList(TypeParameterList(SeparatedList(typeParams)));

        if (parameters.Count > 0)
            syntaxFactoryMethod = syntaxFactoryMethod.WithParameterList(ParameterList(SeparatedList(parameters)));

        return syntaxFactoryMethod;
    }
}

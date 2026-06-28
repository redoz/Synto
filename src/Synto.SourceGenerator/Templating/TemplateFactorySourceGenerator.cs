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

    /// <summary>
    /// The shared value→syntax lift used by both the <c>[Unquote]</c> value-parameter path and the new
    /// <c>[Quote]</c> paths (parameter + inline <c>Quote(value)</c>). Produces the factory-time conversion
    /// expression that lifts <paramref name="valueAccess"/> to <c>ExpressionSyntax</c>:
    /// <c>valueAccess.ToSyntax()</c> for a built-in literal type (binds to the file-local
    /// <c>LiteralSyntaxExtensions</c> / generic <c>ToSyntax&lt;T&gt;</c> fallback, also used for an inlined
    /// generic type parameter), or a fully-qualified <c>global::Ns.Converter.ToSyntax(valueAccess)</c> call for a
    /// concrete non-built-in type carrying exactly one <c>[Runtime]</c> converter. A concrete type with no
    /// converter yields <c>SY1011</c>; with more than one yields <c>SY1012</c> — returned via
    /// <paramref name="diagnostic"/> with a <c>false</c> result so the caller can bail. The generic-type-parameter
    /// <c>typeof(T).ToTypeSyntax()</c> lift is <c>[Unquote]</c>-only and stays out of this helper (<c>[Quote]</c>
    /// is value-axis only). <paramref name="runtimeClasses"/> is the lazily-resolved converter-class cache, walked
    /// only on the first concrete non-built-in type encountered. All work is semantic and done inside the
    /// transform; nothing is captured into pipeline state.
    /// </summary>
    private static bool TryEmitValueLift(
        ITypeSymbol valueType,
        ExpressionSyntax valueAccess,
        Location? diagnosticLocation,
        SemanticModel semanticModel,
        INamedTypeSymbol? runtimeAttribute,
        INamedTypeSymbol? expressionSyntaxSymbol,
        ref ImmutableArray<INamedTypeSymbol>? runtimeClasses,
        out ExpressionSyntax liftedSyntax,
        out DiagnosticInfo? diagnostic)
    {
        diagnostic = null;

        // Built-in literal type (or an inlined generic type parameter): value.ToSyntax(), binding to the
        // file-local LiteralSyntaxExtensions / generic ToSyntax<T> fallback.
        if (valueType is ITypeParameterSymbol || IsBuiltInLiteralType(valueType))
        {
            liftedSyntax = InvocationExpression(
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    valueAccess,
                    IdentifierName("ToSyntax")));
            return true;
        }

        // A concrete, non-built-in type needs a user-provided [Runtime] converter exposing
        // ToSyntax(this ThatType). It is discovered from the TYPE; with no converter (or more than one) a
        // diagnostic is emitted at generation time, rather than letting the generic ToSyntax<T> fallback throw
        // NotImplementedException at the author's runtime.
        runtimeClasses ??= RuntimeConverterFinder.FindRuntimeClasses(semanticModel.Compilation, runtimeAttribute);
        var converters = RuntimeConverterFinder.FindConvertersFor(runtimeClasses.Value, valueType, expressionSyntaxSymbol);

        if (converters.Length == 0)
        {
            diagnostic = Diagnostics.NoRuntimeConverter(diagnosticLocation, valueType.ToDisplayString());
            liftedSyntax = valueAccess;
            return false;
        }

        if (converters.Length > 1)
        {
            diagnostic = Diagnostics.AmbiguousRuntimeConverter(diagnosticLocation, valueType.ToDisplayString(), converters.Length);
            liftedSyntax = valueAccess;
            return false;
        }

        // Call the user's converter DIRECTLY by its fully-qualified name as a static method —
        // `global::Ns.Converter.ToSyntax(value)`. A fully-qualified static call binds to exactly the discovered
        // converter, needs no `using`, and keeps the generated output free of any Synto runtime dependency.
        var converter = converters[0];
        liftedSyntax = InvocationExpression(
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    ParseName(converter.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)),
                    IdentifierName("ToSyntax")))
            .AddArgumentListArguments(Argument(valueAccess));
        return true;
    }

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

        // Interpolation staged-fold channel (spec 2026-06-28): string-typed staged-root REFERENCE nodes mapped to
        // their factory-time raw value accessor (the factory parameter / hoisted local). Built here at EMISSION,
        // adjacent to where the staged roots are consumed, as a plain local exactly like unquotedReplacements /
        // trimNodes — only the resulting Dictionary<SyntaxNode, ExpressionSyntax> of nodes leaves this scope; no
        // ITypeSymbol/SemanticModel ever enters cached pipeline state.
        var stringStagedRoots = new Dictionary<SyntaxNode, ExpressionSyntax>();

        // trim the template attribute
        trimNodes.Add(templateInfo.AttributeSyntax);

        // [Splice] member generators (static methods returning MemberDeclarationSyntax / IEnumerable<…>):
        // discover and validate each up front. An invalid shape is reported (SY1019 non-static, SY1020 bad
        // return type, SY1021 has parameters) and the template bails; a valid generator is recognized and
        // trimmed from the quoted output member set, then emitted as factory-time code below (its members are
        // spliced into the type's member list via BuildList<MemberDeclarationSyntax>).
        var spliceMemberGenerators = SpliceMemberGeneratorFinder.FindGenerators(semanticModel, templateInfo.Source!.Syntax);
        bool spliceGeneratorError = false;
        var validSpliceGenerators = new List<SpliceMemberGenerator>();

        // Every node inside a valid generator body: excluded from the live-staging pipeline (its `foreach` over a
        // Parameter<>() is a REAL factory-time loop emitted verbatim — never unrolled, lifted, or quoted).
        var spliceGeneratorNodes = new HashSet<SyntaxNode>();

        foreach (var generator in spliceMemberGenerators)
        {
            var generatorLocation = generator.Method.Identifier.GetLocation();
            var generatorName = generator.Method.Identifier.Text;

            if (!generator.IsStatic)
            {
                diagnostics.Add(Diagnostics.SpliceMethodMustBeStatic(generatorLocation, generatorName));
                spliceGeneratorError = true;
            }

            if (generator.ReturnShape == SpliceMemberReturnShape.Invalid)
            {
                diagnostics.Add(Diagnostics.SpliceMethodBadReturnType(generatorLocation, generatorName, generator.Method.ReturnType.ToString()));
                spliceGeneratorError = true;
            }

            if (generator.HasParameters)
            {
                diagnostics.Add(Diagnostics.SpliceMethodHasParameters(generatorLocation, generatorName));
                spliceGeneratorError = true;
            }

            if (generator.IsStatic && !generator.HasParameters && generator.ReturnShape != SpliceMemberReturnShape.Invalid)
            {
                // A valid generator is NOT trimmed: the member-list quoter substitutes it with its member
                // segment (BuildList run) at its declaration position. (Trimming it could make a single-generator
                // type collapse under BranchPruneIdentifier once its only member is gone.)
                validSpliceGenerators.Add(generator);
                foreach (var node in generator.Method.DescendantNodesAndSelf())
                    spliceGeneratorNodes.Add(node);
            }
            else
            {
                // An invalid generator is reported above (the template bails); trim it so it is never a quoted
                // output member.
                trimNodes.Add(generator.Method);
            }
        }

        if (spliceGeneratorError)
            return null;

        // Child [Template] methods nested in the carrier (Capability 1): a method-level [Template] inside this
        // class-level [Template] carrier is independently picked up by ForAttributeWithMetadataName and generates
        // its OWN factory. When processing the PARENT carrier, exclude each child entirely: trim it so it is never
        // quoted into the parent's output, and collect its descendant-and-self nodes so the parent's live-staging
        // pipeline skips them (same bucket as spliceGeneratorNodes) — otherwise the child's own Parameter<…>()
        // roots would leak into the parent factory's parameter list. Only relevant for a class-level carrier (a
        // method-level template's own Source.Syntax IS the method, never a nested sibling).
        var childTemplateNodes = new HashSet<SyntaxNode>();
        if (templateInfo.Source!.Syntax is ClassDeclarationSyntax)
        {
            foreach (var childTemplate in ChildTemplateFinder.FindChildTemplates(semanticModel, templateInfo.Source.Syntax))
            {
                trimNodes.Add(childTemplate);
                foreach (var node in childTemplate.DescendantNodesAndSelf())
                    childTemplateNodes.Add(node);
            }
        }

        // The combined staging-exclusion set: a node inside a valid [Splice] generator body OR inside a nested
        // child [Template] is NOT part of THIS template's live-staging pipeline. Used at every region /
        // staged-control / depth-zero-reference consumption point below.
        var stagingExclusionNodes = new HashSet<SyntaxNode>(spliceGeneratorNodes);
        stagingExclusionNodes.UnionWith(childTemplateNodes);

        var preamble = new List<StatementSyntax>();

        // The member segment each valid generator contributes to its enclosing type's member list, keyed at the
        // generator method's declaration node so the quoter splices it at that position (TemplateSyntaxQuoter's
        // member-list override). Built below, after the factory parameters/preamble are assembled.
        var spliceMemberSegments = new Dictionary<SyntaxNode, ExpressionSyntax>();

        // we use these to ensure we generate a unique type name
        var paramNames = new HashSet<string>(StringComparer.Ordinal);
        var inlinedTypeParamNames = new HashSet<string>(StringComparer.Ordinal);

        List<ParameterSyntax> parameters = new List<ParameterSyntax>();
        List<TypeParameterSyntax> typeParams = new List<TypeParameterSyntax>();

        HashSet<ITypeParameterSymbol> inlinedTypeParams = new HashSet<ITypeParameterSymbol>(SymbolEqualityComparer.Default);
        foreach (var replacements in StagedTypeParameterFinder.FindStagedTypeParameters(semanticModel, templateInfo.Source!.Syntax))
        {
            // A [Splice]/[Unquote] type parameter declared on a nested child [Template] (e.g. the child's
            // `TypedGetter<[Splice] TRet>`) belongs to the CHILD's own factory, not the parent's — skip it so it
            // does not leak into the parent factory's parameter/type-parameter list (Capability 1).
            if (childTemplateNodes.Contains(replacements.TypeParameterSyntax))
                continue;

            string typeParamName = replacements.TypeParameterSyntax.Identifier.Text;

            ExpressionSyntax typeSyntaxForTypeParam;
            if (replacements.IsSplice)
            {
                // [Splice] type parameter: splice a pre-built TypeSyntax verbatim. The factory parameter is typed
                // TypeSyntax (NOT ExpressionSyntax) so every TYPE-position use of the parameter lands in a
                // TypeSyntax slot and the generated factory compiles — the fix for the type-axis miscompile where
                // a spliced type was previously emitted as ExpressionSyntax (CS1503 ExpressionSyntax -> TypeSyntax).
                // TODO make a little utility for this
                while (!paramNames.Add(typeParamName))
                    typeParamName += '_';

                typeSyntaxForTypeParam = IdentifierName(typeParamName);

                parameters.Add(Parameter(Identifier(typeParamName)).WithType(additionalUsings.GetTypeName(ParseTypeName(typeof(TypeSyntax).FullName!))));
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

        // [Splice] value parameters: a pre-built ExpressionSyntax supplied to the factory and spliced VERBATIM
        // (no evaluation, no value lift). The factory parameter is typed ExpressionSyntax and every use of it is
        // replaced by the parameter as-is.
        foreach (var replacements in SpliceParameterFinder.FindSpliceParameters(semanticModel, templateInfo.Source.Syntax))
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


        // Staged roots (Template.Parameter<T>() live parameters, Template.Unquote<T>() locals, [Unquote] parameters):
        // discover them, then classify the body's binding-time partition so the staging emitter can unroll
        // live control regions (plan Task 6). All of this is semantic work inside the transform; nothing here
        // is captured into pipeline state.
        var stagedParameterResult = StagedParameterFinder.FindStagedParameters(semanticModel, templateInfo.Source.Syntax);
        diagnostics.AddRange(stagedParameterResult.Diagnostics);

        // A live-parameter naming error is a usage error: bail with the diagnostic(s) already reported
        // rather than emit a factory built from an unresolved/ambiguous parameter set.
        if (stagedParameterResult.Diagnostics.Count > 0)
            return null;

        var stagedRootResult = StagedRootFinder.FindStagedRoots(semanticModel, templateInfo.Source.Syntax);

        // Drop any staged parameter / staged root whose declaration sites AND references lie ENTIRELY within a
        // nested child [Template] (Capability 1, the load-bearing fix). The child's own Parameter<…>() roots
        // (e.g. clrType, typeLabel) would otherwise become spurious PARENT factory parameters. A root SHARED with
        // the parent (the same (name, T) also declared in a parent member — e.g. `columns`) has occurrences
        // outside the child, so it is NOT dropped; only roots whose every occurrence is inside a child method are.
        var stagedParameters = stagedParameterResult.Parameters;
        var stagedLocals = stagedRootResult.Locals;
        var stagedRootParameters = stagedRootResult.Parameters;
        if (childTemplateNodes.Count > 0)
        {
            bool EntirelyWithinChild(IEnumerable<SyntaxNode> occurrences)
            {
                bool any = false;
                foreach (var occurrence in occurrences)
                {
                    any = true;
                    if (!childTemplateNodes.Contains(occurrence))
                        return false;
                }

                return any;
            }

            stagedParameters = stagedParameters
                .Where(p => !EntirelyWithinChild(p.References.Concat(p.TrimNodes)))
                .ToList();
            stagedLocals = stagedLocals
                .Where(l => !EntirelyWithinChild(l.References.Append(l.Declaration)))
                .ToList();
            stagedRootParameters = stagedRootParameters
                .Where(p => !EntirelyWithinChild(p.References.Append<SyntaxNode>(p.Parameter)))
                .ToList();
        }

        // Seed the binding-time classifier from every live root symbol (parameters + bound locals), then find
        // the live control regions (a foreach driven by a live root) to unroll.
        var stagedRootSymbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var rootNames = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
        var classifierRoots = new List<StagedRoot>();

        foreach (var stagedParameter in stagedParameters)
        {
            // Seed EVERY declaration-site local (the same (name, T) may be re-declared across several member
            // bodies of a class template — plan Task 9), so each member's live control region is recognized,
            // not only the first declaration's.
            foreach (var symbol in stagedParameter.Symbols)
            {
                if (stagedRootSymbols.Add(symbol))
                    classifierRoots.Add(new StagedRoot(symbol));
            }
        }

        foreach (var stagedLocal in stagedLocals)
        {
            if (semanticModel.GetDeclaredSymbol(stagedLocal.Declaration.Declaration.Variables[0]) is { } symbol && stagedRootSymbols.Add(symbol))
                classifierRoots.Add(new StagedRoot(symbol, stagedLocal.ValueExpression));
        }

        foreach (var stagedParameterRoot in stagedRootParameters)
        {
            if (semanticModel.GetDeclaredSymbol(stagedParameterRoot.Parameter) is { } symbol && stagedRootSymbols.Add(symbol))
                classifierRoots.Add(new StagedRoot(symbol));
        }

        // Inline Quote(value) calls: discovered up front so the classifier can SHIELD their live arguments
        // (output-world boundary). The actual value-lift + replacement is wired below, alongside the other
        // value lifts, once the [Runtime] converter state is resolved.
        var quoteCalls = QuoteCallFinder.FindQuoteCalls(semanticModel, templateInfo.Source.Syntax);
        var quoteCallNodes = new HashSet<SyntaxNode>(quoteCalls.Select(call => (SyntaxNode)call.Invocation));

        var partition = BindingTimeClassifier.Classify(semanticModel, templateInfo.Source.Syntax, classifierRoots, quoteCallNodes);

        // An impossible cut (a live binding that transitively depends on an output-world/quoted value) cannot be
        // evaluated at factory time: report it (SY1013) with the offending span and bail rather than emit a
        // factory that would not compile.
        if (partition.ImpossibleCuts.Count > 0)
        {
            foreach (var cut in partition.ImpossibleCuts)
                diagnostics.Add(Diagnostics.ImpossibleCut(cut.Node.GetLocation(), cut.Reason));
            return null;
        }

        // Exclude any control region inside a [Splice] generator body: that `foreach` is a real factory-time loop
        // emitted verbatim by the member-generator path, NOT a live region to unroll.
        var stagedRegions = StagedRegionEmitter.FindRegions(semanticModel, templateInfo.Source.Syntax, partition)
            .Where(region => !stagingExclusionNodes.Contains(region.Control))
            .ToList();
        var regionConsumedNodes = StagedRegionEmitter.ComputeConsumedNodes(stagedRegions);
        int stagedRegionCounter = 0;

        // A live control statement that region discovery did not pick up (it is an embedded, non-block statement
        // of an output-world construct, so it owns no container to key the replacement at) and that no other
        // region consumes would otherwise fall through to the normal quoter — lifting its live driver into the
        // OUTPUT (wrong code, no signal). Degrade to SY1014 instead of a silent mis-expansion.
        var unhandledStagedControl = templateInfo.Source.Syntax.DescendantNodes()
            .OfType<StatementSyntax>()
            .Where(statement => partition.IsStagedControl(statement) && !regionConsumedNodes.Contains(statement) && !stagingExclusionNodes.Contains(statement))
            .ToList();
        if (unhandledStagedControl.Count > 0)
        {
            foreach (var statement in unhandledStagedControl)
                diagnostics.Add(Diagnostics.UnsupportedStagedShape(statement.GetLocation(), "a staged control region must be a direct statement of a block to unroll in v1"));
            return null;
        }

        // F2 usings transplant (spec §5.2): a live control region runs VERBATIM in the factory, so the carrier's
        // own `using` directives that its live scaffold relies on (e.g. `System.Linq` for `.Where(...)`) must be
        // merged into the generated factory file — otherwise the verbatim code does not resolve. Only simple
        // namespace usings are transplanted (deduped against the quoter's RequiredUsings); `using static …` and
        // alias usings are skipped (UsingDirectiveSet ignores them anyway), and Synto.* usings are excluded so
        // the inert facade/marker surface (e.g. `using static Synto.Templating.Template;`, `using Synto.Templating;`)
        // is never pulled into the factory scope where it could collide with the injected internal surface.
        if (stagedRegions.Count > 0
            && templateInfo.Source.Syntax.SyntaxTree.GetRoot() is CompilationUnitSyntax carrierUnit)
        {
            foreach (var carrierUsing in carrierUnit.Usings)
            {
                if (!carrierUsing.StaticKeyword.IsKind(SyntaxKind.None) || carrierUsing.Alias is not null)
                    continue;
                if (carrierUsing.Name is not { } usingName || usingName.ToString().StartsWith("Synto", StringComparison.Ordinal))
                    continue;

                additionalUsings.AddNamespace(usingName);
            }
        }

        // Staged scalar member-access count-fold (Capability 2 / spec 2026-06-28). DELIBERATELY NARROW SCOPE: a
        // member-access `root.Member` (e.g. `columns.Count`) whose RECEIVER is a bare reference to a staged-root
        // parameter and whose RESULT type is a built-in literal type (so `EquatableArray<T>.Count` -> int folds to
        // an int literal). The WHOLE member-access is the lift unit — `(root.Member).ToSyntax()`, keyed at the
        // member-access node — so no separate `Parameter<int>()` is required. The bare receiver reference is
        // CLAIMED here (added to foldClaimedReferences) so the depth-zero path below does not also lift it as
        // `root.ToSyntax()` (which would break, since the receiver's collection type is not a built-in literal).
        // Out of scope (NOT handled): arbitrary live method calls, multi-hop chains, non-built-in result types, a
        // member-access consumed by a live control region.
        var rootSymbolToStagedParameter = new Dictionary<ISymbol, StagedParameter>(SymbolEqualityComparer.Default);
        foreach (var stagedParameter in stagedParameters)
            foreach (var symbol in stagedParameter.Symbols)
                rootSymbolToStagedParameter[symbol] = stagedParameter;

        var foldClaimedReferences = new HashSet<SyntaxNode>();
        var foldsByStagedParameter = new Dictionary<StagedParameter, List<MemberAccessExpressionSyntax>>();
        if (rootSymbolToStagedParameter.Count > 0)
        {
            foreach (var memberAccess in templateInfo.Source.Syntax.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
            {
                if (memberAccess.Expression is not IdentifierNameSyntax receiver)
                    continue;
                if (regionConsumedNodes.Contains(memberAccess) || stagingExclusionNodes.Contains(memberAccess))
                    continue;
                if (semanticModel.GetSymbolInfo(receiver).Symbol is not { } receiverSymbol
                    || !rootSymbolToStagedParameter.TryGetValue(receiverSymbol, out var ownerParameter))
                    continue;
                if (semanticModel.GetTypeInfo(memberAccess).Type is not { } resultType || !IsBuiltInLiteralType(resultType))
                    continue;

                foldClaimedReferences.Add(receiver);
                if (!foldsByStagedParameter.TryGetValue(ownerParameter, out var list))
                    foldsByStagedParameter[ownerParameter] = list = new List<MemberAccessExpressionSyntax>();
                list.Add(memberAccess);
            }
        }

        foreach (var stagedParameter in stagedParameters)
        {
            string paramName = stagedParameter.Name;
            while (!paramNames.Add(paramName))
                paramName += '_';

            // Staged scalar count-fold: emit `(paramName.Member).ToSyntax()` for each claimed member-access on this
            // staged root, keyed at the member-access node so the quoter splices the factory-time literal lift in
            // place of the whole `root.Member` expression.
            if (foldsByStagedParameter.TryGetValue(stagedParameter, out var folds))
            {
                foreach (var memberAccess in folds)
                {
                    ExpressionSyntax lift = InvocationExpression(
                        MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            ParenthesizedExpression(
                                MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    IdentifierName(paramName),
                                    memberAccess.Name.WithoutTrivia())),
                            IdentifierName("ToSyntax")));
                    unquotedReplacements[memberAccess] = lift;
                }
            }

            // Map EVERY declaration-site symbol to the shared factory parameter name so the live-region renamer
            // rewrites each member's local reference (the foreach driver) to the one factory parameter.
            foreach (var rootSymbol in stagedParameter.Symbols)
                rootNames[rootSymbol] = paramName;

            var parameterType = ParseTypeName(stagedParameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            parameters.Add(Parameter(Identifier(paramName)).WithType(parameterType));

            // Interpolation staged-fold: a string-typed staged root may be baked into surrounding literal text
            // when used as a bare interpolation hole. Map EVERY reference (depth-0 AND region-consumed) to the
            // factory-time raw value accessor (the shared factory parameter), which is in scope everywhere the
            // factory body / staged-region scaffold runs.
            if (stagedParameter.Type.SpecialType == SpecialType.System_String)
            {
                foreach (var reference in stagedParameter.References)
                    stringStagedRoots[reference] = IdentifierName(paramName);
            }

            // References consumed by a live region are handled by the verbatim scaffold (which uses the factory
            // parameter directly as a runtime value); only depth-0 references lift via value.ToSyntax() — binds
            // to the file-local LiteralSyntaxExtensions (built-in types) or the generic ToSyntax<T> fallback.
            var depthZeroReferences = stagedParameter.References.Where(reference => !regionConsumedNodes.Contains(reference) && !stagingExclusionNodes.Contains(reference) && !foldClaimedReferences.Contains(reference)).ToList();
            if (depthZeroReferences.Count > 0)
            {
                ExpressionSyntax conversion = InvocationExpression(
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName(paramName),
                        IdentifierName("ToSyntax")));

                var syntaxForParamIdentifier = Identifier("syntaxForParam_" + paramName);
                preamble.Add(
                    LocalDeclarationStatement(
                        VariableDeclaration(
                            additionalUsings.GetTypeName(ParseTypeName(typeof(ExpressionSyntax).FullName!)),
                            SingletonSeparatedList(
                                VariableDeclarator(
                                    syntaxForParamIdentifier,
                                    null,
                                    EqualsValueClause(conversion))))));

                foreach (var reference in depthZeroReferences)
                    unquotedReplacements.Add(reference, IdentifierName(syntaxForParamIdentifier));
            }

            foreach (var trimNode in stagedParameter.TrimNodes)
                trimNodes.Add(trimNode);
        }

        // Staged bound roots (Template.Unquote<T>() locals + [Unquote] parameters): the bound expression runs at
        // factory-build time and the resulting runtime value is lifted into the quoted output. Depth-0, a
        // live LOCAL hoists its `var n = <expr>;` verbatim into the factory body (a real runtime local) and
        // each use lifts via `n.ToSyntax()`; a [Unquote] PARAMETER becomes a caller-supplied factory parameter
        // lifted the same way (an [Unquote] value, classified live for later staging).
        foreach (var stagedLocal in stagedLocals)
        {
            // Hoist the runtime local: `var n = <expr>;` (the Unquote(...) carrier is unwrapped to its argument,
            // which is evaluated at factory-build time). Build it fresh so source comments/trivia don't leak.
            preamble.Add(
                LocalDeclarationStatement(
                    VariableDeclaration(
                        IdentifierName("var"),
                        SingletonSeparatedList(
                            VariableDeclarator(Identifier(stagedLocal.Name))
                                .WithInitializer(EqualsValueClause(stagedLocal.ValueExpression.WithoutTrivia()))))));

            // References consumed by a live region are handled by the verbatim scaffold (the live local is a
            // real runtime driver/accumulator there — e.g. a `while` driver, `k++`); only depth-0 references
            // lift via value.ToSyntax(), exactly like an [Unquote] value. When every reference is region-consumed
            // the ToSyntax lift is dead, so it is not emitted at all.
            var liveLocalDepthZeroReferences = stagedLocal.References.Where(reference => !regionConsumedNodes.Contains(reference)).ToList();
            if (liveLocalDepthZeroReferences.Count > 0)
            {
                var syntaxForStagedIdentifier = Identifier("syntaxForStaged_" + stagedLocal.Name);
                preamble.Add(
                    LocalDeclarationStatement(
                        VariableDeclaration(
                            additionalUsings.GetTypeName(ParseTypeName(typeof(ExpressionSyntax).FullName!)),
                            SingletonSeparatedList(
                                VariableDeclarator(
                                    syntaxForStagedIdentifier,
                                    null,
                                    EqualsValueClause(
                                        InvocationExpression(
                                            MemberAccessExpression(
                                                SyntaxKind.SimpleMemberAccessExpression,
                                                IdentifierName(stagedLocal.Name),
                                                IdentifierName("ToSyntax")))))))));

                foreach (var reference in liveLocalDepthZeroReferences)
                    unquotedReplacements.Add(reference, IdentifierName(syntaxForStagedIdentifier));
            }

            // Interpolation staged-fold: a string-typed Unquote<string>(…) local may be baked into surrounding
            // literal text when used as a bare interpolation hole. The hoisted runtime local (`var name = …;`) is
            // in scope throughout the factory body, so its name is the raw value accessor. The String decision is
            // made HERE at emission off the Unquote<T> type argument; only the resulting nodes leave this scope.
            if ((semanticModel.GetSymbolInfo(stagedLocal.StagedCall).Symbol as IMethodSymbol)?.TypeArguments[0].SpecialType == SpecialType.System_String)
            {
                foreach (var reference in stagedLocal.References)
                    stringStagedRoots[reference] = IdentifierName(stagedLocal.Name);
            }

            trimNodes.Add(stagedLocal.Declaration);
        }

        // Lazily-resolved state for [Runtime] converter discovery — only walked when an [Unquote] parameter of
        // a concrete, non-built-in type is actually encountered. All of this is semantic work done INSIDE the
        // transform; nothing here is captured into pipeline state (only the resulting source text flows out).
        var runtimeAttribute = semanticModel.Compilation.GetTypeByMetadataName(typeof(RuntimeAttribute).FullName!);
        var expressionSyntaxSymbol = semanticModel.Compilation.GetTypeByMetadataName(typeof(ExpressionSyntax).FullName!);
        ImmutableArray<INamedTypeSymbol>? runtimeClasses = null;
        bool converterError = false;

        foreach (var stagedParameterRoot in stagedRootParameters)
        {
            string paramName = stagedParameterRoot.Parameter.Identifier.Text;
            while (!paramNames.Add(paramName))
                paramName += '_';

            // The [Unquote] value lift: the supplied value is converted to syntax at factory time and the result
            // spliced into the output (subsumes the old value-lift behavior). By default `value.ToSyntax()`,
            // which binds to the file-local LiteralSyntaxExtensions (built-in types) or the generic ToSyntax<T>
            // fallback (inlined generic type parameters); a custom type instead binds to a discovered [Runtime]
            // converter, called fully-qualified below.
            TypeSyntax parameterType = stagedParameterRoot.Parameter.Type!;
            var paramType = stagedParameterRoot.Type;

            // A generic type reference must be declared on the factory method (unless already inlined as a type param).
            if (paramType is ITypeParameterSymbol typeParam && !inlinedTypeParams.Contains(typeParam))
            {
                string typeParamName = typeParam.Name;
                while (!inlinedTypeParamNames.Add(typeParamName))
                    typeParamName += '_';

                parameterType = IdentifierName(typeParamName);
                typeParams.Add(TypeParameter(typeParamName));
            }

            // The [Unquote] value lift: the supplied value is converted to syntax at factory time and the result
            // spliced into the output. Shared with the [Quote] paths via TryEmitValueLift (the generic-type-param
            // declaration above stays [Unquote]-only; [Quote] is value-axis only).
            if (!TryEmitValueLift(paramType, IdentifierName(paramName), stagedParameterRoot.Parameter.Type!.GetLocation(), semanticModel, runtimeAttribute, expressionSyntaxSymbol, ref runtimeClasses, out ExpressionSyntax conversion, out var liftDiagnostic))
            {
                diagnostics.Add(liftDiagnostic!.Value);
                converterError = true;
            }
            else if (paramType is not ITypeParameterSymbol && !IsBuiltInLiteralType(paramType))
            {
                // Emit the parameter with its fully-qualified declared type so it resolves in the generated file
                // regardless of which usings the template's file happened to carry.
                parameterType = ParseTypeName(paramType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            }

            var syntaxForParamIdentifier = Identifier("syntaxForParam_" + paramName);
            preamble.Add(
                LocalDeclarationStatement(
                    VariableDeclaration(
                        additionalUsings.GetTypeName(ParseTypeName(typeof(ExpressionSyntax).FullName!)),
                        SingletonSeparatedList(
                            VariableDeclarator(
                                syntaxForParamIdentifier,
                                null,
                                EqualsValueClause(conversion))))));

            parameters.Add(Parameter(Identifier(paramName)).WithType(parameterType));

            foreach (var reference in stagedParameterRoot.References)
                unquotedReplacements.Add(reference, IdentifierName(syntaxForParamIdentifier));

            // Interpolation staged-fold: a string-typed [Unquote] parameter may be baked into surrounding literal
            // text when used as a bare interpolation hole. The factory parameter is the raw value accessor.
            if (paramType.SpecialType == SpecialType.System_String)
            {
                foreach (var reference in stagedParameterRoot.References)
                    stringStagedRoots[reference] = IdentifierName(paramName);
            }

            trimNodes.Add(stagedParameterRoot.Parameter);
        }

        // [Quote] value parameters: the SAME value-lift as an [Unquote] value (via TryEmitValueLift), but the
        // parameter is NEVER seeded into BindingTimeClassifier (it is not in stagedRootResult), so a control
        // construct referencing only a quoted value stays Quoted and is emitted as a runtime construct rather than
        // unrolled (spec §3). Value-axis only ([Quote] is AttributeTargets.Parameter), so there is no
        // generic-type-parameter branch here.
        foreach (var quoteParameter in QuoteParameterFinder.FindQuoteParameters(semanticModel, templateInfo.Source.Syntax))
        {
            string paramName = quoteParameter.Parameter.Identifier.Text;
            while (!paramNames.Add(paramName))
                paramName += '_';

            TypeSyntax parameterType = quoteParameter.Parameter.Type!;
            var paramType = semanticModel.GetDeclaredSymbol(quoteParameter.Parameter)?.Type;

            if (paramType is null)
            {
                // Unbound parameter type: cannot lift. Bail (the consumer's own CS error already covers this).
                converterError = true;
                continue;
            }

            if (!TryEmitValueLift(paramType, IdentifierName(paramName), quoteParameter.Parameter.Type!.GetLocation(), semanticModel, runtimeAttribute, expressionSyntaxSymbol, ref runtimeClasses, out ExpressionSyntax conversion, out var liftDiagnostic))
            {
                diagnostics.Add(liftDiagnostic!.Value);
                converterError = true;
                continue;
            }

            if (paramType is not ITypeParameterSymbol && !IsBuiltInLiteralType(paramType))
            {
                // Emit the parameter with its fully-qualified declared type so it resolves in the generated file
                // regardless of which usings the template's file happened to carry.
                parameterType = ParseTypeName(paramType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            }

            var syntaxForQuoteIdentifier = Identifier("syntaxForQuote_" + paramName);
            preamble.Add(
                LocalDeclarationStatement(
                    VariableDeclaration(
                        additionalUsings.GetTypeName(ParseTypeName(typeof(ExpressionSyntax).FullName!)),
                        SingletonSeparatedList(
                            VariableDeclarator(
                                syntaxForQuoteIdentifier,
                                null,
                                EqualsValueClause(conversion))))));

            parameters.Add(Parameter(Identifier(paramName)).WithType(parameterType));

            foreach (var reference in quoteParameter.References)
                unquotedReplacements.Add(reference, IdentifierName(syntaxForQuoteIdentifier));

            trimNodes.Add(quoteParameter.Parameter);
        }

        // Inline Quote(value) calls: lift the value argument at the call position (the same value→syntax lift as
        // an [Unquote]/[Quote] value) and key the replacement at the INVOCATION node, so the quoter splices the
        // lifted syntax in place of the Quote(...) call. The classifier already shielded these as output-world
        // boundaries (so an enclosing loop/condition stayed Quoted).
        foreach (var quoteCall in quoteCalls)
        {
            var valueType = semanticModel.GetTypeInfo(quoteCall.ValueArgument).Type;
            if (valueType is null)
            {
                converterError = true;
                continue;
            }

            // Use the argument as-is for primary expressions; parenthesize anything else so the lifted
            // member-access form (`value.ToSyntax()`) binds to the whole value, not just its trailing operand.
            ExpressionSyntax valueAccess = quoteCall.ValueArgument is IdentifierNameSyntax or LiteralExpressionSyntax or MemberAccessExpressionSyntax or InvocationExpressionSyntax or ElementAccessExpressionSyntax or ParenthesizedExpressionSyntax
                ? quoteCall.ValueArgument.WithoutTrivia()
                : ParenthesizedExpression(quoteCall.ValueArgument.WithoutTrivia());

            if (!TryEmitValueLift(valueType, valueAccess, quoteCall.ValueArgument.GetLocation(), semanticModel, runtimeAttribute, expressionSyntaxSymbol, ref runtimeClasses, out ExpressionSyntax quoteCallConversion, out var quoteCallDiagnostic))
            {
                diagnostics.Add(quoteCallDiagnostic!.Value);
                converterError = true;
                continue;
            }

            unquotedReplacements[quoteCall.Invocation] = quoteCallConversion;
        }

        // A missing/ambiguous converter is a usage error: bail with the diagnostic(s) already reported rather
        // than emit a factory that would fail to compile or throw at the author's runtime.
        if (converterError)
            return null;

        // Syntax builders (Task 3): built-in Template.Member/TypeOf facade calls (recognized by binding) and
        // user [SyntaxBuilder] facade calls (recognized structurally) are replaced — keyed at the invocation
        // node — by a fully-qualified static call of the paired builder over processed arguments. A [Quoted]
        // builder parameter receives the QUOTE of the call argument (an output-world island); an unmarked
        // parameter receives the live/computed value verbatim. No copy of the builder is injected into the
        // generated output (the built-in builders are injected internal; user builders are the consumer's own
        // code) — both are called fully-qualified, so the output carries no Synto.Core runtime dependency.
        var builderResult = SyntaxBuilderFinder.FindBuilderCalls(semanticModel, templateInfo.Source.Syntax);
        diagnostics.AddRange(builderResult.Diagnostics);

        // A builder facade-synthesis / argument-binding / ambiguity error is a usage error: bail with the
        // diagnostic(s) already reported rather than emit a factory built from an unresolved builder call.
        if (builderResult.Diagnostics.Count > 0)
            return null;

        if (builderResult.Calls.Count > 0)
        {
            // A fresh quoter for the [Quoted] islands inside builder arguments: it carries the lifts collected
            // so far (inline/syntax/live parameter replacements) so an island that references a lifted value
            // quotes correctly. It never descends into a builder invocation (those are not yet in the map).
            var islandQuoter = new TemplateSyntaxQuoter(semanticModel, unquotedReplacements, new HashSet<SyntaxNode>(), includeTrivia: false, stringStagedRoots: stringStagedRoots);

            foreach (var call in builderResult.Calls)
            {
                var args = new List<ArgumentSyntax>();
                foreach (var binding in call.Args)
                {
                    ExpressionSyntax argExpr = binding.Kind switch
                    {
                        BuilderArgKind.Quoted => islandQuoter.Visit(binding.ValueArgument!)!,
                        BuilderArgKind.QuotedTypeArg => islandQuoter.Visit(binding.TypeArgument!)!,
                        _ => binding.ValueArgument!.WithoutTrivia(),
                    };
                    args.Add(Argument(argExpr));
                }

                var builderInvocation = InvocationExpression(
                        MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            ParseName(call.BuilderFullyQualifiedTypeName),
                            IdentifierName(call.BuilderMethodName)))
                    .WithArgumentList(ArgumentList(SeparatedList(args)));

                unquotedReplacements[call.Invocation] = builderInvocation;
            }
        }

        // Template.Splice(node) body calls (recognized by binding): splice the pre-built ExpressionSyntax
        // argument VERBATIM into the output at the call position — the inline counterpart to a [Splice]
        // parameter. Keyed at the invocation so the quoter substitutes the argument expression as-is.
        foreach (var spliceCall in SpliceCallFinder.FindSpliceCalls(semanticModel, templateInfo.Source.Syntax))
            unquotedReplacements[spliceCall.Invocation] = (ExpressionSyntax)spliceCall.Argument.WithoutTrivia();

        // Unroll the live control regions: each foreach over a live root becomes a verbatim scaffold in the
        // factory preamble (collecting quoted islands into a run) plus a single replacement keyed at the
        // region's owning container block (spec §5.3). The fixed-sibling quoter and island quoters carry the
        // lifts collected so far; the container replacement is added LAST so neither sees it.
        var liveRegionEmission = StagedRegionEmitter.Emit(
            semanticModel,
            partition,
            stagedRegions,
            partition.StagedSymbols,
            rootNames,
            unquotedReplacements,
            trimNodes,
            stringStagedRoots,
            ref stagedRegionCounter);

        diagnostics.AddRange(liveRegionEmission.Diagnostics);

        // An unsupported live shape is a usage error: bail with the diagnostic(s) already reported rather than
        // emit a mis-expanded factory.
        if (liveRegionEmission.Diagnostics.Count > 0)
            return null;

        preamble.AddRange(liveRegionEmission.Preamble);

        foreach (var containerReplacement in liveRegionEmission.ContainerReplacements)
            unquotedReplacements[containerReplacement.Key] = containerReplacement.Value;

        // [Splice] member generators: emit each valid generator's body VERBATIM as a factory-time local function
        // (its Parameter<>() declarations dropped — those fold into the factory parameters above and are in scope
        // as captured factory parameters), and record the member segment it contributes to its enclosing type's
        // member list (TemplateSyntaxQuoter splices it via BuildList<MemberDeclarationSyntax> at the generator's
        // declaration position).
        int spliceGeneratorCounter = 0;
        foreach (var generator in validSpliceGenerators)
        {
            var segment = EmitSpliceMemberGenerator(generator, semanticModel, preamble, ref spliceGeneratorCounter);
            spliceMemberSegments[generator.Method] = segment;
        }

        var prunableNodes = BranchPruneIdentifier.FindPrunableNodes(trimNodes, templateInfo.Source.Syntax);

        TemplateSyntaxQuoter quoter = new(
            semanticModel,
            unquotedReplacements,
            prunableNodes,
            includeTrivia: options.HasFlag(TemplateOption.PreserveTrivia),
            memberSegments: spliceMemberSegments,
            stringStagedRoots: stringStagedRoots);


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

    /// <summary>
    /// Emits a valid <c>[Splice]</c> member generator as factory-time code: a non-static local function carrying
    /// the generator's body VERBATIM (its <c>Parameter&lt;&gt;()</c> declarations dropped, since those values fold
    /// into the factory parameters and are captured in scope here), appended to the factory <paramref name="preamble"/>.
    /// Returns the member segment the generator contributes to its enclosing type's member list: a
    /// <c>ListSegment&lt;MemberDeclarationSyntax&gt;.Run(localFn())</c> for an enumerable shape, or the local-function
    /// call directly (implicitly a single-node segment) for a single member.
    /// </summary>
    private static ExpressionSyntax EmitSpliceMemberGenerator(
        SpliceMemberGenerator generator,
        SemanticModel semanticModel,
        List<StatementSyntax> preamble,
        ref int counter)
    {
        var methodSymbol = semanticModel.GetDeclaredSymbol(generator.Method);
        var templateSymbol = semanticModel.Compilation.GetTypeByMetadataName(typeof(global::Synto.Templating.Template).FullName!);
        var rewriter = new SpliceGeneratorBodyRewriter(semanticModel, templateSymbol);

        string localName = "__spliceGenerator_" + generator.Method.Identifier.Text + "_" + counter++;

        // Fully-qualified return type so it resolves in the generated factory regardless of its usings.
        TypeSyntax returnType = methodSymbol?.ReturnType is { } returnTypeSymbol
            ? ParseTypeName(returnTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            : generator.Method.ReturnType;

        BlockSyntax body;
        if (generator.Method.Body is { } block)
            body = (BlockSyntax)rewriter.Visit(block)!;
        else if (generator.Method.ExpressionBody is { } arrow)
            body = Block(ReturnStatement((ExpressionSyntax)rewriter.Visit(arrow.Expression)!.WithoutTrivia()));
        else
            body = Block();

        // Non-static local function so it captures the folded factory parameters (a `static` local function could
        // not reach them).
        var localFunction = LocalFunctionStatement(returnType, Identifier(localName))
            .WithParameterList(ParameterList())
            .WithBody(body);

        preamble.Add(localFunction);

        ExpressionSyntax call = InvocationExpression(IdentifierName(localName));

        if (generator.ReturnShape == SpliceMemberReturnShape.Enumerable)
        {
            // CollectionSyntaxExtensions.ListSegment<MemberDeclarationSyntax>.Run(localFn())
            return InvocationExpression(
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            IdentifierName(nameof(CollectionSyntaxExtensions)),
                            GenericName(Identifier(nameof(CollectionSyntaxExtensions.ListSegment<MemberDeclarationSyntax>)))
                                .WithTypeArgumentList(TypeArgumentList(SingletonSeparatedList<TypeSyntax>(IdentifierName(nameof(MemberDeclarationSyntax)))))),
                        IdentifierName(nameof(CollectionSyntaxExtensions.ListSegment<MemberDeclarationSyntax>.Run))))
                .AddArgumentListArguments(Argument(call));
        }

        // Single shape: the call returns a MemberDeclarationSyntax → implicit ListSegment conversion.
        return call;
    }

    /// <summary>
    /// Rewrites a <c>[Splice]</c> member generator body into factory-time code: drops each
    /// <c>var x = Parameter&lt;T&gt;();</c> declaration (the value folds into a captured factory parameter of the
    /// same name) and replaces any inline <c>Parameter&lt;T&gt;("name")</c> call with its resolved parameter
    /// identifier. Everything else is preserved verbatim — the generator's <c>foreach</c> stays a real loop.
    /// </summary>
    private sealed class SpliceGeneratorBodyRewriter : Microsoft.CodeAnalysis.CSharp.CSharpSyntaxRewriter
    {
        private readonly SemanticModel _semanticModel;
        private readonly INamedTypeSymbol? _templateSymbol;

        public SpliceGeneratorBodyRewriter(SemanticModel semanticModel, INamedTypeSymbol? templateSymbol)
        {
            _semanticModel = semanticModel;
            _templateSymbol = templateSymbol;
        }

        public override SyntaxNode? VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
        {
            if (node.Declaration.Variables.Count == 1
                && node.Declaration.Variables[0].Initializer?.Value is InvocationExpressionSyntax invocation
                && IsParameterCall(invocation))
            {
                return null;
            }

            return base.VisitLocalDeclarationStatement(node);
        }

        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (IsParameterCall(node)
                && node.ArgumentList.Arguments.Count == 1
                && _semanticModel.GetConstantValue(node.ArgumentList.Arguments[0].Expression) is { HasValue: true, Value: string name })
            {
                return IdentifierName(name);
            }

            return base.VisitInvocationExpression(node);
        }

        private bool IsParameterCall(InvocationExpressionSyntax node)
        {
            return _templateSymbol is not null
                && _semanticModel.GetSymbolInfo(node).Symbol is IMethodSymbol method
                && method.Name == nameof(global::Synto.Templating.Template.Parameter)
                && method.TypeArguments.Length == 1
                && SymbolEqualityComparer.Default.Equals(method.ContainingType, _templateSymbol);
        }
    }
}

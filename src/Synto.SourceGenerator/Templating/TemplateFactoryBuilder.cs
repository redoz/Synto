using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.Templating;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto;

/// <summary>
/// Builds the per-template <c>public static</c> syntax-factory method (the headline ~675-LOC unit, formerly
/// <c>TemplateFactorySourceGenerator.CreateSyntaxFactoryMethod</c>). The work is carved into named ordered
/// steps over one shared <see cref="TemplateBuildContext"/> accumulator. THE ORDER IS LOAD-BEARING and the
/// step sequence below is the explicit contract that was previously implicit-by-line-position: parameter
/// discovery order, preamble append order, "container replacement added LAST", and "inline/syntax/live lifts
/// populated before the island quoter and the final quoter" are all encoded by the order <see cref="Build"/>
/// invokes the steps. Any reordering changes the generated output. Runs entirely inside the generator
/// transform; nothing is captured into cached pipeline state (only the resulting source text flows out).
/// </summary>
internal static class TemplateFactoryBuilder
{
    public static MethodDeclarationSyntax? Build(
        List<DiagnosticInfo> diagnostics,
        SemanticModel semanticModel,
        UsingDirectiveSet additionalUsings,
        TemplateInfo templateInfo,
        TemplateOption options)
    {
        var ctx = new TemplateBuildContext(diagnostics, semanticModel, additionalUsings, templateInfo, options);

        // trim the template attribute
        ctx.TrimNodes.Add(templateInfo.AttributeSyntax);

        if (!DiscoverSpliceGenerators(ctx))
            return null;

        ResolveScopeAndTrimForeignChildren(ctx);

        LiftStagedTypeParameters(ctx);
        LiftSpliceParameters(ctx);
        LiftSyntaxParameters(ctx);

        if (!DiscoverStagedRootsAndClassify(ctx))
            return null;

        if (!CheckImpossibleCuts(ctx))
            return null;

        if (!DiscoverStagedRegions(ctx))
            return null;

        TransplantCarrierUsings(ctx);
        ComputeScalarFolds(ctx);

        LiftStagedParameters(ctx);
        LiftStagedLocals(ctx);

        InitValueLift(ctx);
        LiftStagedRootParameters(ctx);
        LiftQuoteParameters(ctx);
        LiftQuoteCalls(ctx);
        if (ctx.ConverterError)
            return null;

        if (!ResolveBuilderCalls(ctx))
            return null;

        ResolveSpliceCalls(ctx);

        if (!EmitLiveRegions(ctx))
            return null;

        EmitSpliceMemberGenerators(ctx);

        return QuoteAndAssemble(ctx);
    }

    // [Splice] member generators (static methods returning MemberDeclarationSyntax / IEnumerable<…>):
    // discover and validate each up front. An invalid shape is reported (SY1019 non-static, SY1020 bad
    // return type, SY1021 has parameters) and the template bails; a valid generator is recognized and
    // trimmed from the quoted output member set, then emitted as factory-time code below (its members are
    // spliced into the type's member list via BuildList<MemberDeclarationSyntax>).
    private static bool DiscoverSpliceGenerators(TemplateBuildContext ctx)
    {
        var spliceMemberGenerators = SpliceMemberGeneratorFinder.FindGenerators(ctx.SemanticModel, ctx.TemplateInfo.Source!.Syntax);
        bool spliceGeneratorError = false;

        foreach (var generator in spliceMemberGenerators)
        {
            var generatorLocation = generator.Method.Identifier.GetLocation();
            var generatorName = generator.Method.Identifier.Text;

            if (!generator.IsStatic)
            {
                ctx.Diagnostics.Add(TemplateDiagnostics.SpliceMethodMustBeStatic(generatorLocation, generatorName));
                spliceGeneratorError = true;
            }

            if (generator.ReturnShape == SpliceMemberReturnShape.Invalid)
            {
                ctx.Diagnostics.Add(TemplateDiagnostics.SpliceMethodBadReturnType(generatorLocation, generatorName, generator.Method.ReturnType.ToString()));
                spliceGeneratorError = true;
            }

            if (generator.HasParameters)
            {
                ctx.Diagnostics.Add(TemplateDiagnostics.SpliceMethodHasParameters(generatorLocation, generatorName));
                spliceGeneratorError = true;
            }

            if (generator.IsStatic && !generator.HasParameters && generator.ReturnShape != SpliceMemberReturnShape.Invalid)
            {
                // A valid generator is NOT trimmed: the member-list quoter substitutes it with its member
                // segment (BuildList run) at its declaration position. (Trimming it could make a single-generator
                // type collapse under BranchPruneIdentifier once its only member is gone.)
                ctx.ValidSpliceGenerators.Add(generator);
                foreach (var node in generator.Method.DescendantNodesAndSelf())
                    ctx.SpliceGeneratorNodes.Add(node);
            }
            else
            {
                // An invalid generator is reported above (the template bails); trim it so it is never a quoted
                // output member.
                ctx.TrimNodes.Add(generator.Method);
            }
        }

        return !spliceGeneratorError;
    }

    // Child [Template] methods nested in the carrier (Capability 1): a method-level [Template] inside this
    // class-level [Template] carrier is independently picked up by ForAttributeWithMetadataName and generates
    // its OWN factory. The carrier's ownership boundary is the single source of truth for "what is foreign to
    // this parent"; every per-template finder is re-based onto TemplateScopedWalker, which SKIPS a foreign
    // child's whole subtree (skip-during) so the child's staged roots / parameters / type-parameters are never
    // even visited — they cannot leak into the parent factory's signature. Here we only still TRIM each foreign
    // child from the parent's QUOTED OUTPUT (trimming-from-output is separate from the walk-skip; both sourced
    // from `scope`). An empty scope (a non-class carrier, or a class with no children) behaves like the
    // unscoped walk.
    private static void ResolveScopeAndTrimForeignChildren(TemplateBuildContext ctx)
    {
        ctx.Scope = TemplateScope.ForCarrier(ctx.SemanticModel, ctx.TemplateInfo.Source!.Syntax);
        foreach (var child in ctx.Scope.ForeignChildren)
            ctx.TrimNodes.Add(child);
    }

    private static void LiftStagedTypeParameters(TemplateBuildContext ctx)
    {
        var semanticModel = ctx.SemanticModel;
        var additionalUsings = ctx.AdditionalUsings;

        foreach (var replacements in StagedTypeParameterFinder.FindStagedTypeParameters(semanticModel, ctx.TemplateInfo.Source!.Syntax, ctx.Scope))
        {
            string typeParamName = replacements.TypeParameterSyntax.Identifier.Text;

            ExpressionSyntax typeSyntaxForTypeParam;
            if (replacements.IsSplice)
            {
                // [Splice] type parameter: splice a pre-built TypeSyntax verbatim. The factory parameter is typed
                // TypeSyntax (NOT ExpressionSyntax) so every TYPE-position use of the parameter lands in a
                // TypeSyntax slot and the generated factory compiles — the fix for the type-axis miscompile where
                // a spliced type was previously emitted as ExpressionSyntax (CS1503 ExpressionSyntax -> TypeSyntax).
                // TODO make a little utility for this
                while (!ctx.ParamNames.Add(typeParamName))
                    typeParamName += '_';

                typeSyntaxForTypeParam = IdentifierName(typeParamName);

                ctx.Parameters.Add(Parameter(Identifier(typeParamName)).WithType(additionalUsings.GetTypeName(ParseTypeName(typeof(TypeSyntax).FullName!))));
            }
            else
            {
                ctx.InlinedTypeParams.Add(replacements.TypeParameterSymbol);

                // TODO make a little utility for this
                while (!ctx.InlinedTypeParamNames.Add(typeParamName))
                    typeParamName += '_';

                ctx.TypeParams.Add(TypeParameter(typeParamName));

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

                ctx.Preamble.Add(
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
                ctx.UnquotedReplacements.Add(typeSyntax, typeSyntaxForTypeParam);

            ctx.TrimNodes.Add(replacements.TypeParameterSyntax);
        }
    }

    // [Splice] value parameters: a pre-built ExpressionSyntax supplied to the factory and spliced VERBATIM
    // (no evaluation, no value lift). The factory parameter is typed ExpressionSyntax and every use of it is
    // replaced by the parameter as-is.
    private static void LiftSpliceParameters(TemplateBuildContext ctx)
    {
        var additionalUsings = ctx.AdditionalUsings;
        foreach (var replacements in SpliceParameterFinder.FindSpliceParameters(ctx.SemanticModel, ctx.TemplateInfo.Source!.Syntax, ctx.Scope))
        {
            string paramName = replacements.Parameter.Identifier.Text;
            while (!ctx.ParamNames.Add(paramName))
                paramName += '_';

            var parameterType = additionalUsings.GetTypeName(ParseTypeName(typeof(ExpressionSyntax).FullName!));
            ctx.Parameters.Add(Parameter(Identifier(paramName)).WithType(parameterType));

            foreach (var identifierNameSyntax in replacements.References)
                ctx.UnquotedReplacements.Add(identifierNameSyntax, IdentifierName(paramName));

            ctx.TrimNodes.Add(replacements.Parameter);
        }
    }

    private static void LiftSyntaxParameters(TemplateBuildContext ctx)
    {
        var additionalUsings = ctx.AdditionalUsings;
        foreach (var replacements in SyntaxParameterFinder.FindSyntaxParameters(ctx.SemanticModel, ctx.TemplateInfo.Source!.Syntax, ctx.Scope))
        {
            string paramName = replacements.Parameter.Identifier.Text;
            while (!ctx.ParamNames.Add(paramName))
                paramName += '_';

            var parameterType = additionalUsings.GetTypeName(ParseTypeName(typeof(ExpressionSyntax).FullName!));

            ctx.Parameters.Add(Parameter(Identifier(paramName)).WithType(parameterType));

            foreach (var identifierNameSyntax in replacements.References)
                ctx.UnquotedReplacements.Add(identifierNameSyntax, IdentifierName(paramName));

            ctx.TrimNodes.Add(replacements.Parameter);
        }
    }

    // Staged roots (Template.Parameter<T>() live parameters, Template.Unquote<T>() locals, [Unquote] parameters):
    // discover them, then classify the body's binding-time partition so the staging emitter can unroll
    // live control regions (plan Task 6). All of this is semantic work inside the transform; nothing here
    // is captured into pipeline state.
    private static bool DiscoverStagedRootsAndClassify(TemplateBuildContext ctx)
    {
        var semanticModel = ctx.SemanticModel;

        var stagedParameterResult = StagedParameterFinder.FindStagedParameters(semanticModel, ctx.TemplateInfo.Source!.Syntax, ctx.Scope);
        ctx.Diagnostics.AddRange(stagedParameterResult.Diagnostics);

        // A live-parameter naming error is a usage error: bail with the diagnostic(s) already reported
        // rather than emit a factory built from an unresolved/ambiguous parameter set.
        if (stagedParameterResult.Diagnostics.Count > 0)
            return false;

        var stagedRootResult = StagedRootFinder.FindStagedRoots(semanticModel, ctx.TemplateInfo.Source.Syntax, ctx.Scope);

        // The finders are scoped (TemplateScopedWalker skips foreign child subtrees), so a root that lived ONLY
        // inside a child is never discovered, and a root SHARED with the parent (e.g. `columns`, also referenced in
        // a parent member-generator) is still discovered via its parent-side declaration + references. No
        // per-consumption filtering of child-internal roots is required.
        ctx.StagedParameters = stagedParameterResult.Parameters;
        ctx.StagedLocals = stagedRootResult.Locals;
        ctx.StagedRootParameters = stagedRootResult.Parameters;

        // Seed the binding-time classifier from every live root symbol (parameters + bound locals), then find
        // the live control regions (a foreach driven by a live root) to unroll.
        var stagedRootSymbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var classifierRoots = new List<StagedRoot>();

        foreach (var stagedParameter in ctx.StagedParameters)
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

        foreach (var stagedLocal in ctx.StagedLocals)
        {
            if (semanticModel.GetDeclaredSymbol(stagedLocal.Declaration.Declaration.Variables[0]) is { } symbol && stagedRootSymbols.Add(symbol))
                classifierRoots.Add(new StagedRoot(symbol, stagedLocal.ValueExpression));
        }

        foreach (var stagedParameterRoot in ctx.StagedRootParameters)
        {
            if (semanticModel.GetDeclaredSymbol(stagedParameterRoot.Parameter) is { } symbol && stagedRootSymbols.Add(symbol))
                classifierRoots.Add(new StagedRoot(symbol));
        }

        // Inline Quote(value) calls: discovered up front so the classifier can SHIELD their live arguments
        // (output-world boundary). The actual value-lift + replacement is wired below, alongside the other
        // value lifts, once the [Runtime] converter state is resolved.
        ctx.QuoteCalls = QuoteCallFinder.FindQuoteCalls(semanticModel, ctx.TemplateInfo.Source.Syntax, ctx.Scope);
        var quoteCallNodes = new HashSet<SyntaxNode>(ctx.QuoteCalls.Select(call => (SyntaxNode)call.Invocation));

        ctx.Partition = BindingTimeClassifier.Classify(semanticModel, ctx.TemplateInfo.Source.Syntax, classifierRoots, quoteCallNodes);
        return true;
    }

    // An impossible cut (a live binding that transitively depends on an output-world/quoted value) cannot be
    // evaluated at factory time: report it (SY1013) with the offending span and bail rather than emit a
    // factory that would not compile.
    private static bool CheckImpossibleCuts(TemplateBuildContext ctx)
    {
        if (ctx.Partition.ImpossibleCuts.Count > 0)
        {
            foreach (var cut in ctx.Partition.ImpossibleCuts)
                ctx.Diagnostics.Add(TemplateDiagnostics.ImpossibleCut(cut.Node.GetLocation(), cut.Reason));
            return false;
        }

        return true;
    }

    private static bool DiscoverStagedRegions(TemplateBuildContext ctx)
    {
        var semanticModel = ctx.SemanticModel;

        // Exclude any control region inside a [Splice] generator body: that `foreach` is a real factory-time loop
        // emitted verbatim by the member-generator path, NOT a live region to unroll.
        ctx.StagedRegions = StagedRegionFinder.FindRegions(semanticModel, ctx.TemplateInfo.Source!.Syntax, ctx.Partition)
            .Where(region => !ctx.SpliceGeneratorNodes.Contains(region.Control))
            .ToList();
        ctx.RegionConsumedNodes = StagedRegionFinder.ComputeConsumedNodes(ctx.StagedRegions);

        // A live control statement that region discovery did not pick up (it is an embedded, non-block statement
        // of an output-world construct, so it owns no container to key the replacement at) and that no other
        // region consumes would otherwise fall through to the normal quoter — lifting its live driver into the
        // OUTPUT (wrong code, no signal). Degrade to SY1014 instead of a silent mis-expansion.
        var unhandledStagedControl = ctx.TemplateInfo.Source.Syntax.DescendantNodes()
            .OfType<StatementSyntax>()
            .Where(statement => ctx.Partition.IsStagedControl(statement) && !ctx.RegionConsumedNodes.Contains(statement) && !ctx.SpliceGeneratorNodes.Contains(statement))
            .ToList();
        if (unhandledStagedControl.Count > 0)
        {
            foreach (var statement in unhandledStagedControl)
                ctx.Diagnostics.Add(TemplateDiagnostics.UnsupportedStagedShape(statement.GetLocation(), "a staged control region must be a direct statement of a block to unroll in v1"));
            return false;
        }

        return true;
    }

    // F2 usings transplant (spec §5.2): a live control region runs VERBATIM in the factory, so the carrier's
    // own `using` directives that its live scaffold relies on (e.g. `System.Linq` for `.Where(...)`) must be
    // merged into the generated factory file — otherwise the verbatim code does not resolve. Only simple
    // namespace usings are transplanted (deduped against the quoter's RequiredUsings); `using static …` and
    // alias usings are skipped (UsingDirectiveSet ignores them anyway), and Synto.* usings are excluded so
    // the inert facade/marker surface (e.g. `using static Synto.Templating.Template;`, `using Synto.Templating;`)
    // is never pulled into the factory scope where it could collide with the injected internal surface.
    private static void TransplantCarrierUsings(TemplateBuildContext ctx)
    {
        if (ctx.StagedRegions.Count > 0
            && ctx.TemplateInfo.Source!.Syntax.SyntaxTree.GetRoot() is CompilationUnitSyntax carrierUnit)
        {
            foreach (var carrierUsing in carrierUnit.Usings)
            {
                if (!carrierUsing.StaticKeyword.IsKind(SyntaxKind.None) || carrierUsing.Alias is not null)
                    continue;
                if (carrierUsing.Name is not { } usingName || usingName.ToString().StartsWith("Synto", StringComparison.Ordinal))
                    continue;

                ctx.AdditionalUsings.AddNamespace(usingName);
            }
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
    private static void ComputeScalarFolds(TemplateBuildContext ctx)
    {
        var semanticModel = ctx.SemanticModel;

        var rootSymbolToStagedParameter = new Dictionary<ISymbol, StagedParameter>(SymbolEqualityComparer.Default);
        foreach (var stagedParameter in ctx.StagedParameters)
            foreach (var symbol in stagedParameter.Symbols)
                rootSymbolToStagedParameter[symbol] = stagedParameter;

        if (rootSymbolToStagedParameter.Count > 0)
        {
            foreach (var memberAccess in ctx.TemplateInfo.Source!.Syntax.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
            {
                if (memberAccess.Expression is not IdentifierNameSyntax receiver)
                    continue;
                if (ctx.RegionConsumedNodes.Contains(memberAccess) || ctx.SpliceGeneratorNodes.Contains(memberAccess))
                    continue;
                if (semanticModel.GetSymbolInfo(receiver).Symbol is not { } receiverSymbol
                    || !rootSymbolToStagedParameter.TryGetValue(receiverSymbol, out var ownerParameter))
                    continue;
                if (semanticModel.GetTypeInfo(memberAccess).Type is not { } resultType || !ValueLift.IsBuiltInLiteralType(resultType))
                    continue;

                ctx.FoldClaimedReferences.Add(receiver);
                if (!ctx.FoldsByStagedParameter.TryGetValue(ownerParameter, out var list))
                    ctx.FoldsByStagedParameter[ownerParameter] = list = new List<MemberAccessExpressionSyntax>();
                list.Add(memberAccess);
            }
        }
    }

    private static void LiftStagedParameters(TemplateBuildContext ctx)
    {
        var additionalUsings = ctx.AdditionalUsings;

        foreach (var stagedParameter in ctx.StagedParameters)
        {
            string paramName = stagedParameter.Name;
            while (!ctx.ParamNames.Add(paramName))
                paramName += '_';

            // Staged scalar count-fold: emit `(paramName.Member).ToSyntax()` for each claimed member-access on this
            // staged root, keyed at the member-access node so the quoter splices the factory-time literal lift in
            // place of the whole `root.Member` expression.
            if (ctx.FoldsByStagedParameter.TryGetValue(stagedParameter, out var folds))
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
                    ctx.UnquotedReplacements[memberAccess] = lift;
                }
            }

            // Map EVERY declaration-site symbol to the shared factory parameter name so the live-region renamer
            // rewrites each member's local reference (the foreach driver) to the one factory parameter.
            foreach (var rootSymbol in stagedParameter.Symbols)
                ctx.RootNames[rootSymbol] = paramName;

            var parameterType = ParseTypeName(stagedParameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            ctx.Parameters.Add(Parameter(Identifier(paramName)).WithType(parameterType));

            // Interpolation staged-fold: a string-typed staged root may be baked into surrounding literal text
            // when used as a bare interpolation hole. Map EVERY reference (depth-0 AND region-consumed) to the
            // factory-time raw value accessor (the shared factory parameter), which is in scope everywhere the
            // factory body / staged-region scaffold runs.
            if (stagedParameter.Type.SpecialType == SpecialType.System_String)
            {
                foreach (var reference in stagedParameter.References)
                    ctx.StringStagedRoots[reference] = IdentifierName(paramName);
            }

            // References consumed by a live region are handled by the verbatim scaffold (which uses the factory
            // parameter directly as a runtime value); only depth-0 references lift via value.ToSyntax() — binds
            // to the file-local LiteralSyntaxExtensions (built-in types) or the generic ToSyntax<T> fallback.
            var depthZeroReferences = stagedParameter.References.Where(reference => !ctx.RegionConsumedNodes.Contains(reference) && !ctx.SpliceGeneratorNodes.Contains(reference) && !ctx.FoldClaimedReferences.Contains(reference)).ToList();
            if (depthZeroReferences.Count > 0)
            {
                ExpressionSyntax conversion = InvocationExpression(
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName(paramName),
                        IdentifierName("ToSyntax")));

                var syntaxForParamIdentifier = Identifier("syntaxForParam_" + paramName);
                ctx.Preamble.Add(
                    LocalDeclarationStatement(
                        VariableDeclaration(
                            additionalUsings.GetTypeName(ParseTypeName(typeof(ExpressionSyntax).FullName!)),
                            SingletonSeparatedList(
                                VariableDeclarator(
                                    syntaxForParamIdentifier,
                                    null,
                                    EqualsValueClause(conversion))))));

                foreach (var reference in depthZeroReferences)
                    ctx.UnquotedReplacements.Add(reference, IdentifierName(syntaxForParamIdentifier));
            }

            foreach (var trimNode in stagedParameter.TrimNodes)
                ctx.TrimNodes.Add(trimNode);
        }
    }

    // Staged bound roots (Template.Unquote<T>() locals + [Unquote] parameters): the bound expression runs at
    // factory-build time and the resulting runtime value is lifted into the quoted output. Depth-0, a
    // live LOCAL hoists its `var n = <expr>;` verbatim into the factory body (a real runtime local) and
    // each use lifts via `n.ToSyntax()`; a [Unquote] PARAMETER becomes a caller-supplied factory parameter
    // lifted the same way (an [Unquote] value, classified live for later staging).
    private static void LiftStagedLocals(TemplateBuildContext ctx)
    {
        var semanticModel = ctx.SemanticModel;
        var additionalUsings = ctx.AdditionalUsings;

        foreach (var stagedLocal in ctx.StagedLocals)
        {
            // Hoist the runtime local: `var n = <expr>;` (the Unquote(...) carrier is unwrapped to its argument,
            // which is evaluated at factory-build time). Build it fresh so source comments/trivia don't leak.
            ctx.Preamble.Add(
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
            var liveLocalDepthZeroReferences = stagedLocal.References.Where(reference => !ctx.RegionConsumedNodes.Contains(reference)).ToList();
            if (liveLocalDepthZeroReferences.Count > 0)
            {
                var syntaxForStagedIdentifier = Identifier("syntaxForStaged_" + stagedLocal.Name);
                ctx.Preamble.Add(
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
                    ctx.UnquotedReplacements.Add(reference, IdentifierName(syntaxForStagedIdentifier));
            }

            // Interpolation staged-fold: a string-typed Unquote<string>(…) local may be baked into surrounding
            // literal text when used as a bare interpolation hole. The hoisted runtime local (`var name = …;`) is
            // in scope throughout the factory body, so its name is the raw value accessor. The String decision is
            // made HERE at emission off the Unquote<T> type argument; only the resulting nodes leave this scope.
            if ((semanticModel.GetSymbolInfo(stagedLocal.StagedCall).Symbol as IMethodSymbol)?.TypeArguments[0].SpecialType == SpecialType.System_String)
            {
                foreach (var reference in stagedLocal.References)
                    ctx.StringStagedRoots[reference] = IdentifierName(stagedLocal.Name);
            }

            ctx.TrimNodes.Add(stagedLocal.Declaration);
        }
    }

    // The shared value→syntax lift policy for this invocation: ONE instance whose [Runtime] converter cache
    // is lazy (walked only when an [Unquote]/[Quote] parameter of a concrete, non-built-in type is actually
    // encountered) and shared across all three lift sites. All of this is semantic work done INSIDE the
    // transform; nothing here is captured into pipeline state (only the resulting source text flows out).
    private static void InitValueLift(TemplateBuildContext ctx)
    {
        var semanticModel = ctx.SemanticModel;
        var runtimeAttribute = semanticModel.Compilation.GetTypeByMetadataName(typeof(RuntimeAttribute).FullName!);
        var expressionSyntaxSymbol = semanticModel.Compilation.GetTypeByMetadataName(typeof(ExpressionSyntax).FullName!);
        ctx.ValueLift = new ValueLift(semanticModel, runtimeAttribute, expressionSyntaxSymbol);
    }

    private static void LiftStagedRootParameters(TemplateBuildContext ctx)
    {
        var additionalUsings = ctx.AdditionalUsings;
        var valueLift = ctx.ValueLift;

        foreach (var stagedParameterRoot in ctx.StagedRootParameters)
        {
            string paramName = stagedParameterRoot.Parameter.Identifier.Text;
            while (!ctx.ParamNames.Add(paramName))
                paramName += '_';

            // The [Unquote] value lift: the supplied value is converted to syntax at factory time and the result
            // spliced into the output (subsumes the old value-lift behavior). By default `value.ToSyntax()`,
            // which binds to the file-local LiteralSyntaxExtensions (built-in types) or the generic ToSyntax<T>
            // fallback (inlined generic type parameters); a custom type instead binds to a discovered [Runtime]
            // converter, called fully-qualified below.
            TypeSyntax parameterType = stagedParameterRoot.Parameter.Type!;
            var paramType = stagedParameterRoot.Type;

            // A generic type reference must be declared on the factory method (unless already inlined as a type param).
            if (paramType is ITypeParameterSymbol typeParam && !ctx.InlinedTypeParams.Contains(typeParam))
            {
                string typeParamName = typeParam.Name;
                while (!ctx.InlinedTypeParamNames.Add(typeParamName))
                    typeParamName += '_';

                parameterType = IdentifierName(typeParamName);
                ctx.TypeParams.Add(TypeParameter(typeParamName));
            }

            // The [Unquote] value lift: the supplied value is converted to syntax at factory time and the result
            // spliced into the output. Shared with the [Quote] paths via TryEmitValueLift (the generic-type-param
            // declaration above stays [Unquote]-only; [Quote] is value-axis only).
            if (!valueLift.TryEmitValueLift(paramType, IdentifierName(paramName), stagedParameterRoot.Parameter.Type!.GetLocation(), out ExpressionSyntax conversion, out var liftDiagnostic))
            {
                ctx.Diagnostics.Add(liftDiagnostic!.Value);
                ctx.ConverterError = true;
            }
            else if (paramType is not ITypeParameterSymbol && !ValueLift.IsBuiltInLiteralType(paramType))
            {
                // Emit the parameter with its fully-qualified declared type so it resolves in the generated file
                // regardless of which usings the template's file happened to carry.
                parameterType = ParseTypeName(paramType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            }

            var syntaxForParamIdentifier = Identifier("syntaxForParam_" + paramName);
            ctx.Preamble.Add(
                LocalDeclarationStatement(
                    VariableDeclaration(
                        additionalUsings.GetTypeName(ParseTypeName(typeof(ExpressionSyntax).FullName!)),
                        SingletonSeparatedList(
                            VariableDeclarator(
                                syntaxForParamIdentifier,
                                null,
                                EqualsValueClause(conversion))))));

            ctx.Parameters.Add(Parameter(Identifier(paramName)).WithType(parameterType));

            foreach (var reference in stagedParameterRoot.References)
                ctx.UnquotedReplacements.Add(reference, IdentifierName(syntaxForParamIdentifier));

            // Interpolation staged-fold: a string-typed [Unquote] parameter may be baked into surrounding literal
            // text when used as a bare interpolation hole. The factory parameter is the raw value accessor.
            if (paramType.SpecialType == SpecialType.System_String)
            {
                foreach (var reference in stagedParameterRoot.References)
                    ctx.StringStagedRoots[reference] = IdentifierName(paramName);
            }

            ctx.TrimNodes.Add(stagedParameterRoot.Parameter);
        }
    }

    // [Quote] value parameters: the SAME value-lift as an [Unquote] value (via TryEmitValueLift), but the
    // parameter is NEVER seeded into BindingTimeClassifier (it is not in stagedRootResult), so a control
    // construct referencing only a quoted value stays Quoted and is emitted as a runtime construct rather than
    // unrolled (spec §3). Value-axis only ([Quote] is AttributeTargets.Parameter), so there is no
    // generic-type-parameter branch here.
    private static void LiftQuoteParameters(TemplateBuildContext ctx)
    {
        var semanticModel = ctx.SemanticModel;
        var additionalUsings = ctx.AdditionalUsings;
        var valueLift = ctx.ValueLift;

        foreach (var quoteParameter in QuoteParameterFinder.FindQuoteParameters(semanticModel, ctx.TemplateInfo.Source!.Syntax, ctx.Scope))
        {
            string paramName = quoteParameter.Parameter.Identifier.Text;
            while (!ctx.ParamNames.Add(paramName))
                paramName += '_';

            TypeSyntax parameterType = quoteParameter.Parameter.Type!;
            var paramType = semanticModel.GetDeclaredSymbol(quoteParameter.Parameter)?.Type;

            if (paramType is null)
            {
                // Unbound parameter type: cannot lift. Bail (the consumer's own CS error already covers this).
                ctx.ConverterError = true;
                continue;
            }

            if (!valueLift.TryEmitValueLift(paramType, IdentifierName(paramName), quoteParameter.Parameter.Type!.GetLocation(), out ExpressionSyntax conversion, out var liftDiagnostic))
            {
                ctx.Diagnostics.Add(liftDiagnostic!.Value);
                ctx.ConverterError = true;
                continue;
            }

            if (paramType is not ITypeParameterSymbol && !ValueLift.IsBuiltInLiteralType(paramType))
            {
                // Emit the parameter with its fully-qualified declared type so it resolves in the generated file
                // regardless of which usings the template's file happened to carry.
                parameterType = ParseTypeName(paramType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            }

            var syntaxForQuoteIdentifier = Identifier("syntaxForQuote_" + paramName);
            ctx.Preamble.Add(
                LocalDeclarationStatement(
                    VariableDeclaration(
                        additionalUsings.GetTypeName(ParseTypeName(typeof(ExpressionSyntax).FullName!)),
                        SingletonSeparatedList(
                            VariableDeclarator(
                                syntaxForQuoteIdentifier,
                                null,
                                EqualsValueClause(conversion))))));

            ctx.Parameters.Add(Parameter(Identifier(paramName)).WithType(parameterType));

            foreach (var reference in quoteParameter.References)
                ctx.UnquotedReplacements.Add(reference, IdentifierName(syntaxForQuoteIdentifier));

            ctx.TrimNodes.Add(quoteParameter.Parameter);
        }
    }

    // Inline Quote(value) calls: lift the value argument at the call position (the same value→syntax lift as
    // an [Unquote]/[Quote] value) and key the replacement at the INVOCATION node, so the quoter splices the
    // lifted syntax in place of the Quote(...) call. The classifier already shielded these as output-world
    // boundaries (so an enclosing loop/condition stayed Quoted).
    private static void LiftQuoteCalls(TemplateBuildContext ctx)
    {
        var semanticModel = ctx.SemanticModel;
        var valueLift = ctx.ValueLift;

        foreach (var quoteCall in ctx.QuoteCalls)
        {
            var valueType = semanticModel.GetTypeInfo(quoteCall.ValueArgument).Type;
            if (valueType is null)
            {
                ctx.ConverterError = true;
                continue;
            }

            // Use the argument as-is for primary expressions; parenthesize anything else so the lifted
            // member-access form (`value.ToSyntax()`) binds to the whole value, not just its trailing operand.
            ExpressionSyntax valueAccess = quoteCall.ValueArgument is IdentifierNameSyntax or LiteralExpressionSyntax or MemberAccessExpressionSyntax or InvocationExpressionSyntax or ElementAccessExpressionSyntax or ParenthesizedExpressionSyntax
                ? quoteCall.ValueArgument.WithoutTrivia()
                : ParenthesizedExpression(quoteCall.ValueArgument.WithoutTrivia());

            if (!valueLift.TryEmitValueLift(valueType, valueAccess, quoteCall.ValueArgument.GetLocation(), out ExpressionSyntax quoteCallConversion, out var quoteCallDiagnostic))
            {
                ctx.Diagnostics.Add(quoteCallDiagnostic!.Value);
                ctx.ConverterError = true;
                continue;
            }

            ctx.UnquotedReplacements[quoteCall.Invocation] = quoteCallConversion;
        }
    }

    // Syntax builders (Task 3): built-in Template.Member/TypeOf facade calls (recognized by binding) and
    // user [SyntaxBuilder] facade calls (recognized structurally) are replaced — keyed at the invocation
    // node — by a fully-qualified static call of the paired builder over processed arguments. A [Quoted]
    // builder parameter receives the QUOTE of the call argument (an output-world island); an unmarked
    // parameter receives the live/computed value verbatim. No copy of the builder is injected into the
    // generated output (the built-in builders are injected internal; user builders are the consumer's own
    // code) — both are called fully-qualified, so the output carries no Synto.Core runtime dependency.
    private static bool ResolveBuilderCalls(TemplateBuildContext ctx)
    {
        var semanticModel = ctx.SemanticModel;

        var builderResult = FacadeCallFinder.FindBuilderCalls(semanticModel, ctx.TemplateInfo.Source!.Syntax);
        ctx.Diagnostics.AddRange(builderResult.Diagnostics);

        // A builder facade-synthesis / argument-binding / ambiguity error is a usage error: bail with the
        // diagnostic(s) already reported rather than emit a factory built from an unresolved builder call.
        if (builderResult.Diagnostics.Count > 0)
            return false;

        if (builderResult.Calls.Count > 0)
        {
            // A fresh quoter for the [Quoted] islands inside builder arguments: it carries the lifts collected
            // so far (inline/syntax/live parameter replacements) so an island that references a lifted value
            // quotes correctly. It never descends into a builder invocation (those are not yet in the map).
            var islandQuoter = new TemplateSyntaxQuoter(semanticModel, ctx.UnquotedReplacements, new HashSet<SyntaxNode>(), includeTrivia: false, stringStagedRoots: ctx.StringStagedRoots);

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

                ctx.UnquotedReplacements[call.Invocation] = builderInvocation;
            }
        }

        return true;
    }

    // Template.Splice(node) body calls (recognized by binding): splice the pre-built ExpressionSyntax
    // argument VERBATIM into the output at the call position — the inline counterpart to a [Splice]
    // parameter. Keyed at the invocation so the quoter substitutes the argument expression as-is.
    private static void ResolveSpliceCalls(TemplateBuildContext ctx)
    {
        foreach (var spliceCall in SpliceCallFinder.FindSpliceCalls(ctx.SemanticModel, ctx.TemplateInfo.Source!.Syntax, ctx.Scope))
            ctx.UnquotedReplacements[spliceCall.Invocation] = (ExpressionSyntax)spliceCall.Argument.WithoutTrivia();
    }

    // Unroll the live control regions: each foreach over a live root becomes a verbatim scaffold in the
    // factory preamble (collecting quoted islands into a run) plus a single replacement keyed at the
    // region's owning container block (spec §5.3). The fixed-sibling quoter and island quoters carry the
    // lifts collected so far; the container replacement is added LAST so neither sees it.
    private static bool EmitLiveRegions(TemplateBuildContext ctx)
    {
        var liveRegionEmission = StagedRegionEmitter.Emit(
            ctx.SemanticModel,
            ctx.Partition,
            ctx.StagedRegions,
            ctx.Partition.StagedSymbols,
            ctx.RootNames,
            ctx.UnquotedReplacements,
            ctx.TrimNodes,
            ctx.StringStagedRoots,
            ref ctx.StagedRegionCounter);

        ctx.Diagnostics.AddRange(liveRegionEmission.Diagnostics);

        // An unsupported live shape is a usage error: bail with the diagnostic(s) already reported rather than
        // emit a mis-expanded factory.
        if (liveRegionEmission.Diagnostics.Count > 0)
            return false;

        ctx.Preamble.AddRange(liveRegionEmission.Preamble);

        foreach (var containerReplacement in liveRegionEmission.ContainerReplacements)
            ctx.UnquotedReplacements[containerReplacement.Key] = containerReplacement.Value;

        return true;
    }

    // [Splice] member generators: emit each valid generator's body VERBATIM as a factory-time local function
    // (its Parameter<>() declarations dropped — those fold into the factory parameters above and are in scope
    // as captured factory parameters), and record the member segment it contributes to its enclosing type's
    // member list (TemplateSyntaxQuoter splices it via BuildList<MemberDeclarationSyntax> at the generator's
    // declaration position).
    private static void EmitSpliceMemberGenerators(TemplateBuildContext ctx)
    {
        int spliceGeneratorCounter = 0;
        foreach (var generator in ctx.ValidSpliceGenerators)
        {
            var segment = SpliceMemberGeneratorEmitter.Emit(generator, ctx.SemanticModel, ctx.Preamble, ref spliceGeneratorCounter);
            ctx.SpliceMemberSegments[generator.Method] = segment;
        }
    }

    private static MethodDeclarationSyntax? QuoteAndAssemble(TemplateBuildContext ctx)
    {
        var semanticModel = ctx.SemanticModel;
        var additionalUsings = ctx.AdditionalUsings;

        var prunableNodes = BranchPruneIdentifier.FindPrunableNodes(ctx.TrimNodes, ctx.TemplateInfo.Source!.Syntax);

        TemplateSyntaxQuoter quoter = new(
            semanticModel,
            ctx.UnquotedReplacements,
            prunableNodes,
            includeTrivia: ctx.Options.HasFlag(TemplateOption.PreserveTrivia),
            memberSegments: ctx.SpliceMemberSegments,
            stringStagedRoots: ctx.StringStagedRoots);


        if (!TemplateSyntaxQuoterInvoker.TryQuote(
                quoter,
                ctx.TemplateInfo,
                out ExpressionSyntax? syntaxTreeExpr,
                out TypeSyntax? returnType,
                out DiagnosticInfo? error))
        {
            if (error is { } errorInfo)
                ctx.Diagnostics.Add(errorInfo);
            return null;
        }


        var syntaxFactoryMethod = MethodDeclaration(additionalUsings.GetTypeName(returnType!), ctx.TemplateInfo.Source.Identifier)
            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword))
            .WithBody(Block()
                .AddStatements(ctx.Preamble.ToArray())
                .AddStatements(ReturnStatement(syntaxTreeExpr)));

        if (ctx.TypeParams.Count > 0)
            syntaxFactoryMethod = syntaxFactoryMethod.WithTypeParameterList(TypeParameterList(SeparatedList(ctx.TypeParams)));

        if (ctx.Parameters.Count > 0)
            syntaxFactoryMethod = syntaxFactoryMethod.WithParameterList(ParameterList(SeparatedList(ctx.Parameters)));

        return syntaxFactoryMethod;
    }
}

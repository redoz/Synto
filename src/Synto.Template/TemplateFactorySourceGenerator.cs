using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Synto.Formatting;
using Synto.Templating;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using Synto.Generators.Templating.Emit;
using Synto.Generators.Templating.Diagnostics;
using Synto.Generators.Templating.Discovery;

namespace Synto.Generators.Templating;


[Generator(LanguageNames.CSharp)]
public class TemplateFactorySourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // The whole template is processed inside the transform so the value flowing through the pipeline is
        // an equatable GenerationResult (just generated text + diagnostic data). This keeps the
        // SemanticModel / symbols / syntax nodes out of the cached pipeline state, which restores
        // incrementality and avoids rooting the compilation in memory across edits.
        // The parameterless [Template] form carries no target. A top-level bare [Template] is misuse (SY1033);
        // a bare [Template] nested inside a [Template]/[Template<T>] carrier is an interim no-op (the parent takes
        // ownership as a private sub-template in Phase 2). Either way it never derives a target, so it flows through
        // its own arity-0 transform rather than GenerateTemplate.
        var results = context.SyntaxProvider.ForAttributeWithMetadataName(
                typeof(TemplateAttribute).FullName!,
                static (node, cancellationToken) => true,
                static (syntaxContext, cancellationToken) => GenerateBareTemplate(syntaxContext))
            .WithTrackingName(TemplateTrackingNames.Transform)
            .Where(static result => result is not null)
            .WithTrackingName(TemplateTrackingNames.Result);

        context.RegisterSourceOutput(results, static (context, result) => context.Emit(result!.Value));

        // Second discovery source: the generic [Template<T>] form, whose factory target is carried by the type
        // argument. It flows through the identical GenerateTemplate/Emit shape (TemplateInfo.Create derives the
        // target from the attribute class's type argument) but gets its own tracking names so the all-steps
        // cacheability guards stay unambiguous across the two pipelines.
        var genericResults = context.SyntaxProvider.ForAttributeWithMetadataName(
                typeof(TemplateAttribute<>).FullName!,
                static (node, cancellationToken) => true,
                static (syntaxContext, cancellationToken) => GenerateTemplate(syntaxContext))
            .WithTrackingName(TemplateTrackingNames.GenericTransform)
            .Where(static result => result is not null)
            .WithTrackingName(TemplateTrackingNames.GenericResult);

        context.RegisterSourceOutput(genericResults, static (context, result) => context.Emit(result!.Value));
    }

    private static GenerationResult? GenerateBareTemplate(GeneratorAttributeSyntaxContext syntaxContext)
    {
        // Nested inside a [Template]/[Template<T>] carrier: interim no-op — the parent will own this as a private
        // sub-template in Phase 2. Nothing is emitted and no diagnostic is raised (characterized pin).
        if (NestedTemplateCheck.IsNestedInTemplateCarrier(syntaxContext.SemanticModel, syntaxContext.TargetNode))
            return null;

        // Top-level bare [Template]: no target can be derived, so report SY1033 (use [Template<T>]). Located on the
        // attribute application so the squiggle lands on the offending [Template].
        var diagnostics = new List<DiagnosticInfo>();
        var location = syntaxContext.Attributes.FirstOrDefault()?.ApplicationSyntaxReference?.GetSyntax().GetLocation();
        diagnostics.Add(TemplateDiagnostics.BareTemplateRequiresTarget(location));

        return new GenerationResult(null, null, new EquatableArray<DiagnosticInfo>(diagnostics.ToImmutableArray()));
    }

    private static GenerationResult? GenerateTemplate(GeneratorAttributeSyntaxContext syntaxContext)
    {
        // Thin wrapper: unpack the generator-only context and delegate to the shared plain-input core
        // (highlightsOnly: false — the generator's full-build path, byte-identical to before).
        return TryGenerate(syntaxContext.SemanticModel, syntaxContext.TargetNode, syntaxContext.Attributes, highlightsOnly: false, CancellationToken.None);
    }

    // Shared discovery+emit core over PLAIN inputs (decoupled from GeneratorAttributeSyntaxContext) so the SY1035
    // highlight analyzer can drive the identical classification+emit pass with highlightsOnly:true and discard the
    // factory. The generator calls it with highlightsOnly:false — behavior byte-identical to today. The
    // nested-carrier SY1034 check and the TemplateValidator.Validate gate run in BOTH modes so the analyzer
    // highlights exactly the templates the generator would build. Interim workaround — Option C (front-end
    // Classify extraction) is the 1.0 fix; see spec §Known debt / 1.0 gate.
    internal static GenerationResult? TryGenerate(SemanticModel semanticModel, SyntaxNode target, ImmutableArray<AttributeData> attributes, bool highlightsOnly, CancellationToken cancellationToken)
    {
        // A generic [Template<T>] nested inside a [Template]/[Template<T>] carrier illegally pins a target of its
        // own — a nested template inherits its parent carrier's target and must be authored as a bare [Template]
        // sub-template. Report SY1034 and produce no factory: the parent leaves this child a trimmed foreign child
        // (never built as a sub-template, since CreateSubTemplate only adopts the bare form), so no sibling document
        // is emitted and the error stands alone against the child.
        if (NestedTemplateCheck.IsNestedInTemplateCarrier(semanticModel, target))
        {
            var location = attributes.FirstOrDefault()?.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation();
            var nestedDiagnostics = new List<DiagnosticInfo> { TemplateDiagnostics.NestedTemplateMustNotPinTarget(location) };
            return new GenerationResult(null, null, new EquatableArray<DiagnosticInfo>(nestedDiagnostics.ToImmutableArray()));
        }

        var assemblyName = semanticModel.Compilation.AssemblyName;
        if (assemblyName is null)
            return null;

        var diagnostics = new List<DiagnosticInfo>();

        string? fileName = null;
        string? source = null;

        try
        {
            var templateInfo = TemplateInfo.Create(semanticModel, target, attributes, cancellationToken);
            if (templateInfo is null)
                return null;

            if (TemplateValidator.Validate(diagnostics, assemblyName, templateInfo))
            {
                if (highlightsOnly)
                {
                    // Highlight mode: run the same classification+emit pass to collect the SY1035 set, then discard
                    // the factory. Skip TemplateDocumentBuilder (document assembly/serialization) entirely; Build's
                    // highlightsOnly early-return fires after every SY1035 tap has merged into `diagnostics`.
                    TemplateFactoryBuilder.Build(diagnostics, templateInfo.SemanticModel, new UsingDirectiveSet(Enumerable.Empty<UsingDirectiveSyntax>()), templateInfo, templateInfo.Options, highlightsOnly: true);
                }
                else if (TemplateDocumentBuilder.Build(
                    diagnostics,
                    templateInfo,
                    additionalUsings => TemplateFactoryBuilder.Build(diagnostics, templateInfo.SemanticModel, additionalUsings, templateInfo, templateInfo.Options)) is { } generated)
                {
                    fileName = generated.FileName;
                    source = generated.Source;
                }
            }
        }
#pragma warning disable CA1031 // we're explicitly catching _any_ exception and converting it to a diagnostic message
        catch (Exception ex)
#pragma warning restore CA1031
        {
            diagnostics.Add(Incremental.Diagnostics.InternalError(ex));
        }

        return new GenerationResult(fileName, source, new EquatableArray<DiagnosticInfo>(diagnostics.ToImmutableArray()));
    }

}

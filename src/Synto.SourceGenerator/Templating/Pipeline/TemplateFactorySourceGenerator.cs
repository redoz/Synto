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

namespace Synto.Generators.Templating;


[Generator(LanguageNames.CSharp)]
public class TemplateFactorySourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // The whole template is processed inside the transform so the value flowing through the pipeline is
        // an equatable TemplateGenerationResult (just generated text + diagnostic data). This keeps the
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

        context.RegisterSourceOutput(results, static (context, result) => Emit(context, result!.Value));

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

        context.RegisterSourceOutput(genericResults, static (context, result) => Emit(context, result!.Value));
    }

    private static TemplateGenerationResult? GenerateBareTemplate(GeneratorAttributeSyntaxContext syntaxContext)
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

        return new TemplateGenerationResult(null, null, new EquatableArray<DiagnosticInfo>(diagnostics.ToImmutableArray()));
    }

    private static TemplateGenerationResult? GenerateTemplate(GeneratorAttributeSyntaxContext syntaxContext)
    {
        var assemblyName = syntaxContext.SemanticModel.Compilation.AssemblyName;
        if (assemblyName is null)
            return null;

        var diagnostics = new List<DiagnosticInfo>();

        string? fileName = null;
        string? source = null;

        try
        {
            var templateInfo = TemplateInfo.Create(syntaxContext);
            if (templateInfo is null)
                return null;

            if (TemplateValidator.Validate(diagnostics, assemblyName, templateInfo)
                && TemplateDocumentBuilder.Build(
                    diagnostics,
                    templateInfo,
                    additionalUsings => TemplateFactoryBuilder.Build(diagnostics, templateInfo.SemanticModel, additionalUsings, templateInfo, templateInfo.Options)) is { } generated)
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
        {
            try
            {
                context.AddSource(result.FileName, SourceText.From(result.Source, Encoding.UTF8));
            }
#pragma warning disable CA1031 // degrade an AddSource failure (e.g. a duplicate/invalid hint) to a diagnostic instead of crashing the generator
            catch (ArgumentException ex)
#pragma warning restore CA1031
            {
                context.ReportDiagnostic(Diagnostics.InternalError(ex).ToDiagnostic());
            }
        }
    }

}

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
            context.AddSource(result.FileName, SourceText.From(result.Source, Encoding.UTF8));
    }

}

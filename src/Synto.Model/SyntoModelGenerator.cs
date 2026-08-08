using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Synto.Generators.Model;

/// <summary>
/// Discovers <c>[SyntoModel]</c> spec classes (expression-bodied getters projecting over an
/// <see cref="ISymbol"/>) and emits a sibling cache-safe <c>record</c> plus a <c>From(TSymbol)</c> factory.
/// Mirrors <c>MatchFactorySourceGenerator</c>: all semantic work runs inside the
/// <c>ForAttributeWithMetadataName</c> transform, which flows out only the equatable shared
/// <see cref="GenerationResult"/> (generated text + diagnostic data) — never the <see cref="SemanticModel"/>,
/// symbols or syntax nodes — so the pipeline stays cacheable.
/// </summary>
/// <remarks>
/// The transform is a STUB at this task: it validates that a target is a <c>[SyntoModel]</c> spec and returns
/// an empty (null-Source) result with no diagnostics. Record-name derivation, field extraction, the
/// <c>From</c> factory, <c>Project.Each/One</c> rewriting and the cache-safety diagnostics land in later tasks.
/// </remarks>
[Generator(LanguageNames.CSharp)]
public class SyntoModelGenerator : IIncrementalGenerator
{
    private static readonly string SyntoModelAttributeMetadataName = typeof(global::Synto.Model.SyntoModelAttribute).FullName!;

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var results = context.SyntaxProvider.ForAttributeWithMetadataName(
                SyntoModelAttributeMetadataName,
                static (node, cancellationToken) => true,
                static (syntaxContext, cancellationToken) => GenerateModel(syntaxContext))
            .WithTrackingName(ModelTrackingNames.Transform)
            .Where(static result => result is not null)
            .WithTrackingName(ModelTrackingNames.Result);

        context.RegisterSourceOutput(results, static (context, result) => context.Emit(result!.Value));
    }

    private static GenerationResult? GenerateModel(GeneratorAttributeSyntaxContext syntaxContext)
    {
        var diagnostics = new List<DiagnosticInfo>();

        string? fileName = null;
        string? source = null;

        try
        {
            // Build the transform-local ModelInfo (record-name derivation; SY1039 on a name that cannot be
            // derived), then validate field cache-safety (SY1036/SY1037), dropping offending fields and continuing.
            // A fully-invalid spec (no surviving field) reports every violation but emits no record. Only the
            // equatable GenerationResult flows out — no ModelInfo/symbol/syntax is ever captured.
            if (ModelInfo.Create(syntaxContext, diagnostics) is { } info)
            {
                var validatedFields = ModelValidator.Validate(diagnostics, info);
                if (validatedFields.Count > 0)
                {
                    var generated = ModelFactoryBuilder.Build(info.WithFields(validatedFields));
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

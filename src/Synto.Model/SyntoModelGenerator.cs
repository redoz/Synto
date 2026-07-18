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
            // Task 5+ builds ModelInfo, validates cache-safety (Task 8), and emits the record + From factory
            // (Tasks 5-7). The stub reports nothing for a well-formed spec — the pipeline shape (equatable
            // GenerationResult, no rooted Roslyn objects) is what this task pins.
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

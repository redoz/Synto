using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Synto;

/// <summary>
/// Compiles each <c>[Match&lt;TMatcher&gt;]</c> pattern method into a bespoke Roslyn matcher. Mirrors
/// <see cref="TemplateFactorySourceGenerator"/>: all semantic work runs inside the
/// <c>ForAttributeWithMetadataName</c> transform, which flows out only an equatable
/// <see cref="MatchGenerationResult"/> (generated text + diagnostic data) — never the
/// <see cref="SemanticModel"/>, symbols or syntax nodes — so the pipeline stays cacheable.
/// </summary>
[Generator(LanguageNames.CSharp)]
public class MatchFactorySourceGenerator : IIncrementalGenerator
{
    // A generic attribute's metadata name carries its arity: "Synto.Matching.MatchAttribute`1". The bare
    // name matches nothing on the Roslyn 5.0 floor (spike-verified), so key on the FullName of the open
    // generic, which carries the required `1 suffix.
    private static readonly string MatchAttributeMetadataName = typeof(global::Synto.Matching.MatchAttribute<>).FullName!;

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var results = context.SyntaxProvider.ForAttributeWithMetadataName(
                MatchAttributeMetadataName,
                static (node, cancellationToken) => true,
                static (syntaxContext, cancellationToken) => GenerateMatcher(syntaxContext))
            .WithTrackingName(MatchTrackingNames.Transform)
            .Where(static result => result is not null)
            .WithTrackingName(MatchTrackingNames.Result);

        context.RegisterSourceOutput(results, static (context, result) => Emit(context, result!.Value));
    }

    private static MatchGenerationResult? GenerateMatcher(GeneratorAttributeSyntaxContext syntaxContext)
    {
        var matchInfo = MatchInfo.Create(syntaxContext);
        if (matchInfo is null)
            return null;

        var assemblyName = syntaxContext.SemanticModel.Compilation.AssemblyName;
        if (assemblyName is null)
            return null;

        var diagnostics = new List<DiagnosticInfo>();

        string? fileName = null;
        string? source = null;

        try
        {
            if (ValidateTarget(diagnostics, assemblyName, matchInfo)
                && MatchEmitter.Emit(diagnostics, matchInfo) is { } generated)
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

        return new MatchGenerationResult(fileName, source, new EquatableArray<DiagnosticInfo>(diagnostics.ToImmutableArray()));
    }

    private static void Emit(SourceProductionContext context, MatchGenerationResult result)
    {
        foreach (var diagnostic in result.Diagnostics)
            context.ReportDiagnostic(diagnostic.ToDiagnostic());

        if (result.FileName is not null && result.Source is not null)
            context.AddSource(result.FileName, SourceText.From(result.Source, Encoding.UTF8));
    }

    /// <summary>
    /// The four-arm matcher-target validation (C5). Matching has only the attribute <see cref="Location"/>
    /// (not a <c>typeof</c> argument), so each arm reuses Templating's descriptors via the
    /// <see cref="LocationInfo"/>-based <see cref="Diagnostics"/> overloads. Order is load-bearing — each
    /// reachable misuse routes to its precise descriptor, never a malformed-partial cascade:
    /// (1) not declared in source => SY1003, (2) not a (non-record) class => SY1002,
    /// (3) target not partial => SY1001, (4) any ancestor class not partial => SY1004.
    /// </summary>
    private static bool ValidateTarget(List<DiagnosticInfo> diagnostics, string assemblyName, MatchInfo info)
    {
        var location = LocationInfo.CreateFrom(info.AttributeSyntax.GetLocation());
        var targetName = info.TargetFullName;

        if (info.Target.DeclaringSyntaxReferences.FirstOrDefault() is not { } syntaxRef)
        {
            diagnostics.Add(Diagnostics.TargetNotDeclaredInSource(location, targetName, assemblyName));
            return false;
        }

        if (syntaxRef.GetSyntax() is not ClassDeclarationSyntax classSyntax)
        {
            diagnostics.Add(Diagnostics.TargetNotClass(location, targetName));
            return false;
        }

        if (!classSyntax.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            diagnostics.Add(Diagnostics.TargetNotPartial(location, targetName));
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
                    diagnostics.Add(Diagnostics.TargetAncestorNotPartial(location, targetName, parentClass.Identifier.Text));
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
}

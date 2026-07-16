using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Synto.Templating;
using Synto.Generators.Templating.Emit;

namespace Synto.Generators.Templating.Diagnostics;

/// <summary>
/// <b>WORKAROUND analyzer.</b> Paints the existing <c>SY1035</c> "live template code" diagnostic inline in the
/// editor by re-running the generator's whole classification+emit pass
/// (<see cref="TemplateFactorySourceGenerator.TryGenerate"/> with <c>highlightsOnly: true</c>) per
/// <c>[Template&lt;T&gt;]</c> carrier and reporting the <c>SY1035</c> diagnostics it collects, then discarding the
/// throwaway factory. This is deliberate interim debt: the proper fix is the front-end <c>Classify</c> extraction
/// (Option C), a hard 1.0 gate — see
/// <c>docs/superpowers/specs/2026-07-09-live-template-highlight-analyzer-design.md §"Known debt / 1.0 gate"</c>.
/// The whole per-declaration body is wrapped in try/catch so a staging fault never crashes the editor (report
/// nothing on throw), mirroring the generator's diagnostics-not-exceptions contract.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LiveTemplateHighlightAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(TemplateDiagnostics.LiveFragmentDescriptor);

    public override void Initialize(AnalysisContext context)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));

        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        // The three [Template] attribute targets (Method | Class | Struct) plus local functions, which are valid
        // Method-target carriers but are not surfaced by symbol actions (they live only in the semantic model).
        context.RegisterSyntaxNodeAction(
            AnalyzeDeclaration,
            SyntaxKind.MethodDeclaration,
            SyntaxKind.ClassDeclaration,
            SyntaxKind.StructDeclaration,
            SyntaxKind.LocalFunctionStatement);
    }

    private static void AnalyzeDeclaration(SyntaxNodeAnalysisContext context)
    {
        try
        {
            var node = context.Node;

            // Cheap syntactic gate FIRST — before any semantic-model work: bail unless the declaration carries an
            // attribute whose simple name is Template / TemplateAttribute (covers the generic Template<...>, whose
            // name node is a GenericNameSyntax).
            if (!HasTemplateNamedAttribute(node))
                return;

            var cancellationToken = context.CancellationToken;
            var semanticModel = context.SemanticModel;

            if (semanticModel.GetDeclaredSymbol(node, cancellationToken) is not { } symbol)
                return;

            // Confirm Synto's generic [Template<T>] by metadata identity: only that form derives a factory target
            // and therefore produces SY1035 highlights (the bare [Template] never does).
            var genericTemplateAttribute = semanticModel.Compilation.GetTypeByMetadataName(typeof(TemplateAttribute<>).FullName!);
            if (genericTemplateAttribute is null)
                return;

            var templateAttributes = symbol.GetAttributes()
                .Where(a => a.AttributeClass is { IsGenericType: true } attributeClass
                            && SymbolEqualityComparer.Default.Equals(attributeClass.OriginalDefinition, genericTemplateAttribute))
                .ToImmutableArray();

            if (templateAttributes.IsEmpty)
                return;

            // Reuse the generator's shared core in highlight mode: it runs the same nested-carrier + validation
            // gates and the same classification+emit pass, then discards the factory.
            var result = TemplateFactorySourceGenerator.TryGenerate(semanticModel, node, templateAttributes, highlightsOnly: true, cancellationToken);
            if (result is null)
                return;

            var liveFragmentId = TemplateDiagnostics.LiveFragmentDescriptor.Id;
            foreach (var info in result.Value.Diagnostics)
            {
                // Only paint SY1035 highlights; errors (e.g. SY1034) are the generator's job, not the analyzer's.
                if (info.Descriptor.Id == liveFragmentId)
                    context.ReportDiagnostic(info.ToDiagnostic());
            }
        }
#pragma warning disable CA1031 // never crash the editor: a staging fault degrades to zero highlights
        catch (Exception)
#pragma warning restore CA1031
        {
            // Report nothing on throw — mirrors the generator's diagnostics-not-exceptions contract.
        }
    }

    private static bool HasTemplateNamedAttribute(SyntaxNode node)
    {
        var attributeLists = node switch
        {
            MemberDeclarationSyntax member => member.AttributeLists,
            LocalFunctionStatementSyntax localFunction => localFunction.AttributeLists,
            _ => default,
        };

        foreach (var list in attributeLists)
        {
            foreach (var attribute in list.Attributes)
            {
                var name = attribute.Name;

                // Unwrap a qualified name (e.g. Synto.Templating.Template) down to its right-most segment.
                while (name is QualifiedNameSyntax qualified)
                    name = qualified.Right;

                var simpleName = name switch
                {
                    GenericNameSyntax generic => generic.Identifier.Text,
                    SimpleNameSyntax simple => simple.Identifier.Text,
                    _ => null,
                };

                if (simpleName is "Template" or "TemplateAttribute")
                    return true;
            }
        }

        return false;
    }
}

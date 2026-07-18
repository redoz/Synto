using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto.Generators.Model;

/// <summary>
/// Transform-local description of a single <c>[SyntoModel]</c> spec class: its resolved record name, the
/// source-symbol type each getter projects over, and the ordered list of projected fields (declared type +
/// name + getter RHS). It holds syntax/symbols ONLY inside the <c>ForAttributeWithMetadataName</c> transform
/// and is NEVER captured into pipeline state — only the equatable <see cref="GenerationResult"/> flows out.
/// Mirrors <c>MatchInfo</c>.
/// </summary>
internal sealed class ModelInfo
{
    private const string ProjectTypeName = "Synto.Model.Project";
    private const string SyntoModelAttributeName = "Synto.Model.SyntoModelAttribute";

    // The source symbol type on the From parameter is fully qualified (global:: + namespaces + type params)
    // so the emitted From factory binds regardless of the consumer's usings.
    private static readonly SymbolDisplayFormat SymbolTypeFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);

    private ModelInfo(
        SemanticModel semanticModel,
        AttributeSyntax attributeSyntax,
        INamedTypeSymbol specSymbol,
        string recordName,
        string symbolTypeDisplay,
        string? containingNamespace,
        IReadOnlyList<ProjectedField> fields)
    {
        SemanticModel = semanticModel;
        AttributeSyntax = attributeSyntax;
        SpecSymbol = specSymbol;
        RecordName = recordName;
        SymbolTypeDisplay = symbolTypeDisplay;
        ContainingNamespace = containingNamespace;
        Fields = fields;
    }

    public SemanticModel SemanticModel { get; }
    public AttributeSyntax AttributeSyntax { get; }
    public INamedTypeSymbol SpecSymbol { get; }

    /// <summary>The generated record's name (spec name minus trailing <c>Shape</c>, or the attribute override).</summary>
    public string RecordName { get; }

    /// <summary>The fully-qualified source-symbol type each getter projects over (the <c>From</c> parameter type).</summary>
    public string SymbolTypeDisplay { get; }

    /// <summary>The spec class's namespace (null for the global namespace); the record is emitted as a sibling.</summary>
    public string? ContainingNamespace { get; }

    /// <summary>The projected fields in source order (instance expression-bodied getters only).</summary>
    public IReadOnlyList<ProjectedField> Fields { get; }

    /// <summary>
    /// Builds a <see cref="ModelInfo"/> from a <c>[SyntoModel]</c> target. Returns <see langword="null"/> (adding
    /// <c>SY1039</c> to <paramref name="diagnostics"/>) when no record name can be derived — the spec name lacks a
    /// trailing <c>Shape</c> and no <c>[SyntoModel(Name=…)]</c> override was given.
    /// </summary>
    public static ModelInfo? Create(GeneratorAttributeSyntaxContext syntaxContext, List<DiagnosticInfo> diagnostics)
    {
        var attr = syntaxContext.Attributes.FirstOrDefault();
        if (attr is null)
            return null;

        if (attr.ApplicationSyntaxReference?.GetSyntax() is not AttributeSyntax attrSyntax)
            return null;

        if (syntaxContext.TargetSymbol is not INamedTypeSymbol specSymbol)
            return null;

        if (syntaxContext.TargetNode is not ClassDeclarationSyntax classDeclaration)
            return null;

        var recordName = ResolveRecordName(attr, specSymbol.Name);
        if (recordName is null)
        {
            diagnostics.Add(ModelDiagnostics.NameSuffixMissing(attrSyntax.GetLocation(), specSymbol.Name));
            return null;
        }

        // The source-symbol type is the SyntoModel<T> base's single type argument. Fall back to ISymbol if the
        // spec's base is not the expected SyntoModel<T> (a malformed spec still type-checks its getters).
        var symbolTypeDisplay = specSymbol.BaseType is { Name: "SyntoModel", TypeArguments.Length: 1 } baseType
            ? baseType.TypeArguments[0].ToDisplayString(SymbolTypeFormat)
            : "global::Microsoft.CodeAnalysis.ISymbol";

        var fields = new List<ProjectedField>();
        foreach (var property in classDeclaration.Members.OfType<PropertyDeclarationSyntax>())
        {
            // Instance expression-bodied getters only: `T Name => <rhs>;`. Static members and block-bodied /
            // field / method members are not projections and are ignored.
            if (property.ExpressionBody is not { } arrow)
                continue;
            if (property.Modifiers.Any(SyntaxKind.StaticKeyword))
                continue;

            // A getter whose RHS binds a `Project.Each/One<TSpec>(…)` shim is a nested-model projection: its
            // record field type is synthesized from the nested record name (Task 7) rather than copied from the
            // getter's spec-collection type. A non-[SyntoModel] type argument reports SY1038 and the field is
            // dropped (accumulate-and-continue).
            switch (ResolveProjection(syntaxContext.SemanticModel, arrow.Expression, diagnostics,
                out var kind, out var nestedRecordName, out var projectArgument))
            {
                case ProjectionResult.Dropped:
                    continue;
                case ProjectionResult.Resolved:
                    fields.Add(new ProjectedField(property.Type, property.Identifier.Text, arrow.Expression, kind, nestedRecordName, projectArgument));
                    break;
                default:
                    fields.Add(new ProjectedField(property.Type, property.Identifier.Text, arrow.Expression));
                    break;
            }
        }

        var containingNamespace = specSymbol.ContainingNamespace is { IsGlobalNamespace: false } ns
            ? ns.ToDisplayString()
            : null;

        return new ModelInfo(
            syntaxContext.SemanticModel,
            attrSyntax,
            specSymbol,
            recordName,
            symbolTypeDisplay,
            containingNamespace,
            fields);
    }

    private static string? ResolveRecordName(AttributeData attr, string specName)
    {
        foreach (var named in attr.NamedArguments)
        {
            if (named.Key == "Name" && named.Value.Value is string overrideName && !string.IsNullOrEmpty(overrideName))
                return overrideName;
        }

        const string suffix = "Shape";
        if (specName.EndsWith(suffix, StringComparison.Ordinal) && specName.Length > suffix.Length)
            return specName.Substring(0, specName.Length - suffix.Length);

        return null;
    }

    /// <summary>
    /// Classifies a getter RHS as a nested-model projection. Recognizes <c>Project.Each/One&lt;TSpec&gt;(arg)</c>
    /// by the bound method symbol (against the injected <c>Synto.Model.Project</c> shim). When the type argument
    /// is a <c>[SyntoModel]</c> spec class in the same compilation, resolves its record name and yields the shim
    /// argument; when it is not, reports <c>SY1038</c> and returns <see cref="ProjectionResult.Dropped"/> so the
    /// field is dropped. Anything else is <see cref="ProjectionResult.NotAProjection"/>.
    /// </summary>
    private static ProjectionResult ResolveProjection(
        SemanticModel semanticModel,
        ExpressionSyntax rhs,
        List<DiagnosticInfo> diagnostics,
        out ProjectionKind kind,
        out string? nestedRecordName,
        out ExpressionSyntax? projectArgument)
    {
        kind = ProjectionKind.None;
        nestedRecordName = null;
        projectArgument = null;

        if (rhs is not InvocationExpressionSyntax invocation)
            return ProjectionResult.NotAProjection;
        if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
            return ProjectionResult.NotAProjection;
        if (method.ContainingType?.ToDisplayString() != ProjectTypeName)
            return ProjectionResult.NotAProjection;

        var projectionKind = method.Name switch
        {
            "Each" => ProjectionKind.Each,
            "One" => ProjectionKind.One,
            _ => ProjectionKind.None,
        };
        if (projectionKind == ProjectionKind.None)
            return ProjectionResult.NotAProjection;
        if (method.TypeArguments.Length != 1 || invocation.ArgumentList.Arguments.Count != 1)
            return ProjectionResult.NotAProjection;

        var typeArgument = method.TypeArguments[0];
        projectArgument = invocation.ArgumentList.Arguments[0].Expression;

        var specAttr = typeArgument is INamedTypeSymbol named
            ? named.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == SyntoModelAttributeName)
            : null;
        if (specAttr is null)
        {
            diagnostics.Add(ModelDiagnostics.ProjectElementNotSyntoModel(invocation.GetLocation(), typeArgument.ToDisplayString()));
            return ProjectionResult.Dropped;
        }

        // The nested spec derives its own record name (Shape-strip / Name override). If it cannot, the nested
        // spec reports its own SY1039 during its independent generation; drop the reference here rather than
        // emit an unresolved projection.
        var resolved = ResolveRecordName(specAttr, typeArgument.Name);
        if (resolved is null)
            return ProjectionResult.Dropped;

        kind = projectionKind;
        nestedRecordName = resolved;
        return ProjectionResult.Resolved;
    }
}

/// <summary>The nested-projection classification of a getter RHS (<c>Project.Each/One</c> vs. a plain getter).</summary>
internal enum ProjectionKind
{
    /// <summary>A plain projection: the field type is copied verbatim from the getter's declared type.</summary>
    None,

    /// <summary><c>Project.Each&lt;TSpec&gt;(seq)</c> — the field becomes <c>EquatableArray&lt;Record&gt;</c>.</summary>
    Each,

    /// <summary><c>Project.One&lt;TSpec&gt;(sym)</c> — the field becomes a nullable <c>Record?</c>.</summary>
    One,
}

/// <summary>The outcome of classifying a getter RHS as a possible nested-model projection.</summary>
internal enum ProjectionResult
{
    /// <summary>Not a <c>Project.Each/One</c> shim call — treat as a plain getter.</summary>
    NotAProjection,

    /// <summary>A valid nested projection into a <c>[SyntoModel]</c> record.</summary>
    Resolved,

    /// <summary>A <c>Project</c> shim call whose element type is invalid (SY1038) — drop the field.</summary>
    Dropped,
}

/// <summary>
/// A single projected field of a <c>[SyntoModel]</c> spec: the getter's declared type, its name, and the
/// getter RHS expression (lifted into the emitted <c>From</c> factory; the <c>Symbol</c>→parameter rebind
/// lands in Task 6). Transform-local — never flows through the pipeline.
/// </summary>
internal sealed class ProjectedField
{
    public ProjectedField(
        TypeSyntax declaredType,
        string name,
        ExpressionSyntax rhs,
        ProjectionKind kind = ProjectionKind.None,
        string? nestedRecordName = null,
        ExpressionSyntax? projectArgument = null)
    {
        DeclaredType = declaredType;
        Name = name;
        Rhs = rhs;
        Kind = kind;
        NestedRecordName = nestedRecordName;
        ProjectArgument = projectArgument;
    }

    public TypeSyntax DeclaredType { get; }
    public string Name { get; }
    public ExpressionSyntax Rhs { get; }

    /// <summary>Whether this field is a <c>Project.Each/One</c> nested-model projection (<see cref="ProjectionKind.None"/> for a plain getter).</summary>
    public ProjectionKind Kind { get; }

    /// <summary>For a <see cref="ProjectionKind.Each"/>/<see cref="ProjectionKind.One"/> field, the derived nested record name.</summary>
    public string? NestedRecordName { get; }

    /// <summary>For a nested-projection field, the single (Symbol-bound) argument expression of the <c>Project</c> shim call.</summary>
    public ExpressionSyntax? ProjectArgument { get; }
}

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Synto.Generators.Templating;

/// <summary>
/// Synthesizes the inert carrier-callable <em>facade</em> for each user <c>[SyntaxBuilder]</c> method (plan
/// Task 3 / Locked Names §5) and emits it as <c>internal</c> source into the consumer compilation, so a
/// <c>[Template]</c> body can type-check a call to the builder by its method name. The facade is derived
/// mechanically by <see cref="FacadeShape"/>; the builder method's parameter annotations decide the facade's
/// generic parameters, value parameters and return type. The facade body is inert (<c>=&gt; default!</c>) — at
/// factory-build time <see cref="TemplateFactorySourceGenerator"/> rewrites the recognized facade call to a
/// fully-qualified static call of the builder. This is a SEPARATE generator (like
/// <see cref="SurfaceInjectionGenerator"/>) so the in-memory snapshot tests that instantiate
/// <see cref="TemplateFactorySourceGenerator"/> directly stay stable.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class SyntaxBuilderFacadeGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var facades = context.SyntaxProvider.ForAttributeWithMetadataName(
                typeof(global::Synto.Templating.SyntaxBuilderAttribute).FullName!,
                static (node, _) => node is MethodDeclarationSyntax,
                static (ctx, _) => Build(ctx))
            .Where(static facade => facade is not null);

        context.RegisterSourceOutput(facades, static (spc, facade) => Emit(spc, facade!.Value));
    }

    private static void Emit(SourceProductionContext context, (string HintName, string Source) facade)
    {
        try
        {
            context.AddSource(facade.HintName, SourceText.From(facade.Source, Encoding.UTF8));
        }
#pragma warning disable CA1031 // degrade an AddSource failure (e.g. a duplicate/invalid hint) to a diagnostic instead of crashing the generator
        catch (ArgumentException ex)
#pragma warning restore CA1031
        {
            context.ReportDiagnostic(Synto.Generators.Diagnostics.InternalError(ex).ToDiagnostic());
        }
    }

    private static (string HintName, string Source)? Build(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not IMethodSymbol builder)
            return null;

        var shape = FacadeShape.Derive(builder, out var annotationError, out var returnShapeError);
        if (shape is null || annotationError is not null || returnShapeError is not null)
            return null; // an invalid builder produces no facade; TemplateFactorySourceGenerator reports the diagnostic.

        if (FacadeMatchesBuilder(builder, shape))
            return null; // an all-staged, concrete-node-returning builder IS its own facade; synthesizing one
                         // would collide with the real (injected) method (CS0111). The template binds directly to
                         // the real builder and TemplateFactorySourceGenerator still rewrites the recognized call.

        var sb = new StringBuilder();
        sb.Append("#nullable enable\n");

        string? ns = builder.ContainingType?.ContainingNamespace is { IsGlobalNamespace: false } n
            ? n.ToDisplayString()
            : null;

        if (ns is not null)
            sb.Append("namespace ").Append(ns).Append("\n{\n");

        // Containing type chain (outer to inner), each reopened as partial so the facade is added alongside the builder.
        var chain = new List<INamedTypeSymbol>();
        for (var t = builder.ContainingType; t is not null; t = t.ContainingType)
            chain.Add(t);
        chain.Reverse();

        foreach (var t in chain)
        {
            sb.Append("partial ").Append(TypeKeyword(t)).Append(' ').Append(t.Name);
            AppendTypeParameters(sb, t.TypeParameters);
            sb.Append(" {\n");
        }

        AppendFacadeMethod(sb, shape);

        for (int i = 0; i < chain.Count; i++)
            sb.Append("}\n");

        if (ns is not null)
            sb.Append("}\n");

        // Include the parameter types so overloaded builders (e.g. the two `Switch` shapes, which differ only by
        // list element type) get DISTINCT hint names — a method's fully-qualified display omits its parameter list.
        var hintBuilder = new StringBuilder(builder.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        foreach (var p in builder.Parameters)
            hintBuilder.Append('_').Append(p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

        string hint = hintBuilder.ToString()
            .Replace("global::", "")
            .SanitizeHintName()
            + ".SyntaxBuilderFacade.g.cs";

        return (hint, sb.ToString());
    }

    /// <summary>
    /// True when the derived facade would be signature-identical to the real builder — an all-staged builder that
    /// returns a concrete syntax node (no <c>[Quoted]</c>/<c>[TypeArg]</c> island reshaping a parameter, no fresh
    /// <c>TResult</c>). Such a builder needs no synthesized facade: the real injected method is directly callable
    /// from the template, and a synthesized twin would be a duplicate member (CS0111).
    /// </summary>
    private static bool FacadeMatchesBuilder(IMethodSymbol builder, FacadeShape shape)
    {
        if (shape.GenericParameters.Count > 0 || shape.FreshReturnTypeParam)
            return false;
        if (shape.ReturnTypeDisplay != builder.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            return false;
        foreach (var p in shape.Parameters)
            if (p.Kind != BuilderArgKind.Staged)
                return false;
        return true;
    }

    private static void AppendFacadeMethod(StringBuilder sb, FacadeShape shape)
    {
        sb.Append("internal static ").Append(shape.ReturnTypeDisplay).Append(' ').Append(shape.MethodName);

        if (shape.GenericParameters.Count > 0)
        {
            sb.Append('<');
            for (int i = 0; i < shape.GenericParameters.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(shape.GenericParameters[i].Name);
            }
            sb.Append('>');
        }

        sb.Append('(');
        bool first = true;
        foreach (var p in shape.Parameters)
        {
            if (p.Kind == BuilderArgKind.QuotedTypeArg)
                continue;
            if (!first) sb.Append(", ");
            first = false;
            if (p.IsParams) sb.Append("params ");
            sb.Append(p.ValueTypeDisplay).Append(' ').Append(p.ParameterName);
        }
        sb.Append(')');

        foreach (var gp in shape.GenericParameters)
        {
            if (gp.ConstraintDisplay is not null)
                sb.Append(" where ").Append(gp.Name).Append(" : ").Append(gp.ConstraintDisplay);
        }

        sb.Append(" => default!;\n");
    }

    private static void AppendTypeParameters(StringBuilder sb, System.Collections.Immutable.ImmutableArray<ITypeParameterSymbol> typeParameters)
    {
        if (typeParameters.Length == 0)
            return;

        sb.Append('<');
        for (int i = 0; i < typeParameters.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(typeParameters[i].Name);
        }
        sb.Append('>');
    }

    private static string TypeKeyword(INamedTypeSymbol type) => type.TypeKind switch
    {
        TypeKind.Struct => type.IsRecord ? "record struct" : "struct",
        _ => type.IsRecord ? "record" : "class",
    };
}

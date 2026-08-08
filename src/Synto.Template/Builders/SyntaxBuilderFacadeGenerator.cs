using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Synto.Generators.Symbols;
using Synto.Generators.Templating.Diagnostics;

namespace Synto.Generators.Templating.Builders;

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
            .WithTrackingName(BuilderTrackingNames.Transform)
            .Where(static facade => facade.HintName is not null || facade.Diagnostics.Count > 0)
            .WithTrackingName(BuilderTrackingNames.Result);

        context.RegisterSourceOutput(facades, static (spc, facade) => Emit(spc, facade));
    }

    private static void Emit(SourceProductionContext context, FacadeGenerationResult facade)
    {
        foreach (var diagnostic in facade.Diagnostics)
            context.ReportDiagnostic(diagnostic.ToDiagnostic());

        if (facade.HintName is null || facade.Source is null)
            return;

        try
        {
            context.AddSource(facade.HintName, SourceText.From(facade.Source, Encoding.UTF8));
        }
#pragma warning disable CA1031 // degrade an AddSource failure (e.g. a duplicate/invalid hint) to a diagnostic instead of crashing the generator
        catch (ArgumentException ex)
#pragma warning restore CA1031
        {
            context.ReportDiagnostic(Synto.Generators.Incremental.Diagnostics.InternalError(ex).ToDiagnostic());
        }
    }

    private static FacadeGenerationResult Build(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not IMethodSymbol builder)
            return default;

        // Declaration-site misuse (private/inaccessible builder, non-partial or interface container) would surface
        // only as a cryptic CS error inside the generated facade file — report it as a located SY1031 instead and
        // emit no facade. Located at the builder method's identifier.
        Location? declLocation = (ctx.TargetNode as MethodDeclarationSyntax)?.Identifier.GetLocation();
        if (ValidateDeclaration(builder) is { } declError)
            return new FacadeGenerationResult(null, null, new EquatableArray<DiagnosticInfo>(
                ImmutableArray.Create(TemplateDiagnostics.InvalidBuilderDeclaration(declLocation, builder.Name, declError))));

        var shape = FacadeShape.Derive(builder, out var annotationError, out var returnShapeError);
        if (shape is null || annotationError is not null || returnShapeError is not null)
            return default; // an invalid builder produces no facade; TemplateFactorySourceGenerator reports the diagnostic at the call site.

        if (FacadeMatchesBuilder(builder, shape))
            return default; // an all-staged, concrete-node-returning builder IS its own facade; synthesizing one
                            // would collide with the real (injected) method (CS0111). The template binds directly to
                            // the real builder and TemplateFactorySourceGenerator still rewrites the recognized call.

        var sb = new StringBuilder();
        sb.Append("#nullable enable\n");

        // Emission host: the builder's own containing type, unless [SyntaxBuilder(Facade = ...)] names a
        // different type — required whenever Returns is set (a same-signature facade in the builder's own type
        // would be CS0111). Resolved from the SYMBOL (not just the display string) so its namespace and
        // containing-type chain can be walked, exactly like the builder's own chain below.
        var builderAttrForHost = SymbolMetadataExtensions.FindAttribute(
            builder, typeof(global::Synto.Templating.SyntaxBuilderAttribute).FullName!);
        INamedTypeSymbol host = builderAttrForHost is not null
            && SymbolMetadataExtensions.GetNamedType(builderAttrForHost, "Facade") is { } facadeHost
            ? facadeHost
            : builder.ContainingType;

        string? ns = host.ContainingNamespace is { IsGlobalNamespace: false } n
            ? n.ToDisplayString()
            : null;

        if (ns is not null)
            sb.Append("namespace ").Append(ns).Append("\n{\n");

        // Containing type chain (outer to inner), each reopened as partial so the facade is added alongside the host.
        var chain = new List<INamedTypeSymbol>();
        for (var t = host; t is not null; t = t.ContainingType)
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

        return new FacadeGenerationResult(hint, sb.ToString(), EquatableArray<DiagnosticInfo>.Empty);
    }

    /// <summary>
    /// Validates the builder <em>declaration</em> can host a synthesized facade: the builder must be callable
    /// (public/internal) from the consumer compilation, and every containing type must be a <c>partial</c>
    /// class/struct (not an interface) so the facade partial can be reopened alongside it. When
    /// <c>[SyntaxBuilder(Facade = ...)]</c> names a different emission host, that host's containing chain is
    /// checked the same way (the facade partial is reopened there instead). A fixed <c>Returns</c> without a
    /// <c>Facade</c> is also rejected here — a same-signature facade in the builder's own type would be CS0111.
    /// Returns a human reason for the first violation, or <c>null</c> when the declaration is well-formed. Pure
    /// symbol analysis; the syntax it touches (for the <c>partial</c> check) is not captured into pipeline state.
    /// </summary>
    private static string? ValidateDeclaration(IMethodSymbol builder)
    {
        if (builder.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
            return "a [SyntaxBuilder] method must be public or internal so its synthesized facade can call it";

        for (var t = builder.ContainingType; t is not null; t = t.ContainingType)
        {
            if (t.TypeKind == TypeKind.Interface)
                return $"containing type '{t.Name}' is an interface; only (partial) class/struct containers are supported";
            if (t.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
                return $"containing type '{t.Name}' must be public or internal";
            if (!IsPartial(t))
                return $"containing type '{t.Name}' must be declared partial";
        }

        var attr = SymbolMetadataExtensions.FindAttribute(
            builder, typeof(global::Synto.Templating.SyntaxBuilderAttribute).FullName!);
        INamedTypeSymbol? facadeHost = attr is null ? null : SymbolMetadataExtensions.GetNamedType(attr, "Facade");

        if (attr is not null
            && SymbolMetadataExtensions.GetNamedTypeDisplay(attr, "Returns") is not null
            && facadeHost is null)
        {
            return "a [SyntaxBuilder] with a fixed Returns type must also set Facade "
                + "(a same-signature facade in the builder's own type would be CS0111)";
        }

        if (facadeHost is not null)
        {
            for (var t = facadeHost; t is not null; t = t.ContainingType)
            {
                if (t.TypeKind == TypeKind.Interface)
                    return $"Facade host '{t.Name}' is an interface; only (partial) class/struct containers are supported";
                if (t.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
                    return $"Facade host '{t.Name}' must be public or internal";
                if (!IsPartial(t))
                    return $"Facade host '{t.Name}' must be declared partial";
            }
        }

        return null;
    }

    private static bool IsPartial(INamedTypeSymbol type)
    {
        foreach (var reference in type.DeclaringSyntaxReferences)
            if (reference.GetSyntax() is TypeDeclarationSyntax decl
                && decl.Modifiers.Any(static m => m.IsKind(SyntaxKind.PartialKeyword)))
                return true;
        return false;
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

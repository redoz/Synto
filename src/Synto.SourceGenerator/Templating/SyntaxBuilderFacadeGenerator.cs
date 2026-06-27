using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Synto;

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

        context.RegisterSourceOutput(facades, static (spc, facade) =>
            spc.AddSource(facade!.Value.HintName, SourceText.From(facade.Value.Source, Encoding.UTF8)));
    }

    private static (string HintName, string Source)? Build(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not IMethodSymbol builder)
            return null;

        var shape = FacadeShape.Derive(builder, out var annotationError, out var returnShapeError);
        if (shape is null || annotationError is not null || returnShapeError is not null)
            return null; // an invalid builder produces no facade; TemplateFactorySourceGenerator reports the diagnostic.

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

        string hint = builder.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", "")
            .Replace("<", "_")
            .Replace(">", "_")
            .Replace(", ", "_")
            .Replace(",", "_")
            .Replace("(", "_")
            .Replace(")", "_")
            .Replace(" ", "")
            + ".SyntaxBuilderFacade.g.cs";

        return (hint, sb.ToString());
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
                sb.Append(shape.GenericParameters[i]);
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
            sb.Append(p.ValueTypeDisplay).Append(' ').Append(p.ParameterName);
        }
        sb.Append(") => default!;\n");
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

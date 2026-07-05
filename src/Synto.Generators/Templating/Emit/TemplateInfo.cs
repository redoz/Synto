using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.Templating;

namespace Synto.Generators.Templating.Emit;

internal sealed class TemplateInfo
{
    private TemplateInfo(SemanticModel semanticModel, AttributeSyntax attributeSyntax, Source templateSource, TargetType factoryTarget, TemplateOption options)
    {
        SemanticModel = semanticModel;
        AttributeSyntax = attributeSyntax;
        Source = templateSource;
        Target = factoryTarget;
        Options = options;
    }

    public AttributeSyntax AttributeSyntax { get; private set; }
    public TargetType Target { get; private set; }
    public Source Source { get; private set; }
    public TemplateOption Options { get; private set; }
    public SemanticModel SemanticModel { get; private set; }

    public static TemplateInfo? Create(GeneratorAttributeSyntaxContext syntaxContext)
    {
        // this just sanity checks against accepting things that the compiler should've rejected in the first place

        var attr = syntaxContext.Attributes.FirstOrDefault();
        if (attr is null)
            return null;


        if (attr.ApplicationSyntaxReference is null)
            return null;

        if (attr.ApplicationSyntaxReference.GetSyntax() is not AttributeSyntax attrSyntax)
            return null;

        // Only the generic [Template<T>] form flows here (the parameterless [Template] is handled by the arity-0
        // transform, which never derives a target). The factory target is the attribute class's single type
        // argument; ConstructorArguments is empty on the generic form.
        if (attr.AttributeClass is not { IsGenericType: true, TypeArguments.Length: 1 } genericAttr)
            return null;

        if (genericAttr.TypeArguments[0] is not INamedTypeSymbol factoryType)
            return null;

        var target = new TargetType(attr, factoryType);

        var source = new Source(syntaxContext.TargetNode, syntaxContext.TargetSymbol.Name);

        return new TemplateInfo(syntaxContext.SemanticModel, attrSyntax, source, target, ReadOptions(attr));
    }

    /// <summary>
    /// Second construction path: a nested BARE <c>[Template]</c> child folded into its parent as a private
    /// sub-template. The child's factory <see cref="Target"/> is PINNED to the parent's target (same class);
    /// its <see cref="Source"/> is the child method node; its <see cref="AttributeSyntax"/> is the child's own
    /// <c>[Template]</c> so the child build trims that attribute exactly as the parent build trims its own.
    /// A nested generic <c>[Template&lt;X&gt;]</c> child is NOT a parent-driven sub-template — it still emits its
    /// own public sibling in the interim — so this returns <see langword="null"/> for it. Runs entirely inside the
    /// parent's transform (shared <see cref="SemanticModel"/>); nothing is captured into pipeline state.
    /// </summary>
    public static TemplateInfo? CreateSubTemplate(SemanticModel semanticModel, TargetType parentTarget, MethodDeclarationSyntax childMethod)
    {
        var templateAttributeSymbol = semanticModel.Compilation.GetTypeByMetadataName(typeof(TemplateAttribute).FullName!);
        if (templateAttributeSymbol is null)
            return null;

        if (semanticModel.GetDeclaredSymbol(childMethod) is not { } childSymbol)
            return null;

        foreach (var attr in childSymbol.GetAttributes())
        {
            // Only the parameterless (non-generic) [Template] is a parent-driven sub-template. A constructed
            // generic [Template<X>] is not equal to the unbound non-generic symbol, so it is skipped here.
            if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, templateAttributeSymbol))
                continue;

            if (attr.ApplicationSyntaxReference?.GetSyntax() is not AttributeSyntax attrSyntax)
                continue;

            var source = new Source(childMethod, childSymbol.Name);
            return new TemplateInfo(semanticModel, attrSyntax, source, parentTarget, ReadOptions(attr));
        }

        return null;
    }

    private static TemplateOption ReadOptions(AttributeData attr)
    {
        var options = TemplateOption.None;

        foreach (var namedArgs in attr.NamedArguments)
        {
            if (StringComparer.Ordinal.Equals(namedArgs.Key, nameof(TemplateAttribute.Options)))
            {
                // A mid-keystroke / unresolvable Options expression yields an error-kind constant (Value == null);
                // an enum constant boxes to its underlying int. Anything that is not a plain int is skipped so a
                // malformed argument defaults to None instead of NRE-ing the transform.
                if (namedArgs.Value.Kind != TypedConstantKind.Error && namedArgs.Value.Value is int optionValue)
                {
                    options = (TemplateOption)optionValue;
                }
            }
        }

        return options;
    }
}

internal record struct Source(SyntaxNode Syntax, string Identifier)
{
    public SyntaxNode Syntax { get; } = Syntax;
    public string Identifier { get; } = Identifier;
}

internal record struct TargetType(AttributeData TemplateAttributeData, ITypeSymbol Type)
{
    private static readonly SymbolDisplayFormat SymbolDisplayFormat = new(
        SymbolDisplayGlobalNamespaceStyle.Omitted,
        SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        SymbolDisplayGenericsOptions.IncludeTypeParameters
    );

    public ITypeSymbol Type { get; } = Type;

    public readonly string FullName => Type.ToDisplayString(SymbolDisplayFormat);

    public readonly Location? GetReferenceLocation()
    {
        var syntax = TemplateAttributeData.ApplicationSyntaxReference?.GetSyntax();
        if (syntax is not AttributeSyntax attrSyntax)
            return null;

        // Generic form [Template<T>]: the target lives in the attribute name's type-argument list.
        if (attrSyntax.Name is GenericNameSyntax genericName
            && genericName.TypeArgumentList.Arguments.Count > 0)
        {
            return genericName.TypeArgumentList.Arguments[0].GetLocation();
        }

        return null;
    }
}

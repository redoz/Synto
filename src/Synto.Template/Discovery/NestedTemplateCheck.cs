using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.Templating;

namespace Synto.Generators.Templating.Discovery;

/// <summary>
/// Shared ancestor check consumed by both <c>[Template]</c> FAWMN transforms: is <paramref name="node"/>
/// declared inside a TYPE that itself carries a <c>[Template]</c> / <c>[Template&lt;T&gt;]</c> carrier attribute?
/// This is what distinguishes a top-level template declaration (its own factory) from a nested (child) one.
/// Mirrors <see cref="ChildTemplateFinder"/>'s attribute-symbol match (bare matched directly, generic matched on
/// its original definition, any <c>T</c>). Nothing is captured into pipeline state — the result is a bool consumed
/// entirely inside the transform.
/// </summary>
internal static class NestedTemplateCheck
{
    public static bool IsNestedInTemplateCarrier(SemanticModel semanticModel, SyntaxNode node)
    {
        var templateAttributeSymbol = semanticModel.Compilation.GetTypeByMetadataName(typeof(TemplateAttribute).FullName!);
        var genericTemplateAttributeSymbol = semanticModel.Compilation.GetTypeByMetadataName(typeof(TemplateAttribute<>).FullName!);

        if (templateAttributeSymbol is null && genericTemplateAttributeSymbol is null)
            return false;

        foreach (var ancestor in node.Ancestors().OfType<TypeDeclarationSyntax>())
        {
            if (HasTemplateAttribute(semanticModel, ancestor, templateAttributeSymbol, genericTemplateAttributeSymbol))
                return true;
        }

        return false;
    }

    private static bool HasTemplateAttribute(
        SemanticModel semanticModel,
        TypeDeclarationSyntax node,
        INamedTypeSymbol? templateAttributeSymbol,
        INamedTypeSymbol? genericTemplateAttributeSymbol)
    {
        foreach (var attributeList in node.AttributeLists)
        {
            foreach (var attributeSyntax in attributeList.Attributes)
            {
                if (semanticModel.GetTypeInfo(attributeSyntax).Type is not INamedTypeSymbol attributeType)
                    continue;

                if (SymbolEqualityComparer.Default.Equals(attributeType, templateAttributeSymbol))
                    return true;

                if (SymbolEqualityComparer.Default.Equals(attributeType.OriginalDefinition, genericTemplateAttributeSymbol))
                    return true;
            }
        }

        return false;
    }
}

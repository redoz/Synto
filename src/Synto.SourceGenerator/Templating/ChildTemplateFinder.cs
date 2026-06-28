using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.Templating;

namespace Synto;

/// <summary>
/// Discovers method-level <c>[Template]</c> declarations nested inside a class-level <c>[Template]</c> carrier
/// (Capability 1). Such a method is a SIBLING child template: it is independently picked up by
/// <c>ForAttributeWithMetadataName</c> and generates its OWN factory, so when the PARENT carrier is processed the
/// child method must be excluded from the parent — trimmed from the quoted output and excluded from the parent's
/// live-staging pipeline (otherwise its inner <c>Parameter&lt;T&gt;()</c> roots leak into the parent factory's
/// parameter list). Mirrors the attribute-symbol match of <see cref="SpliceMemberGeneratorFinder"/> but matches the
/// <see cref="TemplateAttribute"/> symbol and only over METHOD declarations (never the carrier's own class-level
/// attribute). Nothing is captured into pipeline state — the result is consumed entirely inside the transform.
/// </summary>
internal sealed class ChildTemplateFinder : CSharpSyntaxWalker
{
    public static IReadOnlyList<MethodDeclarationSyntax> FindChildTemplates(SemanticModel semanticModel, SyntaxNode carrier)
    {
        var finder = new ChildTemplateFinder(semanticModel);
        finder.Visit(carrier);
        return finder._childTemplates;
    }

    private readonly SemanticModel _semanticModel;
    private readonly INamedTypeSymbol? _templateAttributeSymbol;
    private readonly List<MethodDeclarationSyntax> _childTemplates = new();

    private ChildTemplateFinder(SemanticModel semanticModel)
    {
        _semanticModel = semanticModel;
        _templateAttributeSymbol = semanticModel.Compilation.GetTypeByMetadataName(typeof(TemplateAttribute).FullName!);
    }

    public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        if (_templateAttributeSymbol is not null && HasTemplateAttribute(node))
            _childTemplates.Add(node);

        base.VisitMethodDeclaration(node);
    }

    private bool HasTemplateAttribute(MethodDeclarationSyntax node)
    {
        foreach (var attributeList in node.AttributeLists)
        {
            foreach (var attributeSyntax in attributeList.Attributes)
            {
                if (SymbolEqualityComparer.Default.Equals(_semanticModel.GetTypeInfo(attributeSyntax).Type, _templateAttributeSymbol))
                    return true;
            }
        }

        return false;
    }
}

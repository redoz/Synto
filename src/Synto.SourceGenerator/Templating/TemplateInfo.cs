using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto.Templating;

internal class TemplateInfo
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


        if (attr.ApplicationSyntaxReference!.GetSyntax() is not AttributeSyntax attrSyntax)
            return null;

        if (attr.ConstructorArguments.Length != 1)
            return null;

        var typeArg = attr.ConstructorArguments[0];

        if (typeArg.Kind != TypedConstantKind.Type)
            return null;

        if (typeArg.Value is not INamedTypeSymbol factoryType)
            return null;


        var target = new TargetType(attr, factoryType);

        var source = new Source(syntaxContext.TargetNode, syntaxContext.TargetSymbol.Name);

        var options = TemplateOption.Default;

        foreach (var namedArgs in attr.NamedArguments)
        {
            if (StringComparer.Ordinal.Equals(namedArgs.Key, nameof(TemplateAttribute.Options)))
            {
                options = (TemplateOption)namedArgs.Value.Value!;
            }
        }

        return new TemplateInfo(syntaxContext.SemanticModel, attrSyntax, source, target, options);
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
        if (syntax is AttributeSyntax attrSyntax
            && attrSyntax.ArgumentList?.Arguments.First().Expression is TypeOfExpressionSyntax typeOfExpr)
        {
            return typeOfExpr.GetLocation();
        }

        return null;
    }
}
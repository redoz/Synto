using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto.Templating;

internal struct TemplateInfo
{
    private TemplateInfo(GeneratorAttributeSyntaxContext syntaxContext)
    {
        TemplateAttribute = syntaxContext.Attributes.First();
        Attribute = (AttributeSyntax)TemplateAttribute.ApplicationSyntaxReference!.GetSyntax();

        var typeArg = TemplateAttribute.ConstructorArguments.First();

        Target = new TargetType(TemplateAttribute, (INamedTypeSymbol)typeArg.Value!);

        Source = new Source(syntaxContext.TargetNode, syntaxContext.TargetSymbol.Name);

        Options = TemplateOption.Default;

        foreach (var namedArgs in TemplateAttribute.NamedArguments)
        {
            if (StringComparer.Ordinal.Equals(namedArgs.Key, nameof(Synto.TemplateAttribute.Options)))
            {
                Options = (TemplateOption)namedArgs.Value.Value!;
            }
        }

        SemanticModel = syntaxContext.SemanticModel;
    }

    public AttributeData TemplateAttribute { get; }
    public AttributeSyntax Attribute { get; }
    public TargetType Target { get; }
    public Source Source { get; }
    public TemplateOption Options { get; }
    public SemanticModel SemanticModel { get; }

    public static TemplateInfo Create(GeneratorAttributeSyntaxContext syntaxContext)
    {
        return new TemplateInfo(syntaxContext);
    }
}





internal record struct Source(SyntaxNode Syntax, string Identifier)
{
    public SyntaxNode Syntax { get; } = Syntax;
    public string Identifier { get; } = Identifier;
}

internal record struct TargetType(AttributeData TemplateAttributeData, ITypeSymbol? Type)
{
    private static readonly SymbolDisplayFormat SymbolDisplayFormat = new(
        SymbolDisplayGlobalNamespaceStyle.Omitted,
        SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        SymbolDisplayGenericsOptions.IncludeTypeParameters
    );

    public ITypeSymbol? Type { get; } = Type;

    public string FullName
    {
        get
        {
            if (Type is null)
                return "<unknown>";

            return Type.ToDisplayString(
                SymbolDisplayFormat);
        }
    }

    public Location? GetReferenceLocation()
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
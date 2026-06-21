using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.Matching;

namespace Synto;

/// <summary>
/// Transform-local model for a single <c>[Match&lt;TMatcher&gt;]</c> pattern. It holds the
/// <see cref="SemanticModel"/>, symbols and syntax needed to lower the pattern; it lives ONLY inside the
/// FAWMN transform and is NEVER captured into pipeline state (only the equatable
/// <see cref="MatchGenerationResult"/> flows out). Mirrors <see cref="TemplateInfo"/>.
/// </summary>
internal sealed class MatchInfo
{
    private static readonly SymbolDisplayFormat TargetNameFormat = new(
        SymbolDisplayGlobalNamespaceStyle.Omitted,
        SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        SymbolDisplayGenericsOptions.IncludeTypeParameters);

    private MatchInfo(
        SemanticModel semanticModel,
        AttributeSyntax attributeSyntax,
        INamedTypeSymbol target,
        IMethodSymbol patternSymbol,
        SyntaxNode patternSyntax,
        string name,
        MatchOption option)
    {
        SemanticModel = semanticModel;
        AttributeSyntax = attributeSyntax;
        Target = target;
        PatternSymbol = patternSymbol;
        PatternSyntax = patternSyntax;
        Name = name;
        Option = option;
    }

    public SemanticModel SemanticModel { get; }
    public AttributeSyntax AttributeSyntax { get; }

    /// <summary>The matcher target type read off <c>AttributeClass.TypeArguments[0]</c>.</summary>
    public INamedTypeSymbol Target { get; }
    public IMethodSymbol PatternSymbol { get; }
    public SyntaxNode PatternSyntax { get; }
    public string Name { get; }
    public MatchOption Option { get; }

    /// <summary>A display name for the target, used in target-validation diagnostics.</summary>
    public string TargetFullName => Target.ToDisplayString(TargetNameFormat);

    public static MatchInfo? Create(GeneratorAttributeSyntaxContext syntaxContext)
    {
        // Sanity-check the inputs the compiler should already have constrained (attribute-on-method).
        var attr = syntaxContext.Attributes.FirstOrDefault();
        if (attr is null)
            return null;

        if (attr.ApplicationSyntaxReference?.GetSyntax() is not AttributeSyntax attrSyntax)
            return null;

        // The matcher target is the attribute's single type argument: [Match<TMatcher>] => TypeArguments[0].
        if (attr.AttributeClass is not { TypeArguments.Length: 1 } attributeClass)
            return null;

        if (attributeClass.TypeArguments[0] is not INamedTypeSymbol target)
            return null;

        if (syntaxContext.TargetSymbol is not IMethodSymbol patternSymbol)
            return null;

        // The option is the (optional) ctor argument; an omitted optional arg still surfaces with its default.
        var option = MatchOption.None;
        if (attr.ConstructorArguments.Length == 1 && attr.ConstructorArguments[0].Value is { } optionValue)
            option = (MatchOption)optionValue;

        return new MatchInfo(
            syntaxContext.SemanticModel,
            attrSyntax,
            target,
            patternSymbol,
            syntaxContext.TargetNode,
            patternSymbol.Name,
            option);
    }
}

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.Templating;

namespace Synto.Generators.Templating.Emit;

internal sealed class TemplateInfo
{
    private TemplateInfo(SemanticModel semanticModel, AttributeSyntax attributeSyntax, Source templateSource, TargetType factoryTarget, TemplateOption options, TemplateExtent extent)
    {
        SemanticModel = semanticModel;
        AttributeSyntax = attributeSyntax;
        Source = templateSource;
        Target = factoryTarget;
        Options = options;
        Extent = extent;
    }

    public AttributeSyntax AttributeSyntax { get; private set; }
    public TargetType Target { get; private set; }
    public Source Source { get; private set; }
    public TemplateOption Options { get; private set; }
    public TemplateExtent Extent { get; private set; }
    public SemanticModel SemanticModel { get; private set; }

    public static TemplateInfo? Create(GeneratorAttributeSyntaxContext syntaxContext)
    {
        // Thin wrapper: unpack the generator-only context and delegate to the plain-input overload below.
        return Create(syntaxContext.SemanticModel, syntaxContext.TargetNode, syntaxContext.Attributes, CancellationToken.None);
    }

    // Discovery entry decoupled from GeneratorAttributeSyntaxContext so the SY1035 highlight analyzer can drive
    // discovery — interim workaround, see spec §Known debt / 1.0 gate (Option C).
    // targetNode is a plain SyntaxNode (not MemberDeclarationSyntax): template carriers include LOCAL FUNCTIONS
    // (LocalFunctionStatementSyntax : StatementSyntax) as well as method/class/struct members.
    public static TemplateInfo? Create(SemanticModel semanticModel, SyntaxNode targetNode, ImmutableArray<AttributeData> attributes, CancellationToken cancellationToken)
    {
        // this just sanity checks against accepting things that the compiler should've rejected in the first place

        var attr = attributes.FirstOrDefault();
        if (attr is null)
            return null;


        if (attr.ApplicationSyntaxReference is null)
            return null;

        if (attr.ApplicationSyntaxReference.GetSyntax(cancellationToken) is not AttributeSyntax attrSyntax)
            return null;

        // Only the generic [Template<T>] form flows here (the parameterless [Template] is handled by the arity-0
        // transform, which never derives a target). The factory target is the attribute class's single type
        // argument; ConstructorArguments is empty on the generic form.
        if (attr.AttributeClass is not { IsGenericType: true, TypeArguments.Length: 1 } genericAttr)
            return null;

        if (genericAttr.TypeArguments[0] is not INamedTypeSymbol factoryType)
            return null;

        // TargetSymbol.Name in the generator path == the declared symbol's name; derive it here so the plain
        // overload stays context-free. GetDeclaredSymbol resolves the same symbol for every template carrier
        // (method / class / struct member, or a local function).
        if (semanticModel.GetDeclaredSymbol(targetNode, cancellationToken) is not { } targetSymbol)
            return null;

        var target = new TargetType(attr, factoryType);

        var source = new Source(targetNode, targetSymbol.Name);

        return new TemplateInfo(semanticModel, attrSyntax, source, target, ReadOptions(attr), ReadExtent(attr));
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
            return new TemplateInfo(semanticModel, attrSyntax, source, parentTarget, ReadOptions(attr), ReadExtent(attr));
        }

        return null;
    }

    private static TemplateOption ReadOptions(AttributeData attr)
    {
        var options = default(TemplateOption);

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

    private static TemplateExtent ReadExtent(AttributeData attr)
    {
        var extent = TemplateExtent.Carrier;

        foreach (var namedArgs in attr.NamedArguments)
        {
            if (StringComparer.Ordinal.Equals(namedArgs.Key, nameof(TemplateAttribute.Extent)))
            {
                // A mid-keystroke / unresolvable Extent expression yields an error-kind constant (Value == null);
                // an enum constant boxes to its underlying int. Anything that is not a plain int is skipped so a
                // malformed argument defaults to Carrier instead of NRE-ing the transform.
                if (namedArgs.Value.Kind != TypedConstantKind.Error && namedArgs.Value.Value is int extentValue)
                {
                    extent = (TemplateExtent)extentValue;
                }
            }
        }

        return extent;
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

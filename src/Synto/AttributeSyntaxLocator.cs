using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto;

public sealed record SyntaxLocation<TTarget>(AttributeSyntax Attribute, TTarget Target) where TTarget : CSharpSyntaxNode
{
    public AttributeSyntax Attribute { get; } = Attribute;
    public TTarget Target { get; } = Target;
}


public class AttributeSyntaxLocator<TAttribute, TTarget> : ISyntaxContextReceiver where TTarget : CSharpSyntaxNode
{
    private readonly List<SyntaxLocation<TTarget>> _projectionAttrs;

    public IEnumerable<SyntaxLocation<TTarget>> Locations => _projectionAttrs;

    public AttributeSyntaxLocator()
    {
        _projectionAttrs = new List<SyntaxLocation<TTarget>>();
    }

    public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
    {

        if (context.Node is AttributeSyntax syntax)
        {
            var knownAttrType = context.SemanticModel.Compilation.GetTypeByMetadataName(typeof(TAttribute).FullName);
            Debug.Assert(knownAttrType is not null, $"The attribute specified ({typeof(TAttribute).FullName}) could not be found in the Compilation");

            var typeInfo = context.SemanticModel.GetTypeInfo(syntax);

            if (typeInfo.Type is INamedTypeSymbol typeSymbol && SymbolEqualityComparer.Default.Equals(typeSymbol, knownAttrType))
            {
                _projectionAttrs.Add(new SyntaxLocation<TTarget>(syntax, (TTarget)syntax.FirstAncestorOrSelf<AttributeListSyntax>()!.Parent!));
            }
        }
    }
}
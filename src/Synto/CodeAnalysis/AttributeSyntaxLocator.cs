using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Synto.CodeAnalysis;

public sealed record SyntaxLocation(AttributeSyntax Attribute, MemberDeclarationSyntax Target)
{
    public AttributeSyntax Attribute { get; } = Attribute;
    public MemberDeclarationSyntax Target { get; } = Target;
}


public class AttributeSyntaxLocator<TAttribute> : ISyntaxContextReceiver
{
    private readonly List<SyntaxLocation> _projectionAttrs;

    public IEnumerable<SyntaxLocation> Locations => _projectionAttrs;

    public AttributeSyntaxLocator()
    {
        _projectionAttrs = new List<SyntaxLocation>();
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
                _projectionAttrs.Add(new SyntaxLocation(syntax, syntax.FirstAncestorOrSelf<MemberDeclarationSyntax>()!));
            }
        }
    }
}
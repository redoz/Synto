﻿using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto.Templating;

// these are some remnants from the initial experiments, probably a lot of this can be remove and cleaned up
internal record TemplateInfo(AttributeSyntax Attribute, TargetType Target, SourceFunction? Source)
{
    public AttributeSyntax Attribute { get; } = Attribute;
    public TargetType Target { get; } = Target;
    public SourceFunction? Source { get; } = Source;
}

internal record SourceFunction(SyntaxNode Syntax, SyntaxToken Identifier, ParameterListSyntax ParameterListSyntax, BlockSyntax? Body)
{
    public SyntaxNode Syntax { get; } = Syntax;
    public SyntaxToken Identifier { get; } = Identifier;
    public BlockSyntax? Body { get; } = Body;
    public ParameterListSyntax ParameterListSyntax { get; } = ParameterListSyntax;
}

internal record TargetType(TypeSyntax Reference, ITypeSymbol? Type)
{
    public ITypeSymbol? Type { get; } = Type;

    public TypeSyntax Reference { get; } = Reference ?? throw new ArgumentNullException(nameof(Reference));

    public string FullName
    {
        get
        {
            if (Type is null)
                return "<unknown>";

            return Type.ToDisplayString(new SymbolDisplayFormat(SymbolDisplayGlobalNamespaceStyle.Omitted,
                SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                SymbolDisplayGenericsOptions.IncludeTypeParameters));
        }
    }
}
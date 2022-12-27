using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto;

internal class TemplateInfo
{
    public TemplateInfo(AttributeSyntax projectionAttribute, TargetType target, SourceFunction? source)
    {
        this.ProjectionAttribute = projectionAttribute;
        this.Target = target;
        this.Source = source;
    }

    public AttributeSyntax ProjectionAttribute { get; }
    public TargetType Target { get; }
    public SourceFunction? Source { get; }
}

internal class SourceFunction
{
    public SourceFunction(SyntaxNode syntax, SyntaxToken identifier, ParameterListSyntax parameterListSyntax, BlockSyntax? body)
    {
        this.Syntax = syntax;
        this.Identifier = identifier;
        this.ParameterListSyntax = parameterListSyntax;
        this.Body = body;
    }

    public SyntaxNode Syntax { get; }
    public SyntaxToken Identifier { get; }
    public BlockSyntax? Body { get; }
    public ParameterListSyntax ParameterListSyntax { get; }
}

internal class TargetType
{
    public ITypeSymbol? Type { get; }

    public TypeSyntax Reference { get; }

    public TargetType(TypeSyntax reference, ITypeSymbol? type)
    {
        this.Reference = reference ?? throw new ArgumentNullException(nameof(reference));
        this.Type = type;
    }

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
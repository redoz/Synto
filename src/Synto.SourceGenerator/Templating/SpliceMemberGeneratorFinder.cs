using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.Templating;

namespace Synto;

/// <summary>
/// The return-type classification of a <c>[Splice]</c> member generator: a single member
/// (<c>MemberDeclarationSyntax</c> or a subtype), an enumerable of members
/// (<c>IEnumerable&lt;MemberDeclarationSyntax&gt;</c> or of a subtype), or an unsupported shape.
/// </summary>
internal enum SpliceMemberReturnShape
{
    Single,
    Enumerable,
    Invalid,
}

/// <summary>
/// A discovered <c>[Splice]</c> member generator: a method inside a <c>[Template]</c> type marked
/// <c>[Splice]</c>, classified by its shape (static?, parameterless?, return shape). The classification is
/// computed from the <see cref="SemanticModel"/> inside the transform; no symbol is retained on this value.
/// </summary>
internal sealed class SpliceMemberGenerator(MethodDeclarationSyntax method, bool isStatic, bool hasParameters, SpliceMemberReturnShape returnShape)
{
    public MethodDeclarationSyntax Method { get; } = method;
    public bool IsStatic { get; } = isStatic;
    public bool HasParameters { get; } = hasParameters;
    public SpliceMemberReturnShape ReturnShape { get; } = returnShape;
}

/// <summary>
/// Discovers <c>[Splice]</c>-marked methods in a <c>[Template]</c> body and classifies each. Mirrors the
/// attribute-symbol match of <see cref="SpliceParameterFinder"/> but over method declarations; nothing is
/// captured into pipeline state (the result is consumed entirely inside the transform).
/// </summary>
internal sealed class SpliceMemberGeneratorFinder : CSharpSyntaxWalker
{
    public static IReadOnlyList<SpliceMemberGenerator> FindGenerators(SemanticModel semanticModel, SyntaxNode node)
    {
        var finder = new SpliceMemberGeneratorFinder(semanticModel);
        finder.Visit(node);
        return finder._generators;
    }

    private readonly SemanticModel _semanticModel;
    private readonly INamedTypeSymbol? _spliceAttributeSymbol;
    private readonly INamedTypeSymbol? _memberDeclarationSyntaxSymbol;
    private readonly List<SpliceMemberGenerator> _generators = new();

    private SpliceMemberGeneratorFinder(SemanticModel semanticModel)
    {
        _semanticModel = semanticModel;
        _spliceAttributeSymbol = semanticModel.Compilation.GetTypeByMetadataName(typeof(SpliceAttribute).FullName!);
        _memberDeclarationSyntaxSymbol = semanticModel.Compilation.GetTypeByMetadataName("Microsoft.CodeAnalysis.CSharp.Syntax.MemberDeclarationSyntax");
        Debug.Assert(_spliceAttributeSymbol is not null);
    }

    public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        if (_spliceAttributeSymbol is not null && HasSpliceAttribute(node))
        {
            var methodSymbol = _semanticModel.GetDeclaredSymbol(node);

            bool isStatic = methodSymbol?.IsStatic ?? node.Modifiers.Any(SyntaxKind.StaticKeyword);
            bool hasParameters = node.ParameterList.Parameters.Count > 0;
            var returnShape = ClassifyReturnShape(methodSymbol?.ReturnType);

            _generators.Add(new SpliceMemberGenerator(node, isStatic, hasParameters, returnShape));
        }

        base.VisitMethodDeclaration(node);
    }

    private bool HasSpliceAttribute(MethodDeclarationSyntax node)
    {
        foreach (var attributeList in node.AttributeLists)
        {
            foreach (var attributeSyntax in attributeList.Attributes)
            {
                if (SymbolEqualityComparer.Default.Equals(_semanticModel.GetTypeInfo(attributeSyntax).Type, _spliceAttributeSymbol))
                    return true;
            }
        }

        return false;
    }

    private SpliceMemberReturnShape ClassifyReturnShape(ITypeSymbol? returnType)
    {
        if (returnType is null || _memberDeclarationSyntaxSymbol is null)
            return SpliceMemberReturnShape.Invalid;

        if (IsMemberDeclarationOrSubtype(returnType))
            return SpliceMemberReturnShape.Single;

        if (returnType is INamedTypeSymbol named
            && named.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T
            && named.TypeArguments.Length == 1
            && IsMemberDeclarationOrSubtype(named.TypeArguments[0]))
        {
            return SpliceMemberReturnShape.Enumerable;
        }

        return SpliceMemberReturnShape.Invalid;
    }

    private bool IsMemberDeclarationOrSubtype(ITypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, _memberDeclarationSyntaxSymbol))
                return true;
        }

        return false;
    }
}

using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto.Diagnostics;

internal sealed class TargetInfo
{
    private TargetInfo(SemanticModel semanticModel, AttributeSyntax attributeSyntax, MethodDeclarationSyntax target)
    {
        SemanticModel = semanticModel;
        AttributeSyntax = attributeSyntax;
        Target = target;
    }

    public AttributeSyntax AttributeSyntax { get; }
    public MethodDeclarationSyntax Target { get; }
    public SemanticModel SemanticModel { get; }

    public static TargetInfo? Create(GeneratorAttributeSyntaxContext syntaxContext, CancellationToken cancellationToken)
    {
        // this just sanity checks against accepting things that the compiler should've rejected in the first place

        var attr = syntaxContext.Attributes.FirstOrDefault();
        if (attr is null)
            return null;

        if (attr.ApplicationSyntaxReference!.GetSyntax(cancellationToken) is not AttributeSyntax attrSyntax)
            return null;

        if (syntaxContext.TargetNode is not MethodDeclarationSyntax methodDeclaration)
            return null;


        return new TargetInfo(syntaxContext.SemanticModel, attrSyntax, methodDeclaration);
    }
}
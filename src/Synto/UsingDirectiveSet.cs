using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto;

public class UsingDirectiveSet : IEnumerable<UsingDirectiveSyntax>
{
    private readonly UsingDirectiveSyntax[] _predefined;
    private readonly List<UsingDirectiveSyntax> _usings;

    public UsingDirectiveSet(IEnumerable<UsingDirectiveSyntax> predefined)
    {
        // we don't support static or alias usings (we could at least support the alias, but we don't)
        this._predefined = predefined.Where(usingSyntax => usingSyntax.StaticKeyword.IsKind(SyntaxKind.None) && usingSyntax.Alias is null).ToArray();
        this._usings = new List<UsingDirectiveSyntax>();
    }

    public IEnumerator<UsingDirectiveSyntax> GetEnumerator()
    {
        return this._usings.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable) this._usings).GetEnumerator();
    }

    public void AddNamespace(NameSyntax namespaceName)
    {
        
        if (!this._usings.Any(usingSyntax => usingSyntax.Name.IsEquivalentTo(namespaceName, topLevel: true))
            && !this._predefined.Any(usingSyntax => usingSyntax.Name.IsEquivalentTo(namespaceName, topLevel: true)))
        {
            this._usings.Add(SyntaxFactory.UsingDirective(namespaceName));
        }
    }

    public NameSyntax GetTypeName(TypeSyntax fullyQualifiedName)
    {
        switch (fullyQualifiedName)
        {
            case IdentifierNameSyntax identifierName:
                return identifierName;
            case QualifiedNameSyntax qualifiedName:
            {
                NameSyntax namespaceName = qualifiedName.Left;
                AddNamespace(namespaceName);
                return qualifiedName.Right;
            }
            default:
                throw new NotSupportedException();
        }
    }
}
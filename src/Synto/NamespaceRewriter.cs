using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;

namespace Synto
{
    public sealed class NamespaceRewriter : CSharpSyntaxRewriter
    {
        private readonly List<UsingDirectiveSyntax> _usingDirectives;
        private SemanticModel _semanticModel;

        public NamespaceRewriter(SemanticModel semanticModel)
        {
           // System.Diagnostics.Debugger.Launch();
            this._semanticModel = semanticModel;
            this._usingDirectives = new List<UsingDirectiveSyntax>();
        }

        public override SyntaxNode? VisitQualifiedName(QualifiedNameSyntax node)
        {
            var typeInfo = this._semanticModel.GetTypeInfo(node);
            if (typeInfo.Type is null)
                return base.VisitQualifiedName(node);

            ITypeSymbol type = typeInfo.Type;
            
            while (type.ContainingSymbol is ITypeSymbol parentType)
                type = parentType;

            if (type.ContainingNamespace is { IsGlobalNamespace: true })
                return base.VisitQualifiedName(node);

            var namespaceName = type.ContainingNamespace.GetNamespaceName();

            if (!this._usingDirectives.Any(ud => ud.Name.IsEquivalentTo(namespaceName)))
                this._usingDirectives.Add(SyntaxFactory.UsingDirective(namespaceName));

            return typeInfo.Type.GetTypeName();
        }

        public override SyntaxNode? VisitCompilationUnit(CompilationUnitSyntax node)
        {
            CompilationUnitSyntax? compilationUnit = base.VisitCompilationUnit(node) as CompilationUnitSyntax;

            if (compilationUnit is null)
                return null;

            if (_usingDirectives.Count == 0)
                return compilationUnit;

            return SyntaxFactory.CompilationUnit(
                compilationUnit.Externs,
                compilationUnit.Usings.AddRange(_usingDirectives),
                compilationUnit.AttributeLists, 
                compilationUnit.Members, 
                compilationUnit.EndOfFileToken);
        }
    }
}

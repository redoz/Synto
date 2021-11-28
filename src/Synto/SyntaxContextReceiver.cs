using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto;

namespace Synto
{
    internal class SyntaxContextReceiver : ISyntaxContextReceiver
    {
        private readonly List<TemplateInfo> _projectionAttrs;

        public IEnumerable<TemplateInfo> ProjectionAttributes => this._projectionAttrs;

        public SyntaxContextReceiver()
        {
            this._projectionAttrs = new List<TemplateInfo>();
        }

        public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
        {
            if (context.Node is AttributeSyntax syntax)
            {
                var typeInfo = context.SemanticModel.GetTypeInfo(syntax);
                var knownAttrType = context.SemanticModel.Compilation.GetTypeByMetadataName($"{nameof(Synto)}.{nameof(TemplateAttribute)}");
                if (typeInfo.Type is INamedTypeSymbol typeSymbol && SymbolEqualityComparer.Default.Equals(typeSymbol, knownAttrType))
                {
                    var targetArg = syntax.ArgumentList?.Arguments.FirstOrDefault();
                    if (targetArg?.Expression is TypeOfExpressionSyntax typeOfExpr)
                    {
                        // capture target info
                        var target = typeOfExpr.Type;
                        var targetType = context.SemanticModel.GetTypeInfo(target);

                        // and source info
                        SourceFunction? source;
                        var attrListSyntax = syntax.GetAncestor<AttributeListSyntax>();

                        //var symbolInfo = context.SemanticModel.GetSymbolInfo(attrListSyntax?.Parent);
                        //symbolInfo.Symbol.ToDisplayString()

                        if (attrListSyntax?.Parent is LocalFunctionStatementSyntax localFunctionSyntax)
                            source = new SourceFunction(attrListSyntax.Parent, localFunctionSyntax.Identifier, localFunctionSyntax.ParameterList, localFunctionSyntax.Body);
                        else if (attrListSyntax?.Parent is MethodDeclarationSyntax methodSyntax)
                            source = new SourceFunction(attrListSyntax.Parent, methodSyntax.Identifier, methodSyntax.ParameterList, methodSyntax.Body!);
                        else
                            source = null;



                        _projectionAttrs.Add(new TemplateInfo(syntax, new TargetType(target, targetType.Type), source, temp: typeInfo));
                    }
                }
            }
        }
    }
}

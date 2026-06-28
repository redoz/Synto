using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto;

public static class ImplementsAttributeExtensions
{
    public static T ApplyImplementsAttribute<T>(this T node, string interfaceFqn) where T : TypeDeclarationSyntax
    {
        var baseType = SimpleBaseType(ParseTypeName(interfaceFqn));
        if (node.BaseList is null)
            return (T)node.WithBaseList(BaseList(SingletonSeparatedList<BaseTypeSyntax>(baseType)));
        return (T)node.WithBaseList(node.BaseList.WithTypes(node.BaseList.Types.Add(baseType)));
    }
}

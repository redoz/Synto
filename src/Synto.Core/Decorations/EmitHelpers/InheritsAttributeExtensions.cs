using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto;

public static class InheritsAttributeExtensions
{
    public static T ApplyInheritsAttribute<T>(this T node, string baseFqn) where T : TypeDeclarationSyntax
    {
        var baseType = SimpleBaseType(ParseTypeName(baseFqn));
        if (node.BaseList is null)
            return (T)node.WithBaseList(BaseList(SingletonSeparatedList<BaseTypeSyntax>(baseType)));
        return (T)node.WithBaseList(node.BaseList.WithTypes(node.BaseList.Types.Insert(0, baseType)));
    }
}

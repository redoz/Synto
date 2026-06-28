using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto;

public static class IdentifierAttributeExtensions
{
    public static T ApplyIdentifierAttribute<T>(this T node, string identifier) where T : TypeDeclarationSyntax
    {
        var renamed = node.WithIdentifier(Identifier(identifier));
        var members = renamed.Members;
        for (int i = 0; i < members.Count; i++)
            if (members[i] is ConstructorDeclarationSyntax ctor)
                members = members.Replace(ctor, ctor.WithIdentifier(Identifier(identifier)));
        return (T)renamed.WithMembers(members);
    }
}

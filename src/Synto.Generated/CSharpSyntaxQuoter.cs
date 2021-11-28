using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SF = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto.Generated
{
    public partial class CSharpSyntaxQuoter : CSharpSyntaxVisitor<ExpressionSyntax>
    {

        protected static readonly NameSyntax SyntaxFactoryToken = SF.ParseName(typeof(SyntaxFactory).FullName!);

        protected static InvocationExpressionSyntax SyntaxFactoryInvocation(string functionName, params ExpressionSyntax[] arguments)
        {
            return SF.InvocationExpression(SF.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                                                     SyntaxFactoryToken,
                                                                     SF.IdentifierName(functionName)))
                     .AddArgumentListArguments(Array.ConvertAll(arguments, SF.Argument));
        }

        protected static InvocationExpressionSyntax SyntaxFactoryInvocation(string functionName, IEnumerable<ExpressionSyntax> arguments)
        {
            return SyntaxFactoryInvocation(functionName, arguments.ToArray());
        }


        protected static ExpressionSyntax ToArrayLiteral(IEnumerable<ExpressionSyntax> nodeList, TypeSyntax elementType)
        {
            var list = nodeList.ToList();
            if (list.Count == 0)
            {
                return SF.InvocationExpression(
                    SF.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                        SF.ParseTypeName(typeof(Array).FullName!),
                        SF.GenericName(SF.Identifier(nameof(Array.Empty)), SF.TypeArgumentList(SF.SingletonSeparatedList(elementType)))));
            }
            return SF.ImplicitArrayCreationExpression(SF.InitializerExpression(SyntaxKind.ArrayInitializerExpression,
                SF.SeparatedList(list)));
        }


        public virtual ExpressionSyntax Visit<TNode>(SyntaxList<TNode> nodeList) where TNode : SyntaxNode
        {
            var quotedExprs = nodeList.Select(node => Visit(node)!);

            TypeSyntax elementType = SF.ParseTypeName(typeof(TNode).FullName!);
            return SF.InvocationExpression(
                SF.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactoryToken,
                    SF.GenericName(
                        SF.Identifier(nameof(SF.List)),
                        SF.TypeArgumentList(
                            SF.SingletonSeparatedList(elementType)))),
                SF.ArgumentList(SF.SingletonSeparatedList(SF.Argument(ToArrayLiteral(quotedExprs, elementType)))));
        }

        public virtual ExpressionSyntax Visit<TNode>(SeparatedSyntaxList<TNode> nodeList, CSharpSyntaxVisitor<ExpressionSyntax> visitor) where TNode : SyntaxNode
        {
            return SyntaxFactoryInvocation(nameof(SF.SeparatedList),
                ToArrayLiteral(nodeList.Select(t => visitor.Visit(t)!), SF.ParseTypeName(typeof(TNode).FullName!)),
                ToArrayLiteral(nodeList.GetSeparators().Select(Visit), SF.ParseTypeName(typeof(SyntaxToken).FullName!)));
        }

        public virtual ExpressionSyntax Visit(SyntaxKind kind)
        {
            return SF.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SF.ParseName(typeof(SyntaxKind).FullName!),
                SF.IdentifierName(kind.ToString()));
        }

        public virtual ExpressionSyntax Visit(SyntaxToken token)
        {
            return SyntaxFactoryInvocation(nameof(SF.Token), Visit(token.Kind()));
        }

        public virtual ExpressionSyntax Visit(SyntaxTokenList tokenList)
        {
            return SyntaxFactoryInvocation(nameof(SF.TokenList), tokenList.Select(Visit));
        }

    }
}
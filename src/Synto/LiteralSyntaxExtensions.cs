using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SF = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto
{
    public static class LiteralSyntaxExtensions
    {
        public static ExpressionSyntax ToLiteral(this string value) 
        {
            return SF.LiteralExpression(SyntaxKind.StringLiteralExpression, SF.Literal(value));
        }

        public static ExpressionSyntax ToLiteral(this bool value)
        {
            return SF.LiteralExpression(value ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression);
        }
        public static ExpressionSyntax ToLiteral(this ulong value)
        {
            return SF.LiteralExpression(SyntaxKind.StringLiteralExpression, SF.Literal(value));
        }

        public static ExpressionSyntax ToLiteral(this long value)
        {
            return SF.LiteralExpression(SyntaxKind.StringLiteralExpression, SF.Literal(value));
        }

        public static ExpressionSyntax ToLiteral(this uint value)
        {
            return SF.LiteralExpression(SyntaxKind.StringLiteralExpression, SF.Literal(value));
        }

        public static ExpressionSyntax ToLiteral(this int value)
        {
            return SF.LiteralExpression(SyntaxKind.StringLiteralExpression, SF.Literal(value));
        }

        public static ExpressionSyntax ToLiteral(this ushort value)
        {
            return SF.LiteralExpression(SyntaxKind.StringLiteralExpression, SF.Literal(value));
        }

        public static ExpressionSyntax ToLiteral(this short value)
        {
            return SF.LiteralExpression(SyntaxKind.StringLiteralExpression, SF.Literal(value));
        }

        public static ExpressionSyntax ToLiteral(this byte value)
        {
            return SF.LiteralExpression(SyntaxKind.StringLiteralExpression, SF.Literal(value));
        }

        public static ExpressionSyntax ToLiteral(this sbyte value)
        {
            return SF.LiteralExpression(SyntaxKind.StringLiteralExpression, SF.Literal(value));
        }

        public static T ToLiteral<T>(this T node) where T : SyntaxNode
        {
            return node;//CSharpSyntaxQuoter.Quote(node);
        }
    }
}

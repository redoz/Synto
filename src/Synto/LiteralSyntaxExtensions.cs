using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SF = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto;

public static class LiteralSyntaxExtensions
{
    public static ExpressionSyntax ToLiteral(this string? value)
    {
        return value is null 
            ? SF.LiteralExpression(SyntaxKind.NullLiteralExpression) 
            : SF.LiteralExpression(SyntaxKind.StringLiteralExpression, SF.Literal(value));
    }

    public static ExpressionSyntax ToLiteral(this bool value)
    {
        return SF.LiteralExpression(value ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression);
    }
    public static ExpressionSyntax ToLiteral(this ulong value)
    {
        return SF.LiteralExpression(SyntaxKind.NumericLiteralExpression, SF.Literal(value));
    }

    public static ExpressionSyntax ToLiteral(this long value)
    {
        return SF.LiteralExpression(SyntaxKind.NumericLiteralExpression, SF.Literal(value));
    }

    public static ExpressionSyntax ToLiteral(this uint value)
    {
        return SF.LiteralExpression(SyntaxKind.NumericLiteralExpression, SF.Literal(value));
    }

    public static ExpressionSyntax ToLiteral(this int value)
    {
        return SF.LiteralExpression(SyntaxKind.NumericLiteralExpression, SF.Literal(value));
    }

    public static ExpressionSyntax ToLiteral(this ushort value)
    {
        return SF.LiteralExpression(SyntaxKind.NumericLiteralExpression, SF.Literal(value));
    }

    public static ExpressionSyntax ToLiteral(this short value)
    {
        return SF.LiteralExpression(SyntaxKind.NumericLiteralExpression, SF.Literal(value));
    }

    public static ExpressionSyntax ToLiteral(this byte value)
    {
        return SF.LiteralExpression(SyntaxKind.NumericLiteralExpression, SF.Literal(value));
    }

    public static ExpressionSyntax ToLiteral(this sbyte value)
    {
        return SF.LiteralExpression(SyntaxKind.NumericLiteralExpression, SF.Literal(value));
    }

    public static ExpressionSyntax ToLiteral(this decimal value)
    {
        return SF.LiteralExpression(SyntaxKind.NumericLiteralExpression, SF.Literal(value));
    }

    public static ExpressionSyntax ToLiteral(this float value)
    {
        return SF.LiteralExpression(SyntaxKind.NumericLiteralExpression, SF.Literal(value));
    }

    public static ExpressionSyntax ToLiteral(this double value)
    {
        return SF.LiteralExpression(SyntaxKind.NumericLiteralExpression, SF.Literal(value));
    }

    public static ExpressionSyntax ToLiteral(this char value)
    {
        return SF.LiteralExpression(SyntaxKind.CharacterLiteralExpression, SF.Literal(value));
    }

    //public static ExpressionSyntax ToLiteral(this SyntaxKind kind)
    //{
    //    return SF.LiteralExpression(SyntaxKind.CharacterLiteralExpression, SF.Literal(value));
    //}

    public static T ToLiteral<T>(this T node) where T : SyntaxNode
    {
        return node;//CSharpSyntaxQuoter.Quote(node);
    }
}
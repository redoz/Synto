using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using SF = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;



static class LiteralSyntaxExtensions
{
    public static ExpressionSyntax ToSyntax(this string? value)
    {
        return value is null 
            ? SF.LiteralExpression(SyntaxKind.NullLiteralExpression) 
            : SF.LiteralExpression(SyntaxKind.StringLiteralExpression, SF.Literal(value));
    }

    public static ExpressionSyntax ToSyntax(this bool value)
    {
        return SF.LiteralExpression(value ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression);
    }
    public static ExpressionSyntax ToSyntax(this ulong value)
    {
        return SF.LiteralExpression(SyntaxKind.NumericLiteralExpression, SF.Literal(value));
    }

    public static ExpressionSyntax ToSyntax(this long value)
    {
        return SF.LiteralExpression(SyntaxKind.NumericLiteralExpression, SF.Literal(value));
    }

    public static ExpressionSyntax ToSyntax(this uint value)
    {
        return SF.LiteralExpression(SyntaxKind.NumericLiteralExpression, SF.Literal(value));
    }

    public static ExpressionSyntax ToSyntax(this int value)
    {
        return SF.LiteralExpression(SyntaxKind.NumericLiteralExpression, SF.Literal(value));
    }

    public static ExpressionSyntax ToSyntax(this ushort value)
    {
        return SF.LiteralExpression(SyntaxKind.NumericLiteralExpression, SF.Literal(value));
    }

    public static ExpressionSyntax ToSyntax(this short value)
    {
        return SF.LiteralExpression(SyntaxKind.NumericLiteralExpression, SF.Literal(value));
    }

    public static ExpressionSyntax ToSyntax(this byte value)
    {
        return SF.LiteralExpression(SyntaxKind.NumericLiteralExpression, SF.Literal(value));
    }

    public static ExpressionSyntax ToSyntax(this sbyte value)
    {
        return SF.LiteralExpression(SyntaxKind.NumericLiteralExpression, SF.Literal(value));
    }

    public static ExpressionSyntax ToSyntax(this decimal value)
    {
        return SF.LiteralExpression(SyntaxKind.NumericLiteralExpression, SF.Literal(value));
    }

    public static ExpressionSyntax ToSyntax(this float value)
    {
        return SF.LiteralExpression(SyntaxKind.NumericLiteralExpression, SF.Literal(value));
    }

    public static ExpressionSyntax ToSyntax(this double value)
    {
        return SF.LiteralExpression(SyntaxKind.NumericLiteralExpression, SF.Literal(value));
    }

    public static ExpressionSyntax ToSyntax(this char value)
    {
        return SF.LiteralExpression(SyntaxKind.CharacterLiteralExpression, SF.Literal(value));
    }

    public static ExpressionSyntax ToSyntax<T>(this T value)
    {
        throw new NotImplementedException("You can provide you own implementation by applying the RuntimeAttribute to a static class implementing a more explicit version of this signature.");
    }
}
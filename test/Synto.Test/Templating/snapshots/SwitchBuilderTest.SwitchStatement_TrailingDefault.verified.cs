#nullable enable
using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using SF = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

partial class Factory
{
    public static ClassDeclarationSyntax Reader(TypeSyntax TRet)
    {
        return ClassDeclaration(
                   List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                   TokenList(Token(PublicKeyword)), 
                   Token(ClassKeyword), 
                   Identifier("Reader"), 
                   null, 
                   null, 
                   null, 
                   List<TypeParameterConstraintClauseSyntax>(Array.Empty<TypeParameterConstraintClauseSyntax>()), 
                   Token(OpenBraceToken), 
                   List<MemberDeclarationSyntax>(
                       new MemberDeclarationSyntax[] { 
                           FieldDeclaration(
                               List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                               TokenList(
                                   Token(PrivateKeyword), 
                                   Token(ReadOnlyKeyword)), 
                               VariableDeclaration(
                                   GenericName(
                                       Identifier("IEnumerator"), 
                                       TypeArgumentList(
                                           Token(LessThanToken), 
                                           SeparatedList<TypeSyntax>(new SyntaxNodeOrToken[] { TRet }), 
                                           Token(GreaterThanToken))), 
                                   SeparatedList<VariableDeclaratorSyntax>(
                                       new SyntaxNodeOrToken[] { 
                                           VariableDeclarator(
                                               Identifier("_e"), 
                                               null, 
                                               null) })), 
                               Token(SemicolonToken)), 
                           MethodDeclaration(
                               List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                               TokenList(Token(PublicKeyword)), 
                               TRet, 
                               null, 
                               Identifier("GetTypedStmt"), 
                               null, 
                               ParameterList(
                                   Token(OpenParenToken), 
                                   SeparatedList<ParameterSyntax>(
                                       new SyntaxNodeOrToken[] { 
                                           Parameter(
                                               List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                                               TokenList(), 
                                               PredefinedType(Token(IntKeyword)), 
                                               Identifier("i"), 
                                               null) }), 
                                   Token(CloseParenToken)), 
                               List<TypeParameterConstraintClauseSyntax>(Array.Empty<TypeParameterConstraintClauseSyntax>()), 
                               Block(
                                   List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                                   Token(OpenBraceToken), 
                                   List<StatementSyntax>(
                                       new StatementSyntax[] { 
                                           global::Synto.Templating.SyntoBuilders.Switch(
                                               IdentifierName("i"), 
                                               global::Synto.Templating.SyntoBuilders.Case(
                                                   0.ToSyntax(), 
                                                   global::Synto.Templating.SyntoBuilders.Return(
                                                       global::Synto.Templating.SyntoBuilders.Member(
                                                           MemberAccessExpression(
                                                               SimpleMemberAccessExpression, 
                                                               IdentifierName("_e"), 
                                                               Token(DotToken), 
                                                               IdentifierName("Current")), 
                                                           "A"))), 
                                               global::Synto.Templating.SyntoBuilders.Default(
                                                   global::Synto.Templating.SyntoBuilders.Return(
                                                       global::Synto.Templating.SyntoBuilders.Member(
                                                           MemberAccessExpression(
                                                               SimpleMemberAccessExpression, 
                                                               IdentifierName("_e"), 
                                                               Token(DotToken), 
                                                               IdentifierName("Current")), 
                                                           "B")))), 
                                           ThrowStatement(
                                               List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                                               Token(ThrowKeyword), 
                                               ObjectCreationExpression(
                                                   Token(NewKeyword), 
                                                   QualifiedName(
                                                       IdentifierName("System"), 
                                                       Token(DotToken), 
                                                       IdentifierName("Exception")), 
                                                   ArgumentList(
                                                       Token(OpenParenToken), 
                                                       SeparatedList<ArgumentSyntax>(Array.Empty<SyntaxNodeOrToken>()), 
                                                       Token(CloseParenToken)), 
                                                   null), 
                                               Token(SemicolonToken)) }), 
                                   Token(CloseBraceToken)), 
                               null, 
                               Token(None)) }), 
                   Token(CloseBraceToken), 
                   Token(None));
    }
}

file static class LiteralSyntaxExtensions
{
    public static ExpressionSyntax ToSyntax(this string? value)
    {
        return value is null ? SF.LiteralExpression(SyntaxKind.NullLiteralExpression) : SF.LiteralExpression(
                                                                                            SyntaxKind.StringLiteralExpression, 
                                                                                            SF.Literal(value));
    }

    public static ExpressionSyntax ToSyntax(this bool value)
    {
        return SF.LiteralExpression(value ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression);
    }

    public static ExpressionSyntax ToSyntax(this ulong value)
    {
        return SF.LiteralExpression(
                   SyntaxKind.NumericLiteralExpression, 
                   SF.Literal(value));
    }

    public static ExpressionSyntax ToSyntax(this long value)
    {
        return SF.LiteralExpression(
                   SyntaxKind.NumericLiteralExpression, 
                   SF.Literal(value));
    }

    public static ExpressionSyntax ToSyntax(this uint value)
    {
        return SF.LiteralExpression(
                   SyntaxKind.NumericLiteralExpression, 
                   SF.Literal(value));
    }

    public static ExpressionSyntax ToSyntax(this int value)
    {
        return SF.LiteralExpression(
                   SyntaxKind.NumericLiteralExpression, 
                   SF.Literal(value));
    }

    public static ExpressionSyntax ToSyntax(this ushort value)
    {
        return SF.LiteralExpression(
                   SyntaxKind.NumericLiteralExpression, 
                   SF.Literal(value));
    }

    public static ExpressionSyntax ToSyntax(this short value)
    {
        return SF.LiteralExpression(
                   SyntaxKind.NumericLiteralExpression, 
                   SF.Literal(value));
    }

    public static ExpressionSyntax ToSyntax(this byte value)
    {
        return SF.LiteralExpression(
                   SyntaxKind.NumericLiteralExpression, 
                   SF.Literal(value));
    }

    public static ExpressionSyntax ToSyntax(this sbyte value)
    {
        return SF.LiteralExpression(
                   SyntaxKind.NumericLiteralExpression, 
                   SF.Literal(value));
    }

    public static ExpressionSyntax ToSyntax(this decimal value)
    {
        return SF.LiteralExpression(
                   SyntaxKind.NumericLiteralExpression, 
                   SF.Literal(value));
    }

    public static ExpressionSyntax ToSyntax(this float value)
    {
        // SF.Literal(float.NaN).Text is the bare identifier "NaN" — a numeric literal cannot carry a
        // non-finite value, so emit the well-known member access (float.NaN / float.PositiveInfinity /
        // float.NegativeInfinity) instead, which round-trips to legal C#.
        var member = NonFiniteMember(
                         float.IsNaN(value), 
                         float.IsPositiveInfinity(value), 
                         float.IsNegativeInfinity(value));
        return member is null ? SF.LiteralExpression(
                                    SyntaxKind.NumericLiteralExpression, 
                                    SF.Literal(value)) : NonFiniteMemberAccess(
                                                                                                                   SyntaxKind.FloatKeyword, 
                                                                                                                   member);
    }

    public static ExpressionSyntax ToSyntax(this double value)
    {
        var member = NonFiniteMember(
                         double.IsNaN(value), 
                         double.IsPositiveInfinity(value), 
                         double.IsNegativeInfinity(value));
        return member is null ? SF.LiteralExpression(
                                    SyntaxKind.NumericLiteralExpression, 
                                    SF.Literal(value)) : NonFiniteMemberAccess(
                                                                                                                   SyntaxKind.DoubleKeyword, 
                                                                                                                   member);
    }

    private static string? NonFiniteMember(bool isNaN, bool isPositiveInfinity, bool isNegativeInfinity) => isNaN ? "NaN" : isPositiveInfinity ? "PositiveInfinity" : isNegativeInfinity ? "NegativeInfinity" : null;
    private static ExpressionSyntax NonFiniteMemberAccess(SyntaxKind keyword, string member) => SF.MemberAccessExpression(
                                                                                                    SyntaxKind.SimpleMemberAccessExpression, 
                                                                                                    SF.PredefinedType(SF.Token(keyword)), 
                                                                                                    SF.IdentifierName(member));
    public static ExpressionSyntax ToSyntax(this char value)
    {
        return SF.LiteralExpression(
                   SyntaxKind.CharacterLiteralExpression, 
                   SF.Literal(value));
    }

    public static ExpressionSyntax ToSyntax<T>(this T value)
    {
        return value switch
        {
            null => SF.LiteralExpression(SyntaxKind.NullLiteralExpression),
            string @string => @string.ToSyntax(),
            bool literal => literal.ToSyntax(),
            ulong literal => literal.ToSyntax(),
            long literal => literal.ToSyntax(),
            uint literal => literal.ToSyntax(),
            int literal => literal.ToSyntax(),
            ushort literal => literal.ToSyntax(),
            short literal => literal.ToSyntax(),
            byte literal => literal.ToSyntax(),
            sbyte literal => literal.ToSyntax(),
            decimal literal => literal.ToSyntax(),
            float literal => literal.ToSyntax(),
            double literal => literal.ToSyntax(),
            char literal => literal.ToSyntax(),
            Enum @enum => @enum.ToSyntax(),
            _ => throw new NotImplementedException($"You can provide you own implementation by applying the RuntimeAttribute to a static class implementing a more explicit version of this signature for type {value.GetType().FullName}.")};
    }

    /// <summary>
    /// Lifts an enum value to a constant <see cref = "ExpressionSyntax"/>: a qualified enum-member access
    /// (<c>global::Ns.Color.Red</c>) when the value maps to a single named member, otherwise a cast of the
    /// underlying integral literal (<c>(global::Ns.Color)3</c>) for an undefined value or a <c>[Flags]</c>
    /// combination. Nested enum types resolve with <c>.</c> (the reflected <c>+</c> is normalized).
    /// </summary>
    public static ExpressionSyntax ToSyntax(this Enum value)
    {
        Type enumType = value.GetType();
        string qualifiedName = "global::" + enumType.FullName!.Replace(
                                                '+', 
                                                '.');
        if (Enum.GetName(
                enumType, 
                value)is { } name)
        {
            return SF.MemberAccessExpression(
                       SyntaxKind.SimpleMemberAccessExpression, 
                       SF.ParseName(qualifiedName), 
                       SF.IdentifierName(name));
        }

        // Undefined value or a flags combination: cast the underlying integral literal to the enum type.
        object underlying = Convert.ChangeType(
                                value, 
                                Enum.GetUnderlyingType(enumType), 
                                System.Globalization.CultureInfo.InvariantCulture);
        return SF.CastExpression(
                   SF.ParseTypeName(qualifiedName), 
                   underlying.ToSyntax());
    }
}
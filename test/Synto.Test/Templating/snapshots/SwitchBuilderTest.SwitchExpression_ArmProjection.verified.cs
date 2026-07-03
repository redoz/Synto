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
    public static ClassDeclarationSyntax Reader(TypeSyntax TRet, global::System.Collections.Generic.IReadOnlyList<global::Col> cols)
    {
        ExpressionSyntax syntaxForParam_cols = cols.ToSyntax();
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
                               Identifier("GetTypedExpr"), 
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
                                           LocalDeclarationStatement(
                                               List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                                               Token(None), 
                                               Token(None), 
                                               TokenList(), 
                                               VariableDeclaration(
                                                   IdentifierName("var"), 
                                                   SeparatedList<VariableDeclaratorSyntax>(
                                                       new SyntaxNodeOrToken[] { 
                                                           VariableDeclarator(
                                                               Identifier("built"), 
                                                               null, 
                                                               EqualsValueClause(
                                                                   Token(EqualsToken), 
                                                                   global::Synto.Templating.SyntoBuilders.Switch(
                                                                       IdentifierName("i"), 
                                                                       cols.Where(c => c.Ordinal >= 0).Select(
                                                                           c => global::Synto.Templating.SyntoBuilders.Arm(
                                                                               LiteralExpression(
                                                                                   NumericLiteralExpression, 
                                                                                   Literal(c.Ordinal)), 
                                                                               global::Synto.Templating.SyntoBuilders.Member(
                                                                                   MemberAccessExpression(
                                                                                       SimpleMemberAccessExpression, 
                                                                                       IdentifierName("_e"), 
                                                                                       Token(DotToken), 
                                                                                       IdentifierName("Current")), 
                                                                                   c.Name)))))) })), 
                                               Token(SemicolonToken)), 
                                           ExpressionStatement(
                                               List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                                               InvocationExpression(
                                                   MemberAccessExpression(
                                                       SimpleMemberAccessExpression, 
                                                       MemberAccessExpression(
                                                           SimpleMemberAccessExpression, 
                                                           IdentifierName("System"), 
                                                           Token(DotToken), 
                                                           IdentifierName("Console")), 
                                                       Token(DotToken), 
                                                       IdentifierName("WriteLine")), 
                                                   ArgumentList(
                                                       Token(OpenParenToken), 
                                                       SeparatedList<ArgumentSyntax>(
                                                           new SyntaxNodeOrToken[] { 
                                                               Argument(
                                                                   null, 
                                                                   Token(None), 
                                                                   IdentifierName("built")) }), 
                                                       Token(CloseParenToken))), 
                                               Token(SemicolonToken)), 
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
        return SF.LiteralExpression(
                   SyntaxKind.NumericLiteralExpression, 
                   SF.Literal(value));
    }

    public static ExpressionSyntax ToSyntax(this double value)
    {
        return SF.LiteralExpression(
                   SyntaxKind.NumericLiteralExpression, 
                   SF.Literal(value));
    }

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
            _ => throw new NotImplementedException($"You can provide you own implementation by applying the RuntimeAttribute to a static class implementing a more explicit version of this signature for type {value.GetType().FullName}.")};
    }
}
//HintName: Factory.TypedGetter.g.cs
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
    public static MethodDeclarationSyntax TypedGetter(TypeSyntax TRet, global::System.Collections.Generic.IReadOnlyList<global::Col> columns, string clrType, string typeLabel)
    {
        ExpressionSyntax syntaxForParam_typeLabel = typeLabel.ToSyntax();
        var __run_0 = new global::System.Collections.Generic.List<global::Microsoft.CodeAnalysis.CSharp.Syntax.StatementSyntax>();
        foreach (var c in columns.Where(c => c.ClrType == clrType))
        {
            __run_0.Add(
            IfStatement(
            List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
            Token(IfKeyword), 
            Token(OpenParenToken), 
            BinaryExpression(
            EqualsExpression, 
            IdentifierName("i"), 
            Token(EqualsEqualsToken), 
            c.Ordinal.ToSyntax()), 
            Token(CloseParenToken), 
            ReturnStatement(
            List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
            Token(ReturnKeyword), 
            global::Synto.Templating.SyntoBuilders.Member(
            MemberAccessExpression(
            SimpleMemberAccessExpression, 
            IdentifierName("_e"), 
            Token(DotToken), 
            IdentifierName("Current")), 
            c.Name), 
            Token(SemicolonToken)), 
            null));
        }

        return MethodDeclaration(
                   List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                   TokenList(Token(PublicKeyword)), 
                   TRet, 
                   null, 
                   Identifier("TypedGetter"), 
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
                       CollectionSyntaxExtensions.BuildList<StatementSyntax>(
                           CollectionSyntaxExtensions.ListSegment<StatementSyntax>.Run(__run_0), 
                           ThrowStatement(
                               List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                               Token(ThrowKeyword), 
                               ObjectCreationExpression(
                                   Token(NewKeyword), 
                                   QualifiedName(
                                       AliasQualifiedName(
                                           IdentifierName("global"), 
                                           Token(ColonColonToken), 
                                           IdentifierName("System")), 
                                       Token(DotToken), 
                                       IdentifierName("InvalidCastException")), 
                                   ArgumentList(
                                       Token(OpenParenToken), 
                                       SeparatedList<ArgumentSyntax>(
                                           new SyntaxNodeOrToken[] { 
                                               Argument(
                                                   null, 
                                                   Token(None), 
                                                   InterpolatedStringExpression(
                                                       Token(InterpolatedStringStartToken), 
                                                       List<InterpolatedStringContentSyntax>(
                                                           new InterpolatedStringContentSyntax[] { 
                                                               InterpolatedStringText(
                                                                   Token(
                                                                       TriviaList(), 
                                                                       InterpolatedStringTextToken, 
                                                                       "Field ", 
                                                                       "Field ", 
                                                                       TriviaList())), 
                                                               Interpolation(
                                                                   Token(OpenBraceToken), 
                                                                   IdentifierName("i"), 
                                                                   null, 
                                                                   null, 
                                                                   Token(CloseBraceToken)), 
                                                               InterpolatedStringText(
                                                                   Token(
                                                                       TriviaList(), 
                                                                       InterpolatedStringTextToken, 
                                                                       " is not ", 
                                                                       " is not ", 
                                                                       TriviaList())), 
                                                               Interpolation(
                                                                   Token(OpenBraceToken), 
                                                                   syntaxForParam_typeLabel, 
                                                                   null, 
                                                                   null, 
                                                                   Token(CloseBraceToken)), 
                                                               InterpolatedStringText(
                                                                   Token(
                                                                       TriviaList(), 
                                                                       InterpolatedStringTextToken, 
                                                                       " column.", 
                                                                       " column.", 
                                                                       TriviaList())) }), 
                                                       Token(InterpolatedStringEndToken))) }), 
                                       Token(CloseParenToken)), 
                                   null), 
                               Token(SemicolonToken)))), 
                   null, 
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

/// <summary>
/// File-local collection helper (plan Task 5 / spec §5.3 / §6) emitted into a generated factory by the
/// scan-based injection (keyed on <c>BuildList</c>). It assembles a <see cref = "SyntaxList{TNode}"/> from a
/// mix of <em>fixed</em> nodes (quoted siblings of a live region) and <em>node runs</em>
/// (<see cref = "IEnumerable{T}"/> sequences produced by an unrolled live region), in slot order — the
/// incorporation logic the staging emitter (Tasks 6–7) targets.
/// </summary>
/// <remarks>
/// Authored once <c>public</c> in <c>src\Synto</c>; embedded under <c>Synto.Helper.*</c> and emitted
/// <c>file static</c> by <see cref = "FileLocalHelpers"/> so the injected copy can never collide with
/// <c>Synto.Core</c>'s public copy and the generated output carries zero <c>Synto.*</c> dependency. The
/// <see cref = "SeparatedSyntaxList{TNode}"/> (separator-interleaving) counterpart is a deliberately logged
/// later-cut item (the in-scope dog-food stays in non-separated statement lists).
/// </remarks>
file static class CollectionSyntaxExtensions
{
    /// <summary>
    /// Builds a <see cref = "SyntaxList{TNode}"/> by concatenating each <paramref name = "segments"/> entry in
    /// order. A segment is either a single fixed node (via the implicit conversion) or a run of nodes (via
    /// <see cref = "ListSegment{TNode}.Run"/>).
    /// </summary>
    public static SyntaxList<TNode> BuildList<TNode>(params ListSegment<TNode>[] segments)
        where TNode : SyntaxNode
    {
        var nodes = new List<TNode>();
        foreach (var segment in segments)
            segment.AppendTo(nodes);
        return SF.List(nodes);
    }

    /// <summary>
    /// One slot in a <see cref = "BuildList{TNode}"/> call: either a single fixed node (implicitly converted
    /// from a <typeparamref name = "TNode"/>) or a run of nodes (<see cref = "Run"/>).
    /// </summary>
    public readonly struct ListSegment<TNode>
        where TNode : SyntaxNode
    {
        private readonly TNode? _single;
        private readonly IEnumerable<TNode>? _run;
        private ListSegment(TNode? single, IEnumerable<TNode>? run)
        {
            _single = single;
            _run = run;
        }

        /// <summary>A fixed single node becomes a one-element segment.</summary>
        public static implicit operator ListSegment<TNode>(TNode node) => new(node, null);
        /// <summary>A run of nodes (e.g. an unrolled live region's collected islands) in slot order.</summary>
        public static ListSegment<TNode> Run(IEnumerable<TNode> nodes) => new(null, nodes);
        internal void AppendTo(List<TNode> target)
        {
            if (_run is not null)
                target.AddRange(_run);
            else if (_single is not null)
                target.Add(_single);
        }
    }
}
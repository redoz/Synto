//HintName: Factory.Build.g.cs
#nullable enable
using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp;
using SF = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

partial class Factory
{
    public static MethodDeclarationSyntax Build(global::System.Collections.Generic.IReadOnlyList<global::Col> columns)
    {
        int sum = 0;
        var __run_0 = new global::System.Collections.Generic.List<global::Microsoft.CodeAnalysis.CSharp.Syntax.StatementSyntax>();
        foreach (var c in columns)
        {
            __run_0.Add(
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
            sum.ToSyntax()) }), 
            Token(CloseParenToken))), 
            Token(SemicolonToken)));
            sum += c.Ordinal;
        }

        return MethodDeclaration(
                   List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                   TokenList(), 
                   PredefinedType(Token(VoidKeyword)), 
                   null, 
                   Identifier("Build"), 
                   null, 
                   ParameterList(
                       Token(OpenParenToken), 
                       SeparatedList<ParameterSyntax>(Array.Empty<SyntaxNodeOrToken>()), 
                       Token(CloseParenToken)), 
                   List<TypeParameterConstraintClauseSyntax>(Array.Empty<TypeParameterConstraintClauseSyntax>()), 
                   Block(CollectionSyntaxExtensions.BuildList<StatementSyntax>(CollectionSyntaxExtensions.ListSegment<StatementSyntax>.Run(__run_0))), 
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
    private static MemberAccessExpressionSyntax NonFiniteMemberAccess(SyntaxKind keyword, string member) => SF.MemberAccessExpression(
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
        if (value is null)
            throw new ArgumentNullException(nameof(value));
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
        if (segments is null)
            throw new ArgumentNullException(nameof(segments));
        var nodes = new List<TNode>();
        foreach (var segment in segments)
            segment.AppendTo(nodes);
        return SF.List(nodes);
    }

    /// <summary>
    /// One slot in a <see cref = "BuildList{TNode}"/> call: either a single fixed node (implicitly converted
    /// from a <typeparamref name = "TNode"/>) or a run of nodes (<see cref = "Run"/>).
    /// </summary>
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "Authored public only so it can be embedded and emitted as a 'file static' helper into generated output (see the type's XML remarks); consumers never see it as public API, so the nested-visibility API-design rule does not apply.")]
    public readonly struct ListSegment<TNode> : IEquatable<ListSegment<TNode>> where TNode : SyntaxNode
    {
        private readonly TNode? _single;
        private readonly IEnumerable<TNode>? _run;
        private ListSegment(TNode? single, IEnumerable<TNode>? run)
        {
            _single = single;
            _run = run;
        }

        /// <summary>A fixed single node becomes a one-element segment.</summary>
        [SuppressMessage("Usage", "CA2225:Operator overloads have named alternates", Justification = "The implicit conversion is the embedded segment-construction spelling on generated (embed-as-file-local) surface; a named alternate would be unwanted noise there.")]
        public static implicit operator ListSegment<TNode>(TNode node) => new(node, null);
        /// <summary>A run of nodes (e.g. an unrolled live region's collected islands) in slot order.</summary>
        [SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "This type is embedded and emitted as a 'file static' helper into generated output (see the type's XML remarks); it is not consumed as public API, so the static-members-on-generic-types API-design rule does not apply.")]
        public static ListSegment<TNode> Run(IEnumerable<TNode> nodes) => new(null, nodes);
        /// <summary>Segments are equal when they carry the same single-node / run references.</summary>
        public bool Equals(ListSegment<TNode> other) => ReferenceEquals(
                                                            _single, 
                                                            other._single) && ReferenceEquals(
                                                                                                       _run, 
                                                                                                       other._run);
        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is ListSegment<TNode> other && Equals(other);
        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + (_single is null ? 0 : RuntimeHelpers.GetHashCode(_single));
                hash = (hash * 31) + (_run is null ? 0 : RuntimeHelpers.GetHashCode(_run));
                return hash;
            }
        }

        /// <summary>Reference-equality comparison of two segments.</summary>
        public static bool operator ==(ListSegment<TNode> left, ListSegment<TNode> right) => left.Equals(right);
        /// <summary>Reference-inequality comparison of two segments.</summary>
        public static bool operator !=(ListSegment<TNode> left, ListSegment<TNode> right) => !left.Equals(right);
        internal void AppendTo(List<TNode> target)
        {
            if (_run is not null)
                target.AddRange(_run);
            else if (_single is not null)
                target.Add(_single);
        }
    }
}
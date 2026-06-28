//HintName: Factory.Reader.g.cs
#nullable enable
using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;
using System.Collections.Generic;
using SF = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

partial class Factory
{
    public static ClassDeclarationSyntax Reader(global::System.Collections.Generic.IReadOnlyList<global::Col> columns)
    {
        global::System.Collections.Generic.IEnumerable<global::Microsoft.CodeAnalysis.CSharp.Syntax.MemberDeclarationSyntax> __spliceGenerator_Getters_0()
        {
            yield return Factory.TypedGetter(
                         PredefinedType(Token(IntKeyword)), 
                         columns, 
                         "System.Int32", 
                         "an Int32").WithIdentifier(Identifier("GetInt32"));
            yield return Factory.TypedGetter(
                         PredefinedType(Token(StringKeyword)), 
                         columns, 
                         "System.String", 
                         "a String").WithIdentifier(Identifier("GetString"));
        }

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
                   CollectionSyntaxExtensions.BuildList<MemberDeclarationSyntax>(
                       FieldDeclaration(
                           List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                           TokenList(
                               Token(PrivateKeyword), 
                               Token(ReadOnlyKeyword)), 
                           VariableDeclaration(
                               QualifiedName(
                                   QualifiedName(
                                       IdentifierName("System"), 
                                       Token(DotToken), 
                                       IdentifierName("Collections")), 
                                   Token(DotToken), 
                                   IdentifierName("IEnumerator")), 
                               SeparatedList<VariableDeclaratorSyntax>(
                                   new SyntaxNodeOrToken[] { 
                                       VariableDeclarator(
                                           Identifier("_e"), 
                                           null, 
                                           EqualsValueClause(
                                               Token(EqualsToken), 
                                               PostfixUnaryExpression(
                                                   SuppressNullableWarningExpression, 
                                                   LiteralExpression(
                                                       DefaultLiteralExpression, 
                                                       Token(DefaultKeyword)), 
                                                   Token(ExclamationToken)))) })), 
                           Token(SemicolonToken)), 
                       CollectionSyntaxExtensions.ListSegment<MemberDeclarationSyntax>.Run(__spliceGenerator_Getters_0())), 
                   Token(CloseBraceToken), 
                   Token(None));
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
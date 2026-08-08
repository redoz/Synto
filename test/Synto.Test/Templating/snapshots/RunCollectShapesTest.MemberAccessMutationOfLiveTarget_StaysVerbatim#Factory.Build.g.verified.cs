//HintName: Factory.Build.g.cs
#nullable enable
using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using SF = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

partial class Factory
{
    public static MethodDeclarationSyntax Build(global::System.Collections.Generic.IReadOnlyList<global::Col> columns, global::Acc acc)
    {
        var __run_0 = new global::System.Collections.Generic.List<global::Microsoft.CodeAnalysis.CSharp.Syntax.StatementSyntax>();
        foreach (var c in columns)
        {
            acc.Total += c.Ordinal;
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
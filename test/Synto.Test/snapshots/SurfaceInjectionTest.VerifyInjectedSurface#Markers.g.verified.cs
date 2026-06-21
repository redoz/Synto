//HintName: Markers.g.cs
#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto.Matching;

/// <summary>
/// Statement-capture quantifier holder: a <c>[Capture] Stmt</c> pattern parameter whose verb selects how
/// many statements the hole consumes and the captured shape. The verbs are never executed — they are
/// recognized structurally at generation time; their return types document the captured member type.
/// </summary>
internal sealed class Stmt
{
    /// <summary>Captures exactly one statement.</summary>
    public StatementSyntax One() => null!;

    /// <summary>Captures zero or one statement.</summary>
    public StatementSyntax? Opt() => null;

    /// <summary>Captures one or more statements (a greedy run).</summary>
    public SyntaxList<StatementSyntax> Some() => default;

    /// <summary>Captures zero or more statements (a greedy run).</summary>
    public SyntaxList<StatementSyntax> All() => default;

    /// <summary>Captures exactly <paramref name="n"/> statements.</summary>
    public SyntaxList<StatementSyntax> Exactly(int n) => default;
}

/// <summary>
/// Statement wildcard: the same quantifier verbs as <see cref="Stmt"/> but matching statements without
/// capturing them. Spelled statically because a wildcard binds to no pattern parameter.
/// </summary>
internal static class Statement
{
    /// <summary>Matches exactly one statement.</summary>
    public static void One() { }

    /// <summary>Matches zero or one statement.</summary>
    public static void Opt() { }

    /// <summary>Matches one or more statements (a greedy run).</summary>
    public static void Some() { }

    /// <summary>Matches zero or more statements (a greedy run).</summary>
    public static void All() { }

    /// <summary>Matches exactly <paramref name="n"/> statements.</summary>
    public static void Exactly(int n) { }
}

/// <summary>
/// Expression wildcard: matches any <see cref="ExpressionSyntax"/> in expression position without capturing
/// it. <see cref="Any{T}"/> is the sole wildcard spelling; <typeparamref name="T"/> only satisfies the host
/// expression's static type and is otherwise ignored.
/// </summary>
internal static class Expr
{
    /// <summary>Matches any expression, typed as <typeparamref name="T"/> for the host position.</summary>
    public static T Any<T>() => default!;
}

/// <summary>
/// Block anchors: pin a statement run to the first (<see cref="Start"/>) or last (<see cref="End"/>) edge of
/// the candidate block. Used by <c>Bare</c>/<c>Single</c> patterns; never executed.
/// </summary>
internal static class Block
{
    /// <summary>Anchors the run to the first statement of the block.</summary>
    public static void Start() { }

    /// <summary>Anchors the run to the last statement of the block.</summary>
    public static void End() { }
}

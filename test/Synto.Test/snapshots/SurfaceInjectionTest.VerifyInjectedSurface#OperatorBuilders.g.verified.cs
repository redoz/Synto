//HintName: OperatorBuilders.g.cs
#nullable enable
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;

namespace Synto.Templating;

/// <summary>
/// The factory-time emit builders for C#'s value-producing keyword operators (<c>typeof</c>/<c>nameof</c>/
/// <c>sizeof</c>). Each is an ordinary <c>[SyntaxBuilder]</c>: it runs at factory-build time and returns the
/// operator expression. Their inert value-position facades are synthesized into <see cref="Operators"/> (a
/// DIFFERENT type — a same-signature facade here would be CS0111), which authors import via
/// <c>using static …Operators</c> and call by bare name. Injected <c>internal</c> into the consumer's
/// generator compilation by <c>SurfaceInjectionGenerator</c>; authors never import THIS type.
/// </summary>
internal static partial class OperatorBuilders
{
    /// <summary>Builds <c>typeof(name)</c> from a factory-time type name.</summary>
    [SyntaxBuilder(Returns = typeof(System.Type), Facade = typeof(Operators))]
    internal static ExpressionSyntax TypeOf(string name) => TypeOfExpression(ParseTypeName(name));

    /// <summary>Builds <c>nameof(name)</c> from a factory-time name expression.</summary>
    [SyntaxBuilder(Returns = typeof(string), Facade = typeof(Operators))]
    internal static ExpressionSyntax NameOf(string name) =>
        InvocationExpression(IdentifierName("nameof"),
            ArgumentList(SingletonSeparatedList(Argument(ParseExpression(name)))));

    /// <summary>Builds <c>sizeof(name)</c> from a factory-time type name.</summary>
    [SyntaxBuilder(Returns = typeof(int), Facade = typeof(Operators))]
    internal static ExpressionSyntax SizeOf(string name) => SizeOfExpression(ParseTypeName(name));

    /// <summary>Builds a <c>left ?? right</c> null-coalescing expression from two quoted operand islands.
    /// Fixed <c>ExpressionSyntax</c> facade (into <see cref="Operators"/>) so a bare, type-argument-free
    /// <c>Coalesce(...)</c> composes inside an island; the operands expand nested builders (<c>Member</c>,
    /// <c>Throw</c>) via re-entry. A fresh-<c>TResult</c> facade (<c>Coalesce&lt;TResult&gt;</c>) couldn't infer
    /// <c>TResult</c> and would force an explicit type argument — the fixed facade is the whole point.</summary>
    [SyntaxBuilder(Returns = typeof(ExpressionSyntax), Facade = typeof(Operators))]
    internal static ExpressionSyntax Coalesce([Quoted] ExpressionSyntax left, [Quoted] ExpressionSyntax right) =>
        BinaryExpression(CoalesceExpression, left, right);
}

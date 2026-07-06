//HintName: OperatorBuilders.g.cs
#nullable enable
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

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
}

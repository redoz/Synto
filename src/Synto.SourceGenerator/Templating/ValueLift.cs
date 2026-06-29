using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto;

/// <summary>
/// The shared value→syntax lift policy used by the <c>[Unquote]</c> value-parameter path and the <c>[Quote]</c>
/// paths (parameter + inline <c>Quote(value)</c>). ONE instance per <c>CreateSyntaxFactoryMethod</c> invocation
/// (locked decision 4): the <c>[Runtime]</c> converter-class cache is lazy (resolved on the first concrete
/// non-built-in type encountered) and shared across all three lift sites. SY1011/SY1012 fire at identical
/// locations. All work is semantic and done inside the transform; nothing is captured into pipeline state.
/// </summary>
internal sealed class ValueLift
{
    private readonly SemanticModel _semanticModel;
    private readonly INamedTypeSymbol? _runtimeAttribute;
    private readonly INamedTypeSymbol? _expressionSyntaxSymbol;

    // Lazily-resolved [Runtime] converter classes — walked only on the first concrete non-built-in type, then
    // shared across every later lift site of this invocation.
    private ImmutableArray<INamedTypeSymbol>? _runtimeClasses;

    public ValueLift(SemanticModel semanticModel, INamedTypeSymbol? runtimeAttribute, INamedTypeSymbol? expressionSyntaxSymbol)
    {
        _semanticModel = semanticModel;
        _runtimeAttribute = runtimeAttribute;
        _expressionSyntaxSymbol = expressionSyntaxSymbol;
    }

    /// <summary>
    /// The built-in literal types <see cref="LiteralSyntaxExtensions"/> handles directly. An inlined parameter
    /// of one of these binds to a specific <c>ToSyntax(this T)</c> overload (injected via the method-name
    /// scan); any other concrete type needs a user-provided <c>[Runtime]</c> converter.
    /// </summary>
    public static bool IsBuiltInLiteralType(ITypeSymbol type) =>
        type.SpecialType switch
        {
            SpecialType.System_String
                or SpecialType.System_Boolean
                or SpecialType.System_Char
                or SpecialType.System_SByte
                or SpecialType.System_Byte
                or SpecialType.System_Int16
                or SpecialType.System_UInt16
                or SpecialType.System_Int32
                or SpecialType.System_UInt32
                or SpecialType.System_Int64
                or SpecialType.System_UInt64
                or SpecialType.System_Decimal
                or SpecialType.System_Single
                or SpecialType.System_Double => true,
            _ => false,
        };

    /// <summary>
    /// Produces the factory-time conversion expression that lifts <paramref name="valueAccess"/> to
    /// <c>ExpressionSyntax</c>: <c>valueAccess.ToSyntax()</c> for a built-in literal type (binds to the file-local
    /// <c>LiteralSyntaxExtensions</c> / generic <c>ToSyntax&lt;T&gt;</c> fallback, also used for an inlined generic
    /// type parameter), or a fully-qualified <c>global::Ns.Converter.ToSyntax(valueAccess)</c> call for a concrete
    /// non-built-in type carrying exactly one <c>[Runtime]</c> converter. A concrete type with no converter yields
    /// <c>SY1011</c>; with more than one yields <c>SY1012</c> — returned via <paramref name="diagnostic"/> with a
    /// <c>false</c> result so the caller can bail. The generic-type-parameter <c>typeof(T).ToTypeSyntax()</c> lift
    /// is <c>[Unquote]</c>-only and stays out of this helper (<c>[Quote]</c> is value-axis only).
    /// </summary>
    public bool TryEmitValueLift(
        ITypeSymbol valueType,
        ExpressionSyntax valueAccess,
        Location? diagnosticLocation,
        out ExpressionSyntax liftedSyntax,
        out DiagnosticInfo? diagnostic)
    {
        diagnostic = null;

        // Built-in literal type (or an inlined generic type parameter): value.ToSyntax(), binding to the
        // file-local LiteralSyntaxExtensions / generic ToSyntax<T> fallback.
        if (valueType is ITypeParameterSymbol || IsBuiltInLiteralType(valueType))
        {
            liftedSyntax = InvocationExpression(
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    valueAccess,
                    IdentifierName("ToSyntax")));
            return true;
        }

        // A concrete, non-built-in type needs a user-provided [Runtime] converter exposing
        // ToSyntax(this ThatType). It is discovered from the TYPE; with no converter (or more than one) a
        // diagnostic is emitted at generation time, rather than letting the generic ToSyntax<T> fallback throw
        // NotImplementedException at the author's runtime.
        _runtimeClasses ??= RuntimeConverterFinder.FindRuntimeClasses(_semanticModel.Compilation, _runtimeAttribute);
        var converters = RuntimeConverterFinder.FindConvertersFor(_runtimeClasses.Value, valueType, _expressionSyntaxSymbol);

        if (converters.Length == 0)
        {
            diagnostic = TemplateDiagnostics.NoRuntimeConverter(diagnosticLocation, valueType.ToDisplayString());
            liftedSyntax = valueAccess;
            return false;
        }

        if (converters.Length > 1)
        {
            diagnostic = TemplateDiagnostics.AmbiguousRuntimeConverter(diagnosticLocation, valueType.ToDisplayString(), converters.Length);
            liftedSyntax = valueAccess;
            return false;
        }

        // Call the user's converter DIRECTLY by its fully-qualified name as a static method —
        // `global::Ns.Converter.ToSyntax(value)`. A fully-qualified static call binds to exactly the discovered
        // converter, needs no `using`, and keeps the generated output free of any Synto runtime dependency.
        var converter = converters[0];
        liftedSyntax = InvocationExpression(
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    ParseName(converter.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)),
                    IdentifierName("ToSyntax")))
            .AddArgumentListArguments(Argument(valueAccess));
        return true;
    }
}

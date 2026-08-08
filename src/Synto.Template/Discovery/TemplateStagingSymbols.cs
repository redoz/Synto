using System.Linq;
using Microsoft.CodeAnalysis;

namespace Synto.Generators.Templating.Discovery;

/// <summary>
/// Resolves the two <c>Template.Unquote&lt;T&gt;</c> overloads by their bound overload identity so recognition can
/// discriminate the two live behaviors that share the <c>Unquote</c> name (authoring-surface taxonomy §3.1):
/// <list type="bullet">
/// <item><b>parameter-source</b> — <c>Unquote&lt;T&gt;(string? parameterName = null)</c>: a caller-supplied live
/// root declared in the body that folds to a factory parameter.</item>
/// <item><b>value-source</b> — <c>Unquote&lt;T&gt;(T value)</c>: a factory-build-time local.</item>
/// </list>
/// Because both are named <c>Unquote</c>, callers must compare a bound call's
/// <see cref="ISymbol.OriginalDefinition"/> against these resolved overloads — NEVER by method name alone. The two
/// original definitions are told apart structurally: the parameter-source overload's single parameter is a
/// <c>string</c>, the value-source overload's is the type parameter <c>T</c>.
/// </summary>
internal static class TemplateStagingSymbols
{
    /// <summary>The value-source <c>Unquote&lt;T&gt;(T value)</c> overload's original definition, or <c>null</c>.</summary>
    public static IMethodSymbol? ValueSourceUnquote(INamedTypeSymbol templateSymbol) =>
        templateSymbol.GetMembers(nameof(global::Synto.Templating.Template.Unquote))
            .OfType<IMethodSymbol>()
            .FirstOrDefault(m => m.TypeParameters.Length == 1
                && m.Parameters.Length == 1
                && SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, m.TypeParameters[0]));

    /// <summary>The parameter-source <c>Unquote&lt;T&gt;(string? parameterName = null)</c> overload's original definition, or <c>null</c>.</summary>
    public static IMethodSymbol? ParameterSourceUnquote(INamedTypeSymbol templateSymbol) =>
        templateSymbol.GetMembers(nameof(global::Synto.Templating.Template.Unquote))
            .OfType<IMethodSymbol>()
            .FirstOrDefault(m => m.TypeParameters.Length == 1
                && m.Parameters.Length == 1
                && m.Parameters[0].Type.SpecialType == SpecialType.System_String);

    /// <summary>
    /// True when <paramref name="method"/> binds the parameter-source overload identified by
    /// <paramref name="parameterSourceUnquote"/> (a live-root call that folds to a factory parameter).
    /// </summary>
    public static bool IsParameterSourceUnquote(IMethodSymbol method, IMethodSymbol? parameterSourceUnquote) =>
        parameterSourceUnquote is not null
        && SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, parameterSourceUnquote);

    /// <summary>
    /// True when <paramref name="method"/> binds the value-source overload identified by
    /// <paramref name="valueSourceUnquote"/> (a factory-build-time local).
    /// </summary>
    public static bool IsValueSourceUnquote(IMethodSymbol method, IMethodSymbol? valueSourceUnquote) =>
        valueSourceUnquote is not null
        && SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, valueSourceUnquote);
}

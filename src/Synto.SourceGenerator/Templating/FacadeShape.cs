using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto;

/// <summary>One builder parameter as seen by facade derivation (binding + facade element).</summary>
internal sealed class FacadeParam
{
    public FacadeParam(string parameterName, BuilderArgKind kind, string? genericParameterName, string? valueTypeDisplay)
    {
        ParameterName = parameterName;
        Kind = kind;
        GenericParameterName = genericParameterName;
        ValueTypeDisplay = valueTypeDisplay;
    }

    public string ParameterName { get; }
    public BuilderArgKind Kind { get; }

    /// <summary>The synthesized facade generic type-parameter name (for <see cref="BuilderArgKind.QuotedTypeArg"/>).</summary>
    public string? GenericParameterName { get; }

    /// <summary>The synthesized facade value-parameter type (for <see cref="BuilderArgKind.Quoted"/>/<see cref="BuilderArgKind.Staged"/>).</summary>
    public string? ValueTypeDisplay { get; }
}

/// <summary>
/// The mechanical facade-derivation rule (Locked Names §5): given a <c>[SyntaxBuilder]</c> method, derive the
/// inert carrier-callable facade Synto synthesizes for it. Shared by <see cref="SyntaxBuilderFinder"/> (to map
/// a facade call's arguments back onto builder parameters) and by the facade-synthesis generator (to emit the
/// facade declaration). Pure analysis — no pipeline capture.
/// </summary>
internal sealed class FacadeShape
{
    private FacadeShape(
        string methodName,
        IReadOnlyList<FacadeParam> parameters,
        IReadOnlyList<string> genericParameters,
        bool freshReturnTypeParam,
        string returnTypeDisplay)
    {
        MethodName = methodName;
        Parameters = parameters;
        GenericParameters = genericParameters;
        FreshReturnTypeParam = freshReturnTypeParam;
        ReturnTypeDisplay = returnTypeDisplay;
    }

    public string MethodName { get; }

    /// <summary>Builder parameters in declared order, each carrying its binding + facade element.</summary>
    public IReadOnlyList<FacadeParam> Parameters { get; }

    /// <summary>The synthesized facade's generic type parameters, in facade order (a leading fresh TResult first when <see cref="FreshReturnTypeParam"/>).</summary>
    public IReadOnlyList<string> GenericParameters { get; }

    /// <summary>True when the facade gains a fresh leading <c>TResult</c> return type parameter (builder returns ExpressionSyntax with no [ReturnType] param).</summary>
    public bool FreshReturnTypeParam { get; }

    /// <summary>The synthesized facade's return type.</summary>
    public string ReturnTypeDisplay { get; }

    private const string FreshReturnTypeParamName = "TResult";

    public static FacadeShape? Derive(IMethodSymbol builder, out string? annotationError, out string? returnShapeError)
    {
        annotationError = null;
        returnShapeError = null;

        string returnDisplay = builder.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        bool returnsExpression = IsNamed(builder.ReturnType, "Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax");
        bool returnsType = IsNamed(builder.ReturnType, "Microsoft.CodeAnalysis.CSharp.Syntax.TypeSyntax");

        if (!returnsExpression && !returnsType)
        {
            returnShapeError = returnDisplay;
            return null;
        }

        var parameters = new List<FacadeParam>();
        var genericParameters = new List<string>();
        string? returnTypeParamName = null;
        int returnTypeParamCount = 0;

        foreach (var p in builder.Parameters)
        {
            var quoted = FindAttribute(p, typeof(global::Synto.Templating.QuotedAttribute).FullName!);
            bool isReturnType = FindAttribute(p, typeof(global::Synto.Templating.ReturnTypeAttribute).FullName!) is not null;

            if (quoted is null)
            {
                if (isReturnType)
                {
                    annotationError = $"[ReturnType] on parameter '{p.Name}' requires [Quoted(AsTypeArg = true)]";
                    return null;
                }

                // Unmarked (staged): facade value param, same type as the builder param.
                parameters.Add(new FacadeParam(p.Name, BuilderArgKind.Staged, null, p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                continue;
            }

            bool asTypeArg = GetNamedBool(quoted, "AsTypeArg");
            if (asTypeArg)
            {
                string genericName = Pascal(p.Name);
                genericParameters.Add(genericName);
                parameters.Add(new FacadeParam(p.Name, BuilderArgKind.QuotedTypeArg, genericName, null));

                if (isReturnType)
                {
                    returnTypeParamCount++;
                    returnTypeParamName = genericName;
                }
            }
            else
            {
                if (isReturnType)
                {
                    annotationError = $"[ReturnType] on parameter '{p.Name}' requires [Quoted(AsTypeArg = true)]";
                    return null;
                }

                // [Quoted] value island: facade value param typed `As` (if given) else object.
                string valueType = GetNamedTypeDisplay(quoted, "As") ?? "object";
                parameters.Add(new FacadeParam(p.Name, BuilderArgKind.Quoted, null, valueType));
            }
        }

        if (returnTypeParamCount > 1)
        {
            annotationError = "more than one [ReturnType] parameter";
            return null;
        }

        bool freshReturnTypeParam = false;
        string returnTypeDisplay;
        if (returnsType)
        {
            returnTypeDisplay = "global::System.Type";
        }
        else if (returnTypeParamName is not null)
        {
            returnTypeDisplay = returnTypeParamName;
        }
        else
        {
            freshReturnTypeParam = true;
            returnTypeDisplay = FreshReturnTypeParamName;
        }

        // The fresh TResult is the FIRST facade generic parameter.
        if (freshReturnTypeParam)
            genericParameters.Insert(0, FreshReturnTypeParamName);

        return new FacadeShape(builder.Name, parameters, genericParameters, freshReturnTypeParam, returnTypeDisplay);
    }

    private static bool IsNamed(ITypeSymbol type, string fullyQualifiedMetadata)
    {
        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::" + fullyQualifiedMetadata;
    }

    private static AttributeData? FindAttribute(ISymbol symbol, string attributeFullName)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == attributeFullName)
                return attr;
        }

        return null;
    }

    private static bool GetNamedBool(AttributeData attr, string name)
    {
        foreach (var kv in attr.NamedArguments)
        {
            if (kv.Key == name && kv.Value.Value is bool b)
                return b;
        }

        return false;
    }

    private static string? GetNamedTypeDisplay(AttributeData attr, string name)
    {
        foreach (var kv in attr.NamedArguments)
        {
            if (kv.Key == name && kv.Value.Value is ITypeSymbol t)
                return t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        return null;
    }

    private static string Pascal(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "T";
        return char.ToUpperInvariant(name[0]) + name.Substring(1);
    }
}

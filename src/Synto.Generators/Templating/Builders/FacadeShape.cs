using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.Generators.Symbols;

namespace Synto.Generators.Templating.Builders;

/// <summary>One builder parameter as seen by facade derivation (binding + facade element).</summary>
internal sealed class FacadeParam
{
    public FacadeParam(string parameterName, BuilderArgKind kind, string? genericParameterName, string? valueTypeDisplay, bool isParams = false)
    {
        ParameterName = parameterName;
        Kind = kind;
        GenericParameterName = genericParameterName;
        ValueTypeDisplay = valueTypeDisplay;
        IsParams = isParams;
    }

    public string ParameterName { get; }
    public BuilderArgKind Kind { get; }

    /// <summary>The synthesized facade generic type-parameter name (for <see cref="BuilderArgKind.QuotedTypeArg"/>).</summary>
    public string? GenericParameterName { get; }

    /// <summary>The synthesized facade value-parameter type (for <see cref="BuilderArgKind.Quoted"/>/<see cref="BuilderArgKind.Staged"/>).</summary>
    public string? ValueTypeDisplay { get; }

    /// <summary>True when the builder parameter is a <c>params</c> collection — the facade re-emits the modifier and
    /// the binder collects the call's trailing (expanded) arguments into this parameter.</summary>
    public bool IsParams { get; }
}

/// <summary>One synthesized facade generic type parameter: its name and an optional base-type constraint.</summary>
internal readonly struct FacadeGenericParam
{
    public FacadeGenericParam(string name, string? constraintDisplay)
    {
        Name = name;
        ConstraintDisplay = constraintDisplay;
    }

    public string Name { get; }

    /// <summary>Fully-qualified base type for a <c>where Name : X</c> clause, or <c>null</c> when unconstrained.</summary>
    public string? ConstraintDisplay { get; }
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
        IReadOnlyList<FacadeGenericParam> genericParameters,
        bool freshReturnTypeParam,
        string returnTypeDisplay,
        bool returnsStatement)
    {
        MethodName = methodName;
        Parameters = parameters;
        GenericParameters = genericParameters;
        FreshReturnTypeParam = freshReturnTypeParam;
        ReturnTypeDisplay = returnTypeDisplay;
        ReturnsStatement = returnsStatement;
    }

    public string MethodName { get; }

    /// <summary>Builder parameters in declared order, each carrying its binding + facade element.</summary>
    public IReadOnlyList<FacadeParam> Parameters { get; }

    /// <summary>The synthesized facade's generic type parameters (name + optional constraint), in facade order (a leading fresh TResult first when <see cref="FreshReturnTypeParam"/>).</summary>
    public IReadOnlyList<FacadeGenericParam> GenericParameters { get; }

    /// <summary>True when the facade gains a fresh leading <c>TResult</c> return type parameter (builder returns ExpressionSyntax with no [ReturnType] param).</summary>
    public bool FreshReturnTypeParam { get; }

    /// <summary>The synthesized facade's return type.</summary>
    public string ReturnTypeDisplay { get; }

    /// <summary>True when the builder returns a <see cref="StatementSyntax"/>-derived node (e.g. the statement
    /// <c>Switch</c> → <c>SwitchStatementSyntax</c>). Such a call, written as a bare statement, must splice the
    /// built statement node directly (no <c>ExpressionStatement</c> wrapper). Plain equatable bool — no symbol leaks.</summary>
    public bool ReturnsStatement { get; }

    private const string FreshReturnTypeParamName = "TResult";

    public static FacadeShape? Derive(IMethodSymbol builder, out string? annotationError, out string? returnShapeError)
    {
        annotationError = null;
        returnShapeError = null;

        string returnDisplay = builder.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        bool returnsExpression = IsNamed(builder.ReturnType, "Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax");
        bool returnsType = IsNamed(builder.ReturnType, "Microsoft.CodeAnalysis.CSharp.Syntax.TypeSyntax");
        // A concrete Roslyn syntax-node return type (e.g. SwitchStatementSyntax / SwitchSectionSyntax) is also
        // accepted, and its facade returns that concrete type VERBATIM (see the return-display block below).
        bool returnsSyntaxNode = IsSyntaxNode(builder.ReturnType);

        if (!returnsExpression && !returnsType && !returnsSyntaxNode)
        {
            returnShapeError = returnDisplay;
            return null;
        }

        var parameters = new List<FacadeParam>();
        var genericParameters = new List<FacadeGenericParam>();
        string? returnTypeParamName = null;
        int returnTypeParamCount = 0;

        foreach (var p in builder.Parameters)
        {
            var quoted = SymbolMetadataExtensions.FindAttribute(p, typeof(global::Synto.Templating.QuotedAttribute).FullName!);
            var typeArg = SymbolMetadataExtensions.FindAttribute(p, typeof(global::Synto.Templating.TypeArgAttribute).FullName!);
            var constant = SymbolMetadataExtensions.FindAttribute(p, typeof(global::Synto.Templating.ConstantAttribute).FullName!);
            bool isReturnType = SymbolMetadataExtensions.FindAttribute(p, typeof(global::Synto.Templating.ReturnTypeAttribute).FullName!) is not null;

            if (typeArg is not null && quoted is not null)
            {
                // [TypeArg] reshapes a parameter into a facade type argument; [Quoted] into a facade value argument.
                // The two are mutually exclusive — combining them is documented-forbidden and silently half-ignored today.
                annotationError = $"parameter '{p.Name}': [TypeArg] and [Quoted] must never be combined";
                return null;
            }

            if (constant is not null && (quoted is not null || typeArg is not null))
            {
                // [Constant] is a value-axis binding-time kind (value lifted to a constant ExpressionSyntax); it is a
                // sibling of [Quoted] (island quote) and orthogonal to [TypeArg] (type axis) — never combine them.
                annotationError = $"parameter '{p.Name}': [Constant] must never be combined with [Quoted] or [TypeArg]";
                return null;
            }

            if (isReturnType && typeArg is null)
            {
                annotationError = $"[ReturnType] on parameter '{p.Name}' requires [TypeArg]";
                return null;
            }

            if (typeArg is not null)
            {
                // [TypeArg] type island: facade generic type parameter. Name override else Pascal(param).
                string genericName = SymbolMetadataExtensions.GetNamedString(typeArg, "Name") ?? Pascal(p.Name);
                string? constraintDisplay = SymbolMetadataExtensions.GetNamedTypeDisplay(typeArg, "Constraint");

                if (genericParameters.Any(g => g.Name == genericName))
                {
                    // Two facade type-parameters resolving to one name would emit `<T, T>` (CS0692).
                    annotationError = $"duplicate facade type-parameter name '{genericName}'";
                    return null;
                }

                genericParameters.Add(new FacadeGenericParam(genericName, constraintDisplay));
                parameters.Add(new FacadeParam(p.Name, BuilderArgKind.QuotedTypeArg, genericName, null));

                if (isReturnType)
                {
                    returnTypeParamCount++;
                    returnTypeParamName = genericName;
                }

                continue;
            }

            if (quoted is not null)
            {
                // A [Quoted] value param projects onto a facade value param typed `As`/object — a `params object`
                // is not a legal facade shape, so a [Quoted] params combination is rejected rather than silently
                // dropping the params modifier (which would change the call shape).
                if (p.IsParams)
                {
                    annotationError = $"parameter '{p.Name}': [Quoted] params is not supported";
                    return null;
                }

                // [Quoted] value island: facade value param typed `As` (if given) else object.
                string valueType = SymbolMetadataExtensions.GetNamedTypeDisplay(quoted, "As") ?? "object";
                parameters.Add(new FacadeParam(p.Name, BuilderArgKind.Quoted, null, valueType));
                continue;
            }

            if (constant is not null)
            {
                // [Constant] value: the author passes a factory-time value that is lifted to a constant
                // ExpressionSyntax at the binding seam, so the facade value param is fixed `object` (not the builder
                // param type, which RECEIVES the lifted node). A `params object` is not a legal facade shape.
                if (p.IsParams)
                {
                    annotationError = $"parameter '{p.Name}': [Constant] params is not supported";
                    return null;
                }

                parameters.Add(new FacadeParam(p.Name, BuilderArgKind.Constant, null, "object"));
                continue;
            }

            // Unmarked (staged): facade value param, same type as the builder param (preserving any `params`).
            parameters.Add(new FacadeParam(p.Name, BuilderArgKind.Staged, null, p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), p.IsParams));
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
        else if (returnsExpression)
        {
            freshReturnTypeParam = true;
            returnTypeDisplay = FreshReturnTypeParamName;
        }
        else
        {
            // A concrete syntax-node return type (e.g. SwitchStatementSyntax): the facade returns it VERBATIM,
            // NOT a fresh TResult — otherwise overloaded builders (the two `Switch` shapes) would become
            // return-type-generic and overload resolution would collapse.
            returnTypeDisplay = returnDisplay;
        }

        // The fresh TResult is the FIRST facade generic parameter — reject a user [TypeArg(Name = "TResult")]
        // that would collide with it (again CS0692 in the synthesized facade).
        if (freshReturnTypeParam)
        {
            if (genericParameters.Any(g => g.Name == FreshReturnTypeParamName))
            {
                annotationError = $"facade type-parameter name '{FreshReturnTypeParamName}' is reserved for the synthesized return type";
                return null;
            }
            genericParameters.Insert(0, new FacadeGenericParam(FreshReturnTypeParamName, null));
        }

        // A StatementSyntax-derived return (not ExpressionSyntax/TypeSyntax) makes the call statement-position
        // spliceable: a bare-statement builder call emits the built statement node directly.
        bool returnsStatement = !returnsExpression && !returnsType && IsStatementSyntax(builder.ReturnType);

        return new FacadeShape(builder.Name, parameters, genericParameters, freshReturnTypeParam, returnTypeDisplay, returnsStatement);
    }

    private static bool IsNamed(ITypeSymbol type, string fullyQualifiedMetadata)
    {
        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::" + fullyQualifiedMetadata;
    }

    private static bool IsSyntaxNode(ITypeSymbol type)
    {
        for (ITypeSymbol? t = type; t is not null; t = t.BaseType)
            if (t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::Microsoft.CodeAnalysis.SyntaxNode")
                return true;
        return false;
    }

    private static bool IsStatementSyntax(ITypeSymbol type)
    {
        for (ITypeSymbol? t = type; t is not null; t = t.BaseType)
            if (t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::Microsoft.CodeAnalysis.CSharp.Syntax.StatementSyntax")
                return true;
        return false;
    }

    private static string Pascal(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "T";
        return char.ToUpperInvariant(name[0]) + name.Substring(1);
    }
}

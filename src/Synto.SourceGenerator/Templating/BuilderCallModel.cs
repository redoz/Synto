using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto;

/// <summary>
/// How a single syntax-builder parameter's call argument is bound at factory time (plan Task 3 / Locked
/// Names §6). An <c>[Quoted]</c> parameter receives the <em>quote</em> of the call argument (an output-world
/// island); an <c>[Quoted(AsTypeArg = true)]</c> parameter receives the quote of a facade <em>type</em>
/// argument; an unmarked parameter receives the live/computed value verbatim.
/// </summary>
internal enum BuilderArgKind
{
    Staged,
    Quoted,
    QuotedTypeArg,
}

/// <summary>One builder parameter's resolved binding + the facade call element that feeds it.</summary>
internal sealed class BuilderArgBinding
{
    public BuilderArgBinding(BuilderArgKind kind, string parameterName, ExpressionSyntax? valueArgument, TypeSyntax? typeArgument)
    {
        Kind = kind;
        ParameterName = parameterName;
        ValueArgument = valueArgument;
        TypeArgument = typeArgument;
    }

    public BuilderArgKind Kind { get; }
    public string ParameterName { get; }

    /// <summary>The call argument expression (for <see cref="BuilderArgKind.Quoted"/> / <see cref="BuilderArgKind.Staged"/>).</summary>
    public ExpressionSyntax? ValueArgument { get; }

    /// <summary>The facade type argument (for <see cref="BuilderArgKind.QuotedTypeArg"/>).</summary>
    public TypeSyntax? TypeArgument { get; }
}

/// <summary>
/// A recognized facade call in the carrier, resolved to its factory-time builder invocation (plan Task 3).
/// The invocation node is the replacement key; <see cref="BuilderFullyQualifiedTypeName"/> + <see cref="BuilderMethodName"/>
/// name the static builder to call, and <see cref="Args"/> carries each builder parameter's binding in
/// builder-parameter order.
/// </summary>
internal sealed class BuilderCall
{
    public BuilderCall(InvocationExpressionSyntax invocation, string builderFullyQualifiedTypeName, string builderMethodName, IReadOnlyList<BuilderArgBinding> args)
    {
        Invocation = invocation;
        BuilderFullyQualifiedTypeName = builderFullyQualifiedTypeName;
        BuilderMethodName = builderMethodName;
        Args = args;
    }

    public InvocationExpressionSyntax Invocation { get; }
    public string BuilderFullyQualifiedTypeName { get; }
    public string BuilderMethodName { get; }
    public IReadOnlyList<BuilderArgBinding> Args { get; }
}

/// <summary>The result of discovering facade calls: the resolved calls plus any builder/binding diagnostics.</summary>
internal sealed class BuilderCallResult
{
    public BuilderCallResult(IReadOnlyList<BuilderCall> calls, IReadOnlyList<DiagnosticInfo> diagnostics)
    {
        Calls = calls;
        Diagnostics = diagnostics;
    }

    public IReadOnlyList<BuilderCall> Calls { get; }
    public IReadOnlyList<DiagnosticInfo> Diagnostics { get; }
}

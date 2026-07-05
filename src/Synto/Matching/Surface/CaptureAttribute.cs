using System;

namespace Synto.Matching;

/// <summary>
/// Marks a pattern parameter as an open expression hole: the matcher captures whatever
/// <see cref="Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax"/> occupies that position.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class CaptureAttribute : Attribute
{
}

/// <summary>
/// Marks a pattern parameter as an expression hole narrowed to <typeparamref name="TNode"/>: the matcher
/// only matches when the captured node is a <typeparamref name="TNode"/>, and binds the capture to it.
/// </summary>
/// <typeparam name="TNode">The Roslyn syntax type the captured node must be.</typeparam>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class CaptureAttribute<TNode> : Attribute
{
}

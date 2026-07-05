using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto.Generators.Templating.Emit;

/// <summary>
/// The factory-method skeleton the plan phase accumulates for one template build: the generated
/// <c>public static</c> factory's signature (<see cref="Parameters"/> / <see cref="TypeParams"/>) plus the
/// hoisted <see cref="Preamble"/> statements that run before the quoted return expression. The final
/// <c>QuoteAndAssemble</c> step reads this to assemble the method — the body is DERIVED (quoted), not stored
/// here. Plain mutable carrier; never equatable, never captured into cached pipeline state.
/// </summary>
internal sealed class FactoryShape
{
    public List<ParameterSyntax> Parameters { get; } = new();

    public List<TypeParameterSyntax> TypeParams { get; } = new();

    public List<StatementSyntax> Preamble { get; } = new();

    // The inlined (staged) type-parameter symbols, tracked so later lift steps skip re-inlining them.
    public HashSet<ITypeParameterSymbol> InlinedTypeParams { get; } = new(SymbolEqualityComparer.Default);
}

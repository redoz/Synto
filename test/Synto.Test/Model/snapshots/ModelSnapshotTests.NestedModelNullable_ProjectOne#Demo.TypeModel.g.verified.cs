//HintName: Demo.TypeModel.g.cs
#nullable enable
namespace Demo;
sealed record TypeModel(string Name, global::Demo.TypeModel? Base)
{
    public static TypeModel From(global::Microsoft.CodeAnalysis.INamedTypeSymbol symbol) => new(symbol.Name, symbol.BaseType is null ? null : global::Demo.TypeModel.From(symbol.BaseType));
}
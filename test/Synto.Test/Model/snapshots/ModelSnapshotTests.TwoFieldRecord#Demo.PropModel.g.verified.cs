//HintName: Demo.PropModel.g.cs
#nullable enable
namespace Demo;
sealed record PropModel(string Name, bool Required)
{
    public static PropModel From(global::Microsoft.CodeAnalysis.IPropertySymbol symbol) => new(Symbol.Name, Symbol.IsRequired);
}
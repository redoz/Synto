//HintName: DiagnosticBag.g.cs
#nullable enable
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Synto.Generators;

internal sealed class DiagnosticBag
{
    private readonly ImmutableArray<DiagnosticInfo>.Builder _items =
        ImmutableArray.CreateBuilder<DiagnosticInfo>();

    public bool IsEmpty => _items.Count == 0;

    public void Report(DiagnosticInfo diagnostic) => _items.Add(diagnostic);

    public EquatableArray<DiagnosticInfo> ToEquatableArray() => new(_items.ToImmutable());
}

internal static class DiagnosticBagExtensions
{
    public static void Drain(this SourceProductionContext context, EquatableArray<DiagnosticInfo> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
            context.ReportDiagnostic(diagnostic.ToDiagnostic());
    }
}

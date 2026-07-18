extern alias SyntoIncremental;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using DiagnosticBag = SyntoIncremental::Synto.Generators.DiagnosticBag;
using DiagnosticInfo = SyntoIncremental::Synto.Generators.DiagnosticInfo;

namespace Synto.Test.Generators;

public class DiagnosticBagTests
{
    private static readonly DiagnosticDescriptor Desc =
        new("TST0001", "t", "m {0}", "cat", DiagnosticSeverity.Warning, true);

    private static DiagnosticInfo Info(string arg) =>
        new(Desc, Location: null,
            new SyntoIncremental::Synto.Generators.EquatableArray<string>(ImmutableArray.Create(arg)));

    [Fact]
    public void EmptyBag_IsEmpty_AndYieldsEmptyEquatableArray()
    {
        var bag = new DiagnosticBag();
        Assert.True(bag.IsEmpty);
        Assert.Empty(bag.ToEquatableArray());
    }

    [Fact]
    public void SameReports_ProduceValueEqualEquatableArrays()
    {
        var a = new DiagnosticBag(); a.Report(Info("x")); a.Report(Info("y"));
        var b = new DiagnosticBag(); b.Report(Info("x")); b.Report(Info("y"));
        Assert.Equal(a.ToEquatableArray(), b.ToEquatableArray()); // structural equality => cache hit
        Assert.False(a.IsEmpty);
    }
}

using Microsoft.CodeAnalysis;
using Xunit;

namespace Synto.Example.ObjectReader.Tests;

public class ObjectReaderDiagnosticsTests
{
    [Fact]
    public void UnknownMember_ReportsSOR0001_AndSkipsColumn() // C-2 / D5
    {
        const string source = """
            using System.Collections.Generic;
            using Synto.Example.ObjectReader.Api;
            public sealed record Person(string Name, int Age);
            public static class C { public static void M(IEnumerable<Person> p)
                => ObjectReader.Create(p, "Name", "Nope", "Age"); }
            """;

        (var diagnostics, var generated) = GeneratorHarness.Run(source);

        var sor0001 = Assert.Single(diagnostics, d => d.Id == "SOR0001");
        Assert.Equal(DiagnosticSeverity.Warning, sor0001.Severity);
        Assert.Contains("Nope", sor0001.GetMessage(System.Globalization.CultureInfo.InvariantCulture), System.StringComparison.Ordinal);
        Assert.DoesNotContain("\"Nope\"", generated, System.StringComparison.Ordinal); // bad column skipped (C-2)
        Assert.Contains("\"Name\"", generated, System.StringComparison.Ordinal);
        Assert.Contains("\"Age\"", generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void NonConstantMembers_ReportsSOR0002_AndDoesNotIntercept() // C-4 / D5
    {
        const string source = """
            using System.Collections.Generic;
            using Synto.Example.ObjectReader.Api;
            public sealed record Person(string Name, int Age);
            public static class C { public static void M(IEnumerable<Person> p, string[] names)
                => ObjectReader.Create(p, names); }
            """;

        (var diagnostics, var generated) = GeneratorHarness.Run(source);

        Assert.Contains(diagnostics, d => d.Id == "SOR0002");
        Assert.DoesNotContain("InterceptsLocation", generated, System.StringComparison.Ordinal); // not intercepted
    }
}

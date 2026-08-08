using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Synto.Example.ObjectReader.Tests;

public class ObjectReaderSnapshotTests
{
    private const string PersonSource = """
        using System.Collections.Generic;
        using Synto.Example.ObjectReader.Api;

        public sealed record Person(string Name, int Age);

        public static class Demo
        {
            public static void Run(IEnumerable<Person> people)
            {
                using var r = ObjectReader.Create(people, "Name", "Age");
            }
        }
        """;

    [Fact]
    public Task Generates_Specialized_Reader_For_Person()
    {
        return GeneratorHarness.Verify(PersonSource);
    }

    [Fact]
    public void Generated_Reader_Compiles_Without_ImplicitUsings()
    {
        // C1 / T1 guard: the harness reference closure carries no implicit or global usings, so the generated
        // ObjectReader must be self-contained. A regression that de-qualifies body identifiers without carrying
        // their usings fails here with CS0246 — exactly the ImplicitUsings-disabled consumer at risk.
        var errors = GeneratorHarness.Compile(PersonSource)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.True(
            errors.Count == 0,
            "Generated code did not compile: "
                + string.Join("; ", errors.Select(d => d.Id + " " + d.GetMessage(System.Globalization.CultureInfo.InvariantCulture))));
    }

    [Fact]
    public void Generator_UsesSyntoTemplate_NotHandRolledFactorySoupOnly()
    {
        // Sentinel (Task 4 / D3): the reader skeleton is quoted from a Synto [Template] in ReaderTemplate.cs.
        // Kept as a cheap guard so a regression that rips Synto back out of the generator is visible; the
        // load-bearing guarantee remains the behavioral + snapshot + diagnostics tests staying green.
        Assert.True(System.IO.File.Exists(
            System.IO.Path.Combine(GeneratorHarness.GeneratorProjectDir, "ReaderTemplate.cs")));
    }
}

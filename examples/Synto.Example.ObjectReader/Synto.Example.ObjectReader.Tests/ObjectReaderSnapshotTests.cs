using System.Threading.Tasks;
using Xunit;

namespace Synto.Example.ObjectReader.Tests;

public class ObjectReaderSnapshotTests
{
    [Fact]
    public Task Generates_Specialized_Reader_For_Person()
    {
        const string source = """
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

        return GeneratorHarness.Verify(source);
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

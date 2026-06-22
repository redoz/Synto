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
}

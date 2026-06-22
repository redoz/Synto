using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Synto.Example.ObjectReader.Tests;

public class ObjectReaderIncrementalTests
{
    [Fact]
    public void Transform_IsCachedOnUnrelatedEdit() // C-5: equatable model => incremental cacheability
    {
        // The example's headline contract is that the transform roots no Compilation/ISymbol/SemanticModel/
        // SyntaxNode and flows only an equatable ObjectReaderModel. The single easiest property to regress is
        // cacheability, so guard it directly: after an UNRELATED edit the tracked Transform step must come from
        // cache (Cached = step skipped; Unchanged = step re-ran but produced an equal value) — never re-projected.
        const string source = """
            using System.Collections.Generic;
            using Synto.Example.ObjectReader.Api;
            public sealed record Person(string Name, int Age);
            public static class C { public static void M(IEnumerable<Person> p)
                => ObjectReader.Create(p, "Name", "Age"); }
            """;

        GeneratorRunResult result = GeneratorHarness.RunIncremental(source);

        // Mirrors TrackingNames.Transform (= nameof(Transform)); that type is internal to the Generator project.
        const string transform = "Transform";
        Assert.True(result.TrackedSteps.ContainsKey(transform), $"no tracked step '{transform}'");

        var outputs = result.TrackedSteps[transform].SelectMany(step => step.Outputs).ToList();
        Assert.NotEmpty(outputs);
        Assert.All(outputs, output =>
            Assert.True(
                output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                $"step '{transform}' had reason {output.Reason}, expected Cached/Unchanged"));
    }
}

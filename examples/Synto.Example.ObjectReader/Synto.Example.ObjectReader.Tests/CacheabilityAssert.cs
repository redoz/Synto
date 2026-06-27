using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Synto.Example.ObjectReader.Tests;

/// <summary>
/// Shared incremental-cacheability assertion: after an unrelated edit, EVERY tracked step must be
/// Cached/Unchanged except the compilation-coupled input/plumbing steps. Iterating all steps (not a terminal
/// subset) closes the upstream blind spot a terminal-only guard has. (Mirror of the copy in Synto.Test;
/// duplicated because the test projects are separate assemblies.)
/// </summary>
internal static class CacheabilityAssert
{
    private static readonly string[] CompilationCoupledSteps =
    {
        "Compilation",
        "compilationAndGroupedNodes_ForAttributeWithMetadataName",
    };

    public static void AllStepsCachedOrUnchanged(GeneratorRunResult result, IReadOnlyList<string> requiredNamedSteps, params string[] alsoAllowModified)
    {
        foreach (var name in requiredNamedSteps)
            Assert.True(result.TrackedSteps.ContainsKey(name), $"no tracked step '{name}'");

        var allow = new HashSet<string>(CompilationCoupledSteps, StringComparer.Ordinal);
        foreach (var name in alsoAllowModified)
            allow.Add(name);

        Assert.NotEmpty(result.TrackedSteps);
        foreach (var pair in result.TrackedSteps)
        {
            if (allow.Contains(pair.Key))
                continue;

            foreach (var output in pair.Value.SelectMany(step => step.Outputs))
                Assert.True(
                    output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                    $"step '{pair.Key}' had reason {output.Reason}, expected Cached/Unchanged");
        }
    }
}

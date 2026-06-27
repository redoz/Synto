using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Synto.Test;

/// <summary>
/// Shared incremental-cacheability assertion: after an unrelated edit, EVERY tracked step of a generator run
/// must be <see cref="IncrementalStepRunReason.Cached"/>/<see cref="IncrementalStepRunReason.Unchanged"/>,
/// except the compilation-coupled input/plumbing steps that re-run on any compilation change.
/// </summary>
/// <remarks>
/// Iterating ALL steps (rather than a hand-picked terminal subset) closes the upstream blind spot a
/// terminal-only guard has: a new intermediate step — or a future capture of a non-equatable
/// <c>Compilation</c>/<c>SemanticModel</c>/<c>SyntaxNode</c> into pipeline state — would re-run here and fail
/// the test, instead of being silently skipped because only the terminal output happened to stay cached.
/// </remarks>
internal static class CacheabilityAssert
{
    /// <summary>
    /// The Roslyn input/plumbing steps that legitimately re-run on ANY compilation change (e.g. adding an
    /// unrelated syntax tree): the raw <c>Compilation</c> input and the
    /// <c>ForAttributeWithMetadataName</c> compilation-combine. Everything DOWNSTREAM of these — including
    /// every generator-named step — must stay cached for the pipeline to be incremental.
    /// </summary>
    private static readonly string[] CompilationCoupledSteps =
    {
        "Compilation",
        "compilationAndGroupedNodes_ForAttributeWithMetadataName",
    };

    /// <summary>
    /// Assert that the named steps are present (so the iteration is not vacuous) and that EVERY tracked step is
    /// Cached/Unchanged, excluding the compilation-coupled steps plus any caller-supplied
    /// <paramref name="alsoAllowModified"/> (generator-specific input steps that legitimately change).
    /// </summary>
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

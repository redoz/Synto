using Microsoft.CodeAnalysis;

namespace Synto.Test.Match;

/// <summary>
/// The C3 self-containment proof: a generated CAPTURING matcher must compile self-contained on a
/// <c>netstandard</c> consumer that LACKS <c>System.Runtime.CompilerServices.IsExternalInit</c>. The positional
/// result record lowers its members to <c>{ get; init; }</c>, and the <c>init</c> modreq needs
/// <c>IsExternalInit</c> — absent on netstandard — so without our once-per-assembly polyfill the consumer build
/// fails CS0518. These tests pin both directions: GREEN with the polyfill, RED (CS0518) without it, plus the
/// BCL-present coexistence side. The proof lands here (Task 6), the first capturing matcher — a zero-capture
/// record has no <c>init</c> member, so the proof could not reach RED at Task 5.
/// </summary>
/// <remarks>
/// The netstandard2.0 closure is FAITHFUL to a real generator project: it references the ns2.0 BUILD of Roslyn
/// (not the loaded net build), which legitimately embeds an INTERNAL <c>IsExternalInit</c>. That internal copy
/// is inaccessible cross-assembly and so does NOT satisfy a consumer's <c>init</c> modreq — the witnessed-RED
/// <see cref="GeneratedMatcher_WithoutPolyfill_FailsCS0518_OnNetStandard20"/> remains the direct, load-bearing
/// proof that the injected polyfill is required. <see cref="NetStandard20Closure_LacksAccessibleIsExternalInit"/>
/// is accordingly a refined CANARY: it asserts the closure exposes no <em>accessible</em> (public)
/// <c>IsExternalInit</c>, catching a net corlib sneaking in while tolerating Roslyn's internal copy.
/// </remarks>
public class MatchSelfContainmentTests
{
    private const string CapturingMatcherSource =
        """
        using Synto.Matching;

        partial class M { }

        public class Consumer
        {
            [Match<M>(MatchOption.Single)]
            static object Sum([Capture] int a, [Capture] int b) => a + b;
        }
        """;

    [Fact]
    public void NetStandard20Closure_LacksAccessibleIsExternalInit()
    {
        // Closure self-check (the canary for the load-bearing CS0518 proof below): a future ref-set change
        // must not silently restore an ACCESSIBLE IsExternalInit and let GeneratedMatcher_WithoutPolyfill_
        // FailsCS0518_OnNetStandard20 pass green with a broken/absent polyfill.
        //
        // The faithful ns2.0 Roslyn build legitimately embeds an INTERNAL IsExternalInit. That copy is unusable
        // cross-assembly — it does NOT satisfy a consumer's `init` modreq (witnessed by the still-RED CS0518
        // test) — so it does not undermine the proof and is tolerated here. What the closure must NEVER provide
        // is a PUBLIC IsExternalInit (e.g. a net corlib sneaking in), which a consumer COULD use; that is the
        // exact regression this guard exists to catch.
        var compilation = MatchTestHarness.CreateNetStandardClosure();

        var type = compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.IsExternalInit");
        Assert.True(type is null || type.DeclaredAccessibility != Accessibility.Public,
            "the netstandard2.0 closure must provide no PUBLIC IsExternalInit a consumer could use to satisfy an `init` modreq");
    }

    [Fact]
    public void GeneratedMatcher_CompilesOn_NetStandard20()
    {
        var result = MatchTestHarness.Run(CapturingMatcherSource);
        var matcher = MatchTestHarness.GeneratedMatcherSource(result);
        var polyfill = MatchTestHarness.GeneratedPolyfillSource(result);

        // The matcher now references the injected MatchPattern<T> (its {Pattern}Pattern descriptor), so the
        // self-contained closure includes the injected ForMatch surface alongside the matcher + polyfill.
        var forMatch = MatchTestHarness.InjectedSurfaceSource("readonly struct MatchPattern");

        // GREEN: matcher + injected surface + the injected polyfill compile self-contained on the
        // IsExternalInit-less closure.
        var compilation = MatchTestHarness.CreateNetStandardClosure(matcher, forMatch, polyfill);

        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void GeneratedMatcher_WithoutPolyfill_FailsCS0518_OnNetStandard20()
    {
        // Witnessed RED (the acceptance criterion): suppress the polyfill and the `init` modreq is unresolved
        // -> CS0518 (NOT CS0656). This is what makes the single per-assembly polyfill provably load-bearing.
        var result = MatchTestHarness.Run(CapturingMatcherSource);
        var matcher = MatchTestHarness.GeneratedMatcherSource(result);
        var forMatch = MatchTestHarness.InjectedSurfaceSource("readonly struct MatchPattern");

        var compilation = MatchTestHarness.CreateNetStandardClosure(matcher, forMatch);

        Assert.Contains(compilation.GetDiagnostics(),
            d => d.Severity == DiagnosticSeverity.Error && d.Id == "CS0518");
    }

    [Fact]
    public void GeneratedMatcher_CompilesOn_NetWithBcl()
    {
        var result = MatchTestHarness.Run(CapturingMatcherSource);
        var matcher = MatchTestHarness.GeneratedMatcherSource(result);
        var polyfill = MatchTestHarness.GeneratedPolyfillSource(result);
        var forMatch = MatchTestHarness.InjectedSurfaceSource("readonly struct MatchPattern");

        // BCL-present coexistence: the corlib already DEFINES IsExternalInit, so the injected source copy is
        // redundant-but-harmless (at most CS0436, a warning; the source copy wins) — zero ERROR diagnostics.
        var compilation = MatchTestHarness.CreateNetWithBclClosure(matcher, forMatch, polyfill);

        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }
}

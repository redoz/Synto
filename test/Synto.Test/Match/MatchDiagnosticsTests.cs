using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Synto.Test.Match;

/// <summary>
/// Target-validation diagnostics (the four-arm check, SY1001–SY1004) plus the positive read-the-type-arg
/// path and the unrelated-edit cacheability behavior for the matching pipeline. All fixtures route through
/// <see cref="MatchTestHarness"/>, which asserts the consumer source compiles as plain C# before generation
/// — the misuses here are SEMANTIC (a non-partial/non-class/metadata target), caught by ValidateTarget.
/// </summary>
public class MatchDiagnosticsTests
{
    [Fact]
    public void TargetNotDeclaredInSource_ReportsSY1003()
    {
        // Arm 1 — the GATING arm. TMatcher is `int`, a corlib/metadata type with NO source declaration, so
        // it must route to SY1003 and never fall through to a later arm or to WithAncestryFrom's throwing
        // cast (which would surface as SY0000).
        var result = MatchTestHarness.Run(
            """
            using Synto.Matching;

            public class Consumer
            {
                [Match<int>(MatchOption.Single)]
                static object One() => 1;
            }
            """);

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "SY1003");
        AssertHasRealSpan(diag);
    }

    [Fact]
    public void TargetNotPartial_ReportsSY1001()
    {
        // Arm 3: the target is a source class but is not declared `partial`.
        var result = MatchTestHarness.Run(
            """
            using Synto.Matching;

            class M { }

            public class Consumer
            {
                [Match<M>(MatchOption.Single)]
                static object One() => 1;
            }
            """);

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "SY1001");
        AssertHasRealSpan(diag);
    }

    [Theory]
    [InlineData("struct")]
    [InlineData("record struct")]
    [InlineData("interface")]
    public void TargetNotClass_ReportsSY1002(string kind)
    {
        // Arm 2: the target IS declared in source (so NOT SY1003) but is not a (non-record) class.
        var result = MatchTestHarness.Run(
            $$"""
            using Synto.Matching;

            partial {{kind}} M { }

            public class Consumer
            {
                [Match<M>(MatchOption.Single)]
                static object One() => 1;
            }
            """);

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "SY1002");
        AssertHasRealSpan(diag);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "SY1003");
    }

    [Fact]
    public void TargetAncestorNotPartial_ReportsSY1004()
    {
        // Arm 4: M is partial, but its enclosing `Outer` class is not — report the non-partial ancestor.
        var result = MatchTestHarness.Run(
            """
            using Synto.Matching;

            class Outer
            {
                public partial class M { }
            }

            public class Consumer
            {
                [Match<Outer.M>(MatchOption.Single)]
                static object One() => 1;
            }
            """);

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "SY1004");
        AssertHasRealSpan(diag);
    }

    [Fact]
    public void WellFormedMatch_ReadsTypeArg_PassesValidation()
    {
        // The type arg is read off AttributeClass.TypeArguments[0]; a well-formed partial-class target
        // passes all four arms, so NO diagnostics are reported. The emitter is a stub here — do NOT assert
        // on GeneratedTrees (the emission proof lands in Task 5).
        var result = MatchTestHarness.Run(
            """
            using Synto.Matching;

            partial class M { }

            public class Consumer
            {
                [Match<M>(MatchOption.Single)]
                static object One() => 1;
            }
            """);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void WellFormedMatch_EmitsNamedMatcher()
    {
        // The emission proof deferred from Task 4: a well-formed expression-Single pattern now lowers to a
        // real matcher. Exactly one generated tree carries the partial target + the matcher method, and no
        // diagnostics are reported. (The per-assembly IsExternalInit polyfill is a separate post-init tree,
        // so filter to the tree that actually contains the target class.)
        var result = MatchTestHarness.Run(
            """
            using Synto.Matching;

            partial class M { }

            public class Consumer
            {
                [Match<M>(MatchOption.Single)]
                static object One() => 1;
            }
            """);

        Assert.Empty(result.Diagnostics);

        var matcher = Assert.Single(
            result.GeneratedTrees.Where(t => t.ToString().Contains("partial class M", StringComparison.Ordinal)));
        Assert.Contains("One(SyntaxNode node)", matcher.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_IsIncremental_OnUnrelatedEdit()
    {
        // Cacheability guard: the pipeline carries only equatable value types (MatchGenerationResult /
        // DiagnosticInfo / EquatableArray) and captures no Compilation/SemanticModel/SyntaxNode, so an edit
        // in an unrelated tree must leave every tracked step Cached/Unchanged. The Result step caches a
        // null-Source MatchGenerationResult (the stub emits nothing).
        const string source =
            """
            using Synto.Matching;

            partial class M { }

            public class Consumer
            {
                [Match<M>(MatchOption.Single)]
                static object One() => 1;
            }
            """;

        var compilation = MatchTestHarness.CreateCompilation(source);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { new MatchFactorySourceGenerator().AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation);

        // An unrelated edit in a separate tree: the [Match] tree is byte-identical, so its pipeline results
        // must come from cache.
        var modified = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("namespace Other { internal sealed class Unrelated { } }"));
        driver = driver.RunGenerators(modified);

        var result = driver.GetRunResult().Results.Single();

        foreach (var trackingName in new[] { MatchTrackingNames.Transform, MatchTrackingNames.Result })
        {
            Assert.True(result.TrackedSteps.ContainsKey(trackingName), $"no tracked step '{trackingName}'");

            var outputs = result.TrackedSteps[trackingName].SelectMany(step => step.Outputs);
            Assert.All(outputs, output =>
                Assert.True(
                    output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                    $"step '{trackingName}' had reason {output.Reason}, expected Cached/Unchanged"));
        }
    }

    [Fact]
    public void Generator_IsIncremental_AcrossPatterns_OnEditingOne()
    {
        // Cross-pattern cacheability (C4): two [Match] patterns on one target; editing ONE body re-runs that
        // pattern's Transform/Result (Modified) while the other's Result stays Cached/Unchanged. Asserted
        // host-Roslyn-robustly as ">=1 Modified AND >=1 Cached/Unchanged" across both tracked steps.
        const string before =
            """
            using Synto.Matching;

            partial class M { }

            public class Consumer
            {
                [Match<M>(MatchOption.Single)]
                static object One() => 1;

                [Match<M>(MatchOption.Single)]
                static object Two() => 2;
            }
            """;

        // Only Two's literal body changes (2 -> 3); One's tree text is byte-identical.
        const string after =
            """
            using Synto.Matching;

            partial class M { }

            public class Consumer
            {
                [Match<M>(MatchOption.Single)]
                static object One() => 1;

                [Match<M>(MatchOption.Single)]
                static object Two() => 3;
            }
            """;

        var compilation = MatchTestHarness.CreateCompilation(before);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { new MatchFactorySourceGenerator().AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation);

        var modified = compilation.ReplaceSyntaxTree(
            compilation.SyntaxTrees.Single(),
            CSharpSyntaxTree.ParseText(after));
        driver = driver.RunGenerators(modified);

        var result = driver.GetRunResult().Results.Single();

        var reasons = new[] { MatchTrackingNames.Transform, MatchTrackingNames.Result }
            .SelectMany(name => result.TrackedSteps[name])
            .SelectMany(step => step.Outputs)
            .Select(output => output.Reason)
            .ToList();

        Assert.Contains(IncrementalStepRunReason.Modified, reasons);
        Assert.Contains(reasons, reason =>
            reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged);
    }

    [Fact]
    public void AnchorInNonePattern_ReportsSY1201_AndEmitsNoTree()
    {
        // None is fully bounded by its own braces, so a Block.End() anchor is a usage error -> SY1201, and no
        // matcher tree is emitted (the diagnostics-only bail).
        var result = MatchTestHarness.Run(
            """
            using Synto.Matching;

            partial class M { }

            public class Consumer
            {
                [Match<M>]
                static void Trailing([Capture] object result) { _ = result; Block.End(); }
            }
            """);

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "SY1201");
        AssertHasRealSpan(diag);
        Assert.DoesNotContain(result.GeneratedTrees, t => t.ToString().Contains("partial class M", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("None", "static object P([Capture] int a, [Capture] int b) => a + b;")]
    [InlineData("Bare", "static object P([Capture] int a, [Capture] int b) => a + b;")]
    [InlineData("Single", "static void P() { _ = 1; _ = 2; }")]
    public void OptionBodyShapeMisuse_ReportsSY1205_AndEmitsNoTree(string option, string pattern)
    {
        // option×body-shape misuse: None/Bare on an expression body, or Single on a multi-statement core ->
        // SY1205 (located on the attribute), and no matcher tree.
        var result = MatchTestHarness.Run(
            $$"""
            using Synto.Matching;

            partial class M { }

            public class Consumer
            {
                [Match<M>(MatchOption.{{option}})]
                {{pattern}}
            }
            """);

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "SY1205");
        AssertHasRealSpan(diag);
        Assert.DoesNotContain(result.GeneratedTrees, t => t.ToString().Contains("partial class M", StringComparison.Ordinal));
    }

    [Fact]
    public void ForeachOverCapture_ReportsSY1203()
    {
        // The §3.7 Concat repetition pattern: a phantom foreach over a [Capture] needs the deferred v2
        // backtracking lowering -> SY1203, no tree.
        var result = MatchTestHarness.Run(
            """
            using Synto.Matching;
            using System.Text;
            using System.Collections.Generic;

            partial class M { }

            public class Consumer
            {
                [Match<M>(MatchOption.Bare)]
                static void Concat([Capture] StringBuilder sb, [Capture] List<object> parts)
                {
                    foreach (var part in parts)
                        sb.Append(part);
                }
            }
            """);

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "SY1203");
        AssertHasRealSpan(diag);
        Assert.DoesNotContain(result.GeneratedTrees, t => t.ToString().Contains("partial class M", StringComparison.Ordinal));
    }

    [Fact]
    public void TwoVariableLengthQuantifiers_ReportsSY1204_AndEmitsNoTree()
    {
        // Two variable-length elements in one run need backtracking -> SY1204 (located on the second), no tree.
        var result = MatchTestHarness.Run(
            """
            using Synto.Matching;

            partial class M { }

            public class Consumer
            {
                [Match<M>(MatchOption.Bare)]
                static void TwoVar([Capture] Stmt head, [Capture] Stmt tail) { head.Some(); tail.All(); }
            }
            """);

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "SY1204");
        AssertHasRealSpan(diag);
        Assert.DoesNotContain(result.GeneratedTrees, t => t.ToString().Contains("partial class M", StringComparison.Ordinal));
    }

    [Fact]
    public void VariableLengthEmbeddedHole_ReportsSY1204_AndEmitsNoTree()
    {
        // A variable-length quantifier in a single-statement embedded slot can't expand -> SY1204, no tree.
        var result = MatchTestHarness.Run(
            """
            using Synto.Matching;

            partial class M { }

            public class Consumer
            {
                [Match<M>(MatchOption.Bare)]
                static void EmbeddedVar([Capture] bool cond, [Capture] Stmt rest) { if (cond) rest.All(); }
            }
            """);

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "SY1204");
        AssertHasRealSpan(diag);
        Assert.DoesNotContain(result.GeneratedTrees, t => t.ToString().Contains("partial class M", StringComparison.Ordinal));
    }

    [Fact]
    public void ContentBeforeBlockStart_ReportsSY1202()
    {
        // A core statement before Block.Start() requires content before the block's first statement -> provably
        // unsatisfiable -> SY1202, no tree.
        var result = MatchTestHarness.Run(
            """
            using Synto.Matching;

            partial class M { }

            public class Consumer
            {
                [Match<M>(MatchOption.Bare)]
                static void Lead([Capture] Stmt body) { body.Some(); Block.Start(); }
            }
            """);

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "SY1202");
        AssertHasRealSpan(diag);
        Assert.DoesNotContain(result.GeneratedTrees, t => t.ToString().Contains("partial class M", StringComparison.Ordinal));
    }

    private static void AssertHasRealSpan(Diagnostic diag)
    {
        // The location is carried cacheably as a serializable LocationInfo and reconstructed at emit time;
        // assert the squiggle still points at a real, non-empty source span (the attribute's), not Location.None.
        Assert.NotEqual(Location.None, diag.Location);
        Assert.False(diag.Location.SourceSpan.IsEmpty);
    }
}

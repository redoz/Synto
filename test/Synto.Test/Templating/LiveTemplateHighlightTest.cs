extern alias SyntoCore;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Synto.Test.Templating;

/// <summary>
/// End-to-end granularity harness for the <c>SY1035</c> "live template code" highlights (plan Tasks 3–6). Each
/// case runs the template generator over a <c>[Template]</c> body and asserts the exact source spans the three
/// emitter taps highlight — the primary evidence that a fragment is flagged iff the emitter actually lifts/unrolls
/// it. Spans are checked by the covered source text so the assertions read as the author's fragments.
/// </summary>
public class LiveTemplateHighlightTest
{
    private static readonly MetadataReference CorlibReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
    private static readonly MetadataReference NetStandardReference = MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("netstandard, Version=2.0.0.0").Location);
    private static readonly MetadataReference SystemRuntimeReference = MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.Runtime, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location);

    private static CSharpCompilation CreateCompilation() =>
        CSharpCompilation.Create("Test",
            [],
            references:
            [
                CorlibReference,
                NetStandardReference,
                SystemRuntimeReference,
                MetadataReference.CreateFromFile(typeof(System.Console).Assembly.Location),
                MetadataReference.CreateFromFile(SyntoCoreAssembly.Location),
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private readonly Compilation _baseCompilation = CreateCompilation();
    private readonly GeneratorDriver _driver = CSharpGeneratorDriver.Create(new TemplateFactorySourceGenerator());

    private string _source = "";

    private ImmutableArray<Diagnostic> RunAndGetDiagnostics(string source)
    {
        _source = source;
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        Assert.Empty(syntaxTree.GetDiagnostics());
        var compilation = _baseCompilation.AddSyntaxTrees(syntaxTree);
        var result = _driver.RunGenerators(compilation);
        return result.GetRunResult().Diagnostics;
    }

    private string[] LiveSpanTexts(ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics
            .Where(d => d.Id == "SY1035")
            .Select(d => _source.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length))
            .ToArray();

    // ----- Task 3: tap 1 — in-region splice holes --------------------------------------------------

    // A live member-access mixed with an output-world value: only the maximal purely-live subexpression `c.Length`
    // lifts, so exactly it is highlighted — not the enclosing `i == c.Length`, and not the output-world `i`.
    [Fact]
    public void Tap1_InRegionHole_HighlightsMaximalLiveSubexpression_NotEnclosingCompare()
    {
        var diagnostics = RunAndGetDiagnostics(
            """
            using Synto.Templating;
            using System.Collections.Generic;
            using static Synto.Templating.Template;
            partial class Factory {}
            public class TestClass {
                [Template<Factory>]
                void Build(int i) {
                    var cols = Unquote<IReadOnlyList<string>>();
                    foreach (var c in cols)
                        System.Console.WriteLine(i == c.Length);
                }
            }
            """);

        var texts = LiveSpanTexts(diagnostics);
        Assert.Contains("c.Length", texts);
        Assert.DoesNotContain("i == c.Length", texts);
        Assert.DoesNotContain(texts, t => t == "i");
    }

    // A staged loop body whose only output references an output-world value (no live subexpression) carries no
    // in-region SY1035 (the control driver is a separate tap).
    [Fact]
    public void Tap1_StagedBodyWithNoLiveSubexpression_HasNoInRegionHighlight()
    {
        var diagnostics = RunAndGetDiagnostics(
            """
            using Synto.Templating;
            using System.Collections.Generic;
            using static Synto.Templating.Template;
            partial class Factory {}
            public class TestClass {
                [Template<Factory>]
                void Build(int i) {
                    var cols = Unquote<IReadOnlyList<int>>();
                    foreach (var c in cols)
                        System.Console.WriteLine(i);
                }
            }
            """);

        var texts = LiveSpanTexts(diagnostics);
        Assert.DoesNotContain("i", texts);
    }

    // A loop-variable-dependent live expression inside a staged body is highlighted (the emitter's live set tracks
    // the loop variable), at the maximal purely-live granularity `c + 1`.
    [Fact]
    public void Tap1_LoopVariableDependentLiveExpression_IsHighlighted()
    {
        var diagnostics = RunAndGetDiagnostics(
            """
            using Synto.Templating;
            using System.Collections.Generic;
            using static Synto.Templating.Template;
            partial class Factory {}
            public class TestClass {
                [Template<Factory>]
                void Build() {
                    var cols = Unquote<IReadOnlyList<int>>();
                    foreach (var c in cols)
                        System.Console.WriteLine(c + 1);
                }
            }
            """);

        var texts = LiveSpanTexts(diagnostics);
        Assert.Contains("c + 1", texts);
    }

    // ----- Task 4: tap 2 — out-of-region (depth-zero) holes ----------------------------------------

    // A staged root used at depth zero inside a larger output-world expression lifts by VALUE: the SY1035 covers
    // exactly the parameter-use node `p`, not the enclosing `p + 1` (only the depth-zero reference is a live hole).
    [Fact]
    public void Tap2_DepthZeroParameterUse_HighlightsParameterNode_NotEnclosing()
    {
        var diagnostics = RunAndGetDiagnostics(
            """
            using Synto.Templating;
            using static Synto.Templating.Template;
            partial class Factory {}
            public class TestClass {
                [Template<Factory>]
                void Build() {
                    var p = Unquote<int>();
                    System.Console.WriteLine(p + 1);
                }
            }
            """);

        var texts = LiveSpanTexts(diagnostics);
        Assert.Contains("p", texts);
        Assert.DoesNotContain("p + 1", texts);
    }

    // A region-less template that only uses depth-zero live values still emits SY1035 — proves EmitLiveRegions runs
    // and merges the Info channel even when there are zero staged regions.
    [Fact]
    public void Tap2_RegionlessTemplate_WithDepthZeroLiveValue_EmitsHighlight()
    {
        var diagnostics = RunAndGetDiagnostics(
            """
            using Synto.Templating;
            using static Synto.Templating.Template;
            partial class Factory {}
            public class TestClass {
                [Template<Factory>]
                void Build() {
                    var p = Unquote<int>();
                    System.Console.WriteLine(p);
                }
            }
            """);

        var texts = LiveSpanTexts(diagnostics);
        Assert.Contains("p", texts);
    }

    // Negative (O2): a [Splice] value insertion is author-written OUTPUT syntax spliced verbatim, not a live value —
    // no SY1035.
    [Fact]
    public void Tap2_SpliceParameter_ProducesNoHighlight()
    {
        var diagnostics = RunAndGetDiagnostics(
            """
            using System;
            using Synto.Templating;
            partial class Factory {}
            public class TestClass {
                [Template<Factory>]
                void Build<T>([Splice] T value) {
                    Console.WriteLine($"Hello world {value}");
                }
            }
            """);

        Assert.Empty(LiveSpanTexts(diagnostics));
    }

    // Negative (O2): a [Splice] type-parameter substitution inserts an author-written type, not a live value —
    // no SY1035.
    [Fact]
    public void Tap2_SpliceTypeParameter_ProducesNoHighlight()
    {
        var diagnostics = RunAndGetDiagnostics(
            """
            using System;
            using Synto.Templating;
            partial class Factory {}
            public class TestClass {
                [Template<Factory>]
                void Build<[Splice] T>(T value) {
                    Console.WriteLine($"Hello world {value}");
                }
            }
            """);

        Assert.Empty(LiveSpanTexts(diagnostics));
    }

    // Negative (O2, documented v1 scoping): a scalar member-access fold (`columns.Count` -> literal) is a build-time
    // evaluation the spec's tap-2 enumeration does not list, so it is NOT highlighted in v1.
    [Fact]
    public void Tap2_ScalarMemberAccessFold_ProducesNoHighlight()
    {
        var diagnostics = RunAndGetDiagnostics(
            """
            using Synto.Templating;
            using System.Collections.Generic;
            using static Synto.Templating.Template;
            partial class Factory {}
            public class TestClass {
                [Template<Factory>]
                void Build() {
                    var columns = Unquote<IReadOnlyList<int>>();
                    System.Console.WriteLine(columns.Count);
                }
            }
            """);

        Assert.Empty(LiveSpanTexts(diagnostics));
    }

    // ----- Task 5: tap 3 — live control drivers ----------------------------------------------------

    // A live `foreach` iteration source (`columns`, a staged root) drives a staged unroll: the driver is highlighted,
    // while the loop body's output-world statement (`i`) is not.
    [Fact]
    public void Tap3_ForeachLiveDriver_IsHighlighted_BodyOutputIsNot()
    {
        var diagnostics = RunAndGetDiagnostics(
            """
            using Synto.Templating;
            using System.Collections.Generic;
            using static Synto.Templating.Template;
            partial class Factory {}
            public class TestClass {
                [Template<Factory>]
                void Build(int i) {
                    var columns = Unquote<IReadOnlyList<int>>();
                    foreach (var c in columns)
                        System.Console.WriteLine(i);
                }
            }
            """);

        var texts = LiveSpanTexts(diagnostics);
        Assert.Contains("columns", texts);
        Assert.DoesNotContain(texts, t => t == "i");
    }

    // A live `while` condition driving a staged unroll is highlighted (the whole condition expression is the driver).
    [Fact]
    public void Tap3_WhileLiveCondition_IsHighlighted()
    {
        var diagnostics = RunAndGetDiagnostics(
            """
            using Synto.Templating;
            using static Synto.Templating.Template;
            partial class Factory {}
            public class TestClass {
                [Template<Factory>]
                void Build() {
                    var flag = Unquote<bool>();
                    while (flag)
                        System.Console.WriteLine("x");
                }
            }
            """);

        var texts = LiveSpanTexts(diagnostics);
        Assert.Contains("flag", texts);
    }

    // A live `if` condition driving a staged specialization is highlighted.
    [Fact]
    public void Tap3_IfLiveCondition_IsHighlighted()
    {
        var diagnostics = RunAndGetDiagnostics(
            """
            using Synto.Templating;
            using static Synto.Templating.Template;
            partial class Factory {}
            public class TestClass {
                [Template<Factory>]
                void Build() {
                    var flag = Unquote<bool>();
                    if (flag)
                        System.Console.WriteLine("x");
                }
            }
            """);

        var texts = LiveSpanTexts(diagnostics);
        Assert.Contains("flag", texts);
    }

    // ----- Task 6: cap / deterministic order / cross-tap dedupe (end-to-end) ------------------------

    private static ImmutableArray<int> Sy1035Starts(ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics
            .Where(d => d.Id == "SY1035")
            .Select(d => d.Location.SourceSpan.Start)
            .ToImmutableArray();

    // Runs a source through a FRESH driver/compilation (independent of the instance _driver) so run-to-run
    // determinism is a real observation, not a generator-cache echo.
    private static ImmutableArray<int> RunFreshSy1035Starts(string source)
    {
        var driver = CSharpGeneratorDriver.Create(new TemplateFactorySourceGenerator());
        var compilation = CreateCompilation().AddSyntaxTrees(CSharpSyntaxTree.ParseText(source));
        var diagnostics = driver.RunGenerators(compilation).GetRunResult().Diagnostics;
        return diagnostics
            .Where(d => d.Id == "SY1035")
            .Select(d => d.Location.SourceSpan.Start)
            .ToImmutableArray();
    }

    // Cap: a template with 150 depth-zero live holes (150 distinct uses of one staged root) emits EXACTLY 100
    // SY1035s — the 100 lowest-start spans, in ascending order — and the same set/order on a second independent run.
    [Fact]
    public void Cap_OverHundredLiveFragments_EmitsExactlyHundredLowestStarts_Deterministically()
    {
        var body = new System.Text.StringBuilder();
        for (int k = 0; k < 150; k++)
            body.Append("        System.Console.WriteLine(p);\n");
        var source =
            "using Synto.Templating;\n" +
            "using static Synto.Templating.Template;\n" +
            "partial class Factory {}\n" +
            "public class TestClass {\n" +
            "    [Template<Factory>]\n" +
            "    void Build() {\n" +
            "        var p = Unquote<int>();\n" +
            body +
            "    }\n" +
            "}\n";

        var first = RunFreshSy1035Starts(source);
        var second = RunFreshSy1035Starts(source);

        Assert.Equal(100, first.Length);
        Assert.Equal(first, first.OrderBy(s => s).ToImmutableArray()); // ascending
        Assert.Equal(first, second);                                   // deterministic across independent runs
        Assert.Equal(first.Length, first.Distinct().Count());          // all distinct spans (no dupes survived)
    }

    // Ordering: a template mixing all three taps (tap-2 depth-zero `a`, tap-3 driver `cols`, tap-1 in-region `c + 1`)
    // returns its SY1035 spans in ascending document order.
    [Fact]
    public void Ordering_MultiTapTemplate_ReturnsSpansAscending()
    {
        var diagnostics = RunAndGetDiagnostics(
            """
            using Synto.Templating;
            using System.Collections.Generic;
            using static Synto.Templating.Template;
            partial class Factory {}
            public class TestClass {
                [Template<Factory>]
                void Build() {
                    var a = Unquote<int>();
                    var cols = Unquote<IReadOnlyList<int>>();
                    System.Console.WriteLine(a);
                    foreach (var c in cols)
                        System.Console.WriteLine(c + 1);
                }
            }
            """);

        var starts = Sy1035Starts(diagnostics);
        Assert.True(starts.Length >= 3, $"expected >=3 SY1035 spans across the three taps, got {starts.Length}");
        Assert.Equal(starts.OrderBy(s => s).ToImmutableArray(), starts);
    }

    // Dedupe (R1): the taps target disjoint node kinds (tap-1 in-region body subexpressions, tap-2 value-lift
    // reference nodes, tap-3 control drivers), so no natural end-to-end construct makes two taps land on the SAME
    // span — the per-span collapse is proven directly against the merge primitive in
    // LiveFragmentDiagnosticTest.Merge_DedupesBySpan (two DiagnosticInfos with equal LocationInfo -> one). This
    // note records that the R1 safety net has no reachable end-to-end trigger to exercise here.

    // ----- Task 2: Seam B — highlightsOnly early-return in TemplateFactoryBuilder.Build -------------

    private static ImmutableArray<TextSpan> Sy1035Spans(ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics
            .Where(d => d.Id == "SY1035")
            .Select(d => d.Location.SourceSpan)
            .OrderBy(s => s.Start)
            .ToImmutableArray();

    // Direct unit assertion on the seam: Build(..., highlightsOnly: true) produces the SAME SY1035 span set the
    // generator emits end-to-end for the same template, and returns an empty (factory-assembly-skipped) method list
    // — proving the early-return fires after every SY1035 tap has merged, before QuoteAndAssemble.
    [Fact]
    public void HighlightsOnly_Build_ProducesGeneratorSy1035Spans_AndSkipsFactoryAssembly()
    {
        const string source =
            """
            using Synto.Templating;
            using System.Collections.Generic;
            using static Synto.Templating.Template;
            partial class Factory {}
            public class TestClass {
                [Template<Factory>]
                void Build() {
                    var a = Unquote<int>();
                    var cols = Unquote<IReadOnlyList<int>>();
                    System.Console.WriteLine(a);
                    foreach (var c in cols)
                        System.Console.WriteLine(c + 1);
                }
            }
            """;

        // Generator (default-false) path: the reference SY1035 spans, end-to-end.
        var generatorSpans = Sy1035Spans(RunAndGetDiagnostics(source));
        Assert.True(generatorSpans.Length >= 3, $"fixture should surface >=3 SY1035 spans, got {generatorSpans.Length}");

        // Direct highlightsOnly Build over the same source.
        var ct = TestContext.Current.CancellationToken;
        var syntaxTree = CSharpSyntaxTree.ParseText(source, cancellationToken: ct);
        var compilation = _baseCompilation.AddSyntaxTrees(syntaxTree);
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var method = syntaxTree.GetRoot(ct).DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.Text == "Build");
        var attributes = semanticModel.GetDeclaredSymbol(method, ct)!.GetAttributes();

        var templateInfo = TemplateInfo.Create(semanticModel, method, attributes, ct);
        Assert.NotNull(templateInfo);

        var diagnostics = new List<DiagnosticInfo>();
        var factory = TemplateFactoryBuilder.Build(
            diagnostics,
            semanticModel,
            new SyntoCore::Synto.UsingDirectiveSet(Enumerable.Empty<UsingDirectiveSyntax>()),
            templateInfo!,
            templateInfo!.Options,
            highlightsOnly: true);

        var buildSpans = diagnostics
            .Where(d => d.Descriptor.Id == "SY1035")
            .Select(d => d.Location!.Value.TextSpan)
            .OrderBy(s => s.Start)
            .ToImmutableArray();

        Assert.Equal(generatorSpans, buildSpans);
        Assert.True(factory is null || factory.Count == 0, "highlightsOnly build must skip factory assembly (empty/null method list)");
    }
}

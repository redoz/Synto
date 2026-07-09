using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Synto.Test.Templating;

/// <summary>
/// Parity + coverage tests for <see cref="LiveTemplateHighlightAnalyzer"/> — the interim analyzer that paints the
/// SY1035 "live template code" highlight inline by reusing the generator's emit path
/// (<c>TryGenerate(highlightsOnly: true)</c>). Each case asserts the analyzer reports the SAME SY1035 span set the
/// generator emits end-to-end (parity), covers each tap's location, and stays silent on staging faults and
/// non-templates. Uses Roslyn's built-in <c>Compilation.WithAnalyzers</c> — no new NuGet.
/// </summary>
public class LiveTemplateHighlightAnalyzerTest
{
    private static readonly MetadataReference CorlibReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
    private static readonly MetadataReference NetStandardReference = MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("netstandard, Version=2.0.0.0").Location);
    private static readonly MetadataReference SystemRuntimeReference = MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.Runtime, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location);

    private static CSharpCompilation CreateCompilation(string source, CancellationToken cancellationToken) =>
        CSharpCompilation.Create("Test",
            [CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken)],
            references:
            [
                CorlibReference,
                NetStandardReference,
                SystemRuntimeReference,
                MetadataReference.CreateFromFile(typeof(System.Console).Assembly.Location),
                MetadataReference.CreateFromFile(SyntoCoreAssembly.Location),
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    // The SY1035 spans the GENERATOR emits end-to-end for this source (the parity reference).
    private static ImmutableArray<TextSpan> GeneratorSy1035Spans(string source, CancellationToken cancellationToken)
    {
        var compilation = CreateCompilation(source, cancellationToken);
        var driver = CSharpGeneratorDriver.Create(new TemplateFactorySourceGenerator());
        var diagnostics = driver.RunGenerators(compilation, cancellationToken).GetRunResult().Diagnostics;
        return Sy1035Spans(diagnostics);
    }

    // The SY1035 spans the ANALYZER reports for this source, via Roslyn's WithAnalyzers harness.
    private static async Task<ImmutableArray<TextSpan>> AnalyzerSy1035Spans(string source, CancellationToken cancellationToken)
    {
        var compilation = CreateCompilation(source, cancellationToken);
        var withAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new LiveTemplateHighlightAnalyzer()));
        var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync(cancellationToken);
        return Sy1035Spans(diagnostics);
    }

    private static ImmutableArray<TextSpan> Sy1035Spans(ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics
            .Where(d => d.Id == "SY1035")
            .Select(d => d.Location.SourceSpan)
            .OrderBy(s => s.Start)
            .ToImmutableArray();

    private static string[] SpanTexts(string source, ImmutableArray<TextSpan> spans) =>
        spans.Select(s => source.Substring(s.Start, s.Length)).ToArray();

    // A multi-tap fixture: tap-2 depth-zero `a`, tap-3 driver `cols`, tap-1 in-region `c + 1`.
    private const string MultiTapSource =
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

    // ----- Step 1/2: parity ------------------------------------------------------------------------

    [Fact]
    public async Task Parity_AnalyzerReportsSameSy1035Spans_AsGenerator()
    {
        var ct = TestContext.Current.CancellationToken;

        var generatorSpans = GeneratorSy1035Spans(MultiTapSource, ct);
        Assert.True(generatorSpans.Length >= 3, $"fixture should surface >=3 SY1035 spans, got {generatorSpans.Length}");

        var analyzerSpans = await AnalyzerSy1035Spans(MultiTapSource, ct);

        Assert.Equal(generatorSpans, analyzerSpans);
    }

    // ----- Step 3: location coverage ---------------------------------------------------------------

    [Fact]
    public async Task Location_DepthZeroLiveHole_HighlightsParameterNode_NotEnclosing()
    {
        const string source =
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
            """;

        var ct = TestContext.Current.CancellationToken;
        var spans = await AnalyzerSy1035Spans(source, ct);
        var texts = SpanTexts(source, spans);

        Assert.Contains("p", texts);
        Assert.DoesNotContain("p + 1", texts);
        Assert.Equal(GeneratorSy1035Spans(source, ct), spans);
    }

    [Fact]
    public async Task Location_MaximalPurelyStagedSubexpression_IsHighlighted()
    {
        const string source =
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
            """;

        var ct = TestContext.Current.CancellationToken;
        var spans = await AnalyzerSy1035Spans(source, ct);
        var texts = SpanTexts(source, spans);

        Assert.Contains("c.Length", texts);
        Assert.DoesNotContain("i == c.Length", texts);
        Assert.Equal(GeneratorSy1035Spans(source, ct), spans);
    }

    [Fact]
    public async Task Location_LiveControlDriver_IsHighlighted()
    {
        const string source =
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
            """;

        var ct = TestContext.Current.CancellationToken;
        var spans = await AnalyzerSy1035Spans(source, ct);
        var texts = SpanTexts(source, spans);

        Assert.Contains("columns", texts);
        Assert.DoesNotContain(texts, t => t == "i");
        Assert.Equal(GeneratorSy1035Spans(source, ct), spans);
    }

    [Fact]
    public async Task Location_IfLiveCondition_IsHighlighted()
    {
        const string source =
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
            """;

        var ct = TestContext.Current.CancellationToken;
        var spans = await AnalyzerSy1035Spans(source, ct);
        var texts = SpanTexts(source, spans);

        Assert.Contains("flag", texts);
        Assert.Equal(GeneratorSy1035Spans(source, ct), spans);
    }

    // Type-carrier: a [Template<Factory>] CLASS with a nested bare [Template] sub-template that has a live hole.
    // The class-declaration registration must fire, and the nested sub-template's SY1035 (collected while the
    // parent build recurses into it) must be painted.
    [Fact]
    public async Task Location_TypeCarrierWithNestedSubTemplate_HighlightsNestedLiveHole()
    {
        const string source =
            """
            using Synto.Templating;
            using static Synto.Templating.Template;
            partial class Factory {}
            [Template<Factory>]
            public class Reader {
                [Template]
                void Child() {
                    var p = Unquote<int>();
                    System.Console.WriteLine(p);
                }
            }
            """;

        var ct = TestContext.Current.CancellationToken;

        var generatorSpans = GeneratorSy1035Spans(source, ct);
        Assert.True(generatorSpans.Length >= 1, $"type-carrier fixture should surface >=1 SY1035 span, got {generatorSpans.Length}");

        var analyzerSpans = await AnalyzerSy1035Spans(source, ct);

        Assert.Equal(generatorSpans, analyzerSpans);
        Assert.Contains("p", SpanTexts(source, analyzerSpans));
    }

    // ----- Step 4: robustness ----------------------------------------------------------------------

    // A nested live foreach is an unsupported staged shape (SY1014): the generator bails before the SY1035 merge,
    // so the analyzer paints ZERO highlights and does not throw.
    [Fact]
    public async Task Robustness_UnsupportedStagedShape_YieldsNoHighlights_AndDoesNotThrow()
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
                    var rows = Unquote<IReadOnlyList<int>>();
                    var cols = Unquote<IReadOnlyList<int>>();
                    foreach (var r in rows)
                        foreach (var c in cols)
                            System.Console.WriteLine(r);
                }
            }
            """;

        var ct = TestContext.Current.CancellationToken;
        var spans = await AnalyzerSy1035Spans(source, ct);

        Assert.Empty(spans);
        Assert.Equal(GeneratorSy1035Spans(source, ct), spans);
    }

    // ----- Step 5: non-template negative -----------------------------------------------------------

    [Fact]
    public async Task NonTemplate_MethodAndClassWithoutTemplate_YieldNoDiagnostics()
    {
        const string source =
            """
            using System.Collections.Generic;
            public class PlainClass {
                void PlainMethod() {
                    var list = new List<int>();
                    System.Console.WriteLine(list.Count);
                }
            }
            """;

        var ct = TestContext.Current.CancellationToken;
        var compilation = CreateCompilation(source, ct);
        var withAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new LiveTemplateHighlightAnalyzer()));
        var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync(ct);

        Assert.Empty(diagnostics);
    }
}

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

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
                    var cols = Parameter<IReadOnlyList<string>>();
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
                    var cols = Parameter<IReadOnlyList<int>>();
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
                    var cols = Parameter<IReadOnlyList<int>>();
                    foreach (var c in cols)
                        System.Console.WriteLine(c + 1);
                }
            }
            """);

        var texts = LiveSpanTexts(diagnostics);
        Assert.Contains("c + 1", texts);
    }
}

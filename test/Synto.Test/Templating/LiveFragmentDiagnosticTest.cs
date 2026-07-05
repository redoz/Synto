using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Synto.Test.Templating;

/// <summary>
/// Direct unit tests (via IVT) for the <c>SY1035</c> "Live template code" Info descriptor and its
/// <c>TemplateDiagnostics.LiveFragment</c> factory (plan Task 1), plus the merge/dedupe/order/cap primitive and
/// the success-path-only Info channel isolation (plan Task 2). SY1035 is the first Info-severity Synto diagnostic
/// and lives in the new <c>Synto.Staging</c> category. No tap yet — the channel is empty, so behavior is unchanged.
/// </summary>
public class LiveFragmentDiagnosticTest
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

    private ImmutableArray<Diagnostic> RunAndGetDiagnostics(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        Assert.Empty(syntaxTree.GetDiagnostics());
        var compilation = _baseCompilation.AddSyntaxTrees(syntaxTree);
        var result = _driver.RunGenerators(compilation);
        return result.GetRunResult().Diagnostics;
    }

    private static Location SomeLocation()
    {
        var tree = CSharpSyntaxTree.ParseText("class C { int F; }");
        var field = tree.GetRoot().DescendantNodes().First(n => n is Microsoft.CodeAnalysis.CSharp.Syntax.FieldDeclarationSyntax);
        return field.GetLocation();
    }

    private static readonly DiagnosticDescriptor SampleDescriptor = TemplateDiagnostics.LiveFragment(SomeLocation()).Descriptor;

    private static DiagnosticInfo MakeInfo(int start, int length, string file = "File.cs")
    {
        var loc = new LocationInfo(file, new TextSpan(start, length),
            new LinePositionSpan(new LinePosition(0, start), new LinePosition(0, start + length)));
        return new DiagnosticInfo(SampleDescriptor, loc, new EquatableArray<string>(ImmutableArray<string>.Empty));
    }

    // ----- Task 1: descriptor shape ----------------------------------------------------------------

    [Fact]
    public void Descriptor_HasSY1035InfoStagingShape()
    {
        var descriptor = TemplateDiagnostics.LiveFragment(SomeLocation()).Descriptor;

        Assert.Equal("SY1035", descriptor.Id);
        Assert.Equal(DiagnosticSeverity.Info, descriptor.DefaultSeverity);
        Assert.Equal("Synto.Staging", descriptor.Category);
        Assert.True(descriptor.IsEnabledByDefault);
        Assert.Empty(descriptor.CustomTags);
    }

    [Fact]
    public void LiveFragment_LocationRoundTrips()
    {
        var location = SomeLocation();
        var diag = TemplateDiagnostics.LiveFragment(location).ToDiagnostic();

        Assert.Equal("SY1035", diag.Id);
        Assert.Equal(location.SourceSpan, diag.Location.SourceSpan);
    }

    // ----- Task 2: merge/dedupe/order/cap primitive ------------------------------------------------

    [Fact]
    public void Merge_DedupesBySpan()
    {
        var merged = LiveFragmentMerge.Merge(new[] { MakeInfo(10, 5), MakeInfo(10, 5) });

        var only = Assert.Single(merged);
        Assert.Equal(new TextSpan(10, 5), only.Location!.Value.TextSpan);
    }

    [Fact]
    public void Merge_OrdersByStartThenEndAscending()
    {
        var merged = LiveFragmentMerge.Merge(new[]
        {
            MakeInfo(30, 2),
            MakeInfo(10, 8),  // same start as next, larger end -> comes second
            MakeInfo(10, 3),
            MakeInfo(20, 1),
        });

        var spans = merged.Select(m => m.Location!.Value.TextSpan).ToArray();
        Assert.Equal(new[] { new TextSpan(10, 3), new TextSpan(10, 8), new TextSpan(20, 1), new TextSpan(30, 2) }, spans);
    }

    [Fact]
    public void Merge_CapsAtHundredLowestStarts()
    {
        // 150 distinct spans in descending start order; expect the 100 lowest starts, ascending.
        var infos = Enumerable.Range(0, 150).Select(i => MakeInfo(start: (150 - i) * 10, length: 1)).ToArray();

        var merged = LiveFragmentMerge.Merge(infos);

        Assert.Equal(100, merged.Count);
        var starts = merged.Select(m => m.Location!.Value.TextSpan.Start).ToArray();
        Assert.Equal(Enumerable.Range(1, 100).Select(i => i * 10).ToArray(), starts);
    }

    // ----- Task 2: success-path-only Info channel isolation ----------------------------------------

    // A template whose live shape is unsupported reports SY1014 and the factory bails BEFORE the SY1035 merge;
    // no SY1035 may ride the error path. (No taps exist yet, so this locks the invariant before they are added.)
    [Fact]
    public void UnsupportedStagedShape_ReportsSY1014_WithNoLiveFragmentInfo()
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
                    var rows = Parameter<IReadOnlyList<int>>();
                    var cols = Parameter<IReadOnlyList<int>>();
                    foreach (var r in rows)
                        foreach (var c in cols)        // nested live region -> unsupported in v1
                            System.Console.WriteLine(r);
                }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "SY1014");
        Assert.DoesNotContain(diagnostics, d => d.Id == "SY1035");
    }
}

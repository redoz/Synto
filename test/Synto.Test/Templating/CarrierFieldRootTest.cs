using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Synto.Test.Templating;

/// <summary>
/// Plan Task 1 — carrier field-initializer live root. A <c>[Template]</c> carrier declares a live
/// <c>Unquote</c> root ONCE as a field initializer (<c>private readonly T name = Unquote&lt;T&gt;();</c>)
/// shared by every member, instead of re-declaring <c>var name = Unquote&lt;T&gt;();</c> per method body.
/// The field is discovered as a declaration-position staged parameter: it folds into one factory parameter,
/// seeds every member's liveness, and is stripped from the quoted output (no field, no leftover naming error).
/// </summary>
public class CarrierFieldRootTest
{
    private static readonly MetadataReference CorlibReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
    private static readonly MetadataReference NetStandardReference = MetadataReference.CreateFromFile(Assembly.Load("netstandard, Version=2.0.0.0").Location);
    private static readonly MetadataReference SystemRuntimeReference = MetadataReference.CreateFromFile(Assembly.Load("System.Runtime, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location);

    private static CSharpCompilation CreateCompilation() =>
        CSharpCompilation.Create("Test",
            [],
            references:
            [
                CorlibReference,
                NetStandardReference,
                SystemRuntimeReference,
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                MetadataReference.CreateFromFile(SyntoCoreAssembly.Location),
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private readonly Compilation _baseCompilation = CreateCompilation();
    private readonly GeneratorDriver _driver = CSharpGeneratorDriver.Create(new TemplateFactorySourceGenerator());

    private Compilation CompilationWithSource(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        Assert.Empty(syntaxTree.GetDiagnostics());
        return _baseCompilation.AddSyntaxTrees(syntaxTree);
    }

    // A single field-init live root (`ordinals`) shared by TWO members. Each member's foreach over the field
    // must unroll onto its own build-time run — the field is the shared root, never re-declared per body.
    private const string CarrierFieldSource =
        """
        using Synto.Templating;
        using System.Collections.Generic;
        using static Synto.Templating.Template;
        partial class Factory {}
        [Template<Factory>]
        public class Reader {
            private readonly IReadOnlyList<int> ordinals = Unquote<IReadOnlyList<int>>();
            public int Find(int i) {
                foreach (var o in ordinals)
                    if (i == o)
                        return o;
                throw new System.IndexOutOfRangeException();
            }
            public bool Has(int i) {
                foreach (var o in ordinals)
                    if (i == o)
                        return true;
                return false;
            }
        }
        """;

    [Fact]
    public async Task CarrierFieldRoot_SharedAcrossMembers_StripsFieldAndFoldsOneParam()
    {
        var compilation = CompilationWithSource(CarrierFieldSource);
        var result = _driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        var runResult = result.GetRunResult();

        // (d) No error diagnostics. Before the fix the field-init Unquote lands in the inline-fallback branch
        // (no implicit name) and trips SY1010 (missing parameter name).
        Assert.Empty(runResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var sources = runResult.Results
            .SelectMany(r => r.GeneratedSources)
            .ToDictionary(s => s.HintName, s => s.SourceText.ToString(), StringComparer.Ordinal);

        Assert.Contains("Factory.Reader.g.cs", sources.Keys);
        var doc = sources["Factory.Reader.g.cs"];

        // (a) Exactly one factory parameter named `ordinals` of the live list type (the field folded to one param).
        Assert.Contains("IReadOnlyList<int> ordinals)", doc, StringComparison.Ordinal);

        // (b) The output class does NOT declare the `ordinals` field — the field-init root is stripped, never quoted.
        Assert.DoesNotContain("FieldDeclaration", doc, StringComparison.Ordinal);

        // (c) BOTH members' loops unrolled at build time (two live `foreach (var o in ordinals)` runs), and no
        // foreach survives as a quoted output statement (a un-recognized member would emit ForEachStatement(...)).
        Assert.Equal(2, Regex.Count(doc, @"foreach \(var o in ordinals\)"));
        Assert.DoesNotContain("ForEachStatement", doc, StringComparison.Ordinal);

        var ret = await Verify(result).UseDirectory("snapshots");
        Assert.NotEmpty(ret.Files);
    }
}

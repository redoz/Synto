using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Synto.Test.Templating;

/// <summary>
/// Plan "unquote-param-lift-on-splice" — a <c>[Unquote] T value</c>-parameter now means the same thing as the
/// in-body <c>Unquote&lt;T&gt;()</c> spelling: lift-on-splice, control-without-lift. A converterless, control-only
/// <c>[Unquote]</c> param (every reference consumed by a live region) compiles clean and drives unrolling — it no
/// longer dies with SY1008, because the shared <see cref="Synto.Generators.Templating.Lifting.StagedRootLifter"/>
/// core only demands a converter when the depth-zero partition is non-empty.
/// </summary>
public class UnquoteParamLiftOnSpliceTest
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

    // Spec §2.1 "Control-only": a converterless [Unquote] param whose every reference is
    // control-consumed compiles clean, drives unrolling, and never asks for a converter. (Was SY1008.)
    private const string ControlOnlySource =
        """
        using Synto.Templating;
        using System.Collections.Generic;
        partial class Factory {}
        public class TestClass {
            [Template<Factory>]
            void Build([Unquote] IReadOnlyList<int> ordinals) {
                foreach (var o in ordinals)
                    System.Console.WriteLine(o);
            }
        }
        """;

    [Fact]
    public async Task ControlOnlyUnquoteParam_NoConverter_CompilesAndUnrolls()
    {
        var compilation = CompilationWithSource(ControlOnlySource);
        var result = _driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        var runResult = result.GetRunResult();

        // No SY1008/no errors: the value is never spliced, so no converter is required.
        Assert.Empty(runResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var doc = runResult.Results.SelectMany(r => r.GeneratedSources)
            .Single(s => s.HintName.Contains("Factory.Build", StringComparison.Ordinal))
            .SourceText.ToString();

        // The caller-supplied factory parameter exists (fully-qualified declared type)...
        Assert.Contains("IReadOnlyList<int> ordinals)", doc, StringComparison.Ordinal);
        // ...drives a factory-time (scaffold) foreach, not a quoted output loop...
        Assert.Contains("foreach (var o in ordinals)", doc, StringComparison.Ordinal);
        Assert.DoesNotContain("ForEachStatement", doc, StringComparison.Ordinal);
        // ...and no value lift is emitted at all (no preamble local, no ToSyntax on the param).
        Assert.DoesNotContain("syntaxForParam_ordinals", doc, StringComparison.Ordinal);

        var ret = await Verify(result).UseDirectory("snapshots");
        Assert.NotEmpty(ret.Files);
    }
}

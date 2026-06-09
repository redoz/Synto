using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;

namespace Synto.Bootstrap.Test;

public class CSharpSyntaxQuoterGeneratorTest
{
    private const string QuoterSource = """
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
namespace Synto;
public partial class CSharpSyntaxQuoter : CSharpSyntaxVisitor<ExpressionSyntax> {
    // this is not supported by the generator
    public override ExpressionSyntax? VisitIdentifierName(IdentifierNameSyntax node) => base.VisitIdentifierName(node);
}

""";

    [Fact]
    public Task VerifySnapshot()
    {
        var driver = GeneratorDriver();

        return Verify(driver).UseDirectory("snapshots");
    }

    [Fact]
    public void Generator_IsIncremental_OnUnrelatedEdit()
    {
        // Cacheability guard: with the pipeline carrying only the equatable QuoterGenerationResult (and no
        // captured Compilation/SemanticModel/SyntaxNode), an unrelated edit must leave every tracked step
        // cached. Before the equatable-pipeline fix the transform flowed out (ClassDeclarationSyntax,
        // SemanticModel), which re-ran the quoter on every keystroke.
        var compilation = CreateCompilation(QuoterSource);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { new CSharpSyntaxQuoterGenerator().AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation);

        // An unrelated edit in a separate tree: the CSharpSyntaxQuoter tree is byte-identical, so the
        // generated result must come from cache.
        var modified = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("namespace Other { internal sealed class Unrelated { } }"));
        driver = driver.RunGenerators(modified);

        var result = driver.GetRunResult().Results.Single();

        foreach (var trackingName in new[] { TrackingNames.Transform, TrackingNames.Result })
        {
            Assert.True(result.TrackedSteps.ContainsKey(trackingName), $"no tracked step '{trackingName}'");

            var outputs = result.TrackedSteps[trackingName].SelectMany(step => step.Outputs);
            Assert.All(outputs, output =>
                Assert.True(
                    output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                    $"step '{trackingName}' had reason {output.Reason}, expected Cached/Unchanged"));
        }
    }

    static GeneratorDriver GeneratorDriver()
    {
        var compilation = CreateCompilation(QuoterSource);

        var generator = new CSharpSyntaxQuoterGenerator();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        return driver.RunGenerators(compilation);
    }

    static CSharpCompilation CreateCompilation(params string[] sources)
    {
        var syntaxTrees = sources.Select(source => CSharpSyntaxTree.ParseText(source)).ToArray();

        var outputPath = Path.GetDirectoryName(typeof(CSharpSyntaxVisitor<>).GetTypeInfo().Assembly.Location)!;
        var allFiles = Directory.EnumerateFiles(outputPath, "*.dll", SearchOption.TopDirectoryOnly);
        return CSharpCompilation.Create("Test",
            syntaxTrees,
            allFiles.Select(file => MetadataReference.CreateFromFile(file))
        );
    }
}

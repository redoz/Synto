using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Synto.Test.Match;

/// <summary>
/// In-memory harness for the matching generator, mirroring <c>SimpleTemplateTest</c>: it builds a
/// compilation against the PUBLIC <c>Synto.Core</c> markers (via <see cref="SyntoCoreAssembly"/>) plus
/// Roslyn, runs ONLY <see cref="MatchFactorySourceGenerator"/>, and — like
/// <c>SimpleTemplateTest.VerifyTemplate</c> — asserts the consumer source compiles as plain C# BEFORE
/// generation. Negative target-validation fixtures deliberately compile (the misuse is semantic, caught by
/// <c>ValidateTarget</c>), so a pre-generation compile error means a broken fixture, not the behavior under
/// test.
/// </summary>
internal static class MatchTestHarness
{
    private static readonly MetadataReference CorlibReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
    private static readonly MetadataReference NetStandardReference = MetadataReference.CreateFromFile(Assembly.Load("netstandard, Version=2.0.0.0").Location);
    private static readonly MetadataReference SystemRuntimeReference = MetadataReference.CreateFromFile(Assembly.Load("System.Runtime, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location);

    /// <summary>
    /// Builds a compilation over <paramref name="sources"/> referencing corlib + Roslyn + the public
    /// <c>Synto.Core</c> marker surface. Each parsed tree must be free of parse diagnostics.
    /// </summary>
    public static CSharpCompilation CreateCompilation(params string[] sources)
    {
        var trees = sources.Select(source =>
        {
            var tree = CSharpSyntaxTree.ParseText(source);
            Assert.Empty(tree.GetDiagnostics());
            return tree;
        });

        return CSharpCompilation.Create("MatchTest",
            trees,
            references:
            [
                CorlibReference,
                NetStandardReference,
                SystemRuntimeReference,
                // Roslyn — matching markers (Capture<TNode>, Stmt) reference syntax types, and later tasks
                // emit matchers against Microsoft.CodeAnalysis, so keep the closure Roslyn-aware throughout.
                MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.SyntaxNode).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.CSharp.SyntaxKind).Assembly.Location),
                // The PUBLIC Synto.Core surface: the consumer snippet binds [Match<>] / MatchOption against
                // these public marker types (the internal injected copy is not referenceable cross-assembly).
                MetadataReference.CreateFromFile(SyntoCoreAssembly.Location),
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    /// <summary>
    /// Compiles <paramref name="source"/>, asserts it has no compile errors as plain C#, then runs
    /// <see cref="MatchFactorySourceGenerator"/> and returns the run result (diagnostics + generated trees).
    /// </summary>
    public static GeneratorDriverRunResult Run(string source)
    {
        var compilation = CreateCompilation(source);

        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MatchFactorySourceGenerator());
        return driver.RunGenerators(compilation).GetRunResult();
    }
}

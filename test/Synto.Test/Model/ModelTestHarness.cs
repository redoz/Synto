using System;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Synto.Test.Model;

/// <summary>
/// In-memory harness for the <c>[SyntoModel]</c> generator + surface, mirroring <c>MatchTestHarness</c>.
/// At Task 1 it exposes only <see cref="InjectedSurfaceSource"/> (runs <see cref="ModelSurfaceInjector"/>
/// over an empty compilation and returns the injected internal surface a real consumer receives). Later
/// tasks grow it with <c>Run</c>/<c>CreateCompilation</c> for the generator itself.
/// </summary>
internal static class ModelTestHarness
{
    private static readonly MetadataReference CorlibReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
    private static readonly MetadataReference NetStandardReference = MetadataReference.CreateFromFile(Assembly.Load("netstandard, Version=2.0.0.0").Location);
    private static readonly MetadataReference SystemRuntimeReference = MetadataReference.CreateFromFile(Assembly.Load("System.Runtime, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location);

    /// <summary>
    /// Runs <see cref="ModelSurfaceInjector"/> over an empty compilation and returns the single injected
    /// (post-init) source whose text contains <paramref name="contentMarker"/> — e.g.
    /// <c>"class SyntoModel&lt;T&gt;"</c> selects the injected <c>SyntoModel.g.cs</c>. This is the same
    /// surface a real consumer's generator project receives.
    /// </summary>
    public static string InjectedSurfaceSource(string contentMarker)
    {
        var compilation = CSharpCompilation.Create("ModelSurfaceInjectionProbe",
            syntaxTrees: Array.Empty<SyntaxTree>(),
            references:
            [
                CorlibReference,
                NetStandardReference,
                SystemRuntimeReference,
                MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.SyntaxNode).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.CSharp.SyntaxKind).Assembly.Location),
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var result = CSharpGeneratorDriver
            .Create(new ModelSurfaceInjector())
            .RunGenerators(compilation)
            .GetRunResult();

        return result.GeneratedTrees
            .Single(tree => tree.ToString().Contains(contentMarker, StringComparison.Ordinal))
            .ToString();
    }
}

using System;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Synto.Test.Model;

/// <summary>
/// In-memory harness for the <c>[SyntoModel]</c> generator + surface, mirroring <c>MatchTestHarness</c>.
/// <see cref="InjectedSurfaceSource"/> runs <see cref="ModelSurfaceInjector"/> over an empty compilation and
/// returns the injected internal surface a real consumer receives; <see cref="Run"/>/<see cref="CreateCompilation"/>
/// exercise the <see cref="SyntoModelGenerator"/> itself against the PUBLIC <c>Synto.Core</c> markers.
/// </summary>
internal static class ModelTestHarness
{
    private static readonly MetadataReference CorlibReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
    private static readonly MetadataReference NetStandardReference = MetadataReference.CreateFromFile(Assembly.Load("netstandard, Version=2.0.0.0").Location);
    private static readonly MetadataReference SystemRuntimeReference = MetadataReference.CreateFromFile(Assembly.Load("System.Runtime, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location);

    // The `Project` shim is authored under Synto.Core/Model/InjectedOnly (NOT compiled into the public
    // Synto.Core surface): a real consumer only ever sees it as the internal source ModelSurfaceInjector
    // injects into their generator compilation. So a spec that calls `Project.Each/One` cannot bind against the
    // public Synto.Core reference alone — add the injected `Project` source as a sibling tree so `Project.Each`
    // resolves in the test compilation exactly as it does after injection in a real build. Parsed once.
    private static readonly Lazy<SyntaxTree> InjectedProjectTree = new(
        static () => CSharpSyntaxTree.ParseText(InjectedSurfaceSource("class Project")));

    /// <summary>
    /// Builds a compilation over <paramref name="sources"/> referencing corlib + Roslyn + the public
    /// <c>Synto.Core</c> marker surface (so a consumer snippet binds <c>SyntoModel&lt;T&gt;</c> against the
    /// public marker). Each parsed tree must be free of parse diagnostics.
    /// </summary>
    public static CSharpCompilation CreateCompilation(params string[] sources)
    {
        var trees = sources.Select(source =>
        {
            var tree = CSharpSyntaxTree.ParseText(source);
            Assert.Empty(tree.GetDiagnostics());
            return tree;
        }).Append(InjectedProjectTree.Value);

        return CSharpCompilation.Create("ModelTest",
            trees,
            references:
            [
                CorlibReference,
                NetStandardReference,
                SystemRuntimeReference,
                // System.Collections.Immutable — nested-model getters project over `Symbol.GetMembers()` etc.,
                // whose return type is ImmutableArray<ISymbol>; a real generator project references it too.
                MetadataReference.CreateFromFile(typeof(System.Collections.Immutable.ImmutableArray).Assembly.Location),
                // Roslyn — a spec class projects over an ISymbol (e.g. SyntoModel<IPropertySymbol>), so the
                // consumer snippet binds Microsoft.CodeAnalysis symbol types.
                MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.SyntaxNode).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.CSharp.SyntaxKind).Assembly.Location),
                // The PUBLIC Synto.Core surface: [SyntoModel] / SyntoModel<T> bind against these public marker
                // types (the internal injected copy is not referenceable cross-assembly).
                MetadataReference.CreateFromFile(SyntoCoreAssembly.Location),
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    /// <summary>
    /// Compiles <paramref name="source"/>, asserts it has no compile errors as plain C#, then runs
    /// <see cref="SyntoModelGenerator"/> and returns the run result (diagnostics + generated trees).
    /// </summary>
    public static GeneratorDriverRunResult Run(string source)
    {
        var compilation = CreateCompilation(source);

        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SyntoModelGenerator());
        return driver.RunGenerators(compilation).GetRunResult();
    }

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

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Synto.Example.ObjectReader.Generator;

namespace Synto.Example.ObjectReader.Tests;

/// <summary>
/// In-memory generator-driver harness for <see cref="ObjectReaderGenerator"/>, mirroring
/// <c>test/Synto.Test</c>'s pattern: it builds a net10 compilation over <paramref name="source"/> referencing
/// the running framework closure (so <c>System.Data</c>, the BCL, and the ObjectReader API surface resolve),
/// enables C# interceptors, runs the generator through a <see cref="CSharpGeneratorDriver"/>, and snapshots
/// the result.
/// </summary>
internal static class GeneratorHarness
{
    private const string GeneratedNamespace = "Synto.Example.ObjectReader.Generated";

    /// <summary>
    /// Absolute path of the <c>Synto.Example.ObjectReader.Generator</c> project directory, resolved from this
    /// source file's compile-time location. Used by the Task 4 dog-food sentinel to assert the Synto
    /// <c>[Template]</c> skeleton (<c>ReaderTemplate.cs</c>) is present.
    /// </summary>
    public static string GeneratorProjectDir { get; } = ResolveGeneratorProjectDir();

    private static string ResolveGeneratorProjectDir([System.Runtime.CompilerServices.CallerFilePath] string thisFile = "")
    {
        // thisFile = …/Synto.Example.ObjectReader.Tests/GeneratorHarness.cs
        string testsDir = System.IO.Path.GetDirectoryName(thisFile)!;
        string exampleRoot = System.IO.Path.GetDirectoryName(testsDir)!;
        return System.IO.Path.Combine(exampleRoot, "Synto.Example.ObjectReader.Generator");
    }

    private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.Preview)
        .WithFeatures(new[] { new KeyValuePair<string, string>("InterceptorsNamespaces", GeneratedNamespace) });

    private static readonly IReadOnlyList<MetadataReference> References = BuildReferences();

    /// <summary>Runs <see cref="ObjectReaderGenerator"/> over <paramref name="source"/> and snapshots the driver.</summary>
    public static Task Verify(string source)
    {
        GeneratorDriver driver = RunDriver(source);
        return Verifier.Verify(driver).UseDirectory("snapshots");
    }

    /// <summary>
    /// Runs <see cref="ObjectReaderGenerator"/> over <paramref name="source"/>; returns the generator
    /// diagnostics and the concatenated generated text. (Same driver as <see cref="Verify"/>, different
    /// assertion — used by the diagnostics tests.)
    /// </summary>
    public static (ImmutableArray<Diagnostic> Diagnostics, string Generated) Run(string source)
    {
        GeneratorDriver driver = RunDriver(source);
        GeneratorDriverRunResult result = driver.GetRunResult();
        string generated = string.Join("\n", result.GeneratedTrees.Select(tree => tree.GetText().ToString()));
        return (result.Diagnostics, generated);
    }

    /// <summary>
    /// Runs <see cref="ObjectReaderGenerator"/> over <paramref name="source"/> with incremental step tracking
    /// enabled, then re-runs the same driver after adding one UNRELATED syntax tree, and returns the second
    /// run's single generator result. A cacheability guard (C-5): because the transform flows only an equatable
    /// <c>ObjectReaderModel</c> (no rooted Compilation/ISymbol/SemanticModel/SyntaxNode), the call site's tree
    /// is byte-identical across both runs, so the tracked steps must come from cache rather than re-projecting.
    /// </summary>
    public static GeneratorRunResult RunIncremental(string source)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "ObjectReaderSnapshot",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source, ParseOptions, path: "Source.cs") },
            references: References,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { new ObjectReaderGenerator().AsSourceGenerator() },
            additionalTexts: null,
            parseOptions: ParseOptions,
            optionsProvider: null,
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation);

        // An edit in a separate, unrelated tree leaves Source.cs untouched, so the equatable model produced
        // for the Create call site must be reused — Cached (step not re-run) or Unchanged (re-run, equal value).
        Compilation modified = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("namespace Other { internal sealed class Unrelated { } }", ParseOptions));
        driver = driver.RunGenerators(modified);

        return driver.GetRunResult().Results.Single();
    }

    private static GeneratorDriver RunDriver(string source)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "ObjectReaderSnapshot",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source, ParseOptions, path: "Source.cs") },
            references: References,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { new ObjectReaderGenerator().AsSourceGenerator() },
            additionalTexts: null,
            parseOptions: ParseOptions);

        return driver.RunGenerators(compilation);
    }

    private static List<MetadataReference> BuildReferences()
    {
        var trustedAssemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        return trustedAssemblies
            .Split(System.IO.Path.PathSeparator)
            .Where(static path => path.Length > 0)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
    }
}

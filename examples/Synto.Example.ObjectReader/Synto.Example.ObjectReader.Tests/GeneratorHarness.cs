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

using System.Collections.Immutable;
using System.IO;
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

    /// <summary>
    /// Asserts a captured node's text equals <paramref name="expected"/>, normalized — the single routing
    /// point for every <c>m.X == "foo()"</c>-style round-trip assertion (mirrors Templating's
    /// <c>AssertGenerated</c>). A captured node carries the parsed input's trivia, so a raw <c>.ToString()</c>
    /// <c>==</c> is trivia-fragile; normalize whitespace + trim + canonicalize line endings instead. Used from
    /// Task 5 on (the first capturing round-trips land at Task 6).
    /// </summary>
    public static void AssertCapture(string expected, SyntaxNode captured)
    {
        var actual = captured.NormalizeWhitespace().ToString().Trim().Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal(expected.Replace("\r\n", "\n", StringComparison.Ordinal), actual);
    }

    // ----- C3 self-containment proof closures ------------------------------------------------------
    //
    // The proof needs a PINNED reference closure that LACKS System.Runtime.CompilerServices.IsExternalInit, so
    // the generated positional record's `init` modreq stays unresolved (CS0518) unless our polyfill supplies
    // the canonical type. We use the NETStandard.Library.Ref ref-pack (located via the NetStandardRefPath
    // assembly-metadata attribute the csproj injects) — explicitly NOT typeof(object).Assembly.Location (the
    // running net10 corlib) and NOT Assembly.Load("netstandard") (the runtime facade), both of which forward
    // to a corlib that DEFINES IsExternalInit and would pass the proof green even with a broken polyfill. The
    // `NetStandard20Closure_LacksIsExternalInit` self-check fact guards exactly that property.

    private static readonly Lazy<ImmutableArray<MetadataReference>> NetStandardClosureReferences = new(() =>
    {
        var refDirectory = ResolveNetStandardRefDirectory();
        var references = Directory.GetFiles(refDirectory, "*.dll")
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();

        // Roslyn — the generated matcher references only Microsoft.CodeAnalysis(.CSharp); no Synto runtime.
        references.Add(MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.SyntaxNode).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.CSharp.SyntaxKind).Assembly.Location));
        return references.ToImmutableArray();
    });

    private static readonly ImmutableArray<MetadataReference> NetWithBclClosureReferences = ImmutableArray.Create<MetadataReference>(
        // A BCL-present closure: the running net10 corlib DEFINES IsExternalInit, so the injected polyfill is
        // a redundant-but-harmless source copy (at most CS0436, a warning). "Compiles everywhere", other side.
        CorlibReference,
        NetStandardReference,
        SystemRuntimeReference,
        MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.SyntaxNode).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.CSharp.SyntaxKind).Assembly.Location));

    /// <summary>
    /// Compiles <paramref name="sources"/> (re-parsed at <see cref="LanguageVersion.Latest"/> so records lower
    /// to <c>{ get; init; }</c>) against the pinned netstandard reference closure — the closure that LACKS
    /// <c>IsExternalInit</c>.
    /// </summary>
    public static CSharpCompilation CreateNetStandardClosure(params string[] sources) =>
        CreateClosure("NetStandardProof", NetStandardClosureReferences.Value, sources);

    /// <summary>
    /// Compiles <paramref name="sources"/> against a BCL-present (net5.0+/net10) closure whose corlib already
    /// DEFINES <c>IsExternalInit</c>.
    /// </summary>
    public static CSharpCompilation CreateNetWithBclClosure(params string[] sources) =>
        CreateClosure("NetWithBclProof", NetWithBclClosureReferences, sources);

    /// <summary>Runs the generator and returns the generated matcher source (the tree carrying the partial target).</summary>
    public static string GeneratedMatcherSource(GeneratorDriverRunResult result) =>
        result.GeneratedTrees.Single(tree => tree.ToString().Contains("partial class", StringComparison.Ordinal)).ToString();

    /// <summary>Returns the once-per-assembly <c>IsExternalInit</c> post-init polyfill source.</summary>
    public static string GeneratedPolyfillSource(GeneratorDriverRunResult result) =>
        result.GeneratedTrees.Single(tree => tree.ToString().Contains("IsExternalInit", StringComparison.Ordinal)).ToString();

    private static CSharpCompilation CreateClosure(string assemblyName, ImmutableArray<MetadataReference> references, string[] sources)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var trees = sources.Select(source => CSharpSyntaxTree.ParseText(source, parseOptions));
        return CSharpCompilation.Create(assemblyName, trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static string ResolveNetStandardRefDirectory()
    {
        var metadata = typeof(MatchTestHarness).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "NetStandardRefPath");

        Assert.NotNull(metadata);
        Assert.False(string.IsNullOrEmpty(metadata!.Value), "NetStandardRefPath assembly metadata was empty");

        // The package lays its reference assemblies under ref/netstandard2.x/; pick the single ref TFM folder.
        var refRoot = Path.Combine(metadata.Value!, "ref");
        return Directory.GetDirectories(refRoot).Single();
    }
}

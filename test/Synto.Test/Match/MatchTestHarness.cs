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
    // The proof needs a PINNED reference closure FAITHFUL to what a real netstandard2.0 generator project sees,
    // and one that provides no ACCESSIBLE System.Runtime.CompilerServices.IsExternalInit — so the generated
    // positional record's `init` modreq stays unresolved (CS0518) unless our polyfill supplies the canonical
    // type. The closure is built from:
    //   1. The NETStandard.Library.Ref ref-pack (located via the NetStandardRefPath assembly-metadata
    //      attribute the csproj injects) — explicitly NOT typeof(object).Assembly.Location (the running net10
    //      corlib) and NOT Assembly.Load("netstandard") (the runtime facade), both of which forward to a corlib
    //      that DEFINES a PUBLIC IsExternalInit and would pass the proof green even with a broken polyfill.
    //   2. The netstandard2.0 BUILD of Roslyn 5.0.0 (lib/netstandard2.0/*.dll) — NOT the loaded net build. The
    //      loaded net build's System.Runtime 9.0 closure binds against the ns2.x BCL refs (System.Runtime
    //      4.1.2.0) at a skewed version and throws CS1705 on the incremental-provider API; the ns2.0 build is
    //      exactly what src/Synto.SourceGenerator (netstandard2.0) compiles against, so it is the faithful view.
    //   3. The four assemblies the ns2.0 Roslyn build references that the ref pack lacks (Immutable, Reflection
    //      .Metadata, CompilerServices.Unsafe, Encoding.CodePages), at the versions Roslyn 5.0.0 declares.
    // Roslyn's ns2.0 build legitimately embeds an INTERNAL IsExternalInit — unusable cross-assembly, so it does
    // NOT satisfy a consumer's `init` modreq and does NOT mask a broken polyfill. The
    // `NetStandard20Closure_LacksAccessibleIsExternalInit` self-check fact guards that no PUBLIC one sneaks in.

    private static readonly Lazy<ImmutableArray<MetadataReference>> NetStandardClosureReferences = new(() =>
    {
        var refDirectory = ResolveNetStandardRefDirectory();
        var references = Directory.GetFiles(refDirectory, "*.dll")
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();

        // Roslyn — the netstandard2.0 build (not the loaded net build; see the note above). The generated
        // matcher + injected ForMatch surface reference only Microsoft.CodeAnalysis(.CSharp); no Synto runtime.
        references.Add(ResolvePackageDll("MicrosoftCodeAnalysisCommonPath", "lib/netstandard2.0/Microsoft.CodeAnalysis.dll"));
        references.Add(ResolvePackageDll("MicrosoftCodeAnalysisCSharpPath", "lib/netstandard2.0/Microsoft.CodeAnalysis.CSharp.dll"));

        // The four deps the ns2.0 Roslyn build references that the ref pack lacks (>= the versions Roslyn 5.0.0
        // declares for its .NETStandard2.0 dependency group). Without these the incremental-provider API binds
        // against a skewed System.Runtime and throws CS1705.
        references.Add(ResolvePackageDll("SystemCollectionsImmutablePath", "lib/netstandard2.0/System.Collections.Immutable.dll"));
        references.Add(ResolvePackageDll("SystemReflectionMetadataPath", "lib/netstandard2.0/System.Reflection.Metadata.dll"));
        references.Add(ResolvePackageDll("SystemRuntimeCompilerServicesUnsafePath", "lib/netstandard2.0/System.Runtime.CompilerServices.Unsafe.dll"));
        references.Add(ResolvePackageDll("SystemTextEncodingCodePagesPath", "lib/netstandard2.0/System.Text.Encoding.CodePages.dll"));
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

    /// <summary>
    /// Runs <see cref="SurfaceInjectionGenerator"/> over an empty compilation and returns the single injected
    /// (post-init) source whose text contains <paramref name="contentMarker"/> — e.g.
    /// <c>"readonly struct MatchPattern"</c> selects the injected data surface <c>ForMatchHelpers.g.cs</c>.
    /// This is the same surface a real consumer's generator project receives.
    /// </summary>
    public static string InjectedSurfaceSource(string contentMarker)
    {
        var compilation = CSharpCompilation.Create("SurfaceInjectionProbe",
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
            .Create(new SurfaceInjectionGenerator())
            .RunGenerators(compilation)
            .GetRunResult();

        return result.GeneratedTrees
            .Single(tree => tree.ToString().Contains(contentMarker, StringComparison.Ordinal))
            .ToString();
    }

    /// <summary>
    /// Runs an in-test consumer <paramref name="generator"/> (one that hooks its pipeline via
    /// <c>ForMatch</c>) over <paramref name="source"/> and returns the generated source texts. Mirrors a real
    /// consumer generator project: only the consumer generator runs; the matcher members it references are
    /// already present on this assembly's <c>M</c> target.
    /// </summary>
    public static IReadOnlyList<string> RunConsumerGenerator(IIncrementalGenerator generator, string source)
    {
        var compilation = CreateCompilation(source);
        var driver = CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
        return driver.GetRunResult().GeneratedTrees.Select(tree => tree.ToString()).ToList();
    }

    /// <summary>
    /// Runs an in-test consumer <paramref name="generator"/> over <paramref name="source"/> with step-tracking
    /// on, then re-runs after adding an UNRELATED tree (<paramref name="unrelatedSource"/>) — the cacheability
    /// fixture. Returns both run results so a test can assert the consumer's tracked steps stay
    /// <c>Cached</c>/<c>Unchanged</c> on the second run (mirrors <c>Generator_IsIncremental_OnUnrelatedEdit</c>).
    /// </summary>
    public static (GeneratorDriverRunResult First, GeneratorDriverRunResult Second) RunConsumerGeneratorTwice(
        IIncrementalGenerator generator, string source, string unrelatedSource)
    {
        var compilation = CreateCompilation(source);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { generator.AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation);
        var first = driver.GetRunResult();

        var modified = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(unrelatedSource));
        driver = driver.RunGenerators(modified);
        var second = driver.GetRunResult();

        return (first, second);
    }

    /// <summary>
    /// The C-FM3 self-containment proof for the injected <c>ForMatch</c> surface: compiles the injected
    /// <c>ForMatchHelpers</c> source plus the <c>IsExternalInit</c> polyfill on the pinned netstandard2.0
    /// closure (the closure that LACKS <c>IsExternalInit</c>, so the <c>Matched&lt;T&gt;</c> record struct's
    /// <c>init</c> members exercise the polyfill). Returns the resulting diagnostics.
    /// </summary>
    public static ImmutableArray<Diagnostic> CompileInjectedForMatchSurfaceOnNetStandard20()
    {
        var injected = InjectedSurfaceSource("readonly struct MatchPattern");
        var polyfill = GeneratedPolyfillSource(Run(
            """
            using Synto.Matching;

            partial class M { }

            public class Consumer
            {
                [Match<M>(MatchOption.Single)]
                static object Sum([Capture] int a, [Capture] int b) => a + b;
            }
            """));

        return CreateNetStandardClosure(injected, polyfill).GetDiagnostics();
    }

    /// <summary>
    /// The C-FM3 self-containment proof for the injected <c>ForMatch</c> EXTENSION surface: compiles the
    /// injected <c>SyntoMatchProviderExtensions</c> wrappers over the Roslyn incremental-provider API on the
    /// FAITHFUL netstandard2.0 closure (the ns2.0 Roslyn build + its deps), alongside the data surface they
    /// reference and the <c>IsExternalInit</c> polyfill the <c>Matched&lt;T&gt;</c> record struct needs. Returns
    /// the resulting diagnostics — they must be error-free, proving the extensions are self-contained on
    /// netstandard2.0 (no Synto runtime-package dependency, no version-skewed Roslyn).
    /// </summary>
    public static ImmutableArray<Diagnostic> CompileInjectedForMatchExtensionsOnNetStandard20()
    {
        var dataSurface = InjectedSurfaceSource("readonly struct MatchPattern");
        var extensions = InjectedSurfaceSource("class SyntoMatchProviderExtensions");
        var polyfill = GeneratedPolyfillSource(Run(
            """
            using Synto.Matching;

            partial class M { }

            public class Consumer
            {
                [Match<M>(MatchOption.Single)]
                static object Sum([Capture] int a, [Capture] int b) => a + b;
            }
            """));

        return CreateNetStandardClosure(dataSurface, extensions, polyfill).GetDiagnostics();
    }

    /// <summary>
    /// C-R4 self-containment proof for the injected Replace surface: compiles the injected
    /// SyntoMatchReplaceExtensions + ReplaceOption alongside the data surface and the IsExternalInit polyfill
    /// on the FAITHFUL netstandard2.0 closure. Returns the resulting diagnostics (must be error-free).
    /// </summary>
    public static ImmutableArray<Diagnostic> CompileInjectedMatchReplaceSurfaceOnNetStandard20()
    {
        var dataSurface = InjectedSurfaceSource("readonly struct MatchPattern");
        var replaceOption = InjectedSurfaceSource("enum ReplaceOption");
        var replaceExtensions = InjectedSurfaceSource("class SyntoMatchReplaceExtensions");
        var polyfill = GeneratedPolyfillSource(Run(
            """
            using Synto.Matching;

            partial class M { }

            public class Consumer
            {
                [Match<M>(MatchOption.Single)]
                static object Sum([Capture] int a, [Capture] int b) => a + b;
            }
            """));

        return CreateNetStandardClosure(dataSurface, replaceOption, replaceExtensions, polyfill).GetDiagnostics();
    }

    private static CSharpCompilation CreateClosure(string assemblyName, ImmutableArray<MetadataReference> references, string[] sources)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var trees = sources.Select(source => CSharpSyntaxTree.ParseText(source, parseOptions));
        return CSharpCompilation.Create(assemblyName, trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static string ResolveNetStandardRefDirectory()
    {
        // The package lays its reference assemblies under ref/netstandard2.x/; pick the single ref TFM folder.
        var refRoot = Path.Combine(ResolvePackageRoot("NetStandardRefPath"), "ref");
        return Directory.GetDirectories(refRoot).Single();
    }

    /// <summary>
    /// Resolves a NuGet package's restored root from the <see cref="AssemblyMetadataAttribute"/> the test
    /// csproj injects (surfaced from a <c>GeneratePathProperty</c> <c>Pkg*</c> property), then combines it with
    /// <paramref name="relativePath"/> into a <see cref="MetadataReference"/>. Machine-independent — mirrors
    /// the <c>NetStandardRefPath</c> mechanism, so no absolute ~/.nuget path is hardcoded in test code.
    /// </summary>
    private static PortableExecutableReference ResolvePackageDll(string metadataKey, string relativePath)
    {
        var dllPath = Path.Combine(ResolvePackageRoot(metadataKey), relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(dllPath), $"Expected package assembly not found for '{metadataKey}': {dllPath}");
        return MetadataReference.CreateFromFile(dllPath);
    }

    private static string ResolvePackageRoot(string metadataKey)
    {
        var metadata = typeof(MatchTestHarness).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == metadataKey);

        Assert.NotNull(metadata);
        Assert.False(string.IsNullOrEmpty(metadata!.Value), $"{metadataKey} assembly metadata was empty");
        return metadata.Value!;
    }
}

using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Synto.Diagnostics.Test;

public class DiagnosticsGeneratorTest
{
    private static readonly MetadataReference CorlibReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
    private static readonly MetadataReference NetStandardReference = MetadataReference.CreateFromFile(Assembly.Load("netstandard, Version=2.0.0.0").Location);
    private static readonly MetadataReference SystemRuntimeReference = MetadataReference.CreateFromFile(Assembly.Load("System.Runtime, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location);

    [Fact]
    public Task WithFileScopedNamespace()
    {
        var driver = GeneratorDriver(
            """
            using Microsoft.CodeAnalysis;
            using Synto.Diagnostics;

            namespace X.Y.Z;

            internal static partial class Diagnostics {
                private const string IdPrefix = "TST";

                [Diagnostic(IdPrefix + "0000", "Internal Error", "Unhandled exception {0} was thrown: {1}", "Synto.Internal", DiagnosticSeverity.Error, true)]
                public static partial Diagnostic InternalError(Location location, string exceptionType, string exceptionMessage);
            }

            """);

        return Verify(driver).UseDirectory("snapshots");
    }

    [Fact]
    public Task WithBlockScopedNamespaces()
    {
        var driver = GeneratorDriver(
            """
            using Microsoft.CodeAnalysis;
            using Synto.Diagnostics;

            namespace X.Y {
                namespace Z {
                    internal static partial class Diagnostics {
                        private const string IdPrefix = "TST";

                        [Diagnostic(IdPrefix + "0000", "Internal Error", "Unhandled exception {0} was thrown: {1}", "Synto.Internal", DiagnosticSeverity.Error, true)]
                        public static partial Diagnostic InternalError(Location location, string exceptionType, string exceptionMessage);
                    }
                }
            }
            """);

        return Verify(driver).UseDirectory("snapshots");
    }

    [Fact]
    public void TargetNotPartialReportsSDG1001WithLocationAndMethodName()
    {
        // A method carrying [Diagnostic] but not declared partial. This is the exact case that the SDG1001
        // argument-order bug mangled: the diagnostic must carry a real span AND name the method in {0}.
        var diagnostics = RunAndGetDiagnostics(
            """
            using Microsoft.CodeAnalysis;
            using Synto.Diagnostics;

            namespace X;

            internal static partial class Diags {
                [Diagnostic("TST0001", "Title", "msg {0}", "Cat", DiagnosticSeverity.Error, true)]
                public static Diagnostic NotPartial(Location location, string arg);
            }
            """);

        var diag = Assert.Single(diagnostics, d => d.Id == "SDG1001");

        // C3 regression guards: the span must be a real source location, not Location.None. (Serialized-then-
        // reconstructed locations are ExternalFile, so SourceTree is null by design, as in the templating
        // generator; assert the squiggle still carries a real, non-empty source span + path.)
        Assert.NotEqual(Location.None, diag.Location);
        Assert.False(diag.Location.SourceSpan.IsEmpty);
        Assert.NotEmpty(diag.Location.GetLineSpan().Path);
        // ... and {0} must render the method name, not a Location dump.
        Assert.Contains("NotPartial", diag.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void TargetInStructReportsSDG1003NotInternalError()
    {
        // A [Diagnostic] partial method in a struct (a legal partial-method container). The old unconditional
        // (ClassDeclarationSyntax) cast threw InvalidCastException, surfacing as an opaque SDG0000. It must
        // now produce the precise, located SDG1003 instead.
        var diagnostics = RunAndGetDiagnostics(
            """
            using Microsoft.CodeAnalysis;
            using Synto.Diagnostics;

            namespace X;

            internal partial struct Diags {
                [Diagnostic("TST0001", "Title", "msg {0}", "Cat", DiagnosticSeverity.Error, true)]
                public static partial Diagnostic InStruct(Location location, string arg);
            }
            """);

        var diag = Assert.Single(diagnostics, d => d.Id == "SDG1003");
        // Serialized-then-reconstructed locations are ExternalFile (SourceTree is null by design, as in the
        // templating generator); assert the squiggle still carries a real, non-empty source span + path.
        Assert.NotEqual(Location.None, diag.Location);
        Assert.False(diag.Location.SourceSpan.IsEmpty);
        Assert.NotEmpty(diag.Location.GetLineSpan().Path);
        Assert.Contains("InStruct", diag.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);

        // C4 regression guard: no opaque internal error.
        Assert.DoesNotContain(diagnostics, d => d.Id == "SDG0000");
    }

    [Fact]
    public void TargetInRecordReportsSDG1003NotInternalError()
    {
        var diagnostics = RunAndGetDiagnostics(
            """
            using Microsoft.CodeAnalysis;
            using Synto.Diagnostics;

            namespace X;

            internal partial record Diags {
                [Diagnostic("TST0001", "Title", "msg {0}", "Cat", DiagnosticSeverity.Error, true)]
                public static partial Diagnostic InRecord(Location location, string arg);
            }
            """);

        var diag = Assert.Single(diagnostics, d => d.Id == "SDG1003");
        // Serialized-then-reconstructed locations are ExternalFile (SourceTree is null by design, as in the
        // templating generator); assert the squiggle still carries a real, non-empty source span + path.
        Assert.NotEqual(Location.None, diag.Location);
        Assert.False(diag.Location.SourceSpan.IsEmpty);
        Assert.NotEmpty(diag.Location.GetLineSpan().Path);
        Assert.Contains("InRecord", diag.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.DoesNotContain(diagnostics, d => d.Id == "SDG0000");
    }

    [Fact]
    public void AncestorNotPartialReportsSDG1002WithLocation()
    {
        var diagnostics = RunAndGetDiagnostics(
            """
            using Microsoft.CodeAnalysis;
            using Synto.Diagnostics;

            namespace X;

            internal static class Outer {
                internal static partial class Inner {
                    [Diagnostic("TST0001", "Title", "msg {0}", "Cat", DiagnosticSeverity.Error, true)]
                    public static partial Diagnostic Nested(Location location, string arg);
                }
            }
            """);

        var diag = Assert.Single(diagnostics, d => d.Id == "SDG1002");
        // Serialized-then-reconstructed locations are ExternalFile (SourceTree is null by design, as in the
        // templating generator); assert the squiggle still carries a real, non-empty source span + path.
        Assert.NotEqual(Location.None, diag.Location);
        Assert.False(diag.Location.SourceSpan.IsEmpty);
        Assert.NotEmpty(diag.Location.GetLineSpan().Path);
        Assert.Contains("Outer", diag.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Contains("Nested", diag.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedAttributeReportsSDG0000InternalError()
    {
        // [Diagnostic] with no argument list reaches ProcessTarget, where AttributeSyntax.ArgumentList! throws.
        // The generator must convert any such exception to the SDG0000 catch-all rather than crash the host.
        var diagnostics = RunAndGetDiagnostics(
            """
            using Microsoft.CodeAnalysis;
            using Synto.Diagnostics;

            namespace X;

            internal static partial class Diags {
                [Diagnostic]
                public static partial Diagnostic NoArgs(Location location, string arg);
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "SDG0000");
    }

    [Fact]
    public void GeneratorIsIncrementalOnUnrelatedEdit()
    {
        // P1 cacheability guard: with the pipeline carrying only equatable value types (and no captured
        // Compilation/SemanticModel/SyntaxNode), an unrelated edit must leave every tracked step cached.
        const string source =
            """
            using Microsoft.CodeAnalysis;
            using Synto.Diagnostics;

            namespace X.Y.Z;

            internal static partial class Diagnostics {
                private const string IdPrefix = "TST";

                [Diagnostic(IdPrefix + "0000", "Internal Error", "Unhandled exception {0} was thrown: {1}", "Synto.Internal", DiagnosticSeverity.Error, true)]
                public static partial Diagnostic InternalError(Location location, string exceptionType, string exceptionMessage);
            }
            """;

        var compilation = CreateCompilation(source);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { new DiagnosticsGenerator().AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);

        // An unrelated edit in a separate tree: the [Diagnostic] tree is byte-identical, so its results must
        // come from cache.
        var modified = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("namespace Other { internal sealed class Unrelated { } }", cancellationToken: TestContext.Current.CancellationToken));
        driver = driver.RunGenerators(modified, TestContext.Current.CancellationToken);

        var result = driver.GetRunResult().Results.Single();

        CacheabilityAssert.AllStepsCachedOrUnchanged(
            result,
            new[] { TrackingNames.Transform, TrackingNames.Result });
    }

    [Fact]
    public void OverloadedDiagnosticMethodsProduceDistinctHintsNoCollision()
    {
        // Two [Diagnostic] partial methods share a name but differ in arity. With ParameterOptions.None and no
        // arity suffix, both hashed to the same hint name and the second AddSource threw ArgumentException,
        // surfacing as an opaque SDG0000 / host crash. Each overload must now get a distinct hint.
        var compilation = CreateCompilation(
            """
            using Microsoft.CodeAnalysis;
            using Synto.Diagnostics;

            namespace X;

            internal static partial class Diags {
                [Diagnostic("TST0001", "Title", "msg {0}", "Cat", DiagnosticSeverity.Error, true)]
                public static partial Diagnostic Dup(Location location, string arg);

                [Diagnostic("TST0002", "Title", "msg {0} {1}", "Cat", DiagnosticSeverity.Error, true)]
                public static partial Diagnostic Dup(Location location, string arg, int extra);
            }
            """);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new DiagnosticsGenerator());
        var runResult = driver.RunGenerators(compilation, TestContext.Current.CancellationToken).GetRunResult().Results.Single();

        Assert.Null(runResult.Exception);
        Assert.DoesNotContain(runResult.Diagnostics, d => d.Id == "SDG0000");

        var hints = runResult.GeneratedSources.Select(s => s.HintName).ToArray();
        Assert.Equal(hints.Length, hints.Distinct().Count());
        Assert.Equal(2, hints.Count(h => h.Contains("Dup", StringComparison.Ordinal)));
    }

    [Fact]
    public void GeneratedPartialIsCleanShellNoClonedAttributesBaseListOrPrimaryCtor()
    {
        // The container carries an AllowMultiple=false attribute, a base list, and a C# 12 primary constructor.
        // Cloning parent.WithMembers would replicate all three into the generated partial: CS0579 (duplicate
        // ExcludeFromCodeCoverage) plus a duplicate primary constructor. The generated partial must be a clean
        // shell — identifier + modifiers (+ type params/constraints) only.
        const string source =
            """
            using Microsoft.CodeAnalysis;
            using Synto.Diagnostics;
            using System.Diagnostics.CodeAnalysis;

            namespace X;

            public interface IMarker {}

            [ExcludeFromCodeCoverage]
            internal partial class Diags(int seed) : IMarker {
                private readonly int _seed = seed;

                [Diagnostic("TST0001", "Title", "msg {0}", "Cat", DiagnosticSeverity.Error, true)]
                public static partial Diagnostic Report(Location location, string arg);
            }
            """;

        var compilation = CreateCompilation(source);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new DiagnosticsGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _, TestContext.Current.CancellationToken);

        // The generated partial must not re-clone the container's attribute / base list / primary-constructor.
        var runResult = driver.GetRunResult().Results.Single();
        var generated = runResult.GeneratedSources.Single(s => s.SourceText.ToString().Contains("Report", StringComparison.Ordinal)).SourceText.ToString();
        Assert.DoesNotContain("ExcludeFromCodeCoverage", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("IMarker", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("seed", generated, StringComparison.Ordinal);

        // Post-generation, the two partials must compile together: none of the duplicate-declaration errors the
        // clone produced (CS0579 duplicate attribute, CS8863 duplicate primary ctor) may appear. (Unrelated
        // reference/entry-point diagnostics from this bare compilation are ignored — not this task's concern.)
        var errors = outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.DoesNotContain(errors, d => d.Id is "CS0579" or "CS8863");
    }

    [Fact]
    public void InjectedAttributeSourceHasNoCarriageReturnsCheckoutIndependent()
    {
        // The post-init DiagnosticsAttribute raw-string literal inherits the generator source file's checkout
        // line endings. On a CRLF checkout it emits \r\n, so Windows re-accepts write CRLF while CI writes LF —
        // a determinism defect (flapping snapshots). The injected source must be normalized to LF regardless of
        // the checkout's line endings.
        var compilation = CreateCompilation("namespace X;");

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new DiagnosticsGenerator());
        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);

        var injected = driver.GetRunResult().Results.Single()
            .GeneratedSources
            .Single(s => s.HintName.Contains("DiagnosticsAttribute", StringComparison.Ordinal))
            .SourceText.ToString();

        Assert.DoesNotContain('\r', injected);
    }

    [Fact]
    public void UnsupportedReturnTypeReportsSDG1004NotInternalError()
    {
        // A [Diagnostic] factory declared to return neither Microsoft.CodeAnalysis.Diagnostic nor
        // Synto.Generators.DiagnosticInfo (here: string). No emit strategy matches, so the generator must
        // report the precise, located SDG1004 rather than silently falling through to a live-shaped body.
        var diagnostics = RunAndGetDiagnostics(
            """
            using Microsoft.CodeAnalysis;
            using Synto.Diagnostics;

            namespace X;

            internal static partial class Diags
            {
                [Diagnostic("TST0001", "Title", "msg {0}", "Cat", DiagnosticSeverity.Error, true)]
                public static partial string Wrong(Location location, string arg);
            }
            """);

        var diag = Assert.Single(diagnostics, d => d.Id == "SDG1004");
        // Serialized-then-reconstructed locations are ExternalFile (SourceTree is null by design); assert the
        // squiggle still carries a real, non-empty source span pointing at the return type.
        Assert.NotEqual(Location.None, diag.Location);
        Assert.False(diag.Location.SourceSpan.IsEmpty);
        Assert.Contains("Wrong", diag.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.DoesNotContain(diagnostics, d => d.Id == "SDG0000");
    }

    private const string CacheableSource =
        """
        using Synto.Generators;
        using Synto.Diagnostics;

        namespace X;

        internal static partial class Diagnostics
        {
            [Diagnostic("TST0001", "Unsupported property type", "Property '{0}' has unsupported type '{1}'", "MyGen.Usage", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning, true)]
            public static partial DiagnosticInfo UnsupportedProp(LocationInfo? location, string name, string type);
        }
        """;

    private static (CSharpCompilation compilation, GeneratorDriver driver) RunCacheable(string source, bool trackSteps = false)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: "Cacheable.cs");
        var refs = Directory
            .EnumerateFiles(Path.GetDirectoryName(typeof(CSharpSyntaxVisitor<>).Assembly.Location)!, "*.dll")
            .Select(f => (MetadataReference)MetadataReference.CreateFromFile(f))
            .Concat([
                CorlibReference,
                NetStandardReference,
                SystemRuntimeReference,
                // The injected trio (EquatableArray / DiagnosticBag) references ImmutableArray<T>.
                MetadataReference.CreateFromFile(typeof(ImmutableArray).Assembly.Location)
            ]);
        var compilation = CSharpCompilation.Create("CacheableTest", [tree], refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new IIncrementalGenerator[]
                { new global::Synto.Generators.Incremental.IncrementalSurfaceInjector(), new DiagnosticsGenerator() }
                .Select(g => g.AsSourceGenerator()),
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: trackSteps));
        return (compilation, driver.RunGenerators(compilation, TestContext.Current.CancellationToken));
    }

    [Fact]
    public Task CacheableReturnTypeEmitsDiagnosticInfoFactory()
    {
        var (_, driver) = RunCacheable(CacheableSource);
        return Verify(driver).UseDirectory("snapshots");
    }

    [Fact]
    public void CacheableFactoryCompilesAgainstInjectedTrioNoErrors()
    {
        var (compilation, driver) = RunCacheable(CacheableSource);
        var updated = driver.RunGeneratorsAndUpdateCompilation(
            compilation, out var output, out _, TestContext.Current.CancellationToken);
        var errors = output.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.DoesNotContain(errors, d => d.Id is "CS0246" or "CS0117" or "CS8795"); // trio resolves; partial is implemented
    }

    [Fact]
    public void CacheableGeneratorIsIncrementalOnUnrelatedEdit()
    {
        var (compilation, driver) = RunCacheable(CacheableSource, trackSteps: true);
        var modified = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("namespace Z { class Unrelated { } }",
                cancellationToken: TestContext.Current.CancellationToken));
        var result = driver.RunGenerators(modified, TestContext.Current.CancellationToken)
            .GetRunResult().Results.Single(r => r.Generator.GetGeneratorType() == typeof(DiagnosticsGenerator));

        CacheabilityAssert.AllStepsCachedOrUnchanged(
            result, new[] { TrackingNames.Transform, TrackingNames.Result });
    }

    [Fact]
    public void EndToEndDiagnosticsRideCacheAndMaterializeAtSourceOutput()
    {
        // The consumer source declares a cacheable [Diagnostic] factory. Running both generators (trio injector
        // + DiagnosticsGenerator) proves the end-to-end story: the generated partial returns a value-equatable
        // DiagnosticInfo (NOT a live Diagnostic.Create) that rides the incremental cache and only materializes a
        // live Diagnostic via ToDiagnostic() / Drain at source-output time.
        const string source =
            """
            using Synto.Generators;
            using Synto.Diagnostics;

            namespace X;

            internal static partial class Diags
            {
                [Diagnostic("MYG001", "Unsupported property type", "Property '{0}' has unsupported type '{1}'", "MyGen.Usage", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning, true)]
                public static partial DiagnosticInfo UnsupportedProp(LocationInfo? location, string name, string type);
            }
            """;

        var (_, driver) = RunCacheable(source);
        var diags = driver.GetRunResult().Diagnostics;

        // The generator itself reports none (well-formed); the cacheable factory is available for consumers.
        Assert.DoesNotContain(diags, d => d.Id is "SDG0000" or "SDG1004");

        // The generated partial returns a DiagnosticInfo whose ToDiagnostic() rehydrates MYG001 with a real span.
        var generated = driver.GetRunResult().Results
            .SelectMany(r => r.GeneratedSources)
            .Single(s => s.SourceText.ToString().Contains("UnsupportedProp", StringComparison.Ordinal))
            .SourceText.ToString();
        Assert.Contains("new DiagnosticInfo(", generated, StringComparison.Ordinal);
        Assert.Contains("EquatableArray<string>", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("Diagnostic.Create", generated, StringComparison.Ordinal); // NOT the live form
    }

    static ImmutableArray<Diagnostic> RunAndGetDiagnostics(string source)
    {
        var compilation = CreateCompilation(source);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new DiagnosticsGenerator());
        driver = driver.RunGenerators(compilation);

        return driver.GetRunResult().Diagnostics;
    }

    static GeneratorDriver GeneratorDriver(string source)
    {
        var compilation = CreateCompilation(source);

        var generator = new DiagnosticsGenerator();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        return driver.RunGenerators(compilation);
    }

    static CSharpCompilation CreateCompilation(params string[] sources)
    {
        // Give each tree a path so reconstructed (serializable) diagnostic locations carry a real file path,
        // as they would in a real compilation.
        var syntaxTrees = sources.Select((source, i) => CSharpSyntaxTree.ParseText(source, path: $"Source{i}.cs")).ToArray();

        var outputPath = Path.GetDirectoryName(typeof(CSharpSyntaxVisitor<>).GetTypeInfo().Assembly.Location)!;
        var allFiles = Directory.EnumerateFiles(outputPath, "*.dll", SearchOption.TopDirectoryOnly);
        return CSharpCompilation.Create("Test",
            syntaxTrees,
            allFiles.Select(file => MetadataReference.CreateFromFile(file)).Union(
                [
                    CorlibReference,
                    NetStandardReference,
                    SystemRuntimeReference,
                    MetadataReference.CreateFromFile(typeof(global::Synto.Templating.TemplateAttribute).Assembly.Location)
                ]
            )
        );
    }
}

using System.Collections.Immutable;
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
    public void TargetNotPartial_ReportsSDG1001_WithLocationAndMethodName()
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
        Assert.Contains("NotPartial", diag.GetMessage());
    }

    [Fact]
    public void TargetInStruct_ReportsSDG1003_NotInternalError()
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
        Assert.Contains("InStruct", diag.GetMessage());

        // C4 regression guard: no opaque internal error.
        Assert.DoesNotContain(diagnostics, d => d.Id == "SDG0000");
    }

    [Fact]
    public void TargetInRecord_ReportsSDG1003_NotInternalError()
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
        Assert.Contains("InRecord", diag.GetMessage());
        Assert.DoesNotContain(diagnostics, d => d.Id == "SDG0000");
    }

    [Fact]
    public void AncestorNotPartial_ReportsSDG1002_WithLocation()
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
        Assert.Contains("Outer", diag.GetMessage());
        Assert.Contains("Nested", diag.GetMessage());
    }

    [Fact]
    public void MalformedAttribute_ReportsSDG0000_InternalError()
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
    public void Generator_IsIncremental_OnUnrelatedEdit()
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

        driver = driver.RunGenerators(compilation);

        // An unrelated edit in a separate tree: the [Diagnostic] tree is byte-identical, so its results must
        // come from cache.
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

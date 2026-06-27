using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Synto.Test.Templating;

public class SimpleTemplateTest
{
    private static readonly MetadataReference CorlibReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
    private static readonly MetadataReference NetStandardReference = MetadataReference.CreateFromFile(Assembly.Load("netstandard, Version=2.0.0.0").Location);
    private static readonly MetadataReference SystemRuntimeReference = MetadataReference.CreateFromFile(Assembly.Load("System.Runtime, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location);


    static CSharpCompilation CreateCompilation()
    {


        return CSharpCompilation.Create("Test",
            Array.Empty<SyntaxTree>(),
            references:
            [
                CorlibReference,
                NetStandardReference,
                SystemRuntimeReference,
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                // Reference the PUBLIC Synto.Core surface (via the SyntoCore extern alias). The
                // in-memory consumer source uses [Template] etc. against these public types; the
                // internal copy injected into THIS test assembly is not referenceable cross-assembly.
                MetadataReference.CreateFromFile(SyntoCoreAssembly.Location)
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
    }

    static CSharpGeneratorDriver CreateDriver()
    {
        var generator = new TemplateFactorySourceGenerator();
        return CSharpGeneratorDriver.Create(generator);
    }

    private readonly Compilation _baseCompilation;
    private readonly GeneratorDriver _driver;

    public SimpleTemplateTest()
    {
        // probably shift this into a class fixture for perf
        _baseCompilation = CreateCompilation();
        _driver = CreateDriver();
    }

    private Compilation CompilationWithSource(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        Assert.Empty(syntaxTree.GetDiagnostics());

        var compilation = _baseCompilation.AddSyntaxTrees(syntaxTree);
        //var compilation = _baseCompilation.Add
        return compilation;
    }

    private async Task VerifyTemplate(string source)
    {
        var compilation = CompilationWithSource(source);
        Assert.Empty(compilation.GetDiagnostics().Where(diag => diag.Severity == DiagnosticSeverity.Error));
        var result = _driver.RunGenerators(compilation);
        //var diagnostics = result.GetRunResult();
        //Assert.Empty(diagnostics.Diagnostics);
        var ret = await Verify(result).UseDirectory("snapshots");
        Assert.NotEmpty(ret.Files);
    }

    [Fact]
    public async Task LocalFunctionAsSingle()
    {
        await VerifyTemplate(
            """
            using System;
            using Synto.Templating;

            partial class Factory {}

            public class TestClass {
                public void TestMethod() {
                    [Template(typeof(Factory), Options = TemplateOption.Single)]
                    void LocalFunction() {
                        Console.WriteLine("Hello world");
                    }
                }
            }
            """
        );
    }

    [Fact]
    public async Task LocalFunctionAsBare()
    {
        await VerifyTemplate(
            """
            using System;
            using Synto.Templating;

            partial class Factory {}

            public class TestClass {
                public void TestMethod() {
                    [Template(typeof(Factory), Options = TemplateOption.Bare)]
                    void LocalFunction() {
                        Console.WriteLine("Hello world");
                    }
                }
            }
            """
        );
    }

    [Fact]
    public async Task LocalFunctionAsDefault()
    {
        await VerifyTemplate(
            """
            using System;
            using Synto.Templating;

            partial class Factory {}

            public class TestClass {
                public void TestMethod() {
                    [Template(typeof(Factory), Options = TemplateOption.None)]
                    void LocalFunction() {
                        Console.WriteLine("Hello world");
                    }
                }
            }
            """
        );
    }

    [Fact]
    public async Task StaticLocalFunctionAsSingle()
    {
        await VerifyTemplate(
            """
            using System;
            using Synto.Templating;

            partial class Factory {}

            public class TestClass {
                public void TestMethod() {
                    [Template(typeof(Factory), Options = TemplateOption.Single)]
                    static void LocalFunction() {
                        Console.WriteLine("Hello world");
                    }
                }
            }
            """
        );
    }

    [Fact]
    public async Task StaticLocalFunctionAsBare()
    {
        await VerifyTemplate(
            """
            using System;
            using Synto.Templating;

            partial class Factory {}

            public class TestClass {
                public void TestMethod() {
                    [Template(typeof(Factory), Options = TemplateOption.Bare)]
                    static void LocalFunction() {
                        Console.WriteLine("Hello world");
                    }
                }
            }
            """
        );
    }

    [Fact]
    public async Task StaticLocalFunctionAsDefault()
    {
        await VerifyTemplate(
            """
            using System;
            using Synto.Templating;

            partial class Factory {}

            public class TestClass {
                public void TestMethod() {
                    [Template(typeof(Factory), Options = TemplateOption.None)]
                    static void LocalFunction() {
                        Console.WriteLine("Hello world");
                    }
                }
            }
            """
        );
    }


    [Fact]
    public async Task FunctionWithMultipleStatementAsSingle()
    {
        await VerifyTemplate(
            """
            using System;
            using Synto.Templating;

            partial class Factory {}

            public class TestClass {
                public void TestMethod() {
                    [Template(typeof(Factory), Options = TemplateOption.Single)]
                    void LocalFunction() {
                        Console.WriteLine("Hello world");
                        Console.WriteLine("Hello world");
                    }
                }
            }
            """
        );
    }

    [Fact]
    public async Task FunctionAsSingle()
    {
        await VerifyTemplate(
            """
            using System;
            using Synto.Templating;

            partial class Factory {}

            public class TestClass {
                [Template(typeof(Factory), Options = TemplateOption.Single)]
                void LocalFunction() {
                    Console.WriteLine("Hello world");
                }
            }
            """
        );
    }


    [Fact]
    public async Task ClassTemplate()
    {
        await VerifyTemplate(
            """
            using System;
            using Synto.Templating;

            partial class Factory {}

            [TemplateAttribute(typeof(Factory))]
            public class TestClass {
                void LocalFunction() {
                    Console.WriteLine("Hello world");
                }
            }
            """
        );
    }


    [Fact]
    public async Task InlineGenericValue()
    {
        await VerifyTemplate(
            """
            using System;
            using Synto.Templating;

            partial class Factory {}


            public class TestClass {
               [Template(typeof(Factory))]
                void LocalFunction<T>([Unquote]T value) {
                    Console.WriteLine($"Hello world {value}");
                }
            }
            """
        );
    }

    [Fact]
    public async Task SpliceGenericValue()
    {
        await VerifyTemplate(
            """
            using System;
            using Synto.Templating;

            partial class Factory {}


            public class TestClass {
               [Template(typeof(Factory))]
                void LocalFunction<T>([Splice]T value) {
                    Console.WriteLine($"Hello world {value}");
                }
            }
            """
        );
    }

    [Fact]
    public async Task InlineGenericType()
    {
        await VerifyTemplate(
            """
            using System;
            using Synto.Templating;
            using System.Collections.Generic;

            partial class Factory {}


            public class TestClass {
               [Template(typeof(Factory))]
                void LocalFunction<[Unquote]T>() {
                    List<T> list = new();
                }
            }
            """
        );
    }

    [Fact]
    public async Task SpliceGenericType()
    {
        await VerifyTemplate(
            """
            using System;
            using Synto.Templating;

            partial class Factory {}


            public class TestClass {
               [Template(typeof(Factory))]
                void LocalFunction<[Splice]T>(T value) {
                    Console.WriteLine($"Hello world {value}");
                }
            }
            """
        );
    }


    [Fact]
    public async Task WithSyntaxOfString()
    {
        await VerifyTemplate(
            """
            using System;
            using Synto.Templating;

            partial class Factory {}


            public class TestClass {
               [Template(typeof(Factory))]
                void LocalFunction(Syntax<string> value) {
                    Console.WriteLine($"Hello world" + value());
                }
            }
            """
        );
    }

    [Fact]
    public async Task ClassAsBareMultiMember()
    {
        // C1 coverage: [Template(Options = Bare)] on a class with MULTIPLE members exercises the
        // VisitTypeDeclaration else-arm (Bare set, Single not set) that emits a
        // SyntaxList<MemberDeclarationSyntax> — the branch whose uncovered status let a stray
        // Debugger.Launch() survive. The emitted factory must round-trip the member list.
        await VerifyTemplate(
            """
            using System;
            using Synto.Templating;

            partial class Factory {}

            [Template(typeof(Factory), Options = TemplateOption.Bare)]
            public class TestClass {
                void First() {
                    Console.WriteLine("first");
                }

                void Second() {
                    Console.WriteLine("second");
                }
            }
            """
        );
    }

    [Fact]
    public async Task QuoteValue_InValuePosition_EmitsSameLiteralAsUnquote()
    {
        // A [Quote] int value used purely in value position lifts via `value.ToSyntax()` exactly like an
        // [Unquote] int (the value-lift is shared through TryEmitValueLift). The only difference from [Unquote]
        // is in CONTROL position (a quoted value never drives unrolling); in value position the emitted literal
        // lift is identical (spec §3 value-position equivalence). The snapshot pins the `count.ToSyntax()` lift.
        await VerifyTemplate(
            """
            using System;
            using Synto.Templating;

            partial class Factory {}

            public class TestClass {
                [Template(typeof(Factory))]
                void LocalFunction([Quote] int count) {
                    Console.WriteLine($"Hello world {count}");
                }
            }
            """
        );
    }

    [Fact]
    public void QuoteParamTemplate_IsIncrementalOnUnrelatedEdit()
    {
        // Cacheability guard for the [Quote] parameter path: discovery (QuoteParameterFinder), the value-lift,
        // and the factory-parameter wiring all run INSIDE the ForAttributeWithMetadataName transform and capture
        // no Compilation/SemanticModel/SyntaxNode into pipeline state, so an unrelated edit must leave EVERY
        // tracked step Cached/Unchanged.
        const string source =
            """
            using System;
            using Synto.Templating;

            partial class Factory {}

            public class TestClass {
                [Template(typeof(Factory), Options = TemplateOption.Bare)]
                static void Loop([Quote] int count) {
                    int ret = 0;
                    for (int i = 0; i < count; i++) ret++;
                }
            }
            """;

        var compilation = CompilationWithSource(source);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { new TemplateFactorySourceGenerator().AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation);

        var modified = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("namespace Other { internal sealed class Unrelated { } }"));
        driver = driver.RunGenerators(modified);

        var result = driver.GetRunResult().Results.Single();

        CacheabilityAssert.AllStepsCachedOrUnchanged(
            result,
            new[] { TemplateTrackingNames.Transform, TemplateTrackingNames.Result });
    }

    // ----- diagnostics / error paths (T2) -----------------------------------------------------------
    // Each feeds deliberately-malformed Synto usage and asserts the diagnostic Id directly off the
    // driver's diagnostics collection, plus (for usage errors) a real, non-empty source span.

    [Fact]
    public void TargetNotPartialReportsSY1001()
    {
        var diagnostics = RunAndGetDiagnostics(
            """
            using System;
            using Synto.Templating;

            class Factory {}

            public class TestClass {
                public void TestMethod() {
                    [Template(typeof(Factory))]
                    void LocalFunction() {
                        Console.WriteLine("hi");
                    }
                }
            }
            """);

        var diag = Assert.Single(diagnostics, d => d.Id == "SY1001");
        AssertHasRealSpan(diag);
    }

    [Fact]
    public void TargetNotClassReportsSY1002()
    {
        var diagnostics = RunAndGetDiagnostics(
            """
            using System;
            using Synto.Templating;

            partial struct Factory {}

            public class TestClass {
                public void TestMethod() {
                    [Template(typeof(Factory))]
                    void LocalFunction() {
                        Console.WriteLine("hi");
                    }
                }
            }
            """);

        var diag = Assert.Single(diagnostics, d => d.Id == "SY1002");
        AssertHasRealSpan(diag);
    }

    [Fact]
    public void TargetAncestorNotPartialReportsSY1004()
    {
        // Outer is NOT partial but the nested Factory is; Factory must be accessible (public) so the
        // typeof binds — otherwise the attribute argument fails to resolve and a different path is taken.
        var diagnostics = RunAndGetDiagnostics(
            """
            using System;
            using Synto.Templating;

            class Outer {
                public partial class Factory {}
            }

            public class TestClass {
                public void TestMethod() {
                    [Template(typeof(Outer.Factory))]
                    void LocalFunction() {
                        Console.WriteLine("hi");
                    }
                }
            }
            """);

        var diag = Assert.Single(diagnostics, d => d.Id == "SY1004");
        AssertHasRealSpan(diag);
    }

    [Fact]
    public void EmptyBareBodyReportsSY1005()
    {
        var diagnostics = RunAndGetDiagnostics(
            """
            using System;
            using Synto.Templating;

            partial class Factory {}

            public class TestClass {
                public void TestMethod() {
                    [Template(typeof(Factory), Options = TemplateOption.Bare)]
                    void LocalFunction() {
                    }
                }
            }
            """);

        var diag = Assert.Single(diagnostics, d => d.Id == "SY1005");
        AssertHasRealSpan(diag);
    }

    [Fact]
    public void MultipleMembersAsSingleReportsSY1007()
    {
        // TemplateOption.Single (== Single|Bare) on a class with >1 member hits MultipleMembersNotAllowed.
        var diagnostics = RunAndGetDiagnostics(
            """
            using System;
            using Synto.Templating;

            partial class Factory {}

            [Template(typeof(Factory), Options = TemplateOption.Single)]
            public class TestClass {
                void First() {
                    Console.WriteLine("first");
                }

                void Second() {
                    Console.WriteLine("second");
                }
            }
            """);

        var diag = Assert.Single(diagnostics, d => d.Id == "SY1007");
        AssertHasRealSpan(diag);
    }

    [Fact]
    public void InternalErrorMapsToSY0000()
    {
        // Generation is wrapped in a try/catch that converts ANY escaping exception into the SY0000
        // internal-error diagnostic, so a generation bug surfaces as a build diagnostic instead of
        // crashing the consumer's compiler/IDE. The templating quoter is robust enough that no malformed
        // template *body* reliably throws (it handles every node kind), so this verifies the catch-path's
        // mapping directly: an exception becomes a located-nowhere SY0000 carrying the exception type and
        // message.
        var ex = new InvalidOperationException("boom");

        var diag = Diagnostics.InternalError(ex).ToDiagnostic();

        Assert.Equal("SY0000", diag.Id);
        Assert.Equal(Location.None, diag.Location); // internal errors are not tied to a source location
        Assert.Contains("boom", diag.GetMessage(), StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), diag.GetMessage(), StringComparison.Ordinal);
    }

    // ----- incremental caching (T1) ----------------------------------------------------------------

    [Fact]
    public void GeneratorIsIncrementalOnUnrelatedEdit()
    {
        // Cacheability guard: the pipeline carries only equatable value types (TemplateGenerationResult /
        // DiagnosticInfo / EquatableArray) and captures no Compilation/SemanticModel/SyntaxNode, so an edit
        // in an unrelated tree must leave every tracked step Cached/Unchanged (not re-running the generator
        // on every keystroke).
        const string source =
            """
            using System;
            using Synto.Templating;

            partial class Factory {}

            public class TestClass {
                public void TestMethod() {
                    [Template(typeof(Factory), Options = TemplateOption.Single)]
                    void LocalFunction() {
                        Console.WriteLine("Hello world");
                    }
                }
            }
            """;

        var compilation = CompilationWithSource(source);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { new TemplateFactorySourceGenerator().AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation);

        // An unrelated edit in a separate tree: the [Template] tree is byte-identical, so its pipeline
        // results must come from cache.
        var modified = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("namespace Other { internal sealed class Unrelated { } }"));
        driver = driver.RunGenerators(modified);

        var result = driver.GetRunResult().Results.Single();

        CacheabilityAssert.AllStepsCachedOrUnchanged(
            result,
            new[] { TemplateTrackingNames.Transform, TemplateTrackingNames.Result });
    }

    [Fact]
    public void StagedTemplate_IsIncrementalOnUnrelatedEdit()
    {
        // The SAME cacheability guard as GeneratorIsIncrementalOnUnrelatedEdit, but for a LIVE-STAGED template
        // (plan Task 10 / spec §8). All staging — live-root discovery, the binding-time classifier, region
        // unrolling, and the Member builder rewrite — runs INSIDE the ForAttributeWithMetadataName transform and
        // captures no Compilation/SemanticModel/SyntaxNode into pipeline state, so an unrelated edit must leave
        // every tracked step Cached/Unchanged.
        const string source =
            """
            using System.Collections.Generic;
            using Synto.Templating;
            using static Synto.Templating.Template;

            partial class Factory {}

            public readonly record struct Col(int Ordinal, string Name);

            public class TestClass {
                [Template(typeof(Factory))]
                object GetMember<[Splice] TRow>(TRow row, int i) {
                    var columns = Parameter<IReadOnlyList<Col>>();
                    foreach (var c in columns)            // live foreach -> unrolls in the factory
                        if (i == c.Ordinal)               // c.Ordinal -> int literal
                            return Member<object>(row, c.Name); // Member builder -> row.<Name>
                    throw new System.IndexOutOfRangeException();
                }
            }
            """;

        var compilation = CompilationWithSource(source);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { new TemplateFactorySourceGenerator().AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation);

        var modified = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("namespace Other { internal sealed class Unrelated { } }"));
        driver = driver.RunGenerators(modified);

        var result = driver.GetRunResult().Results.Single();

        CacheabilityAssert.AllStepsCachedOrUnchanged(
            result,
            new[] { TemplateTrackingNames.Transform, TemplateTrackingNames.Result });
    }

    private static void AssertHasRealSpan(Diagnostic diag)
    {
        // The location is carried cacheably as a serializable LocationInfo and reconstructed at emit time;
        // assert the squiggle still points at a real, non-empty source span (not Location.None).
        Assert.NotEqual(Location.None, diag.Location);
        Assert.False(diag.Location.SourceSpan.IsEmpty);
    }

    private ImmutableArray<Diagnostic> RunAndGetDiagnostics(string source)
    {
        var compilation = CompilationWithSource(source);
        var result = _driver.RunGenerators(compilation);
        return result.GetRunResult().Diagnostics;
    }
}

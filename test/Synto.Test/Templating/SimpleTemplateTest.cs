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
                    [Template<Factory>(Options = TemplateOption.Single)]
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
                    [Template<Factory>(Options = TemplateOption.Bare)]
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
                    [Template<Factory>(Options = TemplateOption.None)]
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
                    [Template<Factory>(Options = TemplateOption.Single)]
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
                    [Template<Factory>(Options = TemplateOption.Bare)]
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
                    [Template<Factory>(Options = TemplateOption.None)]
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
                    [Template<Factory>(Options = TemplateOption.Single)]
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
                [Template<Factory>(Options = TemplateOption.Single)]
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

            [Template<Factory>]
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
               [Template<Factory>]
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
               [Template<Factory>]
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
               [Template<Factory>]
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
               [Template<Factory>]
                void LocalFunction<[Splice]T>(T value) {
                    Console.WriteLine($"Hello world {value}");
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

            [Template<Factory>(Options = TemplateOption.Bare)]
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
                [Template<Factory>]
                void LocalFunction([Quote] int count) {
                    Console.WriteLine($"Hello world {count}");
                }
            }
            """
        );
    }

    [Fact]
    public void StagedScalarMemberAccess_FoldsToLiteral()
    {
        // Capability 2 (count-fold): a scalar member-access on a staged-root parameter whose RESULT type is a
        // built-in literal type (`xs.Count` -> int) folds to a factory-time literal lift `(xs.Count).ToSyntax()`
        // — NO separate Parameter<int>() is needed, and the bare receiver reference (`xs`) must NOT be separately
        // depth-zero lifted (which would emit a broken `xs.ToSyntax()` on the non-literal list type).
        const string source =
            """
            using System.Collections.Generic;
            using Synto.Templating;
            using static Synto.Templating.Template;

            partial class Factory {}

            public class TestClass {
                [Template<Factory>]
                int N() {
                    var xs = Parameter<IReadOnlyList<int>>();
                    return xs.Count;
                }
            }
            """;

        var compilation = CompilationWithSource(source);
        Assert.Empty(compilation.GetDiagnostics(Xunit.TestContext.Current.CancellationToken).Where(diag => diag.Severity == DiagnosticSeverity.Error));

        var result = _driver.RunGenerators(compilation, Xunit.TestContext.Current.CancellationToken);
        var runResult = result.GetRunResult();
        Assert.Empty(runResult.Diagnostics);

        var factory = runResult.Results
            .SelectMany(r => r.GeneratedSources)
            .Single(s => s.HintName == "Factory.N.g.cs")
            .SourceText.ToString();

        // The whole member-access is lifted via (xs.Count).ToSyntax() — a factory-time literal lift, NOT a literal
        // baked at generator-author time.
        Assert.Contains("(xs.Count).ToSyntax()", factory, StringComparison.Ordinal);

        // The bare receiver reference was claimed by the fold: it is NOT separately lifted (no `xs.ToSyntax()` on
        // the non-literal list type, which would otherwise throw at the author's runtime).
        Assert.DoesNotContain("xs.ToSyntax()", factory, StringComparison.Ordinal);

        // No scalar Parameter<int>()-sourced parameter: the single factory parameter is the staged list `xs`
        // (whose type happens to contain `int`), so the signature carries exactly one parameter (no comma).
        var signature = factory.Split('\n').Single(line => line.Contains(" N(", StringComparison.Ordinal));
        Assert.Contains("xs", signature, StringComparison.Ordinal);
        Assert.DoesNotContain(",", signature, StringComparison.Ordinal);
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
                [Template<Factory>(Options = TemplateOption.Bare)]
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

        driver = driver.RunGenerators(compilation, Xunit.TestContext.Current.CancellationToken);

        var modified = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("namespace Other { internal sealed class Unrelated { } }", cancellationToken: Xunit.TestContext.Current.CancellationToken));
        driver = driver.RunGenerators(modified, Xunit.TestContext.Current.CancellationToken);

        var result = driver.GetRunResult().Results.Single();

        CacheabilityAssert.AllStepsCachedOrUnchanged(
            result,
            new[] { TemplateTrackingNames.GenericTransform, TemplateTrackingNames.GenericResult });
    }

    [Fact]
    public void QuoteCallTemplate_IsIncrementalOnUnrelatedEdit()
    {
        // Cacheability guard for the inline Quote(value) path: QuoteCallFinder discovery, the classifier shield,
        // and the value-lift all run INSIDE the ForAttributeWithMetadataName transform and capture no
        // Compilation/SemanticModel/SyntaxNode into pipeline state, so an unrelated edit must leave EVERY tracked
        // step Cached/Unchanged.
        const string source =
            """
            using System;
            using Synto.Templating;
            using static Synto.Templating.Template;

            partial class Factory {}

            public class TestClass {
                [Template<Factory>(Options = TemplateOption.Bare)]
                static void Loop([Unquote] int count) {
                    int ret = 0;
                    var bound = Unquote(count);
                    for (int i = 0; i < Quote(bound); i++)
                        ret++;
                }
            }
            """;

        var compilation = CompilationWithSource(source);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { new TemplateFactorySourceGenerator().AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation, Xunit.TestContext.Current.CancellationToken);

        var modified = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("namespace Other { internal sealed class Unrelated { } }", cancellationToken: Xunit.TestContext.Current.CancellationToken));
        driver = driver.RunGenerators(modified, Xunit.TestContext.Current.CancellationToken);

        var result = driver.GetRunResult().Results.Single();

        CacheabilityAssert.AllStepsCachedOrUnchanged(
            result,
            new[] { TemplateTrackingNames.GenericTransform, TemplateTrackingNames.GenericResult });
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
                    [Template<Factory>]
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
                    [Template<Factory>]
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
                    [Template<Outer.Factory>]
                    void LocalFunction() {
                        Console.WriteLine("hi");
                    }
                }
            }
            """);

        var diag = Assert.Single(diagnostics, d => d.Id == "SY1004");
        AssertHasRealSpan(diag);
    }

    // NOTE: #15 open-generic-target coverage (GenericTarget_RendersGenericPartial_NoArityZeroTwin) was dropped in
    // Phase 1. It relied on [Template(typeof(Factory<>))] to name an OPEN generic target; the ctor form is now gone
    // and the generic form cannot express an unbound generic (CS7003: unbound generic name is illegal as a type
    // argument). The target is therefore inexpressible through the new surface, so the path is unreachable.

    [Fact]
    public void TargetAncestorUnsupportedKindReportsSY1032()
    {
        // #15: a [Template] target nested in a partial STRUCT can't be honored by the ancestry rebuild
        // (WithAncestryFrom hard-casts each ancestor to ClassDeclarationSyntax). The validator must reject it
        // up-front with a located SY1032 rather than let the cast throw an opaque SY0000.
        var diagnostics = RunAndGetDiagnostics(
            """
            using System;
            using Synto.Templating;

            partial struct Outer {
                public partial class Factory {}
            }

            public class TestClass {
                public void TestMethod() {
                    [Template<Outer.Factory>]
                    void LocalFunction() {
                        Console.WriteLine("hi");
                    }
                }
            }
            """);

        var diag = Assert.Single(diagnostics, d => d.Id == "SY1032");
        AssertHasRealSpan(diag);
        Assert.DoesNotContain(diagnostics, d => d.Id == "SY0000");
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
                    [Template<Factory>(Options = TemplateOption.Bare)]
                    void LocalFunction() {
                    }
                }
            }
            """);

        var diag = Assert.Single(diagnostics, d => d.Id == "SY1005");
        AssertHasRealSpan(diag);
    }

    [Fact]
    public void BodylessBareTemplateReportsSY1005()
    {
        // A Bare template on a bodyless method (partial-definition/extern — no block body, no expression
        // body) previously hit neither quoting branch: Expression and Error both stayed null, TryQuote
        // returned false with error == null, and the caller reported nothing — silent no-output. The
        // tightened contract (TryQuote false => error != null) surfaces the both-null fall-through as the
        // SY1005 "Source can not be empty when Bare is specified" family diagnostic, exactly one, located.
        var diagnostics = RunAndGetDiagnostics(
            """
            using System;
            using Synto.Templating;

            partial class Factory {}

            public partial class TestClass {
                [Template<Factory>(Options = TemplateOption.Bare)]
                partial void BodylessMethod();
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

            [Template<Factory>(Options = TemplateOption.Single)]
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
        Assert.Contains("boom", diag.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), diag.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
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
                    [Template<Factory>(Options = TemplateOption.Single)]
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

        driver = driver.RunGenerators(compilation, Xunit.TestContext.Current.CancellationToken);

        // An unrelated edit in a separate tree: the [Template] tree is byte-identical, so its pipeline
        // results must come from cache.
        var modified = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("namespace Other { internal sealed class Unrelated { } }", cancellationToken: Xunit.TestContext.Current.CancellationToken));
        driver = driver.RunGenerators(modified, Xunit.TestContext.Current.CancellationToken);

        var result = driver.GetRunResult().Results.Single();

        CacheabilityAssert.AllStepsCachedOrUnchanged(
            result,
            new[] { TemplateTrackingNames.GenericTransform, TemplateTrackingNames.GenericResult });
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
                [Template<Factory>]
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

        driver = driver.RunGenerators(compilation, Xunit.TestContext.Current.CancellationToken);

        var modified = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("namespace Other { internal sealed class Unrelated { } }", cancellationToken: Xunit.TestContext.Current.CancellationToken));
        driver = driver.RunGenerators(modified, Xunit.TestContext.Current.CancellationToken);

        var result = driver.GetRunResult().Results.Single();

        CacheabilityAssert.AllStepsCachedOrUnchanged(
            result,
            new[] { TemplateTrackingNames.GenericTransform, TemplateTrackingNames.GenericResult });
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

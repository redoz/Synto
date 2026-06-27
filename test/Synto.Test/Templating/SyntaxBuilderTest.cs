using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Synto.Test.Templating;

/// <summary>
/// Exercises the generic syntax-builder mechanism (plan Task 3): the built-in <c>Member</c>/<c>TypeOf</c>
/// facades (hand-authored on <c>Template</c>, recognized by binding) and user <c>[SyntaxBuilder]</c> methods
/// (facade synthesized, recognized structurally). A recognized facade call is rewritten to a fully-qualified
/// static invocation of the paired builder over processed arguments: a <c>[Quoted]</c> parameter receives the
/// quote of the call argument, an unmarked parameter the live value verbatim.
/// </summary>
public class SyntaxBuilderTest
{
    private static readonly MetadataReference CorlibReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
    private static readonly MetadataReference NetStandardReference = MetadataReference.CreateFromFile(Assembly.Load("netstandard, Version=2.0.0.0").Location);
    private static readonly MetadataReference SystemRuntimeReference = MetadataReference.CreateFromFile(Assembly.Load("System.Runtime, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location);
    private static readonly MetadataReference RoslynReference = MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.SyntaxNode).Assembly.Location);
    private static readonly MetadataReference RoslynCSharpReference = MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.CSharp.SyntaxKind).Assembly.Location);

    private static MetadataReference[] References =>
    [
        CorlibReference,
        NetStandardReference,
        SystemRuntimeReference,
        MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
        RoslynReference,
        RoslynCSharpReference,
        MetadataReference.CreateFromFile(typeof(System.Collections.Immutable.ImmutableArray).Assembly.Location),
        MetadataReference.CreateFromFile(Assembly.Load("System.Linq, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
        MetadataReference.CreateFromFile(Assembly.Load("System.Collections, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
        MetadataReference.CreateFromFile(Assembly.Load("System.Runtime.Extensions, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
        MetadataReference.CreateFromFile(SyntoCoreAssembly.Location),
    ];

    private static Compilation CompilationWithSource(string source) =>
        CSharpCompilation.Create("Test",
            [CSharpSyntaxTree.ParseText(source)],
            references: References,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private readonly GeneratorDriver _driver = CSharpGeneratorDriver.Create(new TemplateFactorySourceGenerator());

    /// <summary>Snapshot the factory for a template whose facade calls bind pre-generation (built-ins).</summary>
    private async Task VerifyTemplate(string source)
    {
        var compilation = CompilationWithSource(source);
        Assert.Empty(compilation.GetDiagnostics().Where(diag => diag.Severity == DiagnosticSeverity.Error));
        var result = _driver.RunGenerators(compilation);
        var ret = await Verify(result).UseDirectory("snapshots");
        Assert.NotEmpty(ret.Files);
    }

    /// <summary>
    /// Snapshot the factory for a template that calls a SYNTHESIZED facade (which does not bind pre-generation,
    /// so we do not assert pre-generation cleanliness), then compile the post-generation output (with the
    /// facade-synthesis generator) and assert it is error-free — the synthesized facade makes the carrier call
    /// resolve and the generated factory call the builder.
    /// </summary>
    private async Task VerifyBuilderTemplate(string source)
    {
        var compilation = CompilationWithSource(source);

        // Snapshot the TemplateFactorySourceGenerator output only (stable, builder call replaced).
        var result = _driver.RunGenerators(compilation);
        var ret = await Verify(result).UseDirectory("snapshots");
        Assert.NotEmpty(ret.Files);

        // Post-generation compile with the synthesis generator present, so the synthesized facade resolves.
        var fullDriver = CSharpGeneratorDriver.Create(
            new SyntaxBuilderFacadeGenerator(),
            new TemplateFactorySourceGenerator());
        fullDriver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var genDiagnostics, TestContext.Current.CancellationToken);

        Assert.Empty(genDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var errors = output.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(errors.Count == 0,
            "Post-generation compilation reported errors: " + string.Join("; ", errors.Select(d => d.Id + " " + d.GetMessage())));
    }

    private ImmutableArray<Diagnostic> RunAndGetDiagnostics(string source)
    {
        var compilation = CompilationWithSource(source);
        var result = _driver.RunGenerators(compilation);
        return result.GetRunResult().Diagnostics;
    }

    private static void AssertHasRealSpan(Diagnostic diag)
    {
        Assert.NotEqual(Location.None, diag.Location);
        Assert.False(diag.Location.SourceSpan.IsEmpty);
    }

    // Built-in identifier builder with a CONSTANT name -> member access in output.
    [Fact]
    public async Task Member_WithConstantName_EmitsMemberAccess()
    {
        await VerifyTemplate(
            """
            using Synto.Templating;
            using static Synto.Templating.Template;
            partial class Factory {}
            public class TestClass {
                [Template(typeof(Factory))]
                void Build<[Inline(AsSyntax = true)] T>(T instance) {
                    var x = Member<object>(instance, "Name");   // -> instance.Name in OUTPUT (identifier, not "Name")
                    System.Console.WriteLine(x);
                }
            }
            """);
    }

    // User-authored builder is discovered, a facade is synthesized, and the factory calls the builder.
    [Fact]
    public async Task UserSyntaxBuilder_IsDiscoveredAndInvoked()
    {
        await VerifyBuilderTemplate(
            """
            using Synto.Templating;
            using Microsoft.CodeAnalysis.CSharp.Syntax;
            using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
            partial class Factory {}

            // builder (factory-time); Synto synthesizes the inert facade `T Cast<T>(object x)`.
            static partial class MyBuilders {
                [SyntaxBuilder]
                public static ExpressionSyntax Cast([Quoted(AsTypeArg = true), ReturnType] TypeSyntax t, [Quoted] ExpressionSyntax x) => CastExpression(t, x);
            }

            public class TestClass {
                [Template(typeof(Factory))]
                void Build([Inline(AsSyntax = true)] object x) {
                    System.Console.WriteLine(MyBuilders.Cast<int>(x));
                }
            }
            """);
    }

    // META: an UNMARKED ExpressionSyntax builder parameter receives a LIVE ExpressionSyntax passed through
    // UNQUOTED (the case the parameter TYPE cannot disambiguate; Locked Names §6).
    [Fact]
    public async Task UnmarkedExpressionSyntaxParam_PassesLiveSyntaxThroughUnquoted()
    {
        await VerifyBuilderTemplate(
            """
            using Synto.Templating;
            using Microsoft.CodeAnalysis.CSharp.Syntax;
            using static Synto.Templating.Template;
            partial class Factory {}

            static partial class MetaBuilders {
                // `node` UNMARKED => the live ExpressionSyntax argument is passed VERBATIM, not re-quoted.
                [SyntaxBuilder]
                public static ExpressionSyntax Splice(ExpressionSyntax node) => node;
            }

            public class TestClass {
                [Template(typeof(Factory))]
                void Build() {
                    var node = Parameter<ExpressionSyntax>("node");
                    System.Console.WriteLine(MetaBuilders.Splice(node));
                }
            }
            """);
    }

    // Diagnostic: a [SyntaxBuilder] whose annotations cannot synthesize a valid facade.
    [Fact]
    public void InvalidBuilderAnnotation_ReportsSY1015()
    {
        var diagnostics = RunAndGetDiagnostics(
            """
            using Synto.Templating;
            using Microsoft.CodeAnalysis.CSharp.Syntax;
            partial class Factory {}

            static partial class Bad {
                // [ReturnType] without [Quoted(AsTypeArg = true)] -> facade-synthesis error.
                [SyntaxBuilder]
                public static ExpressionSyntax Foo([ReturnType] ExpressionSyntax x) => x;
            }

            public class TestClass {
                [Template(typeof(Factory))]
                void Build([Inline(AsSyntax = true)] object x) => System.Console.WriteLine(Bad.Foo(x));
            }
            """);
        var diag = Assert.Single(diagnostics, d => d.Id == "SY1015");
        AssertHasRealSpan(diag);
    }

    // Diagnostic: a facade call that cannot satisfy its builder parameter binding (missing required type arg).
    [Fact]
    public void BuilderArgBindingMismatch_ReportsSY1016()
    {
        var diagnostics = RunAndGetDiagnostics(
            """
            using Synto.Templating;
            using Microsoft.CodeAnalysis.CSharp.Syntax;
            partial class Factory {}

            static partial class Builders {
                [SyntaxBuilder]
                public static ExpressionSyntax Cast([Quoted(AsTypeArg = true), ReturnType] TypeSyntax t, [Quoted] ExpressionSyntax x) => x;
            }

            public class TestClass {
                [Template(typeof(Factory))]
                void Build([Inline(AsSyntax = true)] object x) => System.Console.WriteLine(Builders.Cast(x)); // no <int> type arg
            }
            """);
        var diag = Assert.Single(diagnostics, d => d.Id == "SY1016");
        AssertHasRealSpan(diag);
    }

    // Diagnostic: a [SyntaxBuilder] whose return type is neither ExpressionSyntax nor TypeSyntax.
    [Fact]
    public void BuilderBadReturnShape_ReportsSY1017()
    {
        var diagnostics = RunAndGetDiagnostics(
            """
            using Synto.Templating;
            using Microsoft.CodeAnalysis.CSharp.Syntax;
            partial class Factory {}

            static partial class Bad {
                // returns int -> not a supported builder return shape.
                [SyntaxBuilder]
                public static int Nope([Quoted] ExpressionSyntax x) => 0;
            }

            public class TestClass {
                [Template(typeof(Factory))]
                void Build([Inline(AsSyntax = true)] object x) => System.Console.WriteLine(Bad.Nope(x));
            }
            """);
        var diag = Assert.Single(diagnostics, d => d.Id == "SY1017");
        AssertHasRealSpan(diag);
    }

    // Diagnostic: two [SyntaxBuilder] methods synthesize a colliding facade (same simple name).
    [Fact]
    public void AmbiguousBuilder_ReportsSY1018()
    {
        var diagnostics = RunAndGetDiagnostics(
            """
            using Synto.Templating;
            using Microsoft.CodeAnalysis.CSharp.Syntax;
            partial class Factory {}

            static partial class Builders {
                // two builders with the SAME facade name `Wrap` -> ambiguous once a call matches.
                [SyntaxBuilder]
                public static ExpressionSyntax Wrap([Quoted] ExpressionSyntax x) => x;
                [SyntaxBuilder]
                public static ExpressionSyntax Wrap([Quoted] ExpressionSyntax x, [Quoted] ExpressionSyntax y) => x;
            }

            public class TestClass {
                [Template(typeof(Factory))]
                void Build([Inline(AsSyntax = true)] object x) => System.Console.WriteLine(Builders.Wrap(x));
            }
            """);
        var diag = Assert.Single(diagnostics, d => d.Id == "SY1018");
        AssertHasRealSpan(diag);
    }

    // Zero-collision: the built-in Member builder is injected internal and called fully-qualified, so the
    // generated output compiles alongside the PUBLIC Synto.Core in the default scope with no CS0121 ambiguity
    // and no Synto.Core runtime dependency (the injected SyntoBuilders is the consumer's own internal copy).
    [Fact]
    public void BuiltInBuilderOutput_CompilesAlongsidePublicSyntoCore()
    {
        const string source =
            """
            using Synto.Templating;
            using static Synto.Templating.Template;
            partial class Factory {}
            public class TestClass {
                [Template(typeof(Factory))]
                void Build([Inline(AsSyntax = true)] object instance) {
                    var x = Member<object>(instance, "Name");
                    System.Console.WriteLine(x);
                }
            }
            """;

        var compilation = CompilationWithSource(source);

        // Run BOTH generators so the injected internal SyntoBuilders (and markers) are present.
        var driver = CSharpGeneratorDriver.Create(
            new SurfaceInjectionGenerator(),
            new TemplateFactorySourceGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var genDiagnostics, TestContext.Current.CancellationToken);

        Assert.Empty(genDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var errors = output.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.DoesNotContain(errors, d => d.Id == "CS0121");
        Assert.True(errors.Count == 0,
            "Post-generation compilation reported errors: " + string.Join("; ", errors.Select(d => d.Id + " " + d.GetMessage())));
    }
}

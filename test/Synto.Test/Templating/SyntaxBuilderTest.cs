using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Synto.Generators;
using Synto.Generators.Templating;
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
                void Build<[Splice] T>(T instance) {
                    var x = Member<object>(instance, "Name");   // -> instance.Name in OUTPUT (identifier, not "Name")
                    System.Console.WriteLine(x);
                }
            }
            """);
    }

    // Template.Splice(node) body form: a pre-built ExpressionSyntax is spliced VERBATIM mid-expression at the
    // call position (the inline counterpart to a [Splice] parameter), so the output is `Console.WriteLine(node)`.
    [Fact]
    public async Task TemplateSplice_SplicesNodeVerbatim()
    {
        await VerifyTemplate(
            """
            using Synto.Templating;
            using Microsoft.CodeAnalysis.CSharp.Syntax;
            using static Synto.Templating.Template;
            partial class Factory {}
            public class TestClass {
                [Template(typeof(Factory))]
                void Build([Splice] ExpressionSyntax node) {
                    System.Console.WriteLine(Splice(node));   // -> Console.WriteLine(node) in OUTPUT
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
                public static ExpressionSyntax Cast([TypeArg, ReturnType] TypeSyntax t, [Quoted] ExpressionSyntax x) => CastExpression(t, x);
            }

            public class TestClass {
                [Template(typeof(Factory))]
                void Build([Splice] object x) {
                    System.Console.WriteLine(MyBuilders.Cast<int>(x));
                }
            }
            """);
    }

    // [TypeArg] surfaces a facade generic type parameter; Name decouples the facade type-param name from the
    // builder parameter name. Proven by post-generation compile: the synthesized `TResult Cast<TResult>(object x)`
    // resolves the consumer's `Cast<int>(x)`. Compile-only (no snapshot).
    [Fact]
    public void TypeArg_WithName_SynthesizesGenericFacadeThatCompiles()
    {
        const string source =
            """
            using Synto.Templating;
            using Microsoft.CodeAnalysis.CSharp.Syntax;
            using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
            partial class Factory {}

            static partial class MyBuilders {
                [SyntaxBuilder]
                public static ExpressionSyntax Cast([TypeArg(Name = "TResult"), ReturnType] TypeSyntax targetType, [Quoted] ExpressionSyntax x)
                    => CastExpression(targetType, x);
            }

            public class TestClass {
                [Template(typeof(Factory))]
                void Build([Splice] object x) {
                    System.Console.WriteLine(MyBuilders.Cast<int>(x));
                }
            }
            """;

        var compilation = CompilationWithSource(source);

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

    // [TypeArg(Constraint = ...)] emits a `where T : Constraint` clause on the facade, so a consumer type
    // argument that violates it is a compile error (CS0311). This proves the constraint is emitted, not ignored.
    [Fact]
    public void TypeArg_Constraint_RejectsViolatingTypeArgument()
    {
        const string source =
            """
            using Synto.Templating;
            using Microsoft.CodeAnalysis.CSharp.Syntax;
            using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
            partial class Factory {}

            static partial class MyBuilders {
                [SyntaxBuilder]
                public static ExpressionSyntax Cast([TypeArg(Name = "T", Constraint = typeof(System.IComparable)), ReturnType] TypeSyntax targetType, [Quoted] ExpressionSyntax x)
                    => CastExpression(targetType, x);
            }

            public class TestClass {
                [Template(typeof(Factory))]
                void Build([Splice] object x) {
                    System.Console.WriteLine(MyBuilders.Cast<object>(x)); // object does NOT satisfy `where T : IComparable`
                }
            }
            """;

        var compilation = CompilationWithSource(source);

        var fullDriver = CSharpGeneratorDriver.Create(
            new SyntaxBuilderFacadeGenerator(),
            new TemplateFactorySourceGenerator());
        fullDriver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var genDiagnostics, TestContext.Current.CancellationToken);

        Assert.Empty(genDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var errors = output.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.Contains(errors, d => d.Id == "CS0311");
    }

    // A consumer type argument that SATISFIES the [TypeArg] constraint still compiles cleanly.
    [Fact]
    public void TypeArg_Constraint_AcceptsSatisfyingTypeArgument()
    {
        const string source =
            """
            using Synto.Templating;
            using Microsoft.CodeAnalysis.CSharp.Syntax;
            using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
            partial class Factory {}

            static partial class MyBuilders {
                [SyntaxBuilder]
                public static ExpressionSyntax Cast([TypeArg(Name = "T", Constraint = typeof(System.IComparable)), ReturnType] TypeSyntax targetType, [Quoted] ExpressionSyntax x)
                    => CastExpression(targetType, x);
            }

            public class TestClass {
                [Template(typeof(Factory))]
                void Build([Splice] object x) {
                    System.Console.WriteLine(MyBuilders.Cast<string>(x)); // string satisfies `where T : IComparable`
                }
            }
            """;

        var compilation = CompilationWithSource(source);

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

    // META: an UNMARKED ExpressionSyntax builder parameter receives a LIVE ExpressionSyntax passed through
    // UNQUOTED (the case the parameter TYPE cannot disambiguate; Locked Names §6).
    [Fact]
    public async Task UnmarkedExpressionSyntaxParam_PassesStagedSyntaxThroughUnquoted()
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

    // BUG #1 (Scope-C Stage 2): a Template.Splice(node) call INSIDE a [Quoted] builder argument must INJECT
    // `node` (the quote of `Splice(node)` is the live node), yielding `Wrap(node)`. Today the plan-time island
    // sub-quoter is built before splice-call replacements are registered, so it quotes the `Splice(...)`
    // invocation raw. Re-entry over the full plan fixes this.
    [Fact]
    public async Task SpliceInsideQuotedBuilderArg_InjectsSplicedNode()
    {
        await VerifyBuilderTemplate(
            """
            using Synto.Templating;
            using Microsoft.CodeAnalysis.CSharp.Syntax;
            using static Synto.Templating.Template;
            partial class Factory {}

            static partial class MyBuilders {
                [SyntaxBuilder]
                public static ExpressionSyntax Wrap([Quoted] ExpressionSyntax x) => x;
            }

            public class TestClass {
                [Template(typeof(Factory))]
                void Build([Splice] ExpressionSyntax node) {
                    System.Console.WriteLine(MyBuilders.Wrap(Splice(node)));   // -> Wrap(node) in OUTPUT
                }
            }
            """);
    }

    // BUG #2 (Scope-C Stage 2): a nested builder call `A(B(x))` must EXPAND the inner builder — the inner
    // `[Quoted]` island is itself a builder call and should become a real builder invocation, yielding
    // `Wrap(Wrap(x))`. Today the plan-time island sub-quoter cannot see the sibling builder replacements
    // (added during/after its construction), so it quotes the inner call raw. Re-entry fixes this.
    [Fact]
    public async Task NestedBuilderCalls_ExpandInnerBuilder()
    {
        await VerifyBuilderTemplate(
            """
            using Synto.Templating;
            using Microsoft.CodeAnalysis.CSharp.Syntax;
            using static Synto.Templating.Template;
            partial class Factory {}

            static partial class MyBuilders {
                [SyntaxBuilder]
                public static ExpressionSyntax Wrap([Quoted] ExpressionSyntax x) => x;
            }

            public class TestClass {
                [Template(typeof(Factory))]
                void Build([Splice] ExpressionSyntax x) {
                    System.Console.WriteLine(MyBuilders.Wrap(MyBuilders.Wrap(x)));   // -> Wrap(Wrap(x)) in OUTPUT
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
                // [ReturnType] without [TypeArg] -> facade-synthesis error.
                [SyntaxBuilder]
                public static ExpressionSyntax Foo([ReturnType] ExpressionSyntax x) => x;
            }

            public class TestClass {
                [Template(typeof(Factory))]
                void Build([Splice] object x) => System.Console.WriteLine(Bad.Foo(x));
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
                public static ExpressionSyntax Cast([TypeArg, ReturnType] TypeSyntax t, [Quoted] ExpressionSyntax x) => x;
            }

            public class TestClass {
                [Template(typeof(Factory))]
                void Build([Splice] object x) => System.Console.WriteLine(Builders.Cast(x)); // no <int> type arg
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
                void Build([Splice] object x) => System.Console.WriteLine(Bad.Nope(x));
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
                void Build([Splice] object x) => System.Console.WriteLine(Builders.Wrap(x));
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
                void Build([Splice] object instance) {
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

    // #1: a call whose simple name COLLIDES with a registered builder but which BINDS to a real, unrelated
    // user method must NOT be hijacked as a builder facade — it is quoted verbatim. Today the finder matches by
    // simple name alone and mis-binds `settings.Wrap()` (0 args) to the 1-param builder, emitting SY1016.
    [Fact]
    public void BoundUserMethodCollidingWithBuilderName_NotHijacked_NoDiagnostics()
    {
        var diagnostics = RunAndGetDiagnostics(
            """
            using Synto.Templating;
            using Microsoft.CodeAnalysis.CSharp.Syntax;
            partial class Factory {}

            static partial class Builders {
                [SyntaxBuilder]
                public static ExpressionSyntax Wrap([Quoted] ExpressionSyntax x) => x;
            }

            public class Settings {
                public int Wrap() => 0; // real method, same simple name as the builder facade
            }

            public class TestClass {
                [Template(typeof(Factory))]
                void Build() {
                    var settings = new Settings();
                    System.Console.WriteLine(settings.Wrap()); // binds to Settings.Wrap -> quote verbatim
                }
            }
            """);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    // #7: named arguments on a built-in facade call (Template.Member) are reported SY1029, not silently
    // mispaired. (v1 policy: diagnose, do not reorder.)
    [Fact]
    public void NamedArgumentsOnBuiltInFacade_ReportsSY1029()
    {
        var diagnostics = RunAndGetDiagnostics(
            """
            using Synto.Templating;
            using static Synto.Templating.Template;
            partial class Factory {}
            public class TestClass {
                [Template(typeof(Factory))]
                void Build([Splice] object instance) {
                    var x = Member<object>(instance: instance, name: "Foo");
                }
            }
            """);
        var diag = Assert.Single(diagnostics, d => d.Id == "SY1029");
        AssertHasRealSpan(diag);
    }

    // Runs ONLY the facade-synthesis generator and returns its diagnostics — declaration-site misuse (SY1031)
    // is reported by that generator's own pipeline, independent of any template call.
    private static ImmutableArray<Diagnostic> RunFacadeGeneratorDiagnostics(string source)
    {
        var compilation = CSharpCompilation.Create("Test",
            [CSharpSyntaxTree.ParseText(source)],
            references: References,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var driver = CSharpGeneratorDriver.Create(new SyntaxBuilderFacadeGenerator());
        return driver.RunGenerators(compilation).GetRunResult().Diagnostics;
    }

    // #10 (a): [TypeArg] and [Quoted] on one parameter is documented-forbidden -> SY1015 (annotation error).
    [Fact]
    public void CombinedTypeArgAndQuotedMarkers_ReportsSY1015()
    {
        var diagnostics = RunAndGetDiagnostics(
            """
            using Synto.Templating;
            using Microsoft.CodeAnalysis.CSharp.Syntax;
            partial class Factory {}

            static partial class Bad {
                [SyntaxBuilder]
                public static ExpressionSyntax Combo([TypeArg, Quoted] ExpressionSyntax x) => x;
            }

            public class TestClass {
                [Template(typeof(Factory))]
                void Build([Splice] object x) => System.Console.WriteLine(Bad.Combo(x));
            }
            """);
        var diag = Assert.Single(diagnostics, d => d.Id == "SY1015");
        AssertHasRealSpan(diag);
    }

    // #10 (b): two facade type-parameters resolve to the same name -> SY1015 (annotation error), no CS0692.
    [Fact]
    public void DuplicateFacadeTypeParameterName_ReportsSY1015()
    {
        var diagnostics = RunAndGetDiagnostics(
            """
            using Synto.Templating;
            using Microsoft.CodeAnalysis.CSharp.Syntax;
            partial class Factory {}

            static partial class Bad {
                [SyntaxBuilder]
                public static ExpressionSyntax Two([TypeArg(Name = "T")] TypeSyntax a, [TypeArg(Name = "T")] TypeSyntax b) => a;
            }

            public class TestClass {
                [Template(typeof(Factory))]
                void Build([Splice] object x) => System.Console.WriteLine(Bad.Two(x, x));
            }
            """);
        var diag = Assert.Single(diagnostics, d => d.Id == "SY1015");
        AssertHasRealSpan(diag);
    }

    // #10 (c): [Quoted] params is not a sound facade shape (params object) -> SY1015, never a silent shape change.
    [Fact]
    public void QuotedParams_ReportsSY1015()
    {
        var diagnostics = RunAndGetDiagnostics(
            """
            using Synto.Templating;
            using Microsoft.CodeAnalysis.CSharp.Syntax;
            partial class Factory {}

            static partial class Bad {
                [SyntaxBuilder]
                public static ExpressionSyntax Many([Quoted] params ExpressionSyntax[] xs) => xs[0];
            }

            public class TestClass {
                [Template(typeof(Factory))]
                void Build([Splice] object x) => System.Console.WriteLine(Bad.Many(x));
            }
            """);
        var diag = Assert.Single(diagnostics, d => d.Id == "SY1015");
        AssertHasRealSpan(diag);
    }

    // #10: a private [SyntaxBuilder] would fail CS0122 in the generated facade -> reported SY1031 at the declaration.
    [Fact]
    public void PrivateBuilder_ReportsSY1031()
    {
        var diagnostics = RunFacadeGeneratorDiagnostics(
            """
            using Synto.Templating;
            using Microsoft.CodeAnalysis.CSharp.Syntax;

            static partial class Holder {
                [SyntaxBuilder]
                private static ExpressionSyntax Wrap([Quoted] ExpressionSyntax x) => x;
            }
            """);
        var diag = Assert.Single(diagnostics, d => d.Id == "SY1031");
        AssertHasRealSpan(diag);
    }

    // #10: a non-partial container cannot be reopened for the facade (CS0260) -> reported SY1031.
    [Fact]
    public void NonPartialContainer_ReportsSY1031()
    {
        var diagnostics = RunFacadeGeneratorDiagnostics(
            """
            using Synto.Templating;
            using Microsoft.CodeAnalysis.CSharp.Syntax;

            static class Holder {
                [SyntaxBuilder]
                public static ExpressionSyntax Wrap([Quoted] ExpressionSyntax x) => x;
            }
            """);
        var diag = Assert.Single(diagnostics, d => d.Id == "SY1031");
        AssertHasRealSpan(diag);
    }

    // #10: an interface container cannot host the reopened facade class (CS0261) -> reported SY1031.
    [Fact]
    public void InterfaceContainer_ReportsSY1031()
    {
        var diagnostics = RunFacadeGeneratorDiagnostics(
            """
            using Synto.Templating;
            using Microsoft.CodeAnalysis.CSharp.Syntax;

            partial interface IHolder {
                [SyntaxBuilder]
                public static ExpressionSyntax Wrap([Quoted] ExpressionSyntax x) => x;
            }
            """);
        var diag = Assert.Single(diagnostics, d => d.Id == "SY1031");
        AssertHasRealSpan(diag);
    }

    // #7: named arguments on a user [SyntaxBuilder] facade call are reported SY1029.
    [Fact]
    public void NamedArgumentsOnUserFacade_ReportsSY1029()
    {
        var diagnostics = RunAndGetDiagnostics(
            """
            using Synto.Templating;
            using Microsoft.CodeAnalysis.CSharp.Syntax;
            partial class Factory {}

            static partial class Builders {
                [SyntaxBuilder]
                public static ExpressionSyntax Wrap([Quoted] ExpressionSyntax x) => x;
            }

            public class TestClass {
                [Template(typeof(Factory))]
                void Build([Splice] object x) => System.Console.WriteLine(Builders.Wrap(x: x));
            }
            """);
        var diag = Assert.Single(diagnostics, d => d.Id == "SY1029");
        AssertHasRealSpan(diag);
    }
}

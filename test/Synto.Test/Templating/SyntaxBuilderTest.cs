using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Synto.Test.Templating;

/// <summary>
/// Exercises the generic syntax-builder mechanism (plan Task 3): the built-in <c>TypeOf</c> facade
/// (hand-authored on <c>Template</c>, recognized by binding) and <c>[SyntaxBuilder]</c> methods — including
/// the built-in <c>Member</c> builder, discovered exactly like a user builder (facade synthesized, recognized
/// structurally). A recognized facade call is rewritten to a fully-qualified static invocation of the paired
/// builder over processed arguments: a <c>[Quoted]</c> parameter receives the quote of the call argument, an
/// unmarked parameter the live value verbatim.
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

    private static CSharpCompilation CompilationWithSource(string source) =>
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
            "Post-generation compilation reported errors: " + string.Join("; ", errors.Select(d => d.Id + " " + d.GetMessage(System.Globalization.CultureInfo.InvariantCulture))));
    }

    /// <summary>
    /// Snapshot the factory for a template that calls the built-in <c>Member</c> builder. <c>Member</c> now
    /// rides the SAME structural discovery as a user <c>[SyntaxBuilder]</c> (plan Task 1): the authoring call
    /// is genuinely UNBOUND (no <c>Template.Member</c> facade), and structural discovery needs the injected
    /// <c>SyntoBuilders</c>' <c>[SyntaxBuilder]</c> declaration present in the analyzed compilation — supplied
    /// by <see cref="SurfaceInjectionGenerator"/>'s post-initialization output, exactly as a real consumer's
    /// paired generators see it (mirrors <c>SwitchBuilderTest</c>'s discovery setup for the other built-in-
    /// surface builders). Injecting into the COMPILATION first (rather than adding the generator alongside
    /// <see cref="TemplateFactorySourceGenerator"/> in one driver) keeps the snapshotted result scoped to just
    /// the factory output — no injected-marker noise — so the existing Factory.*.g snapshot stays put.
    /// </summary>
    private async Task VerifyBuiltInBuilderTemplate(string source)
    {
        var compilation = CompilationWithSource(source);

        var injectDriver = CSharpGeneratorDriver.Create(new SurfaceInjectionGenerator());
        injectDriver.RunGeneratorsAndUpdateCompilation(compilation, out var injectedCompilation, out _, TestContext.Current.CancellationToken);

        var result = _driver.RunGenerators(injectedCompilation);
        var ret = await Verify(result).UseDirectory("snapshots");
        Assert.NotEmpty(ret.Files);
    }

    private ImmutableArray<Diagnostic> RunAndGetDiagnostics(string source)
    {
        var compilation = CompilationWithSource(source);
        var result = _driver.RunGenerators(compilation);
        return result.GetRunResult().Diagnostics;
    }

    /// <summary>
    /// Same as <see cref="RunAndGetDiagnostics"/>, but injects the surface (<c>SyntoBuilders</c> + markers)
    /// into the compilation first, so a call to the built-in <c>Member</c> builder is discovered structurally
    /// (see <see cref="VerifyBuiltInBuilderTemplate"/>).
    /// </summary>
    private ImmutableArray<Diagnostic> RunAndGetDiagnosticsWithInjectedSurface(string source)
    {
        var compilation = CompilationWithSource(source);
        var injectDriver = CSharpGeneratorDriver.Create(new SurfaceInjectionGenerator());
        injectDriver.RunGeneratorsAndUpdateCompilation(compilation, out var injectedCompilation, out _, TestContext.Current.CancellationToken);
        var result = _driver.RunGenerators(injectedCompilation);
        return result.GetRunResult().Diagnostics;
    }

    private static void AssertHasRealSpan(Diagnostic diag)
    {
        Assert.NotEqual(Location.None, diag.Location);
        Assert.False(diag.Location.SourceSpan.IsEmpty);
    }

    // Built-in identifier builder with a CONSTANT name -> member access in output. `Member` is discovered
    // structurally (like any user [SyntaxBuilder]): the call is genuinely UNBOUND pre-generation (no
    // Template.Member facade), so this uses VerifyBuiltInBuilderTemplate rather than VerifyTemplate.
    [Fact]
    public async Task Member_WithConstantName_EmitsMemberAccess()
    {
        await VerifyBuiltInBuilderTemplate(
            """
            using Synto.Templating;
            using static Synto.Templating.Template;
            partial class Factory {}
            public class TestClass {
                [Template<Factory>]
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
                [Template<Factory>]
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
                [Template<Factory>]
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
                [Template<Factory>]
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
            "Post-generation compilation reported errors: " + string.Join("; ", errors.Select(d => d.Id + " " + d.GetMessage(System.Globalization.CultureInfo.InvariantCulture))));
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
                [Template<Factory>]
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
                [Template<Factory>]
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
            "Post-generation compilation reported errors: " + string.Join("; ", errors.Select(d => d.Id + " " + d.GetMessage(System.Globalization.CultureInfo.InvariantCulture))));
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
                [Template<Factory>]
                void Build() {
                    var node = Unquote<ExpressionSyntax>("node");
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
                [Template<Factory>]
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
                [Template<Factory>]
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
                [Template<Factory>]
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
                [Template<Factory>]
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
                [Template<Factory>]
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
                [Template<Factory>]
                void Build([Splice] object x) => System.Console.WriteLine(Builders.Wrap(x));
            }
            """);
        var diag = Assert.Single(diagnostics, d => d.Id == "SY1018");
        AssertHasRealSpan(diag);
    }

    // Zero-collision: the built-in Member builder is injected internal and called fully-qualified, so the
    // generated output compiles alongside the PUBLIC Synto.Core in the default scope with no CS0121 ambiguity
    // and no Synto.Core runtime dependency (the injected SyntoBuilders is the consumer's own internal copy).
    // Member now discovers/synthesizes like a user [SyntaxBuilder], so SyntaxBuilderFacadeGenerator must also
    // run to supply the inert facade the ORIGINAL (pre-rewrite) template body type-checks against.
    [Fact]
    public void BuiltInBuilderOutput_CompilesAlongsidePublicSyntoCore()
    {
        const string source =
            """
            using Synto.Templating;
            using static Synto.Templating.Template;
            using static Synto.Templating.SyntoBuilders;
            partial class Factory {}
            public class TestClass {
                [Template<Factory>]
                void Build([Splice] object instance) {
                    var x = Member<object>(instance, "Name");
                    System.Console.WriteLine(x);
                }
            }
            """;

        var compilation = CompilationWithSource(source);

        // Run all three generators: SurfaceInjection supplies the injected internal SyntoBuilders (and
        // markers) that structural discovery needs, SyntaxBuilderFacadeGenerator synthesizes the inert
        // `Member` facade the ORIGINAL template body type-checks against, and TemplateFactorySourceGenerator
        // rewrites the recognized call to the fully-qualified builder invocation.
        var driver = CSharpGeneratorDriver.Create(
            new SurfaceInjectionGenerator(),
            new SyntaxBuilderFacadeGenerator(),
            new TemplateFactorySourceGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var genDiagnostics, TestContext.Current.CancellationToken);

        Assert.Empty(genDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var errors = output.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.DoesNotContain(errors, d => d.Id == "CS0121");
        Assert.True(errors.Count == 0,
            "Post-generation compilation reported errors: " + string.Join("; ", errors.Select(d => d.Id + " " + d.GetMessage(System.Globalization.CultureInfo.InvariantCulture))));
    }

    // Task 2: the injected operator surface exists and its inert value facade synthesizes. A carrier that
    // imports `using static …Operators` and writes `System.Type t = TypeOf("System.Int32");` type-checks
    // against the synthesized `Operators.TypeOf(string) : System.Type`, and the factory rewrites the call to
    // the fully-qualified `OperatorBuilders.TypeOf(...)`. Uses the three-generator driver like
    // BuiltInBuilderOutput_CompilesAlongsidePublicSyntoCore.
    [Fact]
    public void OperatorSurface_TypeOf_Synthesizes_And_Compiles()
    {
        const string source =
            """
            using Synto.Templating;
            using static Synto.Templating.Operators;
            partial class Factory {}
            public class TestClass {
                [Template<Factory>]
                System.Type Build(int i) => TypeOf("System.Int32");
            }
            """;

        var compilation = CompilationWithSource(source);
        var driver = CSharpGeneratorDriver.Create(
            new SurfaceInjectionGenerator(),
            new SyntaxBuilderFacadeGenerator(),
            new TemplateFactorySourceGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var genDiagnostics, TestContext.Current.CancellationToken);

        Assert.Empty(genDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var errors = output.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0,
            "Post-generation compilation reported errors: " + string.Join("; ", errors.Select(d => d.Id + " " + d.GetMessage(System.Globalization.CultureInfo.InvariantCulture))));
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
                [Template<Factory>]
                void Build() {
                    var settings = new Settings();
                    System.Console.WriteLine(settings.Wrap()); // binds to Settings.Wrap -> quote verbatim
                }
            }
            """);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    // #7: named arguments on the built-in Member builder call are reported SY1029, not silently mispaired
    // (v1 policy: diagnose, do not reorder). Member is discovered structurally, so this fires from the
    // structural branch (FacadeCallFinder:104), not the (deleted) built-in-binding branch.
    [Fact]
    public void NamedArgumentsOnMemberFacade_ReportsSY1029()
    {
        var diagnostics = RunAndGetDiagnosticsWithInjectedSurface(
            """
            using Synto.Templating;
            using static Synto.Templating.Template;
            partial class Factory {}
            public class TestClass {
                [Template<Factory>]
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
                [Template<Factory>]
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
                [Template<Factory>]
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
                [Template<Factory>]
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
                [Template<Factory>]
                void Build([Splice] object x) => System.Console.WriteLine(Builders.Wrap(x: x));
            }
            """);
        var diag = Assert.Single(diagnostics, d => d.Id == "SY1029");
        AssertHasRealSpan(diag);
    }

    /// <summary>
    /// Same as <see cref="RunAndGetGeneratedText"/>, but injects the surface (<c>SyntoBuilders</c> + markers)
    /// into the compilation first, so a call to the built-in <c>Member</c> builder is discovered structurally
    /// (see <see cref="VerifyBuiltInBuilderTemplate"/>).
    /// </summary>
    private static string RunAndGetGeneratedTextWithInjectedSurface(string source)
    {
        var compilation = CompilationWithSource(source);
        var injectDriver = CSharpGeneratorDriver.Create(new SurfaceInjectionGenerator());
        injectDriver.RunGeneratorsAndUpdateCompilation(compilation, out var injectedCompilation, out _, TestContext.Current.CancellationToken);
        var driver = CSharpGeneratorDriver.Create(new TemplateFactorySourceGenerator());
        var runResult = driver.RunGenerators(injectedCompilation).GetRunResult();
        return string.Concat(runResult.Results
            .SelectMany(r => r.GeneratedSources)
            .Select(s => s.SourceText.ToString()));
    }

    private static string RunAndGetGeneratedText(string source)
    {
        var compilation = CompilationWithSource(source);
        var driver = CSharpGeneratorDriver.Create(new TemplateFactorySourceGenerator());
        var runResult = driver.RunGenerators(compilation).GetRunResult();
        return string.Concat(runResult.Results
            .SelectMany(r => r.GeneratedSources)
            .Select(s => s.SourceText.ToString()));
    }

    /// <summary>Runs ONLY the facade-synthesis generator and returns the concatenated synthesized facade source.</summary>
    private static string RunFacadeGeneratorSource(string source)
    {
        var compilation = CSharpCompilation.Create("Test",
            [CSharpSyntaxTree.ParseText(source)],
            references: References,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var driver = CSharpGeneratorDriver.Create(new SyntaxBuilderFacadeGenerator());
        var runResult = driver.RunGenerators(compilation).GetRunResult();
        return string.Concat(runResult.Results
            .SelectMany(r => r.GeneratedSources)
            .Select(s => s.SourceText.ToString()));
    }

    // [Constant] value-axis kind: the marked builder parameter (which RECEIVES the lifted ExpressionSyntax) surfaces
    // on the synthesized facade as an `object`-typed VALUE parameter — the author passes a factory-time value, not a
    // hand-built syntax node. A [Constant]-bearing builder is NOT its own facade (a facade IS synthesized, exactly as
    // for [Quoted]). Before recognition, [Constant] falls through to Staged and the facade param is typed as the
    // builder param type (ExpressionSyntax), so this fails until FacadeShape recognizes [Constant].
    [Fact]
    public void ConstantParam_SynthesizesObjectTypedFacadeValueParam()
    {
        var facadeSource = RunFacadeGeneratorSource(
            """
            using Synto.Templating;
            using Microsoft.CodeAnalysis.CSharp.Syntax;
            partial class Factory {}

            static partial class MyBuilders {
                [SyntaxBuilder]
                public static ExpressionSyntax Lit([Constant] ExpressionSyntax label) => label;
            }
            """);

        // A facade WAS synthesized for the [Constant]-bearing builder (not its-own-facade) ...
        Assert.Contains("Lit", facadeSource, StringComparison.Ordinal);
        // ... exposing the [Constant] parameter as an `object`-typed value parameter, NOT the builder param type.
        Assert.Contains("object label", facadeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ExpressionSyntax label", facadeSource, StringComparison.Ordinal);
    }

    // Task 4: a [Constant] builder argument is LIFTED at the binding seam via the shared value-lift — the factory
    // passes `(<value>).ToSyntax()` for each argument (an int literal, a string, a bool, an enum — the enum rides
    // the same `.ToSyntax()` shape — and an inlined generic type parameter), and the ToSyntax helper is injected.
    [Fact]
    public void ConstantArgs_LiftedViaToSyntaxAtBindingSeam()
    {
        var generated = RunAndGetGeneratedText(
            """
            using Synto.Templating;
            using Microsoft.CodeAnalysis.CSharp.Syntax;
            partial class Factory {}

            public enum Hue { Red, Green, Blue }

            static partial class B {
                [SyntaxBuilder]
                public static ExpressionSyntax Lit([Constant] ExpressionSyntax label) => label;
            }

            public class TestClass {
                [Template<Factory>]
                void Build<T>() {
                    System.Console.WriteLine(B.Lit(42));
                    System.Console.WriteLine(B.Lit("hi"));
                    System.Console.WriteLine(B.Lit(true));
                    System.Console.WriteLine(B.Lit(Hue.Red));
                    System.Console.WriteLine(B.Lit(default(T)));
                }
            }
            """);

        // Each [Constant] argument is lifted to `<value>.ToSyntax()` in the factory (verbatim value, not a raw node).
        Assert.Contains("42.ToSyntax()", generated, StringComparison.Ordinal);
        Assert.Contains("\"hi\".ToSyntax()", generated, StringComparison.Ordinal);
        Assert.Contains("true.ToSyntax()", generated, StringComparison.Ordinal);
        Assert.Contains("Hue.Red.ToSyntax()", generated, StringComparison.Ordinal);
        Assert.Contains("default(T).ToSyntax()", generated, StringComparison.Ordinal);
        // The file-local ToSyntax helper is injected (its `.ToSyntax()` calls now appear in the factory).
        Assert.Contains("ToSyntax", generated, StringComparison.Ordinal);
    }

    // Guard: [Constant] and [Quoted] on one parameter are mutually-exclusive value-axis kinds -> SY1015.
    [Fact]
    public void CombinedConstantAndQuotedMarkers_ReportsSY1015()
    {
        var diagnostics = RunAndGetDiagnostics(
            """
            using Synto.Templating;
            using Microsoft.CodeAnalysis.CSharp.Syntax;
            partial class Factory {}

            static partial class Bad {
                [SyntaxBuilder]
                public static ExpressionSyntax Combo([Constant, Quoted] ExpressionSyntax x) => x;
            }

            public class TestClass {
                [Template<Factory>]
                void Build([Splice] object x) => System.Console.WriteLine(Bad.Combo(x));
            }
            """);
        var diag = Assert.Single(diagnostics, d => d.Id == "SY1015");
        AssertHasRealSpan(diag);
    }

    // Guard: [Constant] and [TypeArg] are on different axes (value vs type) and must never combine -> SY1015.
    [Fact]
    public void CombinedConstantAndTypeArgMarkers_ReportsSY1015()
    {
        var diagnostics = RunAndGetDiagnostics(
            """
            using Synto.Templating;
            using Microsoft.CodeAnalysis.CSharp.Syntax;
            partial class Factory {}

            static partial class Bad {
                [SyntaxBuilder]
                public static ExpressionSyntax Combo([Constant, TypeArg] TypeSyntax x) => x;
            }

            public class TestClass {
                [Template<Factory>]
                void Build([Splice] object x) => System.Console.WriteLine(Bad.Combo<int>());
            }
            """);
        var diag = Assert.Single(diagnostics, d => d.Id == "SY1015");
        AssertHasRealSpan(diag);
    }

    // Guard: [Constant] params is not a sound facade shape (params object) -> SY1015, mirroring [Quoted] params.
    [Fact]
    public void ConstantParams_ReportsSY1015()
    {
        var diagnostics = RunAndGetDiagnostics(
            """
            using Synto.Templating;
            using Microsoft.CodeAnalysis.CSharp.Syntax;
            partial class Factory {}

            static partial class Bad {
                [SyntaxBuilder]
                public static ExpressionSyntax Many([Constant] params ExpressionSyntax[] xs) => xs[0];
            }

            public class TestClass {
                [Template<Factory>]
                void Build([Splice] object x) => System.Console.WriteLine(Bad.Many(x));
            }
            """);
        var diag = Assert.Single(diagnostics, d => d.Id == "SY1015");
        AssertHasRealSpan(diag);
    }

    // #12a: a carrier `using` whose namespace merely STARTS WITH "Synto" (e.g. `SyntoX.Helpers`) is a legitimate
    // third-party namespace and must transplant normally — the Synto-surface exclusion is exact-or-dotted-prefix
    // (`== "Synto"` or `StartsWith("Synto.")`), not a bare `StartsWith("Synto")`. A built-in facade call (Member,
    // whose `name` argument is a STAGED binding) triggers the carrier-usings transplant.
    [Fact]
    public void CarrierUsing_SyntoPrefixedButNotSyntoNamespace_IsTransplanted()
    {
        var generated = RunAndGetGeneratedTextWithInjectedSurface(
            """
            using Synto.Templating;
            using static Synto.Templating.Template;
            using SyntoX.Helpers;
            partial class Factory {}

            namespace SyntoX.Helpers { public static class H {} }

            public class TestClass {
                [Template<Factory>]
                void Build<[Splice] T>(T instance) {
                    var x = Member<object>(instance, "Foo");
                    System.Console.WriteLine(x);
                }
            }
            """);

        Assert.Contains("SyntoX.Helpers", generated, System.StringComparison.Ordinal);
    }

    // #12b: a builder call whose arguments are PURELY quoted (no staged/verbatim binding) does NOT drag the
    // carrier's usings into the factory — the transplant trigger is gated on a builder call carrying at least one
    // staged argument binding. `Wrap([Quoted] ExpressionSyntax x)` has only a quoted arg, so `using System.Linq;`
    // on the carrier is not transplanted.
    [Fact]
    public void QuotedOnlyBuilderCall_DoesNotTransplantCarrierUsings()
    {
        var generated = RunAndGetGeneratedText(
            """
            using System.Linq;
            using Synto.Templating;
            using Microsoft.CodeAnalysis.CSharp.Syntax;
            partial class Factory {}

            static partial class Builders {
                [SyntaxBuilder]
                public static ExpressionSyntax Wrap([Quoted] ExpressionSyntax x) => x;
            }

            public class TestClass {
                [Template<Factory>]
                void Build([Splice] object x) => System.Console.WriteLine(Builders.Wrap(x));
            }
            """);

        Assert.DoesNotContain("using System.Linq;", generated, System.StringComparison.Ordinal);
    }

    // Cacheability guard (#22): the facade generator's pipeline carries only the equatable
    // FacadeGenerationResult (no captured Compilation/SemanticModel/SyntaxNode), so an unrelated edit must
    // leave EVERY tracked step Cached/Unchanged. Fails first because the pipeline steps are untracked; passes
    // once WithTrackingName is wired. Iterating ALL steps closes the upstream blind spot a terminal-only guard
    // has (a future Combine/symbol capture would re-run here and fail).
    [Fact]
    public void FacadeGenerator_IsIncremental_OnUnrelatedEdit()
    {
        const string source =
            """
            using Synto.Templating;
            using Microsoft.CodeAnalysis.CSharp.Syntax;
            using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
            partial class Factory {}

            static partial class MyBuilders {
                [SyntaxBuilder]
                public static ExpressionSyntax Cast([TypeArg, ReturnType] TypeSyntax t, [Quoted] ExpressionSyntax x) => CastExpression(t, x);
            }

            public class TestClass {
                [Template<Factory>]
                void Build([Splice] object x) {
                    System.Console.WriteLine(MyBuilders.Cast<int>(x));
                }
            }
            """;

        var compilation = CompilationWithSource(source);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { new SyntaxBuilderFacadeGenerator().AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation, Xunit.TestContext.Current.CancellationToken);

        var modified = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("namespace Other { internal sealed class Unrelated { } }", cancellationToken: Xunit.TestContext.Current.CancellationToken));
        driver = driver.RunGenerators(modified, Xunit.TestContext.Current.CancellationToken);

        var result = driver.GetRunResult().Results.Single();

        CacheabilityAssert.AllStepsCachedOrUnchanged(
            result,
            new[] { BuilderTrackingNames.Transform, BuilderTrackingNames.Result });
    }

    // Task 20 (#23): executed end-to-end proof that a user [SyntaxBuilder]'s [Quoted] parameter receives the
    // QUOTE of the call argument at factory run time. The other builder facts only post-gen-COMPILE; this one
    // emits the generated assembly, Assembly.Load()s it, invokes the factory, and renders the returned node —
    // the builder must have run and produced `(int)42` from the quoted literal.
    [Fact]
    public void UserBuilder_Executed_QuotedArgumentReachesBuilder()
    {
        const string source =
            """
            using Synto.Templating;
            using Microsoft.CodeAnalysis.CSharp.Syntax;
            using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
            partial class Factory {}

            static partial class MyBuilders {
                [SyntaxBuilder]
                public static ExpressionSyntax Cast([TypeArg, ReturnType] TypeSyntax t, [Quoted] ExpressionSyntax x) => CastExpression(t, x);
            }

            [Template<Factory>]
            public class Reader {
                public int Build() => MyBuilders.Cast<int>(42);
            }
            """;

        var compilation = CompilationWithSource(source);

        var fullDriver = CSharpGeneratorDriver.Create(
            new SyntaxBuilderFacadeGenerator(),
            new TemplateFactorySourceGenerator());
        fullDriver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var genDiagnostics, TestContext.Current.CancellationToken);

        Assert.Empty(genDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var compileErrors = output.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(compileErrors.Count == 0,
            "Generated output did not compile: " + string.Join("; ", compileErrors.Select(d => d.Id + " " + d.GetMessage(System.Globalization.CultureInfo.InvariantCulture))));

        using var peStream = new System.IO.MemoryStream();
        var emit = output.Emit(peStream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(emit.Success, "Emit failed: " + string.Join("; ", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id + " " + d.GetMessage(System.Globalization.CultureInfo.InvariantCulture))));

        var assembly = Assembly.Load(peStream.ToArray());
        var readerFactory = assembly.GetType("Factory")!.GetMethod("Reader")!;
        var built = readerFactory.Invoke(null, null);

        var node = Assert.IsAssignableFrom<Microsoft.CodeAnalysis.SyntaxNode>(built!);
        var rendered = node.NormalizeWhitespace().ToString();
        Assert.Contains("(int)42", rendered, System.StringComparison.Ordinal);
    }

    // Operator-facade mechanism (spec §3.2/§3.3): [SyntaxBuilder(Returns=…, Facade=…)] synthesizes a
    // FIXED-return value facade into a DIFFERENT host type than the builder — a same-signature twin in the
    // builder's own type would be CS0111. An ExpressionSyntax builder `T` synthesizes a
    // `System.Type`-returning facade emitted into `Values`, NOT into `Ops`.
    [Fact]
    public void FixedReturnFacade_SynthesizedIntoFacadeHost()
    {
        var facadeSource = RunFacadeGeneratorSource(
            """
            using Synto.Templating;
            using Microsoft.CodeAnalysis.CSharp.Syntax;
            using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

            static partial class Ops {
                [SyntaxBuilder(Returns = typeof(System.Type), Facade = typeof(Values))]
                public static ExpressionSyntax T(string name) => TypeOfExpression(ParseTypeName(name));
            }

            static partial class Values {}
            """);

        // facade returns FIXED System.Type (no fresh TResult) ...
        Assert.Contains("System.Type T(string name)", facadeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TResult", facadeSource, StringComparison.Ordinal);
        // ... emitted into the Facade host `Values`, not the builder's own `Ops`.
        Assert.Contains("partial class Values", facadeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("partial class Ops", facadeSource, StringComparison.Ordinal);
    }

    // spec §3.3: Returns set without Facade is a declaration-site error — a fixed-return facade placed in the
    // builder's OWN type would be CS0111. The generator diagnoses it (SY1031) instead of emitting broken source.
    [Fact]
    public void FixedReturnWithoutFacade_ReportsSY1031()
    {
        var diagnostics = RunFacadeGeneratorDiagnostics(
            """
            using Synto.Templating;
            using Microsoft.CodeAnalysis.CSharp.Syntax;
            using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

            static partial class Ops {
                [SyntaxBuilder(Returns = typeof(System.Type))] // no Facade -> error
                public static ExpressionSyntax T(string name) => TypeOfExpression(ParseTypeName(name));
            }
            """);
        var diag = Assert.Single(diagnostics, d => d.Id == "SY1031");
        AssertHasRealSpan(diag);
    }
}

using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;


namespace Synto.Test.Templating;

/// <summary>
/// The empirical gate for the data-driven switch builders (plan Task 1). Proves the three residual unknowns the
/// list-shaped <c>[SyntaxBuilder]</c> recipe rests on: (1) quoted-vs-staged binding for <c>Case</c>/<c>Return</c>/
/// <c>Arm</c> over a LINQ projection feeding a host <c>Switch</c>'s staged <c>params</c> list; (2) overloaded
/// <c>[SyntaxBuilder]</c> discovery (two <c>Switch</c> overloads chosen by element type — must NOT trip SY1018);
/// (3) <c>params IEnumerable&lt;T&gt;</c> facade synthesis + expanded-form binding. The switch builders live in the
/// injected <c>SyntoBuilders</c> surface, so discovery runs through <see cref="SurfaceInjectionGenerator"/>'s
/// post-initialization output (exactly as a real consumer sees it).
/// </summary>
public class SwitchBuilderTest
{
    private static readonly MetadataReference CorlibReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
    private static readonly MetadataReference NetStandardReference = MetadataReference.CreateFromFile(Assembly.Load("netstandard, Version=2.0.0.0").Location);
    private static readonly MetadataReference SystemRuntimeReference = MetadataReference.CreateFromFile(Assembly.Load("System.Runtime, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location);

    private static readonly MetadataReference[] References =
    [
        CorlibReference,
        NetStandardReference,
        SystemRuntimeReference,
        MetadataReference.CreateFromFile(typeof(System.Console).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.SyntaxNode).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.CSharp.SyntaxKind).Assembly.Location),
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

    /// <summary>
    /// Snapshots the factory output for a switch-builder template AND post-generation-compiles it clean. Discovery
    /// runs through SurfaceInjection (post-init injects <c>SyntoBuilders</c> so the switch builders are found), the
    /// facade generator synthesizes the inert facades, and TemplateFactorySourceGenerator rewrites the recognized
    /// facade calls. The snapshot is scoped to just the factory generator's output (not the injected marker surface).
    /// </summary>
    private static async Task VerifySwitchTemplate(string source, bool postGenCompile = true)
    {
        var compilation = CompilationWithSource(source);

        // Snapshot: SurfaceInjection (discovery) + TemplateFactory (rewrite). Verify only the factory output.
        var snapDriver = CSharpGeneratorDriver.Create(
            new MatchSurfaceInjector(), new TemplateSurfaceInjector(),
            new TemplateFactorySourceGenerator());
        var runResult = snapDriver.RunGenerators(compilation, TestContext.Current.CancellationToken).GetRunResult();

        var factory = runResult.Results.Single(r => r.Generator.GetGeneratorType() == typeof(TemplateFactorySourceGenerator));
        var diag = string.Join("; ", runResult.Diagnostics.Select(d => d.Id + ":" + d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)));
        var hints = string.Join(", ", factory.GeneratedSources.Select(g => g.HintName));
        Assert.True(factory.GeneratedSources.Any(g => g.HintName.StartsWith("Factory.", StringComparison.Ordinal)),
            $"No Factory.* source. Hints=[{hints}] Diagnostics=[{diag}]");
        var factorySource = factory.GeneratedSources.Single(g => g.HintName.StartsWith("Factory.", StringComparison.Ordinal));
        await Verify(factorySource.SourceText.ToString(), "cs").UseDirectory("snapshots");

        // Post-generation compile: markers + SyntoBuilders injected, facades synthesized, factory rewritten.
        // Skipped for the statement-Switch section proof: a SwitchStatementSyntax island has no expression slot in
        // the quoted output, so PLACING it as an output statement needs statement-position splicing — a capability
        // beyond this spike's three unknowns (the section-builder BINDING is fully proven by the snapshot above).
        if (!postGenCompile)
            return;

        var fullDriver = CSharpGeneratorDriver.Create(
            new MatchSurfaceInjector(), new TemplateSurfaceInjector(),
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

    private const string Prelude =
        """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using Synto.Templating;
        using Microsoft.CodeAnalysis.CSharp.Syntax;
        using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
        using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;
        using static Synto.Templating.Template;
        using static Synto.Templating.SyntoBuilders;

        partial class Factory {}

        public readonly record struct Col(int Ordinal, string Name);
        """;

    // Unknown #1 (arm projection) + #2 (both overloads declared -> must not trip SY1018) + return-shape widening
    // for SwitchExpressionSyntax/SwitchExpressionArmSyntax. The switch EXPRESSION is an expression island spliced
    // into the `var` initializer position.
    [Fact]
    public Task SwitchExpression_ArmProjection()
    {
        return VerifySwitchTemplate(Prelude +
            """

            [Template<Factory>]
            public class Reader<[Splice] TRet>
            {
                private readonly IEnumerator<TRet> _e;
                public TRet GetTypedExpr(int i)
                {
                    var cols = Unquote<IReadOnlyList<Col>>();
                    var built = Switch<TRet>(i, cols.Where(c => c.Ordinal >= 0).Select(c =>
                        Arm(LiteralExpression(NumericLiteralExpression, Literal(c.Ordinal)),
                            Member<TRet>(_e.Current, c.Name))));
                    System.Console.WriteLine(built);
                    throw new System.Exception();
                }
            }
            """);
    }

    // Unknown #3 (params expanded form): Switch(i, Arm(...), Arm(...)) — comma-separated arms, not a projection.
    [Fact]
    public Task SwitchExpression_ExpandedArms()
    {
        return VerifySwitchTemplate(Prelude +
            """

            [Template<Factory>]
            public class Reader<[Splice] TRet>
            {
                private readonly IEnumerator<TRet> _e;
                public TRet GetTypedExpr(int i)
                {
                    var built = Switch<TRet>(i,
                        Arm(LiteralExpression(NumericLiteralExpression, Literal(0)), Member<TRet>(_e.Current, "A")),
                        Arm(LiteralExpression(NumericLiteralExpression, Literal(1)), Member<TRet>(_e.Current, "B")));
                    System.Console.WriteLine(built);
                    throw new System.Exception();
                }
            }
            """);
    }

    // Task 2 surface: the guarded arm (WhenArm -> `pattern when guard =>`) and the discard arm (DefaultArm -> `_ =>`),
    // both feeding the EXPRESSION Switch overload. The pattern is a staged constant; the guard and value are [Quoted]
    // islands (the guard `i > 0` references the in-scope parameter, the value expands a nested Member(...) builder).
    [Fact]
    public Task SwitchExpression_WhenArmAndDefaultArm()
    {
        return VerifySwitchTemplate(Prelude +
            """

            [Template<Factory>]
            public class Reader<[Splice] TRet>
            {
                private readonly IEnumerator<TRet> _e;
                public TRet GetTypedExpr(int i)
                {
                    var built = Switch<TRet>(i,
                        WhenArm(LiteralExpression(NumericLiteralExpression, Literal(0)), i > 0, Member<TRet>(_e.Current, "A")),
                        DefaultArm(Member<TRet>(_e.Current, "B")));
                    System.Console.WriteLine(built);
                    throw new System.Exception();
                }
            }
            """);
    }

    // Root-cause coverage for the facade is-a-ExpressionSyntax bridge + arity narrowing: a switch EXPRESSION
    // returned DIRECTLY as the method's TRet value. SwitchExpressionSyntax derives from ExpressionSyntax, so its
    // facade earns a fresh TResult and `Switch<TRet>(...)` lands in the TRet return slot; the explicit type
    // argument also disambiguates the expression overload from the (non-generic, statement) Switch overload so
    // they coexist without SY1018. The `.Append(DefaultArm(Throw<TRet>(...)))` exercises the Throw builder in
    // value position. Mirrors the ObjectReader typed-getter shape (`return i switch { ... _ => throw ... };`).
    [Fact]
    public Task SwitchExpression_ReturnedAsTypedValue_WithThrowingDefaultArm()
    {
        return VerifySwitchTemplate(Prelude +
            """

            [Template<Factory>]
            public class Reader<[Splice] TRet>
            {
                private readonly IEnumerator<TRet> _e;
                public TRet GetTypedExpr(int i)
                {
                    var cols = Unquote<IReadOnlyList<Col>>();
                    return Switch<TRet>(i, cols.Where(c => c.Ordinal >= 0).Select(c =>
                            Arm(LiteralExpression(NumericLiteralExpression, Literal(c.Ordinal)),
                                Member<TRet>(_e.Current, c.Name)))
                        .Append(DefaultArm(Throw<TRet>(new System.InvalidCastException("no")))));
                }
            }
            """);
    }

    // Task 2 surface: a trailing Default section appended after a Case, feeding the STATEMENT Switch overload. The
    // statement-returning Switch call in statement position splices the built SwitchStatement directly (no
    // ExpressionStatement wrapper), so it post-gen compiles. Proves the section-builder BINDING (Case + Default +
    // Return) plus statement-position placement.
    [Fact]
    public Task SwitchStatement_TrailingDefault()
    {
        return VerifySwitchTemplate(Prelude +
            """

            [Template<Factory>]
            public class Reader<[Splice] TRet>
            {
                private readonly IEnumerator<TRet> _e;
                public TRet GetTypedStmt(int i)
                {
                    Switch(i,
                        Case(0, Return(Member<TRet>(_e.Current, "A"))),
                        Default(Return(Member<TRet>(_e.Current, "B"))));
                    throw new System.Exception();
                }
            }
            """);
    }

    // Unknown #1 (section projection): Case + Return over the live projection, feeding the STATEMENT Switch overload.
    // Proves the section builders (Case/Return) + the SwitchSectionSyntax/SwitchStatementSyntax return-shapes BIND
    // and rewrite correctly (snapshot). The statement-returning Switch call in statement position splices the built
    // SwitchStatement directly (no ExpressionStatement wrapper), so it post-gen compiles.
    [Fact]
    public Task SwitchStatement_CaseProjection()
    {
        return VerifySwitchTemplate(Prelude +
            """

            [Template<Factory>]
            public class Reader<[Splice] TRet>
            {
                private readonly IEnumerator<TRet> _e;
                public TRet GetTypedStmt(int i)
                {
                    var cols = Unquote<IReadOnlyList<Col>>();
                    Switch(i, cols.Where(c => c.Ordinal >= 0).Select(c =>
                        Case(c.Ordinal,
                            Return(Member<TRet>(_e.Current, c.Name)))));
                    throw new System.Exception();
                }
            }
            """);
    }

    // Regression (staged builder-projection lift): a staged live-projection argument over a NON-LITERAL collection
    // type — `EquatableArray<Col>`, which is NOT a built-in literal type — feeding a host `Switch` builder must NOT
    // emit a spurious depth-zero `columns.ToSyntax()` lift into the factory preamble. The generic `ToSyntax<T>`
    // fallback binds at COMPILE time for any T, so the other projection tests (which use `IReadOnlyList<Col>` and
    // only post-gen-COMPILE) never caught this — but at factory RUNTIME `EquatableArray<Col>.ToSyntax()` hits the
    // `_ => throw new NotImplementedException(...)` arm. This test actually EXECUTES the generated factory over an
    // `EquatableArray`-typed staged collection and asserts it builds the switch node without throwing.
    [Fact]
    public void SwitchStatement_ProjectionOverEquatableArray_ExecutesWithoutSpuriousLift()
    {
        var source = Prelude +
            """

            [Template<Factory>]
            public class Reader<[Splice] TRet>
            {
                private readonly IEnumerator<TRet> _e;
                public TRet GetTypedStmt(int i)
                {
                    var columns = Unquote<Synto.Generators.EquatableArray<Col>>();
                    Switch(i, columns.Where(c => c.Ordinal >= 0).Select(c =>
                        Case(c.Ordinal,
                            Return(Member<TRet>(_e.Current, c.Name)))));
                    throw new System.Exception();
                }
            }
            """;

        var compilation = CompilationWithSource(source);

        var driver = CSharpGeneratorDriver.Create(
            new MatchSurfaceInjector(), new TemplateSurfaceInjector(),
            // The template Unquotes `Synto.Generators.EquatableArray<Col>`; the trio now injects from
            // Synto.Incremental's IncrementalSurfaceInjector, so run it too or EquatableArray is unresolved.
            new IncrementalSurfaceInjector(),
            new SyntaxBuilderFacadeGenerator(),
            new TemplateFactorySourceGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var genDiagnostics, TestContext.Current.CancellationToken);

        Assert.Empty(genDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var compileErrors = output.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(compileErrors.Count == 0,
            "Generated output did not compile: " + string.Join("; ", compileErrors.Select(d => d.Id + " " + d.GetMessage(System.Globalization.CultureInfo.InvariantCulture))));

        // Emit the generated assembly and run the factory: the injected LiteralSyntaxExtensions.ToSyntax<T> throws
        // NotImplementedException for a non-literal type, so a spurious `columns.ToSyntax()` in the preamble blows up
        // here at invocation time (pre-fix). Post-fix the projection runs verbatim and the switch node is built.
        using var peStream = new System.IO.MemoryStream();
        var emit = output.Emit(peStream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(emit.Success, "Emit failed: " + string.Join("; ", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id + " " + d.GetMessage(System.Globalization.CultureInfo.InvariantCulture))));

        var assembly = Assembly.Load(peStream.ToArray());
        var readerMethod = assembly.GetType("Factory")!.GetMethod("Reader")!;

        // Args in factory-parameter order: (TypeSyntax TRet, EquatableArray<Col> columns). A default (empty)
        // EquatableArray<Col> is enough — the spurious preamble lift runs BEFORE the projection, so contents are moot.
        var columnsType = readerMethod.GetParameters()[1].ParameterType;
        object? columnsArg = System.Activator.CreateInstance(columnsType);
        object tretArg = SyntaxFactory.ParseTypeName("int");

        object? built;
        try
        {
            built = readerMethod.Invoke(null, new[] { tretArg, columnsArg });
        }
        catch (System.Reflection.TargetInvocationException ex)
        {
            Assert.Fail("Executing the generated factory threw (spurious ToSyntax lift over a non-literal collection): "
                + ex.InnerException?.GetType().Name + ": " + ex.InnerException?.Message);
            throw; // unreachable
        }

        var node = Assert.IsAssignableFrom<Microsoft.CodeAnalysis.SyntaxNode>(built!);
        Assert.Contains("switch", node.NormalizeWhitespace().ToString(), System.StringComparison.Ordinal);
    }

    // Task 3: a hand-written FIXED switch statement + switch expression must quote VERBATIM through a [Template]
    // (no builders, no islands). The quoter handles arbitrary nodes generically; this pins that SwitchStatementSyntax
    // and SwitchExpressionSyntax are among them. Post-gen compile confirms the emitted factory resolves.
    [Fact]
    public Task FixedSwitch_RoundTrip()
    {
        return VerifySwitchTemplate(Prelude +
            """

            [Template<Factory>]
            public class Reader
            {
                public int GetStmt(int x)
                {
                    switch (x)
                    {
                        case 1:
                            return 10;
                        default:
                            return 0;
                    }
                }

                public int GetExpr(int x)
                {
                    return x switch
                    {
                        1 => 10,
                        _ => 0,
                    };
                }
            }
            """);
    }
}

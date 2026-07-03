using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Synto.Generators;
using Synto.Generators.Templating;

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

    private static Compilation CompilationWithSource(string source) =>
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
            new SurfaceInjectionGenerator(),
            new TemplateFactorySourceGenerator());
        var runResult = snapDriver.RunGenerators(compilation, TestContext.Current.CancellationToken).GetRunResult();

        var factory = runResult.Results.Single(r => r.Generator.GetGeneratorType() == typeof(TemplateFactorySourceGenerator));
        var diag = string.Join("; ", runResult.Diagnostics.Select(d => d.Id + ":" + d.GetMessage()));
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
            new SurfaceInjectionGenerator(),
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

            [Template(typeof(Factory))]
            public class Reader<[Splice] TRet>
            {
                private readonly IEnumerator<TRet> _e;
                public TRet GetTypedExpr(int i)
                {
                    var cols = Parameter<IReadOnlyList<Col>>();
                    var built = Switch(i, cols.Where(c => c.Ordinal >= 0).Select(c =>
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

            [Template(typeof(Factory))]
            public class Reader<[Splice] TRet>
            {
                private readonly IEnumerator<TRet> _e;
                public TRet GetTypedExpr(int i)
                {
                    var built = Switch(i,
                        Arm(LiteralExpression(NumericLiteralExpression, Literal(0)), Member<TRet>(_e.Current, "A")),
                        Arm(LiteralExpression(NumericLiteralExpression, Literal(1)), Member<TRet>(_e.Current, "B")));
                    System.Console.WriteLine(built);
                    throw new System.Exception();
                }
            }
            """);
    }

    // Unknown #1 (section projection): Case + Return over the live projection, feeding the STATEMENT Switch overload.
    // Proves the section builders (Case/Return) + the SwitchSectionSyntax/SwitchStatementSyntax return-shapes BIND
    // and rewrite correctly (snapshot). Post-gen compile is skipped: the resulting SwitchStatementSyntax has no
    // expression slot to be spliced into as an output statement — statement-position splicing is a follow-up
    // capability (out of this spike's three unknowns), so the statement Switch's OUTPUT placement is deferred.
    [Fact]
    public Task SwitchStatement_CaseProjection()
    {
        return VerifySwitchTemplate(postGenCompile: false, source: Prelude +
            """

            [Template(typeof(Factory))]
            public class Reader<[Splice] TRet>
            {
                private readonly IEnumerator<TRet> _e;
                public TRet GetTypedStmt(int i)
                {
                    var cols = Parameter<IReadOnlyList<Col>>();
                    Switch(i, cols.Where(c => c.Ordinal >= 0).Select(c =>
                        Case(LiteralExpression(NumericLiteralExpression, Literal(c.Ordinal)),
                            Return(Member<TRet>(_e.Current, c.Name)))));
                    throw new System.Exception();
                }
            }
            """);
    }
}

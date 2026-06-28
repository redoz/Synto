using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Synto.Test.Templating;

/// <summary>
/// Covers the <c>[Splice]</c> <b>member generator</b>: a static method inside a <c>[Template]</c> type that
/// runs at factory-build time and returns <c>MemberDeclarationSyntax</c> /
/// <c>IEnumerable&lt;MemberDeclarationSyntax&gt;</c> whose results are spliced into the generated type. This
/// fixture pins the invalid-shape diagnostics (SY1019–SY1021), the canonical member-per-column emission
/// snapshot, and the incremental-caching guard. The reference set mirrors
/// <see cref="InjectedSurfaceCompletenessTest"/> so a template referencing
/// <c>IEnumerable&lt;MemberDeclarationSyntax&gt;</c> binds against the injected surface + Roslyn alone.
/// </summary>
public class SpliceMemberGeneratorTest
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
    ];

    private static ImmutableArray<Diagnostic> RunAndGetDiagnostics(string source)
    {
        var compilation = CSharpCompilation.Create("SpliceMemberGeneratorDiagnosticsTest",
            [CSharpSyntaxTree.ParseText(source)],
            references: References,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new SurfaceInjectionGenerator(),
            new TemplateFactorySourceGenerator());

        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);

        return driver.GetRunResult().Diagnostics;
    }

    private const string NonStaticGenerator =
        """
        using System.Collections.Generic;
        using Synto.Templating;
        using static Synto.Templating.Template;
        using Microsoft.CodeAnalysis.CSharp.Syntax;
        partial class Factory {}
        [Template(typeof(Factory))]
        public class Reader {
            [Splice]                                                   // instance -> SY1019
            IEnumerable<MemberDeclarationSyntax> Gen() { yield break; }
        }
        """;

    private const string BadReturnGenerator =
        """
        using Synto.Templating;
        using static Synto.Templating.Template;
        partial class Factory {}
        [Template(typeof(Factory))]
        public class Reader {
            [Splice] static int Gen() => 0;                            // bad return -> SY1020
        }
        """;

    private const string HasParametersGenerator =
        """
        using System.Collections.Generic;
        using Synto.Templating;
        using static Synto.Templating.Template;
        using Microsoft.CodeAnalysis.CSharp.Syntax;
        partial class Factory {}
        [Template(typeof(Factory))]
        public class Reader {
            [Splice] static IEnumerable<MemberDeclarationSyntax> Gen(int x) { yield break; }  // params -> SY1021
        }
        """;

    [Fact]
    public void NonStaticSpliceMethod_ReportsSY1019()
    {
        var diagnostics = RunAndGetDiagnostics(NonStaticGenerator);
        Assert.Single(diagnostics, d => d.Id == "SY1019");
    }

    [Fact]
    public void BadReturnTypeSpliceMethod_ReportsSY1020()
    {
        var diagnostics = RunAndGetDiagnostics(BadReturnGenerator);
        Assert.Single(diagnostics, d => d.Id == "SY1020");
    }

    [Fact]
    public void ParameterizedSpliceMethod_ReportsSY1021()
    {
        var diagnostics = RunAndGetDiagnostics(HasParametersGenerator);
        Assert.Single(diagnostics, d => d.Id == "SY1021");
    }

    private const string MemberPerColumnTemplate =
        """
        using System;
        using System.Collections.Generic;
        using Synto.Templating;
        using static Synto.Templating.Template;
        using Microsoft.CodeAnalysis.CSharp.Syntax;
        using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
        using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;

        partial class Factory {}

        public readonly record struct Col(int Ordinal, string Name);

        [Template(typeof(Factory))]
        public class Reader {
            [Splice]
            static IEnumerable<MemberDeclarationSyntax> Accessors() {
                var columns = Parameter<IReadOnlyList<Col>>();
                foreach (var c in columns)
                    yield return MethodDeclaration(PredefinedType(Token(IntKeyword)), Identifier("Get" + c.Name))
                        .AddModifiers(Token(PublicKeyword))
                        .WithExpressionBody(ArrowExpressionClause(
                            LiteralExpression(NumericLiteralExpression, Literal(c.Ordinal))))
                        .WithSemicolonToken(Token(SemicolonToken));
            }
        }
        """;

    private static Compilation CompilationWithSource(string source)
    {
        // The snapshot / cacheability runs reference the public Synto.Core surface (so the markers + Template
        // facade bind) and run ONLY TemplateFactorySourceGenerator, so the snapshot captures just the factory
        // output (not the ~two dozen injected marker files).
        MetadataReference[] references = [.. References, MetadataReference.CreateFromFile(SyntoCoreAssembly.Location)];
        return CSharpCompilation.Create("SpliceMemberGeneratorSnapshotTest",
            [CSharpSyntaxTree.ParseText(source)],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    /// <summary>
    /// Snapshot of the canonical member-per-column emission: pins the
    /// <c>BuildList&lt;MemberDeclarationSyntax&gt;(ListSegment&lt;MemberDeclarationSyntax&gt;.Run(...))</c>
    /// member-list shape and the verbatim factory-time generator local function.
    /// </summary>
    [Fact]
    public async Task MemberGenerator_CanonicalShape()
    {
        var compilation = CompilationWithSource(MemberPerColumnTemplate);

        var driver = CSharpGeneratorDriver.Create(new TemplateFactorySourceGenerator());
        var result = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);

        var ret = await Verify(result).UseDirectory("snapshots");
        Assert.NotEmpty(ret.Files);
    }

    /// <summary>
    /// Incremental-caching guard (spec §4 / Global Constraints): all the member-generator analysis runs inside
    /// the <c>ForAttributeWithMetadataName</c> transform and captures no
    /// <c>Compilation</c>/<c>SemanticModel</c>/<c>SyntaxNode</c> into pipeline state, so an unrelated edit must
    /// leave EVERY tracked step Cached/Unchanged.
    /// </summary>
    [Fact]
    public void SpliceMemberGeneratorTemplate_IsIncrementalOnUnrelatedEdit()
    {
        var compilation = CompilationWithSource(MemberPerColumnTemplate);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { new TemplateFactorySourceGenerator().AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);

        var modified = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("namespace Other { internal sealed class Unrelated { } }"));
        driver = driver.RunGenerators(modified, TestContext.Current.CancellationToken);

        var result = driver.GetRunResult().Results.Single();

        CacheabilityAssert.AllStepsCachedOrUnchanged(
            result,
            new[] { TemplateTrackingNames.Transform, TemplateTrackingNames.Result });
    }
}

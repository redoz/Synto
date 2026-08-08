using System.Collections.Immutable;
using System.Linq;
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
            new MatchSurfaceInjector(), new TemplateSurfaceInjector(),
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
        [Template<Factory>]
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
        [Template<Factory>]
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
        [Template<Factory>]
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

    // --- #9 characterization: non-foldable Unquote<T>() shapes inside a [Splice] member generator body are
    // caught by the unquote-axis StagedParameterFinder and reported as a located, fatal SY1010 BEFORE the
    // splice-member emitter ever runs, so no factory is produced. This pins that the "residue reaches the
    // emitter verbatim" leak scenario (#9) is unreachable: the shape is diagnosed, never silent. If these
    // facts ever regress (SY1010 stops firing / a factory appears), a splice-specific diagnostic is warranted.

    // Inline zero-arg Unquote<int>() with no enclosing variable declaration and no explicit name.
    private const string InlineZeroArgParameterGenerator =
        """
        using System.Collections.Generic;
        using Synto.Templating;
        using static Synto.Templating.Template;
        using Microsoft.CodeAnalysis.CSharp.Syntax;
        using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
        using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;
        partial class Factory {}
        [Template<Factory>]
        public class Reader {
            [Splice]
            static IEnumerable<MemberDeclarationSyntax> Gen() {
                yield return MethodDeclaration(PredefinedType(Token(IntKeyword)), Identifier("Get" + Unquote<int>()))
                    .WithSemicolonToken(Token(SemicolonToken));
            }
        }
        """;

    // Compound initializer: Unquote<int>() is not the sole initializer, so no name can be folded from it.
    private const string CompoundInitializerParameterGenerator =
        """
        using System.Collections.Generic;
        using Synto.Templating;
        using static Synto.Templating.Template;
        using Microsoft.CodeAnalysis.CSharp.Syntax;
        using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
        using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;
        partial class Factory {}
        [Template<Factory>]
        public class Reader {
            [Splice]
            static IEnumerable<MemberDeclarationSyntax> Gen() {
                int n = Unquote<int>() + 1;
                yield return MethodDeclaration(PredefinedType(Token(IntKeyword)), Identifier("Get" + n))
                    .WithSemicolonToken(Token(SemicolonToken));
            }
        }
        """;

    // Non-constant name argument: a non-const local is not a foldable constant-string parameter name.
    private const string NonConstantNameParameterGenerator =
        """
        using System.Collections.Generic;
        using Synto.Templating;
        using static Synto.Templating.Template;
        using Microsoft.CodeAnalysis.CSharp.Syntax;
        using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
        using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;
        partial class Factory {}
        [Template<Factory>]
        public class Reader {
            [Splice]
            static IEnumerable<MemberDeclarationSyntax> Gen() {
                string nm = "col";
                yield return MethodDeclaration(PredefinedType(Token(IntKeyword)), Identifier("Get" + Unquote<int>(nm)))
                    .WithSemicolonToken(Token(SemicolonToken));
            }
        }
        """;

    private static (ImmutableArray<Diagnostic> Diagnostics, int FactoryTreeCount) RunFactoryOnly(string source)
    {
        var compilation = CompilationWithSource(source);
        var driver = CSharpGeneratorDriver.Create(new TemplateFactorySourceGenerator());
        var runResult = driver.RunGenerators(compilation, TestContext.Current.CancellationToken).GetRunResult();
        return (runResult.Diagnostics, runResult.GeneratedTrees.Length);
    }

    [Fact]
    public void InlineZeroArgParameterInSplice_ReportsSY1010_AndEmitsNoFactory()
    {
        var (diagnostics, factoryTreeCount) = RunFactoryOnly(InlineZeroArgParameterGenerator);
        Assert.Single(diagnostics, d => d.Id == "SY1010" && d.Location != Location.None);
        Assert.Equal(0, factoryTreeCount);
    }

    [Fact]
    public void CompoundInitializerParameterInSplice_ReportsSY1010_AndEmitsNoFactory()
    {
        var (diagnostics, factoryTreeCount) = RunFactoryOnly(CompoundInitializerParameterGenerator);
        Assert.Single(diagnostics, d => d.Id == "SY1010" && d.Location != Location.None);
        Assert.Equal(0, factoryTreeCount);
    }

    [Fact]
    public void NonConstantNameParameterInSplice_ReportsSY1010_AndEmitsNoFactory()
    {
        var (diagnostics, factoryTreeCount) = RunFactoryOnly(NonConstantNameParameterGenerator);
        Assert.Single(diagnostics, d => d.Id == "SY1010" && d.Location != Location.None);
        Assert.Equal(0, factoryTreeCount);
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

        [Template<Factory>]
        public class Reader {
            [Splice]
            static IEnumerable<MemberDeclarationSyntax> Accessors() {
                var columns = Unquote<IReadOnlyList<Col>>();
                foreach (var c in columns)
                    yield return MethodDeclaration(PredefinedType(Token(IntKeyword)), Identifier("Get" + c.Name))
                        .AddModifiers(Token(PublicKeyword))
                        .WithExpressionBody(ArrowExpressionClause(
                            LiteralExpression(NumericLiteralExpression, Literal(c.Ordinal))))
                        .WithSemicolonToken(Token(SemicolonToken));
            }
        }
        """;

    // #24 regression (list-seam dispatch keyed on (owner, element type)): a [Splice] member generator's owner
    // type declaration exposes BOTH a (non-empty) AttributeLists list and a Members list keyed on the same
    // owner node. The central element-type guard must route ONLY the member list through MemberSegmentStep; the
    // sibling attribute list must fall through to base quoting. This carrier keeps a surviving user attribute
    // ([System.Obsolete]) so the attribute list is non-empty and actually reaches the owner's step set — pinning
    // that deleting the per-step self-guards keeps the sibling list correct.
    private const string SpliceCarrierWithSurvivingAttributeTemplate =
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

        [Obsolete]
        [Template<Factory>]
        public class Reader {
            [Splice]
            static IEnumerable<MemberDeclarationSyntax> Accessors() {
                var columns = Unquote<IReadOnlyList<Col>>();
                foreach (var c in columns)
                    yield return MethodDeclaration(PredefinedType(Token(IntKeyword)), Identifier("Get" + c.Name))
                        .AddModifiers(Token(PublicKeyword))
                        .WithExpressionBody(ArrowExpressionClause(
                            LiteralExpression(NumericLiteralExpression, Literal(c.Ordinal))))
                        .WithSemicolonToken(Token(SemicolonToken));
            }
        }
        """;

    [Fact]
    public void SpliceMemberList_DispatchesWhileSiblingAttributeListFallsThrough()
    {
        var compilation = CompilationWithSource(SpliceCarrierWithSurvivingAttributeTemplate);
        var driver = CSharpGeneratorDriver.Create(new TemplateFactorySourceGenerator());
        var runResult = driver.RunGenerators(compilation, TestContext.Current.CancellationToken).GetRunResult();

        Assert.Empty(runResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = Assert.Single(runResult.GeneratedTrees).ToString();

        // Member list still routes through the segment step.
        Assert.Contains("BuildList<MemberDeclarationSyntax>", generated, StringComparison.Ordinal);
        // The sibling (non-empty) attribute list fell through to base quoting, NOT the segment step — the
        // surviving [Obsolete] attribute is quoted as an AttributeList, never wrapped in a BuildList run.
        Assert.Contains("Obsolete", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildList<AttributeListSyntax>", generated, StringComparison.Ordinal);
    }

    private static CSharpCompilation CompilationWithSource(string source)
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
            CSharpSyntaxTree.ParseText("namespace Other { internal sealed class Unrelated { } }", cancellationToken: TestContext.Current.CancellationToken));
        driver = driver.RunGenerators(modified, TestContext.Current.CancellationToken);

        var result = driver.GetRunResult().Results.Single();

        CacheabilityAssert.AllStepsCachedOrUnchanged(
            result,
            new[] { TemplateTrackingNames.GenericTransform, TemplateTrackingNames.GenericResult });
    }
}

using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Synto.Test.Templating;

/// <summary>
/// The real guarantee behind the scan-based helper injection: it mechanically proves that the surface the
/// generators inject is COMPLETE for everything a rich template emits, no matter which helpers turn out to
/// be needed.
/// </summary>
/// <remarks>
/// <para>
/// Unlike <see cref="ZeroCollisionTest"/> (which references the public <c>Synto.Core</c> runtime to prove
/// the emitted helpers do not COLLIDE with it), this test references NO <c>Synto.Core</c> at all. The only
/// Synto types available to the generated factory are therefore exactly what the generators inject:
/// <list type="bullet">
/// <item>the <c>internal</c> marker surface (<c>TemplateAttribute</c>, <c>UnquoteAttribute</c>, <c>SpliceAttribute</c>,
/// <c>Syntax&lt;T&gt;</c>, ...) from <see cref="SurfaceInjectionGenerator"/>, needed so the consumer
/// template's <c>[Template]</c>/<c>[Unquote]</c>/<c>Syntax&lt;&gt;</c> usage binds; and</item>
/// <item>the file-local helper class(es) (<c>ToSyntax</c>/<c>ToTypeSyntax</c>/<c>OrNullLiteralExpression</c>)
/// the scan-based injection appends into the generated factory file.</item>
/// </list>
/// Both generators are run in the driver so both surfaces are present. We then compile the post-generation
/// output and assert there are NO error diagnostics. If a helper a template emits were dropped (the exact
/// bug this hardening guards against), the generated code would fail with CS0103/CS1061/CS0246 here.
/// </para>
/// <para>
/// This is assertion-based on purpose (not snapshot-based): the point is that the injected surface
/// COMPILES, regardless of the precise generated text.
/// </para>
/// </remarks>
public class InjectedSurfaceCompletenessTest
{
    private static readonly MetadataReference CorlibReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
    private static readonly MetadataReference NetStandardReference = MetadataReference.CreateFromFile(Assembly.Load("netstandard, Version=2.0.0.0").Location);
    private static readonly MetadataReference SystemRuntimeReference = MetadataReference.CreateFromFile(Assembly.Load("System.Runtime, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location);

    /// <summary>
    /// A deliberately RICH template that exercises many constructs, to maximize the set of runtime helpers
    /// the generator emits into the factory:
    /// <list type="bullet">
    /// <item>multiple statements;</item>
    /// <item>an <c>[Unquote]</c> value parameter -> <c>value.ToSyntax()</c>;</item>
    /// <item>an <c>[Unquote]</c> type parameter -> <c>typeof(T).ToTypeSyntax()</c>;</item>
    /// <item>a <c>Syntax&lt;T&gt;</c> parameter (spliced syntax);</item>
    /// <item>string interpolation;</item>
    /// <item>a <c>for</c> loop and a <c>foreach</c> loop;</item>
    /// <item>an object creation (<c>new List&lt;T&gt;()</c>);</item>
    /// <item>a range expression (<c>a..b</c>) and a bare <c>return;</c> and an optional initializer, which
    /// exercise the quoter's NULLABLE-child path (the path behind <c>OrNullLiteralExpression</c>).</item>
    /// </list>
    /// Note the template does NOT reference any Synto helper by name in its own body, so nothing here can
    /// be mistaken for a quoted helper reference — every emitted helper call is produced by the generator.
    /// </summary>
    private const string RichTemplate =
        """
        using System;
        using System.Collections.Generic;
        using Synto.Templating;

        partial class Factory {}

        public class TestClass {
            [Template(typeof(Factory))]
            void KitchenSink<[Unquote] T>([Unquote] int count, Syntax<int> spliced) {
                List<T> list = new List<T>();
                var span = (1..count);
                for (int i = 0; i < count; i++) {
                    Console.WriteLine($"item {i} of {count} -> {spliced}");
                }
                foreach (var item in list) {
                    Console.WriteLine(item);
                }
                int? optional = null;
                if (optional is null) {
                    return;
                }
            }
        }
        """;

    /// <summary>
    /// A template declared inside a <b>namespace AND a nested class</b>, deliberately using both
    /// <c>[Unquote] T</c> (triggers <c>ToTypeSyntax</c>) and <c>[Unquote] int count</c> (triggers
    /// <c>ToSyntax</c>), plus a <c>Syntax&lt;int&gt;</c> splice parameter. This exercises the
    /// generator's namespaced/nested-class code path for placing the file-local helpers inside the
    /// enclosing namespace rather than at the global compilation-unit level.
    /// </summary>
    private const string NamespacedNestedTemplate =
        """
        using System;
        using System.Collections.Generic;
        using Synto.Templating;

        namespace My.Nested.Space
        {
            public partial class Outer
            {
                public partial class Factory { }

                [Template(typeof(Factory))]
                static void Build<[Unquote] T>([Unquote] int count, Syntax<int> extra)
                {
                    List<T> list = new List<T>();
                    Console.WriteLine($"count={count} extra={extra()}");
                }
            }
        }
        """;

    /// <summary>
    /// A deliberately RICH <b>live-staged</b> template (plan Task 10) that exercises the full staging surface in
    /// one body — compiled against ONLY the injected surface, so a dropped/uninjected staging helper or built-in
    /// builder is a hard compile error:
    /// <list type="bullet">
    /// <item>a <c>Parameter&lt;T&gt;()</c> live root (the column list) lifted to a factory parameter;</item>
    /// <item>a <c>Unquote&lt;T&gt;()</c> local (computed at factory time) fed by a <c>[Unquote]</c> method parameter;</item>
    /// <item>a live <c>foreach</c> that unrolls at factory time and emits the file-local <c>BuildList</c>
    /// collection helper;</item>
    /// <item>the built-in <c>Member</c> builder (member access over a <c>[Splice]</c> instance with a
    /// live member name) and the built-in <c>TypeOf</c> builder (a <c>typeof(...)</c> from a constant name).</item>
    /// </list>
    /// Every piece must resolve against the injected surface alone — the internal markers/facades from
    /// <see cref="SurfaceInjectionGenerator"/> (incl. the internal <c>SyntoBuilders.Member</c>/<c>TypeOf</c>) and
    /// the scan-injected <c>file static</c> helpers (<c>BuildList</c>/<c>ToSyntax</c>) — with NO <c>Synto.Core</c>.
    /// </summary>
    private const string StagedRichTemplate =
        """
        using System;
        using System.Collections.Generic;
        using Synto.Templating;
        using static Synto.Templating.Template;

        partial class Factory {}

        public readonly record struct Col(int Ordinal, string Name);

        public class TestClass {
            [Template(typeof(Factory))]
            object StagedKitchenSink([Splice] object row, int i, [Unquote] int bump) {
                var columns = Parameter<IReadOnlyList<Col>>();   // live Parameter root -> factory parameter
                var offset = Unquote(bump + 1);                      // live local fed by the [Unquote] parameter
                Console.WriteLine(offset);                        // offset live -> lifts to an int literal (island)
                foreach (var c in columns)                        // live foreach -> unrolls -> BuildList run
                    if (i == c.Ordinal)                           // i quoted; c.Ordinal (loop var) -> int literal
                        return Member<object>(row, c.Name);       // Member builder -> row.<Name>
                var t = TypeOf("System.Int32");                   // TypeOf builder -> typeof(System.Int32)
                Console.WriteLine(t);
                throw new System.IndexOutOfRangeException();      // quoted island, verbatim
            }
        }
        """;

    /// <summary>
    /// Regression guard (deep end-review, correctness): a live <c>Parameter&lt;T&gt;()</c> whose FACTORY-parameter
    /// name differs from the source local — here an explicit <c>Parameter&lt;…&gt;("columns")</c> bound to local
    /// <c>cols</c> — referenced BY VALUE <b>inside</b> a live region island. The island lift
    /// (<c>StagedRegionEmitter.CollectLiftPoints</c>) must rename the live-root reference to the factory parameter
    /// just like every other staging path; if it lifts the original identifier (<c>cols.Count.ToSyntax()</c>) the
    /// trimmed local is gone and the generated factory fails with CS0103. The foreach-driver and depth-0 paths
    /// already rename; this pins the island path.
    /// </summary>
    private const string RenamedRootInRegionTemplate =
        """
        using System;
        using System.Collections.Generic;
        using Synto.Templating;
        using static Synto.Templating.Template;

        partial class Factory {}

        public readonly record struct Col(int Ordinal, string Name);

        public class TestClass {
            [Template(typeof(Factory))]
            object RenamedRoot(int i) {
                var cols = Parameter<IReadOnlyList<Col>>("columns");  // factory param `columns` != local `cols`
                foreach (var c in cols)
                    if (i == c.Ordinal)
                        Console.WriteLine(cols.Count);                // live root by VALUE inside the island
                throw new System.IndexOutOfRangeException();
            }
        }
        """;

    [Fact]
    public void RenamedStagedRoot_ReferencedByValueInRegion_Compiles()
    {
        var compilation = CSharpCompilation.Create("RenamedStagedRootInRegionTest",
            [CSharpSyntaxTree.ParseText(RenamedRootInRegionTemplate)],
            references:
            [
                CorlibReference,
                NetStandardReference,
                SystemRuntimeReference,
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.SyntaxNode).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.CSharp.SyntaxKind).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Collections.Immutable.ImmutableArray).Assembly.Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Linq, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Collections, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Runtime.Extensions, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(
            new SurfaceInjectionGenerator(),
            new TemplateFactorySourceGenerator());

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics, TestContext.Current.CancellationToken);

        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains(output.SyntaxTrees, t => t.FilePath.Contains("Factory.RenamedRoot"));

        var errors = output.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.True(errors.Count == 0,
            "Post-generation compilation of a renamed live root referenced by value inside a region reported errors "
            + "(the island lift did not rename the live root to its factory parameter): "
            + string.Join("; ", errors.Select(d => d.Id + " " + d.GetMessage())));
    }

    /// <summary>
    /// A <c>&lt;[Splice] T&gt;</c> generic type parameter referenced in TYPE position (the type of the output
    /// method's <c>instance</c> parameter). This is the type-axis miscompile: before the fix the spliced type
    /// landed as an <c>ExpressionSyntax</c> factory parameter and the generated factory emitted
    /// <c>Parameter(..., T, ...)</c> into a <c>TypeSyntax</c> slot, failing post-generation compilation with
    /// CS1503 (ExpressionSyntax -&gt; TypeSyntax). The fix types the splice factory parameter as
    /// <c>TypeSyntax</c>. This test COMPILES the generated output (not just snapshots it), so the miscompile
    /// is caught.
    /// </summary>
    private const string SpliceTypeArgTemplate =
        """
        using System;
        using Synto.Templating;

        partial class Factory {}

        public class TestClass {
            [Template(typeof(Factory))]
            void Build<[Splice] T>(T instance) {
                System.Console.WriteLine(instance);
            }
        }
        """;

    [Fact]
    public void SpliceGenericTypeArg_InTypePosition_Compiles()
    {
        var compilation = CSharpCompilation.Create("SpliceTypeArgCompileTest",
            [CSharpSyntaxTree.ParseText(SpliceTypeArgTemplate)],
            references:
            [
                CorlibReference,
                NetStandardReference,
                SystemRuntimeReference,
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.SyntaxNode).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.CSharp.SyntaxKind).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Collections.Immutable.ImmutableArray).Assembly.Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Linq, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Collections, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Runtime.Extensions, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(
            new SurfaceInjectionGenerator(),
            new TemplateFactorySourceGenerator());

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics, TestContext.Current.CancellationToken);

        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains(output.SyntaxTrees, t => t.FilePath.Contains("Factory.Build"));

        var errors = output.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.True(errors.Count == 0,
            "Post-generation compilation of a [Splice] generic type parameter used in type position reported errors "
            + "(the spliced type was not emitted as TypeSyntax): "
            + string.Join("; ", errors.Select(d => d.Id + " " + d.GetMessage())));
    }

    /// <summary>
    /// A <c>[Quote] int count</c> value parameter driving a <c>for</c> loop bound. The headline
    /// <c>[Quote]</c> guarantee (spec §3): the loop stays a <b>runtime</b> construct bounded by the lifted value
    /// (a single <c>ForStatement</c> whose bound is <c>count.ToSyntax()</c>) rather than being unrolled into
    /// <c>count</c> copies of <c>ret++</c> the way an <c>[Unquote] count</c> would. Compiled against ONLY the
    /// injected surface so an unrecognized <c>[Quote]</c> (no factory parameter, <c>count</c> emitted as a free
    /// identifier) is a hard CS0103 here.
    /// </summary>
    private const string QuoteLoopTemplate =
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

    [Fact]
    public void QuoteParam_InLoopCondition_KeepsRuntimeLoop_Compiles()
    {
        var compilation = CSharpCompilation.Create("QuoteLoopCompileTest",
            [CSharpSyntaxTree.ParseText(QuoteLoopTemplate)],
            references:
            [
                CorlibReference,
                NetStandardReference,
                SystemRuntimeReference,
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.SyntaxNode).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.CSharp.SyntaxKind).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Collections.Immutable.ImmutableArray).Assembly.Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Linq, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Collections, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Runtime.Extensions, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(
            new SurfaceInjectionGenerator(),
            new TemplateFactorySourceGenerator());

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics, TestContext.Current.CancellationToken);

        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var factoryTree = output.SyntaxTrees.SingleOrDefault(t => t.FilePath.Contains("Factory.Loop"));
        Assert.NotNull(factoryTree);

        var errors = output.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.True(errors.Count == 0,
            "Post-generation compilation of a [Quote] loop-bound parameter reported errors "
            + "([Quote] was not recognized -> `count` left as a free identifier / no factory parameter): "
            + string.Join("; ", errors.Select(d => d.Id + " " + d.GetMessage())));

        var factorySource = factoryTree.ToString();

        // The factory must take the value parameter `int count` ...
        Assert.Contains("int count", factorySource, StringComparison.Ordinal);
        // ... lift it (the [Quote] value-lift preamble local) ...
        Assert.Contains("syntaxForQuote_count", factorySource, StringComparison.Ordinal);
        // ... and emit a SINGLE runtime ForStatement node bounded by the lift (the loop is NOT unrolled).
        Assert.Contains("ForStatement", factorySource, StringComparison.Ordinal);
    }

    /// <summary>
    /// The inline <c>Quote(value)</c> liveness boundary (spec §4): a <b>computed</b> factory-time value
    /// (<c>bound = Unquote(count)</c>, live) used as a loop bound through <c>Quote(bound)</c> keeps the loop a
    /// runtime construct (a single <c>ForStatement</c> bounded by <c>bound.ToSyntax()</c>) instead of unrolling.
    /// Without the <c>Quote(...)</c> shield the same live <c>bound</c> in the condition makes the <c>for</c>
    /// StagedControl and it unrolls — so <c>Quote(...)</c> is the ONLY way to keep a loop over a computed bound.
    /// </summary>
    private const string QuoteCallLoopTemplate =
        """
        using System;
        using Synto.Templating;
        using static Synto.Templating.Template;

        partial class Factory {}

        public class TestClass {
            [Template(typeof(Factory), Options = TemplateOption.Bare)]
            static void Loop([Unquote] int count) {
                int ret = 0;
                var bound = Unquote(count);
                for (int i = 0; i < Quote(bound); i++)
                    ret++;
            }
        }
        """;

    [Fact]
    public void QuoteCall_OverComputedBound_KeepsRuntimeLoop_Compiles()
    {
        var compilation = CSharpCompilation.Create("QuoteCallLoopCompileTest",
            [CSharpSyntaxTree.ParseText(QuoteCallLoopTemplate)],
            references:
            [
                CorlibReference,
                NetStandardReference,
                SystemRuntimeReference,
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.SyntaxNode).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.CSharp.SyntaxKind).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Collections.Immutable.ImmutableArray).Assembly.Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Linq, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Collections, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Runtime.Extensions, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(
            new SurfaceInjectionGenerator(),
            new TemplateFactorySourceGenerator());

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics, TestContext.Current.CancellationToken);

        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var factoryTree = output.SyntaxTrees.SingleOrDefault(t => t.FilePath.Contains("Factory.Loop"));
        Assert.NotNull(factoryTree);

        var errors = output.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.True(errors.Count == 0,
            "Post-generation compilation of an inline Quote(computed-bound) loop reported errors "
            + "(Quote(...) was not recognized / not shielded -> the loop unrolled or `Quote` left unbound): "
            + string.Join("; ", errors.Select(d => d.Id + " " + d.GetMessage())));

        var factorySource = factoryTree.ToString();

        // The loop is KEPT: the factory builds a single runtime ForStatement node (shielded by Quote), it is not
        // unrolled into a runtime loop that appends `ret++` statements.
        Assert.Contains("ForStatement", factorySource, StringComparison.Ordinal);
    }

    /// <summary>
    /// The headline <c>[Splice]</c> member-generator guarantee (spec §3–§4): a static method returning
    /// <c>IEnumerable&lt;MemberDeclarationSyntax&gt;</c> runs at factory-build time (its <c>foreach</c> is a real
    /// loop, NOT unrolled), its <c>Parameter&lt;…&gt;()</c> folds into the factory signature, and the members it
    /// yields are spliced into the produced type's member list via <c>BuildList&lt;MemberDeclarationSyntax&gt;</c>.
    /// Compiled against ONLY the injected surface + Roslyn, so a dropped helper / unrecognized generator is a hard
    /// compile error.
    /// </summary>
    private const string SpliceMemberGeneratorTemplate =
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
                var columns = Parameter<IReadOnlyList<Col>>();   // folds into the Factory signature
                foreach (var c in columns)                       // real factory-time loop (NOT unrolled)
                    yield return MethodDeclaration(PredefinedType(Token(IntKeyword)), Identifier("Get" + c.Name))
                        .AddModifiers(Token(PublicKeyword))
                        .WithExpressionBody(ArrowExpressionClause(
                            LiteralExpression(NumericLiteralExpression, Literal(c.Ordinal))))
                        .WithSemicolonToken(Token(SemicolonToken));
            }
        }
        """;

    [Fact]
    public void SpliceMemberGenerator_EmitsMemberPerColumn_Compiles()
    {
        var compilation = CSharpCompilation.Create("SpliceMemberGeneratorCompileTest",
            [CSharpSyntaxTree.ParseText(SpliceMemberGeneratorTemplate)],
            references:
            [
                CorlibReference,
                NetStandardReference,
                SystemRuntimeReference,
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.SyntaxNode).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.CSharp.SyntaxKind).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Collections.Immutable.ImmutableArray).Assembly.Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Linq, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Collections, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Runtime.Extensions, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(
            new SurfaceInjectionGenerator(),
            new TemplateFactorySourceGenerator());

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics, TestContext.Current.CancellationToken);

        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var factoryTree = output.SyntaxTrees.SingleOrDefault(t => t.FilePath.Contains("Factory.Reader"));
        Assert.NotNull(factoryTree);

        var errors = output.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.True(errors.Count == 0,
            "Post-generation compilation of a [Splice] member generator reported errors "
            + "(the generator was not emitted / its members were not spliced): "
            + string.Join("; ", errors.Select(d => d.Id + " " + d.GetMessage())));

        var factorySource = factoryTree.ToString();

        // The generator's Parameter<…>() folded into the factory signature ...
        Assert.Contains("columns", factorySource, StringComparison.Ordinal);
        // ... and its yielded members were spliced into the type's member list (the member-axis BuildList).
        Assert.Contains("BuildList<MemberDeclarationSyntax>", factorySource, StringComparison.Ordinal);
    }

    [Fact]
    public void InjectedSurfaceIsCompleteForStagedTemplate()
    {
        var compilation = CSharpCompilation.Create("InjectedSurfaceCompletenessStagedTest",
            [CSharpSyntaxTree.ParseText(StagedRichTemplate)],
            references:
            [
                CorlibReference,
                NetStandardReference,
                SystemRuntimeReference,
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                // The Roslyn assemblies the GENERATED factory body uses (the staged surface emits
                // MemberAccessExpression / TypeOfExpression / SyntaxList runs via the injected builders + helpers).
                MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.SyntaxNode).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.CSharp.SyntaxKind).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Collections.Immutable.ImmutableArray).Assembly.Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Linq, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Collections, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Runtime.Extensions, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
                // DELIBERATELY NO reference to Synto.Core: the staging surface (markers + internal SyntoBuilders +
                // file-local BuildList/ToSyntax) must all come from the generators, or this fails to compile.
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(
            new SurfaceInjectionGenerator(),
            new TemplateFactorySourceGenerator());

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics, TestContext.Current.CancellationToken);

        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        Assert.Contains(output.SyntaxTrees, t => t.FilePath.Contains("Factory.StagedKitchenSink"));

        var errors = output.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.True(errors.Count == 0,
            "Post-generation compilation of a STAGED template against ONLY the injected surface reported errors "
            + "(the injected staging surface — markers, SyntoBuilders, BuildList/ToSyntax helpers — is incomplete): "
            + string.Join("; ", errors.Select(d => d.Id + " " + d.GetMessage())));
    }

    [Fact]
    public void InjectedSurfaceIsCompleteForRichTemplate()
    {
        var compilation = CSharpCompilation.Create("InjectedSurfaceCompletenessTest",
            [CSharpSyntaxTree.ParseText(RichTemplate)],
            references:
            [
                CorlibReference,
                NetStandardReference,
                SystemRuntimeReference,
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                // The Roslyn assemblies the GENERATED factory body uses (MethodDeclaration, ExpressionSyntax,
                // SyntaxFactory, ...) plus the helper bodies (ParseTypeName, etc.).
                MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.SyntaxNode).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.CSharp.SyntaxKind).Assembly.Location),
                // The injected Synto.Generators cacheability toolkit (EquatableArray<T>) references
                // ImmutableArray<T>; a real netstandard2.0 generator project always has System.Collections
                // .Immutable available (the Roslyn package depends on it), so reference it here too.
                MetadataReference.CreateFromFile(typeof(System.Collections.Immutable.ImmutableArray).Assembly.Location),
                // System.Linq / System.Collections for the consumer template body (List<T>, etc.).
                MetadataReference.CreateFromFile(Assembly.Load("System.Linq, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Collections, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Runtime.Extensions, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
                // DELIBERATELY NO reference to Synto.Core: the only Synto types available to the generated
                // factory are the ones the generators inject (internal markers + file-local helpers). That
                // is precisely what makes a dropped helper a hard compile error below.
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // Run BOTH generators: SurfaceInjectionGenerator supplies the internal markers the consumer
        // template binds against (post-init), TemplateFactorySourceGenerator emits the factory + the
        // scan-detected file-local helpers.
        var driver = CSharpGeneratorDriver.Create(
            new SurfaceInjectionGenerator(),
            new TemplateFactorySourceGenerator());

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics, TestContext.Current.CancellationToken);

        // The generators must not have reported any errors (e.g. an internal error producing the factory).
        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // The generator must actually have produced the factory (markers from post-init are added too, so
        // require strictly more than just the marker files: assert a Factory file specifically exists).
        Assert.Contains(output.SyntaxTrees, t => t.FilePath.Contains("Factory.KitchenSink"));

        // Compile the POST-GENERATION output and assert there are NO error diagnostics. If the injected
        // surface were incomplete for anything the rich template emits, this is where it fails:
        //   CS0103 "The name 'ToSyntax' does not exist" / CS1061 (missing extension) / CS0246 (missing type)
        //   for a dropped helper or marker, or CS0121 for an ambiguity.
        var errors = output.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.True(errors.Count == 0,
            "Post-generation compilation against ONLY the injected surface reported errors (the injected "
            + "surface is incomplete for what the template emits): "
            + string.Join("; ", errors.Select(d => d.Id + " " + d.GetMessage())));
    }

    /// <summary>
    /// Same guarantee as <see cref="InjectedSurfaceIsCompleteForRichTemplate"/> but for a template
    /// declared inside a <b>namespace AND a nested class</b> (<c>My.Nested.Space.Outer.Factory</c>).
    /// This exercises the code path in <see cref="TemplateFactorySourceGenerator"/> that places the
    /// file-local helpers <em>inside</em> the enclosing namespace member rather than at the global
    /// compilation-unit level (the branch taken when <c>targetSyntax</c> is a
    /// <c>FileScopedNamespaceDeclarationSyntax</c>). The template deliberately uses both
    /// <c>[Unquote] T</c> (emits <c>ToTypeSyntax</c>) and <c>[Unquote] int count</c> (emits
    /// <c>ToSyntax</c>) so BOTH helpers must be placed correctly inside the namespace scope.
    /// </summary>
    [Fact]
    public void InjectedSurfaceIsCompleteForNamespacedNestedTemplate()
    {
        var compilation = CSharpCompilation.Create("InjectedSurfaceCompletenessNamespacedTest",
            [CSharpSyntaxTree.ParseText(NamespacedNestedTemplate)],
            references:
            [
                CorlibReference,
                NetStandardReference,
                SystemRuntimeReference,
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                // The Roslyn assemblies the GENERATED factory body uses (MethodDeclaration, ExpressionSyntax,
                // SyntaxFactory, ...) plus the helper bodies (ParseTypeName, etc.).
                MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.SyntaxNode).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.CSharp.SyntaxKind).Assembly.Location),
                // The injected Synto.Generators cacheability toolkit (EquatableArray<T>) references
                // ImmutableArray<T>; a real netstandard2.0 generator project always has System.Collections
                // .Immutable available (the Roslyn package depends on it), so reference it here too.
                MetadataReference.CreateFromFile(typeof(System.Collections.Immutable.ImmutableArray).Assembly.Location),
                // System.Linq / System.Collections for the consumer template body (List<T>, etc.).
                MetadataReference.CreateFromFile(Assembly.Load("System.Linq, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Collections, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Runtime.Extensions, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
                // DELIBERATELY NO reference to Synto.Core: the only Synto types available to the generated
                // factory are the ones the generators inject (internal markers + file-local helpers). That
                // is precisely what makes a dropped helper a hard compile error below.
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // Run BOTH generators: SurfaceInjectionGenerator supplies the internal markers the consumer
        // template binds against (post-init), TemplateFactorySourceGenerator emits the factory + the
        // scan-detected file-local helpers inside the namespace.
        var driver = CSharpGeneratorDriver.Create(
            new SurfaceInjectionGenerator(),
            new TemplateFactorySourceGenerator());

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics, TestContext.Current.CancellationToken);

        // The generators must not have reported any errors.
        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // The generator must actually have produced the namespaced factory file.
        Assert.Contains(output.SyntaxTrees, t => t.FilePath.Contains("Factory.Build"));

        // Compile the POST-GENERATION output and assert there are NO error diagnostics. If the
        // helpers were not placed inside the namespace (or were dropped entirely), the generated
        // code would fail with CS0103/CS1061/CS0246 because the file-local extension methods would
        // be invisible from within the namespace scope where the factory method lives.
        var errors = output.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.True(errors.Count == 0,
            "Post-generation compilation against ONLY the injected surface reported errors (the injected "
            + "surface is incomplete or misplaced for the namespaced/nested template): "
            + string.Join("; ", errors.Select(d => d.Id + " " + d.GetMessage())));
    }
}

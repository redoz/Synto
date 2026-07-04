using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Synto.Generators.Templating;
namespace Synto.Test.Templating;

/// <summary>
/// Exercises the additional live-control shapes (plan Task 7 / Locked Names §3): <c>for</c>/<c>while</c> with a
/// live driver, <c>if</c> branch-specialization on a live condition, and mutable accumulation across iterations.
/// Each shape runs its control statement <em>verbatim</em> in the factory body — collecting the quote of its
/// (purely-live-lifted) island statements into a run while keeping live locals/accumulators as real runtime
/// state — and the region's owning container block is replaced by a <c>BuildList</c> of that run.
/// </summary>
public class RunCollectShapesTest
{
    private static readonly MetadataReference CorlibReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
    private static readonly MetadataReference NetStandardReference = MetadataReference.CreateFromFile(Assembly.Load("netstandard, Version=2.0.0.0").Location);
    private static readonly MetadataReference SystemRuntimeReference = MetadataReference.CreateFromFile(Assembly.Load("System.Runtime, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location);

    private static CSharpCompilation CreateCompilation() =>
        CSharpCompilation.Create("Test",
            [],
            references:
            [
                CorlibReference,
                NetStandardReference,
                SystemRuntimeReference,
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                MetadataReference.CreateFromFile(SyntoCoreAssembly.Location),
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private readonly Compilation _baseCompilation = CreateCompilation();
    private readonly GeneratorDriver _driver = CSharpGeneratorDriver.Create(new TemplateFactorySourceGenerator());

    private Compilation CompilationWithSource(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        Assert.Empty(syntaxTree.GetDiagnostics());
        return _baseCompilation.AddSyntaxTrees(syntaxTree);
    }

    private async Task VerifyTemplate(string source)
    {
        var compilation = CompilationWithSource(source);
        Assert.Empty(compilation.GetDiagnostics().Where(diag => diag.Severity == DiagnosticSeverity.Error));
        var result = _driver.RunGenerators(compilation);
        var ret = await Verify(result).UseDirectory("snapshots");
        Assert.NotEmpty(ret.Files);
    }

    // for over a live Parameter<T>() bound: unrolls in the factory, the loop variable lifting to a literal.
    [Fact]
    public async Task StagedFor_Unrolls()
    {
        await VerifyTemplate(
            """
            using Synto.Templating;
            using static Synto.Templating.Template;
            partial class Factory {}
            public class TestClass {
                [Template<Factory>]
                void Build() {
                    var n = Parameter<int>();
                    for (int k = 0; k < n; k++)        // live for -> unrolls in the factory
                        System.Console.WriteLine(k);   // k -> int literal per iteration
                }
            }
            """);
    }

    // while over a live driver: the live local `k` is the runtime driver/accumulator (k++ stays verbatim),
    // each iteration's island collects with `k` lifted to a literal.
    [Fact]
    public async Task StagedWhile_Unrolls()
    {
        await VerifyTemplate(
            """
            using Synto.Templating;
            using static Synto.Templating.Template;
            partial class Factory {}
            public class TestClass {
                [Template<Factory>]
                void Build() {
                    var n = Parameter<int>();
                    var k = Unquote(0);
                    while (k < n) {                    // live while -> unrolls in the factory
                        System.Console.WriteLine(k);   // k -> int literal per iteration
                        k++;                            // live mutation, stays verbatim
                    }
                }
            }
            """);
    }

    // if on a live condition: branch specialization — exactly one branch's islands reach the output.
    [Fact]
    public async Task StagedIf_Specializes()
    {
        await VerifyTemplate(
            """
            using Synto.Templating;
            using static Synto.Templating.Template;
            partial class Factory {}
            public class TestClass {
                [Template<Factory>]
                void Build() {
                    var n = Parameter<int>();
                    if (n > 0)                                  // live condition -> picks a branch at factory time
                        System.Console.WriteLine("positive");
                    else
                        System.Console.WriteLine("nonpositive");
                }
            }
            """);
    }

    // element-access mutation of a live target: `totals[c.Ordinal] = …` — the mutation target resolves to the
    // array indexer (not a bare live identifier), yet all roots (`totals`, `columns`, the loop var) are live, so
    // the whole statement must stay VERBATIM in the scaffold (a factory-time side effect) rather than being
    // lifted as an assignment value into the run. (Task 8 / #8.)
    [Fact]
    public async Task ElementAccessMutationOfLiveTarget_StaysVerbatim()
    {
        await VerifyTemplate(
            """
            using Synto.Templating;
            using System.Collections.Generic;
            using static Synto.Templating.Template;
            partial class Factory {}
            public readonly record struct Col(int Ordinal, string Name);
            public class TestClass {
                [Template<Factory>]
                void Build() {
                    var columns = Parameter<IReadOnlyList<Col>>();
                    var totals = Parameter<int[]>();
                    foreach (var c in columns)
                        totals[c.Ordinal] = c.Ordinal * 2;   // element-access mutation of a live target
                }
            }
            """);
    }

    // member-access mutation of a live target: `acc.Total += …` — the target resolves to a field/property symbol
    // (not a bare live identifier); all roots live, so the compound-assignment mutation must stay VERBATIM in the
    // scaffold rather than being lifted as a value. (Task 8 / #8.)
    [Fact]
    public async Task MemberAccessMutationOfLiveTarget_StaysVerbatim()
    {
        await VerifyTemplate(
            """
            using Synto.Templating;
            using System.Collections.Generic;
            using static Synto.Templating.Template;
            partial class Factory {}
            public sealed class Acc { public int Total; }
            public readonly record struct Col(int Ordinal, string Name);
            public class TestClass {
                [Template<Factory>]
                void Build() {
                    var columns = Parameter<IReadOnlyList<Col>>();
                    var acc = Parameter<Acc>();
                    foreach (var c in columns)
                        acc.Total += c.Ordinal;              // member-access mutation of a live target
                }
            }
            """);
    }

    // mutable accumulation across iterations: `sum` is an accumulator declared before the loop, mutated inside,
    // and read by each island (lifting to its current literal value).
    [Fact]
    public async Task MutableAccumulationAcrossIterations_Works()
    {
        await VerifyTemplate(
            """
            using Synto.Templating;
            using System.Collections.Generic;
            using static Synto.Templating.Template;
            partial class Factory {}
            public readonly record struct Col(int Ordinal, string Name);
            public class TestClass {
                [Template<Factory>]
                void Build() {
                    var columns = Parameter<IReadOnlyList<Col>>();
                    int sum = 0;                            // accumulator (live: mutated with a live value)
                    foreach (var c in columns) {
                        System.Console.WriteLine(sum);      // sum -> current literal value
                        sum += c.Ordinal;                   // live mutation across iterations
                    }
                }
            }
            """);
    }
}

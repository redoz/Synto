using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Synto.Test.Match;

/// <summary>
/// Snapshot (Verify) goldens for the matching generator's emitted output. The generated-output SHAPE — the
/// nested result record, the matcher method signature, the file-scoped namespace, the three usings and the
/// <c>#nullable enable</c> — is the snapshot-pinned one-way door; an unexplained snapshot change is a finding,
/// not a rubber stamp. (The matcher BODY's indexing/scan style is non-binding and snapshot-reversible.)
/// The per-assembly <c>IsExternalInit</c> polyfill is captured as its own one-line golden.
/// </summary>
public class MatchSnapshotTests
{
    private static Task VerifyMatcher(string source)
    {
        var compilation = MatchTestHarness.CreateCompilation(source);
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        var driver = CSharpGeneratorDriver
            .Create(new MatchFactorySourceGenerator())
            .RunGenerators(compilation, TestContext.Current.CancellationToken);

        return Verify(driver).UseDirectory("snapshots");
    }

    [Fact]
    public Task ExpressionSingle_Literal()
    {
        // Zero-capture expression-Single: an empty-member nested record + a structural walk for literal `1`,
        // under a file-scoped namespace with NO embedded polyfill (the polyfill is a separate post-init file).
        return VerifyMatcher(
            """
            using Synto.Matching;

            namespace Demo;

            partial class M { }

            public class Consumer
            {
                [Match<M>(MatchOption.Single)]
                static object LiteralOne() => 1;
            }
            """);
    }
}

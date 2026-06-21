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

    [Fact]
    public Task ExpressionSingle_Captures()
    {
        // Two plain [Capture] expression holes: a positional record SumMatch(ExpressionSyntax A,
        // ExpressionSyntax B) + a structural `<a> + <b>` walk binding each operand into a cap_ local.
        return VerifyMatcher(
            """
            using Synto.Matching;

            namespace Demo;

            partial class M { }

            public class Consumer
            {
                [Match<M>(MatchOption.Single)]
                static object Sum([Capture] int a, [Capture] int b) => a + b;
            }
            """);
    }

    [Fact]
    public Task ExpressionSingle_Narrowed()
    {
        // [Capture<TNode>] narrowing: the member is typed the fully-qualified narrow type and the guard is an
        // `is not <narrow>` that both rejects a non-matching kind and binds the typed local.
        return VerifyMatcher(
            """
            using Synto.Matching;
            using Microsoft.CodeAnalysis.CSharp.Syntax;

            namespace Demo;

            partial class M { }

            public class Consumer
            {
                [Match<M>(MatchOption.Single)]
                static object Narrowed([Capture<InvocationExpressionSyntax>] object call) => call;
            }
            """);
    }

    [Fact]
    public Task ExpressionSingle_Wildcard()
    {
        // Expr.Any<T>() in the right operand: an `is not ExpressionSyntax` assertion with NO capture member,
        // and no recursion into the marker invocation. Only the left [Capture] surfaces.
        return VerifyMatcher(
            """
            using Synto.Matching;

            namespace Demo;

            partial class M { }

            public class Consumer
            {
                [Match<M>(MatchOption.Single)]
                static object EqualsAnything([Capture] object lhs) => lhs == Expr.Any<object>();
            }
            """);
    }

    [Fact]
    public Task ExpressionSingle_NonLinear()
    {
        // Reused capture: ONE record member (X), a first-site cap_x binding + a reuse-site temp guarded by
        // IsEquivalentTo(cap_x). No second member.
        return VerifyMatcher(
            """
            using Synto.Matching;

            namespace Demo;

            partial class M { }

            public class Consumer
            {
                [Match<M>(MatchOption.Single)]
                static object SelfEq([Capture] object x) => x == x;
            }
            """);
    }
}

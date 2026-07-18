using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Synto.Test.Model;

/// <summary>
/// Snapshot (Verify) goldens for the <c>[SyntoModel]</c> generator's emitted output. The pinned shape is the
/// sibling <c>sealed record</c> + its positional fields + the <c>From(TSymbol)</c> factory under
/// <c>#nullable enable</c>. The <c>From</c> body is parameter-bound: each getter RHS has its
/// <c>SyntoModel&lt;T&gt;.Symbol</c> references rebound to the <c>symbol</c> factory parameter (Task 6).
/// </summary>
public class ModelSnapshotTests
{
    private static Task VerifyModel(string source)
    {
        var compilation = ModelTestHarness.CreateCompilation(source);
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        var driver = CSharpGeneratorDriver
            .Create(new SyntoModelGenerator())
            .RunGenerators(compilation, TestContext.Current.CancellationToken);

        return Verify(driver).UseDirectory("snapshots");
    }

    [Fact]
    public Task TwoFieldRecord()
    {
        // A two-field spec (Name, Required): a positional `sealed record PropModel(string Name, bool Required)`
        // with a `From(IPropertySymbol symbol)` factory whose arguments are the getter RHS with `Symbol.*`
        // rebound to the `symbol` parameter (Task 6).
        return VerifyModel(
            """
            using Synto.Model;

            namespace Demo;

            [SyntoModel]
            class PropModelShape : SyntoModel<Microsoft.CodeAnalysis.IPropertySymbol>
            {
                string Name => Symbol.Name;
                bool Required => Symbol.IsRequired;
            }
            """);
    }
}

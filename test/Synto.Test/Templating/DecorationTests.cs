using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Synto.Test.Templating;

/// <summary>
/// Verifies that the six decoration marker types (post-quote declaration decorations feature) are
/// injected as <c>internal</c> into the consumer compilation by <see cref="SurfaceInjectionGenerator"/>.
/// </summary>
public class DecorationTests
{
    private static readonly MetadataReference CorlibReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
    private static readonly MetadataReference NetStandardReference = MetadataReference.CreateFromFile(Assembly.Load("netstandard, Version=2.0.0.0").Location);
    private static readonly MetadataReference SystemRuntimeReference = MetadataReference.CreateFromFile(Assembly.Load("System.Runtime, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location);

    [Fact]
    public void MarkerSurface_IsInjectedAsInternal()
    {
        var compilation = CSharpCompilation.Create("DecorationSurfaceTest",
            syntaxTrees: [],
            references:
            [
                CorlibReference,
                NetStandardReference,
                SystemRuntimeReference,
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver
            .Create(new SurfaceInjectionGenerator())
            .RunGenerators(compilation, TestContext.Current.CancellationToken);

        var result = driver.GetRunResult();
        var allGeneratedSource = string.Concat(result.GeneratedTrees.Select(t => t.ToString()));

        Assert.Contains("internal enum Access", allGeneratedSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class IdentifierAttribute", allGeneratedSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class VisibilityAttribute", allGeneratedSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class SealedAttribute", allGeneratedSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class ImplementsAttribute<TInterface>", allGeneratedSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class InheritsAttribute<TBase>", allGeneratedSource, StringComparison.Ordinal);
    }
}

using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Synto.Test;

/// <summary>
/// Proves the consumer-facing surface injected by <see cref="SurfaceInjectionGenerator"/> cannot
/// silently drift: it snapshots every injected (post-initialization) source. Because the surface is
/// embedded from <c>src\Synto</c> and rewritten to internal at injection time, any change to the
/// authored surface shows up here.
/// </summary>
public class SurfaceInjectionTest
{
    private static readonly MetadataReference CorlibReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
    private static readonly MetadataReference NetStandardReference = MetadataReference.CreateFromFile(Assembly.Load("netstandard, Version=2.0.0.0").Location);
    private static readonly MetadataReference SystemRuntimeReference = MetadataReference.CreateFromFile(Assembly.Load("System.Runtime, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location);

    [Fact]
    public Task VerifyInjectedSurface()
    {
        var compilation = CSharpCompilation.Create("Test",
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

        return Verify(driver).UseDirectory("snapshots");
    }
}

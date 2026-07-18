using System;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;

namespace Synto.Test.Model;

/// <summary>
/// End-to-end dog-food for the <c>[SyntoModel]</c> generator (spec §Verification 3): drive one real consumer
/// through the feature, compile the generated record + <c>From(TSymbol)</c> factory standalone, invoke it over
/// live <see cref="ISymbol"/> inputs, and prove the emitted record is <b>cache-safe by construction</b> — two
/// <c>From</c> calls over structurally-equal symbol inputs produce <c>.Equals</c>-equal records (with equal hash
/// codes), and a different input yields an unequal record. This is the runtime proof behind the compile-time
/// cacheability guards in <see cref="ModelCacheabilityTests"/>: the model carries only equatable value shape, so
/// equal inputs collapse to one cache key.
/// </summary>
public class ModelEndToEndTests
{
    // A plain-scalar spec: every getter projects the source symbol to a cache-safe value (string). The emitted
    // record + `From` are self-contained (symbol type is `global::`-qualified), so they compile with only the BCL
    // + Roslyn referenced — no injected Synto surface required.
    private const string Spec =
        """
        using Synto.Model;

        namespace Demo;

        [SyntoModel]
        class TypeModelShape : SyntoModel<Microsoft.CodeAnalysis.INamedTypeSymbol>
        {
            string Name => Symbol.Name;
            string Namespace => Symbol.ContainingNamespace.ToDisplayString();
        }
        """;

    [Fact]
    public void GeneratedRecord_FromEqualSymbols_IsStructurallyEqual_AndCacheSafe()
    {
        var runResult = ModelTestHarness.Run(Spec);
        Assert.Empty(runResult.Diagnostics);

        var assembly = ModelTestHarness.EmitGeneratedAssembly(runResult);
        var from = assembly.GetType("Demo.TypeModel", throwOnError: true)!
            .GetMethod("From", BindingFlags.Public | BindingFlags.Static)!;

        // Two distinct-but-equal INamedTypeSymbol instances (separate identical compilations) and one different
        // symbol. Equal projected values must yield equal records even though the symbol instances differ.
        var alphaA = TypeSymbol("namespace In; public class Alpha { public string P { get; set; } }", "In.Alpha");
        var alphaB = TypeSymbol("namespace In; public class Alpha { public string P { get; set; } }", "In.Alpha");
        var beta = TypeSymbol("namespace In; public class Beta { public string P { get; set; } }", "In.Beta");

        Assert.False(ReferenceEquals(alphaA, alphaB));

        var recordA = from.Invoke(null, new object[] { alphaA });
        var recordB = from.Invoke(null, new object[] { alphaB });
        var recordC = from.Invoke(null, new object[] { beta });

        // Equal symbol inputs -> structurally-equal records with equal hash codes: one incremental cache key.
        Assert.Equal(recordA, recordB);
        Assert.Equal(recordA!.GetHashCode(), recordB!.GetHashCode());

        // A different symbol input -> an unequal record.
        Assert.NotEqual(recordA, recordC);
    }

    private static INamedTypeSymbol TypeSymbol(string source, string metadataName)
    {
        var compilation = ModelTestHarness.CreateInputCompilation(source);
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        return compilation.GetTypeByMetadataName(metadataName)
            ?? throw new InvalidOperationException($"Type '{metadataName}' not found in the input compilation.");
    }
}

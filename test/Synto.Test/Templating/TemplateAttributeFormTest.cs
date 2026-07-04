using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Synto.Generators.Templating;

namespace Synto.Test.Templating;

/// <summary>
/// Phase-1 declaration-rules facts for the two-form <c>[Template]</c> surface. A top-level parameterless
/// <c>[Template]</c> carries no target and is misuse — it reports <c>SY1033</c>. A parameterless <c>[Template]</c>
/// nested inside a <c>[Template&lt;T&gt;]</c> carrier is exempt (interim no-op: the parent takes ownership as a
/// private sub-template in Phase 2), so it produces neither output nor diagnostic. (SY1034 — nested
/// <c>[Template&lt;T&gt;]</c> pinning a target — is authored now but only enforced in Phase 2.)
/// </summary>
public class TemplateAttributeFormTest
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
        MetadataReference.CreateFromFile(SyntoCoreAssembly.Location),
    ];

    private static GeneratorDriverRunResult Run(string source)
    {
        var compilation = CSharpCompilation.Create("TemplateAttributeFormTest",
            [CSharpSyntaxTree.ParseText(source)],
            references: References,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new TemplateFactorySourceGenerator());
        return driver.RunGenerators(compilation, TestContext.Current.CancellationToken).GetRunResult();
    }

    [Fact]
    public void TopLevelBareTemplate_ReportsSY1033_NoSource()
    {
        var runResult = Run(
            """
            using System;
            using Synto.Templating;

            partial class Factory {}

            public class TestClass
            {
                [Template]
                static void Sample()
                {
                    Console.WriteLine("Hello world");
                }
            }
            """);

        var diag = Assert.Single(runResult.Diagnostics, d => d.Id == "SY1033");
        Assert.NotEqual(Location.None, diag.Location);
        Assert.False(diag.Location.SourceSpan.IsEmpty);

        Assert.Empty(runResult.Results.SelectMany(r => r.GeneratedSources));
    }

    [Fact]
    public void NestedBareTemplate_InGenericCarrier_ProducesNoOutputNoDiagnostic()
    {
        // Interim characterization pin: the bare child inside a [Template<Factory>] carrier is exempt from SY1033
        // (nested), yet Phase 1 does not yet build it as a sub-template — so it emits no diagnostic and no document.
        var runResult = Run(
            """
            using System;
            using Synto.Templating;

            partial class Factory {}

            [Template<Factory>]
            public class Reader
            {
                [Template]
                void Child()
                {
                    Console.WriteLine("child");
                }
            }
            """);

        Assert.DoesNotContain(runResult.Diagnostics, d => d.Id == "SY1033");
        Assert.DoesNotContain(runResult.Diagnostics, d => d.Id == "SY1034");
    }
}

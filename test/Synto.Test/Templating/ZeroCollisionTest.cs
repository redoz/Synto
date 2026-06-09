using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Synto.Test.Templating;

/// <summary>
/// Proves the goal of the file-local-helper design: a consumer can run
/// <see cref="TemplateFactorySourceGenerator"/> AND reference the public <c>Synto.Core</c> runtime in the
/// default scope at the same time, with zero collisions.
/// </summary>
/// <remarks>
/// The emitted helpers (<c>ToSyntax</c> / <c>ToTypeSyntax</c>) are <c>file static class</c>es local to each
/// generated file, and generated files no longer carry <c>using Synto;</c>. So even though
/// <c>Synto.Core</c> declares the same helpers as public extension methods in <c>namespace Synto</c>, those
/// public copies are never in scope inside a generated file and can never produce the CS0121 ambiguity the
/// previous "injected internal copy" approach risked. This test compiles the post-generation output and
/// asserts there are no error diagnostics (in particular no CS0121).
/// </remarks>
public class ZeroCollisionTest
{
    private static readonly MetadataReference CorlibReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
    private static readonly MetadataReference NetStandardReference = MetadataReference.CreateFromFile(Assembly.Load("netstandard, Version=2.0.0.0").Location);
    private static readonly MetadataReference SystemRuntimeReference = MetadataReference.CreateFromFile(Assembly.Load("System.Runtime, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location);

    [Fact]
    public void GeneratedOutputCompilesAlongsidePublicSyntoCore()
    {
        // A template that forces BOTH emitted helpers into a single generated file:
        //   [Inline] T        -> typeof(T).ToTypeSyntax()
        //   [Inline] int value -> value.ToSyntax()
        const string source =
            """
            using System;
            using System.Collections.Generic;
            using Synto.Templating;

            partial class Factory {}

            public class TestClass {
               [Template(typeof(Factory))]
                void LocalFunction<[Inline] T>([Inline] int value) {
                    List<T> list = new();
                    Console.WriteLine($"Hello world {value}");
                }
            }
            """;

        var compilation = CSharpCompilation.Create("ZeroCollisionTest",
            [CSharpSyntaxTree.ParseText(source)],
            references:
            [
                CorlibReference,
                NetStandardReference,
                SystemRuntimeReference,
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                // The Roslyn assemblies the GENERATED factory body uses (MethodDeclaration, ExpressionSyntax,
                // SyntaxFactory, ...) — needed so the post-generation output actually compiles.
                MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.SyntaxNode).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.CSharp.SyntaxKind).Assembly.Location),
                // System.Linq / System.Collections for the consumer template body (List<T>, etc.).
                MetadataReference.CreateFromFile(Assembly.Load("System.Linq, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Collections, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Runtime.Extensions, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
                // The crucial part: reference the PUBLIC Synto.Core in the DEFAULT scope (no extern alias).
                // This is what a real consumer that also takes a runtime dependency on Synto.Core looks like.
                // Its public ToSyntax/ToTypeSyntax extension methods are now present in the compilation, so if
                // the generated files leaked `using Synto;` or injected an internal copy, every helper call
                // would be a CS0121 ambiguity.
                MetadataReference.CreateFromFile(SyntoCoreAssembly.Location),
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // Sanity: the input itself is valid before generation.
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        var driver = CSharpGeneratorDriver.Create(new TemplateFactorySourceGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics, TestContext.Current.CancellationToken);

        // The generator must not have reported any errors.
        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // The generator must actually have produced the factory (otherwise the test proves nothing).
        Assert.True(output.SyntaxTrees.Count() > compilation.SyntaxTrees.Count(),
            "Expected the generator to add at least one generated syntax tree.");

        // Compile the POST-GENERATION output and assert there are no ERROR diagnostics. A CS0436 *warning*
        // on the markers is expected and acceptable (this synthetic compilation references Synto.Core's
        // public markers AND gets the injected internal markers); we assert only on errors — in particular
        // there must be no CS0121 ambiguous-call error on the emitted .ToSyntax()/.ToTypeSyntax() calls.
        var errors = output.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.DoesNotContain(errors, d => d.Id == "CS0121");
        Assert.True(errors.Count == 0,
            "Post-generation compilation reported errors: " + string.Join("; ", errors.Select(d => d.Id + " " + d.GetMessage())));
    }
}
